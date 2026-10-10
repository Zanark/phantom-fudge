"""Reproduce silent website excerpts from the immutable, sample-data tutorial.

Requires existing Pillow, PyAV and imageio-ffmpeg installations. No downloads.
Run from the repository root with the Python environment containing those modules.
"""
from __future__ import annotations

import argparse
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import subprocess

import av
import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "artifacts" / "tutorial" / "20261010-221225" / "Phantom-Fudge-Full-Tutorial.mp4"
SOURCE_HASH = "388ee914e1ffdc0ac3911d2a2e8dc6c8f33d8aad047f1aeff02fbe41138e1fde"
ASSETS = ROOT / "site" / "assets"
WORK = ROOT / ".local" / "site-art"
WIDTH, HEIGHT, FPS = 1000, 720, 30
PANES = (0, 68, 1000, 668)
DEMOS = {
    "demo-selection": {
        "alt": "Scripted sample: select a second transcript row, later explicitly Ask, then read the completed sample answer.",
        "footer": "Edited excerpts · sample data · no model latency or quality claim",
        "poster_seconds": 2.5,
        "segments": [
            {"scene": "select-second", "start_frame": 17971, "end_frame": 18073, "action_frame": 17992,
             "crop_xywh": [64, 183, 1334, 685], "label": "01 / Select the useful transcript lines"},
            {"scene": "answer-ask", "start_frame": 29295, "end_frame": 29382, "action_frame": 29316,
             "crop_xywh": [44, 340, 1442, 560], "label": "02 / Later: review the question, then Ask"},
            {"scene": "answer-read", "start_frame": 29700, "end_frame": 29811,
             "crop_xywh": [78, 219, 1120, 632], "label": "03 / Later: read the completed sample answer"},
        ],
        "review_seconds": [0, .6, .8, 2.9, 3.3, 3.4, 4, 4.2, 6.2, 6.3, 8, 9.9],
        "edit_note": "Three explicitly labelled later excerpts. Intervening draft editing, setup, waiting and most streaming are omitted. Not one continuous request or a latency measurement.",
    },
    "demo-context": {
        "alt": "Scripted sample: click Previous answer immediately left of Ask; it changes from OFF to ON without sending.",
        "footer": "Default off · one completed answer · explicit Ask still required",
        "poster_seconds": 3,
        "segments": [
            {"scene": "context-on", "start_frame": 31515, "end_frame": 31710, "action_frame": 31537,
             "crop_xywh": [44, 342, 1442, 558], "label": "Previous answer / opt in for your next Ask"},
        ],
        "review_seconds": [0, .5, .8, 1, 2.2, 6.4],
        "edit_note": "One continuous unaccelerated excerpt showing the toggle and a reading hold. The toggle does not send. This feature is in current source, not v0.2.0-preview.1.",
    },
    "demo-cover": {
        "alt": "Scripted sample: click the top-left book icon to show NOTEPAD IS READY, then later click the same icon to reveal the workspace.",
        "footer": "Visual cover only · background work continues · not universal invisibility",
        "poster_seconds": 2.5,
        "segments": [
            {"scene": "cover-click", "start_frame": 61220, "end_frame": 61328, "action_frame": 61243,
             "crop_xywh": [146, 128, 1238, 794], "label": "01 / Cover the workspace"},
            {"scene": "cover-restore", "start_frame": 61925, "end_frame": 62057, "action_frame": 61947,
             "crop_xywh": [146, 128, 1238, 794], "label": "02 / Later: reveal with the same icon"},
        ],
        "review_seconds": [0, .6, .8, 1, 3.5, 3.6, 4.2, 4.4, 7.9],
        "edit_note": "The intervening explanatory cover hold is omitted. Both clicks and their resulting states are retained. The entire cover and top-left control remain in frame.",
    },
}


def font(size, bold=False):
    return ImageFont.truetype("seguisb.ttf" if bold else "segoeui.ttf", size)


def sha256(path):
    result = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def frame_at(path, seconds):
    with av.open(str(path)) as container:
        video = container.streams.video[0]
        container.seek(int(seconds / video.time_base), stream=video, backward=True)
        for frame in container.decode(video):
            if float(frame.time) >= seconds - 1 / 60:
                return frame.to_image().convert("RGB")
    raise RuntimeError(f"No video frame at {seconds:.3f}s")


