# Order notifications by SMS (Twilio)

Additive capability on **`src/PublicApi`** that keeps shoppers informed by text message as their
orders progress, using **Twilio** as the messaging provider. It adds the shopper's mobile contact
details, the messages that go out as an order moves, and the operator's view of what actually
reached the customer. The existing catalog/basket/order flow is untouched.

Twilio is consumed **only** through the OpenAPI specs in `api-specs/` (no Twilio SDK):

* **Messages** — `twilio_api_v2010.yaml` (`https://api.twilio.com`): send, schedule, cancel, fetch,
  redact and list messages. Overridable via `Twilio:BaseUrl`.
* **Lookups v2** — `twilio_lookups_v2.yaml` (`https://lookups.twilio.com`): validate a number and
  return its canonical E.164 form. Served from its own host — `Twilio:BaseUrl` does **not** govern it.

## Endpoints

All routes are JWT-authenticated on PublicApi; the caller's identity comes from the token.

### Flow 1 — the shopper's contact number (shopper-scoped)
| Method & route | Purpose |
|---|---|
| `POST /api/contact-numbers` | Register a mobile number. Rejected up-front (HTTP 400) if the provider does not consider it a usable destination; stores the provider's canonical E.164 form. Returns `contactNumberId`. |
| `GET /api/contact-numbers` | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | Remove one. Afterwards it is gone from the list and nothing is ever sent to it again. |

### Flow 2 — messages as the order moves
| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (reuses the existing order model). Returns `orderId`. Sends "order placed". |
| `POST /api/orders/{orderId}/dispatch` | operator | Mark dispatched. Sends "on its way" and **queues a delivery follow-up with the provider** for a few days later. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel. Sends "cancelled" and **calls off any follow-up that has not yet gone out**. |
| `GET /api/my-orders` | shopper | The caller's orders, each showing where its notifications got to. |
| `GET /api/orders/{orderId}/notifications` | shopper | What was sent for this order and what became of each message. Each entry carries its `notificationId`. |

### Flow 3 — what the operator can do (operator-scoped, administrator role)
| Method & route | Purpose |
|---|---|
| `POST /api/notifications/{notificationId}/resend` | Re-send a message that did not reach the shopper. Carries a caller-supplied idempotency key (`Idempotency-Key` header, or `idempotencyKey` query). A repeat under the same key returns the same notification without sending again; a fresh key is a legitimate new attempt. Returns the new `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | Dispose of a message's content: redacts the body **at the provider** (not merely hidden locally). The fact of the message and its delivery outcome survive. |
| `GET /api/notifications/reconciliation?from={ISO}&to={ISO}` | Line up the provider's own record of messages **sent from the configured `Twilio:FromNumber`** against what eShop believes it sent, over the whole range. |

Guarantees: a message that cannot be sent never fails the underlying order operation; a shopper
with no number on file is simply not messaged; a shopper's number and the Twilio auth token are
never written to logs; one shopper can never see, use or delete another's numbers or orders.

## Configuration

Bind from the `Twilio:` section using these exact keys (values come from **.NET user-secrets** /
environment — never committed):

| Key | Source env var | Notes |
|---|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` | |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` | Secret. Never logged/returned. |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` | The app's sending number; reconciliation is scoped to it. |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` | Used for scheduled messages. |
| `Twilio:BaseUrl` | — | Optional verbatim override for the **messaging** API base address. |
| `Notifications:DeliveryFollowUpDelay` | — | Follow-up delay (TimeSpan), default `3.00:00:00`. |

Load the secrets from the environment into user-secrets (values are not echoed):

```bash
P=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"          --project "$P" >/dev/null
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"           --project "$P" >/dev/null
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"          --project "$P" >/dev/null
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" --project "$P" >/dev/null
```

## Run it (this machine)

