# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Subscription billing is backed by Maxio (the system of record) through the SDK in `sdk/maxio`.
Settings come from the `Maxio:` configuration section; keep the values out of source control
(use `dotnet user-secrets` in `src/PublicApi`, or environment variables such as `Maxio__ApiKey`):

| Key | Meaning |
| --- | --- |
| `Maxio:ApiKey` | API key |
| `Maxio:Subdomain` | Site subdomain |
| `Maxio:Environment` | `US` or `EU` |
| `Maxio:BaseUrl` | Optional. Used verbatim as the API base address instead of one derived from the subdomain |

Endpoints (JWT; staff endpoints need the `Administrators` role):

- `GET /api/plans`, `POST /api/subscriptions` `{ "planHandle" }`
- `GET /api/subscriptions/{id}` (includes `paid`), `POST .../pause`, `.../resume`, `.../cancel`, `.../restart`, `.../switch` `{ "planHandle" }`
- `GET /api/subscriptions/{id}/usage?date=YYYY-MM-DD`
- `POST /api/admin/plans` `{ "name", "monthlyPrice", "brand" | "brandId" }`
- `POST /api/admin/plans/{planId}/currency-prices` `{ "currency", "price" }`
- `PUT /api/admin/subscriptions/{id}/next-billing-date` `{ "date" }`

A shopper is a Maxio customer whose reference is their lower-cased user name; subscriptions are
invoiced (no card). Plans of a catalog brand live in the Maxio product family `"<brand> plans"`.
The build needs the .NET 10 SDK because the Maxio SDK uses C# 14 (the apps still target net8.0).
