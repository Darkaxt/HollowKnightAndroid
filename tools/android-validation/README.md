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
  "name": "Hollow Knight upper-display Mods presentation",
  "steps": [
    {
      "id": "open-mods",
      "title": "Confirm the selected Mods entry in the upper Options menu",
      "expected_before": "Paused Options screen on the upper display with MODS selected; the lower display contains no Mods gear or pane",
      "expected_after": "The complete controller-operated Mods catalog owns the upper menu window; the lower display remains free of Mods UI",
      "action": { "type": "keyevent", "display": 0, "keycode": "KEYCODE_BUTTON_A" }
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
  --note "The complete catalog was readable on the upper display, controller focus remained stable, and the lower display contained no Mods UI."

python tools/android-validation/dualsouls_validate.py report \
  --session "$CLAUDE_JOB_DIR/tmp/hk-mods-live"
```

`report.md` links each verdict to its before/after upper and lower captures and filtered diagnostics. A step stays `AWAITING_REVIEW` until an evidence-backed verdict is explicitly recorded.
