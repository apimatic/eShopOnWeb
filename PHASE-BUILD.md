# Task — Add subscription billing, PayPal checkout & refunds, and SMS notifications to eShopOnWeb

Add three integrations to the eShopOnWeb reference app: **Maxio Advanced Billing** as the
recurring-subscription system of record, **PayPal** as the payment processor for one-time
orders (pay and refund), and **Twilio** for transactional SMS
notifications. eShopOnWeb today is one-time commerce with no payment processing
(Catalog → Basket → Order); all three are **additive, parallel** capabilities — they do
not replace the existing cart/checkout flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

**Flow 1 — Subscribe (Maxio).**
A logged-in shopper browses available plans, subscribes to one, and sees it reflected in
their account. Ensure a Maxio customer exists for the eShopOnWeb user (idempotent, so a
double-click never creates two customers/subscriptions), enroll them, and confirm
plan/price/state/next-billing-date back to the user.
Endpoints: `GET /api/subscription-plans`, `POST /api/subscriptions`,
`GET /api/my-subscriptions`.

**Flow 2 — Pay for an order (PayPal).**
A logged-in shopper places an order and pays for it with PayPal; a paid order can later be
refunded in full.
Endpoints:

- `POST /api/orders` — place an order from catalog items; the request carries catalog item
  ids and quantities, and reuses the app's existing order/order-item model rather than a
  parallel one (the caller's identity comes from the token). The order starts in a state
  awaiting payment.
- `POST /api/orders/{orderId}/pay` — pay for that order with PayPal.
- `POST /api/orders/{orderId}/refunds` — **full** refund of that order's payment (partial
  refunds are out of scope); on success the order reflects **refunded**.
- `GET /api/my-orders` — the caller's orders with their payment state.

Amounts come from catalog prices, currency USD. Payment operations must be idempotent in
effect: a double-click never produces a double charge or a double refund.

**Flow 3 — SMS notifications (Twilio).**
Send the shopper an SMS on three events: **payment completed**, **refund issued**, and
**subscription created**. The shopper's mobile number lives on their ASP.NET Identity user
profile (`PhoneNumber`) and notifications go to that number; seed the demo user's profile
with the number in `TWILIO_TEST_TO_NUMBER` so the flows are verifiable. When a user has no
number on file, skip the SMS. A notification failure must **never** fail the underlying
operation — the payment/refund/subscription outcome stands.

**Where it goes.** Expose all capabilities as HTTP endpoints on the **`src/PublicApi`**
project (JWT-authenticated; the caller's identity comes from the token), following that
project's existing endpoint conventions, routed under `/api/` as named above.

---

## API tooling — non-negotiable

- Use the **maxio-sdk-merged** plugin for **every** Maxio interaction, the
  **paypal-sdk** plugin for **every** PayPal interaction, and the
  **twilio-sdk** plugin for **every** Twilio interaction — all from the
  **apimatic** marketplace. Each plugin is your sole reference for how to talk
  to its API.
- **Do not** web-search or rely on general/external knowledge for Maxio, PayPal, or Twilio
  API details.
- If a plugin does not expose a capability you need, **STOP and report the gap** — do not
  invent or work around it.

---

## Sandbox entities & test fixtures

### Maxio (already seeded on site `cp-exp-1`)

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

### PayPal (sandbox)

Nothing is pre-seeded — payments are created dynamically per checkout. Verify the payment and
refund flows with a **direct card payment** using PayPal's sandbox test card: Visa
`4111 1111 1111 1111`, any future expiry date, any CVC, any name and billing address.

### Twilio (test-credentials sandbox)

The Twilio account runs in **test-credentials mode**: the live API authenticates,
validates, and accepts requests exactly as production would, but no SMS is ever delivered
and nothing is charged. Self-verification for the SMS leg therefore means **API
acceptance** — assert the send is accepted (HTTP 201 with a Message SID) — not delivery.
Do not poll for delivery status; message resources are not retrievable in this mode. Send
verification messages **only** to the number in `TWILIO_TEST_TO_NUMBER`, and keep sends
minimal — a handful of messages, not a loop.

---

## Credentials

All sandbox credentials arrive as env vars. Target each provider's **sandbox/test**
environment for all development and verification. **Bind settings from configuration using
exactly the keys below**, and hard-code none of their values — the same build has to run
against a different Maxio site, a different PayPal app, and a different Twilio account
than the ones behind these vars.

- **Maxio** — env: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`,
  `MAXIO_DEFAULT_PRODUCT_FAMILY`. Config section `Maxio:` — `Maxio:ApiKey`,
  `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional override: when
  set, use it verbatim as the API base address instead of deriving one from the subdomain).
- **PayPal** — env: `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`
  (`sandbox`). Config section `PayPal:` — `PayPal:ClientId`, `PayPal:ClientSecret`,
  `PayPal:Environment`, `PayPal:BaseUrl` (optional override, same verbatim rule).
- **Twilio** — env: `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_FROM_NUMBER`,
  `TWILIO_TEST_TO_NUMBER`. Config section `Twilio:` — `Twilio:AccountSid`,
  `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:BaseUrl` (optional override, same
  verbatim rule).

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so persisted state (orders, payment
  records, userId ↔ subscription mappings) only survives within a single run.
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
- When done, **self-verify** that it builds and all three flows actually work — then give me
  a concise, step-by-step guide to verify the working integration myself.

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

