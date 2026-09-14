# Recurring subscription billing (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This capability sits
**alongside** it and shares nothing with it. A logged-in shopper can browse recurring plans, subscribe to
one, and see the result in their account.

**Maxio Advanced Billing is the system of record.** eShopOnWeb stores no billing state: no plan table, no
subscription table, no user→customer mapping. Every read goes to Maxio, and the link between an
eShopOnWeb user and their Maxio customer is a deterministic `reference` written onto the Maxio customer.
That is what makes the integration correct across restarts, across instances, and against the in-memory
database this repo defaults to on a machine without LocalDB.

---

## Endpoints

All three live on `src/PublicApi` and require a JWT bearer token from `POST /api/authenticate`. The
shopper's identity always comes from the token, never from the request body.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/subscription-plans` | The plans on offer, read live from the configured Maxio product family. |
| `POST` | `/api/subscriptions` | Ensure a Maxio customer exists for the caller and enroll them on a plan. |
| `GET` | `/api/my-subscriptions` | The caller's subscriptions, newest first. |

### `POST /api/subscriptions`

```jsonc
{
  "planHandle": "eshop-pro",     // optional: falls back to Maxio:DefaultPlanHandle, then to the only plan
  "idempotencyKey": "order-42"   // optional: may also be sent as an Idempotency-Key header, which wins
}
```

- `201 Created` — the subscription was created by this request.
- `200 OK` — the caller was already enrolled; `alreadySubscribed` is `true` and nothing was created.
- `404 Not Found` — no such plan in the configured product family.
- `422 Unprocessable Entity` — Maxio rejected the request; its own messages are returned in `errors`.
- `503 Service Unavailable` — billing is not configured, or Maxio is unreachable.

Response:

```jsonc
{
  "subscription": {
    "id": "94209904",
    "state": "active",                  // Maxio's own vocabulary, passed through verbatim
    "isActive": true,
    "reference": "eshoponweb:subscription:demouser@microsoft.com:eshop-pro",
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299.00,
    "priceInCents": 29900,
    "currency": "USD",
    "formattedPrice": "299.00 USD / month",
    "currentPeriodEndsAt": "2026-10-06T15:48:23+05:00",
    "nextBillingAt": "2026-10-06T15:48:23+05:00",
    "paymentCollectionMethod": "remittance",
    "customerId": "98838455"
  },
  "alreadySubscribed": false,
  "customerCreated": true
}
```

---

## How "a double-click never creates two customers or two subscriptions" is guaranteed

Maxio enforces uniqueness on the `reference` field of both customers and subscriptions, rejecting a
duplicate with HTTP 422 (`Reference: must be unique - that value has been taken.`). The integration
builds those references deterministically and treats that rejection as its idempotency signal, so the
guarantee is enforced by the billing system of record rather than by local bookkeeping:

| Reference | Value |
|---|---|
| Customer | `eshoponweb:customer:{userName}` |
| Subscription | `eshoponweb:subscription:{userName}:{idempotencyKey \| planHandle}` |

Three layers, outermost first:

1. **Per-shopper lock (latency).** Concurrent subscribe attempts for one shopper are serialised inside
   the process, so the everyday double-click does not even reach Maxio twice.
2. **One live subscription per plan (semantics).** Before creating anything, the caller's Maxio
   subscriptions are read; if a live one already exists on that plan it is returned with `200` and
   `alreadySubscribed: true`. "Live" is every state Maxio does not document as end-of-life, so a
   `past_due` shopper is not sold a second subscription.
3. **Unique reference (correctness).** The create still carries a deterministic reference. If two
   instances race past layer 2, Maxio rejects the loser with a 422 and the loser reads the winner's
   subscription back. This is the layer that holds when the in-process lock cannot see the other caller.

An explicit `Idempotency-Key` narrows the reference further, so replaying a request always returns
exactly what the first call produced. When a shopper re-subscribes after cancelling, the natural
reference is already taken by the ended subscription, so the integration moves to the next reference in
the series (`…:eshop-pro#2`) rather than refusing the signup.

The prefix (`Maxio:ReferencePrefix`, default `eshoponweb`) namespaces the references, so one Maxio site
can host several applications.

