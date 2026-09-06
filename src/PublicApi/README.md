# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing is an **additive** capability: it runs alongside the existing
Catalog → Basket → Order flow and replaces none of it. Maxio Advanced Billing is the system of
record — eShopOnWeb stores no copy of the customer or subscription.

### Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The caller's identity comes
from the token, never from the request body, so a shopper can only ever act on their own
subscriptions.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans on offer, scoped to the configured product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan. `201` on a fresh enrollment, `200` with `alreadySubscribed: true` on an idempotent replay. |
| `GET /api/my-subscriptions` | Every subscription the caller holds, read live from Maxio. |

`POST /api/subscriptions` body:

```json
{ "planHandle": "your-plan-handle", "idempotencyKey": "optional" }
```

### Configuration

Bound from the `Maxio` section. **Never commit values** — supply them through
`dotnet user-secrets` locally, or the environment (`Maxio__ApiKey`, `Maxio__Subdomain`, …) in a
deployment.

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Advanced Billing API key. Sent as the HTTP Basic username with the literal `x` as the password. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Advanced Billing site subdomain. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Overrides the API base address; used verbatim when set. |
| `Maxio:Environment` | no | `US` (default) or `EU`. Only consulted when `BaseUrl` is absent. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to `remittance`. |
| `Maxio:TimeoutSeconds` | no | Latency budget per API call, retries included. Default 30. |
| `Maxio:MaxRetries` | no | Transient-failure retries. Default 3. |

When `BaseUrl` is absent the base address is derived as `https://{Subdomain}.chargify.com` (US) or
`https://{Subdomain}.ebilling.maxio.com` (EU).

Local setup:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

If the section is missing the application still starts and everything else keeps working; the
three subscription endpoints answer `503` naming the missing keys. Startup logs say which state
you are in.

### How idempotency works

Every record this integration creates carries a deterministic `reference`, and Maxio enforces
uniqueness on it. That check lives in the provider, so it holds across concurrent requests and
across application instances with no local state to keep in sync.

* Customer reference is derived from the eShopOnWeb **user name**. It is stable across restarts,
  which is why the link survives the in-memory database (where every local identifier is
  regenerated on restart).
* Subscription reference is scoped to the customer and to an idempotency key, which defaults to
  the plan handle. A subscriber therefore holds at most one live subscription per plan, and a
  double-click resolves to the subscription that already exists instead of billing a second one.
* Pass an explicit `idempotencyKey` to deliberately create an additional subscription — for
  example when re-subscribing to a plan that was cancelled.

### Error responses

| Status | When |
| --- | --- |
| `400` | `planHandle` missing. |
| `401` | No or invalid bearer token. |
| `404` | The plan handle is not offered in the configured product family. |
| `409` | The idempotency key was already consumed by a subscription that is no longer live. |
| `422` | The provider refused the request (its messages are included). |
| `502` | The provider failed or was unreachable. |
| `503` | Subscription billing is not configured on this deployment. |
