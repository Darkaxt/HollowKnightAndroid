using System;
using UnityEngine;

// Live-tunable config for the dual-screen runtime (HKDualScreen). Loaded from persistentDataPath/hkds_layout.json by
// JsonUtility and HOT-RELOADED on mtime (adb-push a new file -> applies within ~1/3 s).
//
// JSON CONTRACT: JsonUtility is NAME-keyed and FLAT. Never rename a field or nest them (the user's existing JSON keys
// would be silently dropped and fall back to these defaults); DELETING a field is safe (its JSON key becomes an inert
// unknown key — HKDualScreen logs `HKDS cfg unknown keys` so stale keys can be pruned). Field ORDER is irrelevant to
// JsonUtility, so the class is sectioned by owning module purely for readability.
[Serializable]
class HKLayout
{
    // ==== MAIN — top-screen tweaks, HUD mirror framing, native blit, input bridge ====================================
    public float zoomMul = 0.667f, panX = -4.0f, panY = -0.5f, logoScale = 1.0f;   // hudCam2 (HUD mirror) zoom/pan; bottom menu-logo width
    public float haloAlpha = -1.0f, haloScale = 1.0f;   // hero-light halo (haloAlpha < 0 = leave HK's light untouched)
    public int haloSolid = 0;        // 1 = replace HK's hero light with a soft radial gradient
    public int killIdleGlow = 0;     // 1 = disable the "Idle Glow" around the standing Knight
    public int killDarkness = 0;     // 1 = disable HK's "Darkness Camera" (dark-room lit bubble)
    public int killDonut = 1;        // 1 = disable the Knight's "white_light_donut" ring light
    public int killBlueFlash = 1;    // 1 = soften the fullscreen "Screen Flash(Clone)" lifeblood flash
    public float flashAlpha = 0.35f; // peak opacity of the softened flash (1=opaque wall, 0=removed)
    public int routeHudFx = 1;       // 1 = route the focus-heal "HP Up Particles" to the bottom HUD
    public float healOffX = 4.3f;      // focus-heal particle placement nudge on the bottom (world X)
    public float healOffY = 0.0f;      // ... world Y
    public float healScale = 1.0f;     // ... scale (1 = native)
    public float creditScale = 2.0f;   // opening attribution size on the bottom screen (200%)
    public float dim = 0.08f;        // dimmed main-frame backdrop brightness on the bottom (0=black)
    public int flip = 0;             // native blit: flip the bottom RT vertically
    public int bgFlip = 0;           // native blit: flip the backdrop RT vertically
    public int bgMask = 17;           // backdrop camera cullingMask override (0 = built-in default)
    public int bgBlur = 6;           // backdrop softness: render at 1/bgBlur res -> bilinear upscale
    public int invBtn = 11;          // Joystick1Button that toggles the inventory (-1 = off). HK's own
                                     // openInventory binding doesn't reach the Thor pad and isn't rebindable.
    public int companion = 1;        // 0 = off, 1 = bottom-screen companion (Map/Inventory/Charms) on. Also L3+R3 at runtime.
    public int dualScreen = 1;       // 1 = dual-screen on (HUD/companion on the bottom); 0 = OFF -> HUD back to the top screen, bottom blank
    public float touchAlpha = 0.32f; // virtual-controller opacity (0..1)
    public int swapFaceButtons = 1;            // 1 = swap A<->B and X<->Y (Thor's Nintendo-positioned face buttons)
    public string forceType = "AUTO";          // controller service (P3): AUTO=detect / NINTENDO / XBOX / XBOX_ONE / PS4 / PS5 / NONE
    public string touchControls = "auto";      // P4: auto (touch only when no gamepad) / on / off
    public int westernAccept = 0;              // 1 = Nintendo glyphs but WESTERN menu accept/reject (A=Submit); 0 = HK-native (Switch => Japanese, jump=Cancel)
    public int compHudSync = 1;      // 1 = hide the whole companion when HK fades the HUD (lore tablets/cutscenes); 0 = always show
    public int modsEnabled = 1;      // 1 = the asset-mod/skin engine applies enabled mods at runtime (Mods page in the hub); 0 = master off (originals restored live)
    public int modsSelfTestN = 0;    // debug: bump (with debug=1) to generate+enable the built-in test skin (recolored Knight) — proves the whole mod pipeline in-game

