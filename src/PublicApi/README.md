# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Plans and subscriptions live in Maxio (the system of record), called through the SDK in `sdk/maxio`.
Settings come from the `Maxio:` configuration section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`
(`US` or `EU`) and the optional `Maxio:BaseUrl`, which when set is used verbatim as the API base address.
Keep the values out of the repository, e.g. with user-secrets (run in `src/PublicApi`):

```
dotnet user-secrets set "Maxio:ApiKey" "<value of MAXIO_API_KEY>"
dotnet user-secrets set "Maxio:Subdomain" "<value of MAXIO_SITE_SUBDOMAIN>"
dotnet user-secrets set "Maxio:Environment" "<value of MAXIO_ENVIRONMENT>"
```

Endpoints (shoppers: any signed-in user; staff: the Administrators role): `GET /api/plans`,
`POST /api/subscriptions`, `GET /api/subscriptions/{id}`, `POST /api/subscriptions/{id}/pause|resume|cancel|restart|switch`,
`GET /api/subscriptions/{id}/usage?date=YYYY-MM-DD`, `POST /api/admin/plans`,
`POST /api/admin/plans/{planId}/currency-prices`, `PUT /api/admin/subscriptions/{id}/next-billing-date`.
A shopper is a Maxio customer whose reference is their user name; a request the app cannot carry out gets a 4xx with a reason.
The tests in `tests/PublicApiIntegrationTests/BillingEndpoints` run against the sandbox when the `MAXIO_*` variables are set.
