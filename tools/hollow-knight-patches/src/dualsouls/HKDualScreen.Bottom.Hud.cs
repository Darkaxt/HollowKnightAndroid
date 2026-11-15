using System;
using System.Collections.Generic;
using UnityEngine;

// [B3] BOTTOM SCREEN — persistent HUD. HK's own Hud Canvas (masks / soul / geo) is re-layered onto hudLayer and
// mirrored by hudCam2 (FrameHudCams) into the shell's header band; also the routed tutorial/dialogue framing.
public partial class HKDualScreen
{
    // Camera ownership is local to the existing gameplay mirror, not a resident-fit certificate.
    // Preserve the complete owned projection/framing tuple before its first gameplay mutation.
    Camera hudCameraStateOwner, hudCameraStateSource;
    Transform hudCameraStateRoot;
    Rect hudCameraStateRect;
    float hudCameraStateAspect, hudCameraStateSize, hudCameraStateNear, hudCameraStateFar;
    bool hudCameraStateOrthographic;
    Vector3 hudCameraStatePosition;
    Quaternion hudCameraStateRotation;
    Matrix4x4 hudCameraStateProjection;

    void RestoreHudCameraState()
    {
        ResetNativeHudView();
        var owner = hudCameraStateOwner;
        if (owner != null)
        {
            // Restore the captured camera itself, NEVER a replacement hudCam2 identity.
            owner.rect = hudCameraStateRect; owner.aspect = hudCameraStateAspect;
            owner.orthographic = hudCameraStateOrthographic; owner.orthographicSize = hudCameraStateSize;
            owner.nearClipPlane = hudCameraStateNear; owner.farClipPlane = hudCameraStateFar;
            owner.transform.position = hudCameraStatePosition; owner.transform.rotation = hudCameraStateRotation;
            // Automatic projection regenerates from the restored inputs. Do not lock it by writing
            // an identical matrix; an actually different owned projection is restored explicitly.
            if (!owner.projectionMatrix.Equals(hudCameraStateProjection)) owner.projectionMatrix = hudCameraStateProjection;
        }
        hudCameraStateOwner = hudCameraStateSource = null; hudCameraStateRoot = null;
    }

    void ValidateHudCameraState(GameCameras cameras)
    {
        if (hudCameraStateOwner == null) return;
        if (hudCameraStateOwner != hudCam2 || cameras == null || hudCameraStateSource != cameras.hudCamera ||
            cameras.hudCanvas == null || hudCameraStateRoot != cameras.hudCanvas.transform)
            RestoreHudCameraState();
    }

    void CaptureHudCameraState(Camera source)
    {
        var cameras = resolvedGameCameras;
        ValidateHudCameraState(cameras);
        if (hudCam2 == null || source == null || cameras == null || cameras.hudCanvas == null) return;
        if (hudCameraStateOwner != null) return;
        hudCameraStateRect = hudCam2.rect; hudCameraStateAspect = hudCam2.aspect;
        hudCameraStateOrthographic = hudCam2.orthographic; hudCameraStateSize = hudCam2.orthographicSize;
        hudCameraStateNear = hudCam2.nearClipPlane; hudCameraStateFar = hudCam2.farClipPlane;
        hudCameraStatePosition = hudCam2.transform.position; hudCameraStateRotation = hudCam2.transform.rotation;
        hudCameraStateProjection = hudCam2.projectionMatrix;
        hudCameraStateSource = source; hudCameraStateRoot = cameras.hudCanvas.transform;
        hudCameraStateOwner = hudCam2;
    }

    // The existing HUD mirror is fitted at pre-cull, after this frame's generated
    // header reservations. Prompt framing remains independently full-panel.
    void FrameHudCams(Camera src, bool popupBlackNow, bool creditShowing)
    {
        if (hudCam2 == null || src == null) { RestoreHudCameraState(); return; }
        bool nativeHidden = HudGlobalHide.IsHidden;
        if (nativeHidden) RestoreHudCameraState();
        else CaptureHudCameraState(src);
        if (nativeHidden) hudCam2.cullingMask = 0;
        else PrepareNativeHudView(popupBlackNow);

        // promptCam: full-frame (100%), same view centre as the main HUD camera. While the opening
        // attribution draws, zoom in by creditScale (its old dedicated-camera treatment, now on this cam).
        // The dialogue zoom is NOT applied here: enlarging blindly cropped the box left/right in
        // v1.0.1. CenterDialogue() does it, where the box's real bounds are known and the zoom can be
        // clamped to keep the whole box (and the speaker name) on screen.
        promptCam.orthographicSize = creditShowing ? src.orthographicSize / Mathf.Max(0.1f, cfg.creditScale)
                                                   : src.orthographicSize;
        promptCam.transform.position = src.transform.position;
        promptCam.transform.rotation = src.transform.rotation;
        CenterDialogue();   // fix5: vertically centre the routed NPC/lore dialogue box (moves promptCam, not the box)
        CenterTutorial();   // fix(1.0.1f) [user]: fit the tutorial/prompt overlay to the bottom panel instead of inheriting the main screen's 1:1 framing
    }

