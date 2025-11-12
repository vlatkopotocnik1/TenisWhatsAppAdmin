# WhatsApp Admin

## Overview
- **Type:** Blazor Server administration console built on `.NET 8`
- **Purpose:** Curate WhatsApp groups, synchronize members with the Whapi Cloud API, and send targeted broadcasts
- **UI Stack:** MudBlazor components on top of standard Blazor Server routing/layout
- **Runtime services:** Serilog for structured logs, CsvHelper for CSV imports, Protected Session Storage for lightweight auth sessions

## Features
- **Group synchronization:** Import groups from CSV or a backing service, diff changes against live WhatsApp groups, and apply create/add/remove operations in bulk.
- **Broadcast tooling:** Send announcements either to every group, a single group, or an individual member directly from the dashboard.
- **Safety rails:** Overlay-based progress indicator, granular snackbars, batched deletes, and guardrails around participant removal and invalid numbers.
- **In-app persistence helpers:** Automatically persists imported contacts into `contacts.json` for name enrichments on subsequent refreshes.
- **Structured diagnostics:** Serilog request logging, custom HTTP delegating handler, and detailed masking of sensitive numbers in logs.

## High-Level Architecture
- **Blazor Server host (`Program.cs`):** Boots Razor Pages + SignalR hub, registers MudBlazor, configures Serilog sinks, and wires application services via DI.
- **Authentication:** `AuthStateProvider` uses protected browser storage plus **hard-coded credentials** (`admin / 321Vsite+`). Replace before production release.
- **State + UX services:**
  - `ImportStateService` maintains imported group drafts and triggers UI refresh events.
  - `OverlayService` exposes an async runner that toggles the global loading overlay.
  - `DialogManager` centralizes dialog setup callbacks for add/remove/rename/broadcast flows.
- **Domain services:**
  - `WhapiService`—HTTP client wrapper with validation, masking, and batched messaging; consumes the Whapi Cloud REST API using the configured bearer token.
  - `GroupSyncService`—diff engine coordinating imports, API refreshes, overlay updates, and MudBlazor snackbar notifications.
  - `MockService`—in-memory `IService` implementation for local data seeding/testing.
- **UI:** `Pages/Index.razor` hosts the main board, `Toolbar.razor` handles imports, `Shared/Dialog` components drive CRUD modals, and `GlobalOverlay` listens to `OverlayService`.

## Data Flow
1. **Import:** User selects CSV or clicks "Import from Service" ➜ `Toolbar.razor` parses data ➜ `ImportStateService.SetGroups()` fires change event.
2. **Diff:** `GroupSyncService.InitializeAsync()` consumes imported data, refreshes live groups from Whapi, and annotates `ToAdd/ToRemove`.
3. **Review:** UI chips reflect each group's state; dialogs allow manual adjustments.
4. **Apply:** Sync/Delete actions call into `GroupSyncService` ➜ `WhapiService` operations ➜ MudBlazor snackbars summarize results.
5. **Enrichment:** Imported contacts are written to `contacts.json`; future refreshes enrich member names.

## Prerequisites
- [.NET SDK 8.0+](https://dotnet.microsoft.com/en-us/download)
- Optional: Node.js (only if you plan to work on the experimental `whatsapp-automation` integration)
- Whapi Cloud API key with group management permissions

## Getting Started
```bash
# Restore dependencies
dotnet restore

# Run the Blazor Server host (defaults to https://localhost:5001)
dotnet run
```

The app enforces authentication—open `/login` and sign in with the configured credentials (see **Security**).

### Live reload while developing
To auto-rebuild the server whenever you save a file, use the `dotnet watch` tooling via the helper script:
```powershell
# From the repository root
.\watch.ps1              # Starts in Development mode (https://localhost:7232)
.\watch.ps1 Production   # Starts in Production mode (https://localhost:7233)
```
The watch process rebuilds on file changes. After each rebuild, simply refresh your browser to see the latest changes—no manual restarts required.

## Configuration
- **Whapi API key:** Stored under `Whapi:ApiKey` (default `appsettings.json`). Prefer user-secrets or environment variables in real deployments:
  ```bash
  dotnet user-secrets set "Whapi:ApiKey" "<your-key>"
  ```
- **Logging:** Controlled by `Serilog` sections in `appsettings.Development.json` / `appsettings.Production.json`. Logs write to the console and rolling files under `logs/`.
- **Protected storage:** Blazor Server `ProtectedSessionStorage` retains the logged-in username per browser session.

## Important Files
- `Program.cs` – DI setup, HTTP clients, Serilog pipeline, hub mapping.
- `Services/WhapiService.cs` – Wraps Whapi Cloud REST endpoints (groups, participants, messages).
- `Services/GroupSyncService.cs` – Aggregates imports, live refreshes, and sync/delete operations.
- `Pages/Index.razor` & `.cs` – Primary dashboard logic.
- `Shared/Dialogs/*.razor` – Add/remove/broadcast interactions surfaced via MudBlazor dialogs.
- `Authentication/AuthStateProvider.cs` – Authentication state management (replace with a real provider for production).

## Logging & Diagnostics
- HTTP traffic to Whapi is traced via `LoggingHandler`, capturing timing, status, and payload sizes.
- `Serilog` enrichers attach application, machine, and thread context to every log entry.
- Request logging template: `HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms`.
- Review rolling log files under `logs/` (14-day retention by default).

## Development Workflow
- **UI changes:** Most Blazor components live under `Pages/` and `Shared/`.
- **Dialog patterns:** Extend `DialogManager` when adding new dialogs so callbacks stay centralized.
- **Service abstractions:** Implement `IService` to connect to a real backend; swap the DI registration in `Program.cs`.
- **Tests:** No automated tests are present yet—consider adding unit tests around `GroupSyncService` diffing logic and `WhapiService` call wrappers.

## Security Considerations
- Replace the hard-coded admin credential immediately; integrate with your identity provider or environment-specific secrets.
- Never commit production API keys to source control—`appsettings.json` is currently storing a placeholder key.
- Review log data before sharing—phone numbers are masked, but group names and message bodies are not.

## Deployment Notes
- Deploy the `.NET 8` server to your preferred host (Azure App Service, IIS, Docker, etc.).
- Ensure the Whapi API key is available as an environment variable and that outbound HTTPS traffic to `https://gate.whapi.cloud` is permitted.
- Configure persistent storage (or a shared volume) if you rely on `contacts.json` or log retention across instances.

## Roadmap Ideas
- Swap `MockService` for a production data source.
- Expand authentication (e.g., Azure AD, IdentityServer, or OAuth providers).
- Add automated tests and CI/CD.
- Surface sync history or audit trails for member changes.
- Harden phone normalization and locale support for the CSV pipeline.


