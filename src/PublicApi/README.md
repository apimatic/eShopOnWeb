# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription Billing (Maxio Advanced Billing)

Recurring-subscription billing is exposed as an additive capability on this API
(the one-time cart/checkout flow is untouched). Maxio Advanced Billing is the
billing system of record.

### Endpoints (JWT Bearer auth required)

| Method | Route                     | Purpose |
|--------|---------------------------|---------|
| GET    | `api/subscription-plans`  | Plans available for subscription (from the configured product family) |
| POST   | `api/subscriptions`       | Subscribe the authenticated user to a plan: body `{ "planHandle": "eshop-pro", "idempotencyKey": "optional-guid" }` |
| GET    | `api/my-subscriptions`    | The authenticated user's subscriptions (plan, price, state, next billing date) |

### Design notes

- The eShopOnWeb user ID is stored as the Maxio customer **reference**, so the
  user-to-billing-customer mapping lives in the billing system of record and
  survives restarts without local persistence.
- Enrollment is idempotent: the billing customer is looked up before creation,
  an existing live subscription on the plan is returned instead of creating a
  duplicate, and every create carries a `uniqueness_token` (the optional
  `idempotencyKey` request field) so retried requests are rejected as duplicates.
- Signups are created with manual collection (`remittance`, falling back to
  `invoice` on statement-architecture sites) so no payment capture is needed at
  signup; override with the `Maxio:PaymentCollectionMethod` setting if desired.

### Configuration

All settings are bound from the `Maxio:` configuration section. Provide values
via **user secrets** (`dotnet user-secrets set ... --project src/PublicApi`) or
environment variables - never in appsettings files or source:

- `Maxio:ApiKey` (from `MAXIO_API_KEY`)
- `Maxio:Subdomain` (from `MAXIO_SITE_SUBDOMAIN`)
- `Maxio:ProductFamilyHandle` (from `MAXIO_DEFAULT_PRODUCT_FAMILY`)
- `Maxio:BaseUrl` (optional; when set, used verbatim instead of deriving
  `https://{subdomain}.chargify.com`)
- `Maxio:PaymentCollectionMethod` (optional; see above)
