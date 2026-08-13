# Order notifications by SMS (Twilio)

An additive capability on top of the existing eShopOnWeb catalog/basket/order flow: shoppers put a
mobile number on file, receive SMS as their order is placed / dispatched / cancelled, and operators
can resend, dispose of message content, and reconcile against the provider. All of it is exposed as
JWT-authenticated HTTP endpoints on **`src/PublicApi`**. Twilio is reached exclusively through the
**twilio-sdk** plugin (`AsadAli.TwilioSdk`).

## Where it lives

| Layer | What |
|---|---|
| `ApplicationCore/Entities/ContactAggregate/ContactNumber` | shopper's on-file number (stores provider-canonical E.164) |
| `ApplicationCore/Entities/NotificationAggregate/OrderNotification` | one message: provider SID + current delivery outcome + content |
| `ApplicationCore/Entities/OrderAggregate/Order` | gained `Status` (`Placed`/`Dispatched`/`Cancelled`) + `Dispatch()`/`Cancel()` |
| `ApplicationCore/Interfaces/INotificationGateway` | the messaging port (no Twilio types leak into the core) |
| `ApplicationCore/Services/{ContactNumberService,OrderMessagingService}` | orchestration |
| `Infrastructure/Notifications/TwilioNotificationGateway` | the only place that talks to Twilio |
| `PublicApi/{ContactNumberEndpoints,OrderNotificationEndpoints}` | the HTTP surface |

## Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

| Method | Route | Notes |
|---|---|---|
| POST | `/api/contact-numbers` | validates via provider, stores canonical; returns `contactNumberId` |
| GET | `/api/contact-numbers` | caller's numbers |
| DELETE | `/api/contact-numbers/{contactNumberId}` | also calls off not-yet-sent messages to that number |
| POST | `/api/orders` | places order from catalog item ids+quantities; returns `orderId` |
| GET | `/api/my-orders` | caller's orders, each with its notifications' outcomes |
| GET | `/api/orders/{orderId}/notifications` | this order's notifications (each carries `notificationId`) |

Operator actions (restricted to the `Administrators` role):

| Method | Route | Notes |
|---|---|---|
| POST | `/api/orders/{orderId}/dispatch` | notifies + queues a delivery follow-up a few days out |
| POST | `/api/orders/{orderId}/cancel` | notifies + calls off the pending follow-up |
| POST | `/api/notifications/{notificationId}/resend` | idempotent on body `idempotencyKey`; returns the new `notificationId` |
| DELETE | `/api/notifications/{notificationId}/content` | redacts content at the provider; record + outcome survive |
| GET | `/api/notifications/reconciliation?from={iso}&to={iso}` | provider ledger (From = configured number) vs eShop's records |

## Configuration

Bound from the `Twilio:` section — **values are never committed**; load them into user-secrets:

```
Twilio:AccountSid           (from TWILIO_ACCOUNT_SID)
Twilio:AuthToken            (from TWILIO_AUTH_TOKEN)   -- secret, never logged/returned
Twilio:FromNumber           (from TWILIO_FROM_NUMBER)
Twilio:MessagingServiceSid  (from TWILIO_MESSAGING_SERVICE_SID)  -- used for scheduled follow-ups
Twilio:BaseUrl              (optional; overrides the messaging API base URL only)
```

Load them (PowerShell/bash; values come from the environment, never typed into a file):

```bash
proj=src/PublicApi/PublicApi.csproj
dotnet user-secrets set --project $proj "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set --project $proj "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set --project $proj "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set --project $proj "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

Optional: `Notifications:FollowUpDelayDays` (default 3).

## Run it (this machine)

`global.json` rolls forward to the installed .NET 10 SDK. Run PublicApi in-memory:

```bash
export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:9963;http://localhost:9964"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory means data lives only for one run, and PublicApi has its own store — so place, dispatch
> and cancel the orders you created in that same run, through PublicApi.

## Step-by-step verification (curl)

Seeded users share password `Pass@word1`: `demouser@microsoft.com` (shopper) and
`admin@microsoft.com` (operator). Below, `$CA` = `TWILIO_TEST_TO_NUMBER` (Canadian, deliverable),
`$US` = `TWILIO_UNREACHABLE_TO_NUMBER` (US, undeliverable — the carrier refuses it; that is an
expected live-account outcome, not a defect). Send only to those two.

