# Task — Sell digital downloads from the shop's Box account

The eShopOnWeb shop starts selling **digital editions** of some catalog items: a PDF guide, a printable
image. The files already live in the merchant's **Box** account. Make eShopOnWeb hand the right file to the
shopper who bought it. It is an **additive** capability — it does not replace the existing catalog, basket or
order flow.

You own the design and every implementation decision — architecture, file layout, build order, patterns.
Just honor the mandates and the details below.

---

## What to build

### Flow 1 — Link catalog items to files (operator)

- `GET /api/digital-files` — the files in the merchant's Box folder named `eshop-digital-products`: each
  file's Box id, name and size, so an operator can pick one.
- `PUT /api/catalog-items/{catalogItemId}/digital-file` — link a catalog item to one of those files by its
  Box id. A file that does not exist in Box is refused. Returns the file's name and size.

### Flow 2 — Download what you bought (shopper)

- `POST /api/orders` — place an order from catalog items; the request carries catalog item ids and
  quantities, and reuses the app's existing order/order-item model rather than a parallel one (the caller's
  identity comes from the token). Returns `orderId`.
- `GET /api/orders/{orderId}/downloads/{catalogItemId}` — the shopper who placed that order downloads the
  item's file. The response **is the file**, with its original file name and type, so a browser saves it
  properly. Files can be large: pass them through to the shopper rather than holding a whole file in the
  shop's memory. One shopper must never download another shopper's purchase, and an item that has no linked
  file, or was not in the order, is refused.
- If Box stops sending data for 30 seconds during a download, the download is abandoned and the failure is
  logged. A shopper must never receive a partial file presented as a complete one.

### Where it goes

Expose all capabilities as HTTP endpoints on the **`src/PublicApi`** project (JWT-authenticated; the
caller's identity comes from the token), following that project's existing endpoint conventions, routed
under `/api/` as named above. Every flow above has to be drivable through that API alone. No storefront UI is
required. Listing and linking files are **operator** actions: restrict them to the administrator role this
project already uses for its privileged endpoints. Orders and downloads are shopper-scoped.

---

## Box tooling — non-negotiable

- Use the **box** plugin (from the **context-plugins** marketplace) for **every** Box
  interaction. It is your sole reference for how to talk to Box.
- **Do not** web-search or rely on general/external knowledge for Box API details.
- If the plugin does not expose a capability you need, **STOP and report the gap** — do not invent or work
  around it.

---

## The merchant's Box account

The folder `eshop-digital-products` holds two test files: a PDF and a PNG image. Use only that folder and
never change or delete anything in the account.

---

## Credentials

- The merchant's Box access token arrives as the env var `BOX_ACCESS_TOKEN`. It is a short-lived developer
  token: it stops working about 60 minutes after it was issued, so do your live checks early as well as at
  the end.
- **Bind it from configuration as `Box:AccessToken`** (from `BOX_ACCESS_TOKEN`) and hard-code nothing — the
  same build has to run against a different Box account.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so orders and file links only survive
  within a single run. Download through the orders you created in that same run.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — an order placed through the Web storefront is invisible to
  PublicApi. Keep the download flow verifiable end-to-end through PublicApi alone (that is why
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
- When done, **self-verify** that it builds and the flows actually work against the real Box account — list
  the folder, link an item to the real PDF, place an order and download the file, and confirm the bytes match
  the file in Box. Then give me a concise, step-by-step guide to verify the working integration myself.

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

