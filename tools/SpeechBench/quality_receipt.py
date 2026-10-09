"""Publish only the synthetic quality runner's allowlisted, path-free measurements."""

import hashlib
import json
from pathlib import Path

from metrics import aggregate, errors, normalize
from quality_regression import HERE, LOCAL, write


def character_errors(reference, hypothesis):
    # Generic whitespace-insensitive spelling metric, not a LeetCode substitution.
    # Primary WER is always preserved unmodified alongside it.
    ref, hyp = "".join(normalize(reference)), "".join(normalize(hypothesis))
    previous = list(range(len(hyp) + 1))
    for i, left in enumerate(ref, 1):
        current = [i]
        for j, right in enumerate(hyp, 1):
            current.append(min(previous[j] + 1, current[j - 1] + 1,
                               previous[j - 1] + (left != right)))
        previous = current
    return {"reference_characters": len(ref), "errors": previous[-1],
            "rate": previous[-1] / len(ref) if ref else None}


def compact(receipt):
    result = {key: receipt[key] for key in (
        "kind", "identity", "versions", "load_and_verify_seconds", "repeats", "totals", "queue",
        "first_partial_seconds", "end_of_speech_to_final_seconds", "stop_to_stopped_seconds",
        "normalization", "limitations", "source_sha256", "created_utc")}
    rows = []
    for row in receipt["rows"]:
        allowed = ("id", "reference", "text", "voice", "condition", "category", "sha256", "repeat",
                   "audio_seconds", "speech_start_seconds", "speech_end_seconds",
                   "ready_seconds", "first_partial_seconds", "first_final_seconds", "last_final_seconds",
                   "end_of_speech_to_final_seconds", "stop_to_stopped_seconds", "elapsed_seconds", "faults")
        output = {key: row[key] for key in allowed}
        output["score"] = {key: row["score"][key] for key in (
            "reference_words", "substitutions", "deletions", "insertions", "errors", "wer")}
        rescored = errors(row["reference"], row["text"])
        if any(rescored[key] != value for key, value in output["score"].items()):
            raise ValueError("Persisted WER does not match the actual synthetic hypothesis")
        output["character_score"] = character_errors(row["reference"], row["text"])
        rows.append(output)
    result["rows"] = rows
    chars = sum(row["character_score"]["reference_characters"] for row in rows)
    edits = sum(row["character_score"]["errors"] for row in rows)
    result["character_totals"] = {"reference_characters": chars, "errors": edits,
                                "rate": edits / chars if chars else None}
    result["category_totals"] = {
        category: aggregate([row["score"] for row in rows if row["category"] == category])
        for category in sorted({row["category"] for row in rows})}
    return result


