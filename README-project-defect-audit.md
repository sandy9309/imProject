# Project defect audit

## Fixed on `fix/project-defect-fixes`

| Area | Defect | Resolution | Detail |
|---|---|---|---|
| Unity / API | Project furniture list became stale after initial selection | Lightweight revision polling and manual refresh | `README-fix-project-live-sync.md` |
| Unity | Refreshing required re-selecting and clearing the scene | Background refresh preserves placed objects | `README-fix-project-live-sync.md` |
| Unity scene | Hard-coded API tester ran in the production scene | Component disabled | `README-fix-production-scene-cleanup.md` |
| Unity | Legacy scan stub could throw at runtime | Replaced exception with migration warning | `README-fix-production-scene-cleanup.md` |
| Backend | Database root password was embedded in source | Configuration/environment-based connection | `README-fix-backend-security-config.md` |
| Backend | CORS allowed every website | Configurable origin allowlist | `README-fix-backend-security-config.md` |
| Repository | Generated files and secrets were not ignored | Added root ignore rules | `README-fix-repository-hygiene.md` |

## Confirmed existing improvements

The current branch already contained earlier fixes for unique furniture names, model-load cleanup, simplified colliders, debounced position saving, interaction-state control, saved-scene loading and surface/wall handling. Their existing `README-fix-*.md` files remain the source of detail.

## Remaining defects not safely completed in this pass

These require a coordinated data/API migration or product decision and should not be silently changed:

1. **Authentication is not authorization.** Login returns a GUID, but protected endpoints do not validate it or enforce project ownership. Implement JWT/session validation before Internet deployment.
2. **Project items use array indexes.** Reordering or simultaneous website/MR edits can associate coordinates with the wrong item. Add a persistent `projectItemId` in the database and migrate Unity position updates to that ID.
3. **Concurrent edits can overwrite each other.** Add optimistic concurrency (`revision` in update requests and HTTP 409 on mismatch).
4. **Delete semantics are incomplete.** Removing a spawned object in MR does not necessarily remove it from the website project. Define temporary hide versus permanent project deletion.
5. **Plain HTTP is still configured in the Unity scene.** Production should use HTTPS with a configurable base URL and a valid certificate trusted by Quest.
6. **Frontend source is not present in the current `client` directory.** It contains dependencies/lock data but no `src` or `package.json`, so frontend build and API alignment cannot be verified from this checkout.
7. **Git tracking is inconsistent.** Much of the backend and duplicate project folders are untracked, while generated Unity logs were previously committed. Review and stage intentionally; do not use a blanket `git add .`.
8. **Quest validation remains required.** Passthrough capture, MRUK permissions, controller mappings and performance cannot be proven by desktop C# compilation.

## Recommended next branches

- `fix/auth-authorization`
- `fix/project-item-identity`
- `fix/optimistic-concurrency`
- `fix/frontend-source-layout`
- `fix/quest-device-validation`
