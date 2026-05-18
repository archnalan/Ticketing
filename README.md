# Ticketing

Standalone .NET 10 kanban + OTel-driven ticketing platform.

Provider-agnostic: ingests OTLP/HTTP JSON logs from any source, dedupes by exception fingerprint, and turns severity ≥ ERROR records into draggable tickets. Also exposes a `/api/tickets/from-feedback` endpoint that any upstream service can POST to so user feedback rolls up as tickets.

## Projects

| Project | Type | Purpose |
|---|---|---|
| `Ticketing.Api` | ASP.NET Core Web API (port 8090) | OTLP `/v1/logs` receiver, ticket CRUD, feedback ingest, AI summarization, digest emails |
| `Ticketing.Web` | Blazor Server (port 5090) | Kanban UI: Backlog → Todo → InProgress → Review → Deployed, with Errors/Feedback tabs |

Both target `net10.0`. Solution file: `Ticketing.slnx`.

## Build & Run

```powershell
dotnet restore Ticketing.slnx
dotnet build Ticketing.slnx -c Debug

# Run API (requires SQL Server)
cd Ticketing.Api && dotnet run

# Run Web (port 5090 by default)
cd Ticketing.Web && dotnet run
```

## Docker

Each project has its own Dockerfile at the solution root: `Dockerfile.api`, `Dockerfile.web`. The build context is `./` (this repo root). Docker compose orchestration currently lives in the upstream consumer's repo (`FRELODY/docker-compose.yml`) which references `../Ticketing` as the build context for both images. Move the compose stanzas here if you want Ticketing to run independently.

## Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:TicketsData` | SQL Server connection string |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:Key` | Token validation — must match whichever identity provider issues the JWTs you trust |
| `Tickets:IngestSecret` | Shared header secret that `/api/tickets/from-feedback` requires |
| `OtlpReceiver:Secret` | Optional shared header secret on `/v1/logs` |
| `API_KEYS:nvidiaApiKey` | NVIDIA NIM key for AI-generated titles/descriptions on error tickets (optional — falls back to raw text) |
| `Smtp:*` | SMTP host/port/user/password/from for digest emails |
| `Digest:SuperAdminEmail` | Recipient for the per-source 5-ticket digest (env: `DIGEST_SUPERADMIN_EMAIL`) |
| `Digest:ThresholdPerSource` | How many new tickets per source trigger a digest (default 5) |

## Auth

Currently validates JWTs issued by an external identity provider. Any consumer wishing to integrate authenticates via that provider, then forwards the access token. The Web app proxies its login through `Apis:Frelody` (configurable to any auth backend).

The single page is gated to the `SuperAdmin` role.
