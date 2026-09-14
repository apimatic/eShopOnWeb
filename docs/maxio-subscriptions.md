# Subscription billing with Maxio Advanced Billing

eShopOnWeb ships with one-time commerce: catalog → basket → order. This adds a **parallel**
capability — recurring subscriptions — with **Maxio Advanced Billing as the system of record**. The
existing cart and checkout flow is untouched.

## The hero flow

A signed-in shopper browses the available plans, subscribes to one, and sees it on their account:

| Method | Route                    | Purpose                                                      |
| ------ | ------------------------ | ------------------------------------------------------------ |
| `GET`  | `/api/subscription-plans`| The plans on offer, cheapest first. Subscribe by `handle`.     |
| `POST` | `/api/subscriptions`     | Enrol the caller in a plan. Idempotent per shopper and plan.   |
| `GET`  | `/api/my-subscriptions`  | Everything the caller is subscribed to, newest first.          |

All three live on `src/PublicApi` and are JWT authenticated. **The shopper is always taken from the
token**, never from the request — there is no way to subscribe somebody else.

## Design

### Maxio is the system of record

eShopOnWeb keeps no local copy of who is subscribed to what. Every read goes to Maxio, so a plan
change, a cancellation or a dunning failure applied inside Maxio shows up immediately, and there is no
mirror to drift.

A shopper is linked to their Maxio customer by a **deterministic reference** derived from their
eShopOnWeb identity:

```
customer reference      eshoponweb-<normalised user name>
subscription reference  eshoponweb-<normalised user name>-<plan handle>[-<n>]
```

Because the reference is a pure function of the identity, the link survives an application restart
with nothing persisted locally — which matters here, since eShopOnWeb can run on the in-memory
database provider where the Identity primary keys are regenerated on every start.

### Idempotency

A double-clicked subscribe button must not produce two customers or two subscriptions. Three layers,
weakest to strongest:

1. **An in-process lock** per shopper and plan, so two simultaneous requests on one instance are
   serialised rather than racing.
2. **A read of the shopper's current subscriptions** before writing. This is authoritative and works
   across instances: if a live subscription to the plan already exists, it is returned with
   `200 OK` and `alreadySubscribed: true` instead of a second one being created.
3. **A guard on the write itself**, so a replayed request is rejected rather than performed twice.
   The guard differs by resource, because Maxio does:
   - **Customers** — the customer `reference` is enforced as unique for the life of the site. A
     duplicate create always fails with `422`, permanently and precisely, and the loser re-reads and
     converges on the winner. No `uniqueness_token` is used, deliberately: it would add nothing here
     and would block a legitimate retry until its window expired.
   - **Subscriptions** — Maxio does *not* constrain subscription references, so a replayed write
     really would create a second subscription. These carry a `uniqueness_token`, Maxio's own
     duplicate-prevention mechanism: a duplicate delivery comes back as `409 Conflict` and the service
     re-reads the shopper's state and returns the winner.

Layer 3 also makes it safe for the HTTP retry policy to replay a `POST`.

The subscription token is scoped to a short window (`Maxio:IdempotencyWindowSeconds`, default 120s)
rather than being fixed forever. Maxio consumes the token even for a *rejected* attempt, so a shopper
who is told "no payment method was on file", fixes it, and tries again would otherwise be locked out
for the full 60 minutes of Maxio's window. Two minutes is far longer than the milliseconds separating a
double-click or a replayed request, which is all the token has to cover — anything slower is caught by
the lock and the pre-flight read.

The honest limit of this: two requests that arrive milliseconds apart, *on different instances*, and
happen to straddle a window boundary, get different tokens and fall back to layers 1–2 alone — and
layer 1 is per-instance. On a single instance (which is how eShopOnWeb runs) that case cannot occur.
A caller that needs the guarantee unconditionally should send an explicit `Idempotency-Key`.

Callers that want to guard a retry explicitly may send an `Idempotency-Key` header, which replaces the
derived token entirely (window included).

`past_due`, `trialing` and the other problem states count as *live*: the shopper is still enrolled and
the fix is to settle payment, not to open a second subscription. `canceled` and `expired` do not, so a
shopper who cancels can sign up again — and gets a fresh reference and token, rather than colliding
with the previous enrolment.

### Resilience

Maxio limits a site to a small number of concurrent calls and answers `429` beyond that, so the
`MaxioRetryHandler` backs off (exponential, full jitter, honouring `Retry-After`) rather than fanning
out. Timeouts are applied per attempt, so backoff delays do not eat the budget. The plan catalog is
cached briefly to keep browsing off the wire.

