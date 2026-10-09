import unittest

from quality_receipt import character_errors, errors


class QualityReceiptTests(unittest.TestCase):
    def test_generic_compound_metric_is_not_reference_substitution(self):
        self.assertEqual(character_errors("leet code", "LeetCode")["errors"], 0)
        self.assertEqual(character_errors("any compound", "AnyCompound")["errors"], 0)
        self.assertGreater(character_errors("leet code", "lead code")["errors"], 0)
        self.assertGreater(character_errors("two sum", "to some")["errors"], 0)

    def test_empty_reference_retains_hallucinations(self):
        self.assertEqual(character_errors("", "hallucination")["errors"], 13)
        self.assertIsNone(character_errors("", "hallucination")["rate"])

    def test_real_deletions_and_substitutions_are_counted(self):
        self.assertEqual(character_errors("abc", "ac")["errors"], 1)
        self.assertEqual(character_errors("abc", "adc")["errors"], 1)
        self.assertEqual(character_errors("abc", "abxc")["errors"], 1)

    def test_repeated_model_output_is_not_hidden_by_scoring(self):
        reference = "tea or coffee"
        repeated = "tea or coffee tea or coffee"
        self.assertEqual(errors(reference, repeated)["insertions"], 3)
        self.assertGreater(character_errors(reference, repeated)["errors"], 0)


if __name__ == "__main__":
    unittest.main()
