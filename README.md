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
- Exceptionless (error/event reporting)

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
- `BlueSky`
  - Service URL and live profile prefix.
- `Discord`
  - Bot token, bot client ID, invite permissions/scopes.
- `Encryption`
  - Salt used by credential encryption at rest.
- `Exceptionless`
  - API key and server URL.

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
4. Configure timed chat messages.
5. Add Discord sync target:
   - Use the in-app Invite Discord Bot button first.
   - Paste a full Discord channel URL (recommended), or enter guild/channel IDs manually.

## Overlay Endpoint

Overlay is anonymous by design for OBS/browser source usage:

- `/overlay/{token}`

Each streamer has a unique overlay token.

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