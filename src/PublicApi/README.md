# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.
# PublicApi order SMS notifications

The notification API is JWT-authenticated. Shopper routes use the token's name claim; dispatch,
cancellation, resend, content disposal, and reconciliation require the existing `Administrators`
role.

## Local configuration

Twilio credentials are bound from the `Twilio` section. Keep them outside configuration files:

```powershell
dotnet user-secrets set "Twilio:AccountSid" "$env:TWILIO_ACCOUNT_SID" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AuthToken" "$env:TWILIO_AUTH_TOKEN" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:FromNumber" "$env:TWILIO_FROM_NUMBER" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:MessagingServiceSid" "$env:TWILIO_MESSAGING_SERVICE_SID" --project src/PublicApi/PublicApi.csproj
```

`Twilio:BaseUrl` is optional. If configured, it is the base address for every messaging request;
phone-number validation always uses Twilio Lookup's own host.

For an in-memory local run, set `UseOnlyInMemoryDatabase=true` and
`DOTNET_ROLL_FORWARD=Major`. Obtain shopper and administrator bearer tokens from
`POST /api/authenticate`.

## Routes

- Shopper: `POST|GET /api/contact-numbers`, `DELETE /api/contact-numbers/{id}`
- Shopper: `POST /api/orders`, `GET /api/my-orders`,
  `GET /api/orders/{id}/notifications`
- Administrator: `POST /api/orders/{id}/dispatch`, `POST /api/orders/{id}/cancel`
- Administrator: `POST /api/notifications/{id}/resend`,
  `DELETE /api/notifications/{id}/content`, `GET /api/notifications/reconciliation`

The follow-up created at dispatch is scheduled with Twilio for three days later. The application
does not send it from a timer; its background worker only retries persisted cancellation requests.
