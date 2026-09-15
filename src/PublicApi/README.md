# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The notification API uses Twilio Lookup v2 to validate and canonicalize contact numbers and
Twilio Programmable Messaging to send, schedule, poll, cancel, redact, and reconcile messages.
The implementation polls message resources because this application has no public callback URL.

Required settings are bound from the `Twilio` section. Keep credentials in user-secrets:

```powershell
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "Twilio:AccountSid" $env:TWILIO_ACCOUNT_SID
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "Twilio:AuthToken" $env:TWILIO_AUTH_TOKEN
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "Twilio:FromNumber" $env:TWILIO_FROM_NUMBER
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID
```

`Twilio:BaseUrl` defaults to `https://api.twilio.com`. An override applies to every
Programmable Messaging request, including paginated reconciliation requests. Lookup always
uses its separate official host.

Run PublicApi with its in-memory store for a local verification:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$process = Start-Process dotnet -ArgumentList @(
  "run", "--project", "src/PublicApi/PublicApi.csproj", "--launch-profile", "PublicApi"
) -WindowStyle Hidden -PassThru
```

Authenticate at `POST /api/authenticate`, then use its token as `Authorization: Bearer TOKEN`.
The shopper flow is:

1. Register only the supplied `TWILIO_TEST_TO_NUMBER` and `TWILIO_UNREACHABLE_TO_NUMBER` with
   `POST /api/contact-numbers` using `{ "phoneNumber": "..." }`.
2. Place an order with `POST /api/orders`. Supply `items` containing `catalogItemId` and
   `quantity`, plus `shippingAddress` containing `street`, `city`, `state`, `country`, and
   `zipCode`.
3. Poll `GET /api/orders/{orderId}/notifications` until the reachable message is `delivered`
   and the unreachable message is `undelivered` or `failed`.
4. As an administrator, resend the failed notification with
   `POST /api/notifications/{notificationId}/resend` and `{ "idempotencyKey": "..." }`.
   Repeating the key returns the same `notificationId`.
5. Delete the unreachable contact, dispatch the order as an administrator, and confirm the
   delivery follow-up is `scheduled`. Cancel the order and confirm that follow-up becomes
   `canceled`.
6. Dispose content with `DELETE /api/notifications/{notificationId}/content`; the subsequent
   order-notifications response has null content while retaining message identity and status.
7. Call `GET /api/notifications/reconciliation?from=...&to=...` as an administrator with
   ISO-8601 timestamps. The report includes matched, provider-only, and eShop-only records.

Stop the detached process with `Stop-Process -Id $process.Id`.

Provider contracts used by the integration are documented by Twilio in
[Lookup v2](https://www.twilio.com/docs/lookup/v2-api),
[the Message resource](https://www.twilio.com/docs/messaging/api/message-resource), and
[Message Scheduling](https://www.twilio.com/docs/messaging/features/message-scheduling).
