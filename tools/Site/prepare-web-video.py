"""Locally derive and validate the complete, self-contained website tutorial.

Requires the already installed PyAV, Pillow and imageio-ffmpeg packages.
Example: python tools/Site/prepare-web-video.py --samples --crf 25
Inspect sample pixels before running --encode --validate with the same CRF.
The immutable source is never overwritten. AAC packets are copied, not encoded.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess

import av
import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "artifacts" / "tutorial" / "20261010-221225" / "Phantom-Fudge-Full-Tutorial.mp4"
SOURCE_HASH = "388ee914e1ffdc0ac3911d2a2e8dc6c8f33d8aad047f1aeff02fbe41138e1fde"
OUTPUT = ROOT / "site" / "media" / "Phantom-Fudge-Full-Tutorial.mp4"
WORK = ROOT / ".local" / "site-art" / "web-video"
SAMPLES = [0, 366, 599, 990, 1400, 1750, 2040, 2285]
REVIEW_TIMES = [0, 18.5, 59.5, 204.967, 367.967, 507.5, 599.733, 977.2,
                990, 1051.233, 1130, 1280, 1400, 1571, 1750, 2041.433, 2297.7]


def digest(path):
    value = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def run(arguments, **kwargs):
    return subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-nostdin",
                           *arguments], check=True, **kwargs)


def encode_args(output, crf, start=None, duration=None):
    args = ["-loglevel", "error", "-y"]
    if start is not None:
        args += ["-ss", str(start)]
    args += ["-i", str(SOURCE)]
    if duration is not None:
        args += ["-t", str(duration)]
    args += ["-map", "0:v:0", "-map", "0:a:0", "-map_metadata", "0",
             "-map_chapters", "0", "-c:v", "libx264", "-preset", "medium",
             "-crf", str(crf), "-threads", "4", "-pix_fmt", "yuv420p",
             "-fps_mode", "passthrough", "-c:a", "copy", "-movflags", "+faststart",
             str(output)]
    return args


def frame_at(path, seconds):
    with av.open(str(path)) as container:
        video = container.streams.video[0]
        container.seek(int(seconds / video.time_base), stream=video, backward=True)
        for frame in container.decode(video):
            if float(frame.time) >= seconds - 1 / 60:
                return frame.to_image().convert("RGB")
    raise RuntimeError(f"Missing frame at {seconds}")


def samples(crf):
    records = []
    for start in SAMPLES:
        output = WORK / f"sample-{start}-crf{crf}.mp4"
        run(encode_args(output, crf, start, 10))
        sample = frame_at(output, 2)
        sample.save(WORK / f"sample-{start}-crf{crf}.jpg", quality=96)
        original = frame_at(SOURCE, start + 2)
        original.save(WORK / f"sample-{start}-source.jpg", quality=96)
        records.append({"start": start, "seconds": 10, "bytes": output.stat().st_size,
                        "sha256": digest(output)})
        print(f"sample {start}: {output.stat().st_size:,} bytes", flush=True)
    total = sum(record["bytes"] for record in records)
    (WORK / f"samples-crf{crf}.json").write_text(json.dumps({
        "crf": crf, "samples": records,
        "rough_full_bytes_from_samples": round(total / (len(records) * 10) * 2297.767),
        "note": "Unweighted samples are a quality check and rough size estimate, not a guarantee."
    }, indent=2), encoding="utf-8")


def audio_packets(path):
    payload, timeline = hashlib.sha256(), hashlib.sha256()
    count = 0
    with av.open(str(path)) as container:
        audio = container.streams.audio[0]
        for packet in container.demux(audio):
            if not packet.size:
                continue
            payload.update(bytes(packet))
            timeline.update(f"{packet.pts}|{packet.dts}|{packet.duration}|{packet.time_base}\n".encode())
            count += 1
    return {"packets": count, "payload_sha256": payload.hexdigest(),
            "timestamp_sha256": timeline.hexdigest()}


def media_info(path):
    with av.open(str(path)) as container:
        video, audio = container.streams.video[0], container.streams.audio[0]
        chapters = [{"start": item["start"], "end": item["end"],
                     "time_base": str(item["time_base"]), "metadata": item["metadata"]}
                    for item in container.chapters()]
        return {"bytes": path.stat().st_size, "sha256": digest(path),
                "duration_seconds": container.duration / av.time_base,
                "video_duration_seconds": float(video.duration * video.time_base),
                "width": video.width, "height": video.height, "fps": float(video.average_rate),
                "frames": video.frames, "pixel_format": video.codec_context.format.name,
                "video_codec": video.codec_context.name, "audio_codec": audio.codec_context.name,
                "audio_sample_rate": audio.codec_context.sample_rate,
                "video_start": video.start_time, "chapters": chapters}


def boxes(path):
    result = []
    with path.open("rb") as stream:
        while header := stream.read(8):
            length, kind = struct.unpack(">I4s", header)
            extra = 0
            if length == 1:
                length = struct.unpack(">Q", stream.read(8))[0]
                extra = 8
            result.append(kind.decode("ascii"))
            if not length:
                break
            stream.seek(length - 8 - extra, 1)
    return result


def review_frames():
    contact = Image.new("RGB", (1280, ((len(REVIEW_TIMES) + 1) // 2) * 390), "#000F13")
    draw = ImageDraw.Draw(contact)
    font = ImageFont.truetype("segoeui.ttf", 19)
    for index, seconds in enumerate(REVIEW_TIMES):
        image = frame_at(OUTPUT, seconds)
        image.save(WORK / f"web-{seconds:.3f}.jpg", quality=96)
        image.thumbnail((640, 360))
        x, y = index % 2 * 640, index // 2 * 390
        contact.paste(image, (x, y))
        draw.text((x + 8, y + 361), f"Web copy / {seconds:.3f}s", font=font, fill="#EEE8D5")
    contact.save(WORK / "web-contact.jpg", quality=94)


def validate(crf):
    source, output = media_info(SOURCE), media_info(OUTPUT)
    assert output["bytes"] < 100 * 1024 * 1024, "Exceeds GitHub's individual file limit."
    for key in ["duration_seconds", "video_duration_seconds", "width", "height", "fps",
                "frames", "audio_codec", "audio_sample_rate", "video_start", "chapters"]:
        assert output[key] == source[key], f"Timeline or format changed: {key}"
    assert output["frames"] == 68933 and output["pixel_format"] == "yuv420p"
    source_audio, web_audio = audio_packets(SOURCE), audio_packets(OUTPUT)
    assert source_audio == web_audio, "AAC packet payload or timeline differs."
    top_boxes = boxes(OUTPUT)
    assert top_boxes.index("moov") < top_boxes.index("mdat"), "Missing faststart."
    result = run(["-loglevel", "error", "-xerror", "-i", str(OUTPUT),
                  "-map", "0:v:0", "-map", "0:a:0", "-progress", "pipe:1",
                  "-f", "null", "-"], capture_output=True, text=True)
    (WORK / "full-decode.log").write_text(result.stdout + result.stderr, encoding="utf-8")
    assert "frame=68933" in result.stdout and "progress=end" in result.stdout
    review_frames()
    args = encode_args(OUTPUT, crf)
    portable = [str(value).replace(str(ROOT) + "\\", "").replace("\\", "/") for value in args]
    record = {"file": "site/media/Phantom-Fudge-Full-Tutorial.mp4", **output,
              "source_file": "artifacts/tutorial/20261010-221225/Phantom-Fudge-Full-Tutorial.mp4",
              "source_sha256": SOURCE_HASH, "source_bytes": source["bytes"],
              "ffmpeg_arguments": portable, "audio_verification": web_audio,
              "full_decode": {"completed": True, "video_frames": 68933, "errors": 0},
              "faststart": True, "target_90_mib_met": output["bytes"] <= 90 * 1024 * 1024,
              "preservation": "Complete original timeline, frame count, chapters and AAC packet payloads/timestamps. Burned captions remain in the full uncropped video frame. No cuts, retiming, scaling, added music or remote hosting.",
              "tradeoff": "Lossy H.264 recompression at 1080p30; original AAC stream copied exactly.",
              "sampled_visual_review_seconds": REVIEW_TIMES,
              "visual_review": "Pending separate inspection of decoded output pixels; encoding and full decode do not constitute visual review."}
    (WORK / "web-provenance.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
    print(json.dumps({key: output[key] for key in ["bytes", "sha256", "duration_seconds",
                                                  "frames", "width", "height", "fps"]}), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--samples", action="store_true")
    parser.add_argument("--encode", action="store_true")
    parser.add_argument("--validate", action="store_true")
    parser.add_argument("--crf", type=int, default=25)
    args = parser.parse_args()
    if not any([args.samples, args.encode, args.validate]):
        parser.error("Choose --samples, --encode or --validate.")
    if digest(SOURCE) != SOURCE_HASH:
        raise RuntimeError("Immutable source hash mismatch.")
    WORK.mkdir(parents=True, exist_ok=True)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    if args.samples:
        samples(args.crf)
    if args.encode:
        run(encode_args(OUTPUT, args.crf))
    if args.validate:
        validate(args.crf)
    if digest(SOURCE) != SOURCE_HASH:
        raise RuntimeError("Immutable source changed.")


if __name__ == "__main__":
    main()