def main():
    labels = ["vosk-paced", "base.en-paced", "small.en-paced", "small.en-technical-paced",
              "small.en-technical-paced-bounded15", "small.en-technical-paced-parallel15"]
    runs = [dict(run_label=label, **compact(json.loads((LOCAL / label / "receipt.json").read_text(encoding="utf-8"))))
            for label in labels]
    identifiers = [row["id"] for row in runs[0]["rows"]]
    for run in runs:
        if [row["id"] for row in run["rows"]] != identifiers:
            raise ValueError("Missing or reordered cases")
        if [(r["sha256"], r["reference"]) for r in run["rows"]] != [
                (r["sha256"], r["reference"]) for r in runs[0]["rows"]]:
            raise ValueError("Compared engines did not receive identical synthetic PCM/reference text")
        if aggregate([r["score"] for r in run["rows"]]) != run["totals"]:
            raise ValueError("Aggregate WER mismatch")
        if run["queue"]["fault_count"] or run["queue"]["drop_count"] or any(row["faults"] for row in run["rows"]):
            raise ValueError("A paced quality run faulted; preserve failure and investigate")
    original_stress = json.loads((LOCAL / "small.en-technical-dual-long" / "receipt.json").read_text(encoding="utf-8"))
    stress = json.loads((LOCAL / "small.en-technical-dual-long-parallel15" / "receipt.json").read_text(encoding="utf-8"))
    shared_intake = json.loads((LOCAL / "small.en-technical-dual-long-bounded15" / "receipt.json").read_text(encoding="utf-8"))
    rotation = json.loads((LOCAL / "small.en-technical-dual-rotation" / "receipt.json").read_text(encoding="utf-8"))
    short_probe = json.loads((LOCAL / "small.en-technical-dual-rotation15" / "receipt.json").read_text(encoding="utf-8"))
    sampling = json.loads((LOCAL / "small.en-technical-dual-rotation-fallback" / "receipt.json").read_text(encoding="utf-8"))
    for failed_run in (sampling, shared_intake):
        failed_run["unusable_original_harness_stop_delta_seconds"] = failed_run["stop_to_stopped_seconds"]
        failed_run["stop_to_stopped_seconds"] = None
        failed_run["timing_note"] = "No stopped acknowledgement occurred; the old harness delta is not a valid Stop latency."
    setup = json.loads((LOCAL / "setup-check" / "receipt.json").read_text(encoding="utf-8"))
    if not setup["ready"] or not setup["stopped"] or setup["fault_count"] or setup["drop_count"]:
        raise ValueError("Persisted production setup did not pass")
    if setup["identity"]["configFingerprint"] != runs[-1]["identity"]["configFingerprint"]:
        raise ValueError("Production setup and final measured profile differ")
    for stress_run in (stress, rotation, short_probe):
        if stress_run["status"] != "stopped" or stress_run["errors"]:
            raise ValueError("Dual-source stress failed")
        for source in stress_run["span_ledger"].values():
            if (not source["contiguous"] or not source["all_samples_owned_once"]
                    or source["input_sha256"] != source["decoded_pcm_sha256"]):
                raise ValueError("Dual-source PCM ownership was not exact")
    if stress["forced_rotation_count"] < 4:
        raise ValueError("Forced rotation coverage is incomplete")
    original_stress["initial_coverage_failure"] = (
        "Service stopped successfully with byte-exact PCM and no fault/drop; the first harness assertion "
        "failed because natural Vosk endpoints occurred around 20 seconds, so this run did not cover "
        "forced 30-second rotation. A separate explicitly injected rotation case follows.")
    receipt = {
        "schema_version": 1, "title": "Local ASR synthetic quality and paced latency regression",
        "date": "2026-10-09",
        "hardware": {"cpu": "AMD Ryzen 7 PRO 7840U", "cores": 8, "logical_processors": 16,
                     "inference": "CPU int8, 4 threads; no verified NVIDIA GPU",
                     "ram_gib_approx": 24, "machine_exclusivity": "Shared host; background work may affect latency."},
        "safety": {"real_microphone": False, "render_endpoint": False, "playback": False,
                   "cloud_asr": False, "original_user_recording_available": False,
                   "reference_sentences_as_recognizer_hints": False,
                   "fixed_text_substitutions": False,
                   "runtime_model_downloads": False,
                   "fixtures": "Two installed Windows synthesis voices; in-memory TTS -> ignored .local PCM."},
        "scoring": {"primary": "Existing SpeechBench WER, unchanged; compound word tokenization counts as errors.",
                    "secondary": "Character edit distance over normalized words joined without spaces; all engines identical.",
                    "latency": "Wall time from last clean synthesized sample >=100/32768 to last nonempty final; fixed before adding noise.",
                    "cold": "Fresh-process model import/verification/load and first row are reported; OS file cache is uncontrolled. No row omitted.",
                    "silence": "Three nonspeech cases retain any final insertion/hallucination errors. Provisional text may still be wrong."},
        "runs": runs, "dual_long_stress": stress,
        "experiments_retained": {
            "initial_natural_long": original_stress,
            "rejected_thirty_second_windows": rotation,
            "rejected_sampling_retry": sampling,
            "rejected_shared_intake": shared_intake,
            "shorter_window_probe": short_probe,
            "interpretation": "The first long run passed runtime/PCM checks but failed the harness's rotation coverage assertion. "
                              "Injected 30-second windows exposed 44 word insertions on Computer. A bounded 0/0.2/0.4-temperature "
                              "retry exceeded the 20-second native-call deadline and visibly faulted; its incomplete scores are "
                              "not comparable to completed runs. The 15-second probe reduced that source to two insertions. "
                              "The first production 15-second dual run then reached the shared Vosk intake's 32-frame "
                              "bound and visibly faulted. Dedicated bounded source queues/workers replaced that shared "
                              "hybrid intake. The adopted parallel 15-second path is remeasured without endpoint injection."},
        "production_setup_check": setup,
        "default_selection": {
            "engine": "hybrid-whisper", "model": "small.en", "compute": "CPU int8", "threads": 4,
            "vocabulary": "technical", "rotation_target_seconds": 15, "max_span_seconds": 16,
            "rationale": "Retains fast provisional text; stronger local final model reduces errors on this corpus. "
                         "Disclosed general technical terms improve proper-name spelling and character errors. "
                         "Raw WER remains visible: LeetCode as one token costs two word edits against leet code, "
                         "so hinted small.en has a higher raw WER than unhinted small.en, but lower than Vosk. "
                         "Unrelated and nonspeech final outputs did not regress here.",
            "remaining": "One synthesized target case uses to instead of two, and two use some instead of sum. "
                         "Longer speech has additional model errors; byte-exact ownership does not imply zero word errors. "
                         "Nothing is claimed 100 percent accurate."},
    }
    destination = HERE / "results" / "synthetic-quality-20261009.json"
    write(destination, receipt)
    print(json.dumps({"receipt": str(destination.relative_to(HERE)),
                      "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
                      "word_errors": [r["totals"]["errors"] for r in runs],
                      "character_errors": [r["character_totals"]["errors"] for r in runs]}))


if __name__ == "__main__":
    main()
