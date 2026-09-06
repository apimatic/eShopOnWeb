# Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing for eShopOnWeb, with [Maxio Advanced Billing](https://developers.maxio.com/)
(formerly Chargify) as the system of record. This runs **alongside** the existing one-time
Catalog → Basket → Order flow; nothing about that flow changes.

## Endpoints

All three are JWT-authenticated. The shopper's identity comes from the bearer token and nothing
else, so a caller can only ever read or change their own subscriptions.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans available to subscribe to. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. |
| `GET` | `/api/my-subscriptions` | The caller's subscriptions, newest first. |

### `POST /api/subscriptions`

```jsonc
// request
{ "planHandle": "eshop-pro" }        // optional when Maxio:DefaultPlanHandle is configured
```

```jsonc
// 201 Created — the shopper was just enrolled
{
  "subscription": {
    "id": 94208831,
    "state": "active",
    "isLive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299,
    "currency": "USD",
    "intervalLength": 1,
    "intervalUnit": "month",
    "nextBillingAt": "2026-10-06T12:26:01+05:00",
    "paymentCollectionMethod": "remittance",
    "billingCustomerId": 98837545
  },
  "alreadyExisted": false
}
```

Responds `200 OK` with `"alreadyExisted": true` when the request was a replay.

Optional request header: **`Idempotency-Key`** (≤ 128 characters). See below.

### Status codes

| Status | Meaning |
| --- | --- |
| `201` | The shopper was enrolled. |
| `200` | Already subscribed — the request was a no-op. |
| `400` | No plan named and no default configured, a bad `Idempotency-Key`, or Advanced Billing refused the request. |
| `401` | Missing or invalid bearer token. |
| `404` | The named plan is not in the configured product family. |
| `500` | Billing is misconfigured. The detail is logged, not returned. |
| `503` | Advanced Billing was unreachable, throttled, or erroring. Retry. |

## Configuration

Bound from the `Maxio` configuration section.

