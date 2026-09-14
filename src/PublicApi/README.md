# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Recurring subscription billing (Maxio Advanced Billing)

The PublicApi host also exposes a recurring-subscription capability backed by
[Maxio Advanced Billing](https://docs.maxio.com) as the billing system of record. It is an
additive, parallel capability — the one-time Catalog → Basket → Order flow is untouched.

All three endpoints are JWT-authenticated; the caller's identity (their eShopOnWeb account,
i.e. their email) comes from the bearer token:

| Method | Route                    | Description                                                        |
|--------|--------------------------|--------------------------------------------------------------------|
| GET    | `/api/subscription-plans`   | Lists the subscribable plans in the configured Maxio product family |
| POST   | `/api/subscriptions`        | Subscribes the caller to a plan (`{"productHandle":"eshop-pro"}`). Idempotent: repeating it returns the existing subscription (`created:false`). First-time creation returns `201 Created` with `created:true`. |
| GET    | `/api/my-subscriptions`     | Lists the caller's subscriptions (plan, price, state, next billing date) as recorded by Maxio |

### Configuration

Settings are bound from the `Maxio:` configuration section. No secrets are stored in this
repository — load the key into .NET user-secrets:

| Setting                 | Environment variable            | Notes                                              |
|-------------------------|---------------------------------|----------------------------------------------------|
| `Maxio:ApiKey`          | `MAXIO_API_KEY`                 | Maxio Basic-auth API key                           |
| `Maxio:Subdomain`       | `MAXIO_SITE_SUBDOMAIN`          | Maxio site subdomain                               |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY`  | Product family that holds the subscription plans   |
| `Maxio:Environment`     | `MAXIO_ENVIRONMENT`             | `US`/`EU`; used only when `Maxio:BaseUrl` is unset |
| `Maxio:BaseUrl`         | (optional)                      | Verbatim API base URL override                     |

When `Maxio:BaseUrl` is set it is used verbatim; otherwise the base URL is derived from
`Maxio:Subdomain` (+ `Maxio:Environment`).

The implementation lives under `Subscriptions/` (settings, Maxio API client, orchestration
service and the local userId → Maxio customer mapping store) and `SubscriptionEndpoints/`
(the minimal-API endpoint classes).

### Notes on the mapping store

A Maxio customer is created on demand (keyed idempotently by the user's email as the Maxio
`reference`) and the userId ↔ Maxio customer link is cached locally in a dedicated
`SubscriptionMapping` database. Because the sample runs on the EF in-memory provider
(`UseOnlyInMemoryDatabase=true`) by default, that local cache only lives for a single run;
Maxio remains the source of truth for subscription state.
