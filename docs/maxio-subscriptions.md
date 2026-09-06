# Recurring subscriptions with Maxio Advanced Billing

eShopOnWeb is one-time commerce: Catalog → Basket → Order. This capability adds recurring
subscription billing **alongside** that flow, with **Maxio Advanced Billing** as the system of
record. Nothing in the cart or checkout path changes.

## The hero flow

A logged-in shopper lists the plans, subscribes to one, and sees it on their account:

```
GET  /api/subscription-plans   ->  the plans in the configured Maxio product family
POST /api/subscriptions        ->  ensure a Maxio customer, enroll them, confirm the enrollment
GET  /api/my-subscriptions     ->  the caller's own subscriptions, read back from Maxio
```

All three live on **`src/PublicApi`** and are JWT-authenticated. The shopper is always taken from
the bearer token — never from a request body — so a caller can only ever act on their own account.

## Layout

| Layer | What lives there |
|---|---|
| `src/ApplicationCore/Subscriptions` | `SubscriptionPlan`, `CustomerSubscription`, `BillingCustomer`, `SubscriberIdentity`, `SubscriptionEnrollment`, `SubscriptionStates` — vendor-neutral models |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability the API depends on |
| `src/ApplicationCore/Exceptions` | `BillingConfigurationException`, `BillingApiException`, `SubscriptionPlanNotFoundException` |
| `src/Infrastructure/Billing/Maxio` | The Maxio implementation: options, wire contracts, HTTP client, resilience, enrollment locking |
| `src/PublicApi/SubscriptionEndpoints` | The three endpoints, their DTOs, and the token-to-shopper accessor |

`ApplicationCore` never mentions Maxio. Swapping billing systems means one new implementation of
`ISubscriptionBillingService`.

## Maxio is the system of record

There is **no local table** mapping shoppers to Maxio customers or subscriptions. Instead every
shopper gets a deterministic customer reference:

```
reference = "{Maxio:ReferencePrefix}-{lowercased user name}"     e.g. eshoponweb-demouser@microsoft.com
```

and every enrollment a deterministic subscription reference:

```
reference = "{customer reference}:{plan handle}"                 e.g. eshoponweb-demouser@microsoft.com:eshop-pro
```

Reads go `reference → customer → subscriptions`, so the integration is correct even on a cold
start. That matters here: eShopOnWeb can run on the EF in-memory provider, which loses everything
on restart and regenerates identity GUIDs — hence the reference keys off the stable **user name**
rather than `ApplicationUser.Id`.

## Idempotency — why a double-click cannot double-charge

Four layers, in order:

1. **Per-shopper lock** (`SubscriberKeyedLock`) serialises enrollment inside the process, so a
   second click cannot run its "already subscribed?" check while the first is still creating.
2. **Read before write.** The shopper's Maxio subscriptions are listed and checked for a *live*
   one on the requested plan. If there is one, it is returned with `alreadySubscribed: true` and
   HTTP 200 — nothing is created. A brand-new enrollment is HTTP 201.
3. **Deterministic customer reference.** Creating a customer is `lookup → create`, and a 409/422
   from the create is resolved by re-reading, so a race produces one customer, not two.
4. **Maxio's `uniqueness_token`** on every write, derived deterministically from the reference.
   A replayed create inside Maxio's 60-minute window comes back 409 instead of creating a second
   record — this is what makes retrying a write safe at all.

A 409 is then resolved rather than propagated: the integration re-reads, and

- if a subscription with that reference exists, it is returned (`alreadySubscribed: true`);
- if nothing exists, the earlier attempt failed without creating anything, so the create is
  retried once under a fresh token. Otherwise one failed signup would lock the shopper out for an
  hour.

"Live" means `pending`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `paused` or
`awaiting_signup`. A `canceled` or `expired` subscription does **not** block re-subscribing: the
new enrollment takes the next free reference (`…:eshop-pro:2`).

## Payment collection

eShopOnWeb captures no card details. A plan whose Maxio product has `require_credit_card = false`
is therefore created with an invoice-style collection method — `remittance` on Relationship
Invoicing sites, `invoice` on legacy Statements sites — read from `GET /site.json`. Left on the
usual site default of `automatic`, Maxio would try to charge at signup and reject the whole
request with *"No payment method was on file"*. Set `Maxio:PaymentCollectionMethod` to override.

Plans that *do* require a payment method are sent as `automatic`; without card capture (which this
integration deliberately does not implement) Maxio will decline them, and the reason is passed
straight back to the caller.

## Talking to Maxio

- Base address: `https://{Maxio:Subdomain}.chargify.com/`, or `Maxio:BaseUrl` verbatim when set.
- Auth: HTTP Basic, API key as the username and the literal `X` as the password.
- Endpoints used: `GET /site.json`, `GET /product_families/handle:{handle}/products.json`,
  `GET /customers/lookup.json?reference=…`, `POST /customers.json`,
  `GET /customers/{id}/subscriptions.json`, `POST /subscriptions.json`.
- Product families and products are addressed **by handle**. Numeric ids are reassigned when the
  catalog is re-seeded; handles are not.

`MaxioResilienceHandler` caps in-flight calls (Maxio throttles by concurrency, not request rate)
and retries throttled and transient failures with exponential backoff, full jitter and
`Retry-After` support. Reads are always replayable; writes only when they carry a uniqueness token.

Failures map to honest status codes: missing configuration → **503**, Maxio validation/not-found/
conflict → the upstream 4xx, Maxio outage or throttling → **502**.

## Configuration

Bound from the `Maxio:` section. No value is hard-coded — the same build runs against a different
site and a different catalog.

| Key | From | Required | Meaning |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | Maxio API key |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | unless `BaseUrl` is set | Maxio site subdomain |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | Product family offered as plans |
| `Maxio:BaseUrl` | — | no | Verbatim API base address, overriding the subdomain |

Operational knobs, all optional: `TimeoutSeconds` (30), `MaxRetryAttempts` (3),
`RetryBaseDelayMilliseconds` (250), `MaxConcurrentRequests` (4), `PlanCacheSeconds` (60),
`SiteCacheSeconds` (300), `ReferencePrefix` (`eshoponweb`), `PaymentCollectionMethod` (auto).

**Secrets never go in the repository.** Load them into user-secrets from the environment:

```pwsh
pwsh ./scripts/set-maxio-user-secrets.ps1
```

or by hand:

```pwsh
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey" $env:MAXIO_API_KEY
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY
```

In environments without user-secrets, the double-underscore form works too:
`Maxio__ApiKey`, `Maxio__Subdomain`, `Maxio__ProductFamilyHandle`, `Maxio__BaseUrl`.

If the integration is not configured, the rest of the API still starts and serves normally; only
the three subscription endpoints report 503 with an actionable message.

## Running and verifying it

See [Verifying the Maxio subscription integration](./verify-maxio-subscriptions.md).
