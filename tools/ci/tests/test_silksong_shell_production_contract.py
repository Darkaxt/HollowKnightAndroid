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

    def test_native_reconciliation_and_gameplay_admission_precede_input(self):
        cls = extractor.class_text((SOURCE/'DualScreenV2.cs').read_text(encoding='utf-8'), 'DualScreenV2')
        update = extractor.member(cls, 'Update')
        self.assertLess(update.index('BindGameManager();'), update.index('OnGamePauseChanged(NativeGamePaused());'))
        self.assertLess(update.index('OnGamePauseChanged(NativeGamePaused());'), update.index('_input.Poll();'))
        self.assertLess(update.index('_shell.SetIdle('), update.index('_input.Poll();'))
        self.assertIn('_input != null && !_shell.Paused', update)
        self.assertIn('_shell.SetPaused(NativeGamePaused());', extractor.member(cls, 'BuildShell'))
        scalar = extractor.member(cls, 'NativeGamePaused')
        self.assertIn('var current = _gameManager;', scalar)
        self.assertIn('current.isPaused', scalar)
        self.assertIn('current.ui.uiState == GlobalEnums.UIState.PAUSED', scalar)
        # The existing Bind manager lookup is not doubled by the added scalar gate.
        statements = re.sub(r'//[^\n]*', '', scalar)
        self.assertNotIn('SilentInstance', statements)
        self.assertNotIn('Inventory', statements)
        for name in ('NativeGamePaused', 'OnGamePauseChanged'):
            body = re.sub(r'//[^\n]*', '', extractor.member(cls, name))
            for token in ('new ', 'File.', 'Directory.', 'Resources.', 'FindObject', 'Serialize', 'Debug.'):
                self.assertNotIn(token, body)

    def test_pause_does_not_route_through_page_activation_or_idle_policy(self):
        cls = extractor.class_text((SOURCE/'DsShell.cs').read_text(encoding='utf-8'), 'DsShell')
        pause = extractor.member(cls, 'SetPaused')
        self.assertIn('paused == _paused', pause)
        self.assertIn('var outgoing = SettleSlide();', pause)
        self.assertIn('SuspendPresentation(outgoing);', pause)
        self.assertIn('SuspendPresentation(_entries[_active]);', pause)
        self.assertIn('_gestures.Reset();', pause)
        settle = extractor.member(cls, 'SettleSlide')
        suspend = extractor.member(cls, 'SuspendPresentation')
        self.assertIn('_slideFrom = -1;', settle)
        self.assertIn('Shift(outgoing.Host, 0f);', settle)
        self.assertIn('outgoing.Host.gameObject.SetActive(false);', settle)
        self.assertIn('Shift(_entries[_active].Host, 0f);', settle)
        self.assertIn('return outgoing;', settle)
        self.assertIn('Guard(e, suspend.SuspendPresentation);', suspend)
        for body in (pause, settle, suspend):
            for token in ('SetIdle(', 'OnHide(', 'OnShow(', 'EndSlide('):
                self.assertNotIn(token, re.sub(r'//[^\n]*', '', body))
        end = extractor.member(cls, 'EndSlide')
        self.assertIn('var outgoing = SettleSlide();', end)
        self.assertIn('if (outgoing != null && _operational)', end)
        self.assertIn('Guard(prev, () => prev.Screen.OnHide());', end)
        operational = extractor.member(cls, 'UpdateOperational')
        self.assertIn('bool operational = !_disposed && _visible && !_idle && !_transitioning;', operational)
        self.assertIn('_hud.SetVisible(operational && !_paused)', operational)
        for method, delegate in (('Tick', 'TickOperational(dt);'), ('OnGesture', 'OnOperationalGesture(g);')):
            wrapper = extractor.member(cls, method)
            self.assertIn('if (!_operational || _paused) return;', wrapper)
            self.assertIn(delegate, wrapper)
            self.assertNotIn('=>', wrapper)
        presentation = extractor.member(cls, 'ApplyPresentation')
        for role in ('_tabBar', '_body', '_header', '_actionHost'):
            self.assertIn(role+'.gameObject.SetActive(gameplay);', presentation)
        self.assertIn('_title.SetVisible(!gameplay)', presentation)

    def test_map_pause_releases_transients_but_not_marker_or_framing_choices(self):
        cls = extractor.class_text((SOURCE/'DsMapScreen.cs').read_text(encoding='utf-8'), 'DsMapScreen')
        pause = extractor.member(cls, 'SuspendPresentation')
        self.assertIn('_sliderGesture = false;', pause)
        self.assertIn('_slider.Release();', pause)
        self.assertIn('_view.SetVisible(false);', pause)
        for token in ('_markerMode =', '_markerPick =', '_erasing =', 'ResetPan(', 'ResetView(', 'SetMode('):
            self.assertNotIn(token, pause)

    def test_ss_fixture_never_imports_or_edits_published_hk_fixture_or_policy(self):
        generator = GENERATOR.read_text(encoding='utf-8')
        self.assertNotIn('generate_hk_pause_fixture', generator)
        self.assertNotIn('hollow-knight-patches', generator)
        fixture = (ROOT/'tools/shared-patches-tests/SilksongShellFixture.cs').read_text(encoding='utf-8')
        self.assertNotRegex(fixture, r'using\s+PlayerData\s*=')
        self.assertNotIn('class DsShell', fixture)
        self.assertNotIn('class DualScreenV2', fixture)

    def test_production_shell_has_no_lower_telemetry_and_primary_overlay_is_retained(self):
        files=('DualScreenV2.cs','DsShell.cs','DsScreens.cs','DsLoadoutScreen.cs','DsTasksScreen.cs','DsJournalScreen.cs','DsMapScreen.cs')
        for file in files:
            source=(SOURCE/file).read_text(encoding='utf-8')
            self.assertNotIn('PerfOverlay', source)
            self.assertNotRegex(source, r'Battery|battery|\"fps\"|FPSCounter|batteryLevel')
        primary=(ROOT/'tools/silksong-patches/src/PerfOverlay.cs').read_text(encoding='utf-8')
        self.assertIn('class PerfOverlay', primary)
        # Its routing is still a native display/GPU boundary, not claimed away.
        self.assertNotIn('targetDisplay', primary)


if __name__ == '__main__':
    unittest.main()