    // fix(1.0.1f) [user: "i need bottom screen more zoomed in and up"]: the tutorial/prompt overlays (focus,
    // soul, charm/journal tutors) are authored for the MAIN screen and promptCam mirrors that framing 1:1, so
    // on the shorter bottom panel they land small and low with a wide empty band above. Frame them the way the
    // dialogue box is framed: fit the drawing content to the panel, then centre it.
    Renderer[] tutRs; Transform tutRsFor; int tutRsFrame = -999;
    void CenterTutorial()
    {
        if (cfg.compTutFit != 1 || loreDialogueOpen || dlgNameRouted || CreditShowing()) return;
        try
        {
            Transform t = null;
            for (int i = 0; i < tutRoots.Count; i++)
            {
                var r = tutRoots[i];
                if (r != null && r.gameObject.activeInHierarchy && AnyRendererDrawing(r)) { t = r; break; }
            }
            if (t == null) return;
            if (tutRsFor != t || tutRs == null || Time.frameCount - tutRsFrame > 30)
            { tutRs = t.GetComponentsInChildren<Renderer>(false); tutRsFor = t; tutRsFrame = Time.frameCount; }

            float baseOrtho = promptCam.orthographicSize;         // = the main camera's size
            float aspect = (float)BOTTOM_W / BOTTOM_H;
            // A full-screen backdrop is not part of the artwork the reader looks at; measuring it would peg
            // the fit at 1:1 and defeat the whole point (the same trap the dialogue frame hit).
            float rejectH = baseOrtho * 1.6f, rejectW = baseOrtho * aspect * 1.9f;
            Bounds b = default; bool have = false; float bestArea = 0f, artCx = float.NaN;
            for (int i = 0; i < tutRs.Length; i++)
            {
                var r = tutRs[i];
                if (r == null || !r.enabled || !r.isVisible) continue;
                var rb = r.bounds;
                if (float.IsNaN(rb.center.y) || rb.size.sqrMagnitude < 1e-4f) continue;
                if (rb.size.y > rejectH || rb.size.x > rejectW) continue;
                if (EffectiveAlpha(r) < 0.08f) continue;          // faded-out leftovers must not drag the frame
                if (cfg.debug == 1 && (Time.frameCount % 180) == 0)
                    Dbg($"HKDS tut + '{r.transform.name}' {rb.size.x:F2}x{rb.size.y:F2} @x{rb.center.x:F2} a={EffectiveAlpha(r):F2}");
                // fix(1.0.1f) [user: "its not on center and slightly right"]: HK ships ALTERNATE prompt lines
                // (keyboard vs gamepad wording) that are not drawn but still report bounds — one of them sits
                // ~3 units to the left, which dragged the measured centre left and pushed everything visible
                // to the right. Take the horizontal centre from the ARTWORK panel (much the largest element,
                // and genuinely centred) and let the union decide size and vertical placement only.
                float area = rb.size.x * rb.size.y;
                if (area > bestArea) { bestArea = area; artCx = rb.center.x; }
                if (!have) { b = rb; have = true; } else b.Encapsulate(rb);
            }
            if (!have) return;

            const float PAD = 1.08f;
            float needHalfW = Mathf.Max(b.extents.x, 1e-4f) * PAD, needHalfH = Mathf.Max(b.extents.y, 1e-4f) * PAD;
            float zoom = Mathf.Max(1f, cfg.compTutScale);
            zoom = Mathf.Min(zoom, (baseOrtho * aspect) / needHalfW);
            zoom = Mathf.Min(zoom, baseOrtho / needHalfH);
            zoom = Mathf.Clamp(zoom, 1f, 4f);
            promptCam.orthographicSize = baseOrtho / zoom;

            var p = promptCam.transform.position;
            float wantY = b.center.y - cfg.compTutY * promptCam.orthographicSize;   // + = content sits higher
            if (float.IsNaN(b.center.x) || float.IsNaN(wantY)) return;
            float cx = float.IsNaN(artCx) ? b.center.x : artCx;
            promptCam.transform.position = new Vector3(cx + cfg.compTutX * promptCam.orthographicSize, wantY, p.z);
            if (cfg.debug == 1 && (Time.frameCount % 90) == 0)
                Dbg($"HKDS tut fit '{t.name}' box={b.size.x:F2}x{b.size.y:F2} -> zoom={zoom:F2}");
        }
        catch (Exception e) { WarnOnce("tut centre", e); }
    }

    // fix5: HK authors the dialogue box near the TOP of the screen; on the bottom panel the user wants it
    // vertically centred. Move promptCam's view centre to the box's rendered centre instead of moving the
    // box — HK's own open/close FSM keeps driving the box untouched, and the fade quad rides the camera.
    Renderer[] dlgRs; Transform dlgRsFor; int dlgRsFrame = -999;

    // fix(1.0.1c) [user: "increase dialog box height so i can increase font size more"]
    // The font could not simply be zoomed further: HK wraps its dialogue at a FIXED rect width, so at
    // compDlgScale 1.25 the text already spanned ~88% of the bottom panel and the fit-clamp refused to go
    // bigger. The lever that actually works is to narrow HK's wrap (compDlgWidth) — the box then occupies
    // less width, the fit-clamp allows a proportionally larger zoom, and the glyphs come out bigger. That
    // alone would cost extra page turns, so the rect is made taller (compDlgHeight) to hold the extra lines,
    // which is the taller box the user asked for. Applied ONCE when the box is first found (never mid-
    // conversation: TMP re-paginates on a rect change and HK's page index would then point at other text).
    Component dlgTmp, dlgTc; RectTransform dlgRt; Vector2 dlgSizeOrig; Vector3 dlgRtPosOrig; bool dlgShaped; float dlgShapedW, dlgShapedH;
    // Everything anchored to the BOTTOM of the box has to travel down with a taller box. That is not just
    // the ornament: HK's page-advance "Arrow" and end-of-conversation "Stop" glyphs sit centred on the same
    // baseline, and leaving them behind split the bottom ornament in half [user screenshot].
    static readonly string[] DLG_BOTTOM_BITS = { "Fleur Bot", "Arrow", "Stop" };
    Transform[] dlgBotBits; Vector3[] dlgBotBitsOrig; float dlgGrewWorld; Transform dlgBackboard; Vector3 dlgBackboardOrigScale, dlgBackboardOrigPos;

    static Vector2 TcSize(Component tc)
    {
        var pi = tc.GetType().GetProperty("size"); if (pi == null) return Vector2.zero;
        var v = pi.GetValue(tc, null); return v is Vector2 ? (Vector2)v : Vector2.zero;
    }
    static void TcSetSize(Component tc, Vector2 v) { tc.GetType().GetProperty("size")?.SetValue(tc, v, null); }

