"""One normalization and deterministic minimum-edit alignment for every engine."""

import re
import unicodedata

from num2words import num2words

NORMALIZATION = (
    "NFKC, lowercase, percent sign -> percent, digit numerals -> English cardinal "
    "words (num2words), apostrophes removed, other punctuation -> spaces; "
    "no stop-word removal or engine-specific substitutions. Colon times split "
    "into two cardinals; decimal points become point. Acronyms are not expanded."
)


def normalize(text):
    text = unicodedata.normalize("NFKC", text).lower().replace("%", " percent ")
    text = re.sub(r"(?<=\d),(?=\d{3}(?:\D|$))", "", text)
    text = re.sub(
        r"\d+(?:\.\d+)?",
        lambda match: " " + num2words(match.group(), lang="en") + " ",
        text,
    )
    text = text.replace("'", "").replace("\u2019", "")
    return re.findall(r"[a-z]+", text)


def errors(reference, hypothesis):
    ref, hyp = normalize(reference), normalize(hypothesis)
    # Tuples are total edits, substitutions, deletions, insertions.
    table = [[(0, 0, 0, 0)] * (len(hyp) + 1) for _ in range(len(ref) + 1)]
    for i in range(len(ref) + 1):
        table[i][0] = (i, 0, i, 0)
    for j in range(len(hyp) + 1):
        table[0][j] = (j, 0, 0, j)
    for i in range(1, len(ref) + 1):
        for j in range(1, len(hyp) + 1):
            if ref[i - 1] == hyp[j - 1]:
                table[i][j] = table[i - 1][j - 1]
            else:
                t, s, d, ins = table[i - 1][j - 1]
                substitution = (t + 1, s + 1, d, ins)
                t, s, d, ins = table[i - 1][j]
                deletion = (t + 1, s, d + 1, ins)
                t, s, d, ins = table[i][j - 1]
                insertion = (t + 1, s, d, ins + 1)
                # Stable preference: substitution, deletion, insertion on ties.
                table[i][j] = min((substitution, deletion, insertion), key=lambda value: value[0])
    total, substitutions, deletions, insertions = table[-1][-1]
    return {
        "reference_words": len(ref), "substitutions": substitutions,
        "deletions": deletions, "insertions": insertions, "errors": total,
        "wer": total / len(ref) if ref else None,
        "normalized_reference": ref, "normalized_hypothesis": hyp,
    }


def aggregate(rows):
    keys = ("reference_words", "substitutions", "deletions", "insertions", "errors")
    result = {key: sum(row[key] for row in rows) for key in keys}
    result["wer"] = result["errors"] / result["reference_words"] if result["reference_words"] else None
    return result
