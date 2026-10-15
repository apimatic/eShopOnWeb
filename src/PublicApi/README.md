# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Maxio subscription billing

Subscriptions are billed through Maxio Advanced Billing (system of record). Endpoints (JWT bearer required):

- `GET /api/subscription-plans` - plans of the configured product family
- `POST /api/subscriptions` with `{ "planHandle": "<handle>" }` - subscribe the caller (idempotent)
- `GET /api/my-subscriptions` - the caller's subscriptions (state, price, next billing date)

Configuration (section `Maxio`; the host refuses to start without the first three): `Maxio:ApiKey`, `Maxio:Subdomain` (optional when `BaseUrl` is set), `Maxio:ProductFamilyHandle`, and the optional `Maxio:BaseUrl` override used verbatim as the API base address. Load them into user-secrets with `scripts/load-maxio-secrets.sh`.

The Maxio SDK is referenced as source from the maxio-advanced-billing plugin; override the path with `-p:MaxioSdkProject=<path>` if your plugin copy lives elsewhere.
