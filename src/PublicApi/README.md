# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` exposes `GET /api/subscription-plans`, `POST /api/subscriptions` and
`GET /api/my-subscriptions` (any authenticated user, JWT from `api/authenticate`). `Billing/SubscriptionBillingService`
fronts the Maxio Advanced Billing .NET SDK, which ships inside the maxio-advanced-billing plugin and is referenced
by `PublicApi.csproj` through the `MaxioAdvancedBillingSdkProject` MSBuild property (override it with
`-p:MaxioAdvancedBillingSdkProject=<path to MaxioAdvancedBilling.csproj>`; building it needs the .NET 10 SDK).

Configuration (the host refuses to start when a required key is blank; never put values in a repo file):

| Key | Required | Source |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | user-secrets, `Maxio__ApiKey`, or `MAXIO_API_KEY` |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | user-secrets, `Maxio__Subdomain`, or `MAXIO_SITE_SUBDOMAIN` |
| `Maxio:ProductFamilyHandle` | yes | user-secrets, `Maxio__ProductFamilyHandle`, or `MAXIO_DEFAULT_PRODUCT_FAMILY` |
| `Maxio:BaseUrl` | no | optional override, used verbatim as the API base address |

Duplicate protection and unknown-outcome bookkeeping live in two `CatalogContext` tables
(`BillingCustomerLinks`, `SubscriptionEnrollments`; migration `AddSubscriptionBilling`).
