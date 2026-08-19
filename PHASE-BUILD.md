# Task — Add supplier-catalog sync to eShopOnWeb

Give the eShopOnWeb reference app a way to bring a supplier's product listing into its own
catalog without anyone re-typing it by hand. Some of the store's suppliers offer no API or
data feed — only a product listing page — so the store has to go get the data itself. This
adds that capability using **Firecrawl** as the way of reading a supplier's page. It is an
**additive** capability — it does not replace the existing catalog, basket or order flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

### Flow — Sync a supplier's catalog

An operator registers a supplier, tells the app where that supplier's product listing lives,
and starts a sync. The app reads the listing, and for every product it finds captures at
least its **name, description, price and brand**, matching them into the store's own catalog.

- `POST /api/catalog/suppliers` — register a supplier: a name and the URL of its product
  listing page. The response identifies the supplier as a top-level field: `supplierId`.
- `POST /api/catalog/suppliers/{supplierId}/sync` — start a sync of that supplier's listing.
  This does not have to finish before the call returns. The response identifies the sync as a
  top-level field: `syncId`.
- `GET /api/catalog/syncs/{syncId}` — the status and outcome of one sync. The response must
  let a caller tell, without guessing, whether the sync is still running, finished having
  captured the supplier's whole listing, or finished having captured only part of it — and
  exactly how many products it found versus how many it actually brought into the catalog.
  Report these as top-level fields: `status`, `itemsFound`, `itemsImported`.
- Reuse the app's **existing catalog listing** to show the resulting items, rather than
  building a parallel one.

Each product found is matched against the catalog by the supplier's own identifier or URL for
it, so running a sync again updates the same catalog item instead of creating a second one —
running the same sync twice must never duplicate a product already imported.

### Where it goes

Expose this as HTTP endpoints on the **`src/PublicApi`** project, following that project's
existing conventions, routed under `/api/` as named above. Registering a supplier and starting
or reading a sync are **operator** actions: restrict them to the administrator role this
project already uses for its privileged endpoints. No storefront UI is required.

---

## Firecrawl tooling — non-negotiable

- Firecrawl's **OpenAPI specification** — located in the **`firecrawl-spec/`** folder — is the
  **authoritative contract** for **every** Firecrawl interaction. Endpoints, request and
  response schemas, auth scheme, and error models all come from the spec. How you consume it —
  codegen a client or hand-write against it — is your call, as long as the spec is the contract
  you build to.
- Do **not** install a pre-built Firecrawl SDK or client package from NuGet or anywhere else. A
  client you generate from `firecrawl-spec` or write by hand is fine; someone else's client is
  not the contract you were given.
- You **may** consult official Firecrawl documentation as a **secondary** reference to clarify
  semantics or fill in behavior the spec describes ambiguously. The **spec is authoritative**:
  where the spec and any doc/web source conflict, the spec wins. Do not build against
  endpoints, fields, or shapes that don't appear in the spec.
- If the spec genuinely does not cover a capability you need — and official docs don't
  resolve it — **STOP and report the gap**. Do not invent endpoints/fields or work around
  the contract.
---

## Sandbox entities & test fixtures

Nothing is pre-seeded on the catalog side beyond eShopOnWeb's own sample catalog. You will be
given the URL of a small, real, publicly reachable product listing built for this task. Verify
the whole flow — register it as a supplier, sync it, and see the resulting items land in the
catalog — against that listing.

---

## Credentials

- The Firecrawl API key arrives as an env var: `FIRECRAWL_API_KEY`.
- **Bind it from the `Firecrawl:` configuration section using exactly this key**, and hard-code
  no value — the same build has to run against a different Firecrawl account than this one:
  `Firecrawl:ApiKey` (from `FIRECRAWL_API_KEY`), and `Firecrawl:BaseUrl`.
- `Firecrawl:BaseUrl` is an optional override: when it is set, use it verbatim as the API base
  address for every Firecrawl call instead of deriving one from the environment.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so suppliers, syncs and imported catalog
  items only survive within a single run. Register, sync and verify within that same run.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — a supplier registered through the Web storefront (if any)
  is invisible to PublicApi. Keep the whole flow verifiable end-to-end through PublicApi
  alone.
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
- When done, **self-verify** that it builds and the flow actually works — register the
  supplier, run a real sync against the fixture listing, and see the items land in the
  catalog. Then give me a concise, step-by-step guide to verify the working integration
  myself.

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

