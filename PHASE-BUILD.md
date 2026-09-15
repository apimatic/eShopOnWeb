# Task — Add order notifications by SMS to eShopOnWeb

Make the eShopOnWeb reference app keep its shoppers informed by text message as their orders
progress, with **Twilio** as the messaging provider. eShopOnWeb today ends checkout by writing
an `Order` row and tells the shopper nothing afterwards — there is no contact detail on file,
no notion of an order having been dispatched or cancelled, and no record of anything ever
having been sent. This adds the shopper's mobile contact details, the messages that go out as
an order moves, and the operator's view of what actually reached the customer. It is an
**additive** capability — it does not replace the existing catalog/basket/order flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

### Flow 1 — The shopper's contact number

A logged-in shopper puts a mobile number on file so the shop can reach them.

- `POST /api/contact-numbers` — register a mobile number for the signed-in shopper. A number
  the provider does not consider a usable destination must be rejected at this point rather
  than at the moment a message fails to go out, and what gets stored is the provider's own
  canonical form of the number, not whatever the caller typed.
- `GET /api/contact-numbers` — the caller's registered numbers.
- `DELETE /api/contact-numbers/{contactNumberId}` — remove one. Afterwards it must no longer
  appear among the caller's numbers, and nothing may be sent to it again.

A registered number belongs to the shopper who registered it: one shopper must never see,
use, or delete another's. The same goes for orders. A shopper's number is never written to
logs.

### Flow 2 — Messages as the order moves

