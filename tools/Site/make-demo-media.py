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
        "chapter_id": "questions",
        "title": "Select, review, then Ask",
        "copy": "Build a question from selected lines; send only when you choose.",
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
        "chapter_id": "followups",
        "title": "Choose whether to include the previous answer",
        "copy": "Opt one completed answer into your next explicit Ask.",
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
        "chapter_id": "cover",
        "title": "Cover and reveal the workspace",
        "copy": "Use the same book icon to cover the view and bring it back.",
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
    "demo-inputs": {
        "chapter_id": "audio",
        "title": "Choose Mic or Both",
        "copy": "Pick the input tracks before starting; source selection alone opens no audio.",
        "alt": "Scripted sample: click Mic to show microphone devices, then later Both to select Computer and Mic; the stopped state remains.",
        "footer": "Stopped sample devices · choose inputs first · Start remains explicit",
        "poster_seconds": 2.5,
        "segments": [
            {"scene": "mic-mode", "start_frame": 11338, "end_frame": 11473, "action_frame": 11359,
             "crop_xywh": [44, 236, 1442, 666], "label": "01 / Choose Mic for microphone input"},
            {"scene": "both-mode", "start_frame": 12099, "end_frame": 12234, "action_frame": 12120,
             "crop_xywh": [44, 236, 1442, 666], "label": "02 / Later: choose Both for two input tracks"},
        ],
        "review_seconds": [0, .6, .8, 2.5, 4.4, 4.5, 5.1, 5.3, 8.9],
        "edit_note": "Two labelled excerpts omit the intervening microphone-card explanation. Both mode activates the Computer and Mic source choices, not capture. The camera shows only part of the resulting device list. Sample names, no real device access.",
    },
    "demo-transcription": {
        "chapter_id": "transcription",
        "title": "Start deliberately; stop deliberately",
        "copy": "Watch the running indicator appear, then stop while keeping the transcript.",
        "alt": "Scripted sample: Start lights the running dot; a later Stop clears it and leaves completed sample transcript rows visible.",
        "footer": "Sample events, not real recording · Stop does not cancel an answer",
        "poster_seconds": 7,
        "segments": [
            {"scene": "start", "start_frame": 15204, "end_frame": 15339, "action_frame": 15225,
             "crop_xywh": [44, 236, 1364, 658], "label": "01 / Start the selected inputs explicitly"},
            {"scene": "stop", "start_frame": 17058, "end_frame": 17223, "action_frame": 17079,
             "crop_xywh": [44, 236, 1364, 658], "label": "02 / Later: Stop and keep the completed lines"},
        ],
        "review_seconds": [0, .6, .8, 2.5, 4.4, 4.5, 5.1, 5.3, 7, 9.9],
        "edit_note": "Start and Stop excerpts are separated by an explicit Later label. All intervening sample transcript arrival and most explanatory waiting are omitted; this does not demonstrate recognition speed. No raw audio is recorded or saved. Normal capture may drain final accepted lines after Stop.",
    },
    "demo-answer-size": {
        "chapter_id": "answers",
        "title": "Make the answer easier to read",
        "copy": "Adjust the answer font independently of the transcript and notes.",
        "alt": "Scripted sample: drag the answer Size slider from 10 to 17; the completed answer text grows.",
        "footer": "Answer and interpretation only · size 8–24 · current session",
        "poster_seconds": 5,
        "segments": [
            {"scene": "answer-font", "start_frame": 30027, "end_frame": 30267, "action_frame": 30048,
             "action_end_frame": 30090,
             "crop_xywh": [44, 300, 1442, 578], "label": "Answer Size / drag from 10 to 17"},
        ],
        "review_seconds": [0, .6, .9, 1.4, 2, 2.2, 5, 7.9],
        "edit_note": "Continuous 1× excerpt. The complete scripted slider progression ends 3.4 seconds into its source scene; the larger-font result is held. The source camera crops the lower part of the enlarged reader; the excerpt additionally excludes the resulting partial trailing line. This is a font-control demonstration, not a full-answer reading clip.",
    },
    "demo-keyboard": {
        "chapter_id": "keyboard",
        "title": "Select a line from the keyboard",
        "copy": "From Control, V enters Select; Space toggles the focused row without sending.",
        "alt": "Scripted keyboard demo: V changes Control to Select; later Space selects the first row and the bar shows one selected.",
        "footer": "V enters Select · Space toggles a row · selection is not submission",
        "poster_seconds": 6.5,
        "segments": [
            {"scene": "keys-select", "start_frame": 50294, "end_frame": 50414, "action_frame": 50315,
             "crop_xywh": [44, 246, 1442, 590], "label": "01 / From Control, press V for Select"},
            {"scene": "keys-space", "start_frame": 50956, "end_frame": 51106, "action_frame": 50977,
             "crop_xywh": [44, 246, 1442, 590], "label": "02 / Later: press Space to select the focused row"},
        ],
        "review_seconds": [0, .6, .8, 2, 3.9, 4, 4.6, 4.8, 6.5, 8.9],
        "edit_note": "Two labelled keyboard excerpts; the intervening gg navigation to the first row is omitted. The mode bar and selection count provide the visible result. Original keyboard guides in the tutorial sidebar are outside this focused crop; editorial labels name the exact keys from action metadata. No request is sent.",
    },
    "demo-themes": {
        "chapter_id": "workspace",
        "title": "Choose comfortable contrast",
        "copy": "Switch Harbor Daylight, Plain B/W and DeepSeaFoam without resetting content.",
        "alt": "Scripted sample: use the actual theme buttons to change DeepSeaFoam to Harbor Daylight, later Plain B/W, then back to DeepSeaFoam.",
        "footer": "Appearance only · theme changes preserve transcript and question",
        "poster_seconds": 10,
        "segments": [
            {"scene": "daylight", "start_frame": 8617, "end_frame": 8722, "action_frame": 8638,
             "crop_xywh": [44, 132, 1364, 770], "label": "01 / Switch to Harbor Daylight"},
            {"scene": "plain", "start_frame": 8923, "end_frame": 9028, "action_frame": 8944,
             "crop_xywh": [44, 132, 1364, 770], "label": "02 / Later: choose Plain B/W"},
            {"scene": "dark", "start_frame": 9183, "end_frame": 9303, "action_frame": 9204,
             "crop_xywh": [44, 132, 1364, 770], "label": "03 / Later: return to DeepSeaFoam"},
        ],
        "review_seconds": [0, .6, .8, 2, 3.4, 3.5, 4.1, 4.3, 6.9, 7, 7.6, 7.8, 10, 10.9],
        "edit_note": "Three source actions at original speed with shorter explanatory holds between labelled later excerpts. All actual button activations and resulting themes retained. The sample workspace is empty; preserving content is product behavior described by the full tutorial, not a new data-persistence test.",
    },
}
ORIGINAL_DEMOS = ("demo-selection", "demo-context", "demo-cover")


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


