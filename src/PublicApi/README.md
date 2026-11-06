# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Subscriptions are kept in Maxio (the billing system of record) and reached through the SDK in `sdk/maxio`.
Settings come from the `Maxio` configuration section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US` or `EU`)
and the optional `Maxio:BaseUrl`, which is used verbatim as the API base address when set. Keep them out of the repository, for example:

```
dotnet user-secrets set "Maxio:ApiKey" "<value of MAXIO_API_KEY>"
dotnet user-secrets set "Maxio:Subdomain" "<value of MAXIO_SITE_SUBDOMAIN>"
dotnet user-secrets set "Maxio:Environment" "<value of MAXIO_ENVIRONMENT>"
```

Shopper endpoints (any signed-in user): `GET /api/plans`, `POST /api/subscriptions`, `GET /api/subscriptions/{id}`,
`POST /api/subscriptions/{id}/pause|resume|cancel|restart|switch`, `GET /api/subscriptions/{id}/usage?date=YYYY-MM-DD`.
Staff endpoints (Administrators): `POST /api/admin/plans`, `POST /api/admin/plans/{planId}/currency-prices`,
`PUT /api/admin/subscriptions/{id}/next-billing-date`.
A plan's brand is stored in the Maxio product description as `Brand: <name>`; shoppers are matched to Maxio customers by their
user name (customer reference). Requests the app cannot carry out are answered with a 4xx status and a `Message`.
