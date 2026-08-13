# Order notifications by SMS (Twilio)

An additive capability on top of the existing eShopOnWeb catalog/basket/order flow: shoppers put a
mobile number on file, get texted as their orders move (placed → dispatched → cancelled), a
post-delivery follow-up is scheduled with Twilio and called off if the order is cancelled, and an
operator can re-send, dispose of message content, and reconcile against Twilio's own records.

All capabilities are HTTP endpoints on **`src/PublicApi`** (JWT-authenticated), routed under `/api/`.

## Architecture

- **Domain (`ApplicationCore`)** — new `ContactNumber` and `Notification` aggregates; `Order` gains a
  `Status` (`Placed`/`Dispatched`/`Cancelled`) with `Dispatch()`/`Cancel()` behavior. `ISmsSender`
  abstracts the provider. `ContactNumberService` and `OrderNotificationService` orchestrate the flows.
- **Infrastructure** — `TwilioSmsSender` talks to Twilio over plain HTTP (chosen so the
  `Twilio:BaseUrl` override governs every messaging call while Lookup stays on its own host, and so
  reconciliation can ask Twilio to filter by sending number). EF configs + `CatalogContext` DbSets.
- **PublicApi (`SmsNotificationEndpoints/`)** — one endpoint per action, following the project's
  `MinimalApi.Endpoint` conventions. Operator actions require the `Administrators` role.

### Twilio contract used (verified against Twilio docs)
| Capability | Call |
|---|---|
| Validate + canonicalize number | `GET https://lookups.twilio.com/v2/PhoneNumbers/{E164}` → `valid`, `phone_number` |
| Send | `POST {base}/2010-04-01/Accounts/{Sid}/Messages.json` (`To`,`From`,`Body`) |
| Schedule follow-up | same + `MessagingServiceSid`,`SendAt`(ISO-8601),`ScheduleType=fixed` (15 min–35 days out) |
| Cancel scheduled | `POST .../Messages/{Sid}.json` `Status=canceled` |
| Read status | `GET .../Messages/{Sid}.json` → `status`,`error_code` |
| Dispose content | `POST .../Messages/{Sid}.json` `Body=` (empty) |
| Reconcile | `GET .../Messages.json?From={E164}&DateSent>=&DateSent<=` (+ `next_page_uri`) |

`{base}` = `Twilio:BaseUrl` when set (messaging API only), else `https://api.twilio.com`. Lookup is
never governed by `Twilio:BaseUrl`.

## Configuration

Settings are bound from the **`Twilio:`** configuration section — nothing is hard-coded:

| Key | Source env var |
|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` (secret) | `TWILIO_AUTH_TOKEN` |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` *(optional)* | — (messaging-API override) |

Load them into **.NET user-secrets** for the `PublicApi` project (values never go into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

The auth token is never logged, never returned by an endpoint, and never written to a source file.
Shopper phone numbers are never written to logs.

## Running (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major          # global.json pins 8.0.x; only .NET 10 SDK is installed
export ASPNETCORE_ENVIRONMENT=Development  # loads user-secrets
export UseOnlyInMemoryDatabase=true        # no LocalDB here
export ASPNETCORE_URLS="https://localhost:10243;http://localhost:10244"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory stores are per-process and lost on restart, so create, dispatch and cancel orders within
> the same run. Swagger UI: `https://localhost:10243/swagger`.

## Endpoints

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/contact-numbers` | shopper | register a number (validated + canonicalized) → `contactNumberId` |
| `GET /api/contact-numbers` | shopper | caller's numbers |
| `DELETE /api/contact-numbers/{id}` | shopper | remove one |
| `POST /api/orders` | shopper | place order from catalog items → `orderId` |
| `POST /api/orders/{id}/dispatch` | operator | mark dispatched (+schedule follow-up) |
| `POST /api/orders/{id}/cancel` | operator | cancel (+call off follow-up) |
| `GET /api/my-orders` | shopper | caller's orders with notification outcomes |
| `GET /api/orders/{id}/notifications` | shopper | notifications for one order (each has `notificationId`) |
| `POST /api/notifications/{id}/resend` | operator | idempotent re-send → `notificationId` |
| `DELETE /api/notifications/{id}/content` | operator | dispose of message content |
| `GET /api/notifications/reconciliation?from=&to=` | operator | Twilio vs eShop over an ISO-8601 range |

## Verify it yourself

Use only the two provided destinations: `TWILIO_TEST_TO_NUMBER` (Canadian, deliverable) and
`TWILIO_UNREACHABLE_TO_NUMBER` (US, accepted then refused by the carrier — `error_code 30034`, an
expected outcome for this account, not a defect).

```bash
API=https://localhost:10243
pw='Pass@word1'
SHOP=$(curl -sk $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"'"$pw"'"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')
ADMIN=$(curl -sk $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"'"$pw"'"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# 1) register both numbers (a bogus one is rejected up front)
curl -sk $API/api/contact-numbers -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"phoneNumber":"'"$TWILIO_TEST_TO_NUMBER"'"}'
curl -sk $API/api/contact-numbers -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"phoneNumber":"'"$TWILIO_UNREACHABLE_TO_NUMBER"'"}'
curl -sk $API/api/contact-numbers -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"phoneNumber":"not-a-number"}'   # 400

# 2) place an order (returns orderId); the Canadian number really receives a text
curl -sk $API/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":1,"quantity":1}]}'
curl -sk $API/api/orders/1/notifications -H "Authorization: Bearer $SHOP"   # delivered (CA) / undelivered 30034 (US)

# 3) dispatch (operator): 'on its way' + a follow-up is SCHEDULED with Twilio
curl -sk -X POST $API/api/orders/1/dispatch -H "Authorization: Bearer $ADMIN"
curl -sk $API/api/orders/1/notifications -H "Authorization: Bearer $SHOP"   # DeliveryFollowUp -> status "scheduled"

# 4) cancel (operator): the scheduled follow-up is called off before it can go out
curl -sk -X POST $API/api/orders/1/cancel -H "Authorization: Bearer $ADMIN"
curl -sk $API/api/orders/1/notifications -H "Authorization: Bearer $SHOP"   # DeliveryFollowUp -> status "canceled"

# 5) resend (operator), idempotent on the key; repeat same key = no 2nd send, fresh key = new send
curl -sk -X POST $API/api/notifications/2/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: k1' -d '{}'
curl -sk -X POST $API/api/notifications/2/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: k1' -d '{}'  # Duplicate
curl -sk -X POST $API/api/notifications/2/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: k2' -d '{}'  # new send

# 6) dispose of a message's content (gone at Twilio too, record survives)
curl -sk -X DELETE $API/api/notifications/2/content -H "Authorization: Bearer $ADMIN"

# 7) reconciliation over a populated range (operator)
FROM=$(python -c 'import time;print(time.strftime("%Y-%m-%dT00:00:00Z",time.gmtime()))')
TO=$(python -c 'import time;print(time.strftime("%Y-%m-%dT%H:%M:%SZ",time.gmtime(time.time()+120)))')
curl -sk "$API/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

A message that cannot be sent never fails the order operation; a shopper with no number on file is
simply not messaged; and every shopper-scoped endpoint acts only on the caller's own data.
