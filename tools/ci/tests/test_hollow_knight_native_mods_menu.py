import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
NATIVE_MENU = ROOT / "tools/hollow-knight-patches/src/mods/HollowKnightNativeModsMenu.cs"
RUNTIME = ROOT / "tools/hollow-knight-patches/src/mods/HollowKnightModsRuntime.cs"
PROJECT = ROOT / "tools/hollow-knight-patches/HollowKnightPatches.csproj"


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


class HollowKnightNativeModsMenuContractTest(unittest.TestCase):
    def source(self) -> str:
        self.assertTrue(NATIVE_MENU.is_file(), "native Hollow Knight Mods menu source is missing")
        return NATIVE_MENU.read_text(encoding="utf-8")

    def test_options_route_uses_exact_typed_native_menu_seams(self):
        source = self.source()
        for seam in (
            "UIManager",
            "optionsMenuScreen",
            "MenuScreen",
            "MenuButton",
            "ShowMenu(binding.ModsScreen)",
            "HideMenu(binding.OptionsScreen)",
        ):
            self.assertIn(seam, source)
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

    def test_open_transition_rechecks_exact_binding_pause_and_readiness(self):
        source = self.source()
        signature = "IEnumerator OpenRoutine(NativeMenuBinding binding, int generation)"
        body = method_body(source, signature)
        gate_call = "OpenTransitionStillAvailable(binding, generation)"
        self.assertGreaterEqual(body.count(gate_call), 2)

        hide = body.index("binding.Ui.HideMenu(binding.OptionsScreen)")
        first_gate = body.index(gate_call, hide)
        open_model = body.index("binding.Menu.Open()")
        show = body.index("binding.Ui.ShowMenu(binding.ModsScreen)")
        second_gate = body.index(gate_call, show)
        self.assertLess(hide, first_gate)
        self.assertLess(first_gate, open_model)
        self.assertLess(open_model, show)
        self.assertLess(show, second_gate)

        gate = method_body(source, "bool OpenTransitionStillAvailable(")
        self.assertIn("BindingIsAlive(binding, generation)", gate)
        self.assertIn("binding.Session.IsReady", gate)
        self.assertIn("binding.Ui.uiState == UIState.PAUSED", gate)

    def test_teardown_restores_original_navigation_before_destroying_entry(self):
        clear = method_body(self.source(), "void ClearBinding()")
        restore = clear.index("RestoreOptionsNavigation();")
        destroy_entry = clear.index("Destroy(_entryRoot)")
        destroy_screen = clear.index("Destroy(_modsScreen.gameObject)")
        self.assertLess(restore, destroy_entry)
        self.assertLess(restore, destroy_screen)

    def test_entry_topology_is_derived_from_typed_options_content(self):
        source = self.source()
        self.assertIn("manager.optionsMenuScreen.content", source)
        self.assertIn("GetComponentsInChildren<MenuButton>(true)", source)
        self.assertIn("FindDirectChild", source)
        self.assertNotIn('transform.Find("Content/', source)

    def test_cloned_native_list_is_removed_before_replacing_content(self):
        source = self.source()
        build = method_body(source, "void BuildModsScreen(")
        self.assertIn("Destroy(oldList)", build)
        self.assertNotIn("oldList.enabled = false", build)
        self.assertLess(build.index("Destroy(oldList)"), build.index("Destroy(child)"))

    def test_cloned_menu_buttons_remain_the_only_selectables(self):
        source = self.source()
        entry = method_body(source, "void BuildEntry(")
        create = method_body(source, "void CreateButton(")
        self.assertIn("_entrySelectable = source;", entry)
        self.assertIn("button.Selectable = source;", create)
        self.assertNotIn("Destroy(source)", entry)
        self.assertNotIn("Destroy(source)", create)
        self.assertIn(
            "class HollowKnightNativeModsEntryButton : MonoBehaviour,", source
        )
        self.assertIn(
            "class HollowKnightNativeModsButton : MonoBehaviour,", source
        )
        self.assertIn("behaviour is MenuButton", source)

    def test_binding_waits_for_ready_authority_and_rebinds_after_loss(self):
        source = self.source()
        bind = method_body(source, "void TryBind()")
        self.assertIn("session == null || !session.IsReady", bind)
        update = method_body(source, "void Update()")
        self.assertIn("if (!binding.Session.IsReady)", update)
        self.assertIn("CancelAndClearBinding();", update[update.index("if (!binding.Session.IsReady)"):])

    def test_options_navigation_is_reapplied_after_native_menu_rewrites(self):
        wire = method_body(self.source(), "void WireOptionsNavigation(bool includeMods)")
        self.assertNotIn("_optionsNavigationIncludesEntry == includeMods", wire)
        self.assertIn("if (!_optionsNavigationIncludesEntry)", wire)
        self.assertIn("SetVertical(_entrySelectable", wire)

    def test_options_entry_keeps_native_spacing_while_mod_rows_fit_full_screen(self):
        bind = method_body(self.source(), "void TryBind()")
        self.assertIn("float optionStep = ResolveOptionStep();", bind)
        self.assertIn("Mathf.Clamp(optionStep, MinimumRowStep, MaximumRowStep)", bind)
        self.assertIn(".Y - optionStep", bind)

    def test_process_runtime_owns_native_presenter_without_touching_transport(self):
        runtime = RUNTIME.read_text(encoding="utf-8")
        self.assertIn("AddComponent<HollowKnightNativeModsMenu>()", runtime)
        source = self.source()
        self.assertNotIn("DirectDisplay", source)
        self.assertNotIn("HkDirectDisplayAdapter", source)

    def test_native_labels_use_runtime_text_contract_without_tmp_reference(self):
        source = self.source()
        project = PROJECT.read_text(encoding="utf-8")
        self.assertNotIn("using TMPro;", source)
        self.assertNotIn("List<TextMeshProUGUI>", source)
        self.assertNotIn("GetComponentInChildren<TextMeshProUGUI>", source)
        self.assertIn('type.Namespace == "TMProOld"', source)
        self.assertIn('type.Namespace == "TMPro"', source)
        self.assertIn('type.GetProperty("text")', source)
        self.assertNotIn('<Reference Include="Unity.TextMeshPro">', project)
        self.assertNotIn('$(HollowKnightManaged)/Unity.TextMeshPro.dll', project)


if __name__ == "__main__":
    unittest.main()
