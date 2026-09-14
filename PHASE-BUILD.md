# Task — Add Maxio subscription billing to eShopOnWeb

Add recurring-subscription billing to the eShopOnWeb reference app, with **Maxio Advanced
Billing** as the billing system of record. eShopOnWeb today is one-time commerce
(Catalog → Basket → Order); this is an **additive, parallel** capability — it does not
replace the existing cart/checkout flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

**The hero flow: Subscribe.**
A logged-in shopper browses available plans, subscribes to one, and sees it reflected in
their account. Ensure a Maxio customer exists for the eShopOnWeb user (idempotent, so a
double-click never creates two customers/subscriptions), enroll them, and confirm
plan/price/state/next-billing-date back to the user.

**Where it goes.** Expose the capability as HTTP endpoints on the **`src/PublicApi`**
project (JWT-authenticated; the caller's identity comes from the token), following that
project's existing endpoint conventions. Route them under `/api/` named for the capability —
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.

---

## Maxio tooling — non-negotiable

- Maxio's **OpenAPI specification** — located in the **`maxio-spec/`** folder — is the
  **authoritative contract** for **every** Maxio interaction. Endpoints, path/query params,
  request and response schemas, auth scheme, server/base-URL templating, and error models
  all come from the spec. How you consume it — codegen a client or hand-write against it —
  is your call, as long as the spec is the contract you build to.
- You **may** consult official Maxio documentation as a **secondary** reference to clarify
  semantics or fill in behavior the spec describes ambiguously. The **spec is authoritative**:
  where the spec and any doc/web source conflict, the spec wins. Do not build against
  endpoints, fields, or shapes that don't appear in the spec.
- If the spec genuinely does not cover a capability you need — and official docs don't
  resolve it — **STOP and report the gap**. Do not invent endpoints/fields or work around
  the contract.

---

## Sandbox entities (already seeded on site `cp-exp-2`)

The demo catalog already exists — no need to create it. **Handles are stable; numeric IDs
are not** — Maxio reassigns them on re-seed, so the IDs below may already be stale.

| Entity | Handle | ID (current) | Notes |
|--------|--------|--------------|-------|
| Product Family | `eshop-subscribe` | 3023074 | Container for the plans + component |
| Pro Plan | `eshop-pro` | 7126957 | $299.00/mo — default subscribe target |
| Basic Plan | `basic-plan` | 7126958 | $29.00/mo — alternate plan (also seeded) |
| Metered component | `api-call` | 3057195 | Metered, $0.01/unit — also seeded on the family |

Both plans: no trial, no setup fee, expires never, taxable no, **payment method not
required** (so subscribe works without card capture / 3-DS).

---

## Credentials

- Sandbox credentials arrive as env vars: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`,
  `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`.
- Target the Maxio **sandbox** for all development and testing.
- **Bind settings from the `Maxio:` configuration section using exactly these keys**, and
  hard-code none of their values — the same build has to run against a different Maxio site
  and a different catalog than the one above: `Maxio:ApiKey` (from `MAXIO_API_KEY`),
  `Maxio:Subdomain` (from `MAXIO_SITE_SUBDOMAIN`), `Maxio:ProductFamilyHandle` (from
  `MAXIO_DEFAULT_PRODUCT_FAMILY`), and `Maxio:BaseUrl`.
- `Maxio:BaseUrl` is an optional override: when it is set, use it verbatim as the API base
  address instead of deriving one from the subdomain.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so a persisted userId ↔ subscription
  mapping only survives within a single run.
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
- When done, **self-verify** that it builds and the flows actually work — then give me a
  concise, step-by-step guide to verify the working integration myself.

---

## Constraints

- **Secrets never enter the repository.** Read the Maxio credentials from the environment
  variables above and load them into **.NET user-secrets** yourself. Never write their
  **values** into any file inside this repository — not into `appsettings*.json`, not into
  a launch profile, a script, a test fixture, a comment, or a commit message. This clone is
  published; referencing the variable/secret **names** is fine, the values are not.
- **You are running headless — there is no one to answer you.** Work until the integration
  is fully complete. Never hand back, never end with a question, and never defer remaining
  work to the user: decide and proceed.

