// HkNativeCharmPresets -- #69 the charm-preset selector inside Hollow Knight's own
// Charms pane (the Start-menu inventory), replacing #56's lower-screen buttons.
//
// Adapted from Clazex's HollowKnight.CharmPreset and Osmi CharmUtil (MIT, see
// NOTICE.md): one icon under the equipped row, drawn with the game's own notch
// art for presets 1-3. Pressing down from the equipped row selects it; left,
// right or confirm switch preset. Switching saves the current charms into the
// preset being left, then equips the next one (an empty preset starts as a copy
// of the current charms). The bench and Godhome-binding checks are the pane's
// own FSM actions, so the game shows its own reminders; EQUIP ANYWHERE works
// because it forces atBench while the inventory is open.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace HollowKnightPatches.Lower
{
    public static class HkNativeCharmPresets
    {
        const string GroupName = "DualSouls Presets";
        static Vector3 Position = new Vector3(0.6f, 1.4f, -3.33f);

        static GameObject _pane, _group;
        static FadeGroup _fade;
        static readonly SpriteRenderer[] _icons = new SpriteRenderer[HkCharmPresets.Count];
        static float _nextCheck;
        static string _error;

        /// <summary>Called every frame; builds once per Charms pane instance.</summary>
        public static void Tick(GameCameras gc)
        {
            if (_group != null) SyncAlpha();
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            if (gc == null || !DsFrame.InGame) return;
            var paneT = gc.transform.Find("HudCamera/Inventory/Charms");
            if (paneT == null) return;
            if (_pane == paneT.gameObject && _group != null) { ShowActive(); return; }
            if (paneT.Find(GroupName) != null) return;
            try { Build(paneT.gameObject); _error = null; }
            catch (Exception e) { _error = e.GetType().Name + ": " + e.Message; Debug.LogWarning("[HkPresets] build failed: " + _error); }
        }

        static void Build(GameObject pane)
        {
            PlayMakerFSM fsm = null;
            foreach (var f in pane.GetComponents<PlayMakerFSM>()) if (f.FsmName == "UI Charms") fsm = f;
            if (fsm == null) throw new InvalidOperationException("no UI Charms FSM");
            var fade = pane.GetComponent<FadeGroup>();
            var sprites = NotchSprites();

            var group = new GameObject(GroupName, typeof(BoxCollider2D), typeof(ConstrainPosition));
            group.transform.SetParent(pane.transform, false);
            group.layer = 5; // UI
            // Pane-local: the pane may be parked off screen while it is built.
            group.transform.localPosition = Position;
            group.GetComponent<BoxCollider2D>().size = new Vector2(1.3f, 1.3f);
            // As CharmPreset: the group's Y is sometimes shifted after entering a save.
            var cp = group.GetComponent<ConstrainPosition>();
            cp.constrainY = false;

            for (int i = 0; i < _icons.Length; i++)
            {
                var go = new GameObject("Preset " + (i + 1), typeof(SpriteRenderer));
                go.transform.SetParent(group.transform, false);
                go.layer = 5;
                var sr = go.GetComponent<SpriteRenderer>();
                sr.sprite = sprites[i];
                sr.color = new Color(1f, 1f, 1f, 0f);
                sr.sortingLayerName = "HUD";
                sr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                sr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                if (fade != null)
                {
                    var list = new List<SpriteRenderer>(fade.spriteRenderers ?? new SpriteRenderer[0]) { sr };
                    fade.spriteRenderers = list.ToArray();
                }
                _icons[i] = sr;
            }

            // Navigation into and out of the selector, as CharmPreset wires it.
            var toPreset = AddState(fsm, "DS To Preset");
            SetActions(toPreset, new FsmStateAction[]
            {
                Clone(Action<SetFsmString>(fsm, "Notch?", 3)),
                Clone(Action<SetFsmString>(fsm, "Notch?", 4)),
                Clone(Action<SetFsmInt>(fsm, "Notch?", 5)),
                Clone(Action<SetFsmInt>(fsm, "Notch?", 6)),
                Clone(Action<SendEventByName>(fsm, "Notch?", 7)),
                Clone(Action<ActivateGameObject>(fsm, "Con Action Down", 0)),
                new SetFsmGameObject { gameObject = new FsmOwnerDefault(), fsmName = "Update Cursor", variableName = "Item", setValue = group, everyFrame = false },
                Clone(Action<SendEventByName>(fsm, "Get Selected", 2)),
                Clone(Action<SendEventByName>(fsm, "Get Selected", 3)),
            });
            ChangeTransition(fsm, "Up", "TO TOP", toPreset.Name);
            ChangeTransition(fsm, "Idle Collection", "UI RS UP", toPreset.Name);
            ChangeTransition(fsm, "Idle Equipped", "UI DOWN", toPreset.Name);
            ChangeTransition(fsm, "Idle Equipped", "UI RS DOWN", toPreset.Name);

            var idle = AddState(fsm, "DS Idle Preset");
            var idleSource = GetState(fsm, "Idle Equipped");
            var idleActions = new List<FsmStateAction>();
            foreach (var a in idleSource.Actions) idleActions.Add(Clone(a));
            SetActions(idle, idleActions.ToArray());
            AddTransition(fsm, toPreset, FsmEvent.Finished.Name, idle.Name);
            AddTransition(fsm, idle, "UI UP", "To Equipment");
            AddTransition(fsm, idle, "UI RS UP", "To Equipment");
            AddTransition(fsm, idle, "UI DOWN", "To Bot");
            AddTransition(fsm, idle, "UI RS DOWN", "To Bot");

            var benchReminder = CopyState(fsm, "Bench Reminder", "DS Bench Reminder Preset");
            ChangeTransition(fsm, benchReminder.Name, FsmEvent.Finished.Name, idle.Name);
            var boundReminder = CopyState(fsm, "Bound Reminder", "DS Bound Reminder Preset");
            ChangeTransition(fsm, boundReminder.Name, FsmEvent.Finished.Name, idle.Name);

            var benchTest = Action<PlayerDataBoolTest>(fsm, "Deactivate UI", 2);
            var boundTest = Action<GGCheckBoundCharms>(fsm, "Deactivate UI", 3);
            var sound = Action<AudioPlayerOneShotSingle>(fsm, "Tween Up", 1);
            AddSwitch(fsm, idle, benchReminder, boundReminder, benchTest, boundTest, sound, "DS Preset Last", -1, "UI LEFT");
            AddSwitch(fsm, idle, benchReminder, boundReminder, benchTest, boundTest, sound, "DS Preset Next", +1, "UI RIGHT", "UI CONFIRM");

            _pane = pane;
            _fade = fade;
            _group = group;
            ShowActive();
        }

        static void AddSwitch(PlayMakerFSM fsm, FsmState idle, FsmState bench, FsmState bound,
                              FsmStateAction benchTest, FsmStateAction boundTest, FsmStateAction sound,
                              string name, int step, params string[] events)
        {
            var s = AddState(fsm, name);
            SetActions(s, new FsmStateAction[] { Clone(boundTest), Clone(benchTest), Clone(sound), new DsInvoke { Method = () => Step(step) } });
            foreach (string e in events) AddTransition(fsm, idle, e, s.Name);
            AddTransition(fsm, s, "NOT BENCH", bench.Name);
            AddTransition(fsm, s, "CHARM BOUND", bound.Name);
            AddTransition(fsm, s, FsmEvent.Finished.Name, idle.Name);
        }

        static void Step(int step)
        {
            int from = HkCharmPresets.Active;
            int to = (from - 1 + step + HkCharmPresets.Count) % HkCharmPresets.Count + 1;
            string result = HkCharmPresets.SwitchTo(to);
            Debug.Log("[HkPresets] " + result);
            ShowActive();
            UpdateCharmUI();
        }

        /// <summary>QA: the selector's state; "set x y" moves it.</summary>
        public static string Command(string[] a)
        {
            if (a.Length >= 4 && a[1] == "pos")
            {
                float x, y;
                if (!float.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                    !float.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return "error preset pos <x> <y>";
                Position = new Vector3(x, y, Position.z);
                if (_group != null) _group.transform.localPosition = Position;
            }
            if (a.Length >= 3 && a[1] == "step")
            {
                int s;
                if (!int.TryParse(a[2], out s)) return "error preset step <+1|-1>";
                Step(s);
            }
            if (a.Length >= 2 && a[1] == "tree" && _pane != null)
            {
                var sb = new System.Text.StringBuilder("pane " + _pane.transform.position.ToString("F2"));
                foreach (Transform c in _pane.transform)
                    sb.Append("\n ").Append(c.name).Append(' ').Append(c.localPosition.ToString("F2")).Append(c.gameObject.activeSelf ? "" : " off");
                return sb.ToString();
            }
            string icons = "";
            for (int i = 0; i < _icons.Length; i++)
                if (_icons[i] != null)
                    icons += " i" + (i + 1) + "=" + (_icons[i].gameObject.activeInHierarchy ? "on" : "off") + "/a" + _icons[i].color.a.ToString("F2") +
                             (_icons[i].sprite != null ? "" : "/nosprite");
            return "native built=" + (_group != null) + " active=" + HkCharmPresets.Active +
                   " local=" + Position.ToString("F2") +
                   " world=" + (_group != null ? _group.transform.position.ToString("F2") : "-") +
                   " pane=" + (_pane != null ? _pane.transform.position.ToString("F2") : "-") + icons +
                   (_error != null ? " error=" + _error : "");
        }

        /// <summary>
        /// Follow the pane's own fade: the first renderer of its FadeGroup that is not ours.
        /// (A renderer appended to the group after it cached its list stays at alpha 0.)
        /// </summary>
        static void SyncAlpha()
        {
            if (_fade == null || _fade.spriteRenderers == null) return;
            SpriteRenderer reference = null;
            foreach (var r in _fade.spriteRenderers)
                if (r != null && Array.IndexOf(_icons, r) < 0) { reference = r; break; }
            if (reference == null) return;
            float alpha = reference.color.a;
            for (int i = 0; i < _icons.Length; i++)
            {
                var sr = _icons[i];
                if (sr == null || Mathf.Approximately(sr.color.a, alpha)) continue;
                var c = sr.color; c.a = alpha; sr.color = c;
            }
        }

        static void ShowActive()
        {
            int active = HkCharmPresets.Active;
            for (int i = 0; i < _icons.Length; i++)
                if (_icons[i] != null && _icons[i].gameObject.activeSelf != (i + 1 == active))
                    _icons[i].gameObject.SetActive(i + 1 == active);
        }

        static Sprite[] NotchSprites()
        {
            string[] names = { "Inv_0013_charm1", "Inv_0012_charm2", "Inv_0011_charm3" };
            var found = new Sprite[names.Length];
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                for (int i = 0; i < names.Length; i++)
                    if (found[i] == null && s != null && s.name == names[i]) found[i] = s;
            for (int i = 0; i < found.Length; i++)
                if (found[i] == null) throw new InvalidOperationException("sprite " + names[i] + " not found");
            return found;
        }

        // ── the Charms pane after a preset changed (Osmi CharmUtil.UpdateCharmUI) ──

        static void UpdateCharmUI()
        {
            var pd = PlayerData.instance;
            var gc = GameCameras.instance;
            if (pd == null || gc == null) return;
            var hudOc = gc.transform.Find("HudCamera/Hud Canvas/Health/OC Backboard");
            var pane = gc.transform.Find("HudCamera/Inventory/Charms");
            var back = pane != null ? pane.Find("Cursor/Back") : null;
            if (pane == null || back == null || !back.gameObject.activeInHierarchy)
            {
                if (hudOc != null) hudOc.gameObject.SetActive(pd.overcharmed);
                return;
            }
            var equipped = pane.Find("Equipped Charms");
            if (equipped == null) return;
            var charms = equipped.Find("Charms");
            if (charms != null) for (int i = charms.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(charms.GetChild(i).gameObject);
            var builder = equipped.GetComponent<BuildEquippedCharms>();
            var build = typeof(BuildEquippedCharms).GetMethod("BuildCharmList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (builder != null && build != null) build.Invoke(builder, null);
            FSMUtility.SendEventToGameObject(equipped.gameObject, "UP", false);

            var textEquipped = equipped.Find("Text Equipped");
            var textOver = equipped.Find("Text Overcharmed");
            var overT = equipped.Find("Notches/Over Indicator");
            PlayMakerFSM overFsm = overT != null ? FSMUtility.LocateFSM(overT.gameObject, "Over Control") : null;
            var ocBack = pane.Find("Overcharm/OC Backboard");
            int overSlots = pd.charmSlotsFilled - pd.charmSlots;
            if (pd.overcharmed)
            {
                if (textOver != null) textOver.gameObject.SetActive(true);
                if (textEquipped != null) textEquipped.gameObject.SetActive(false);
                if (overFsm != null)
                {
                    overFsm.FsmVariables.GetFsmInt("Cost").Value = overSlots;
                    overFsm.FsmVariables.GetFsmBool("Overcharmed").Value = true;
                    overFsm.SendEvent("DISPLAY OVER");
                }
                if (ocBack != null) { var p = ocBack.localPosition; p.y = -3.91f; ocBack.localPosition = p; }
                if (hudOc != null) hudOc.gameObject.SetActive(true);
            }
            else
            {
                if (textOver != null) textOver.gameObject.SetActive(false);
                if (textEquipped != null) { textEquipped.gameObject.SetActive(true); FSMUtility.SendEventToGameObject(textEquipped.gameObject, "UP", false); }
                if (overFsm != null) overFsm.SendEvent("OVERCHARM END");
                if (ocBack != null) { var p = ocBack.localPosition; p.y = -50f; ocBack.localPosition = p; }
                if (hudOc != null) hudOc.gameObject.SetActive(false);
            }
            var collected = pane.Find("Collected Charms");
            if (collected != null) FSMUtility.SendEventToGameObject(collected.gameObject, "UPDATE", true);

            PlayMakerFSM charmsFsm = FSMUtility.LocateFSM(pane.gameObject, "UI Charms");
            var nextDot = equipped.Find("Next Dot");
            if (charmsFsm != null) charmsFsm.FsmVariables.GetFsmBool("Open Slot").Value = overSlots < 0;
            if (nextDot != null) FSMUtility.SendEventToGameObject(nextDot.gameObject, overSlots < 0 ? "NOTCH DEF UP" : "NOTCH DOWN", false);
        }

        // ── PlayMaker editing (as Satchel's FsmUtil) ──

        sealed class DsInvoke : FsmStateAction
        {
            public Action Method;
            public override void OnEnter()
            {
                try { if (Method != null) Method(); }
                catch (Exception e) { Debug.LogWarning("[HkPresets] " + e.Message); }
                Finish();
            }
        }

        static readonly MethodInfo CloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        static T Clone<T>(T action) where T : FsmStateAction
        {
            if (action == null) throw new InvalidOperationException("missing action to clone");
            return (T)CloneMethod.Invoke(action, null);
        }

        static FsmState GetState(PlayMakerFSM fsm, string name)
        {
            foreach (var s in fsm.FsmStates) if (s.Name == name) return s;
            throw new InvalidOperationException("no state " + name);
        }

        static T Action<T>(PlayMakerFSM fsm, string state, int index) where T : FsmStateAction
        {
            var actions = GetState(fsm, state).Actions;
            var a = index < actions.Length ? actions[index] as T : null;
            if (a == null) throw new InvalidOperationException(state + "[" + index + "] is not " + typeof(T).Name);
            return a;
        }

        static FsmState AddState(PlayMakerFSM fsm, string name)
        {
            var state = new FsmState(fsm.Fsm) { Name = name };
            var states = new List<FsmState>(fsm.Fsm.States) { state };
            fsm.Fsm.States = states.ToArray();
            return state;
        }

        static FsmState CopyState(PlayMakerFSM fsm, string from, string name)
        {
            var copy = new FsmState(GetState(fsm, from)) { Name = name };
            foreach (var t in copy.Transitions) t.ToFsmState = GetState(fsm, t.ToState);
            var states = new List<FsmState>(fsm.Fsm.States) { copy };
            fsm.Fsm.States = states.ToArray();
            return copy;
        }

        static void SetActions(FsmState state, FsmStateAction[] actions)
        {
            state.Actions = actions;
            foreach (var a in actions) a.Init(state);
        }

        static void ChangeTransition(PlayMakerFSM fsm, string state, string evt, string to)
        {
            foreach (var t in GetState(fsm, state).Transitions)
                if (t.EventName == evt) { t.ToState = to; t.ToFsmState = GetState(fsm, to); return; }
            throw new InvalidOperationException("no transition " + state + "/" + evt);
        }

        static void AddTransition(PlayMakerFSM fsm, FsmState state, string evt, string to)
        {
            var list = new List<FsmTransition>(state.Transitions)
            {
                new FsmTransition { ToState = to, ToFsmState = GetState(fsm, to), FsmEvent = FsmEvent.GetFsmEvent(evt) }
            };
            state.Transitions = list.ToArray();
        }
    }
}
#endif
