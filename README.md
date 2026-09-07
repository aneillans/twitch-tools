# Twitch Tools

Twitch Tools is an ASP.NET Core MVC app for streamers.

It provides:

- Live status monitoring and event tracking.
- BlueSky live notifications and profile indicator updates.
- Timed Twitch chat messages.
- Discord schedule sync targets for each streamer.
- Viewer time monitoring while live.
- An anonymous overlay endpoint for OBS/browser sources.

## Tech Stack

- .NET 10 (ASP.NET Core MVC)
- PostgreSQL
- EF Core (Npgsql)
- Keycloak (OIDC authentication)
- OpenTelemetry (traces/metrics/logs, exported via OTLP)

## Project Layout

- `src/TwitchTools.Web` - web app, controllers, services, EF Core model/migrations.
- `docker-compose.yml` - local full stack (web + postgres + keycloak).
- `docker/keycloak/realm-twitch-tools.json` - sample realm, client, and users.
- `Dockerfile` - production-style multi-stage app container build.

## Prerequisites

For local app execution:

- .NET SDK 10
- PostgreSQL 16+
- A Keycloak realm/client (or run Keycloak via compose)

For containerized execution:

- Docker + Docker Compose, or Podman + Podman Compose

## Quick Start (Recommended: Full Stack)

1. Copy environment defaults:

```bash
cp .env.example .env
```

2. Edit `.env` and set at minimum:

- `ENCRYPTION_SALT` to a long random value.
- `TWITCH_CLIENT_ID` and `TWITCH_CLIENT_SECRET` if using Twitch OAuth connect flow.
- `DISCORD_BOT_CLIENT_ID` if you want the in-app Discord invite link.
- `FEATURE_DISABLE_EXTERNAL_POSTING=true` to run a non-posting test mode.
- `FEATURE_ENABLE_EVENTSUB_PAYLOAD_LOGGING=true` to log full EventSub payloads for debugging.
- `FEATURE_EVENTSUB_PAYLOAD_RETENTION_DAYS=14` to control how long payload logs are retained.

3. Start the stack:

Docker:

```bash
docker compose up --build
```

Podman:

```bash
podman compose up --build
```

4. Open applications:

- App: http://localhost:8080
- Keycloak: http://localhost:8081

5. Sign in with sample users imported from realm JSON:

- Admin user:
  - Username: `streamer-admin`
  - Password: `streamer-admin`
- Normal user:
  - Username: `streamer-user`
  - Password: `streamer-user`

## Local Development (App Outside Containers)

1. Ensure PostgreSQL is running and create a database (for example `twitchtools_dev`).
2. Configure settings in `src/TwitchTools.Web/appsettings.Development.json` and/or environment variables:

- `ConnectionStrings__DefaultConnection`
- `Keycloak__Authority`
- `Keycloak__MetadataAddress` (optional; useful when app and IdP are on different network paths)
- `Keycloak__ClientId`
- `Keycloak__ClientSecret`
- `Encryption__Salt`

3. Restore tools and dependencies:

```bash
dotnet tool restore
dotnet restore
```

4. Run the app:

```bash
dotnet run --project src/TwitchTools.Web
```

The app applies EF Core migrations automatically on startup.

## Configuration Reference

Main app settings are in:

- `src/TwitchTools.Web/appsettings.json`
- `src/TwitchTools.Web/appsettings.Development.json`

Important sections:

- `Keycloak`
  - OIDC authority and client settings.
  - `MetadataAddress` can be set for container backchannel scenarios.
- `Twitch`
  - API base URLs, OAuth client settings, scopes, and redirect URI.
- `YouTube`
  - Google OAuth client settings, scopes, and redirect URI for the YouTube Data API v3.
  - Requires a Google Cloud project with the **YouTube Data API v3** enabled and an OAuth 2.0
    client (Web application type). Add the redirect URI (`.../my-tools/connect/youtube/callback`)
    as an authorized redirect URI on that client, and configure an OAuth consent screen requesting
    the `youtube.readonly` and `youtube.force-ssl` scopes.
  - `youtube.readonly` and `youtube.force-ssl` are Google-restricted scopes; while the OAuth
    consent screen is in Testing publishing status this only works for the test users you add in
    the Google Cloud console. Moving to a verified production app requires Google's OAuth
    verification process.
  - `NotLivePollIntervalSeconds` / `MinChatPollIntervalSeconds` control how often the background
    poller checks for a new live broadcast and, once live, the floor applied to YouTube's own
    reported chat polling interval. The YouTube Data API has a default quota of 10,000 units/day
    per Google Cloud project, shared across every streamer connected to this deployment - tune
    these (or request a quota increase from Google) if you have many concurrently-live streamers.
- `BlueSky`
  - Service URL and live profile prefix.
