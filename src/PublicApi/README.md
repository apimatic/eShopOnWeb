# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


# Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow. Maxio Advanced Billing is
the system of record. All routes need a JWT from `POST /api/authenticate`; the caller is identified by the token.

| Route | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Live, non-archived plans in the configured product family (`isTruncated` flags a partial list) |
| `POST /api/subscriptions` | `{ "planHandle": "..." }` (optional; falls back to `Maxio:DefaultPlanHandle`). `201 Created`, `200 AlreadySubscribed`, or `202 PendingConfirmation` |
| `GET /api/my-subscriptions` | The caller's subscriptions read live from Maxio: plan, price, state, next billing date |

Errors are `{ "StatusCode", "Message" }`: 400 unknown plan, 409 the same subscribe is already in flight, 422 Maxio
declined, 502/503 Maxio unavailable or misconfigured. Provider detail stays in the server log.

**Configuration** (section `Maxio`; values go in user-secrets or host configuration, never in this repository):

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Basic-auth API key (falls back to `MAXIO_API_KEY`) |
| `Maxio:Subdomain` | unless `BaseUrl` | Site subdomain → `https://{subdomain}.chargify.com` (falls back to `MAXIO_SITE_SUBDOMAIN`) |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered (falls back to `MAXIO_DEFAULT_PRODUCT_FAMILY`) |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address when set |
| `Maxio:Environment` | no | `US` (default) or `EU` (falls back to `MAXIO_ENVIRONMENT`) |
| `Maxio:DefaultPlanHandle` | no | Plan used when the request names none |

The host refuses to start when a required key is missing. Example (from `src/PublicApi`):

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:DefaultPlanHandle" "<plan handle>"
```

**Duplicate safety.** Each write takes a primary-key claim in `CatalogContext` before Maxio is called: one
`BillingCustomerLink` per user and one `SubscriptionClaim` per (user, plan). The Maxio customer reference is
derived from the user name, so an existing customer and subscription are found again after the local store is
lost, for example when an in-memory database restarts. If a create's outcome is unknown (timeout or dropped
connection), the subscription is re-read by its reference instead of being reported as failed.

**SDK reference.** `src/Infrastructure` references the Maxio .NET SDK as source shipped by the
`maxio-advanced-billing` Claude Code plugin (a machine-local `ProjectReference` path). Re-point that reference
on machines where the plugin is installed elsewhere.
