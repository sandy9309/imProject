# Production scene cleanup

## Problems

- The production Unity scene still enabled `ApiTester`.
- `ApiTester` automatically requested a hard-coded project and loaded a test model at startup, racing the real `ModelLoader` workflow.
- The legacy `OVRSceneScript.RequestSceneCapture` method threw `NotImplementedException` if called.

## Changes

- Disabled the `ApiTester` component in `Assets/my project.unity` while retaining it for development diagnostics.
- Replaced the legacy exception with a warning directing callers to `SceneAutoScanner.TriggerNewScan`.

## Verification

1. Open the production scene in Unity.
2. Confirm the API tester component is disabled.
3. Enter Play Mode and confirm no model is loaded before the user selects a project.
4. Confirm room scanning is handled by `SceneAutoScanner`.