```bash
B=https://localhost:9963/api
tok() { curl -sk -X POST "$B/authenticate" -H 'Content-Type: application/json' \
        -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import json,sys;print(json.load(sys.stdin)['token'])"; }
DEMO=$(tok demouser@microsoft.com); ADMIN=$(tok admin@microsoft.com)
auth() { echo "Authorization: Bearer $1"; }

# 1) Contact number: an unusable number is rejected (400); a good one is stored canonically (201)
curl -sk -o /dev/null -w '%{http_code}\n' -X POST "$B/contact-numbers" -H "$(auth $DEMO)" -H 'Content-Type: application/json' -d '{"phoneNumber":"not-a-phone"}'      # 400
curl -sk -X POST "$B/contact-numbers" -H "$(auth $DEMO)" -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$CA\"}"                                            # 201 -> contactNumberId
curl -sk "$B/contact-numbers" -H "$(auth $DEMO)"

# 2) Place an order -> the shopper gets a "placed" text (really delivered to $CA)
curl -sk -X POST "$B/orders" -H "$(auth $DEMO)" -H 'Content-Type: application/json' \
     -d '{"items":[{"catalogItemId":4,"quantity":1},{"catalogItemId":5,"quantity":2}]}'      # 201 -> orderId (say 1)
curl -sk "$B/orders/1/notifications" -H "$(auth $DEMO)"    # OrderPlaced -> delivered

# 3) Dispatch (operator) -> "on its way" + a follow-up queued for a few days later
curl -sk -X POST "$B/orders/1/dispatch" -H "$(auth $ADMIN)"
curl -sk "$B/orders/1/notifications" -H "$(auth $DEMO)"    # DeliveryFollowUp -> scheduled, has SID, sendAt ~3 days out

# 4) Cancel (operator) -> the follow-up is called off before it can send
curl -sk -X POST "$B/orders/1/cancel" -H "$(auth $ADMIN)"
curl -sk "$B/orders/1/notifications" -H "$(auth $DEMO)"    # DeliveryFollowUp -> canceled

# 5) Undeliverable + resend (register $US under admin, place an order, resend the failed message)
curl -sk -X POST "$B/contact-numbers" -H "$(auth $ADMIN)" -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$US\"}"
curl -sk -X POST "$B/orders" -H "$(auth $ADMIN)" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":5,"quantity":1}]}'   # orderId (say 2)
curl -sk "$B/orders/2/notifications" -H "$(auth $ADMIN)"   # OrderPlaced -> undelivered (say notificationId 5)
curl -sk -X POST "$B/notifications/5/resend" -H "$(auth $ADMIN)" -H 'Content-Type: application/json' -d '{"idempotencyKey":"K1"}'  # -> new notificationId
curl -sk -X POST "$B/notifications/5/resend" -H "$(auth $ADMIN)" -H 'Content-Type: application/json' -d '{"idempotencyKey":"K1"}'  # SAME id (no 2nd send)
curl -sk -X POST "$B/notifications/5/resend" -H "$(auth $ADMIN)" -H 'Content-Type: application/json' -d '{"idempotencyKey":"K2"}'  # new id (legit 2nd send)

# 6) Dispose of a message's content (operator) -> content gone at provider, record + outcome kept
curl -sk -o /dev/null -w '%{http_code}\n' -X DELETE "$B/notifications/5/content" -H "$(auth $ADMIN)"   # 204
curl -sk "$B/orders/2/notifications" -H "$(auth $ADMIN)"    # notification 5: contentRedacted=true, status retained

# 7) Reconciliation over a range that has data (operator)
curl -sk "$B/notifications/reconciliation?from=2026-08-13T00:00:00Z&to=2026-08-14T00:00:00Z" -H "$(auth $ADMIN)"
```

What to look for: a **delivered** OrderPlaced to `$CA`; the follow-up going **scheduled → canceled**;
the `$US` message **undelivered**; the same idempotency key returning the **same** `notificationId`
while a fresh key returns a new one; a disposed notification keeping its status with
`contentRedacted=true`; and the reconciliation report counting only messages from `Twilio:FromNumber`,
matching eShop's records and surfacing anything on either side alone.

## Design notes

- **Owner identity** is the JWT name claim, used as both the order buyer id and the notification/number
  owner key, so every shopper endpoint acts only on the caller's own data. Cross-owner access returns 404.
- **Best-effort messaging**: a send/schedule failure is caught, recorded on the notification as a
  send error, and never fails the order operation.
- **Privacy**: destination numbers and the auth token are never written to logs or returned by any
  endpoint; only provider SIDs, statuses and error codes are surfaced.
- **Delivery outcomes** are refreshed from the provider when notifications are read (there is no public
  callback URL, so state is polled).
- **Scheduled follow-ups** use the Messaging Service; immediate messages use `Twilio:FromNumber` so the
  reconciliation `From` filter lines them up.
- **Content disposal** redacts the body at the provider (empty-body update) — distinct from deleting the
  message resource — so the send fact and outcome survive.