    // ==== B1 LAYERING — the companion's viewport box on the bottom panel ==============================================
    public int compPopupBlack = 1;       // 1 = while a dialogue/tutorial/credit overlay draws on the bottom, also blank the HUD mirror + backdrop (the popup reads on clean black); 0 = old behaviour (overlay over live HUD/backdrop)
    public int compDlgCenter = 1;        // 1 = vertically centre the routed NPC/lore dialogue box on the bottom panel (it is authored near the screen top)
    public float compDlgCenterY = 0.0f;
    public float compDlgScale = 1.28f;    // fix(1.0.1c) [user]: ceiling only — CenterDialogue zooms to this at most, then clamps to what still FITS
    public float compDlgWidth = 0.75f;   // fix(1.0.1c) [user: "increase font size more"]: narrow HK's text wrap so the fit-clamp can zoom further (this is what makes the glyphs bigger)
    public float compDlgHeight = 1.3f;   // fix(1.0.1c) [user: "increase dialog box height"]: taller text block so the narrower wrap does not cost extra page turns
    public float compDlgNameLeft = 0.045f;  // fix(1.0.2) [user]: the caption's LEFT EDGE, as a fraction of the half-view from the panel's left edge — fixed anchor, so long names grow rightward and never crop
    public float compDlgNameX = 0.0f;    // nudge the speaker-name label left(-)/right(+), as a fraction of the half-view
    public float compDlgNameY = 0.0f;    // nudge it down(-)/up(+)
    public int compTutFit = 1;           // fix(1.0.1f) [user]: 1 = fit tutorial/prompt overlays to the bottom panel (they are authored for the main screen and land small + low)
    public float compTutScale = 1.7f;    // ceiling for that fit; the panel-fit clamp decides the rest
    public float compTutX = 0.0f;        // + = overlay sits further right on the panel (fraction of the half-view)
    public float compTutY = 0.10f;       // + = overlay sits higher on the panel (fraction of the half-view)
    public float compDlgNameScale = 0.72f;  // fix(1.0.1f) [user: "name is too big"]: size of the speaker-name label, relative to HK's own card
    public int compDimSync = 1;          // 1 = mirror the MAIN screen's fade (room transitions, death, cutscenes) onto the bottom, dusklight-style; 2 = force 55% (render test)
    public float compDimOutRate = 10.0f;   // bottom fade-OUT speed, alpha/second (10 = fully black in 0.1s — the main snaps fast on room crossings; raise if the bottom trails)
    public float compDimInRate = 4.0f;     // bottom fade-IN speed, alpha/second (~0.25s back to clear, matching the main's fade-in feel)
    public int compBox = 0;              // 1 = draw the companion (map+frame) as its own BOX below the HUD (a sub-rect), not full-screen
    public float compBoxX = 0.03f;       // companion box viewport rect (0..1) — x, y (from bottom), width, height
    public float compBoxY = 0.005f;
    public float compBoxW = 0.96f;
    public float compBoxH = 0.9f;       // height < 1 leaves the top strip for the HUD