    // How visible the speaker-name card actually is. HK fades it with COLOUR alpha (FadeGroup lerps the TMP
    // color.a on Title Small Main/Sub/Super) and only disables the mesh renderers once it reaches zero, so
    // Renderer.enabled alone cannot answer it — which is why handing the card back the moment the box closed
    // flashed it on the main screen.
    // fix(1.0.1d) [user: "char name not showing"]: HK's own name card cannot be relied on to stay up for a
    // conversation. Its FSM parks in "NPC Wait for Event" but leaves on either NPC TITLE DOWN or NPC CONVO
    // START, so on multi-part conversations the card fades out and the reader loses track of who is talking.
    // Forcing HK's card back on is not safe — "Quick Disappear" clears its Area Event string first, so a
    // reactivated card re-reads an empty key. Instead: remember the name while HK is showing it, and draw OUR
    // OWN copy (cloned from HK's own Title Small Main, so the font and styling are identical) whenever HK's
    // has faded but the dialogue box is still up. Never both at once, so nothing double-draws.
    // fix(1.0.1f) [user: "some name like snail shaman is cropped and only show shaman"]: HK splits a speaker
    // title across THREE TMPs — Title Small Super / Main / Sub, from the <KEY>_SUPER/_MAIN/_SUB entries of the
    // Titles sheet — and renders them at different sizes ("Snail" above "Shaman"). Cloning Main alone dropped
    // the other lines. Clone the whole Title Small group and drive every part.
    static readonly string[] NAME_PARTS = { "Title Small Main", "Title Small Sub", "Title Small Super" };
    Transform dlgNameClone; Component[] dlgNameSrcTmps, dlgNameCloneTmps; Renderer[] dlgNameCloneRs;
    Transform dlgNameSourcesOwner, dlgNameAppliedOwner, dlgNameCloneFor;
    int dlgNameSourceMask, dlgNameSourceRetry, dlgTitleFsmRetry;
    int dlgNameClipUntil = -1, dlgNameClipFrame = -1, dlgNameCloneChildren = -1, dlgNameCloneBindRetry;
    bool dlgNameClipPending, dlgTitleFsmWasBound;
    readonly string[] dlgNameObservedParts = new string[NAME_PARTS.Length];
    string dlgNameObservedText;
    sealed class NameClonePart
    {
        public Component Owner;
        public Renderer Renderer;
        public Func<string> Text;
        public Action<string> SetText;
        public Func<Color> Color;
        public Action<Color> SetColor;
        public Func<bool> Dirty;
        public Action Generate;
        public string Content;
        public int Children = -1;
        public bool Applied, PropertiesDirty;
    }
    NameClonePart[] dlgNameCloneState;
    string[] dlgNameParts = new string[NAME_PARTS.Length];
    string dlgNameStr = "", dlgNameSeen = ""; float dlgRouteT; Vector3 dlgNameSrcLossy = Vector3.one;
    float dlgNameFit = 1f;   // <1 only when a caption is wider than the panel

    static string TmpText(Component c)
    {
        try { var v = TmpProp(c, "text")?.GetValue(c, null); return v as string ?? ""; } catch { return ""; }
    }

    static Component TmpOn(Transform t)
    {
        if (t == null) return null;
        foreach (var c in t.GetComponents<Component>())
            if (IsTextMeshProGraphic(c)) return c;
        return null;
    }

    // The TMPs on HK's card, in NAME_PARTS order (missing parts stay null).
    // fix(1.0.1f) [user: "when talk to snail shaman first time, it show ancestral mound"]: Title Small is the
    // SAME card HK uses for area titles, so on entering a room it is left holding the area name. HK tells the
    // two apart with a bool on the card's own FSM — "NPC Title", set true by the NPC's Conversation Control
    // before it publishes the speaker key — so read that instead of trusting whatever text is loaded.
    PlayMakerFSM dlgTitleFsm; bool dlgTitleFsmSearched;
    bool NpcTitleActive()
    {
        BindNameSources();
        if (dlgTitleFsmWasBound && dlgTitleFsm == null)
        { dlgTitleFsmSearched = false; dlgTitleFsmWasBound = false; }
        if (dlgTitleFsmSearched && !dlgTitleFsmWasBound && Time.frameCount >= dlgTitleFsmRetry) dlgTitleFsmSearched = false;
        if (!dlgTitleFsmSearched && dlgNameT != null)
        {
            dlgTitleFsmSearched = true; dlgTitleFsmRetry = Time.frameCount + 30;
            var root = dlgNameT.parent != null ? dlgNameT.parent : dlgNameT;
            foreach (var f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f != null && f.FsmName == "Area Title Control") { dlgTitleFsm = f; dlgTitleFsmWasBound = true; break; }
        }
        if (dlgTitleFsm == null) return true;   // FSM not found: fall back to accepting the text
        try { var v = dlgTitleFsm.FsmVariables.GetFsmBool("NPC Title"); return v == null || v.Value; }
        catch { return true; }
    }

    void BindNameSources()
    {
        bool changed = dlgNameSourcesOwner != dlgNameT ||
            (dlgNameSrcTmps != null && dlgNameSourceMask != (1 << NAME_PARTS.Length) - 1 && Time.frameCount >= dlgNameSourceRetry);
        for (int i = 0; !changed && dlgNameSrcTmps != null && i < dlgNameSrcTmps.Length; i++)
            changed = (dlgNameSourceMask & (1 << i)) != 0 && dlgNameSrcTmps[i] == null;
        if (changed || dlgNameT == null)
        {
            dlgNameSourcesOwner = dlgNameT; dlgNameSrcTmps = null; dlgNameSourceMask = 0; dlgNameObservedText = null;
            dlgTitleFsm = null; dlgTitleFsmSearched = dlgTitleFsmWasBound = false;
        }
        if (dlgNameSrcTmps != null || dlgNameT == null) return;
        dlgNameSrcTmps = new Component[NAME_PARTS.Length]; dlgNameSourceRetry = Time.frameCount + 30;
        for (int i = 0; i < NAME_PARTS.Length; i++)
        {
            dlgNameSrcTmps[i] = TmpOn(FindDeep(dlgNameT, NAME_PARTS[i]));
            if (dlgNameSrcTmps[i] != null) dlgNameSourceMask |= 1 << i;
        }
    }

