# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The notification endpoints use JWT authentication from `POST /api/authenticate`. Configure the
provider through .NET user-secrets (or another .NET configuration provider); do not put credentials
in an appsettings file:

```powershell
dotnet user-secrets set "Twilio:AccountSid" $env:TWILIO_ACCOUNT_SID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AuthToken" $env:TWILIO_AUTH_TOKEN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:FromNumber" $env:TWILIO_FROM_NUMBER --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi/PublicApi.csproj
```

`Twilio:BaseUrl` is optional. When supplied, it is the base address for all Messaging API calls;
phone-number validation continues to use Twilio Lookup's own host.

Run PublicApi with `UseOnlyInMemoryDatabase=true` when SQL Server is unavailable. In-memory data is
host-local and disappears when PublicApi stops, so register contacts, place orders, and operate on
those orders during the same run.

Shopper routes:

- `POST/GET /api/contact-numbers`, `DELETE /api/contact-numbers/{id}`
- `POST /api/orders`, `GET /api/my-orders`
- `GET /api/orders/{id}/notifications`

Administrator routes:

- `POST /api/orders/{id}/dispatch`, `POST /api/orders/{id}/cancel`
- `POST /api/notifications/{id}/resend`
- `DELETE /api/notifications/{id}/content`
- `GET /api/notifications/reconciliation?from={ISO-8601}&to={ISO-8601}`
