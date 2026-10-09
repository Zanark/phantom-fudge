"""Compile explicitly reviewed synthetic benchmark C#; never calls a model.

The operator must inspect the code first. This is not an automatic execution
path for arbitrary answers, and the production provider never executes code.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import uuid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--reviewed-csharp", action="store_true", required=True)
    parser.add_argument("--receipt", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    receipt = json.loads(Path(args.receipt).read_text(encoding="utf-8"))
    root = Path(__file__).resolve().parents[2] / ".local" / ("answer-code-" + uuid.uuid4().hex)
    classes, checks, hashes = [], [], []
    for trial in receipt["trials"]:
        blocks = re.findall(r"```csharp\s*\n(.*?)```", trial["answer"], re.S | re.I)
        if len(blocks) != 1:
            raise SystemExit("Every reviewed trial must contain exactly one C# block.")
        source = blocks[0]
        original_hash = hashlib.sha256(source.encode()).hexdigest()
        renamed, count = re.subn(r"\bpublic\s+(static\s+)?class\s+\w+",
                                lambda match: "public " + (match.group(1) or "") + f"class Answer{trial['trial']}",
                                source, count=1)
        if count != 1:
            raise SystemExit("Unsupported reviewed class shape.")
        renamed = re.sub(r"^using .*?;\s*", "", renamed, flags=re.M)
        classes.append(renamed)
        checks.append(f"Check(typeof(Answer{trial['trial']}), {trial['trial']});")
        hashes.append({"trial": trial["trial"], "sourceSha256": original_hash})
    source = r"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
class Program
{
    private static readonly List<object> Results = new();
    static void Main()
    {
        CHECKS
        Console.WriteLine(JsonSerializer.Serialize(Results));
    }
    static void Check(Type type, int trial)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(candidate => candidate.ReturnType == typeof(int[]) && candidate.GetParameters().Select(p => p.ParameterType)
                .SequenceEqual(new[] { typeof(int[]), typeof(int) })).ToArray();
        if (methods.Length != 1)
        {
            Results.Add(new {trial, name = "expected-two-sum-contract", passed = false});
            return;
        }
        var method = methods[0];
        var instance = method.IsStatic ? null : Activator.CreateInstance(type);
        var cases = new (string Name, int[] Values, int Target)[]
        {
            ("basic", new[] {2,7,11,15}, 9),
            ("duplicates", new[] {3,3}, 6),
            ("indices", new[] {3,2,4}, 6),
            ("negative", new[] {-3,4,3,90}, 0),
            ("zeros", new[] {0,4,3,0}, 0),
            ("max-overflow-false-pair", new[] {int.MaxValue,int.MaxValue,0,-2}, -2),
            ("min-overflow-false-pair", new[] {int.MinValue,int.MinValue,0,0}, 0),
            ("opposite-extremes", new[] {int.MaxValue,int.MinValue,1}, -1),
            ("min-target", new[] {int.MinValue,1,0}, int.MinValue),
            ("max-target", new[] {int.MaxValue,0,-1}, int.MaxValue)
        };
        foreach (var test in cases)
        {
            var passed = false;
            try
            {
                var answer = (int[]?)method.Invoke(instance, new object[] {test.Values, test.Target});
                passed = answer is { Length: 2 } && answer[0] != answer[1] &&
                    answer.All(index => index >= 0 && index < test.Values.Length) &&
                    (long)test.Values[answer[0]] + test.Values[answer[1]] == test.Target;
            }
            catch (TargetInvocationException) {}
            Results.Add(new {trial, name = test.Name, passed});
        }
        var noPair = false;
        try
        {
            var answer = (int[]?)method.Invoke(instance, new object[] {new[] {1,2}, 100});
            noPair = answer is { Length: 0 };
        }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { noPair = true; }
        Results.Add(new {trial, name = "no-pair", passed = noPair});
    }
}
""".replace("CHECKS", "\n".join(checks))
    root.mkdir(parents=True)
    (root / "Program.cs").write_text(source + "\n" + "\n".join(classes), encoding="utf-8")
    (root / "AnswerCode.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', encoding="utf-8")
    result = {"reviewedSource": hashes, "compiled": False, "checks": [], "failure": None}
    try:
        run = subprocess.run(["dotnet", "run", "--project", str(root / "AnswerCode.csproj"),
                              "-c", "Release", "--verbosity", "quiet"], capture_output=True,
                             text=True, encoding="utf-8", timeout=90)
        if run.returncode != 0:
            result["failure"] = "build_or_execution_failed"
        else:
            result["compiled"] = True
            result["checks"] = json.loads(run.stdout.strip().splitlines()[-1])
    except subprocess.TimeoutExpired:
        result["failure"] = "validation_timeout"
    finally:
        shutil.rmtree(root)
    result["passed"] = sum(check["passed"] for check in result["checks"])
    result["failed"] = len(result["checks"]) - result["passed"]
    Path(args.output).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in result.items() if key not in ("checks", "reviewedSource")}))
    return 0 if result["compiled"] and result["failed"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
