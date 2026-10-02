"""Bounded native stand-ins; full SS API/runtime and complete HK map bodies unchanged.
--source-root supports isolated original replay without replacing repository source.
"""
from pathlib import Path
import argparse, hashlib, json, re
p = argparse.ArgumentParser()
p.add_argument('output')
p.add_argument('--source-root', default=str(Path(__file__).resolve().parents[2]))
a = p.parse_args()
root, output = Path(a.source_root), Path(a.output)
sha = lambda b: hashlib.sha256(b).hexdigest()
parts, identities = [], []
for name in ('SilksongGameTweakApi.cs', 'SilksongModsRuntime.cs'):
    path = root / 'tools/silksong-patches/src/mods' / name
    text = path.read_text(encoding='utf-8')
    text = text.replace('#if UNITY_ANDROID && !UNITY_EDITOR', '').replace('#endif', '')
    text = text.replace('using UnityEngine;', 'using DualSouls.Mods.Silksong;')
    text = text.replace('namespace DualSouls.Mods.Silksong', 'namespace ModsRuntimeNativeModel')
    # Only compilation envelope changes; every class declaration/body is identical.
    original = path.read_text(encoding='utf-8')
    body = original[original.index('    public sealed class'):original.rfind('\n}')]
    assert body in text
    parts.append(re.sub(r'^using .*;\n', '', text, flags=re.M))
    identities.append({'source':str(path), 'source_sha256':sha(path.read_bytes()),
                       'complete_classes_sha256':sha(body.encode()), 'body_identical':True})
# Complete HK map/retirement bodies. Other feature/art dependencies are explicit stand-ins.
path = root / 'tools/hollow-knight-patches/src/mods/HollowKnightGameplayFeatures.cs'
source = path.read_text(encoding='utf-8')
def block(pattern):
    match = re.search(pattern, source, re.M)
    if not match: return None
    depth, end = 1, match.end()
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[match.start():end]
fields = source[source.index('        static PlayerData owner;'):source.index('        static int[] charmCostBaseline;')]
region_declaration = block(r'        sealed class MapRegionBaseline\s*\{')
hk = [fields, region_declaration]
identities.append({'source':str(path), 'source_sha256':sha(path.read_bytes()),
                   'declaration':'map ownership fields', 'complete_declaration_sha256':sha(fields.encode()),
                   'body_identical':True})
if region_declaration is not None:
    identities.append({'source':str(path), 'source_sha256':sha(path.read_bytes()),
                       'declaration':'MapRegionBaseline',
                       'complete_declaration_sha256':sha(region_declaration.encode()), 'body_identical':True})
missing_methods = []
for name in ('MaintainAutoMap','CaptureMapBaseline','RestoreMapBaseline','SetCurrentZoneMap',
             'RestorePlayerOwned','RestoreAll','BeginAuthoritativeMapUpdate','EndAuthoritativeMapUpdate',
             'BeforeAuthoritativeMapBoolSet','ApplyMapBaseline','CaptureMapRegions'):
    body = block(r'        (?:(?:public|internal) )?static (?:void|MapRegionBaseline) ' + name + r'\([^)]*\)\s*\{')
    if body is None:
        missing_methods.append(name)
        continue  # Original replay has no authority seams; never claim they executed.
    hk.append(body)
    identities.append({'source':str(path), 'method':name, 'source_sha256':sha(path.read_bytes()),
                       'complete_body_sha256':sha(body.encode()), 'body_identical':True})
parts.append('namespace ModsRuntimeNativeModel { internal static partial class HollowKnightGameplayFeatures {\n' + '\n'.join(hk) + '\n} }')
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text('using System;\nusing System.Collections.Generic;\nusing DualSouls.Mods;\nusing DualSouls.Mods.Silksong;\nusing DualSouls.Skins.Runtime;\n' + '\n'.join(parts), encoding='utf-8')
model = Path(__file__).with_name('ModsRuntimeNativeModel.cs')
output.with_suffix('.manifest.json').write_text(json.dumps({'source_root':str(root), 'output':str(output),
    'output_sha256':sha(output.read_bytes()), 'bodies':identities, 'missing_methods':missing_methods,
    'generator':{'path':str(Path(__file__).resolve()), 'sha256':sha(Path(__file__).read_bytes())},
    'boundary_model':{'path':str(model), 'sha256':sha(model.read_bytes()),
        'ss':['Unity component lifetime is synchronous managed invocation, not Unity scheduling',
              'CheatManager/PlayerData/native gameplay helpers are typed models',
              'Menu/skin library and JNI are no-op dependencies',
              'LineFileTweakStore is in-memory with explicit modeled flush failure'],
        'hk':['Complete map fields, MapRegionBaseline and listed map/restoration bodies unchanged',
              'MapTick is modeled entry; non-map feature teardown helpers are no-ops',
              'SetBool model uses its receiver, not exact native singleton semantics',
              'UpdateGameMap model covers Crossroads/Greenpath and modeled exception only'],
        'not_proven':['native/IL2CPP timing', 'Unity/GPU rendering', 'save serialization', 'live gameplay parity']},
    'envelope_changes':['remove UNITY_ANDROID guard', 'remap namespace to ModsRuntimeNativeModel',
                        'replace UnityEngine using with adapter namespace', 'remove outer using directives']}, indent=2))
