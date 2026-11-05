# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

JWT-authenticated endpoints: `GET api/subscription-plans`, `POST api/subscriptions` (`{"planHandle":"..."}`), `GET api/my-subscriptions`.

Configuration (section `Maxio`; keep values in user-secrets or environment variables, never in files):

| Key | Purpose |
|-----|---------|
| `Maxio:ApiKey` | API key |
| `Maxio:Subdomain` | Site subdomain |
| `Maxio:ProductFamilyHandle` | Product family whose products are the plans |
| `Maxio:BaseUrl` | Optional. Used verbatim as the API base address instead of deriving one from the subdomain |
| `Maxio:Environment` | Optional. `US` (default) or `EU` |
| `Maxio:PaymentCollectionMethod` | Optional. `remittance` (default, no card needed), `automatic`, `prepaid` |

```
dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
```
