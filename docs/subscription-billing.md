# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This adds a **parallel**
recurring-subscription capability. It does not change or replace the cart and checkout.

**Maxio Advanced Billing is the system of record.** No plans, customers or subscriptions are stored
in the eShopOnWeb database. That is deliberate: it keeps the two flows independent, and it means the
capability behaves correctly even when the app runs on the in-memory database provider, which throws
its data away on restart.

## Endpoints

All three live on `src/PublicApi`, are JWT-authenticated, and take the caller's identity from the
token — never from the request body.

| Method | Route | Purpose |
| ------ | ----- | ------- |
| `GET`  | `/api/subscription-plans` | Plans on offer: the non-archived products of the configured product family. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. `201` when created, `200` when they were already subscribed. |
| `GET`  | `/api/my-subscriptions` | The caller's subscriptions, newest first, with the live ones called out separately. |

`POST /api/subscriptions` takes `{ "planHandle": "<handle>", "idempotencyKey": "<optional>" }`. The
idempotency key may also be sent as an `Idempotency-Key` header. There is no built-in default plan:
the handle is required so the same build works against any Maxio catalog.

Failures map to honest status codes — `400` for a request Maxio rejected as invalid, `404` for an
unknown plan handle, `409` for an unreconcilable duplicate submission, `502` when Maxio is
unreachable or answers with a failure, and `503` when billing has not been configured at all.

## How a shopper is linked to Maxio

The link is the Maxio customer `reference`, the field Maxio guarantees is unique per customer. It is
derived deterministically from the authenticated user name:

```
eshoponweb-demouser@microsoft.com
```

Because the reference is derived rather than stored, `GET /api/my-subscriptions` keeps working across
restarts even though the in-memory database re-seeds users with fresh ids each time.

## Idempotency

Subscribing is idempotent at three levels, so a double-click cannot produce two customers or two
subscriptions:

1. **Per-shopper serialization.** Concurrent subscribe requests for one shopper are serialized inside
   the process, so the "is there already a live subscription?" check cannot be raced by the request
   that is about to create one.
2. **A uniqueness token on the write.** Every create carries Maxio's `uniqueness_token`. Repeating a
   token inside Maxio's de-duplication window is refused with `409`, which is exactly the guard we
   want when two clicks land on different instances. Without a caller-supplied key the token is
   derived from (customer, plan).
3. **Reconciliation after a conflict.** On `409` the shopper's subscriptions are re-read. If the
   earlier submission produced a live subscription it is returned (`200`, `alreadySubscribed: true`).
   If it produced nothing, the signup is genuinely new and is retried under a fresh token — unless
   the caller supplied their own idempotency key, in which case the conflict is surfaced rather than
   quietly worked around.

De-duplication is **per plan**: a shopper can hold subscriptions to several plans, and a canceled or
expired subscription never blocks signing up again.

## Payment collection

The demo plans are configured with no payment method required, and this integration deliberately
captures no card details. Subscriptions are therefore created with an invoicing collection method
(`remittance` on Relationship Invoicing sites, `invoice` on legacy Statements sites) so Maxio raises
an invoice instead of trying to settle the first period against a card that is not there. Asking a
plan that *does* require a stored payment method to subscribe is rejected up front with a clear
message rather than being allowed to fail at the gateway.

## Configuration

Bound from the `Maxio` configuration section. **Values never go in the repository** — use
user-secrets locally and your platform's secret store elsewhere.

| Key | Required | Notes |
| --- | -------- | ----- |
| `Maxio:ApiKey` | yes | Maxio API key. Blank or absent ⇒ the three endpoints answer `503` and the rest of the API is unaffected. |
| `Maxio:Subdomain` | yes | Maxio site subdomain; the API base address is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address when set, instead of deriving one. Any path prefix is preserved. |
| `Maxio:Environment` | no | `US` (default) or `EU`; selects the host the base address is derived from. |
| `Maxio:PaymentCollectionMethod` | no | Overrides the collection method chosen for signups. |
| `Maxio:TimeoutSeconds` | no | Per-request timeout. Default `30`. |
| `Maxio:MaxRetries` | no | Retries after a throttled/transient failure. Default `3`. |
| `Maxio:CatalogCacheSeconds` | no | In-process plan cache. Default `60`; `0` disables. |
| `Maxio:CustomerReferencePrefix` | no | Prefix on derived customer references. Default `eshoponweb`. |

Load the sandbox credentials into user-secrets (values come from your environment, so nothing is
echoed into a file in the repo):

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets --project src/PublicApi set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"
```

## Layout

| Where | What |
| ----- | ---- |
| `src/ApplicationCore/Subscriptions` | Domain models: plans, subscriptions, the subscriber, subscribe command/result, state classification. |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability, provider-agnostic. |
| `src/ApplicationCore/Exceptions/Billing*.cs` | Billing failures the API layer maps to status codes. |
| `src/Infrastructure/Billing/Maxio` | The Maxio implementation: options, typed HTTP client, resilience handler, idempotency and mapping. |
| `src/Infrastructure/Billing/UnconfiguredSubscriptionBillingService.cs` | Stand-in when no provider is configured. |
| `src/PublicApi/SubscriptionEndpoints` | The three endpoints and their DTOs. |
| `tests/IntegrationTests/Billing/Maxio` | Provider behaviour against a stubbed Maxio (no network). |
| `tests/FunctionalTests/PublicApi/SubscriptionEndpoints` | Routing, authorization and status-code mapping through the real host. |