    // ==== B2 FRAME / TABS — context-box chrome, tab row, fleurs, separators, per-tab camera ============================
    public int compTabSlide = 1;         // 1 = animate context-tab switches like the MAIN screen's pane carousel (both panes slide horizontally, camera eased)
    public float compTabSlideTime = 0.3f;// tab-slide duration in seconds (easeOutCubic)
    public int compTab = 0;          // startup tab: 0=Map 1=Inventory 2=Charms (a bottom-screen tap overrides it until the config value changes)
    public float compOffX = 0.0f;      // companion placement offset on the bottom (attrCam-view units)
    public float compOffY = 0.0f;
    public int compFrame = 1;        // 1 = draw HK's context-box ornament frame around the map
    public float compFrameScale = 1.0f;// ornament size multiplier (live-tune)
    public float compFrameFit = 1.15f;   // Inventory/Charms: when the frame is on, zoom the pane out by this so it sits INSIDE the frame (Map tab: compMapMargin instead)
    public float compTabScale = 0.6f;    // device-safe tab-label size multiplier (~3.3% of the lower display height)
    public float compTabY = -0.76f;      // tab glyph centre Y (bottom 12% band) — fractions of the half-view; +up
    public float compMapCenterY = 0.0f; // Inventory/Charms: shift the pane UP so it sits inside the frame (Map tab centres itself in the inner rect; not used there)
    public float compTabSpacing = 0.45f;// horizontal spacing between tab labels (live) — 3 tabs centred on the middle one
    public float compBotFleurGap = 0.03f;// SAME gap between the tab text and BOTH fleurs (above + below), ortho fracs
    public int compBotFleurFlip = 0;     // 1 = flip the TOP fleur (#3) upside-down
    public float compBotFleurWScale = 1.0f;   // TOP fleur (#3) width = Charms-tab text width * this (aspect locked)
    public float compBotFleur2WScale = 1.0f; // BOTTOM fleur (#2) width = Charms width * this (< top -> "less length")
    public int compBotFleur2Flip = 0;    // 1 = flip the BOTTOM fleur (#2)
    public float compBotFleurTextH = 0.09f;  // assumed VISIBLE tab-text height (ortho fracs) for symmetric top/bottom spacing
    // Full-width horizontal separators: #5 (credits) between HUD and context box, #6 (game_over) between box and tabs.
    public int compSepTop = 1;           // 1 = show the HUD->context-box separator (#5)   [build-time: needs a frame rebuild to toggle]
    public float compSepTopY = 0.58f;    // its vertical position (ortho fracs; + = up)
    public int compSepBot = 0;           // 1 = show the context-box->tab-bar separator (#6); OFF by design — the tab fleurs close the box   [build-time]
    public float compSepBotY = -0.64f;   // its vertical position (ortho fracs; - = down)
    public float compSepW = 0.98f;       // separator width as a fraction of the full view width (aspect-locked height)

    // ==== B3 HUD STRIP — our added persistent widgets (HK's own masks/soul/geo HUD is mirrored by hudCam2) =============
    public int compAreaName = 1;         // 1 = show the map zone name (top-right, inline with the HUD) on the Map tab   [build-time]
    public float compAreaNameX = 0.92f;  // area-name center X (fraction of half-view, +right)
    public float compAreaNameY = 0.5f;  // area-name center Y (+up, near the top = inline with the HUD)
    public float compAreaNameScale = 1.15f;// area-name label size multiplier
    public int compStats = 1;            // 1 = show an FPS + battery readout on the bottom screen (all tabs)   [build-time]
    public float compStatsX = 0.92f;      // battery readout RIGHT-edge anchor X (bottom-right, inline with the tab row); also anchors the equip row
    public float compFpsX = -0.92f;      // FPS readout LEFT-edge anchor X (bottom-left, inline with the tab row)
    public float compStatsScale = 1.0f;  // stats label size multiplier (bold, a touch bigger than the area name)
    public float compBattScale = 0.4f;  // battery glyph height as a fraction of the readout text height
    public float compBattOffY = 0.4f;   // battery glyph vertical nudge (fraction of text height; + = up) to align it with the text — 0.40 centres it on the fps/level digit line (emulator-verified 2026-08-13)
    public float compBattGap = 0.12f;    // horizontal gap between the battery icon and the level % (fraction of text height); fps<->icon keeps the wider 0.35 so icon+% read as one unit
    public int compEquipRow = 1;         // 1 = show a row of EQUIPPED-CHARM icons across the TOP of the box (every tab)
    public float compEquipRowX = 0.0f;     // equipped-charm row centre X (fraction of half-view)
    public float compEquipRowY = 0.85f;  // equipped-charm row Y (+up, top area where FPS/battery used to sit)
    public float compEquipRowScale = 0.22f; // charm-icon height as a fraction of half-view
    public float compEquipRowGap = 0.3f;   // extra spacing between charm icons (fraction of icon height)
    public int compNoMapMsg = 1;         // fix4: show a subtle grey "Map not acquired yet" label when the zone has no map   [build-time]
    public float compNoMapY = 0.02f;     // fix4: no-map label Y (fraction of half-view, +up; 0 = box centre)
    public float compNoMapScale = 1.8f;  // fix4: no-map label size multiplier