---

## Configuration

Bound from the `Maxio` section. **No value is ever committed** — locally they live in .NET user-secrets,
in a container they arrive as the flat `MAXIO_*` variables Maxio hands out, which are projected onto the
section by `AddMaxioEnvironmentVariables()`. Environment variables win over user-secrets.

| Key | Environment variable | Required | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | Sent as the HTTP Basic user name; the password is the literal `x`. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | yes | The `acme` in `acme.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | Product family whose products become the plans. |
| `Maxio:BaseUrl` | `MAXIO_BASE_URL` | no | Used **verbatim** as the API base address instead of the one derived from the subdomain. |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` | no | `US` (default) or `EU`. |
| `Maxio:DefaultPlanHandle` | — | no | Plan used when a request does not name one. |
| `Maxio:PaymentCollectionMethod` | — | no | Overrides the collection method; see below. |
| `Maxio:ReferencePrefix` | — | no | Default `eshoponweb`. |
| `Maxio:CatalogCacheDuration` | — | no | Default `00:05:00`. |
| `Maxio:Timeout` / `Maxio:RetryCount` | — | no | Default 30s / 3 retries, reads only. |

Handles are configured, numeric ids never are: Maxio reassigns ids when a site is re-seeded, so the
product family handle is resolved to an id at runtime and cached briefly.

Load the sandbox credentials into user-secrets (values come from the environment; nothing is echoed into
a file in this repository):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"
dotnet user-secrets set "Maxio:DefaultPlanHandle"   "eshop-pro"     # optional
```

### Subscribing without capturing a card

The demo plans are configured with *payment method not required*, but Maxio's default collection method
(`automatic`) still tries to charge at signup and fails with *"No payment method was on file"*. The
integration therefore creates subscriptions with the invoiced collection method, chosen from the site
itself: `remittance` on a Relationship Invoicing site, `invoice` on a legacy Statements site. Neither
needs a stored payment method, so signup never detours through card capture or 3-D Secure.
`Maxio:PaymentCollectionMethod` overrides this if a deployment wants `automatic` or `prepaid`.

### When billing is not configured

Registration never fails: the host starts, the rest of the API is unaffected, and only the three
subscription routes answer `503` with a message naming the missing keys. That keeps a deployment that
does not use billing — and the hermetic test suite — working.

---

## Where the code lives

| Layer | Path | Contents |
|---|---|---|
| Domain | `src/ApplicationCore/Subscriptions/` | `SubscriptionPlan`, `CustomerSubscription`, `Subscriber`, `SubscribeRequest/Result`, `SubscriptionStates` |
| Port | `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The provider-agnostic contract |
| Errors | `src/ApplicationCore/Exceptions/SubscriptionBillingException.cs` | Not-configured / not-found / rejected / unavailable |
| Adapter | `src/Infrastructure/Subscriptions/Maxio/` | Maxio SDK client, catalog cache, mapping, error translation, references, locks |
| API | `src/PublicApi/SubscriptionEndpoints/` | The three endpoints, DTOs and the token→subscriber resolution |

The adapter uses the official [`Maxio.AdvancedBillingSdk`](https://www.nuget.org/packages/Maxio.AdvancedBillingSdk)
(v10, generated from Maxio's own API specification), driven through an `IHttpClientFactory` pipeline so
`Maxio:BaseUrl` can be applied by a delegating handler and so retries stay limited to safe reads.

### Maxio operations used

| Purpose | Maxio operation |
|---|---|
| Site currency and billing architecture | `GET /site.json` |
| Resolve the product family handle | `GET /product_families.json` |
| List plans | `GET /product_families/{id}/products.json` |
| Find the shopper's customer | `GET /customers/lookup.json?reference=…` |
| Create the customer | `POST /customers.json` |
| List the shopper's subscriptions | `GET /customers/{id}/subscriptions.json` |
| Subscribe | `POST /subscriptions.json` |
| Resolve a reference conflict | `GET /subscriptions/lookup.json?reference=…` |

---

## Verifying it

See the "Verify the subscription integration" section of [README.md](README.md).
