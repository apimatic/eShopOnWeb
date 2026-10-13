# Task — Offer the eShopOnWeb catalog's stock on Walmart Marketplace

Make the eShopOnWeb reference app keep **Walmart Marketplace** told how much of its catalog it
has to sell, so Walmart can be a second sales channel. eShopOnWeb today sells only through its
own storefront: it has no idea how much of anything it holds, and nothing outside the app knows
either. This adds a stock level per catalog item, the shop's stock on Walmart, and the
operator's view of whether the two agree. It is an **additive** capability — it does not replace
the existing catalog/basket/order flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

Each eShop catalog item is sold on Walmart under a seller SKU. The SKU for a catalog item is
`ESHOP-{catalogItemId}` (catalog item 7 is `ESHOP-7`).

### Flow 1 — One item's stock

- `PUT /api/walmart/stock/{sku}` — set how many of that item the shop offers on Walmart. The
  request carries `quantity`. Afterwards Walmart's own record of the SKU's quantity is that
  number. It does not change eShop's own level for the item.
- `GET /api/walmart/stock/{sku}` — the SKU's stock as Walmart currently records it — Walmart's
  own record, not a copy this application kept from when it was set.

### Flow 2 — The whole catalog at once

- `POST /api/walmart/stock/sync` — push the stock level of **every** item in the eShop catalog
  to Walmart. The caller makes one request, which carries nothing; the levels are the ones
  eShop holds, and an item whose level was never set has a level of 0. How this application
  talks to Walmart to do it is your call. The response reports, per SKU, what Walmart did with
  it.
- eShop's stock level for a catalog item is set through `PUT /api/catalog-items/{catalogItemId}/stock`
  (request carries `quantity`), and placing an order through `POST /api/orders` (catalog item
  ids and quantities; reuse the app's existing order/order-item model rather than a parallel
  one) takes the ordered quantities off eShop's stock. An order for more of an item than eShop
  holds is refused. Neither of these two calls Walmart by
  itself — Walmart learns the new levels at the next sync.

### Flow 3 — Do Walmart and eShop agree?

- `GET /api/walmart/stock` — a report listing **every** SKU Walmart holds stock for, lined up
  against eShop's own level for the matching catalog item, so a SKU whose two numbers differ —
  or that exists on one side only — is visible. It covers all of them, not only the first few.

### Where it goes

Expose all capabilities as HTTP endpoints on the **`src/PublicApi`** project
(JWT-authenticated; the caller's identity comes from the token), following that project's
existing endpoint conventions, routed under `/api/` as named above. Every flow above has to
be drivable through that API alone, and each action a caller can take stays separately
invocable. No storefront UI is required.

Every endpoint above except `POST /api/orders` is an **operator** action: restrict it to the
administrator role this project already uses for its privileged endpoints. `POST /api/orders`
is available to any signed-in shopper.

### Response identifiers

These fields are top-level in the response body: `orderId` from `POST /api/orders`; `sku` and
`quantity` from `PUT` and `GET /api/walmart/stock/{sku}`; `results` from
`POST /api/walmart/stock/sync`, one entry per SKU, each carrying its `sku` and a `status` of
`updated` or `failed`; and `items` from `GET /api/walmart/stock`, one entry per SKU, each
carrying its `sku`, `walmartQuantity` and `eshopQuantity` (`null` on the side that has no such
SKU). Everything else about the response shape is your call.

---

## Walmart tooling — non-negotiable

- Walmart's **OpenAPI specification** — located in the **`api-specs/`** folder, as one
  document per Walmart API — is the **authoritative contract** for **every** Walmart
  interaction. Endpoints, path/query params, request and response schemas, auth scheme,
  server/base-URL templating, and error models all come from the spec. How you consume it —
  codegen a client or hand-write against it — is your call, as long as the spec is the
  contract you build to. Working out which of the documents this task needs is part of the job.
- Do **not** install a pre-built Walmart SDK or client package from NuGet or anywhere else. A
  client you generate from `api-specs` or write by hand is fine; someone else's client is
  not the contract you were given.
- **Do not** web-search or rely on general/external knowledge for Walmart API details. The
  specification is your sole reference for how to talk to Walmart.
- If the spec genuinely does not cover a capability you need, **STOP and report the gap**. Do
  not invent endpoints/fields or work around the contract.
---

## No live Walmart environment

There is **no Walmart environment reachable from this machine** — no sandbox and no test
account. Nothing on the Walmart side can be called while you work, and you must not call
Walmart's production service. Build the integration against the contract you were given, and
verify it with whatever tests you judge it needs. It will be run against a Walmart-compatible
service later, through the base-URL override below.

---

## Credentials

- Account credentials arrive as env vars: `WALMART_CLIENT_ID`, `WALMART_CLIENT_SECRET`.
- **Bind settings from the `Walmart:` configuration section using exactly these keys**, and
  hard-code none of their values — the same build has to run against a different Walmart
  account than the one above: `Walmart:ClientId` (from `WALMART_CLIENT_ID`),
  `Walmart:ClientSecret` (from `WALMART_CLIENT_SECRET`), and `Walmart:BaseUrl`.
- `Walmart:BaseUrl` is an optional override, in the form `scheme://host[:port]`. When it is
  set, it replaces the scheme, host and port of **every** Walmart call this integration makes,
  including obtaining access. Everything after the host — including any base path the
  contract's server address carries — stays exactly as the contract gives it.
- The client secret is a secret: it is never logged, never returned by an endpoint, and never
  written into a source file.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so stock levels and orders only survive
  within a single run.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — an order placed through the Web storefront is invisible to
  PublicApi. Keep every flow verifiable end-to-end through PublicApi alone (that is why
  `POST /api/orders` is part of the surface).
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
- When done, **self-verify** that it builds, that the app starts, and that every endpoint
  above behaves as specified as far as can be shown without a live Walmart. Then give me a
  concise, step-by-step guide to verify the working integration myself.

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