- `POST /api/orders` — place an order from catalog items; the request carries catalog item
  ids and quantities, and reuses the app's existing order/order-item model rather than a
  parallel one (the caller's identity comes from the token). The shopper is told their order
  was placed.
- `POST /api/orders/{orderId}/dispatch` — an operator marks the order dispatched. The shopper
  is told it is on its way, and a **follow-up message asking how the delivery went is queued
  with the provider for a few days later** — not held in this application to be sent by some
  timer of its own.
- `POST /api/orders/{orderId}/cancel` — an operator cancels the order. The shopper is told,
  and a follow-up that has not yet gone out must never reach them: asking a customer how their
  delivery went for an order that was cancelled is exactly the incident this must prevent.
- `GET /api/my-orders` — the caller's orders, each showing where its notifications got to.
- `GET /api/orders/{orderId}/notifications` — what was sent for this order, and what became of
  each message.

A message that cannot be sent must **never** fail the underlying operation — the order is
still placed, dispatched or cancelled, and the caller's request still succeeds. A shopper with
no number on file is simply not messaged.

The notification has to carry enough of the state the provider owns (its identifier and
current delivery outcome for each message) that a later request can act on it and report on
it, not only the one that sent it.

### Flow 3 — What the operator can do about it

- `POST /api/notifications/{notificationId}/resend` — an operator re-sends a message that did
  not reach the shopper. The request carries a caller-supplied idempotency key: repeating a
  request under the same key must not send a second message, while a genuine second attempt
  under a fresh key remains legitimate.
- `DELETE /api/notifications/{notificationId}/content` — a shopper has asked for the content
  of a message about them to be disposed of. Afterwards the text of that message must no
  longer be retrievable from the provider either — not merely hidden by this application —
  while the fact that a message was sent, and what became of it, survives.
- `GET /api/notifications/reconciliation?from={from}&to={to}` — a report listing the
  provider's own record of messages for a date range and lining them up against what eShop
  believes it sent, so a message the provider knows about and eShop doesn't — or the reverse —
  is visible. It covers the whole range. `from` and `to` are ISO-8601 date-times.
  **The provider account carries traffic that is not this application's**, so the report must
  count only messages sent from this application's own configured sending number
  (`Twilio:FromNumber`) — ask the provider for that number's messages, rather than filtering a
  wider answer after the fact.

### Where it goes

Expose all capabilities as HTTP endpoints on the **`src/PublicApi`** project
(JWT-authenticated; the caller's identity comes from the token), following that project's
existing endpoint conventions, routed under `/api/` as named above. Every flow above has to
be drivable through that API alone, and each action a caller can take stays separately
invocable — not one do-everything call that places, dispatches and re-notifies behind a
single route. No storefront UI is required.

Dispatch, cancel, resend, content disposal and reconciliation are **operator** actions:
restrict them to the administrator role this project already uses for its privileged
endpoints. Every other endpoint is shopper-scoped and acts only on the caller's own data.

### Response identifiers

So the flows can be driven end to end by a caller, a response that creates something returns
its identifier as a top-level field of the response body: `orderId` from `POST /api/orders`,
`contactNumberId` from `POST /api/contact-numbers`, and `notificationId` from
`POST /api/notifications/{notificationId}/resend` — the identifier of the message that resend
produced. Each entry `GET /api/orders/{orderId}/notifications` returns carries its own
`notificationId` too, since that is what the operator endpoints act on. Everything else about
the response shape is your call.

---

## Twilio tooling — non-negotiable

- Use the **twilio-sdk** plugin — already loaded into this session — for **every** Twilio
  interaction. It is your sole reference for how to talk to Twilio.
- **Do not** web-search or rely on general/external knowledge for Twilio API details.
- If the plugin does not expose a capability you need, **STOP and report the gap** — do not
  invent or work around it.
---

## Sandbox entities & test fixtures

Nothing is pre-seeded on the Twilio side — every message is created dynamically. The account
is a live one: the messages this app sends are really sent, and really cost money. Keep the
volume to what verifying the work needs.

- The account's own sending number arrives in configuration (below), and so do the two
  destinations to verify against. `TWILIO_TEST_TO_NUMBER` is a **Canadian** number this account
  can reach and which is safe to text — the United States restriction below does not apply to
  it, so a message to it really is expected to arrive. `TWILIO_UNREACHABLE_TO_NUMBER` is a
  reserved, unassigned United States number that no handset will ever receive.
  **Register and message those two only.**
  Never send to any other real number — no colleague's handset, no number from a document,
  no test fixture you invent.
- **Some destinations are legitimately undeliverable for this account**, which is what the
  second number is for. The API accepts the message and the carrier then refuses it — for this
  account that is the case for United States destinations generally. That is an expected
  result of a live account's registration status, not a missing capability and not a defect in
  your integration: handle it as an outcome and do not report it as a gap.
- **There is no publicly reachable URL for this application.** The provider cannot call back
  into it, so anything you need to know about what happened to a message has to be obtained by
  asking the provider, not by receiving a notification from it.

---

## Credentials

- Live account credentials arrive as env vars: `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`,
  `TWILIO_FROM_NUMBER`, `TWILIO_MESSAGING_SERVICE_SID`, `TWILIO_TEST_TO_NUMBER`,
  `TWILIO_UNREACHABLE_TO_NUMBER`.
- **Bind settings from the `Twilio:` configuration section using exactly these keys**, and
  hard-code none of their values — the same build has to run against a different Twilio
  account than the one above: `Twilio:AccountSid` (from `TWILIO_ACCOUNT_SID`),
  `Twilio:AuthToken` (from `TWILIO_AUTH_TOKEN`), `Twilio:FromNumber` (from
  `TWILIO_FROM_NUMBER`), `Twilio:MessagingServiceSid` (from `TWILIO_MESSAGING_SERVICE_SID`),
  and `Twilio:BaseUrl`.
- `Twilio:BaseUrl` is an optional override for the **messaging** API — the one this
  integration sends, reads and reconciles messages through. Twilio serves other capabilities
  from other hosts, and this setting does not govern those. When it is set, use it verbatim as
  the base address for **every** messaging-API call instead of the provider's default.
- The auth token is a secret: it is never logged, never returned by an endpoint, and never
  written into a source file.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so orders, contact numbers and
  notification records only survive within a single run. Dispatch and cancel the orders you
  created in that same run.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — an order placed through the Web storefront is invisible to
  PublicApi. Keep the notification flow verifiable end-to-end through PublicApi alone (that is
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
- When done, **self-verify** that it builds and the flows actually work — a real message that
  genuinely reaches the destination number, a follow-up really queued with the provider and
  then called off before it goes out, an operator re-send, and a reconciliation report over a
  range that has data in it. Then give me a concise, step-by-step guide to verify the working
  integration myself.

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

