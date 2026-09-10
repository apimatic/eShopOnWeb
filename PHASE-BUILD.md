# Task — Add PayPal payments and saved cards to eShopOnWeb

Make the eShopOnWeb reference app actually collect money, with **PayPal** as the payment
processor, and let a shopper **save a card** so a later order can be paid without re-entering
it. eShopOnWeb today is one-time commerce that ends checkout by writing an `Order` row — no
payment is ever taken, and `Order` carries no payment or fulfilment state at all. This adds
the money movement and the operator flows that follow a real payment: hold the money at
checkout, take it at fulfilment, give it back on a return. It is an **additive** capability —
it does not replace the existing catalog/basket/order flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

### Flow 1 — Pay for an order

A logged-in shopper places an order and pays for it by card; an operator then fulfils,
cancels or refunds it.

- `POST /api/orders` — place an order from catalog items; the request carries catalog item
  ids and quantities, and reuses the app's existing order/order-item model rather than a
  parallel one (the caller's identity comes from the token). The order starts in a state
  awaiting payment.
- `POST /api/orders/{orderId}/pay` — **authorize** the order total: put a hold on the money,
  do **not** take it yet. The request either carries card details for a one-off payment,
  **or** names one of the shopper's saved cards (Flow 2) to pay with instead. The amount
  PayPal holds must equal the order total to the cent.
- `POST /api/orders/{orderId}/fulfil` — an operator marks the order fulfilled, and *that* is
  when the money is actually taken. Afterwards the payment must show what PayPal reported:
  the captured amount, PayPal's fee, and the net proceeds to the merchant. An authorization
  that has gone stale before fulfilment has to be renewed rather than failing the fulfilment
  outright — and one that can no longer be renewed must say so in terms an operator can act
  on.
- `POST /api/orders/{orderId}/cancel` — cancel *before* fulfilment: the shopper's held funds
  are released, so no money ever moved.
- `POST /api/orders/{orderId}/refunds` — return *after* fulfilment: refund the captured
  payment, in full or in part. A partly-refunded order must never become refundable beyond
  what was captured.
- `GET /api/my-orders` — the caller's orders with their payment state.
- `GET /api/reconciliation?from={from}&to={to}` — a report listing PayPal's own record of
  transactions for a date range and lining them up against eShop orders, so a payment PayPal
  knows about and eShop doesn't — or the reverse — is visible. It covers the whole range, not
  just the first page of it. `from` and `to` are ISO-8601 date-times.

Amounts come from catalog prices; the currency comes from configuration (below). Payment
operations must be idempotent in effect: a double-click never authorizes or captures the
shopper twice. Refunds carry a caller-supplied idempotency key — repeating a request under the
same key must not refund twice, while two distinct partial refunds of the same capture remain
legitimate.

The payment has to carry enough of the state PayPal owns (ids and current status for the
hold, the capture, the refunds) that a later request can act on it, not only the one that
started it.

### Flow 2 — Saved cards

A logged-in shopper saves a card once and reuses it for later orders.

- `POST /api/payment-methods` — save a card for the signed-in shopper. The response
  identifies the saved card and describes it safely enough for the shopper to recognise which
  card it is — never full card details.
- `GET /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — remove a saved card. Afterwards it must
  no longer appear among the caller's saved cards, and must no longer be usable to pay.

A saved card belongs to the shopper who saved it: one shopper must never see, use, or delete
another's. The same goes for orders — one shopper must never see or act on another's. Full
card details are never stored in the application's own database and never written to logs.

### Where it goes

Expose all capabilities as HTTP endpoints on the **`src/PublicApi`** project
(JWT-authenticated; the caller's identity comes from the token), following that project's
existing endpoint conventions, routed under `/api/` as named above. Every flow above has to
be drivable through that API alone, and each action a caller can take stays separately
invocable — not one do-everything call that pays, fulfils and refunds behind a single route.
No storefront UI is required.

Fulfil, cancel and reconciliation are **operator** actions: restrict them to the administrator
role this project already uses for its privileged endpoints. Every other endpoint is
shopper-scoped and acts only on the caller's own data.

### Response identifiers

So the flows can be driven end to end by a caller, a response that creates something returns
its identifier as a top-level field of the response body: `orderId` from `POST /api/orders`,
`paymentMethodId` from `POST /api/payment-methods`, and `refundId` from
`POST /api/orders/{orderId}/refunds`. Everything else about the response shape is your call.

---

## PayPal tooling — non-negotiable

- PayPal's **OpenAPI specification** — located in the **`api-specs/`** folder, as one
  document per PayPal API — is the **authoritative contract** for **every** PayPal
  interaction. Endpoints, path/query params, request and response schemas, auth scheme,
  server/base-URL templating, and error models all come from the spec. How you consume it —
  codegen a client or hand-write against it — is your call, as long as the spec is the
  contract you build to. Working out which of the documents this task needs is part of the job.
- Do **not** install a pre-built PayPal SDK or client package from NuGet or anywhere else. A
  client you generate from `api-specs` or write by hand is fine; someone else's client is
  not the contract you were given.
- You **may** consult official PayPal documentation as a **secondary** reference to clarify
  semantics or fill in behavior the spec describes ambiguously. The **spec is authoritative**:
  where the spec and any doc/web source conflict, the spec wins. Do not build against
  endpoints, fields, or shapes that don't appear in the spec.
- If the spec genuinely does not cover a capability you need — and official docs don't
  resolve it — **STOP and report the gap**. Do not invent endpoints/fields or work around
  the contract.
---

## Sandbox entities & test fixtures

Nothing is pre-seeded on the PayPal side — orders, payments and saved cards are all created
dynamically. A sandbox **business** account is the merchant, and the app's REST credentials
(client id / secret) belong to it.

That account is enabled for direct card processing and for vaulting cards, so the whole task
is drivable without a browser. Verify the payment, saved-card, fulfilment, cancel and refund
flows with a **direct card payment** using PayPal's sandbox test card: Visa
`4111 1111 1111 1111`, any future expiry date, any CVC, any name and billing address. No card
number is ever kept by this app.

PayPal's transaction reporting lags live activity, so a reconciliation range covering payments
you have just created may legitimately come back empty. That is an expected sandbox result,
not a missing capability — build the report so it is correct over a range that does have data,
and do not report the empty range as a gap.

If PayPal answers a card payment with a challenge that requires a shopper to approve in a
browser, **STOP and report it** — do not build an approval round-trip instead.

---

## Credentials

- Sandbox credentials arrive as env vars: `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`,
  `PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY`.
- Target the PayPal **sandbox** for all development and testing.
- **Bind settings from the `PayPal:` configuration section using exactly these keys**, and
  hard-code none of their values — the same build has to run against a different PayPal
  account than the one above: `PayPal:ClientId` (from `PAYPAL_CLIENT_ID`),
  `PayPal:ClientSecret` (from `PAYPAL_CLIENT_SECRET`), `PayPal:Environment` (from
  `PAYPAL_ENVIRONMENT`), `PayPal:Currency` (from `PAYPAL_CURRENCY`), and `PayPal:BaseUrl`.
- `PayPal:BaseUrl` is an optional override: when it is set, use it verbatim as the API base
  address for **every** PayPal call — including the credential/token request — instead of
  deriving one from the environment.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so orders, payments and saved cards only
  survive within a single run. Pay, fulfil and refund the orders you created in that same run.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — an order placed through the Web storefront is invisible to
  PublicApi. Keep the payment flow verifiable end-to-end through PublicApi alone (that is why
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
- When done, **self-verify** that it builds and the flows actually work — a real authorization
  on the sandbox card, a real capture at fulfilment, a real refund, and a saved card reused to
  pay a second order. No browser step is required. Then give me a concise, step-by-step guide
  to verify the working integration myself.

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

