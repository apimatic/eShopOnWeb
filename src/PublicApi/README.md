# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-protected subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

PublicApi reads the Maxio sandbox connection from the `Maxio` configuration section:
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and the optional
`Maxio:BaseUrl`. The base URL override is used verbatim when supplied; otherwise the
SDK derives the API host from the configured subdomain.
