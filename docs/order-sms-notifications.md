# Order notifications by SMS (Twilio)

An additive capability on top of the existing catalog/basket/order flow: shoppers put a mobile
number on file, get texted as their orders move, and operators can see and act on what actually
reached the customer. Twilio is the messaging provider, reached exclusively through the
`AsadAli.TwilioSdk` .NET SDK.

## Where it lives

| Layer | What was added |
|---|---|
| `ApplicationCore/Entities` | `ContactNumber` and `OrderNotification` aggregate roots; `OrderStatus` + `Order.MarkDispatched()/MarkCancelled()` (additive to the existing `Order`). |
| `ApplicationCore/Interfaces` | `ISmsGateway` (provider abstraction — keeps the SDK out of the core), `IContactNumberService`, `IOrderNotificationService`. |
| `ApplicationCore/Services` | `ContactNumberService`, `OrderNotificationService` (orchestration). |
| `Infrastructure/Services/Sms` | `TwilioSmsGateway` (the only place the Twilio SDK is used), `TwilioSettings`, `AddTwilioMessaging(...)` DI. |
| `Infrastructure/Data` | EF configs + `DbSet`s for the two new entities. |
| `PublicApi/*Endpoints` | All HTTP endpoints (MinimalApi `IEndpoint` convention), JWT-authenticated. |

## Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

| Method | Route | Returns |
|---|---|---|
| POST | `/api/contact-numbers` | `contactNumberId` + stored canonical E.164 |
| GET | `/api/contact-numbers` | the caller's numbers |
| DELETE | `/api/contact-numbers/{contactNumberId}` | 204 |
| POST | `/api/orders` | `orderId` |
| GET | `/api/my-orders` | orders + each one's notification states |
| GET | `/api/orders/{orderId}/notifications` | notifications (each with `notificationId`) |

Operator actions (**Administrators** role only):

| Method | Route | Returns |
|---|---|---|
| POST | `/api/orders/{orderId}/dispatch` | marks dispatched, texts the shopper, queues a follow-up a few days out |
| POST | `/api/orders/{orderId}/cancel` | marks cancelled, texts the shopper, calls off the queued follow-up |
| POST | `/api/notifications/{notificationId}/resend` | `notificationId` of the message the resend produced (idempotency-keyed) |
| DELETE | `/api/notifications/{notificationId}/content` | redacts the message text at the provider; 204 |
| GET | `/api/notifications/reconciliation?from=&to=` | provider-vs-eShop reconciliation over an ISO-8601 range |

## Key behaviours

- **Validation up front.** A number is validated/canonicalized via Twilio Lookup v2 at registration;
  an unusable destination is rejected with 400. The provider's canonical E.164 form is what's stored.
- **Sends never fail the operation.** Order place/dispatch/cancel always succeed; a send that is
  rejected or unreachable is recorded as a notification outcome. An undeliverable US number surfaces as
  `undelivered`/`failed` with a provider error code — an expected outcome, not an error.
- **Follow-up is queued with the provider**, not by an in-app timer (Twilio scheduled message,
  `scheduleType=fixed` + `sendAt`), and cancelled at the provider when the order is cancelled.
- **State is obtained from the provider.** There is no public callback URL, so delivery outcomes are
  read from Twilio on demand (`FetchMessage`) when notifications are listed.
- **Content disposal** redacts the body at the provider (`UpdateMessage` with an empty body) while the
  sent-fact and outcome survive.
- **Reconciliation asks the provider for only `Twilio:FromNumber`'s messages** (server-side `From` +
  `DateSent` filters, paginated) and lines them up against eShop's records both ways.
- **Privacy & secrets.** A shopper's number is never logged and never returned by an endpoint. The
  Twilio auth token is never logged, returned, or written to a source file — all `Twilio:` settings come
  from configuration/user-secrets.

## Configuration (`Twilio:` section)

`AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`, and optional `BaseUrl`
(overrides the **messaging** API base address only — not the Lookup host). Load them into user-secrets;
never commit values.

```bash
dotnet user-secrets set "Twilio:AccountSid" "$TWILIO_ACCOUNT_SID"          --project src/PublicApi
dotnet user-secrets set "Twilio:AuthToken" "$TWILIO_AUTH_TOKEN"            --project src/PublicApi
dotnet user-secrets set "Twilio:FromNumber" "$TWILIO_FROM_NUMBER"          --project src/PublicApi
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" --project src/PublicApi
```

## Running (this machine)

The SDK is pinned to 8.0.x but only the .NET 10 SDK is installed, and there is no LocalDB, so:

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build eShopOnWeb.sln -c Debug
UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:9403;http://localhost:9404" \
dotnet run --project src/PublicApi -c Debug --no-launch-profile
```

With the in-memory provider, PublicApi keeps its own store — place, dispatch and cancel orders through
this API within a single run. Get a bearer token from `POST /api/authenticate`
(`admin@microsoft.com` / `Pass@word1` for operator actions; `demouser@microsoft.com` for a plain shopper).
