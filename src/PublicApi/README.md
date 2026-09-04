# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The subscription endpoints are additive to the existing catalog and checkout API:

- `GET /api/subscription-plans` lists active plans in `Maxio:ProductFamilyHandle`.
- `POST /api/subscriptions` accepts `{ "planHandle": "..." }` and provisions the plan for the JWT user.
- `GET /api/my-subscriptions` returns the current Maxio subscriptions for the JWT user.

Configure Maxio through user-secrets or another configuration provider using these keys only:
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and the optional `Maxio:BaseUrl`.
When `Maxio:BaseUrl` is absent, the direct Billing API address is derived from `Maxio:Subdomain`.
