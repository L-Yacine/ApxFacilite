# ApxFacilite

Offline-first inventory & instalment (EMI) sales management for an Algerian home-appliance store. Fully functional with no internet: local SQLite database, bundled CSS/JS, and an optional native desktop shell.

## Features

- **Catalog** — products, brands, categories, stock ledger with before/after quantities
- **Clients & sales** — each sale is its own independent instalment plan (paid / remaining balances are system-calculated)
- **Prélèvements** — generate instalment collection sheets
- **Purchases & suppliers** — supplier orders and stock intake
- **Import** — legacy data import with preview
- **Users & roles** — login, owner/seller roles, full audit trail (who created/modified what, when)
- **Desktop app** — WinForms + WebView2 window hosting the same MVC app
- **Offline licensing** — machine-bound activation keys (ECDSA), editor-only key generator

## Tech stack

ASP.NET Core MVC on .NET 10 · EF Core + SQLite (`%LOCALAPPDATA%\APXEMI\apxemi.db`) · Bootstrap/jQuery served locally · ClosedXML for Excel exports.

## Projects

| Project | Purpose |
|---|---|
| `APXEMI` | The full app (models, views, controllers, services) |
| `APXEMI.Desktop` | Native window launcher hosting `APXEMI` in-process |
| `LicenseGenerator` | Console tool to issue activation codes — **never shipped** |

## Getting started

```bash
# Web/dev (http://localhost:5145)
dotnet run --project APXEMI

# Build
dotnet build APXEMI.slnx

# Ship the desktop app → publish\APXEMI.Desktop.exe
dotnet publish APXEMI.Desktop -c Release -r win-x64 --self-contained -o publish
```

The database is auto-created on first run. Schema changes use EF Core migrations (`dotnet ef migrations add <Name> --project APXEMI`).

## License

Apache 2.0
