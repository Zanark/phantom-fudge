"""Fail-closed PE closure and loaded-DLL checks for the embedded Windows runtime."""

import hashlib
from pathlib import Path
import re
import struct


# Only reviewed Windows 11 inbox dependencies belong here. A file's presence in
# the build host's System32 is not evidence that it ships with Windows.
WINDOWS_IMPORTS = frozenset("""
advapi32.dll bcrypt.dll bcryptprimitives.dll crypt32.dll dbghelp.dll dxgi.dll
iphlpapi.dll kernel32.dll msvcrt.dll ntdll.dll ole32.dll oleaut32.dll propsys.dll
rpcrt4.dll setupapi.dll shell32.dll user32.dll version.dll winmm.dll ws2_32.dll
api-ms-win-core-path-l1-1-0.dll api-ms-win-core-synch-l1-2-0.dll
api-ms-win-core-winrt-error-l1-1-0.dll api-ms-win-core-winrt-l1-1-0.dll
api-ms-win-core-winrt-string-l1-1-0.dll
api-ms-win-crt-conio-l1-1-0.dll api-ms-win-crt-convert-l1-1-0.dll
api-ms-win-crt-environment-l1-1-0.dll api-ms-win-crt-filesystem-l1-1-0.dll
api-ms-win-crt-heap-l1-1-0.dll api-ms-win-crt-locale-l1-1-0.dll
api-ms-win-crt-math-l1-1-0.dll api-ms-win-crt-private-l1-1-0.dll
api-ms-win-crt-process-l1-1-0.dll api-ms-win-crt-runtime-l1-1-0.dll
api-ms-win-crt-stdio-l1-1-0.dll api-ms-win-crt-string-l1-1-0.dll
api-ms-win-crt-time-l1-1-0.dll api-ms-win-crt-utility-l1-1-0.dll
""".split())

REQUIRED_CRT = frozenset({
    "msvcp140.dll", "msvcp140_1.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcomp140.dll",
})


