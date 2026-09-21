import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
NATIVE_MENU = ROOT / "tools/silksong-patches/src/mods/SilksongNativeModsMenu.cs"
LIFECYCLE = ROOT / "tools/silksong-patches/src/mods/SilksongNativeMenuLifecycle.cs"
RUNTIME = ROOT / "tools/silksong-patches/src/mods/SilksongModsRuntime.cs"


def method_body(source: str, signature: str) -> str:
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    raise AssertionError(f"unterminated method: {signature}")


class SilksongNativeModsMenuContractTest(unittest.TestCase):
    def source(self) -> str:
        self.assertTrue(NATIVE_MENU.is_file(), "native Silksong Mods menu source is missing")
        return NATIVE_MENU.read_text(encoding="utf-8")

    def test_options_route_uses_exact_typed_native_menu_seams(self):
        source = self.source()
        for seam in (
            "UIManager",
            "optionsMenuScreen",
            "MenuScreen",
            "MenuSelectable",
            "ShowMenu(binding.ModsScreen)",
            "HideMenu(binding.OptionsScreen)",
        ):
            self.assertIn(seam, source)
        self.assertNotIn("System.Reflection", source)
        self.assertNotIn("GetField(", source)
        self.assertNotIn("GetMethod(", source)

    def test_native_route_is_paused_gameplay_only_and_event_driven(self):
        source = self.source()
        self.assertIn("binding.Ui.uiState == UIState.PAUSED", source)
        self.assertIn("ISubmitHandler", source)
        self.assertIn("IMoveHandler", source)
        self.assertIn("ICancelHandler", source)
        for forbidden in ("Input.Get", "GetButton", "activeDevice", "ReadValue"):
            self.assertNotIn(forbidden, source)

    def test_native_screen_executes_the_complete_typed_menu_model(self):
        source = self.source()
        for operation in (
            "TweakMenuModel",
            ".Open()",
            ".Close()",
            ".MoveGroup(",
            ".MoveRow(",
            ".ToggleMaster()",
            ".ActivateSelected()",
            ".SetSelected(",
            ".Reset()",
        ):
            self.assertIn(operation, source)
        self.assertIn("TweakControlKind.Command", source)
        self.assertIn("TweakControlKind.Route", source)

    def test_invalid_binding_cancels_and_clears_before_retry(self):
        update = method_body(self.source(), "void Update()")
        invalid = update.index("if (!BindingIsAlive())")
        self.assertIn("CancelAndClearBinding();", update[invalid:])
        cancel = update.index("CancelAndClearBinding();", invalid)
        retry = update.index("TryBind();", invalid)
        self.assertLess(cancel, retry)

    def test_transition_routines_capture_and_revalidate_exact_binding(self):
        source = self.source()
        open_signature = "IEnumerator OpenRoutine(NativeMenuBinding binding,"
        close_signature = "IEnumerator CloseRoutine(NativeMenuBinding binding,"
        self.assertIn(open_signature, source)
        self.assertIn(close_signature, source)
        open_body = method_body(source, open_signature)
        close_body = method_body(source, close_signature)
        self.assertIn("binding.Ui.HideMenu(binding.OptionsScreen)", open_body)
        self.assertIn("binding.Ui.ShowMenu(binding.ModsScreen)", open_body)
        self.assertGreaterEqual(
            open_body.count("OpenTransitionStillAvailable(binding, transition)"), 2)
        self.assertIn("binding.Ui.HideMenu(binding.ModsScreen)", close_body)
        self.assertIn("binding.Ui.ShowMenu(binding.OptionsScreen)", close_body)
        self.assertGreaterEqual(close_body.count("TransitionStillCurrent(binding, transition)"), 2)
        for mutable_field in ("_ui.", "_modsScreen", "_menu."):
            self.assertNotIn(mutable_field, open_body)
            self.assertNotIn(mutable_field, close_body)

    def test_open_transition_rechecks_pause_and_readiness_after_each_yield(self):
        source = self.source()
        open_signature = "IEnumerator OpenRoutine(NativeMenuBinding binding,"
        open_body = method_body(source, open_signature)
        gate_call = "OpenTransitionStillAvailable(binding, transition)"
        self.assertGreaterEqual(open_body.count(gate_call), 2)

        hide = open_body.index("binding.Ui.HideMenu(binding.OptionsScreen)")
        first_gate = open_body.index(gate_call, hide)
        open_model = open_body.index("binding.Menu.Open()")
        show = open_body.index("binding.Ui.ShowMenu(binding.ModsScreen)")
        second_gate = open_body.index(gate_call, show)
        self.assertLess(hide, first_gate)
        self.assertLess(first_gate, open_model)
        self.assertLess(open_model, show)
        self.assertLess(show, second_gate)

        gate = method_body(source, "bool OpenTransitionStillAvailable(")
        self.assertIn("TransitionStillCurrent(binding, transition)", gate)
        self.assertIn("binding.Session.IsReady", gate)
        self.assertIn("binding.Ui.uiState == UIState.PAUSED", gate)
        self.assertGreaterEqual(open_body.count("CancelOpenTransition(binding, transition);"), 2)

    def test_teardown_restores_original_navigation_before_destroying_entry(self):
        clear = method_body(self.source(), "void ClearBinding()")
        self.assertIn("WireOptionsNavigation(false);", clear)
        restore = clear.index("WireOptionsNavigation(false);")
        destroy_entry = clear.index("Destroy(_entryRoot)")
        destroy_screen = clear.index("Destroy(_modsScreen.gameObject)")
        self.assertLess(restore, destroy_entry)
        self.assertLess(restore, destroy_screen)

    def test_process_runtime_owns_the_native_presenter_without_touching_transport(self):
        runtime = RUNTIME.read_text(encoding="utf-8")
        self.assertIn("AddComponent<SilksongNativeModsMenu>()", runtime)
        source = self.source()
        self.assertNotIn("DirectDisplay", source)
        self.assertNotIn("DsModsScreen", source)
        self.assertNotIn("DsTouch", source)

    def test_cloned_screens_hide_inherited_buttons_outside_custom_content(self):
        source = self.source()
        for signature in ("void BuildModsScreen(", "void BuildSkinsScreen("):
            build = method_body(source, signature)
            self.assertIn("DisableInheritedButtonsOutsideContent(root, content);", build)
        helper = method_body(source, "static void DisableInheritedButtonsOutsideContent(")
        self.assertIn("!button.transform.IsChildOf(content)", helper)
        self.assertIn("button.gameObject.SetActive(false);", helper)

    def test_mod_rows_use_native_name_and_value_columns(self):
        source = self.source()
        create = method_body(source, "void CreateButton(")
        paint = method_body(source, "void Paint()")
        self.assertIn("_valueLabels.Add", create)
        self.assertIn("CloneColumnText", create)
        self.assertIn("ConfigureColumn", create)
        self.assertIn("_valueLabels[0].text", paint)
        self.assertIn("_valueLabels[1].text", paint)
        self.assertIn("_valueLabels[slot + 2].text", paint)
        self.assertNotIn('"GROUP', paint)

    def test_mods_description_tracks_focus_and_only_errors_override_it(self):
        source = self.source()
        paint = method_body(source, "void Paint()")
        self.assertIn("string FocusDescription()", source)
        describe = method_body(source, "string FocusDescription()")
        self.assertIn("_description.text =", paint)
        self.assertIn("_menu.MessageIsError", paint)
        self.assertIn("descriptor.Description", describe)
        self.assertIn("descriptor.UnavailableReason", describe)
        self.assertNotIn('"STATUS', source)

    def test_skins_description_explains_focus_or_actionable_error(self):
        source = self.source()
        skins = method_body(source, "void PaintSkins()")
        self.assertIn("string SkinFocusDescription()", source)
        describe = method_body(source, "string SkinFocusDescription()")
        self.assertIn('_skinsTitle.text = "SKINS";', skins)
        self.assertIn("_skinsDescription.text =", skins)
        self.assertIn("NativeSkinMenuRowKind.Mode", describe)
        self.assertIn("NativeSkinMenuRowKind.Sprites", describe)
        self.assertIn("NativeSkinMenuRowKind.Skin", describe)
        self.assertIn("NativeSkinMenuRowKind.Back", describe)
        self.assertIn("CreateDescription(", method_body(source, "void BuildModsScreen("))
        self.assertIn("CreateSkinDescription(", method_body(source, "void BuildSkinsScreen("))


if __name__ == "__main__":
    unittest.main()
