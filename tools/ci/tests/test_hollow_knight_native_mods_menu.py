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

    def test_mod_rows_keep_focus_after_submit_while_options_entry_can_transition(self):
        source = self.source()
        create = method_body(source, "void CreateButton(")
        entry = method_body(source, "void BuildEntry(")
        self.assertIn(
            "source.buttonType = MenuButton.MenuButtonType.Activate;", create
        )
        self.assertNotIn("MenuButtonType.Activate", entry)

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
        self.assertIn("component is Text", source)
        self.assertIn('type.GetProperty("text")', source)
        self.assertNotIn('<Reference Include="Unity.TextMeshPro">', project)
        self.assertNotIn('$(HollowKnightManaged)/Unity.TextMeshPro.dll', project)

    def test_native_button_labels_use_the_full_row_without_wrapping(self):
        source = self.source()
        configure = method_body(source, "void ConfigureSingleLine(float horizontalInset)")
        self.assertIn("uiText.horizontalOverflow = HorizontalWrapMode.Overflow", configure)
        self.assertIn("uiText.verticalOverflow = VerticalWrapMode.Overflow", configure)
        self.assertIn("rect.sizeDelta = new Vector2(-horizontalInset, rect.sizeDelta.y)", configure)
        set_text = method_body(source, "static NativeText SetButtonText(")
        self.assertIn("if (fullRow)", set_text)
        self.assertIn("text.ConfigureSingleLine(ButtonTextHorizontalInset)", set_text)
        create = method_body(source, "void CreateButton(")
        self.assertIn("SetButtonText(wrapper, initialText, fullRow: true)", create)

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
        self.assertIn("CloneSibling", create)
        self.assertIn("ConfigureColumn", create)
        self.assertIn("_valueLabels[0].Text", paint)
        self.assertIn("_valueLabels[1].Text", paint)
        self.assertIn("_valueLabels[slot + 2].Text", paint)
        self.assertNotIn('"GROUP', paint)

    def test_mods_description_tracks_focus_and_only_errors_override_it(self):
        source = self.source()
        paint = method_body(source, "void Paint()")
        self.assertIn("string FocusDescription()", source)
        describe = method_body(source, "string FocusDescription()")
        self.assertIn("_description.Text =", paint)
        self.assertIn("_menu.MessageIsError", paint)
        self.assertIn("descriptor.Description", describe)
        self.assertIn("descriptor.UnavailableReason", describe)
        self.assertNotIn('"STATUS', source)

    def test_skins_description_explains_focus_or_actionable_error(self):
        source = self.source()
        skins = method_body(source, "void PaintSkins()")
        self.assertIn("string SkinFocusDescription()", source)
        describe = method_body(source, "string SkinFocusDescription()")
        self.assertIn('_skinsTitle.Text = "SKINS";', skins)
        self.assertIn("_skinsDescription.Text =", skins)
        self.assertIn("NativeSkinMenuRowKind.Mode", describe)
        self.assertIn("NativeSkinMenuRowKind.Sprites", describe)
        self.assertIn("NativeSkinMenuRowKind.Skin", describe)
        self.assertIn("NativeSkinMenuRowKind.Back", describe)
        self.assertIn("CreateDescription(", method_body(source, "void BuildModsScreen("))
        self.assertIn("CreateSkinDescription(", method_body(source, "void BuildSkinsScreen("))


if __name__ == "__main__":
    unittest.main()
