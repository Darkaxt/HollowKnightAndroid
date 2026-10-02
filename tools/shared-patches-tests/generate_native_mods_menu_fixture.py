"""Complete production menu bodies against explicit, namespace-isolated engine models.
Original replay uses --source-root pointing at retained copies, never repository swaps.
This is not native Unity/EventSystem/IL2CPP timing or post-warp arrival evidence.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re


def digest(data):
    return hashlib.sha256(data).hexdigest()


def declarations(source):
    # Keep complete class declarations as identities, including all nested methods.
    items = []
    for match in re.finditer(r'^\s*(?:(?:public|internal|private|static|sealed|partial|abstract)\s+)*class\s+(\w+)[^{]*\{', source, re.M):
        opening = source.index('{', match.start())
        depth, i, state = 1, opening + 1, 'code'
        while depth:
            c = source[i]
            nxt = source[i:i + 2]
            if state == 'code':
                if nxt == '//': state = 'line'; i += 1
                elif nxt == '/*': state = 'comment'; i += 1
                elif c == '"': state = 'string'
                elif c == "'": state = 'char'
                elif c == '{': depth += 1
                elif c == '}': depth -= 1
            elif state == 'line':
                if c == '\n': state = 'code'
            elif state == 'comment':
                if nxt == '*/': state = 'code'; i += 1
            elif c == '\\': i += 1
            elif (state == 'string' and c == '"') or (state == 'char' and c == "'"): state = 'code'
            i += 1
        text = source[match.start():i]
        items.append({'declaration': match.group(1), 'start_character': match.start(),
                      'end_character_exclusive': i, 'start_line': source[:match.start()].count('\n') + 1,
                      'end_line': source[:i].count('\n') + 1, 'declaration_sha256': digest(text.encode()),
                      'complete_body_sha256': digest(source[opening:i].encode())})
    return items


def generate(root, output):
    testroot = Path(__file__).resolve().parent
    template = testroot / 'NativeModsMenuNativeModel.cs'
    parts, identities = [], []
    for profile, folder, filename in (
        ('Hk', 'hollow-knight-patches', 'HollowKnightNativeModsMenu.cs'),
        ('Ss', 'silksong-patches', 'SilksongNativeModsMenu.cs')):
        model = template.read_text(encoding='utf-8').replace('NativeMenuFixture.PROFILE', 'NativeMenuFixture.' + profile)
        if profile == 'Hk':
            model = model.replace('// HK_MENU_GROUPS', 'public CanvasGroup title, controls, content;')
        if profile == 'Ss':
            model = model.replace('// SS_BACK_BUTTON', 'public UI.MenuButton backButton;')
            model = model.replace('// SS_SUBMIT_FIELD', 'public Events.UnityEvent OnSubmitPressed = new Events.UnityEvent();')
            model = model.replace('// SS_SUBMIT_INVOKE', 'OnSubmitPressed?.Invoke();')
        parts.append(model)
        path = root / 'tools' / folder / 'src/mods' / filename
        source = path.read_text(encoding='utf-8')
        namespace = 'DualSouls.Mods.' + ('HollowKnight' if profile == 'Hk' else 'Silksong')
        text = source.replace('#if UNITY_ANDROID && !UNITY_EDITOR', '').replace('#endif', '')
        text = text.replace('using GlobalEnums;', 'using NativeMenuFixture.' + profile + '.Engine.GlobalEnums;')
        text = text.replace('UnityEngine', 'NativeMenuFixture.' + profile + '.Engine')
        text = text.replace('namespace ' + namespace, 'namespace NativeMenuFixture.' + profile + '.Engine')
        if profile == 'Hk':
            text = text.replace('global::HkStageHooks', 'global::NativeMenuFixture.Hk.Engine.HkStageHooks')
        text = 'using ' + namespace + ';\n' + text
        parts.append(text)
        identities.append({'path': str(path), 'source_sha256': digest(path.read_bytes()),
                           'classes': declarations(source), 'complete_production_source': True,
                           'changes': ['preprocessor envelope', 'namespace/using qualification only']})
    for relative in ('tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs',
                     'tools/hollow-knight-patches/src/mods/HollowKnightGameTweakApi.cs'):
        path = root / relative
        source = path.read_text(encoding='utf-8')
        text = source.replace('#if (UNITY_ANDROID && !UNITY_EDITOR) || HOLLOW_KNIGHT_GAMEPLAY_TESTS', '#if true')
        text = text.replace('#if UNITY_ANDROID && !UNITY_EDITOR', '#if true')
        text = text.replace('UnityEngine', 'NativeMenuFixture.Hk.Engine')
        text = text.replace('global::', 'global::NativeMenuFixture.Hk.Engine.')
        text = text.replace('DualSouls.Skins.HollowKnight.Runtime.HollowKnightSkinRuntime', 'NativeMenuFixture.Hk.Engine.NativeSkinRuntime')
        if path.name == 'HkStageHooks.cs':
            # Global class is wrapped, body unchanged.
            first = text.index('// Explicit boundary')
            text = text[:first] + 'namespace NativeMenuFixture.Hk.Engine {\n' + text[first:] + '\n}'
        else:
            text = text.replace('namespace DualSouls.Mods.HollowKnight', 'namespace NativeMenuFixture.Hk.Engine')
            text = 'using DualSouls.Mods.HollowKnight;\n' + text
        parts.append(text)
        identities.append({'path': str(path), 'source_sha256': digest(path.read_bytes()),
                           'classes': declarations(source), 'complete_production_source': True,
                           'changes': ['preprocessor envelope', 'namespace/using qualification only']})
    # Compilation units are separate: using declarations must stay at unit start.
    output.mkdir(parents=True, exist_ok=True)
    files = []
    for index, text in enumerate(parts):
        path = output / ('NativeMenuFixture' + str(index) + '.g.cs')
        path.write_text(text, encoding='utf-8')
        files.append({'path': str(path), 'sha256': digest(path.read_bytes())})
    manifest = {'source_root': str(root), 'sources': identities, 'outputs': files,
                'generator': {'path': str(Path(__file__).resolve()), 'sha256': digest(Path(__file__).read_bytes())},
                'boundary_model': {'path': str(template), 'sha256': digest(template.read_bytes()),
                    'models': ['synchronous Instantiate/Awake/OnEnable with parent activity',
                               'hierarchy-aware Selectable activity and inherited native/custom dispatch',
                               'deferred Destroy with explicit flush', 'nested coroutine yields with explicit pump',
                               'typed PlayerData.SetBenchRespawn and GameManager.ReadyForRespawn dependencies',
                               'native serialized UnityEvent targets and clone remapping',
                               'fallible engine operations enumerated by ordinal fault injection'],
                    'not_proven': ['native Unity EventSystem ordering/timing', 'IL2CPP', 'GPU/readability pixels',
                                   'controller hardware', 'save integration', 'post-warp arrival', 'optical parity']}}
    (output / 'identities.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('output')
    parser.add_argument('--source-root', default=str(Path(__file__).resolve().parents[2]))
    args = parser.parse_args()
    generate(Path(args.source_root), Path(args.output))
