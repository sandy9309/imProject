# Repository hygiene

## Problem

Unity-generated folders, IDE caches, Node dependencies, build outputs, local settings and runtime uploads can enter Git. These files are large, machine-specific and cause frequent merge conflicts.

## Changes

The root `.gitignore` now excludes:

- Unity `Library`, `Temp`, `Obj`, `Logs`, builds and user settings
- Visual Studio `.vs`, generated solutions and project files
- Node `node_modules` and frontend build output
- ASP.NET `bin`, `obj` and local `appsettings` files
- uploaded screenshots and `.env` secrets

## Important

`.gitignore` does not untrack files already committed. Existing tracked Unity logs and IDE files are intentionally not removed in this change because the working tree already contained user modifications. They should be removed from the index in a dedicated cleanup commit after review.