    // Everything the card is currently displaying, joined — used both to detect a new speaker and to log.
    string NameCardText()
    {
        BindNameSources();
        if (dlgNameSrcTmps == null) return "";
        bool changed = dlgNameObservedText == null;
        for (int i = 0; i < dlgNameSrcTmps.Length; i++)
        {
            var text = dlgNameSrcTmps[i] != null ? TmpText(dlgNameSrcTmps[i]) : "";
            changed |= dlgNameObservedParts[i] != text; dlgNameObservedParts[i] = text;
        }
        if (changed)
        {
            string joined = "";
            for (int i = 0; i < dlgNameObservedParts.Length; i++)
                if (!string.IsNullOrEmpty(dlgNameObservedParts[i])) joined += (joined.Length > 0 ? " " : "") + dlgNameObservedParts[i];
            dlgNameObservedText = joined;
        }
        return dlgNameObservedText;
    }

    void EnsureNameClone()
    {
        if (dlgNameT == null || dlgBoxT == null) return;
        if (dlgNameClone != null && dlgNameCloneFor == dlgNameT) return;
        if (dlgNameClone != null) Destroy(dlgNameClone.gameObject);
        dlgNameClone = null; dlgNameCloneTmps = null; dlgNameCloneRs = null;
        dlgNameCloneState = null; dlgNameAppliedOwner = null;
        dlgNameClipPending = false; dlgNameClipUntil = dlgNameClipFrame = -1;
        try
        {
            BindNameSources();
            dlgNameSrcLossy = dlgNameT.lossyScale;
            var go = Instantiate(dlgNameT.gameObject, dlgBoxT);   // the WHOLE group: Super + Main + Sub, laid out as HK authored it
            go.name = "F_DlgName";
            SanitizeDetachedTmpClone(go);   // no FadeGroup/FSM/legacy clip driver on our detached copy
            SetLayerRecursive(go.transform, tutLayer);
            go.SetActive(true);
            dlgNameClone = go.transform; dlgNameCloneFor = dlgNameT;
            dlgNameCloneTmps = new Component[NAME_PARTS.Length];
            dlgNameCloneRs = new Renderer[NAME_PARTS.Length];
            for (int i = 0; i < NAME_PARTS.Length; i++)
            {
                var t = FindDeep(dlgNameClone, NAME_PARTS[i]);
                if (t == null) continue;
                t.gameObject.SetActive(true);
                dlgNameCloneTmps[i] = TmpOn(t);
                dlgNameCloneRs[i] = t.GetComponent<Renderer>();
            }
            ApplyNameCloneScale();
            SetNameClone(false);
            if (cfg.debug == 1) Dbg("HKDS dlg name clone built (Super/Main/Sub)");
        }
        catch (Exception e) { WarnOnce("dlg name clone", e); }
    }

    // The clone hangs off the dialogue box, whose scale HK animates while it opens, so a one-shot localScale
    // computed at clone time can be badly wrong by the time the animation settles. Re-assert it every frame,
    // with compDlgNameScale on top so the label can be sized to taste [user: "name is too big"].
    void ApplyNameCloneScale()
    {
        if (dlgNameClone == null || dlgNameClone.parent == null) return;
        var ps = dlgNameClone.parent.lossyScale;
        if (Mathf.Abs(ps.x) < 1e-6f || Mathf.Abs(ps.y) < 1e-6f || Mathf.Abs(ps.z) < 1e-6f) return;
        float k = Mathf.Clamp(cfg.compDlgNameScale, 0.3f, 2f) * Mathf.Clamp(dlgNameFit, 0.3f, 1f);
        var want = new Vector3(dlgNameSrcLossy.x / ps.x, dlgNameSrcLossy.y / ps.y, dlgNameSrcLossy.z / ps.z) * k;
        if ((dlgNameClone.localScale - want).sqrMagnitude > 1e-8f) dlgNameClone.localScale = want;
    }