def pe_dependencies(path):
    """Read ordinary imports, delay imports and exported forwarders, without loading code."""
    data = path.read_bytes()

    def unpack(fmt, offset):
        if offset < 0 or offset + struct.calcsize(fmt) > len(data):
            raise ValueError(f"Truncated PE: {path.name}")
        return struct.unpack_from(fmt, data, offset)

    if data[:2] != b"MZ":
        raise ValueError(f"Not a PE: {path.name}")
    header, = unpack("<I", 60)
    if data[header:header + 4] != b"PE\0\0":
        raise ValueError(f"Invalid PE signature: {path.name}")
    machine, sections = unpack("<HH", header + 4)
    optional_size, = unpack("<H", header + 20)
    optional = header + 24
    magic, = unpack("<H", optional)
    if magic not in (0x10B, 0x20B):
        raise ValueError(f"Unsupported PE format: {path.name}")
    directories = optional + (96 if magic == 0x10B else 112)
    directory_count, = unpack("<I", directories - 4)
    image_base, = unpack("<I" if magic == 0x10B else "<Q", optional + (28 if magic == 0x10B else 24))
    header_size, = unpack("<I", optional + 60)
    ranges = []
    for index in range(sections):
        start = optional + optional_size + 40 * index
        virtual_size, address, raw_size, raw = unpack("<IIII", start + 8)
        ranges.append((address, raw_size, raw))

    def offset(rva, size=1):
        if 0 <= rva < header_size and rva + size <= min(header_size, len(data)):
            return rva
        for address, raw_size, raw in ranges:
            if address <= rva and rva + size <= address + raw_size:
                result = raw + rva - address
                if result + size <= len(data):
                    return result
        raise ValueError(f"Unmapped PE address in {path.name}")

    def text(rva):
        start = offset(rva)
        end = data.find(b"\0", start, min(start + 4096, len(data)))
        if end < 0:
            raise ValueError(f"Unterminated PE string in {path.name}")
        try:
            return data[start:end].decode("ascii")
        except UnicodeError as error:
            raise ValueError(f"Invalid PE import string in {path.name}") from error

    def directory(index):
        if index >= directory_count:
            return 0, 0
        if directories + 8 * (index + 1) > optional + optional_size:
            raise ValueError(f"Invalid PE directory table in {path.name}")
        return unpack("<II", directories + 8 * index)

    result = {"machine": hex(machine), "imports": [], "delayImports": [], "forwarders": []}
    for index, width, key in ((1, 20, "imports"), (13, 32, "delayImports")):
        address, size = directory(index)
        if not address:
            continue
        if size < width or size > 10_000_000:
            raise ValueError(f"Invalid PE import table in {path.name}")
        for position in range(0, size - width + 1, width):
            values = unpack("<" + "I" * (width // 4), offset(address + position, width))
            if not any(values):
                break
            name = values[3] if index == 1 else values[1]
            if index == 13 and not values[0] & 1:
                name -= image_base
            result[key].append(text(name).lower())
        else:
            raise ValueError(f"Unterminated PE import table in {path.name}")
    address, size = directory(0)
    if address:
        count, = unpack("<I", offset(address + 20, 4))
        functions, = unpack("<I", offset(address + 28, 4))
        if count > 1_000_000:
            raise ValueError(f"Invalid PE export table in {path.name}")
        for index in range(count):
            target, = unpack("<I", offset(functions + index * 4, 4))
            if address <= target < address + size:
                name, separator, _ = text(target).rpartition(".")
                if not separator:
                    raise ValueError(f"Invalid PE export forwarder in {path.name}")
                result["forwarders"].append(name.lower() if name.lower().endswith(".dll") else name.lower() + ".dll")
    for key in ("imports", "delayImports", "forwarders"):
        result[key] = sorted(set(result[key]))
        if any(not re.fullmatch(r"[a-z0-9_+.-]+\.dll", name) for name in result[key]):
            raise ValueError(f"Invalid dependency name in {path.name}")
    return result


def audit_pe_closure(runtime):
    runtime = Path(runtime).resolve()
    files = sorted(path for path in runtime.rglob("*") if path.suffix.lower() in (".dll", ".pyd", ".exe"))
    if not files:
        raise ValueError("No native runtime files found.")
    records = {}
    inbox = set()
    for path in files:
        relative = path.relative_to(runtime).as_posix()
        record = pe_dependencies(path)
        if record["machine"] != "0x8664" and not (
                relative.startswith("Lib/site-packages/setuptools/") and path.suffix.lower() == ".exe"):
            raise ValueError(f"Non-x64 executable runtime dependency: {relative}")
        resolutions = {}
        for name in sorted(set(record["imports"] + record["delayImports"] + record["forwarders"])):
            if name in WINDOWS_IMPORTS:
                resolutions[name] = "Windows 11 inbox"
                inbox.add(name)
                continue
            # NumPy's wheel loader registers numpy.libs. Other package DLLs are
            # resolved adjacent to their importing library or the Python executable.
            choices = [path.parent / name, runtime / name]
            if relative.startswith(("Lib/site-packages/numpy/", "Lib/site-packages/numpy.libs/")):
                choices.append(runtime / "Lib" / "site-packages" / "numpy.libs" / name)
            selected = next((candidate for candidate in choices if candidate.is_file()), None)
            if selected is None:
                raise ValueError(f"Unbundled non-inbox PE dependency: {relative} -> {name}")
            resolutions[name] = selected.relative_to(runtime).as_posix()
        record["resolved"] = resolutions
        record["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        records[relative] = record
    missing = sorted(name for name in REQUIRED_CRT if not (runtime / name).is_file())
    if missing:
        raise ValueError(f"Missing app-local VC runtime: {', '.join(missing)}")
    return {
        "passed": True, "nativeFiles": len(files), "files": records,
        "windowsInboxImports": sorted(inbox),
        "scope": "All shipped PE normal/delay imports and export-forwarder DLL names; not a clean-VM test.",
        "system32UsedToResolveNonInboxDependencies": False,
        "setuptoolsLauncherTemplates": "Unused cross-platform executable templates are inventoried, not invoked.",
    }


DLL_PROBE = r'''
import ctypes as _dll_ctypes, os as _dll_os, pathlib as _dll_pathlib, re as _dll_re, sys as _dll_sys
_dll_root = _dll_pathlib.Path(_dll_sys.executable).resolve().parent
_dll_system = _dll_pathlib.Path(_dll_os.environ["SystemRoot"]).resolve()
_dll_defender = _dll_pathlib.Path(_dll_os.environ.get(
    "ProgramData", str(_dll_pathlib.Path(_dll_system.anchor) / "ProgramData"))
    ) / "Microsoft" / "Windows Defender" / "Platform"
_dll_kernel = _dll_ctypes.WinDLL("kernel32", use_last_error=True)
_dll_kernel.SetDefaultDllDirectories.argtypes = [_dll_ctypes.c_uint32]
_dll_kernel.SetDefaultDllDirectories.restype = _dll_ctypes.c_int
if not _dll_kernel.SetDefaultDllDirectories(0x1000):
    raise _dll_ctypes.WinError(_dll_ctypes.get_last_error())
_dll_kernel.GetCurrentProcess.restype = _dll_ctypes.c_void_p
_dll_kernel.K32EnumProcessModules.argtypes = [
    _dll_ctypes.c_void_p, _dll_ctypes.POINTER(_dll_ctypes.c_void_p),
    _dll_ctypes.c_uint32, _dll_ctypes.POINTER(_dll_ctypes.c_uint32)]
_dll_kernel.K32EnumProcessModules.restype = _dll_ctypes.c_int
_dll_kernel.K32GetModuleFileNameExW.argtypes = [
    _dll_ctypes.c_void_p, _dll_ctypes.c_void_p, _dll_ctypes.c_wchar_p, _dll_ctypes.c_uint32]
_dll_kernel.K32GetModuleFileNameExW.restype = _dll_ctypes.c_uint32

def _dll_snapshot():
    handles = (_dll_ctypes.c_void_p * 2048)()
    needed = _dll_ctypes.c_uint32()
    process = _dll_kernel.GetCurrentProcess()
    if not _dll_kernel.K32EnumProcessModules(process, handles, _dll_ctypes.sizeof(handles),
                                            _dll_ctypes.byref(needed)):
        raise _dll_ctypes.WinError(_dll_ctypes.get_last_error())
    assert needed.value <= _dll_ctypes.sizeof(handles), "Module enumeration overflow"
    bundled, system, defender = {}, [], []
    for handle in handles[:needed.value // _dll_ctypes.sizeof(_dll_ctypes.c_void_p)]:
        buffer = _dll_ctypes.create_unicode_buffer(32768)
        length = _dll_kernel.K32GetModuleFileNameExW(process, handle, buffer, len(buffer))
        assert 0 < length < len(buffer), "Cannot identify loaded module"
        path = _dll_pathlib.Path(buffer.value).resolve()
        name = path.name.lower()
        if path.is_relative_to(_dll_root):
            bundled[name] = path.relative_to(_dll_root).as_posix()
        elif path.is_relative_to(_dll_defender) and _dll_re.fullmatch(
                r"[0-9.-]+/MpOAV\.dll", path.relative_to(_dll_defender).as_posix(), _dll_re.IGNORECASE):
            # Windows Defender's OS antivirus provider is injected by Windows,
            # not a runtime dependency and never copied into the installer.
            defender.append(path.relative_to(_dll_defender).as_posix())
        else:
            assert not _dll_re.match(r"(?:msvcp|vcruntime|vcomp|concrt|vccorlib)\d", name), (
                "VC runtime loaded outside payload", str(path))
            assert path.is_relative_to(_dll_system / "System32") or path.is_relative_to(
                _dll_system / "WinSxS"), ("Non-Windows DLL loaded outside payload", str(path))
            system.append(path.relative_to(_dll_system).as_posix())
    return {"bundled": bundled, "windows": sorted(system), "windowsDefenderInjection": sorted(defender)}

_dll_before = _dll_snapshot()

def _dll_evidence():
    evidence = _dll_snapshot()
    required = {"msvcp140.dll", "msvcp140_1.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcomp140.dll"}
    assert required.issubset(evidence["bundled"]), ("Expected app-local CRT was not loaded", evidence)
    for name in required:
        assert evidence["bundled"][name] == name, ("CRT not beside python.exe", name)
    evidence.update({
        "passed": True, "searchPolicy": "LOAD_LIBRARY_SEARCH_DEFAULT_DIRS; no PATH/current-directory lookup",
        "vcRuntimeOutsidePayload": False, "cleanWindowsVmTested": False,
        "scope": "Loaded-module paths after exercised imports/inference; complements static PE closure."
    })
    return evidence
'''
