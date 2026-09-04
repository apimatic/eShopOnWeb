# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

PublicApi exposes the additive, JWT-authenticated subscription routes:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

The Maxio SDK is configured from the `Maxio` configuration section. Local development
uses user-secrets; the values must never be committed:

```text
Maxio:ApiKey
Maxio:Subdomain
Maxio:ProductFamilyHandle
Maxio:BaseUrl (optional override)
```

The product-family and product handles are resolved at runtime. Numeric Maxio IDs are
used only transiently for SDK requests, so catalog re-seeding does not require code
changes. The service derives stable customer and subscription references from the JWT
identity and reconciles read-before-create requests to make repeated enrollment safe.
