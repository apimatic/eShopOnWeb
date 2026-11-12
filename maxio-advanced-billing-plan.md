# Maxio billing — plan and decisions

Maxio is the system of record: nothing about plans or subscriptions is stored in eShopOnWeb.
The code is in `src/PublicApi` (`Billing/`, `PlanEndpoints/`, `SubscriptionEndpoints/`), calling the
`MaxioAdvancedBilling` SDK from the maxio plugin (referenced by source; override the location with
`-p:MaxioSdkProject=<path to MaxioAdvancedBilling.csproj>` where the plugin is installed elsewhere).
The planner agent was not available in this session, so this plan was written by the main agent.

## Settings

Read from the `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US`/`EU`),
`Maxio:BaseUrl` (optional; when set it is used verbatim as the API base address).
Credentials come from the `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` and `MAXIO_ENVIRONMENT` environment
variables and are loaded into .NET user-secrets (`dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"`, etc.).
Nothing is written to a file in the repo. A missing setting makes billing requests answer 503 (the host still starts).

## Operations used (all throw-based; failures become 4xx/5xx with a reason via `MaxioErrors`)

| Feature | Route | SDK operations |
| --- | --- | --- |
| Plans | `GET /api/plans` | `Products.ListProducts` (paged, archived dropped) |
| Subscribe | `POST /api/subscriptions` | `Sites.ReadSite`, `Customers.ReadCustomerByReference`, `Subscriptions.CreateSubscription` |
| Page | `GET /api/subscriptions/{id}` | `Subscriptions.ReadSubscription` |
| Pause / resume | `POST .../pause`, `.../resume` | `SubscriptionStatus.PauseSubscription`, `ResumeSubscription` |
| Cancel / restart | `POST .../cancel`, `.../restart` | `SubscriptionStatus.CancelSubscription`, `ReactivateSubscription` |
| Switch | `POST .../switch` | `SubscriptionProducts.MigrateSubscriptionProduct` (+ reactivate / resume / hold around it) |
| Usage for a day | `GET .../usage?date=` | `SubscriptionComponents.ListSubscriptionComponents`, `ListUsages` |
| New plan | `POST /api/admin/plans` | `ProductFamilies.ListProductFamilies/CreateProductFamily`, `Products.CreateProduct` |
| Currency price | `POST /api/admin/plans/{planId}/currency-prices` | `Products.ReadProduct`, `ProductPricePoints.CreateProductCurrencyPrices` |
| Next billing date | `PUT /api/admin/subscriptions/{id}/next-billing-date` | `Subscriptions.ReadSubscription`, `UpdateSubscription` |

Shopper routes need a JWT; staff routes need the `Administrators` role. The caller's identity is the JWT name claim.

## Decisions

- **Customer identity.** The shopper is a Maxio customer whose `reference` is the JWT user name; created on the first
  subscription. A subscription is the caller's only if its customer reference equals the JWT name; otherwise 404.
- **No card details.** Collection method is `remittance` on a Relationship Invoicing site (the sandbox is one), `invoice` otherwise.
- **Cross-operation invariant.** A plan handle accepted by subscribe/switch must be one `GET /api/plans` returns
  (checked by `FindOfferedPlanAsync` before the write). A plan's brand must be a catalog brand (`FindCatalogBrandAsync`).
- **Brand = Maxio product family** named after the catalog brand (created on first use).
- **`paid`** is `balance_in_cents <= 0` on the subscription as Maxio reports it (open invoices make it `false`).
- **Switch** works at any time: Maxio only migrates active subscriptions, so a cancelled subscription is reactivated
  (resume), a paused one resumed, then migrated; a paused one is held again afterwards; if the migration fails the earlier
  state is restored best-effort. Switching to the current plan is 409.
- **Usage for a day.** Only metered and prepaid-usage components. Maxio's `since_date`/`until_date` are midnight-based,
  so a wider window is requested and entries are filtered by the date of `created_at` in the site's own offset.
- **Next billing date.** Must be a future `YYYY-MM-DD`; sent as noon in the site's offset; cancelled subscriptions are refused (409).
  The new date is read back from `current_period_ends_at` (Maxio's update response has no `next_billing_at`).
- **Currency prices** are whole currency units (the SDK's `Price` is an `int`) and need the currency defined on the site;
  the sandbox site defines none, so Maxio answers 422 and that reason is returned.
- **Retries / timeouts.** Only GET/HEAD are resent (2 retries, 20 s per attempt); the HTTP client times out at 60 s.
  No write is replayed. There is no local store, so no duplicate-claim table: a double-submitted subscribe creates two subscriptions.
- **Errors.** `BillingException` (status + reason) is rendered by `ExceptionMiddleware`; Maxio 401/403 → 502, 4xx → same status, 5xx/timeouts → 502/504.

## Testing

`tests/PublicApiIntegrationTests/BillingEndpoints/BillingEndpointsTest.cs` runs against the real Maxio sandbox using
user-secrets (inconclusive if unconfigured); it covers authorization, the subscription lifecycle including switching a
cancelled subscription, usage, ownership, the billing date and plan creation.
