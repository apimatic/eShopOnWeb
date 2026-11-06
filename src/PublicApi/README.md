# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Plans and subscriptions live in Maxio (the billing system of record), called through the SDK in `sdk/maxio`.
A shopper is a Maxio customer whose `reference` is the user name in the JWT; shoppers only ever see their own subscriptions (others answer 404).
Subscriptions are created invoiced (remittance), so no card details are needed. A plan's brand is the Maxio product family `"{brand} plans"`.

| Route | Who |
| --- | --- |
| `GET /api/plans` | signed-in |
| `POST /api/subscriptions` `{planHandle}` | signed-in |
| `GET /api/subscriptions/{id}` (includes `paid`) | owner |
| `POST /api/subscriptions/{id}/pause`, `/resume`, `/cancel`, `/restart` | owner |
| `POST /api/subscriptions/{id}/switch` `{planHandle}` (also works on cancelled or paused ones) | owner |
| `GET /api/subscriptions/{id}/usage?date=YYYY-MM-DD` (day in the Maxio site's time zone) | owner |
| `POST /api/admin/plans` `{name, price, brandId}` | Administrators |
| `POST /api/admin/plans/{planId}/currency-prices` `{currency, price}` | Administrators |
| `PUT /api/admin/subscriptions/{id}/next-billing-date` `{date}` | Administrators |

Requests the app cannot carry out answer 4xx with `{statusCode, message}`.

### Settings

Read from the `Maxio` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US`/`EU`) and the optional `Maxio:BaseUrl`
(used verbatim as the API base address when set). Keep credentials out of the repository, in user-secrets, e.g.:

```
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT" --project src/PublicApi
```

The SDK needs the .NET 10 SDK to build (`global.json`); the app itself still targets net8.0.
