# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Card and bank payments go through the Stax SDK (`sdk/stax`). Settings are read from the `Stax:` configuration section:

| Key | Meaning |
| --- | --- |
| `Stax:ApiKey` | the shop's Stax API key (sandbox key while testing) |
| `Stax:PlatformApiKey` | the Stax platform key; only needed to onboard sellers (`POST /api/admin/sellers`) |
| `Stax:BaseUrl` | optional; used verbatim as the API base address instead of the public one |

Keep the values out of the repository, in user-secrets:

```
dotnet user-secrets set "Stax:ApiKey" "<value of STAX_API_KEY>"
dotnet user-secrets set "Stax:PlatformApiKey" "<value of STAX_PLATFORM_API_KEY>"   # when the shop has one
```

A background worker (`StaxReconciliationService`) marks bank payments paid once the bank settles them and keeps
retrying refunds that Stax cannot make yet (a bank payment can only be refunded some business days after it was made).