- `Discord`
  - Bot token, bot client ID, invite permissions/scopes.
- `Encryption`
  - Salt used by credential encryption at rest.
- `FeatureFlags`
  - `DisableExternalPosting` disables outward posting side effects (BlueSky publish/profile updates, Discord event create/update/delete sync, timed Twitch chat sends, EventSub subscription creation, YouTube live chat sends, and Twitch/YouTube chat cross-posting).
  - Environment variable override: `FeatureFlags__DisableExternalPosting=true`.
  - `EnableEventSubPayloadLogging` logs full EventSub request payloads (including chat message events) for debug tracing.
  - Environment variable override: `FeatureFlags__EnableEventSubPayloadLogging=true`.
  - `EventSubPayloadRetentionDays` controls automatic cleanup of old EventSub payload debug rows; default is `14` days. Set `0` or a negative value to disable automatic pruning.
  - Environment variable override: `FeatureFlags__EventSubPayloadRetentionDays=14`.
- `Site`
  - `SupportContactEmail` shown on the Privacy Policy page.
  - Environment variable override: `Site__SupportContactEmail`.
- `OpenTelemetry`
  - Always-on instrumentation (traces, metrics, and structured logs) for ASP.NET Core requests, outbound HTTP calls, and .NET runtime metrics; `service.name` is fixed to `twitch-tools-web`.
  - Exporter destination/protocol/headers are configured via the standard OTel SDK environment variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS`) so this can be pointed at any existing OTel collector or OpenSearch Data Prepper OTLP pipeline without code changes.
  - `docker-compose.yml` runs a local, self-contained `otel-collector` (config at `docker/otel/config.yaml`) that just prints telemetry to its own logs (`docker compose logs otel-collector`) - handy for confirming instrumentation works without any OpenSearch dependency. Override `OTEL_EXPORTER_OTLP_ENDPOINT` to forward to a real collector/Data Prepper pipeline instead.
  - Note: `IncludeScopes` is deliberately left `false` for the log exporter - ASP.NET Core's nested logging scopes repeat the same attribute keys (e.g. `HttpMethod`), and OTLP-based backends such as OpenSearch Data Prepper reject the entire log record on a duplicate attribute key.

## Authentication and Roles

- OIDC login is handled through Keycloak.
- Users with realm role `admin` can access admin pages.
- Non-admin users can still configure their own streamer tools in My Tools.

Default realm/client/users are defined in:

- `docker/keycloak/realm-twitch-tools.json`

## Streamer Onboarding Flow

After login:

1. Go to My Tools (`/my-tools`).
2. Save streamer profile values.
3. Connect Twitch via OAuth (if configured).
4. Optionally connect YouTube via OAuth to merge YouTube live chat into the overlay widget feed.
5. Optionally connect a Twitch bot account and a YouTube bot account, then enable chat
   cross-posting on the Live Automation page.
6. Configure timed chat messages.
7. Add Discord sync target:
   - Use the in-app Invite Discord Bot button first.
   - Paste a full Discord channel URL (recommended), or enter guild/channel IDs manually.

## Overlay Endpoint

Overlay is anonymous by design for OBS/browser source usage:

- `/overlay/{token}`

Each streamer has a unique overlay token.

### Merged Twitch + YouTube chat feed

The custom widget overlay's SSE event stream (`/overlay/widgets/{token}/events`) carries chat
messages from Twitch (via EventSub) and, once a streamer connects YouTube, from YouTube live chat
(polled in the background - YouTube has no webhook/push equivalent of Twitch EventSub for chat).
Both publish onto the same per-streamer stream, so a custom widget sees one merged feed while both
platforms are live. Each published message includes a top-level `platform` field (`"twitch"` or
`"youtube"`) alongside the existing `listener`/`event` fields, so custom widget JS can style or
filter by source if it wants to; existing widgets that only read `event.data` are unaffected.

### Twitch ↔ YouTube chat cross-posting

Streamers can optionally mirror chat messages between Twitch and YouTube using a dedicated bot
account on each platform (Live Automation page). This requires connecting *both* a Twitch bot
account and a YouTube bot account first. Mirrored messages are prefixed with a configurable
template (default `[{platform}] {user}: {message}`) since the bot can't post as the original
author. A message authored by the streamer's own configured bot identity on either platform is
never mirrored again and never republished into the merged overlay feed, which is what prevents
both an infinite mirror loop and duplicate lines showing up in the widget.

## Useful Commands

Build solution:

```bash
dotnet build TwitchTools.slnx
```

Run web app project:

```bash
dotnet run --project src/TwitchTools.Web
```

Stop and remove local compose resources:

Docker:

```bash
docker compose down -v
```

Podman:

```bash
podman compose down -v
```