`global.json` is set to `rollForward: latestMajor`; run with `DOTNET_ROLL_FORWARD=Major`. Use the
in-memory database (no LocalDB here). Bind to a port in your assigned block. Running over plain HTTP
avoids the dev-cert dance for curl (with no HTTPS port configured, `UseHttpsRedirection` is a no-op).

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development         # loads user-secrets
export ASPNETCORE_URLS=http://localhost:9350      # a port in APP_PORT_BLOCK_BASE..+SIZE-1
export UseOnlyInMemoryDatabase=true
# optional, to see schedule-then-cancel quickly: export Notifications__DeliveryFollowUpDelay=2.00:00:00
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory caveat: data lives only for a single run and PublicApi holds its own store, so place,
> dispatch and cancel the orders you created **in the same run**. Only ever register/message
> `TWILIO_TEST_TO_NUMBER` (Canadian, reachable) and `TWILIO_UNREACHABLE_TO_NUMBER` (US, undeliverable).

## Verify it yourself (step by step)

```bash
B=http://localhost:9350
tok() { curl -s -X POST "$B/api/authenticate" -H "Content-Type: application/json" \
        -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
SHOPPER=$(tok demouser@microsoft.com)     # a regular shopper
ADMIN=$(tok admin@microsoft.com)          # the operator (administrator role)

# 1) Register the reachable number -> 201 + contactNumberId, stored in canonical E.164.
curl -s -X POST "$B/api/contact-numbers" -H "Authorization: Bearer $SHOPPER" \
     -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
#    An unusable number is rejected here (HTTP 400):
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$B/api/contact-numbers" -H "Authorization: Bearer $SHOPPER" \
     -H "Content-Type: application/json" -d '{"phoneNumber":"not-a-number"}'

# 2) Place an order -> 201 + orderId. A real "order placed" text is sent to the number above.
curl -s -X POST "$B/api/orders" -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":5,"quantity":2}]}'
ORDER=<orderId from the response>

# 3) Dispatch (operator) -> "on its way" text + a follow-up scheduled with Twilio for later.
curl -s -X POST "$B/api/orders/$ORDER/dispatch" -H "Authorization: Bearer $ADMIN"
curl -s "$B/api/orders/$ORDER/notifications" -H "Authorization: Bearer $SHOPPER"   # see DeliveryFollowUp = Scheduled + a providerMessageSid

# 4) Cancel (operator) -> "cancelled" text + the scheduled follow-up is called off (never sent).
curl -s -X POST "$B/api/orders/$ORDER/cancel" -H "Authorization: Bearer $ADMIN"
curl -s "$B/api/orders/$ORDER/notifications" -H "Authorization: Bearer $SHOPPER"   # DeliveryFollowUp is now Canceled

# 5) Undeliverable outcome: register the US number and place another order; its placed
#    notification settles to Undelivered (accepted by the API, refused by the carrier).
curl -s -X POST "$B/api/contact-numbers" -H "Authorization: Bearer $SHOPPER" \
     -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -s -X POST "$B/api/orders" -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":4,"quantity":1}]}'

# 6) Operator: re-send a message that did not reach the shopper (idempotent).
curl -s -X POST "$B/api/notifications/<notificationId>/resend" -H "Authorization: Bearer $ADMIN" \
     -H "Idempotency-Key: my-key-1"        # first use -> new notificationId
curl -s -X POST "$B/api/notifications/<notificationId>/resend" -H "Authorization: Bearer $ADMIN" \
     -H "Idempotency-Key: my-key-1"        # same key -> replayed:true, same id, no second send

# 7) Operator: dispose of a message's content at the provider.
curl -s -X DELETE "$B/api/notifications/<notificationId>/content" -H "Authorization: Bearer $ADMIN"

# 8) Operator: reconciliation over a range that has data.
FROM=$(python -c "import datetime;print((datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(hours=3)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
TO=$(python -c "import datetime;print((datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=3)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -s "$B/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

An automated end-to-end driver of all of the above (26 assertions) plus an authorization/redaction
suite (11 assertions) live at `tmp/.../scratchpad` during development; both pass against the live
account. Operator routes reject a shopper with 403; a non-owner reading another's order notifications
gets 404; the app log contains no phone numbers, no auth token and no Basic-auth header.
