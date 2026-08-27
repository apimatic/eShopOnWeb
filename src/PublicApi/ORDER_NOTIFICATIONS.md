# Order SMS notifications

PublicApi exposes the order-notification workflow under `/api`. Shopper endpoints use the
JWT name claim as their buyer id. Dispatch, cancellation, resend, content disposal, and
reconciliation require the existing `Administrators` role.

## Configuration

The integration binds `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`,
`Twilio:MessagingServiceSid`, and optional `Twilio:BaseUrl`. Conventional deployment
environment variables are mapped to the first four keys in `Program.cs`. For local use,
load them without putting values in the repository:

```powershell
dotnet user-secrets set 'Twilio:AccountSid' $env:TWILIO_ACCOUNT_SID --project src/PublicApi
dotnet user-secrets set 'Twilio:AuthToken' $env:TWILIO_AUTH_TOKEN --project src/PublicApi
dotnet user-secrets set 'Twilio:FromNumber' $env:TWILIO_FROM_NUMBER --project src/PublicApi
dotnet user-secrets set 'Twilio:MessagingServiceSid' $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi
```

`Twilio:BaseUrl` overrides only the API v2010 messaging host. Lookups v2 always uses its
contract host. The hand-written client in `Infrastructure/Twilio` follows the supplied
`twilio_api_v2010.yaml` and `twilio_lookups_v2.yaml`; no Twilio SDK is used.

## Endpoints

- `POST`, `GET`, `DELETE /api/contact-numbers[/{id}]`
- `POST /api/orders`
- `POST /api/orders/{id}/dispatch`
- `POST /api/orders/{id}/cancel`
- `GET /api/my-orders`
- `GET /api/orders/{id}/notifications`
- `POST /api/notifications/{id}/resend`
- `DELETE /api/notifications/{id}/content`
- `GET /api/notifications/reconciliation?from=...&to=...`

Run locally with `UseOnlyInMemoryDatabase=true`. Keep one PublicApi process alive for the
entire flow because that provider loses state on restart. The reconciliation client sends
`From=Twilio:FromNumber` and both date bounds to Twilio and follows every `next_page_uri`.

## Automated verification

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
```

The notification tests replace the provider gateway with an in-process contract fake, so
they do not send or charge for messages.
