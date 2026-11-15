"""Production-owner SS contracts; complements executable extracted-body tests.

Native manager/event timing, Unity/GPU clipping, real display and Android JNI are
explicit boundaries, not inferred from host geometry or dormant port tests.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / 'tools/silksong-patches/src/dualscreen'
GENERATOR = ROOT / 'tools/shared-patches-tests/generate_ss_shell_fixture.py'
_spec = importlib.util.spec_from_file_location('ss_shell_extractor', GENERATOR)
extractor = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(extractor)


class SilksongProductionShellContracts(unittest.TestCase):
    def test_fixture_compiles_complete_authoritative_entry_shell_title_and_pane_builds(self):
        entrypoints = json.loads((ROOT/'tools/silksong-patches/entrypoints.json').read_text(encoding='utf-8'))['entryPoints']
        self.assertEqual(1, sum(value.get('className') == 'DualScreenV2' and
                                value.get('methodName') == 'Bootstrap' and
                                value.get('loadTypes') == 0 for value in entrypoints))
        with tempfile.TemporaryDirectory(prefix='ss-shell-body-') as folder:
            output, manifest = Path(folder)/'fixture.cs', Path(folder)/'identity.json'
            extractor.generate(output, SOURCE, manifest)
            data = json.loads(manifest.read_text(encoding='utf-8'))
            identities = {value['member']: value for value in data['bodies']}
            for file, name in (
                ('DualScreenV2.cs', 'DualScreenV2'),
                ('DualScreenV2.cs', 'DsHudReleasePump'),
                ('DsShell.cs', 'DsShell'), ('DsTitleCard.cs', 'DsTitleCard'),
                ('DsScreens.cs', 'DsGridScreen'), ('DsMapScreen.cs', 'DsMapScreen'),
                ('DsLayout.cs', 'DsLayout'), ('DsLayout.cs', 'DsShellInput'),
                ('DsInput.cs', 'DsInput'), ('DsActions.cs', 'DsActionBar'),
            ):
                source = (SOURCE/file).read_text(encoding='utf-8')
                body = extractor.class_text(source, name)
                self.assertEqual(hashlib.sha256(body[body.index('{'):].encode()).hexdigest(), identities[name]['body_utf8_lf_sha256'])
                self.assertTrue(identities[name]['body_identical'])
            for file, name in (
                ('DsScreens.cs', 'DsInventoryScreen'), ('DsLoadoutScreen.cs', 'DsLoadoutScreen'),
                ('DsTasksScreen.cs', 'DsTasksScreen'), ('DsJournalScreen.cs', 'DsJournalScreen'),
                ('DsIconGrid.cs', 'DsIconGrid'),
            ):
                cls = extractor.class_text((SOURCE/file).read_text(encoding='utf-8'), name)
                build = extractor.member(cls, 'Build')
                self.assertIn(build, output.read_text(encoding='utf-8'))
                self.assertTrue(identities[name+'.Build']['body_identical'])
                self.assertTrue(identities[name+'.production-prefix']['declaration_identical'])
            self.assertIn('namespace SsShellContracts;', output.read_text(encoding='utf-8'))
            self.assertNotIn('HkPauseContracts', output.read_text(encoding='utf-8'))
            self.assertIn('DsHudManagerCallbacks (compiled directly)', identities)
            self.assertIn('DsMapView.LateTick', identities)
            self.assertIn('DsHudView.OnDisable', identities)
            self.assertIn('DsHudView.Suspend', identities)
            self.assertIn('DsHudView.BeforeCamera', identities)
            self.assertIn('DsHudView.RestoreAllScopes', identities)
            shell = extractor.class_text((SOURCE/'DsShell.cs').read_text(encoding='utf-8'), 'DsShell')
            generated = extractor.class_text(output.read_text(encoding='utf-8'), 'DsShell')
            for name in ('SetPaused', 'EndSlide', 'SettleSlide', 'SuspendPresentation'):
                value = extractor.member(shell, name)
                self.assertEqual(value, extractor.member(generated, name))
                self.assertTrue(identities['DsShell.'+name]['body_identical'])
                self.assertEqual(hashlib.sha256(value[value.index('{'):].encode()).hexdigest(),
                                 identities['DsShell.'+name]['body_utf8_lf_sha256'])


if __name__ == '__main__':
    unittest.main()
