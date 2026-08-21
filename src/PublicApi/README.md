# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The JWT-authenticated subscription capability is exposed at:

- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

Maxio Advanced Billing is the system of record. The checked-in `maxio-spec/openapi.yaml`
contract defines the HTTP paths, schemas, authentication, server template, and errors used
by the client. `SubscriptionRecords` is only a locally reconciled user-to-subscription
projection.

PublicApi binds these keys from the `Maxio` configuration section:

- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional; replaces the spec-derived subdomain URL verbatim)

At deployment, `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and
`MAXIO_DEFAULT_PRODUCT_FAMILY` are translated to the first three keys. For local
development, keep their values outside the repository by loading the environment variables
into the PublicApi user-secret store.