def inspect_source():
    WORK.mkdir(parents=True, exist_ok=True)
    times = [988.8, 990.3, 992.0, 995.0, 977.0, 979.0,
             1049.9, 1051.1, 1051.5, 2040.2, 2041.7, 2065.3]
    font = ImageFont.truetype("segoeui.ttf", 21)
    contact = Image.new("RGB", (1440, 4 * 442), "#000F13")
    draw = ImageDraw.Draw(contact)
    for index, seconds in enumerate(times):
        image = frame_at(SOURCE, seconds)
        image.save(WORK / f"source-{seconds:.1f}.jpg", quality=94)
        image.thumbnail((720, 405))
        x, y = (index % 2) * 720, (index // 2) * 442
        if y + 442 > contact.height:
            expanded = Image.new("RGB", (1440, y + 442), "#000F13")
            expanded.paste(contact, (0, 0))
            contact = expanded
            draw = ImageDraw.Draw(contact)
        contact.paste(image, (x, y))
        draw.text((x + 12, y + 410), f"Source {seconds:.3f}s", font=font, fill="#EEE8D5")
    contact.save(WORK / "source-contact.jpg", quality=92)


def source_frames(start, end):
    with av.open(str(SOURCE)) as container:
        video = container.streams.video[0]
        container.seek(int((start / FPS) / video.time_base), stream=video, backward=True)
        count = 0
        for frame in container.decode(video):
            index = round(float(frame.time) * FPS)
            if index < start:
                continue
            if index >= end:
                break
            if index != start + count:
                raise RuntimeError("Unexpected tutorial frame timestamp sequence.")
            count += 1
            yield frame.to_image().convert("RGB")
        if count != end - start:
            raise RuntimeError(f"Expected {end - start} source frames, decoded {count}.")


def compose(frame, segment, demo):
    x, y, width, height = segment["crop_xywh"]
    retained = frame.crop((x, y, x + width, y + height))
    retained.thumbnail((PANES[2], PANES[3] - PANES[1]), Image.Resampling.LANCZOS)
    image = Image.new("RGB", (WIDTH, HEIGHT), "#000F13")
    image.paste(retained, ((WIDTH - retained.width) // 2,
                          PANES[1] + (PANES[3] - PANES[1] - retained.height) // 2))
    draw = ImageDraw.Draw(image)
    draw.text((22, 11), segment["label"], font=font(24, True), fill="#EEE8D5")
    draw.text((23, 42), "PHANTOM FUDGE  /  SCRIPTED APP DEMO · SAMPLE DATA", font=font(12), fill="#93A1A1")
    draw.line((22, 65, 978, 65), fill="#00A591", width=1)
    draw.text((22, 685), demo["footer"], font=font(19), fill="#EEE8D5")
    return image


def encode(name, demo):
    output = ASSETS / f"{name}.mp4"
    frames_written = 0
    with av.open(str(output), "w", options={"movflags": "+faststart"}) as container:
        stream = container.add_stream("libx264", rate=FPS)
        stream.width, stream.height, stream.pix_fmt = WIDTH, HEIGHT, "yuv420p"
        stream.options = {"crf": "21", "preset": "medium", "threads": "2"}
        for segment in demo["segments"]:
            for source in source_frames(segment["start_frame"], segment["end_frame"]):
                image = compose(source, segment, demo)
                frame = av.VideoFrame.from_image(image)
                frame.pts, frame.time_base = frames_written, Fraction(1, FPS)
                for packet in stream.encode(frame):
                    container.mux(packet)
                frames_written += 1
        for packet in stream.encode():
            container.mux(packet)
    expected = sum(s["end_frame"] - s["start_frame"] for s in demo["segments"])
    if expected != frames_written:
        raise RuntimeError("Encoded duration differs from edit map.")
    gif = ASSETS / f"{name}.gif"
    subprocess.run([
        imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y",
        "-i", str(output), "-filter_complex",
        "[0:v]fps=10,split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];"
        "[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
        "-an", "-loop", "0", "-gifflags", "+offsetting+transdiff", str(gif),
    ], check=True)
    if gif.stat().st_size > 5_000_000:
        raise RuntimeError(f"{name}: GIF exceeds the hard 5 MB budget.")
    frame_at(output, demo["poster_seconds"]).save(ASSETS / f"{name}.jpg", quality=94)
    print(f"{name}: {frames_written / FPS:.3f}s; GIF {gif.stat().st_size:,} bytes; MP4 {output.stat().st_size:,} bytes")


def gif_frame_at(path, seconds):
    with Image.open(path) as image:
        at_ms, target_ms = 0, round(seconds * 1000)
        for index in range(image.n_frames):
            image.seek(index)
            duration_ms = image.info.get("duration", 100)
            if at_ms + duration_ms > target_ms:
                return image.convert("RGB")
            at_ms += duration_ms
    raise RuntimeError(f"No GIF frame at {seconds:.2f}s")


def review_outputs():
    WORK.mkdir(parents=True, exist_ok=True)
    for name, demo in DEMOS.items():
        samples = demo["review_seconds"]
        contact = Image.new("RGB", (1000, ((len(samples) + 1) // 2) * 388), "#000F13")
        draw = ImageDraw.Draw(contact)
        for index, seconds in enumerate(samples):
            image = gif_frame_at(ASSETS / f"{name}.gif", seconds)
            image.save(WORK / f"{name}-gif-{seconds:.1f}.jpg", quality=96)
            image.thumbnail((500, 360))
            x, y = (index % 2) * 500, (index // 2) * 388
            contact.paste(image, (x, y))
            draw.text((x + 10, y + 362), f"Decoded GIF · {seconds:.1f}s", font=font(17), fill="#EEE8D5")
        contact.save(WORK / f"{name}-gif-contact.jpg", quality=93)
        for seconds in [samples[0], samples[len(samples) // 2], samples[-1]]:
            frame_at(ASSETS / f"{name}.mp4", seconds).save(WORK / f"{name}-mp4-{seconds:.1f}.jpg", quality=96)


def provenance():
    names = ["ghost.svg", "favicon.svg", "tutorial-poster.svg", "tutorial-poster.jpg", "social-card.jpg"]
    names.extend(f"{name}.{extension}" for name in DEMOS for extension in ["gif", "jpg", "mp4"])
    art_alt = {
        "ghost.svg": "Original ivory phantom carrying a teal notebook beside a seafoam cursor.",
        "favicon.svg": "Small ivory Phantom Fudge ghost on deep teal.",
        "tutorial-poster.svg": "Phantom Fudge: the complete walkthrough, 38 minutes and 15 chapters, with an original notebook-carrying ghost.",
        "tutorial-poster.jpg": "Phantom Fudge: the complete walkthrough, 38 minutes and 15 chapters, with an original notebook-carrying ghost.",
        "social-card.jpg": "Phantom Fudge, your words, your context, your call, with an ivory phantom and teal notebook.",
    }
    records = []
    for name in names:
        file = ASSETS / name
        record = {"file": name, "bytes": file.stat().st_size, "sha256": sha256(file),
                  "alt": art_alt[name] if name in art_alt else DEMOS[file.stem]["alt"]}
        if file.suffix in [".jpg", ".gif"]:
            with Image.open(file) as image:
                record["width"], record["height"] = image.size
                if file.suffix == ".gif":
                    duration = 0
                    for index in range(image.n_frames):
                        image.seek(index)
                        duration += image.info.get("duration", 0)
                    record.update({"frames": image.n_frames, "duration_seconds": duration / 1000,
                                   "fps": 10, "loop": "infinite"})
        elif file.suffix == ".mp4":
            with av.open(str(file)) as container:
                stream = container.streams.video[0]
                if len(container.streams.audio):
                    raise RuntimeError("Website loop unexpectedly contains audio.")
                record.update({"width": stream.width, "height": stream.height,
                               "fps": float(stream.average_rate),
                               "duration_seconds": float(stream.duration * stream.time_base),
                               "audio_streams": 0})
        records.append(record)
    demos = {}
    for name, demo in DEMOS.items():
        output_at = 0
        segments = []
        for segment in demo["segments"]:
            duration = (segment["end_frame"] - segment["start_frame"]) / FPS
            segments.append({
                **segment,
                "source_start_seconds": segment["start_frame"] / FPS,
                "source_end_seconds_exclusive": segment["end_frame"] / FPS,
                "output_start_seconds": output_at, "output_end_seconds": output_at + duration,
                "speed": 1,
            })
            output_at += duration
        demos[name] = {
            "alt": demo["alt"], "edit_note": demo["edit_note"],
            "segments": segments, "poster_output_seconds": demo["poster_seconds"],
            "sampled_gif_review_seconds": demo["review_seconds"],
            "sampled_mp4_review_seconds": [demo["review_seconds"][0],
                                           demo["review_seconds"][len(demo["review_seconds"]) // 2],
                                           demo["review_seconds"][-1]],
        }
    data = {
        "schema": 1,
        "source": {
            "file": str(SOURCE.relative_to(ROOT)).replace("\\", "/"),
            "sha256": SOURCE_HASH, "bytes": SOURCE.stat().st_size,
            "fps": 30, "width": 1920, "height": 1080, "duration_seconds": 2297.767,
            "preservation": "Original full tutorial read only; exact SHA-256 verified before and after derivation.",
            "disclosure": "Actual unchanged-XAML WPF visual-tree renders with scripted sample I/O. Not a live meeting, desktop recording, physical mouse recording, real audio capture or model response. Pointer guides already exist in the tutorial.",
            "timing_evidence": "Exact shot action metadata aligned with the prepared final timeline; zero-based source frame ranges are start-inclusive/end-exclusive.",
        },
        "artwork": {
            "creator": "Original vector illustration authored by GitHub Copilot for Phantom Fudge.",
            "description": "Asymmetric warm-ivory phantom, notebook, cursor, seafoam mist and original composition; no borrowed illustrations, logos, images or downloaded fonts.",
            "palette": "DeepSeaFoam canonical colors; palette includes Solarized-derived colors.",
            "colors": {"base": "#000F13", "panel": "#001E26", "accent": "#00A591",
                       "text": "#93A1A1", "faint": "#839496", "warm": "#EEE8D5",
                       "lightEdge": "#FDF6E3", "border": "#586E75"},
            "fonts": "Installed Segoe UI Variable Display / Segoe UI; Arial / sans-serif SVG fallbacks. No font files distributed.",
            "review": "Actual 1920×1080 poster, 480×270 poster, 1200×630 social card, 640×720 ghost raster and 64×64 favicon inspected.",
        },
        "derivatives": {
            "geometry": "1000×720. Source crops aspect-contained in a 1000×600 pane at (0,68), never stretched. Editorial labels and disclosures occupy separate header/footer areas, never cover UI.",
            "motion": "All retained source spans run at 1×; native tutorial camera and pointer guides preserved. GIF 10 fps; silent H.264 MP4 30 fps.",
            "review_limit": "Representative decoded GIF frames at early/action/result/end and all internal edit boundaries, plus early/middle/end decoded MP4 stills were inspected. Active controls and results are readable at native size. The tutorial's existing focus camera can crop peripheral labels and neighboring panels; the excerpts do not reconstruct those missing pixels. This is sampled visual review, not exhaustive frame review, direct listening, user approval, live integration testing or capture qualification.",
            "demos": demos,
        },
        "reproduction": {
            "artwork": "node tools/Site/make-artwork.mjs",
            "media": "python tools/Site/make-demo-media.py --encode --review-outputs",
            "after_visual_review": "python tools/Site/make-demo-media.py --provenance --record-reviewed",
            "dependencies": "Existing tools/Docs Puppeteer, installed Edge (or --browser), Pillow, PyAV and imageio-ffmpeg. No network or package installation.",
            "note": "Hashes describe these delivered files. Codec/browser/font versions can change regenerated bytes. Inspect outputs before updating a review assertion.",
        },
        "files": records,
    }
    if sha256(SOURCE) != SOURCE_HASH:
        raise RuntimeError("Immutable tutorial changed during derivation.")
    (ASSETS / "asset-provenance.json").write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect-source", action="store_true")
    parser.add_argument("--encode", action="store_true")
    parser.add_argument("--review-outputs", action="store_true")
    parser.add_argument("--provenance", action="store_true")
    parser.add_argument("--record-reviewed", action="store_true",
                        help="Attest that decoded outputs were separately visually inspected; this flag performs no review.")
    args = parser.parse_args()
    if sha256(SOURCE) != SOURCE_HASH:
        raise RuntimeError("Source tutorial hash differs; refusing to derive new media.")
    ASSETS.mkdir(parents=True, exist_ok=True)
    if args.inspect_source:
        inspect_source()
    if args.encode:
        for name, demo in DEMOS.items():
            encode(name, demo)
    if args.review_outputs:
        review_outputs()
    if args.provenance:
        if not args.record_reviewed:
            parser.error("--provenance requires --record-reviewed after separate visual inspection.")
        provenance()
    if not any(vars(args).values()):
        parser.error("Choose --inspect-source, --encode, --review-outputs or --provenance.")


if __name__ == "__main__":
    main()
