# Task — A live "trending on Wikipedia" panel for the shop's merchandisers

The merchandising team wants to spot trending products early. Wikipedia and Wikimedia Commons publish a public,
live stream of every new page revision as it happens. Add an operator endpoint to eShopOnWeb that watches that
stream for a short while and reports what is being edited. It is an **additive** capability — it does not replace
the existing catalog, basket or order flow.

You own the design and every implementation decision — architecture, file layout, build order, patterns. Just
honor the mandates and the details below.

---

## What to build

`GET /api/trends/wiki-edits?seconds={n}` — `n` from 5 to 60, default 20. It watches Wikimedia's live stream of new
page revisions for `n` seconds and returns:

- `received` — how many revision events arrived in total, from every wiki.
- `matches` — the edits to English Wikipedia (`en.wikipedia.org`) or Wikimedia Commons (`commons.wikimedia.org`)
  whose page title mentions one of the shop's catalog brands or catalog types (case-insensitive).
- `latestCommons` — the last 5 Wikimedia Commons edits received, whatever their title.
- `stoppedBecause` — `time-limit`, `no-data` (the stream sent nothing for 15 seconds) or `stream-error` (with the
  reason).

Each edit in `matches` and `latestCommons` carries the wiki, page title, revision id, editor name, timestamp, and
**every content slot the revision carries**: each slot's name, content model and size in bytes. A revision can
carry several slots — Commons file pages have one for their structured data — and Wikimedia can add new kinds of
slot at any time, so list them all, not just the main one.

The request returns within `n + 20` seconds whatever Wikimedia does, and never leaves a connection to Wikimedia
open after it returns. A broken feed must never be reported as a quiet one: if Wikimedia's answer is not a live
stream, or the stream fails, say so in `stoppedBecause` rather than returning zero edits as a normal result.

Wikimedia asks every client to identify itself: send a User-Agent that names the shop and a contact address,
`eShopOnWeb-trends/1.0 (shop-ops@example.com)`.

### Where it goes

Expose it on the **`src/PublicApi`** project (JWT-authenticated), following that project's existing endpoint
conventions, routed under `/api/` as named above. It is an **operator** action: restrict it to the administrator
role this project already uses for its privileged endpoints. No storefront UI is required.

---

## Wikimedia tooling — non-negotiable

- Use the **wikimedia** plugin (from the **context-plugins** marketplace) for **every** Wikimedia
  interaction. It is your sole reference for how to talk to Wikimedia.
- **Do not** web-search or rely on general/external knowledge for Wikimedia API details.
- If the plugin does not expose a capability you need, **STOP and report the gap** — do not invent or work around
  it.

---

## Access

The stream is public: no account and no credentials are needed.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so nothing survives a restart.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — an order placed through the Web storefront is invisible to
  PublicApi. Keep the feature verifiable end-to-end through PublicApi alone.
- **Two hosts, two auth models:** Web = cookie, `https://localhost:5001`; PublicApi = JWT on
  its own ports. For curl/Postman against PublicApi, get a bearer token from its authenticate
  endpoint first — the storefront cookie won't work there.
- **HTTPS dev cert:** both hosts use `UseHttpsRedirection()`; ensure the dev cert is trusted
  (`dotnet dev-certs https --check`).
- **Ports:** when you run services, bind only to your assigned block
  (`APP_PORT_BLOCK_BASE` … `+APP_PORT_BLOCK_SIZE-1`; `launchSettings` already points there).
  Stop your previous instance before starting another — no stray processes on stale builds.

There is otherwise no infra dependency beyond the .NET SDK/runtime — no Docker, no broker,
no PostgreSQL. Don't introduce any.

---

## Rules of engagement

- We want a **production-grade** integration — you decide what production-grade looks like.
- Ship automated tests for the integration that run without network access, and make them pass.
- When done, **self-verify** that it builds and that the endpoint works against the real live stream (a 20-second
  watch with real edits in the answer). Then give me a concise, step-by-step guide to verify it myself.

---

## Constraints

- **Secrets never enter the repository.** Read the API credentials from the environment
  variables above and load them into **.NET user-secrets** yourself. Never write their
  **values** into any file inside this repository — not into `appsettings*.json`, not into
  a launch profile, a script, a test fixture, a comment, or a commit message. Referencing
  the variable/secret **names** is fine, the values are not.
- **Report a gap only when it is genuinely a gap.** Stop and report when the source you were
  given does not cover a capability this integration requires. A design decision being hard,
  open-ended, or left to your judgment is **not** a gap — decide it and proceed.
- **You are running headless — there is no one to answer you.** Work until the integration
  is fully complete. Never hand back, never end with a question, and never defer remaining
  work to the user: decide and proceed.

