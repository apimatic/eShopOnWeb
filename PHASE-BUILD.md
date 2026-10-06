# Task — Keep the merchant's Square account in step with eShopOnWeb

The eShopOnWeb shop also sells in person with **Square**. The owner wants Square kept in step
with the online shop: the same products, with their photos, in the Square catalog, and every
online order visible to Square staff together with the customer's gift message. The shop
connects to the merchant's Square account the way any Square app does: the merchant signs in to
Square once and approves the shop's access. It is an **additive** capability — it does not
replace the existing catalog, basket or order flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

### Flow 1 — Connect the merchant's Square account

- `GET /api/square/connect` — start connecting: returns `signInUrl`, the Square page the
  merchant opens in a browser to sign in and approve the shop's access.
- `GET /api/square/callback` — where Square sends the merchant's browser back after they
  approve. From then on the app acts for that merchant without asking them to sign in again,
  also after the access Square granted runs out. A callback the shop did not start must be
  refused, and must not change the connection.
- `GET /api/square/connection` — whether the shop is connected, and as which Square merchant
  (its id and business name).
- **This sandbox's merchant has already approved the shop:** its access token arrives in
  `SQUARE_ACCESS_TOKEN` (below). While no merchant has connected through sign-in, the app acts
  for the merchant with that token. Nobody will sign in during your session, so verify the
  sign-in flow with automated tests.

### Flow 2 — Products and photos

- `POST /api/square/catalog/sync` — bring Square's catalog in line with eShop's: every eShop
  catalog item exists in Square as an item with the same name and price. Running it again
  creates no duplicates, and an eShop item whose name or price changed is updated in Square.
  The response says how many items were created, updated and left unchanged.
- `PUT /api/catalog-items/{catalogItemId}/photo` — upload a product photo: a JPEG or PNG image
  in a `multipart/form-data` field named `photo`, at most 5 MB. It becomes the photo of that
  product's Square item, and the response returns Square's id for the image (`imageId`) and the
  address Square serves it from (`imageUrl`). Anything that is not a JPEG or PNG image is
  rejected without calling Square.

### Flow 3 — Orders with a gift message

- `POST /api/orders` — place an order from catalog items; the request carries catalog item ids
  and quantities and an optional `giftMessage` (at most 200 characters), and reuses the app's
  existing order/order-item model rather than a parallel one (the caller's identity comes from
  the token). The order is also created in Square, at the merchant's location, with the same
  items and prices, so Square staff see it. Returns `orderId`.
- The gift message lives **in Square, on the Square order**, as a field labelled
  "Gift message" that Square staff can see and edit — not in eShop's database.
- `GET /api/my-orders/{orderId}` — the caller's order, with its Square order id and the gift
  message as Square holds it now, so an edit made by staff in Square shows up here.

### Where it goes

Expose all capabilities as HTTP endpoints on the **`src/PublicApi`** project
(JWT-authenticated; the caller's identity comes from the token), following that project's
existing endpoint conventions, routed under `/api/` as named above. Every flow above has to
be drivable through that API alone. No storefront UI is required.

Connecting Square, syncing the catalog and uploading photos are **operator** actions: restrict
them to the administrator role this project already uses for its privileged endpoints — except
`GET /api/square/callback`, which the merchant's browser reaches without a token. Orders are
shopper-scoped: one shopper must never see or act on another's.

---

## Square tooling — non-negotiable

- Use the **square** plugin (from the **context-plugins** marketplace) for
  **every** Square interaction. It is your sole reference for how to talk to Square.
- **Do not** web-search or rely on general/external knowledge for Square API details.
- If the plugin does not expose a capability you need, **STOP and report the gap** — do not
  invent or work around it.

---

## Sandbox

The merchant is a Square **sandbox** test seller with one active location. Its catalog may
already hold items eShop did not create; leave those alone. Nothing else is pre-seeded:
items, photos, orders and the gift-message field are all created by your build.

---

## Credentials

- Sandbox credentials arrive as env vars: `SQUARE_ENVIRONMENT`, `SQUARE_APPLICATION_ID`,
  `SQUARE_APPLICATION_SECRET`, `SQUARE_REDIRECT_URI` (the callback address registered with
  Square) and `SQUARE_ACCESS_TOKEN`.
- Target the Square **sandbox** for all development and testing.
- **Bind settings from the `Square:` configuration section using exactly these keys**, and
  hard-code none of their values — the same build has to run against a different Square
  account than the one above: `Square:Environment` (from `SQUARE_ENVIRONMENT`),
  `Square:ApplicationId` (from `SQUARE_APPLICATION_ID`), `Square:ApplicationSecret` (from
  `SQUARE_APPLICATION_SECRET`), `Square:RedirectUri` (from `SQUARE_REDIRECT_URI`) and
  `Square:AccessToken` (from `SQUARE_ACCESS_TOKEN`).
- `Square:AccessToken` is optional: a deployment where the merchant signs in leaves it unset.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so orders only survive within a single
  run.
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
- Ship automated tests for the integration that run without network access, and make them pass.
- When done, **self-verify** that it builds and the flows actually work against the sandbox — a
  real catalog sync, a real photo uploaded to a Square item, and a real order whose gift message
  reads back from Square. No browser step is possible. Then give me a concise, step-by-step
  guide to verify the working integration myself.

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