| Key | Required | Default | Notes |
| --- | --- | --- | --- |
| `Maxio:ApiKey` | yes | — | **Secret.** Basic-auth user name; the password is the literal `x`. |
| `Maxio:Subdomain` | yes¹ | — | `acme` for `https://acme.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | — | Only products in this family are offered as plans. |
| `Maxio:BaseUrl` | no | — | Used **verbatim** as the API base address when set, in place of the address derived from the subdomain. |
| `Maxio:Environment` | no | `US` | `US` or `EU`. |
| `Maxio:DefaultPlanHandle` | no | — | Plan used when a request names none. Unset means callers must always name one. |
| `Maxio:PaymentCollectionMethod` | no | `remittance` | `remittance`, `automatic`, `prepaid` or `invoice`. |
| `Maxio:ReferencePrefix` | no | `eshoponweb` | Namespaces the references this integration owns. |
| `Maxio:CatalogCacheDuration` | no | `00:01:00` | Plan catalog cache TTL. `00:00:00` disables it. |
| `Maxio:Timeout` | no | `00:00:30` | Per-request timeout. |
| `Maxio:MaxRetries` | no | `3` | Retries for reads. Writes are never retried in transport. |
| `Maxio:RetryBaseDelay` | no | `200ms` | Base for exponential backoff with full jitter. |
| `Maxio:MaxConcurrentRequests` | no | `4` | Client-side ceiling on in-flight requests. |

¹ Optional when `Maxio:BaseUrl` is set.

Settings are validated at **startup**, so a misconfigured deployment fails immediately rather than
on the first shopper who tries to subscribe. Validation messages name the setting, never its value.

### Supplying the API key

The key is a credential and must never be committed. Use user-secrets locally:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Elsewhere, use environment variables (`Maxio__ApiKey`, …) or a secret store. User-secrets are only
loaded in the Development environment.

`Maxio:PaymentCollectionMethod` defaults to `remittance` because it does not require a payment method
on file, which is what lets a shopper subscribe without card capture or a 3-DS round trip. Sites that
capture cards before subscribing should set it to `automatic`.

## How subscribing stays idempotent

A double-clicked Subscribe button must not enroll a shopper twice, and no local database can promise
that — the app runs on the in-memory provider here, and would run on several instances in production.
So the guarantee is anchored in Advanced Billing itself, which enforces uniqueness on both
`customer.reference` and `subscription.reference` and answers a duplicate with
`422 Reference: must be unique`.

Three layers, weakest to strongest:

1. **A per-shopper gate** (`SubscriberGate`) serialises one process's concurrent requests for the same
   shopper, so the common case resolves with a single write. Shoppers are gated independently.
2. **Read before write.** The shopper's existing subscriptions are fetched first; a live subscription to
   the requested plan short-circuits the request and is returned as-is.
3. **Deterministic references** (`MaxioReferenceFactory`). Customers key on the shopper's email;
   subscriptions key on email + plan + how many times that shopper has taken that plan. If a concurrent
   instance wins the race, our create comes back `422` — and rather than pattern-matching on Advanced
   Billing's error prose, the service re-reads and returns whatever now exists under that reference.

The reference keys on **email**, not the Identity user id: it is the shopper's stable business identity
and it survives restarts, which the in-memory database's regenerated user ids do not. References are
capped at the 255 characters Advanced Billing accepts, collapsing to a SHA-256 digest rather than
truncating — truncation could let two shoppers share a subscription.

`Idempotency-Key` overrides layer 2 when a caller wants to define replay scope themselves. Two requests
carrying the same key are the same request; two requests carrying different keys are different requests,
even for the same plan. Send one if a shopper may legitimately hold several subscriptions to one plan.

## Design notes

- **Advanced Billing is the system of record.** eShopOnWeb keeps no local mirror of who is subscribed
  to what, so renewals, dunning and cancellations Advanced Billing performs on its own are reflected
  the moment a shopper looks, with no reconciliation job to get wrong.
- **The official SDK** ([`Maxio.AdvancedBillingSdk`](https://www.nuget.org/packages/Maxio.AdvancedBillingSdk),
  generated by Maxio) carries the request and response contract, so the shapes on the wire are the
  vendor's rather than a hand-maintained copy.
- **The `Maxio:BaseUrl` override** is implemented as a `DelegatingHandler` on the `HttpClient` the SDK is
  given. The SDK builds URLs from a fixed `https://{site}.chargify.com` template with no hook to replace
  it, so the authority is rewritten in the pipeline; paths, queries, headers and bodies are untouched.
- **Product families resolve by handle** (`handle:eshop-subscribe`), not numeric id — handles are stable
  across catalog re-seeds and ids are not.
- **Plans are restricted to the configured product family**, so a handle from elsewhere on the billing
  site cannot be used to subscribe to something eShopOnWeb does not sell.
- **Reads are retried, writes are not** (`MaxioResilienceHandler`), honouring `Retry-After` and backing
  off with full jitter. A replayed write could enroll a shopper twice if the first attempt landed and
  only the response was lost; that case is resolved against Advanced Billing's records instead.
- **Credentials never reach a log or a response.** The SDK's exception carries the `Authorization`
  header, so error translation reads only the status code and the validation messages.

## Layout

| Path | Contents |
| --- | --- |
| `src/ApplicationCore/Subscriptions/` | Domain models: plans, subscriptions, subscriber. |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability, with no Maxio in the signature. |
| `src/ApplicationCore/Exceptions/SubscriptionBillingException.cs` | Failure taxonomy the API maps to status codes. |
| `src/Infrastructure/Billing/Maxio/` | The Advanced Billing implementation. |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints and their DTOs. |
| `tests/UnitTests/Infrastructure/Billing/Maxio/` | Unit tests for settings, references, handlers and the gate. |
