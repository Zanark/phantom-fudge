import unittest

from metrics import aggregate, errors, normalize


class MetricsTests(unittest.TestCase):
    def test_case_and_punctuation(self):
        self.assertEqual(errors("Hello, WORLD!", "hello world")["errors"], 0)

    def test_numbers_are_common(self):
        self.assertEqual(normalize("25, 9:30, 3.5, 12%"), normalize("twenty five nine thirty three point five twelve percent"))

    def test_grouped_numerals(self):
        self.assertEqual(normalize("1,000"), normalize("one thousand"))

    def test_apostrophes(self):
        self.assertEqual(normalize("Don't don’t"), ["dont", "dont"])

    def test_all_edit_types(self):
        self.assertEqual(errors("one two three", "one too three")["substitutions"], 1)
        self.assertEqual(errors("one two three", "one three")["deletions"], 1)
        self.assertEqual(errors("one two", "one two three")["insertions"], 1)

    def test_empty_hypothesis(self):
        self.assertEqual(errors("one two three", "")["deletions"], 3)

    def test_empty_reference_is_not_divided_by_zero(self):
        self.assertIsNone(errors("", "hello")["wer"])
        self.assertEqual(errors("", "hello")["insertions"], 1)

    def test_wer_can_exceed_one(self):
        self.assertEqual(errors("cat", "dog horse bird")["wer"], 3)

    def test_weighted_aggregation_not_mean_of_case_wer(self):
        total = aggregate([errors("a", "x"), errors("a b c d", "a b c d")])
        self.assertEqual(total["wer"], 0.2)
        self.assertEqual(total["reference_words"], 5)

    def test_known_alignment(self):
        row = errors("the cat sat on the mat", "the dog sat on mat now")
        self.assertEqual(row["errors"], 3)


if __name__ == "__main__":
    unittest.main()
