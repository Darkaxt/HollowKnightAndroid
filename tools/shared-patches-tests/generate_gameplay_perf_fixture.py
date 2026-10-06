"""Extract complete production bodies for focused gameplay-work regressions.

Only namespace/imports/type access are adapted. Engine behavior is modeled in
GameplayPerformanceFixture.cs; these counters are not native latency or FPS.
"""
import argparse
import hashlib
import json
from pathlib import Path
from generate_ss_shell_fixture import member


def generate(root, output):
    bodies = []
    identity = []
    def record(path, label, text):
        source = root / path
        identity.append(dict(source=path, member=label,
            source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
            selected_utf8_lf_sha256=hashlib.sha256(text.encode()).hexdigest(),
            body_unchanged=True))
        bodies.append(text)
    hud_path = 'tools/hollow-knight-patches/src/dualsouls/HKDualScreen.Bottom.Hud.cs'
    hud = (root / hud_path).read_text(encoding='utf-8')
    start = hud.index('    static readonly string[] NAME_PARTS')
    finish = hud.index('    // The ornament and page glyphs', start)
    record(hud_path, 'complete native speaker fields/helpers through SetNameClone', hud[start:finish])
    path = 'tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs'
    main = (root / path).read_text(encoding='utf-8')
    for name in ('RouteLoreDialogue','ScanTutorials','ScanNode','RouteHealParticles'):
        record(path, name, member(main, name))
    if '    public sealed class TutorialInventoryWatch' in main:
        record(path, 'local tutorial inventory declarations', main[main.index('    public sealed class TutorialInventoryWatch'):main.index('    void ScanTutorials')])
        for name in ('WatchTutorialNode','RetireTutorialWatches'):
            record(path, name, member(main, name))
    path = 'tools/hollow-knight-patches/src/dualsouls/HKDualScreen.Util.cs'
    util = (root / path).read_text(encoding='utf-8')
    for name in ('IsTextMeshProGraphic', 'SanitizeDetachedTmpClone', 'NeutralizeDetachedTmpClip', 'TmpProp', 'FindDeep', 'SetLayerRecursive','RouteToLayer','RestoreRoutedLayers'):
        record(path, name, member(util, name))
    # Retain any production clipping scratch declaration; absent on original RED.
    for line in util.splitlines():
        if line.startswith('    static MaterialPropertyBlock '):
            record(path, 'clipping scratch declaration', line)
    path = 'tools/hollow-knight-patches/src/dualsouls/HKDualScreen.Bottom.NativeHud.cs'
    # Existing extractor intentionally accepts plain member names only. Match
    # the generic declaration, then use its independent comment/string-aware brace parser.
    from generate_ss_shell_fixture import end_brace
    text = (root / path).read_text(encoding='utf-8')
    start = text.index('    static Func<T> NativeHudGetter<T>')
    finish = end_brace(text, text.index('{', start))
    record(path, 'NativeHudGetter<T>', text[start:finish])
    output.mkdir(parents=True, exist_ok=False)
    generated = 'using System;\nusing System.Collections.Generic;\nusing UnityEngine;\npartial class HKDualScreen {\n' + '\n'.join(bodies) + '\n}\n'
    (output / 'Production.cs').write_text(generated, encoding='utf-8')
    (output / 'source-manifest.json').write_text(json.dumps(dict(
        source_root=str(root), selected=identity,
        generated_sha256=hashlib.sha256(generated.encode()).hexdigest(),
        limits='Exact selected bodies with modeled engine boundaries; no native timing, rendering or FPS proof.'), indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    parser.add_argument('--source-root', type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    generate(args.source_root, args.output)
