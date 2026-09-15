# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The order-notification endpoints use JWT authentication and Twilio. Configure the provider in the
`Twilio` section. Keep credentials in user-secrets during local development:

```powershell
dotnet user-secrets set "Twilio:AccountSid" $env:TWILIO_ACCOUNT_SID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AuthToken" $env:TWILIO_AUTH_TOKEN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:FromNumber" $env:TWILIO_FROM_NUMBER --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi/PublicApi.csproj
```

`Twilio:BaseUrl` is optional. When supplied, it replaces the default base address for every
messaging request (send, fetch, update, and list); Lookup continues to use its own provider host.

For a local, infrastructure-free run:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$apiPort = [int]$env:APP_PORT_BLOCK_BASE + 3
$apiProcess = Start-Process dotnet `
  -ArgumentList @("run", "--project", "src/PublicApi/PublicApi.csproj", "--urls", "http://127.0.0.1:$apiPort") `
  -WindowStyle Hidden -PassThru
```

Authenticate at `POST /api/authenticate`, then use its token as `Authorization: Bearer <token>`.
The shopper routes are:

- `POST|GET /api/contact-numbers` and `DELETE /api/contact-numbers/{id}`
- `POST /api/orders`
- `GET /api/my-orders`
- `GET /api/orders/{id}/notifications`

The administrator routes are:

- `POST /api/orders/{id}/dispatch` and `POST /api/orders/{id}/cancel`
- `POST /api/notifications/{id}/resend`
- `DELETE /api/notifications/{id}/content`
- `GET /api/notifications/reconciliation?from=<ISO-8601>&to=<ISO-8601>`

Example request bodies:

```json
{ "phoneNumber": "<value of TWILIO_TEST_TO_NUMBER>" }
```

```json
{
  "items": [
    { "catalogItemId": 1, "quantity": 2 }
  ]
}
```

```json
{ "idempotencyKey": "operator-attempt-2026-08-28-1" }
```

The in-memory database is per host and is erased at restart. Place, dispatch, cancel, and inspect an
order through the same running PublicApi process.

Stop the host when finished, using only the assigned port and the process captured above:

```powershell
Get-NetTCPConnection -LocalPort $apiPort -State Listen |
  Select-Object -ExpandProperty OwningProcess -Unique |
  ForEach-Object { Stop-Process -Id $_ -Force }
Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
```
