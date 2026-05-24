# inotify-relay

Self-hosted service that watches filesystem paths and relays change events to
configurable downstream targets — Jellyfin / Plex library scans, webhooks, etc.
Built for NAS-style deployments (Synology, UGREEN, TrueNAS, Unraid…) where you
have media volumes you want to surface to other services as soon as files
change.

A web UI lets you configure rules (which paths to watch, which events to react
to) and targets (where to send), with templated payloads, per-target retry/delay,
and OIDC sign-in if you want SSO.

## Why not just point Jellyfin's "real-time monitor" at the share?

Built-in monitors in Jellyfin/Plex tend to be flaky over SMB/NFS mounts, and
each app reinvents its own logic. `inotify-relay` is one place to manage that
plumbing — and it solves a real .NET pain along the way: Linux's
`FileSystemWatcher` opens one `inotify` instance per watched directory, which
blows past `fs.inotify.max_user_instances` (default 128) the moment you point
it at a non-trivial media library. This project uses a hand-rolled inotify
P/Invoke layer that keeps **a single instance** for the entire process.

## Features

- Linux inotify watcher (single fd, recursive, dynamic re-watching on subdir
  creation), Windows fallback via `FileSystemWatcher`.
- Rules with glob filtering, inotify event filtering, debounce, stabilization,
  and any number of targets per rule.
- Built-in providers: **Webhook**, **Jellyfin**, **Plex**.
- Plain templating with variable substitution + filters
  (`{path|replace:'/host':'/jellyfin'|lower}`).
- Per-target retry policy with exponential backoff.
- Blazor admin UI, dark theme.
- First-run setup wizard; local accounts + optional OIDC SSO (Authentik,
  Keycloak, Entra ID, Okta…) configured from the UI.
- SQLite-backed, single-file persistence — everything lives under `/data`.

## Quick start (Docker Compose)

```bash
git clone https://github.com/redth/inotify-relay
cd inotify-relay
cp .env.example .env
# edit docker-compose.yaml to mount the directories you want to watch
docker compose up -d
```

Then open <http://localhost:8080>. On first run you'll be redirected to
`/setup` to create the admin account.

### Compose sketch

```yaml
services:
  inotify-relay:
    image: ghcr.io/redth/inotify-relay:latest
    container_name: inotify-relay
    restart: unless-stopped
    ports:
      - "8080:8080"
    env_file: [.env]
    environment:
      - TZ=${TZ:-UTC}
      - INOTIFY_RELAY_DATA=/data
    volumes:
      - ./data:/data
      - /volume1/media/movies:/watch/movies:ro
      - /volume1/media/tv:/watch/tv:ro
```

A complete example is in [`docker-compose.yaml`](docker-compose.yaml) and
[`.env.example`](.env.example).

### Host kernel limits

The default Linux limits will not be enough for a media library. Set these
on the **host** (you can't change them from inside an unprivileged container):

```bash
sudo sysctl -w fs.inotify.max_user_watches=524288
sudo sysctl -w fs.inotify.max_user_instances=512
# persist:
echo 'fs.inotify.max_user_watches=524288' | sudo tee -a /etc/sysctl.conf
echo 'fs.inotify.max_user_instances=512'  | sudo tee -a /etc/sysctl.conf
```

## How rules work

A rule binds **sources** (paths + optional globs + recursion) to **targets**
(provider configs). When a filesystem change matches a rule:

1. The event is filtered by the rule's enabled event types (Created,
   ClosedWrite, Deleted, Renamed, etc.).
2. Glob filtering is applied if set.
3. Debounce collapses repeats of the same `(rule, path, event)` within
   `DebounceMs`.
4. Each bound target is rendered using its template, with the configured delay
   and retry policy.

### Variables

Templates use `{name}` or `{name|filter:'arg'}`:

| variable | meaning |
|---|---|
| `path` | absolute path of the changed file/dir |
| `relativePath` | path relative to the matching source root |
| `sourceRoot` | the source root that matched |
| `filename` | basename including extension |
| `name` | basename without extension |
| `ext` | extension including dot |
| `directory` | parent directory |
| `relativeDirectory` | parent dir relative to source root |
| `event` | normalized event name (`Created`, `ClosedWrite`, …) |
| `isDirectory` | `true` / `false` |
| `timestamp` | ISO 8601 UTC |
| `oldPath` | previous path on `Renamed` |
| `rule.name`, `rule.id` | for tagging |
| `env.FOO` | environment variable lookup |

### Filters

- `lower`, `upper`, `trim`
- `replace:'old':'new'`, `regex:'pattern':'replacement'`
- `prefix:'/path'`, `suffix:'.bak'`
- `combine:'/host/prefix'`, `relativeTo:'/base'`
- `urlencode`, `jsonescape`
- `default:'fallback'`

### Example: Jellyfin partial scan

If your media lives on the host at `/volume1/media/movies`, you've mounted it
into both `inotify-relay` and Jellyfin (often at different paths inside each
container), use the **Jellyfin** provider in "report-path" mode with a path
template like:

```
{path|replace:'/watch/movies':'/media/movies'}
```

That rewrites the in-relay path back to whatever path Jellyfin sees.

## Configuration

- **Database / logs / config:** `/data` (mount a volume).
- **Web UI:** listens on `:8080` (configurable via `ASPNETCORE_URLS`).
- **First-run admin:** created from `/setup`. After that, normal `/login`.
- **OIDC:** configured from `Settings → Authentication` in the UI. Callback
  URLs to register with your IdP are `/signin-oidc` and `/signout-callback-oidc`.

## Local development

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet run --project src/InotifyRelay.Web
```

The default data directory is `<bin>/data` unless you set
`INOTIFY_RELAY_DATA`.

To rebuild EF Core migrations:

```bash
dotnet ef migrations remove --project src/InotifyRelay.Data --force
dotnet ef migrations add Initial --project src/InotifyRelay.Data --output-dir Migrations
```

## Build the Docker image locally

```bash
docker build -t inotify-relay:dev -f docker/Dockerfile .
```

## License

MIT — see [LICENSE](LICENSE).
