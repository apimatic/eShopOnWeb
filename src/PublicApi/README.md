# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

JWT-authenticated endpoints backed by Maxio (system of record), via the Maxio C# SDK:

- `GET /api/subscription-plans` - plans in the configured product family
- `POST /api/subscriptions` `{ "planHandle": "..." }` - idempotent subscribe (201 created, 200 if already subscribed)
- `GET /api/my-subscriptions` - the caller's subscriptions

Settings (section `Maxio`, keep values in user-secrets / environment, never in the repo):
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl` (used verbatim when set).
