# todomcp

A Model Context Protocol (MCP) server that lets an MCP client — Claude Desktop,
a coding agent, or any other harness — call a tiny, deterministic tool surface
over HTTP.

## End goal

Todomcp is a reference MCP server: small enough to read end-to-end, complete
enough to show the whole loop (config → health → tools → SSE transport).

When finished it will be:

- **A .NET 10 Web app** that serves MCP over HTTP SSE (the AspNetCore MCP
  integration), packaged as a single Docker image.
- **A stable tool contract:** `hello` (echo a name), `get_flag` (return the
  flag from config), and `list_todos` / `add_todo` (an in-memory todo list the
  client can read and append to). Tools are validated at the API boundary.
- **Deployable anywhere** a container runs, with the compose file choosing the
  port. Runs the same in CI, in docker compose, and on the Arcane stack.
- **Auditable:** every dependency is lockfile-pinned, actions are SHA-pinned,
  and the image gets a Trivy scan before it is pushed.

### Users

- Developers wiring an MCP client and wanting a predictable, boring server to
  point at.
- Anyone reviewing how an MCP tool server is shaped in C#.

### Features (target)

- `GET /health` → `Healthy`
- MCP `hello(name)` → `"Hello, <name>!"`
- MCP `get_flag()` → the flag configured via `Flag:Value` / `FLAG` env var
- MCP `list_todos()` / `add_todo(text)` → in-memory list across calls
- Strongly-typed DTOs at the tool boundary, validated
- Docker image built and scanned in CI/release, pushed to GHCR

## Phases

- **Phase 0 — MVP: `hello`, `get_flag`, health.** ✅ done
  - .NET 10 MCP endpoint, `hello` + `get_flag` tools, `/health`
  - xUnit v3 unit + integration tests (6 passing)
  - Multi-stage Dockerfile, lockfiles
- **Phase 1 — Repo hygiene & publishing.** ← current
  - CI (build/format/test/docker), CodeQL, dependabot, README, SHA-pinned
    actions, lockfile restore
  - Release workflow pushing a GHCR image + Trivy scan
- **Phase 2 — Todo list tools.** planned
  - `list_todos` / `add_todo`, in-memory store, validation, tests
- **Phase 3 — Hardening.** planned
  - Structured logging, request logging, timeout & cancellation plumbing

## Getting started

Prerequisites: .NET 10 SDK, Docker.

```bash
dotnet run --project src            # or: dotnet watch --project src
```

The MCP endpoint is exposed at `/` (SSE). Health is at `/health`.

### Tests

```bash
dotnet test
```

### Docker

```bash
docker build -t todomcp:ci .
docker run --rm -p 8080:8080 todomcp:ci
```

The `Flag:Value` flag is read from the `Flag:Value` config key or the `FLAG`
environment variable, and defaults to `FLAG-lightbulb-1234`.

## Deviations

None from `docs/policy.md` (default stack).