def review_outputs(names=None):
    WORK.mkdir(parents=True, exist_ok=True)
    for name in names or DEMOS:
        demo = DEMOS[name]
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


def provenance(review_record=None, web_record=None):
    existing_path = ASSETS / "asset-provenance.json"
    existing = json.loads(existing_path.read_text(encoding="utf-8")) if existing_path.exists() else {}
    reviews = json.loads(Path(review_record).read_text(encoding="utf-8")) if review_record else {}
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
            "chapter_id": demo["chapter_id"], "title": demo["title"], "copy": demo["copy"],
            "alt": demo["alt"], "edit_note": demo["edit_note"],
            "segments": segments, "poster_output_seconds": demo["poster_seconds"],
            "sampled_gif_review_seconds": demo["review_seconds"],
            "sampled_mp4_review_seconds": [demo["review_seconds"][0],
                                           demo["review_seconds"][len(demo["review_seconds"]) // 2],
                                           demo["review_seconds"][-1]],
        }
        if name in reviews:
            review = reviews[name]
            for extension in ["gif", "jpg", "mp4"]:
                if review["output_sha256"][extension] != sha256(ASSETS / f"{name}.{extension}"):
                    raise RuntimeError(f"{name}: visual review is bound to different output bytes.")
            demos[name]["visual_review"] = review
        elif name in ORIGINAL_DEMOS and existing:
            demos[name]["visual_review"] = existing["derivatives"]["demos"][name].get(
                "visual_review", {"status": "Previously sampled; media byte-identical to original delivery."})
        else:
            raise RuntimeError(f"{name}: provide a specific hash-bound visual review record.")
    data = {
        "schema": 2,
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
            "review_limit": "Per-demo visual_review records describe the separately inspected pixels and their output hashes; encoding or contact-sheet extraction alone is not approval. The tutorial's existing focus camera can crop peripheral labels and neighboring panels; excerpts do not reconstruct missing pixels. Review is sampled, not exhaustive frame review, direct listening, user approval, live integration testing or capture qualification.",
            "demos": demos,
        },
        "reproduction": {
            "artwork": "node tools/Site/make-artwork.mjs",
            "media": "python tools/Site/make-demo-media.py --encode --review-outputs",
            "after_visual_review": "python tools/Site/make-demo-media.py --provenance --review-record REVIEW.json",
            "web_video": "python tools/Site/prepare-web-video.py --encode --validate --crf 30",
            "web_provenance_after_visual_review": "python tools/Site/make-demo-media.py --provenance --review-record REVIEW.json --web-record WEB-REVIEWED.json",
            "dependencies": "Existing tools/Docs Puppeteer, installed Edge (or --browser), Pillow, PyAV and imageio-ffmpeg. No network or package installation.",
            "note": "Hashes describe these delivered files. Codec/browser/font versions can change regenerated bytes. Inspect outputs before updating a review assertion.",
        },
        "files": records,
    }
    if "artwork" in existing:
        data["artwork"] = existing["artwork"]
    if web_record:
        web = json.loads(Path(web_record).read_text(encoding="utf-8"))
        if sha256(ROOT / web["file"]) != web["sha256"]:
            raise RuntimeError("Web video provenance hash mismatch.")
        if not isinstance(web.get("visual_review"), dict):
            raise RuntimeError("Web video needs separate hash-bound visual review evidence.")
        if web["visual_review"].get("output_sha256") != web["sha256"]:
            raise RuntimeError("Web video visual review is bound to different bytes.")
        data["web_video"] = web
    elif "web_video" in existing:
        data["web_video"] = existing["web_video"]
    if sha256(SOURCE) != SOURCE_HASH:
        raise RuntimeError("Immutable tutorial changed during derivation.")
    (ASSETS / "asset-provenance.json").write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect-source", action="store_true")
    parser.add_argument("--encode", action="store_true")
    parser.add_argument("--review-outputs", action="store_true")
    parser.add_argument("--provenance", action="store_true")
    parser.add_argument("--demo", action="append", choices=DEMOS,
                        help="Restrict encoding/review to named demos; repeat for several.")
    parser.add_argument("--replace", action="store_true",
                        help="Replace explicitly named new demos only; original three are protected.")
    parser.add_argument("--review-record", help="Per-demo hash-bound notes from separate visual inspection.")
    parser.add_argument("--web-record", help="Validated web-video metadata including separate review evidence.")
    args = parser.parse_args()
    if sha256(SOURCE) != SOURCE_HASH:
        raise RuntimeError("Source tutorial hash differs; refusing to derive new media.")
    ASSETS.mkdir(parents=True, exist_ok=True)
    baseline = json.loads((ASSETS / "asset-provenance.json").read_text(encoding="utf-8"))
    protected = {item["file"]: item["sha256"] for item in baseline["files"]
                 if Path(item["file"]).stem in ORIGINAL_DEMOS}
    for filename, expected in protected.items():
        if sha256(ASSETS / filename) != expected:
            raise RuntimeError(f"Original demo differs from delivered hash: {filename}")
    names = args.demo or list(DEMOS)
    if args.replace and (not args.demo or any(name in ORIGINAL_DEMOS for name in names)):
        parser.error("--replace requires explicit new --demo names; original demos are immutable.")
    if args.inspect_source:
        inspect_source()
    if args.encode:
        for name in names:
            outputs = [ASSETS / f"{name}.{ext}" for ext in ["gif", "jpg", "mp4"]]
            if all(path.exists() for path in outputs) and not args.replace:
                continue
            if name in ORIGINAL_DEMOS and any(path.exists() for path in outputs):
                raise RuntimeError(f"Refusing to overwrite existing original demo: {name}")
            encode(name, DEMOS[name])
    if args.review_outputs:
        review_outputs(names)
    if args.provenance:
        provenance(args.review_record, args.web_record)
    for filename, expected in protected.items():
        if sha256(ASSETS / filename) != expected:
            raise RuntimeError(f"Original demo was modified: {filename}")
    if not any(vars(args).values()):
        parser.error("Choose --inspect-source, --encode, --review-outputs or --provenance.")


if __name__ == "__main__":
    main()
