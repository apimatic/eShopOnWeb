# Task — Add PayPal payments and saved cards to eShopOnWeb

Add **PayPal** to the eShopOnWeb reference app as the payment processor for one-time orders,
and let a shopper **save a card** so a later order can be paid without re-entering it.
eShopOnWeb today is one-time commerce with no payment processing (Catalog → Basket → Order);
this is an **additive** capability — it does not replace the existing cart/checkout flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

**Flow 1 — Pay for an order.**
A logged-in shopper places an order and pays for it with PayPal; a paid order can later be
refunded in full.
Endpoints:

- `POST /api/orders` — place an order from catalog items; the request carries catalog item
  ids and quantities, and reuses the app's existing order/order-item model rather than a
  parallel one (the caller's identity comes from the token). The order starts in a state
  awaiting payment.
- `POST /api/orders/{orderId}/pay` — pay for that order with PayPal. The request either
  carries card details for a one-off payment, **or** names one of the shopper's saved cards
  (Flow 2) to pay with instead.
- `POST /api/orders/{orderId}/refunds` — **full** refund of that order's payment (partial
  refunds are out of scope); on success the order reflects **refunded**.
- `GET /api/my-orders` — the caller's orders with their payment state.

Amounts come from catalog prices, currency USD. Payment operations must be idempotent in
effect: a double-click never produces a double charge or a double refund.

**Flow 2 — Saved cards.**
A logged-in shopper saves a card once and reuses it for later orders.
Endpoints:

- `POST /api/payment-methods` — save a card for the signed-in shopper. The response
  identifies the saved card and describes it safely enough for the shopper to recognise
  which card it is — never full card details.
- `GET /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — remove a saved card. Afterwards it must
  no longer appear among the caller's saved cards, and must no longer be usable to pay.

A saved card belongs to the shopper who saved it: one shopper must never see, use, or delete
another's. Full card details are never stored in the application's own database and never
written to logs.

**Where it goes.** Expose all capabilities as HTTP endpoints on the **`src/PublicApi`**
project (JWT-authenticated; the caller's identity comes from the token), following that
project's existing endpoint conventions, routed under `/api/` as named above.

**Response identifiers.** So the flows can be driven end to end by a caller, a response that
creates something returns its identifier as a top-level field of the response body:
`orderId` from `POST /api/orders`, and `paymentMethodId` from `POST /api/payment-methods`.
Everything else about the response shape is your call.

---

## API tooling — non-negotiable

- You are given **no** plugin and **no** bundled specification for the API. Sourcing the
  contract is part of the task: **research and confirm** the correct, current way to perform
  every PayPal capability this integration requires **before** writing code against it.
  Official documentation and web search are available to you; how you consume them — and
  whether you use an SDK/package or plain HTTP — is your call.
- Confirm before you commit: do not build against endpoints, fields, or shapes you have not
  verified against a real source.
- If you cannot confirm how a required capability works, or find that PayPal does not support
  something the integration needs, **STOP and report the gap** — do not invent or work around
  it.

---

## Sandbox entities & test fixtures

### PayPal (sandbox)

Nothing is pre-seeded — payments and saved cards are created dynamically. Verify the payment,
saved-card and refund flows with a **direct card payment** using PayPal's sandbox test card:
Visa `4111 1111 1111 1111`, any future expiry date, any CVC, any name and billing address.

---

## Credentials

All sandbox credentials arrive as env vars. Target PayPal's **sandbox** environment for all
development and verification. **Bind settings from configuration using exactly the keys
below**, and hard-code none of their values — the same build has to run against a different
PayPal app than the one behind these vars.

- **PayPal** — env: `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`
  (`sandbox`). Config section `PayPal:` — `PayPal:ClientId`, `PayPal:ClientSecret`,
  `PayPal:Environment` (always `sandbox` for this task — no other environment needs to be
  supported), `PayPal:BaseUrl` (optional override: when set, use it verbatim as the API base
  address instead of deriving one from the environment).

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so persisted state (orders and their
  payment state) only survives within a single run.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — an order placed through the Web storefront is invisible to
  PublicApi. Keep the payment flow verifiable end-to-end through PublicApi alone (that is
  why `POST /api/orders` is part of the surface).
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
- When done, **self-verify** that it builds and both flows actually work — then give me a
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

