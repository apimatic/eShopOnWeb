# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio Advanced Billing subscriptions

JWT-protected endpoints keep Maxio Advanced Billing as the billing system of record:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Configure the `Maxio` section through user-secrets (never committed): `ApiKey`, `Subdomain`,
`ProductFamilyHandle`, and optional `BaseUrl`. If `BaseUrl` is omitted, the API base is derived as
`https://{Subdomain}.chargify.com`.

A stable Maxio customer reference comes from the authenticated identity. A unique local enrollment
intent per user and product handle, together with a Maxio read-before-create, makes retries return
the existing subscription instead of creating a duplicate.
