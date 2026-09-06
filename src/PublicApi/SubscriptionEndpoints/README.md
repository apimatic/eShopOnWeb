# Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing for eShopOnWeb, with **Maxio Advanced Billing** as the billing
system of record. This is an additive capability that runs alongside the existing one-time
Catalog → Basket → Order flow; nothing in that flow changes.

## Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The subscriber is taken from
the token's name claim and never from the request body, so a caller can only act on themselves.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/subscription-plans` | Plans on offer, from the configured Maxio product family. |
| `POST /api/subscriptions` | Enrol the caller on a plan. `201` when enrolled, `200` when an equivalent subscription already existed. |
| `GET /api/my-subscriptions` | The caller's subscriptions, live ones first. |

`POST /api/subscriptions` body:

```json
{ "planHandle": "eshop-pro", "idempotencyKey": "optional-caller-supplied-key" }
```

### Status codes

| Status | Meaning |
|--------|---------|
| `200` | Already subscribed - the response carries the pre-existing subscription and `alreadySubscribed: true`. |
| `201` | Enrolled. |
| `400` | `planHandle` missing. |
| `401` | No or invalid bearer token. |
| `404` | `planHandle` is not a plan in the configured product family. |
| `422` | Maxio rejected the request; the body carries Maxio's own messages under `Errors`. |
| `502` | Maxio was unreachable or answered unexpectedly. Safe to retry. |

## Configuration

Bound from the `Maxio` configuration section. **Never commit values for `ApiKey`** - supply it
through user-secrets, environment variables or a secret store.

| Key | Required | Default | Notes |
|-----|----------|---------|-------|
| `Maxio:ApiKey` | yes | - | Sent as the basic-auth user name with password `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | yes¹ | - | Fills the `site` variable of the spec's server template. |
| `Maxio:ProductFamilyHandle` | yes | - | Handle of the family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | - | Verbatim override of the API base address. When set, `Subdomain` and `Environment` are not used. |
| `Maxio:Environment` | no | `US` | `US` → `https://{site}.chargify.com`, `EU` → `https://{site}.ebilling.maxio.com` (from the spec's `x-server-configuration`). |
| `Maxio:PaymentCollectionMethod` | no | `remittance` | Maxio `Collection-Method`. See "Payment methods" below. |
| `Maxio:Timeout` | no | `00:00:30` | Per-call budget, retries included. |
| `Maxio:MaxRetryAttempts` | no | `3` | Retries after the first attempt. |
| `Maxio:RetryBaseDelay` | no | `00:00:00.25` | Base for the exponential back-off. |
| `Maxio:PlanCacheDuration` | no | `00:01:00` | In-process plan cache. `00:00:00` disables it. |
| `Maxio:CustomerCacheDuration` | no | `00:05:00` | In-process billing-customer cache, used when reading subscriptions back. Enrolment always resolves the customer fresh. `00:00:00` disables it. |
| `Maxio:CustomerReferencePrefix` | no | `eshoponweb` | Prefix for the Maxio customer `reference` this app writes. |

¹ Not required when `Maxio:BaseUrl` is set.

Configuration is validated at start-up: a missing key fails the host rather than the first
shopper request, and only key *names* appear in the failure message.

Load the sandbox credentials into user-secrets (values come from the environment, so nothing
lands in the repository):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"          --project src/PublicApi
```

## How it maps onto Maxio

The Maxio OpenAPI specification in `maxio-spec/` is the contract. Every call this integration
makes is one of these operations:

| Operation (spec `operationId`) | Path | Used for |
|--------------------------------|------|----------|
| `listProductsForProductFamily` | `GET /product_families/{product_family_id}/products.json` | The plan catalogue. The family is addressed as `handle:{ProductFamilyHandle}`. |
| `readCustomerByReference` | `GET /customers/lookup.json` | Finding the shopper's billing customer. |
| `createCustomer` | `POST /customers.json` | Creating it on first use. |
| `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` | Reading subscriptions back. |
| `createSubscription` | `POST /subscriptions.json` | Enrolment. |
| `findSubscription` | `GET /subscriptions/lookup.json` | Resolving an idempotency key to its subscription. |

**Handles, not ids.** Maxio reassigns numeric ids when a catalogue is re-seeded, so plans are
always addressed by `handle` and the product family by `handle:{...}`. Numeric ids appear in
responses for information only and are never configured or persisted.

**Maxio is the system of record.** Plans, customers and subscriptions are read back from Maxio
rather than mirrored in the eShopOnWeb database. Answers therefore stay correct across an
eShopOnWeb restart - which matters here, because the sample can run on the in-memory EF provider -
and reflect changes made directly in the Maxio UI.

## Identity mapping

An eShopOnWeb account maps to a Maxio customer through a deterministic `reference`:

```
{CustomerReferencePrefix}:{lowercased eShopOnWeb user name}   e.g. eshoponweb:demouser@microsoft.com
```

Maxio enforces that references are unique, which is what makes customer creation safe to repeat.
The user name is used rather than the ASP.NET Identity id because the id is regenerated whenever
the sample runs on the in-memory database, whereas the user name is stable.

Maxio requires a first and last name on a customer but eShopOnWeb accounts carry neither, so both
are derived from the account's e-mail address (`jane.doe@example.com` → "Jane Doe";
`demouser@microsoft.com` → "Demouser Microsoft").

## Repeat-safety

A double-clicked "Subscribe" must never produce two customers or two subscriptions. Four things
together guarantee that:

1. **Lookup before create.** The customer is resolved by reference first, and only created if
   absent.
2. **A unique reference resolves the race.** If a create is rejected because the reference is
   already taken, re-reading it returns the record the winner created.
3. **An in-process gate.** Enrolment for one subscriber is serialised, so its "is there one
   already?" check and its create cannot interleave with another request's.
4. **No second live subscription to the same plan.** If the shopper already holds a live
   subscription to the requested plan, that subscription is returned with `alreadySubscribed:
   true` and nothing is created. Terminated subscriptions (`canceled`, `expired`,
   `failed_to_create`, `trial_ended`) do not block a fresh enrolment.

Supplying `idempotencyKey` adds a fifth, stronger guarantee that also holds across processes: the
key is hashed into the Maxio subscription `reference`, which Maxio enforces as unique, so a repeat
resolves to exactly the subscription the first request created.

## Payment methods

This integration does not capture card details, so it never attaches a payment profile. Maxio's
`automatic` collection would then fail at signup with *"No payment method was on file"*, so
enrolment uses an invoice-based `Collection-Method` (`remittance` by default). Set
`Maxio:PaymentCollectionMethod` to `automatic` only on a deployment that provisions payment
profiles some other way.

## Layout

| Path | Contents |
|------|----------|
| `src/ApplicationCore/Subscriptions/` | Provider-agnostic domain models. |
| `src/ApplicationCore/Interfaces/ISubscriptionService.cs` | The capability's contract. |
| `src/ApplicationCore/Exceptions/Billing*.cs` | Billing failure contract. |
| `src/Infrastructure/Maxio/` | Maxio options, typed API client, HTTP handlers and the service implementation. |
| `src/Infrastructure/Maxio/Models/` | Spec-shaped request/response types. |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints and their DTOs. |
