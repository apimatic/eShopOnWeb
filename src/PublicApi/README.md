# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription Billing (Maxio Advanced Billing)

Recurring-subscription billing is available alongside the one-time commerce flow,
with **Maxio Advanced Billing** as the billing system of record.

### Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/subscription-plans` | anonymous | Lists the plans available for subscription (from the configured Maxio product family). |
| POST | `/api/subscriptions` | JWT bearer | Subscribes the authenticated user to a plan. Body: `{ "planHandle": "eshop-pro" }`. Idempotent — repeating it returns the existing subscription instead of creating a duplicate. |
| GET | `/api/my-subscriptions` | JWT bearer | Lists the authenticated user's subscriptions (plan, price, state, next billing date). |

Get a JWT first via `POST /api/authenticate` (`{ "username": "...", "password": "..." }`),
then send `Authorization: Bearer {token}`.

Responses: unknown plan → `404`; missing/invalid token → `401`; Maxio API rejection → `502` with the errors returned by Maxio.

### How it works

- A Maxio **customer** is created (or found) per eShopOnWeb user. The Maxio customer
  `reference` is the eShopOnWeb user id, and the Maxio subscription `reference` is
  `{userId}:{planHandle}` — both are unique keys enforced by Maxio, so double-clicks
  never create duplicates (verified on Maxio's side: duplicate `reference` creation
  returns HTTP 422, and the existing record is then returned).
- Subscriptions are created with `payment_collection_method=remittance`, which is
  verified to enroll without requiring a stored payment method (no card capture / 3-DS).
- The `userId ↔ subscription` mapping is also persisted locally (`Subscription` entity
  in `CatalogContext`) for fast lookups and a second layer of duplicate protection.
  Note: with `UseOnlyInMemoryDatabase=true` this mapping is lost on restart, but the
  service heals it from Maxio on the next subscribe.

### Configuration

Settings are bound from the `Maxio:` configuration section and must be provided via
**user-secrets or environment variables** — never hard-coded:

| Key | Environment variable | Purpose |
|-----|----------------------|---------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Maxio Advanced Billing API key |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Maxio site subdomain |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are offered as plans |
| `Maxio:BaseUrl` | — (optional) | When set, used verbatim as the API base address instead of deriving `https://{subdomain}.chargify.com` |

Load them into user-secrets for local development:

```
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

### Verified Maxio API behavior (live sandbox)

The `MaxioBillingClient` (src/Infrastructure/Services/MaxioBilling) encodes the following,
each verified against a live Maxio Advanced Billing sandbox:

- Auth: HTTP Basic, API key as username and the literal `x` as password.
- `POST` bodies are sent **form-encoded** (`customer[...]`, `subscription[...]`): the
  sandbox consistently returned HTTP 500 for JSON POSTs that include a `reference` field.
- `GET /customers.json?reference={ref}`: no match → HTTP 404 (empty body); single
  match → `{"customer": {...}}` (object wrapper, not an array).
- `GET /subscriptions.json?customer_id={id}`: unknown customer → HTTP 404; known
  customer without subscriptions → `[]`. This filter is verified correct; the global
  `?reference=` filter on `/subscriptions.json` proved unreliable on the sandbox and
  is therefore **not** used — subscription lookups are customer-scoped with exact
  client-side reference matching.
- `GET /subscriptions/{id}.json`: unknown id → HTTP 404.
- The metered component `api-call` ($0.01/unit) exists on the product family but is
  not used by the subscription endpoints above.