### Error mapping

| Situation                                             | Status | Body                                    |
| ----------------------------------------------------- | ------ | --------------------------------------- |
| Billing not configured for this deployment            | `503`  | names the missing configuration keys    |
| Unknown plan handle                                   | `404`  | the handle that was not found           |
| Request cannot be fulfilled (no payment method, ...)  | `400`  | Maxio's own error messages in `Errors`  |
| A competing request is still settling                 | `409`  | retry shortly                           |
| Maxio unreachable or failing                          | `502`  | —                                       |

## Configuration

Bound from the `Maxio` section. Nothing about a particular site or catalog is compiled in.

| Key                             | Required | Notes                                                                         |
| ------------------------------- | -------- | ----------------------------------------------------------------------------- |
| `Maxio:ApiKey`                  | yes      | Secret. Basic-auth username; the password is the literal `X`.                  |
| `Maxio:Subdomain`               | yes*     | Site subdomain. \*Not needed if `Maxio:BaseUrl` is set.                        |
| `Maxio:ProductFamilyHandle`     | yes      | Product family whose products are offered as plans.                            |
| `Maxio:BaseUrl`                 | no       | Absolute API base address. When set it is used **verbatim**.                   |
| `Maxio:Environment`             | no       | `US` (default) or `EU`. Selects the host when `BaseUrl` is not set.            |
| `Maxio:DefaultPlanHandle`       | no       | Plan used when a subscribe request names none. Unset ⇒ callers must name one.  |
| `Maxio:PaymentCollectionMethod` | no       | `remittance` (default), `automatic` or `prepaid`. See below.                   |
| `Maxio:PlanCacheSeconds`        | no       | Plan catalog cache TTL. Default 60; `0` disables.                              |
| `Maxio:IdempotencyWindowSeconds`| no       | How long two subscribe attempts count as one request. Default 120.             |
| `Maxio:RequestTimeoutSeconds`   | no       | Per-attempt timeout. Default 30.                                               |
| `Maxio:MaxRetryAttempts`        | no       | Retries after a throttled or transient failure. Default 3.                     |
| `Maxio:ReferencePrefix`         | no       | Prefix on generated references. Default `eshoponweb`.                          |

### Why `remittance` is the default

On a Relationship Invoicing site, `automatic` collection charges a stored payment method at signup. A
shopper who has not gone through card capture therefore fails with
`No payment method was on file for the $299.00 balance` — even on a plan whose
`require_credit_card` is false. `remittance` issues an invoice instead, which is what lets the
subscribe flow complete without card capture or 3-DS. Set `Maxio:PaymentCollectionMethod` to
`automatic` on a deployment that does capture cards.

### Supplying the credentials

**Never commit the API key.** Three routes, highest precedence last:

1. `dotnet user-secrets` on `src/PublicApi` (local development):

   ```bash
   cd src/PublicApi
   dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
   dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
   dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
   ```

2. The `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`, `MAXIO_BASE_URL` and
   `MAXIO_ENVIRONMENT` environment variables, which `AddMaxioEnvironmentVariables()` projects onto
   the `Maxio:*` keys.

3. Standard `Maxio__ApiKey`-style environment variables or any other configuration provider.

A deployment with no Maxio configuration still starts. It logs a warning naming the missing keys at
startup, and the three endpoints answer `503` with the same list.

## Where the code lives

| Path                                   | Contents                                                        |
| -------------------------------------- | --------------------------------------------------------------- |
| `src/ApplicationCore/Subscriptions/`    | Provider-neutral models: plan, subscription, subscriber, states. |
| `src/ApplicationCore/Interfaces/`       | `ISubscriptionPlanService`, `ISubscriptionService`, `ISubscriberResolver`. |
| `src/ApplicationCore/Exceptions/`       | `BillingException` and its subtypes.                             |
| `src/Infrastructure/Maxio/`             | Settings, typed HTTP client, retry handler, wire contracts, the service. |
| `src/Infrastructure/Identity/`          | `IdentitySubscriberResolver`, token identity → subscriber.       |
| `src/PublicApi/SubscriptionEndpoints/`  | The three endpoints and their DTOs.                              |

Swapping providers means writing one `ISubscriptionPlanService` / `ISubscriptionService`
implementation; nothing above the infrastructure layer knows Maxio exists.
