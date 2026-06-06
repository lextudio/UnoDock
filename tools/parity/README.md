# Parity Harness

This folder contains the first Windows-focused parity harness for:

- `src/UnoDock.Sample`
- `externals/AvalonDock/sample`

The initial workflow is:

1. Run `launch-baselines.ps1` to build and start both apps on known DevFlow ports.
2. Run `capture-scene.ps1 -Scene <name>` to drive one deterministic scene and save screenshots.
3. Run `diff-scene.ps1 -Scene <name>` to generate `diff.png` and `diff.json`.
4. Run `stop-baselines.ps1` to close both apps.

For a complete pass, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\parity\run-suite.ps1
```

## Scenes

- `document-active`
  - Captures the default startup state with the first document active.
- `auto-hide-rest`
  - Moves `Solution Explorer` into auto-hide before capture.
- `auto-hide-open`
  - Moves `Solution Explorer` into auto-hide and opens its flyout before capture.

## Output

Artifacts are written under:

- `artifacts/parity/<scene>/`

Each scene currently stores:

- `uno.png`
- `wpf.png`
- `diff.png`
- `diff.json`
- `status.json`
- `actions.log`

The suite also writes `artifacts/parity/report.md`.

## Notes

- The harness assumes Windows desktop execution.
- It uses the shared DevFlow action contract where possible:
  - `dock-toggle-autohide`
  - `dock-open-flyout`
  - `dock-active-content`