    // ==== B4 INVENTORY TAB ============================================================================================
    public float compPaneZoom = 0.82f;      // Inv/Charm: extra zoom multiplier for the pane fit (>1 = larger content)
    public float compInvOffX = 0.3f;       // Inventory horizontal shift (fraction of ortho; + moves content LEFT)
    public float compFocusDY = 1.0f;    // Inventory: move the Focus spell + its 3 surrounding spells UP by this (world units) so the bottom spell no longer clips the nail-arts row below
    public float compFocusDYGod = 0.3f;  // Focus-cluster lift when the GOD TUNER is owned (Godhome saves): the soul gauge slides above the spells, so the full lift would clip it
    public float compEquipGridDY = 0.0f;    // manual trim on the equipment row (+ = down); 0 = HK-native placement (mirrored from the real pane)

    // ==== B5 SELECT / CONTROL PROMPT — tap-to-select, HK cursor, right-detail fonts, [verb][glyph] line ================
    public int compTouch = 1;            // 1 = poll the bottom-panel touch bridge (tap a tab / item to select)
    public int compTapSelect = 1;        // 1 = tap an Inventory/Charm item to select it -> fills the right-side detail
    public float compTabBandY = 0.85f;   // normalized Y (0=top,1=bottom) where the TAB-tap band starts; taps ABOVE go to item-select. 0.85 = only the bottom tab-row switches tabs (was a too-tall 0.72, which ate lower pane items)
    public int compSelHighlight = 1;     // 1 = draw HK's corner-bracket cursor around the tapped item
    public float compSelInset = -0.2f;  // highlight frame grow(+)/shrink(-) vs item half-extent; negative = corners hug closer
    public float compSelCursorScale = 1.0f; // UNIFORM native size of each corner ornament (no stretch/zoom); tune to match the main menu
    public float compDetailFont = 8.0f;     // right-detail Text Desc fontSize (native ~4 = small/thin); 8 = large/readable (user pref)
    public float compNameFont = 10.0f;      // right-detail Text Name fontSize — item NAME above the desc, larger than the body (0 = use compDetailFont)
    public int compCtrlPrompt = 1;        // 1 = show HK's control-prompt line (glyph + PRESS/HOLD/TAP) for the selected item, between name+desc
    public float compCtrlDX = 0.0f;         // control-prompt glyph X offset from Text Name's X (world units; + = right)
    public float compCtrlDY = 0.0f;         // control-prompt glyph Y offset from the name/desc midpoint (world units; + = up)
    public float compCtrlVerbGap = 0.6f;  // WORLD-unit gap between the verb (PRESS/HOLD/TAP) and the glyph on the line
    public float compCtrlFont = 6.0f;       // verb TMP fontSize for crispness (0 = leave authored)
    public float compCtrlBand = 0.0f;     // control-prompt line sits this far BELOW the item name (world units)
    public float compCtrlGap = 0.4f;      // extra gap between the prompt line and the (pushed-down) description top
    public float compCtrlGlyphDY = 0.0f;    // verb Y offset vs the glyph centre (world units, +up). Verb and glyph are both pinned by RENDERED-BOUNDS CENTRE since the one-shot layout, so 0 = optically inline; this is a fine-tune only
    public float compCtrlPressDY = 0.0f;    // per-verb trim: EXTRA verb Y offset for PRESS (its Y face-button sprite is centred in its cell -> 0)
    public float compCtrlHoldDY = -0.75f; // per-verb trim: HOLD's glyph sprite carries top-heavy padding, so drop the verb to its visible centre
    public float compCtrlTapDY = -0.75f;  // per-verb trim: TAP, same reason (tuned on-device via screenshots; live hot-reload)
    public float compCtrlGlyphSize = 1.0f;// ABSOLUTE glyph height (world units) — fixed so it's identical for every item

