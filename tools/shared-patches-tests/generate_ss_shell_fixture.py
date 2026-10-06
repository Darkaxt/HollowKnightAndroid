"""Independent SS production-body fixture; no HK fixture/policy is imported.

Only namespace/imports and partial-class access are adapted. Complete selected
bodies and production declarations are checked and hashed; engine/native/art
adapters live separately in SilksongShellFixture.cs. --source-root enables
identical extraction against an immutable original-source replay.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re


def end_brace(text, start):
    depth = 0
    token = re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]', re.S)
    for match in token.finditer(text, start):
        if match.group() == '{':
            depth += 1
        elif match.group() == '}':
            depth -= 1
            if depth == 0:
                return match.end()
    raise ValueError('Unclosed production body')


def class_text(text, name):
    match = re.search(r'(?:public )?(?:(?:sealed|abstract|static|readonly) )?(?:class|struct|interface|enum) ' + name + r'\b[^\{]*\{', text)
    if not match:
        raise ValueError('Missing production type ' + name)
    return text[match.start():end_brace(text, text.index('{', match.start()))]


def member(text, name):
    match = re.search(r'^    (?:public |protected |private |internal )?(?:override |virtual |static )*(?:readonly )?[\w<>.?]+\s+' + name + r'\s*(?:\([^;]*?\)|(?=\{|=>))\s*(?:\{|=>)', text, re.M)
    if not match:
        raise ValueError('Missing production member ' + name)
    if text[match.end()-1] == '{':
        return text[match.start():end_brace(text, match.end()-1)]
    return text[match.start():text.index(';', match.end())+1]


def generate(output, source_root, manifest, shared_source_root=None):
    if shared_source_root is None:
        shared_source_root = source_root.parents[2] / 'shared-patches/src/dualscreen'
    identity, parts = [], []
    def record(file, label, original, extracted):
        # Imports and partial access are outside executable type bodies.
        def executable(value):
            if '{' in value and '=>' not in value[:value.index('{')]:
                return value[value.index('{'):]
            return value[value.index('=>'):]
        assert executable(original) == executable(extracted), (file, label)
        identity.append(dict(source=str(source_root/file), member=label,
                             source_file_sha256=hashlib.sha256((source_root/file).read_bytes()).hexdigest(),
                             body_utf8_lf_sha256=hashlib.sha256(executable(original).encode()).hexdigest(),
                             body_identical=True, signature_only_partial_access=original != extracted))
    def whole(file, names):
        text=(source_root/file).read_text(encoding='utf-8')
        for name in names:
            value=class_text(text,name)
            adapted=value.replace('class '+name, 'partial class '+name, 1)
            parts.append(adapted if 'class ' + name in value else value)
            record(file,name,value,parts[-1])
    whole('DualScreenV2.cs',('DualScreenV2','DsHudReleasePump'))
    whole('DsPresentation.cs',('DsPresentation',))
    touch = class_text((source_root/'DsTouch.cs').read_text(encoding='utf-8'), 'DsTouch')
    mapping = member(touch, 'MapToCanvas')
    parts.append('public static partial class DsTouch {\n' + mapping + '\n}')
    record('DsTouch.cs', 'DsTouch.MapToCanvas', mapping, mapping)
    whole(shared_source_root / 'DirectDisplayPresentation.cs',
          ('DirectDisplayPresentation', 'DirectDisplayCanvasScaler'))
    whole('DsShell.cs',('DsShell',))
    shell = class_text((source_root/'DsShell.cs').read_text(encoding='utf-8'), 'DsShell')
    for name in ('SetPaused', 'EndSlide', 'SettleSlide', 'SuspendPresentation'):
        # Extra per-method identity only; the complete class above is always the
        # compiled authority. Frozen pre-fix replay simply lacks the new helpers.
        if re.search(r'^    [\w<>.?]+ ' + name + r'\(', shell, re.M) or name == 'SetPaused':
            value = member(shell, name)
            record('DsShell.cs', 'DsShell.'+name, value, value)
    whole('DsLayout.cs',('DsLayout','DsGestureTarget','DsShellInput'))
    whole('DsInput.cs',('DsGestureType','DsGesture','DsInput'))
    whole('DsTitleCard.cs',('DsTitleCard',))
    whole('DsZoomSlider.cs',('DsZoomSlider',))
    whole('DsHudRouting.cs',('DsHudRouting','DsHudRenderScope','DsHudSuppressionScope','DsHudFrame'))
    theme=(source_root/'DsTheme.cs').read_text(encoding='utf-8')
    declarations=re.findall(r'^    public (?:const [^;]+|static readonly Color [^;]+);',theme,re.M)
    parts.append('public static partial class DsTheme {\n'+'\n'.join(declarations)+'\n}')
    identity.append(dict(source=str(source_root/'DsTheme.cs'),member='DsTheme.production-metrics-colors',
                         source_file_sha256=hashlib.sha256((source_root/'DsTheme.cs').read_bytes()).hexdigest(),
                         declaration_utf8_lf_sha256=hashlib.sha256('\n'.join(declarations).encode()).hexdigest(),declaration_identical=True))
    whole('IDsScreen.cs',('IDsHeaderTitle','DsStripItem','IDsTabStrip','IDsScreen'))
    text=(source_root/'IDsScreen.cs').read_text(encoding='utf-8')
    if 'interface IDsPresentationSuspend' in text:
        whole('IDsScreen.cs',('IDsPresentationSuspend',))
    whole('DsActions.cs',('DsActionPlace','DsAction','IDsActionBar','DsActionBar'))
    # The pane prefixes contain actual production constants/properties/fields
    # and complete Build bodies. Data collection/painting below these prefixes
    # is a typed native-data/art boundary, never a replacement pane builder.
    whole('DsScreens.cs',('DsGridScreen',))
    whole('DsIconGrid.cs',('DsItem','DsSection'))
    panes=(
        ('DsIconGrid.cs','DsIconGrid',('Build',),()),
        ('DsScreens.cs','DsInventoryScreen',('Build','Tick','OnGesture'),('ActionPane',)),
        ('DsLoadoutScreen.cs','DsLoadoutScreen',('Build','OnShow','OnHide','Tick','ColumnHeight','ActionPane','ShowCrestPicker'),()),
        ('DsTasksScreen.cs','DsTasksScreen',('Build','OnShow','OnHide','Tick','ActionPane','CollectActions','ToggleCompleted','OnGesture','HitTest'),()),
        ('DsJournalScreen.cs','DsJournalScreen',('Build','OnShow','OnHide','Tick','OnGesture','HitTest'),()),
    )
    for file,name,names,_ in panes:
        source=(source_root/file).read_text(encoding='utf-8')
        cls=class_text(source,name)
        build=member(cls,'Build')
        end=cls.index(build)+len(build)
        prefix=cls[:end]
        # Hash the prefix as a declaration slice and the full Build separately.
        identity.append(dict(source=str(source_root/file),member=name+'.production-prefix',
                             source_file_sha256=hashlib.sha256((source_root/file).read_bytes()).hexdigest(),
                             declaration_utf8_lf_sha256=hashlib.sha256(prefix.encode()).hexdigest(),declaration_identical=True))
        pieces=[prefix.replace('class '+name,'partial class '+name,1)]
        record(file,name+'.Build',build,build)
        for name2 in names:
            if name2=='Build': continue
            value=member(cls,name2)
            pieces.append(value)
            record(file,name+'.'+name2,value,value)
        if name == 'DsLoadoutScreen':
            # Resident crest selection/animation state is declared by production,
            # not invented by the native crest-art construction seam.
            for pattern in (r'^    RectTransform _picker, _strip;',
                            r'^    int _crestPick = -1;',
                            r'^    float _stripFrom, _stripTo, _stripNow, _stripT = 1f;',
                            r'^    bool _choosingCrest;'):
                match = re.search(pattern, cls, re.M)
                if not match: raise ValueError('Missing crest state ' + pattern)
                value = match.group()
                pieces.append(value)
                identity.append(dict(source=str(source_root/file), member=name+'.'+value.strip(),
                    source_file_sha256=hashlib.sha256((source_root/file).read_bytes()).hexdigest(),
                    declaration_utf8_lf_sha256=hashlib.sha256(value.encode()).hexdigest(), declaration_identical=True))
        parts.append('\n'.join(pieces)+'\n}')
    whole('DsMapScreen.cs',('DsMapScreen',))
    # Engine camera construction/native map discovery remains modeled, while
    # actual camera admission/late-render policy executes unchanged.
    text=(source_root/'DsMapView.cs').read_text(encoding='utf-8')
    bodies=[class_text(text,'Frame')]
    identity.append(dict(source=str(source_root/'DsMapView.cs'),member='DsMapView.Frame',
                         declaration_utf8_lf_sha256=hashlib.sha256(bodies[0].encode()).hexdigest(),declaration_identical=True))
    for field in ('MinZoom','MaxZoom','_pan','_zoom','_mapUnitsPerPixel','_worldLatched','_worldAreas','_nextAssert','_forceAssert'):
        match=re.search(r'^    (?:public const )?(?:float|Vector2|bool|int) '+field+r'\b[^;]*;',text,re.M)
        if not match: raise ValueError('Missing map declaration '+field)
        value=match.group()
        bodies.append(value)
        identity.append(dict(source=str(source_root/'DsMapView.cs'),member='DsMapView.'+field,
                             declaration_utf8_lf_sha256=hashlib.sha256(value.encode()).hexdigest(),declaration_identical=True))
    for field in ('_cameraOwner','_cameraHud','_nextSourceCameraFrame','_sourceCamerasBound'):
        match=re.search(r'^    (?:GameCameras|Transform|int|bool) '+field+r'\b[^;]*;',text,re.M)
        if match:
            value=match.group();bodies.append(value)
            identity.append(dict(source=str(source_root/'DsMapView.cs'),member='DsMapView.'+field,
                source_file_sha256=hashlib.sha256((source_root/'DsMapView.cs').read_bytes()).hexdigest(),
                declaration_utf8_lf_sha256=hashlib.sha256(value.encode()).hexdigest(),declaration_identical=True))
    for name in ('SetVisible','LateTick','Mode','Pan','Zoom','ZoomLevel','SetZoom','ResetPan','ResetZoom','ViewMoved','ResetView','SetMode','Poll','Rebind','FindSourceCameras','Classify'):
        value=member(class_text(text,'DsMapView'),name)
        adapted=value.replace('Poll(', 'PollSources(', 1) if name=='Poll' else value
        bodies.append(adapted)
        record('DsMapView.cs','DsMapView.'+name,value,adapted)
    parts.append('public partial class DsMapView {\n'+'\n'.join(bodies)+'\n}')
    # HUD render suppression/event lifecycle bodies, not a fake capture policy.
    text=(source_root/'DsHudView.cs').read_text(encoding='utf-8')
    bodies=[]
    for name in ('OnEnable','OnDisable','SetVisible','Stop','StopCanvasCleanup','Suspend','LateUpdate','BeforeCamera','AfterCamera','RestoreScope','RestoreAllScopes','CanPresent','NativeVisible','Hide','CollectRenderers','SuppressOverlay','RestoreOverlay','PrepareCanvasScope','FrameCamera','MeasureToolSplit','LayoutPosition','HasArtwork','Fail'):
        value=member(class_text(text,'DsHudView'),name)
        adapted=value.replace('FrameCamera(', 'FrameCameraBody(', 1) if name=='FrameCamera' else value
        bodies.append(adapted)
        record('DsHudView.cs','DsHudView.'+name,value,adapted)
    inventory_start=next((text.index(marker) for marker in ('    struct HudNode','    public sealed class HudInventoryWatch') if marker in text),None)
    if inventory_start is not None:
        value=text[inventory_start:text.index('    readonly DsHudRenderScope')]
        bodies.append(value)
        identity.append(dict(source=str(source_root/'DsHudView.cs'),member='native inventory declarations',
            source_file_sha256=hashlib.sha256((source_root/'DsHudView.cs').read_bytes()).hexdigest(),
            declaration_utf8_lf_sha256=hashlib.sha256(value.encode()).hexdigest(),declaration_identical=True))
        start=text.index('    static bool Retired<T>')
        value=text[start:end_brace(text,text.index('{',start))]
        bodies.append(value);record('DsHudView.cs','Retired<T>',value,value)
        for name in ('EnsureInventory','WatchInventoryNode','RetireInventoryWatches'):
            if name=='RetireInventoryWatches' and name not in text: continue
            value=member(text,name);bodies.append(value);record('DsHudView.cs',name,value,value)
    parts.append('public partial class DsHudView : MonoBehaviour {\n'+'\n'.join(bodies)+'\n}')
    text=(source_root/'DsWidgets.cs').read_text(encoding='utf-8')
    cls=class_text(text,'DsWidgets')
    bodies=[]
    for name in ('Rect','Box','Circle','HRule','VRule','RuleImage','Icon','Stretch','SetActive'):
        value=member(cls,name)
        bodies.append(value)
        record('DsWidgets.cs','DsWidgets.'+name,value,value)
    # Both overloads of Place are complete, with UnityEngine.Rect unchanged.
    for match in re.finditer(r'^    public static void Place\([^)]*\)\s*\{',cls,re.M):
        value=cls[match.start():end_brace(cls,match.end()-1)]
        bodies.append(value)
        record('DsWidgets.cs','DsWidgets.Place',value,value)
    parts.append('public static partial class DsWidgets {\n'+'\n'.join(bodies)+'\n}')
    text=(source_root/'DsPortHudState.cs').read_text(encoding='utf-8')
    for name in ('DsHudManagerCallbacks','DsHudReleaseState'):
        value=class_text(text,name)
        record('DsPortHudState.cs',name+' (compiled directly)',value,value)
    generated=('// Generated SS production bodies; namespace-isolated from HK.\n'
               '#pragma warning disable CS0649, CS0414, CS0169 // Typed native-data boundaries retain original fields.\n'
               'namespace SsShellContracts;\n'
               'using System;\nusing System.Collections;\nusing System.Collections.Generic;\n'
               'using DualSouls.DualScreen;\nusing SsShellContracts.Engine;\n'
               'using UnityEngine = SsShellContracts.Engine;\n'
               'using SsShellContracts.GlobalEnums;\n'
               'using TMProOld = SsShellContracts.Engine;\n'
               'using TmpText = SsShellContracts.Engine.TextMeshProUGUI;\n'
               'using TmpAlign = SsShellContracts.Engine.TextAlignmentOptions;\n'
               + '\n\n'.join(parts)+'\n')
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(generated,encoding='utf-8')
    manifest.parent.mkdir(parents=True,exist_ok=True)
    manifest.write_text(json.dumps(dict(source_root=str(source_root),output=str(output),
        output_sha256=hashlib.sha256(output.read_bytes()).hexdigest(),
        line_endings='UTF-8 LF normalized bodies; physical source hashes retained',
        bodies=identity),indent=2),encoding='utf-8')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('output',type=Path)
    parser.add_argument('--source-root',type=Path,default=Path(__file__).resolve().parents[1]/'silksong-patches/src/dualscreen')
    parser.add_argument('--manifest',type=Path)
    parser.add_argument('--shared-source-root',type=Path)
    args=parser.parse_args()
    generate(args.output,args.source_root,args.manifest or args.output.with_suffix('.manifest.json'),args.shared_source_root)
