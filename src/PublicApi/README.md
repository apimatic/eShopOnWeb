# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Verify order SMS notifications

The PublicApi reads provider configuration from these user-secret keys: `Twilio:AccountSid`,
`Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optionally
`Twilio:BaseUrl`. For local development, populate them from the supplied environment variables:

```powershell
dotnet user-secrets set "Twilio:AccountSid" $env:TWILIO_ACCOUNT_SID --project src/PublicApi
dotnet user-secrets set "Twilio:AuthToken" $env:TWILIO_AUTH_TOKEN --project src/PublicApi
dotnet user-secrets set "Twilio:FromNumber" $env:TWILIO_FROM_NUMBER --project src/PublicApi
dotnet user-secrets set "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi
```

Run with `DOTNET_ROLL_FORWARD=Major`, `UseOnlyInMemoryDatabase=true`, and HTTPS/HTTP URLs from
the assigned port block. Authenticate `demouser@microsoft.com` and `admin@microsoft.com` at
`POST /api/authenticate`, then use their returned bearer tokens as follows:

1. Register only a supplied safe fixture with `POST /api/contact-numbers` and body
   `{"number":"<TWILIO_TEST_TO_NUMBER>"}`.
2. Place an order with `POST /api/orders`, supplying `items` (`catalogItemId`, `quantity`) and
   `shippingAddress`; retain the top-level `orderId`.
3. As the administrator, call `POST /api/orders/{orderId}/dispatch`, inspect
   `GET /api/orders/{orderId}/notifications`, then call `POST /api/orders/{orderId}/cancel`.
   The delivery follow-up must move from `scheduled` to `canceled`.
4. Delete that contact, register `TWILIO_UNREACHABLE_TO_NUMBER`, place another order, and read
   notifications until its provider status is `undelivered` or `failed`.
5. As the administrator, call `POST /api/notifications/{notificationId}/resend` twice with the
   same JSON `idempotencyKey`; both responses must return the same new `notificationId`.
6. Call `DELETE /api/notifications/{notificationId}/content`, then confirm the shopper view has
   `content: null`.
7. Call `GET /api/notifications/reconciliation?from=<ISO-8601>&to=<ISO-8601>` as the administrator.
   The report identifies matched, provider-only, and eShop-only records.
8. Delete the remaining contact and stop the detached PublicApi process. In-memory data is lost
   when the process stops.
