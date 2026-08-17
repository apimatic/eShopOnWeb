# Order notifications by SMS (Twilio)

An **additive** capability on the `src/PublicApi` project: shoppers put a mobile number on file, get
text messages as their orders move, and operators can re-send, dispose of, and reconcile those
messages. It does not change the existing catalog/basket/order flow.

## Configuration

Bound from the `Twilio:` configuration section (loaded from user-secrets / environment — **no values
live in the repo**):

| Key | Meaning |
| --- | --- |
| `Twilio:AccountSid` | Account SID (HTTP Basic username). |
| `Twilio:AuthToken` | Auth token (HTTP Basic password). Secret — never logged/returned/committed. |
| `Twilio:FromNumber` | The configured sending number. Also the only sender reconciliation counts. |
| `Twilio:MessagingServiceSid` | Messaging Service used to schedule the delivery follow-up. |
| `Twilio:BaseUrl` | *Optional* override for the **messaging** API base address only. When set it is used verbatim for every send/fetch/list/redact/cancel call. It does **not** govern number lookup, which always uses the lookup host. |

Load them into user-secrets for the PublicApi project (values come from the environment):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
# Twilio:BaseUrl is optional and normally left unset (defaults to the provider's messaging host).
```

## Endpoints

All endpoints are JWT-authenticated on the PublicApi host; the caller's identity comes from the token.
Operator actions require the existing **Administrators** role. Every other endpoint is scoped to the
caller's own data — one shopper can never see, use, or delete another's numbers or orders.

### Flow 1 — the shopper's contact number (shopper-scoped)
- `POST /api/contact-numbers` — register a number. Validated and canonicalised with the provider;
  an unusable destination is rejected here (`400`). Stores the provider's canonical E.164 form.
  Returns `contactNumberId`.
- `GET /api/contact-numbers` — the caller's numbers.
- `DELETE /api/contact-numbers/{contactNumberId}` — remove one.

### Flow 2 — messages as the order moves
- `POST /api/orders` (shopper) — place an order from catalog item ids + quantities (reuses the app's
  Order/OrderItem model). Returns `orderId`. Sends a "placed" message.
- `POST /api/orders/{orderId}/dispatch` (operator) — sends "on its way" and **queues a delivery
  follow-up with the provider for ~3 days later** (scheduled at the provider, not held by a local timer).
- `POST /api/orders/{orderId}/cancel` (operator) — sends "cancelled" and **calls off any not-yet-sent
  follow-up** at the provider so it can never reach the shopper.
- `GET /api/my-orders` (shopper) — the caller's orders, each with its notifications' outcomes.
- `GET /api/orders/{orderId}/notifications` (shopper) — the messages for one order; each entry carries
  its own `notificationId` and its provider status, refreshed live from the provider on read.

A message that cannot be sent never fails the underlying order operation. A shopper with no number on
file is simply not messaged.

### Flow 3 — operator actions (Administrators only)
- `POST /api/notifications/{notificationId}/resend` — re-send a message. Body carries an
  `idempotencyKey`; a repeat under the same key sends nothing and returns the first result, a fresh
  key sends anew. Returns the produced `notificationId`.
- `DELETE /api/notifications/{notificationId}/content` — dispose of the message content. The text is
  redacted at the provider too (not merely hidden here); the fact of the message and its outcome survive.
- `GET /api/notifications/reconciliation?from={iso}&to={iso}` — the provider's record of messages sent
  from `Twilio:FromNumber` over the range, lined up against what eShop believes it sent (matched /
  provider-only / eShop-only). The provider is queried filtered by the sending number itself.

## Notes / design decisions

- Talks to the provider over its **documented HTTP REST API** (no SDK), so the `Twilio:BaseUrl`
  override and the sender/date filters map exactly to the documented endpoints.
- There is no public callback URL, so delivery outcomes are obtained by **polling** the provider on
  read (fetch by SID / list), per the provider's documented fallback.
- Destination numbers are masked in operator/notification responses and are never written to logs;
  the auth token is never logged, returned, or committed.
- With the in-memory database (`UseOnlyInMemoryDatabase=true`) records live only for a single run, so
  place → dispatch → cancel an order within the same run.
