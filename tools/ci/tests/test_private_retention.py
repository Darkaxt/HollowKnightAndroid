import importlib.util
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[3]

class PrivateCompilerRetentionTests(unittest.TestCase):
    def test_all_committed_line_endings_match_declared_text_attributes(self):
        entries = subprocess.check_output(['git', '-C', str(ROOT), 'ls-files', '--eol', '-z']).decode()
        incompatible = []
        for entry in entries.split('\0'):
            if not entry:
                continue
            metadata, path = entry.split('\t', 1)
            index, _, attributes = metadata.split(maxsplit=2)
            if index in ('i/crlf', 'i/mixed') and attributes.strip() == 'attr/text eol=lf':
                incompatible.append(path)
        self.assertEqual([], incompatible)

if __name__ == '__main__':
    unittest.main()
