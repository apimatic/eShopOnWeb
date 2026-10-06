# Task — Take card payments in eShopOnWeb with Adyen

Make the eShopOnWeb reference app actually collect money, with **Adyen** as the payment
processor. eShopOnWeb today ends checkout by writing an `Order` row — no payment is ever taken,
and `Order` carries no payment state at all. This adds taking the money, giving it back, and
the record support staff need when a shopper asks what happened to a payment. It is an
**additive** capability — it does not replace the existing catalog/basket/order flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

### Flow 1 — Pay for an order

- `POST /api/orders` — place an order from catalog items; the request carries catalog item
  ids and quantities, and reuses the app's existing order/order-item model rather than a
  parallel one (the caller's identity comes from the token). The order starts in a state
  awaiting payment. Returns `orderId`.
- `POST /api/orders/{orderId}/pay` — pay the order total by card, taking the money now. The
  request carries the card the way Adyen's checkout front end hands it over: an encrypted card
  number, expiry month, expiry year and security code, plus the holder's name. The shop never
  sees a plain card number. The amount Adyen charges must equal the order total to the cent. A
  refused card leaves the order unpaid and tells the shopper why in terms they can act on.
- `POST /api/orders/{orderId}/refunds` — an operator gives money back on a paid order, in full
  or in part. An order must never be refunded beyond what was paid. Returns `refundId`.
- `GET /api/my-orders` — the caller's orders with their payment state.

Amounts come from catalog prices; the currency comes from configuration (below). Paying is
idempotent in effect: a double-click never charges the shopper twice.

### Flow 2 — The payment record for support

- Support must be able to see **everything Adyen returned** for each payment and each refund
  of an order, including fields Adyen may add later that this build does not know about yet.
  Keep it with the order.
- `GET /api/orders/{orderId}/adyen-record` — returns that record.

### Where it goes

Expose all capabilities as HTTP endpoints on the **`src/PublicApi`** project
(JWT-authenticated; the caller's identity comes from the token), following that project's
existing endpoint conventions, routed under `/api/` as named above. Every flow above has to
be drivable through that API alone. No storefront UI is required.

Refunds and the support record are **operator** actions: restrict them to the administrator
role this project already uses for its privileged endpoints. Every other endpoint is
shopper-scoped and acts only on the caller's own data: one shopper must never see or act on
another's orders.

---

## Adyen tooling — non-negotiable

- Use the **adyen** plugin (from the **context-plugins** marketplace) for
  **every** Adyen interaction. It is your sole reference for how to talk to Adyen.
- **Do not** web-search or rely on general/external knowledge for Adyen API details.
- If the plugin does not expose a capability you need, **STOP and report the gap** — do not
  invent or work around it.

---

## Test account & test card

The merchant is an Adyen **test** merchant account with card payments enabled. Nothing is
pre-seeded: payments and refunds are all created by your build.

In Adyen's test environment, each encrypted card field can be given as its plain value
prefixed with `test_`. Verify with Adyen's Visa test card: card number
`test_4111111145551142`, expiry month `test_03`, expiry year `test_2030`, security code
`test_737`, any holder name.

---

## Credentials

- Test credentials arrive as env vars: `ADYEN_API_KEY`, `ADYEN_MERCHANT_ACCOUNT`,
  `ADYEN_ENVIRONMENT`, `ADYEN_CURRENCY`.
- Target Adyen's **test** environment for all development and testing.
- **Bind settings from the `Adyen:` configuration section using exactly these keys**, and
  hard-code none of their values — the same build has to run against a different Adyen
  account than the one above: `Adyen:ApiKey` (from `ADYEN_API_KEY`), `Adyen:MerchantAccount`
  (from `ADYEN_MERCHANT_ACCOUNT`), `Adyen:Environment` (from `ADYEN_ENVIRONMENT`) and
  `Adyen:Currency` (from `ADYEN_CURRENCY`).

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so orders and payments only survive
  within a single run. Pay and refund the orders you created in that same run.
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
- Ship automated tests for the integration that run without network access, and make them pass.
- When done, **self-verify** that it builds and the flows actually work — a real payment on the
  test card, a real partial refund, and the support record of that order. No browser step is
  required. Then give me a concise, step-by-step guide to verify the working integration myself.

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

