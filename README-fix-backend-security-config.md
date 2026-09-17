# Backend configuration hardening

## Problems

- The repository contained a root database username and plaintext password.
- CORS accepted requests from every origin.
- Local and production configuration were mixed into source code.

## Changes

- `DbService` now reads `ConnectionStrings:FurnitureDb` or the `FURNITURE_DB_CONNECTION` environment variable.
- The application fails at startup with a clear message when no connection is configured.
- CORS now reads `Cors:AllowedOrigins` and defaults only to local React development origins.
- Added `appsettings.example.json` containing placeholders only.

## Required setup

Copy `appsettings.example.json` to an ignored `appsettings.json` and replace placeholders, or set:

```powershell
$env:FURNITURE_DB_CONNECTION = "server=...;user=...;database=ar_furniture_db;port=3306;password=..."
```

The database password previously committed to the repository must be rotated on the database server. Removing it from the current file does not remove it from Git history.

## Remaining security work

Login currently returns an opaque GUID without server-side authorization enforcement. JWT authentication and ownership checks are still required before public deployment.
