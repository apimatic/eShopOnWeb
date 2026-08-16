# Subscription Billing (Maxio Advanced Billing)

An **additive, parallel** capability on top of eShopOnWeb's one-time commerce flow: a logged-in
shopper can browse recurring subscription plans, subscribe to one, and see it reflected in their
account. **Maxio Advanced Billing** (Chargify) is the billing system of record — eShopOnWeb stores
no subscription state of its own.

## Endpoints (PublicApi, JWT-authenticated)

| Method & route | Purpose |
|----------------|---------|
| `GET  /api/subscription-plans` | Lists the plans available for enrollment (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. |
| `GET  /api/my-subscriptions` | Lists the caller's subscriptions. |

The caller's identity always comes from the JWT (never from the request body). A stable Maxio
customer *reference* is derived from the user name (`eshop-user-{userName}`), which is the idempotency
anchor: a given user always maps to the same Maxio customer, even across restarts.

### Idempotency

`POST /api/subscriptions` is idempotent, so a double-click never creates two customers or two
subscriptions:

- **Customer** — looked up by reference and created only if missing; a concurrent create that loses
  Maxio's uniqueness race (HTTP 422) is recovered by re-reading the winner.
- **Subscription** — an existing *live* subscription to the same plan is reused instead of creating a
  duplicate. The check-then-create sequence is serialized per subscriber (in-process keyed lock), so
  concurrent requests can't each create one.

### No payment method at signup

The demo plans require no card. Subscriptions are created with Maxio's `remittance` payment
collection method, so Maxio issues an invoice for the balance instead of attempting an immediate card
charge — no card capture or 3-DS is involved.

## Configuration

Settings are bound from the `Maxio` configuration section. **Secret values live only in .NET
user-secrets / environment — never in the repository.**

| Config key | Sourced from env var | Notes |
|------------|----------------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | API key (HTTP Basic username; password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site subdomain; API base is derived as `https://{Subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | *(optional)* | If set, used verbatim as the API base address instead of being derived from the subdomain. |

Load the secrets (values come from your environment; they are not printed or stored in-repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Settings are validated at startup (`ValidateOnStart`); a misconfigured deployment fails fast.

## Design / layering

- **ApplicationCore** — `ISubscriptionBillingService` abstraction, provider-neutral models
  (`SubscriptionPlan`, `CustomerSubscription`, `SubscriberInfo`), and typed exceptions.
- **Infrastructure/Maxio** — `MaxioSubscriptionBillingService` (typed `HttpClient` + `System.Text.Json`),
  `MaxioSettings`, and internal wire DTOs. No third-party SDK; the REST contract was verified against
  the live Maxio API.
- **PublicApi/SubscriptionEndpoints** — the three endpoints (following the project's `IEndpoint`
  convention) and their request/response DTOs; wired up via `AddMaxioBilling` in `Program.cs`.

## Running & verifying

See the verification steps in the project README / hand-off notes. In short: run PublicApi with
`UseOnlyInMemoryDatabase=true`, get a bearer token from `POST /api/authenticate`
(`demouser@microsoft.com` / `Pass@word1`), then call the three endpoints. Swagger UI is available at
`/swagger`.
