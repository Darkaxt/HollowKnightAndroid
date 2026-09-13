# Dual Souls Android validation harness

`dualsouls_validate.py` records repeatable, serial-scoped evidence for live Android validation. It deliberately separates observation from input:

1. `prepare` captures both displays and returns a short-lived token.
2. Review both returned images against `expected_before`.
3. `execute` accepts only that token, sends one allow-listed action, then captures both displays and filtered logs.
4. Review the resulting evidence and record `pass`, `fail`, or `blocked`.

The harness never treats successful ADB execution as a visual pass. It does not expose arbitrary shell commands, read process memory, use native offsets, edit saves, clear package data, or uninstall applications.

## Plan format

```json
{
  "version": 1,
  "name": "Hollow Knight Mods presentation",
  "steps": [
    {
      "id": "open-mods",
      "title": "Open Mods from the lower-screen gear",
      "expected_before": "Stable gameplay with the ordinary lower HUD visible",
      "expected_after": "Mods exclusively owns the lower content while frame chrome remains",
      "action": { "type": "tap", "display": 4, "x": 106, "y": 922 }
    }
  ]
}
```

Supported actions are bounded `tap`, `swipe`, `keyevent`, and input-free `observe`. Every input action names a logical display. Key events are restricted to navigation/controller keys.

## Usage

Store evidence outside the repository, such as under `$CLAUDE_JOB_DIR/tmp`:

```bash
python tools/android-validation/dualsouls_validate.py init \
  --session "$CLAUDE_JOB_DIR/tmp/hk-mods-live" \
  --plan path/to/plan.json \
  --serial bfa98654 \
  --package io.github.darkaxt.dualsouls

python tools/android-validation/dualsouls_validate.py prepare \
  --session "$CLAUDE_JOB_DIR/tmp/hk-mods-live" \
  --step open-mods
```

Inspect the two paths returned by `prepare`. If the state is wrong or the token expires, discard it without input and prepare again:

```bash
python tools/android-validation/dualsouls_validate.py discard \
  --session "$CLAUDE_JOB_DIR/tmp/hk-mods-live" \
  --note "The transition had not completed."
```

If the precondition is visibly correct, pass the exact token returned by `prepare`:

```bash
python tools/android-validation/dualsouls_validate.py execute \
  --session "$CLAUDE_JOB_DIR/tmp/hk-mods-live" \
  --token TOKEN
```

After inspecting the post-action captures and log file:

```bash
python tools/android-validation/dualsouls_validate.py review \
  --session "$CLAUDE_JOB_DIR/tmp/hk-mods-live" \
  --step open-mods \
  --verdict pass \
  --note "Ordinary HUD content was absent and the Mods hierarchy was clean."

python tools/android-validation/dualsouls_validate.py report \
  --session "$CLAUDE_JOB_DIR/tmp/hk-mods-live"
```

`report.md` links each verdict to its before/after upper and lower captures and filtered diagnostics. A step stays `AWAITING_REVIEW` until an evidence-backed verdict is explicitly recorded.