    // ==== B6 MAP TAB ==================================================================================================
    public float compZoom = 1.0f;      // Map tab zoom multiplier on top of the full-area fit (>1 zooms in, area stays centred)
    public float compMapMargin = 0.08f;  // Map tab: ONE equal minimum margin (fraction of the box half-height) between the whole area
                                         // (all rooms, revealed or not) and the frame's inner rect (top separator / tab-row fleur / view
                                         // sides); the area is centred in that rect and sized so the binding sides touch this margin
    public int compMapPinch = 1;         // Map tab: 1 = pinch to zoom, one-finger drag to pan (when zoomed), quick tap to reset — dusklight-style
    public float compMapZoomMax = 6.0f;    // Map tab: maximum pinch zoom factor

    // ==== B7 CHARMS TAB — redesign: detail band on TOP (full width), charm grid BELOW (HK's native honeycomb order) ====
    public float compCharmZoom = 1.05f;   // Charms tab camera fill (separate from inventory's compPaneZoom)
    public float compCharmOffY = 0.15f;     // Charms vertical shift (fraction of ortho; + moves content DOWN)
    public int compCharmsRedesign = 1;   // 1 = re-lay the Charms tab: detail band on TOP (full width), charm grid BELOW (full width). 0 = HK's authored grid-left/detail-right
    public float compCharmsDetailBandY = 4.0f;   // redesign: detail-band centre Y, in PITCH units above compRoot (tune via hot-reload)
    public float compCharmsGridTopY = -0.6f;       // redesign: TOP grid-row centre Y, in PITCH units above compRoot (grid grows downward)
    public float compCharmsCellScale = 1.0f;      // redesign: charm-icon cell size multiplier
    public float compCharmsCenterY = -1.3f;       // redesign: view centre Y offset (PITCH units) — shift content down out of the HUD band
    public float compCharmsNameFont = 9.0f;       // redesign: charm NAME font (small; the full-width grid framing makes HK's 12 huge)
    public float compCharmsDescFont = 6.5f;       // redesign: charm DESCRIPTION font
    public float compCharmsDescWidth = 15.5f;      // redesign: description word-wrap width (world units)
    public float compCharmsZoom = 1.0f;           // redesign: extra zoom on the whole charms view (>1 = closer)
    public float compCharmsNameX = -0.55f;        // redesign: NAME + DESCRIPTION shared left edge X (fraction of grid width, from centre)
    public float compCharmsNameDY = 1.3f;        // redesign: NAME Y offset above the band centre (pitch units)
    public float compCharmsNotchDY = 0.0f;          // redesign: cost-pip Y trim vs the name line (pitch units; pips sit INLINE right after the name)
    public float compCharmsGridPct = 0.82f;       // redesign: grid width as a fraction of the view (user: 80%)
    public float compCharmsIconScale = 0.82f;      // redesign: scale the charm ICONS smaller (1 = HK native slot size)
    public float compCharmsLineSpacing = -25.0f;    // redesign: DESCRIPTION line/paragraph spacing (TMP units; negative tightens the gaps)
    public float compCharmsDimEquip = 0.4f;       // redesign: EQUIPPED charms rendered at this brightness (darkened like the main screen)
    public float compCharmsNotchScale = 1.5f;     // redesign: scale the required-notch pips cluster

    // ==== DEBUG — leave all 0 / -1 in the shipped JSON ================================================================
    public int debug = 0;            // 1 = verbose HKDS logging (field triage; also enables the sim-tap harness below)
    public int compDumpRT = 0;       // 1 = save the bottom-screen RenderTexture to hkds_rt.png (~1/s) — the only way to SEE the bottom screen without display capture. LAGGY: arm for one capture, then 0
    public float compSimTapX = -1f;  // simulate a bottom-screen tap at this normalized X (0..1, top-left) — emulator testing (needs debug=1)
    public float compSimTapY = -1f;  // ...and Y; bump compSimTapN to fire it
    public int compSimTapN = 0;      // increment to trigger the simulated tap
    public int compCharmDetailN = 0; // Charms detail DEBUG override: 0 = empty until a charm is tapped (matches Inventory); >0 = force that id
    public int compDbgMsgN = 0;      // debug: bump to spawn the Journal Update popup on demand (needs debug=1) — popup glyph-render testing
    public int compDbgMsgKind = 1;   // debug: 1 = the "full entry" journal popup variant, 0 = the "new notes" variant
}