    // Show or hide our label. A blank TMP reports its whole container rect as bounds, which would poison both
    // the placement and the zoom reserve — so an empty part has its renderer switched off, not just its text.
    static NameClonePart BindNameClonePart(Component c, Renderer renderer)
    {
        var text = NativeHudGetter<string>(c, "text");
        var setter = TmpProp(c, "text")?.GetSetMethod();
        var generate = c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes);
        if (text == null || setter == null || generate == null) throw new MissingMethodException("Native speaker TMP text/generation methods unavailable");
        var colorSetter = TmpProp(c, "color")?.GetSetMethod();
        return new NameClonePart
        {
            Owner = c, Renderer = renderer, Text = text,
            SetText = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), c, setter),
            Generate = (Action)Delegate.CreateDelegate(typeof(Action), c, generate),
            Dirty = NativeHudGetter<bool>(c, "havePropertiesChanged"),
            Color = NativeHudGetter<Color>(c, "color"),
            SetColor = colorSetter != null ? (Action<Color>)Delegate.CreateDelegate(typeof(Action<Color>), c, colorSetter) : null
        };
    }

    void SetNameClone(bool show)
    {
        if (dlgNameCloneTmps == null || dlgNameClone == null) return;
        if (dlgNameAppliedOwner != dlgNameClone || dlgNameCloneState == null || dlgNameCloneState.Length != dlgNameCloneTmps.Length)
        {
            dlgNameAppliedOwner = dlgNameClone;
            dlgNameCloneState = new NameClonePart[dlgNameCloneTmps.Length];
            dlgNameClipPending = false; dlgNameClipUntil = dlgNameClipFrame = -1; dlgNameCloneChildren = -1;
        }
        if (dlgNameCloneChildren != dlgNameClone.childCount)
        {
            dlgNameCloneChildren = dlgNameClone.childCount; dlgNameCloneBindRetry = 0;
            dlgNameClipPending = true; dlgNameClipUntil = Time.frameCount + 2; dlgNameClipFrame = -1;
        }
        bool missing = false, retired = false;
        for (int i = 0; i < dlgNameCloneTmps.Length; i++)
        {
            if (dlgNameCloneTmps[i] != null && dlgNameCloneRs[i] != null) continue;
            missing = true;
            var old = dlgNameCloneState[i];
            if (old != null && (old.Owner == null || (!ReferenceEquals(old.Renderer, null) && old.Renderer == null)))
            { retired = true; dlgNameCloneState[i] = null; }
        }
        if (missing && (retired || Time.frameCount >= dlgNameCloneBindRetry))
        {
            dlgNameCloneBindRetry = Time.frameCount + 30;
            for (int i = 0; i < dlgNameCloneTmps.Length; i++)
            {
                if (dlgNameCloneTmps[i] == null) dlgNameCloneTmps[i] = TmpOn(FindDeep(dlgNameClone, NAME_PARTS[i]));
                if (dlgNameCloneRs[i] == null && dlgNameCloneTmps[i] != null) dlgNameCloneRs[i] = dlgNameCloneTmps[i].GetComponent<Renderer>();
            }
        }
        for (int i = 0; i < dlgNameCloneTmps.Length; i++)
        {
            var c = dlgNameCloneTmps[i];
            if (c == null) { dlgNameCloneState[i] = null; continue; }
            string text = show && dlgNameParts != null && i < dlgNameParts.Length ? (dlgNameParts[i] ?? "") : "";
            NameClonePart part = dlgNameCloneState[i];
            try
            {
                var renderer = dlgNameCloneRs[i];
                if (part == null || part.Owner != c || part.Renderer != renderer)
                    dlgNameCloneState[i] = part = BindNameClonePart(c, renderer);
                if (part.Children != c.transform.childCount)
                {
                    part.Children = c.transform.childCount;
                    dlgNameClipPending = true; dlgNameClipUntil = Time.frameCount + 2; dlgNameClipFrame = -1;
                }
                bool dirty = show && part.Dirty != null && part.Dirty();
                bool contentChanged = part.Content != text || part.Text() != text;
                if (!part.Applied || contentChanged || (dirty && !part.PropertiesDirty))
                {
                    part.Applied = false;
                    if (part.Text() != text) part.SetText(text);
                    part.Generate();
                    if (part.Text() != text) throw new InvalidOperationException("Native speaker TMP changed during generation");
                    part.Content = text; part.Applied = true;
                    dlgNameClipPending = true; dlgNameClipUntil = Time.frameCount + 2; dlgNameClipFrame = -1;
                    dirty = part.Dirty != null && part.Dirty();
                }
                part.PropertiesDirty = dirty;
                if (show && part.Color != null && part.SetColor != null)
                {
                    var color = part.Color();
                    if (color.a < .999f) { color.a = 1f; part.SetColor(color); }
                }
            }
            catch (Exception e)
            {
                if (part != null) part.Applied = false;
                WarnOnce("dlg name apply", e);
            }
        }
        // Generation can add fallback submeshes during the following LateUpdate.
        // Keep only this event-bounded settle window, plus retry after a failed clip write.
        if ((dlgNameClipPending || Time.frameCount <= dlgNameClipUntil) && dlgNameClipFrame != Time.frameCount)
        {
            try
            {
                NeutralizeDetachedTmpClip(dlgNameClone.gameObject);
                dlgNameClipPending = false; dlgNameClipFrame = Time.frameCount;
            }
            catch (Exception e) { dlgNameClipPending = true; WarnOnce("dlg name clip", e); }
        }
        for (int i = 0; i < dlgNameCloneRs.Length; i++)
        {
            var renderer = dlgNameCloneRs[i]; if (renderer == null) continue;
            var part = i < dlgNameCloneState.Length ? dlgNameCloneState[i] : null;
            bool enabled = !dlgNameClipPending && part != null && part.Applied && part.Content.Length > 0;
            if (renderer.enabled != enabled) renderer.enabled = enabled;
        }
    }

    // The ornament and page glyphs sit under DialogueBox, whose scale HK ANIMATES as the box opens. Converting
    // the world-space growth into their local space once, at shaping time, therefore baked in a mid-animation
    // scale and left the ornament split [user screenshot]. Re-apply every frame against the CURRENT scale.
    void ApplyBottomBits()
    {
        if (dlgBotBits == null || dlgGrewWorld <= 0f) return;
        for (int i = 0; i < dlgBotBits.Length; i++)
        {
            var t = dlgBotBits[i];
            if (t == null || t.parent == null) continue;
            float ps = Mathf.Abs(t.parent.lossyScale.y);
            if (ps > 1e-5f) t.localPosition = dlgBotBitsOrig[i] + new Vector3(0f, -dlgGrewWorld / ps, 0f);
        }
    }

    void ZeroNameCard()
    {
        if (dlgNameT == null) return;
        try
        {
            foreach (var c in dlgNameT.GetComponentsInChildren<Component>(true))
            {
                if (!IsTextMeshProGraphic(c)) continue;
                var pi = TmpProp(c, "color");
                if (pi == null) continue;
                var v = pi.GetValue(c, null);
                if (v is Color) { var col = (Color)v; col.a = 0f; pi.SetValue(c, col, null); }
            }
        }
        catch { }
    }

    // The clone hangs off the dialogue box, whose scale HK animates while it opens, so a one-shot localScale
    // computed at clone time can be badly wrong by the time the animation settles — big enough to hide the
    // label and to inflate the name reserve that caps the text size. Re-assert it every frame.
    float NameCardAlpha()
    {
        if (dlgNameT == null || !dlgNameT.gameObject.activeInHierarchy) return 0f;
        float max = 0f;
        try
        {
            foreach (var r in dlgNameT.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var sr = r as SpriteRenderer;
                if (sr != null) { max = Mathf.Max(max, sr.color.a); if (max > 0.99f) break; continue; }
                float a = -1f;
                foreach (var c in r.GetComponents<Component>())
                {
                    if (!IsTextMeshProGraphic(c)) continue;
                    var pi = TmpProp(c, "color");
                    if (pi != null) { var v = pi.GetValue(c, null); if (v is Color) a = ((Color)v).a; }
                    break;
                }
                if (a < 0f) { var m = r.sharedMaterial; if (m != null && m.HasProperty("_Color")) a = m.color.a; }
                max = Mathf.Max(max, a < 0f ? 1f : a);
                if (max > 0.99f) break;
            }
        }
        catch { return 1f; }
        return max;
    }

    void ShapeDialogue()
    {
        if (dlgBoxT == null) return;
        if (cfg.compDlgCenter != 1) { if (dlgShaped) RestoreDialogueShape(); return; }
        float wS = Mathf.Clamp(cfg.compDlgWidth, 0.4f, 1f);
        float hS = Mathf.Clamp(cfg.compDlgHeight, 1f, 2.5f);
        // re-shape only when the dials actually move (hkds_layout.json hot-reloads, so these can be tuned live)
        if (dlgShaped && Mathf.Approximately(dlgShapedW, wS) && Mathf.Approximately(dlgShapedH, hS)) return;
        try
        {
            if (dlgRt == null)
            {
                // HK's dialogue text is the TMP that carries the DialogueBox script (its siblings are the
                // shop/YN widgets, which must not be touched).
                foreach (var mb in dlgBoxT.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb.GetType().Name != "DialogueBox") continue;
                    foreach (var c in mb.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        var n = c.GetType().Name;
                        if (IsTextMeshProGraphic(c)) dlgTmp = c;
                        else if (n == "TextContainer") dlgTc = c;
                    }
                    if (dlgTmp != null) { dlgRt = mb.transform as RectTransform; break; }
                }
                // fix(1.0.1d): HK ships TMProOld, where a TextContainer component — not the RectTransform —
                // is the authority on the text area (it keeps its own m_rect and re-applies it). Writing
                // sizeDelta directly therefore did NOT change the wrap, while the position compensation I
                // paired with it still moved the block, so the whole box left the view [user: "bottom screen
                // dialog and name hidden ... blipping then hidden"]. Drive the container instead, and if
                // there is no container, shape nothing at all rather than guess.
                if (dlgRt == null || dlgTc == null) { dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS; return; }
                dlgSizeOrig = TcSize(dlgTc); dlgRtPosOrig = dlgRt.localPosition;
                dlgBotBits = new Transform[DLG_BOTTOM_BITS.Length]; dlgBotBitsOrig = new Vector3[DLG_BOTTOM_BITS.Length];
                for (int i = 0; i < DLG_BOTTOM_BITS.Length; i++)
                {
                    dlgBotBits[i] = FindDeep(dlgBoxT, DLG_BOTTOM_BITS[i]);
                    if (dlgBotBits[i] != null) dlgBotBitsOrig[i] = dlgBotBits[i].localPosition;
                }
                dlgBackboard = FindDeep(dlgBoxT, "backboard");
                if (dlgBackboard != null) { dlgBackboardOrigScale = dlgBackboard.localScale; dlgBackboardOrigPos = dlgBackboard.localPosition; }
                if (cfg.debug == 1) Dbg($"HKDS dlg container size {dlgSizeOrig} bits={(dlgBotBits[0] != null)}/{(dlgBotBits[1] != null)}/{(dlgBotBits[2] != null)} backboard={(dlgBackboard != null)}");
                if (dlgSizeOrig.x < 1e-4f || dlgSizeOrig.y < 1e-4f) { dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS; return; }
            }

            var want = new Vector2(dlgSizeOrig.x * wS, dlgSizeOrig.y * hS);
            if ((TcSize(dlgTc) - want).sqrMagnitude > 1e-5f)
            {
                // Measure in the rect's OWN local space, never world space: HK's "Box Up" FSM is animating
                // this box's position/scale while it opens, so a world-space before/after would latch a delta
                // from mid-animation and keep it forever. Keep the top edge and the horizontal centre where
                // HK authored them, so the first line still sits under the top ornament.
                var r0 = dlgRt.rect;
                float pTop0 = dlgRtPosOrig.y + r0.yMax, pBot0 = dlgRtPosOrig.y + r0.yMin, pCx0 = dlgRtPosOrig.x + r0.center.x;

                TcSetSize(dlgTc, want);
                try { dlgTmp?.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(dlgTmp, null); } catch { }

                var r1 = dlgRt.rect;
                var np = new Vector3(pCx0 - r1.center.x, pTop0 - r1.yMax, dlgRtPosOrig.z);
                dlgRt.localPosition = np;

                float grewParent = Mathf.Max(0f, pBot0 - (np.y + r1.yMin));      // how much lower the block now reaches
                float grewWorld = grewParent * (dlgRt.parent != null ? Mathf.Abs(dlgRt.parent.lossyScale.y) : 1f);
                dlgGrewWorld = grewWorld;   // applied per frame by ApplyBottomBits (parents are scale-animated)
                if (dlgBackboard != null && dlgBackboard.parent != null && dlgSizeOrig.y > 1e-5f)
                {   // the dark panel behind the text (invisible under the black-background tweak) grows with it
                    var sc = dlgBackboardOrigScale; sc.y *= want.y / dlgSizeOrig.y; dlgBackboard.localScale = sc;
                    float ps = Mathf.Abs(dlgBackboard.parent.lossyScale.y);
                    if (ps > 1e-5f) dlgBackboard.localPosition = dlgBackboardOrigPos + new Vector3(0f, -grewWorld * 0.5f / ps, 0f);
                }
                if (cfg.debug == 1) Dbg($"HKDS dlg shaped -> {want} grewWorld={grewWorld:F3}");
            }
            dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS;
        }
        catch (Exception e) { dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS; WarnOnce("dlg shape", e); }
    }

    // Hand HK's box back exactly as authored (used if the feature is switched off at runtime).
    void RestoreDialogueShape()
    {
        try { RestoreDialogueShapeOrThrow(); }
        catch (Exception e) { WarnOnce("dialogue shape restore", e); }
    }

    // Failure-propagating form used by transport shutdown. The shaped-state
    // ownership flag clears only after every mutable reference is restored.
    void RestoreDialogueShapeOrThrow()
    {
        if (dlgTc != null && dlgSizeOrig.sqrMagnitude > 1e-5f) { TcSetSize(dlgTc, dlgSizeOrig); if (dlgRt != null) dlgRt.localPosition = dlgRtPosOrig; }
        for (int i = 0; dlgBotBits != null && i < dlgBotBits.Length; i++)
            if (dlgBotBits[i] != null) dlgBotBits[i].localPosition = dlgBotBitsOrig[i];
        if (dlgBackboard != null) { dlgBackboard.localScale = dlgBackboardOrigScale; dlgBackboard.localPosition = dlgBackboardOrigPos; }
        dlgShaped = false;
    }

    // fix5: HK authors the dialogue box near the TOP of the screen; on the bottom panel the user wants it
    // vertically centred. Move promptCam's view centre to the box's rendered centre instead of moving the
    // box — HK's own open/close FSM keeps driving the box untouched, and the fade quad rides the camera.
    void CenterDialogue()
    {
        // fix(1.0.1d) [user: "bottom screen dialog and name hidden ... blipping then hidden"]: this gate was
        // widened to `|| dlgNameRouted` on the belief that NPC talk did not set loreDialogueOpen. It does —
        // RouteLoreDialogue sets it for ANY box that is up. The widened gate meant that while the name card
        // was being held on the bottom during its fade-out, this kept re-framing promptCam onto a box that
        // had already closed, so the whole bottom panel went black. Frame only while the box is genuinely up.
        if (!loreDialogueOpen || cfg.compDlgCenter != 1 || dlgBoxT == null) return;
        try
        {
            ShapeDialogue();
            ApplyBottomBits();
            ApplyNameCloneScale();
            if (dlgRsFor != dlgBoxT || dlgRs == null || Time.frameCount - dlgRsFrame > 30)
            { dlgRs = dlgBoxT.GetComponentsInChildren<Renderer>(true); dlgRsFor = dlgBoxT; dlgRsFrame = Time.frameCount; }   // refresh: TMP submeshes/arrow can appear after open
            Bounds b = default; bool have = false;
            // fix(1.0.1e) [user: "text need larger"]: something under DialogueManager reports PIXEL-scale
            // bounds (measured 3840x2915 world units against a box that is really ~13.5x5.3 — a full-screen
            // canvas element). Encapsulating it made the "box" enormous, so the fit-zoom computed 0.01 and was
            // clamped to its 1.0 floor: the text could never grow, whatever the dials said. Anchor the frame
            // on the TEXT RECT, whose size we set ourselves and therefore trust, and admit only renderers of a
            // sane size next to it (the ornaments and page glyphs) — everything else is not part of the box.
            float maxW = float.MaxValue, maxH = float.MaxValue, rectHalfW = 0f, rectMinX = float.NaN;
            if (dlgRt != null)
            {
                var wc = new Vector3[4]; dlgRt.GetWorldCorners(wc);
                var rb0 = new Bounds(wc[0], Vector3.zero); for (int i = 1; i < 4; i++) rb0.Encapsulate(wc[i]);
                if (!float.IsNaN(rb0.size.x) && rb0.size.x > 1e-4f)
                {
                    maxW = rb0.size.x * 4f; maxH = Mathf.Max(rb0.size.y, 1e-3f) * 4f;
                    rectHalfW = rb0.extents.x;   // stable width reference for the zoom (glyph width varies per page)
                    rectMinX = rb0.min.x;        // the TEXT's left edge — what the name should line up with
                }
            }
            for (int i = 0; i < dlgRs.Length; i++)
            {
                var r = dlgRs[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                // the dark backboard plate is bigger than the visible box and would throttle the zoom
                if (dlgBackboard != null && r.transform == dlgBackboard) continue;
                // fix(1.0.1e) [user: "it pushed down screen because char name is popping"]: our name label is
                // parented to the box, so it lands in the box's own renderer list. Measuring it made the frame
                // grow UPWARD the moment the name appeared — pushing the box down the panel and jumping the
                // zoom as the name popped in and out. The name is positioned against this frame, so it can
                // never be part of it.
                if (dlgNameClone != null && (r.transform == dlgNameClone || r.transform.IsChildOf(dlgNameClone))) continue;
                // fix(1.0.1e) [user: "instead of zooming based on text, how about based on fleur dialog that
                // consistent"]: the glyph mesh changes size with every page (a short line measured 11.3 wide,
                // a long one 13.3), which re-zoomed and re-centred the box as the reader turned pages. The
                // ornaments are fixed in size and position, so they define a frame that never moves; the text
                // lives inside them, and its wrap width is bounded by the rect used for the width reserve.
                if (dlgRt != null && r.transform == dlgRt) continue;
                // fix(1.0.1e) [user: "why its pulsing, isnt bottom center ornament only that pulsing"]: HK
                // ANIMATES the page-advance glyph, so measuring it made the frame breathe — and the zoom is
                // derived from the frame, so the entire box pulsed with it. The glyphs still travel down with
                // a taller box (ApplyBottomBits); they just must not define its size.
                if (dlgBotBits != null && (r.transform == dlgBotBits[1] || r.transform == dlgBotBits[2])) continue;
                var rb = r.bounds;
                if (float.IsNaN(rb.center.y) || float.IsInfinity(rb.center.y) || rb.size.sqrMagnitude < 1e-4f) continue;
                if (rb.size.x > maxW || rb.size.y > maxH)
                {
                    if (cfg.debug == 1 && (Time.frameCount % 180) == 0)
                        Dbg($"HKDS dlg frame: ignoring out-of-scale '{r.transform.name}' {rb.size.x:F0}x{rb.size.y:F0}");
                    continue;
                }
                if (cfg.debug == 1 && (Time.frameCount % 180) == 0)
                    Dbg($"HKDS dlg frame: + '{r.transform.name}' {rb.size.x:F2}x{rb.size.y:F2} @y{rb.center.y:F2}");
                if (!have) { b = rb; have = true; } else b.Encapsulate(rb);
            }
            if (!have) return;
            // fix(1.0.1b) [user: "dialog is cropped left and right"]: v1.0.1 divided the ortho size by
            // compDlgScale with no regard for how wide the box actually is, so the enlarged box ran off
            // both edges. Enlarge only as far as the box still FITS, and frame the camera on the box
            // itself (it is not centred on the main camera's x, which is why the left side clipped worst).
            float aspect = (float)BOTTOM_W / BOTTOM_H;
            float baseOrtho = promptCam.orthographicSize;          // = the main camera's size
            // reserve room above the box for the speaker-name card
            float nameH = 0f, nameHalfW = 0f;
            if (dlgNameRouted && dlgNameT != null)
            {
                try
                {
                    // fix(1.0.1e): measure only the label that is ACTUALLY on screen. An empty TMP falls back
                    // to reporting its whole container rect — our clone measured 1341x754 while blank — and
                    // that was feeding the width reserve, collapsing the fit-zoom to its floor. Both axes are
                    // now capped against the box: the speaker's name must never dictate the box's zoom.
                    var m = dlgNameClone;
                    if (m != null)
                    {
                        var nb = ItemBounds(m);
                        if (nb.size.y > 1e-4f) { nameH = nb.size.y; nameHalfW = nb.extents.x; }
                    }
                    nameH = Mathf.Min(nameH, b.size.y * 0.25f);
                    nameHalfW = Mathf.Min(nameHalfW, b.extents.x);
                }
                catch { nameH = 0f; nameHalfW = 0f; }
            }
            const float PAD = 1.03f;                               // 3% breathing room at the edges
            float needHalfW = Mathf.Max(Mathf.Max(b.extents.x, nameHalfW), rectHalfW) * PAD;
            float needHalfH = (b.extents.y + nameH * 1.1f) * PAD;  // box + name stacked above it
            float zoom = Mathf.Max(1f, cfg.compDlgScale);
            if (needHalfW > 1e-4f) zoom = Mathf.Min(zoom, (baseOrtho * aspect) / needHalfW);
            if (needHalfH > 1e-4f) zoom = Mathf.Min(zoom, baseOrtho / needHalfH);
            zoom = Mathf.Clamp(zoom, 1f, 4f);
            promptCam.orthographicSize = baseOrtho / zoom;
            if (cfg.debug == 1 && (Time.frameCount % 90) == 0)
            {
                float zW = needHalfW > 1e-4f ? (baseOrtho * aspect) / needHalfW : 99f;
                float zH = needHalfH > 1e-4f ? baseOrtho / needHalfH : 99f;
                Dbg($"HKDS dlg fit box={b.size.x:F2}x{b.size.y:F2} nameH={nameH:F2} zW={zW:F2} zH={zH:F2} ceil={cfg.compDlgScale:F2} -> zoom={zoom:F2} name='{dlgNameStr}' hk={NameCardAlpha():F2}");
            }

            // fix(1.0.1d) [user: "it blipping, want to show but second later seems hidden"]: 1.0.1c added a
            // `+ nameAllow * 0.5f` term here to reserve a band above the box. HK does not activate the name
            // card until ~1 s into the conversation, so that term switched on mid-conversation and jumped the
            // bottom camera far enough to carry the box out of view — the box showed, then vanished a second
            // later. The camera stays centred on the box; the name is fitted into the frame below instead.
            float wantY = b.center.y + cfg.compDlgCenterY * promptCam.orthographicSize;
            if (float.IsNaN(wantY) || float.IsInfinity(wantY)) return;
            var p = promptCam.transform.position;
            // centre horizontally on the BOX so it cannot clip on one side
            float wantX = float.IsNaN(b.center.x) ? p.x : b.center.x;
            promptCam.transform.position = new Vector3(wantX, wantY, p.z);
            p = promptCam.transform.position;
            // fix#2(160-fb): park the routed speaker-name card ("Title Small") above the dialogue box.
            // fix(1.0.1c): left-aligned with the box's TEXT and sitting in the reserved band, with a real
            // margin — previously it was clamped hard into the top-left corner of the panel.
            if (dlgNameRouted && dlgNameT != null)
            {
                EnsureNameClone();
                // fix(1.0.1e): ONE display path. HK's card is kept silent and our label always draws — its own
                // placement cannot be measured reliably (empty parts report a full-container fallback size).
                ZeroNameCard();
                var cur = NameCardText();
                // Accept the card's text once it differs from what it held when the box opened (a new speaker),
                // or after a short grace — by then HK's "Set Text Small" has certainly run, so the same speaker
                // talking twice still resolves correctly.
                if (!string.IsNullOrEmpty(cur) && NpcTitleActive() && (cur != dlgNameSeen || Time.unscaledTime - dlgRouteT > 0.35f))
                {
                    dlgNameStr = cur;
                    for (int i = 0; dlgNameSrcTmps != null && i < dlgNameSrcTmps.Length; i++)
                        dlgNameParts[i] = dlgNameObservedParts[i] ?? "";
                }
                SetNameClone(dlgNameStr.Length > 0);
                Transform tgt = dlgNameClone;
                if (tgt != null)
                {
                    var nb = ItemBounds(tgt);
                    float nHalfW = nb.extents.x, nHalfH = nb.extents.y;
                    if (nHalfW < 1e-4f) { nHalfW = nameHalfW; nHalfH = nameH * 0.5f; }
                    float hw = promptCam.orthographicSize * aspect, hh = promptCam.orthographicSize;
                    // A name wider than the panel would be clipped whatever we do about position, so shrink
                    // that one caption to fit (only ever downward — normal names are untouched).
                    float budget = (hw * 2f) * 0.94f;
                    dlgNameFit = (nHalfW * 2f > budget && nHalfW > 1e-4f) ? budget / (nHalfW * 2f) : 1f;
                    if (dlgNameFit < 0.999f) { nHalfW *= dlgNameFit; nHalfH *= dlgNameFit; }
                    // fix(1.0.2) [user: "when on, it need more left, also check if its can be left dynamic to
                    // prevent long name cropped"]: the caption used to align to the dialogue TEXT's left edge,
                    // which sits inset from the panel. Anchor its LEFT EDGE to a fixed margin from the panel
                    // instead — further left, and inherently safe for long names: the start never moves, the
                    // name grows rightward, so it cannot be pushed off the left however long it gets.
                    // compDlgNameLeft is that margin (fraction of the half-view); compDlgNameX still nudges.
                    float leftEdge = p.x - hw + cfg.compDlgNameLeft * hw;
                    float nx = leftEdge + nHalfW + cfg.compDlgNameX * hw;
                    float ny = b.max.y + nHalfH + hh * 0.12f + cfg.compDlgNameY * hh;
                    float minX = p.x - hw + nHalfW + hw * 0.02f, maxX = p.x + hw - nHalfW - hw * 0.02f;
                    float minY = p.y - hh + nHalfH + hh * 0.04f, maxY = p.y + hh - nHalfH - hh * 0.04f;
                    if (maxX > minX) nx = Mathf.Clamp(nx, minX, maxX);
                    if (maxY > minY) ny = Mathf.Clamp(ny, minY, maxY);
                    tgt.position = new Vector3(nx, ny, tgt.position.z);
                }
            }
        }
        catch (Exception e) { WarnOnce("dlg center", e); }
    }
}
