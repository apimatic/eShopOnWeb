# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Payments go to Stax over plain HTTP (no SDK). Settings are read from the `Stax:` configuration section and never stored in the repository:

| Key | Meaning |
| --- | --- |
| `Stax:ApiKey` | merchant API key (sandbox) |
| `Stax:PlatformApiKey` | platform key, only needed for `POST /api/admin/sellers` |
| `Stax:BaseUrl` | optional; used verbatim as the API base address instead of the public one |

Load them into user-secrets, e.g. `dotnet user-secrets set "Stax:ApiKey" "<value of STAX_API_KEY>"` (and `Stax:PlatformApiKey` from `STAX_PLATFORM_API_KEY` when the shop has one). Without a platform key the sellers endpoint answers `422` with the reason.

Notes: card fees come from Stax's surcharge review; bank payments show `paid` only once the bank has settled them (a background service, `Stax:ReconcileIntervalSeconds`, default 60, and every order read check Stax); ACH refunds are only possible a few business days after the payment, so such a refund stays `pending` and is carried out automatically.
