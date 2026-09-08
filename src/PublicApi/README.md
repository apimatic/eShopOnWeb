# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription Billing (Maxio Advanced Billing)

Additive, JWT-authenticated endpoints that use Maxio Advanced Billing as the billing system of record. Maxio is called over plain HTTPS (Basic auth with the API key); no gateway or extra infrastructure is required.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/subscription-plans` | Lists the plans (products) of the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribes the calling user to a plan. Idempotent body: `{ "planHandle": "eshop-pro" }`. |
| `GET /api/my-subscriptions` | Lists the calling user's subscriptions (state, price, next billing date) from Maxio. |

Configuration is bound from the `Maxio:` settings section (user-secrets / environment; never committed):

- `Maxio:ApiKey` - Advanced Billing API key (sent as the Basic-auth username).
- `Maxio:Subdomain` - Maxio site subdomain; the base address is derived as `https://{subdomain}.chargify.com` (US data center).
- `Maxio:BaseUrl` - optional override; when set it is used verbatim instead of the derived address.
- `Maxio:ProductFamilyHandle` - product family whose plans are offered for signup.

Idempotency guarantees for `POST /api/subscriptions`:

1. Exactly one Maxio customer per eShopOnWeb user - the customer `reference` is `eshoponweb:{username}` and Maxio enforces reference uniqueness (a lost create race is resolved by re-reading the winner).
2. Exactly one live subscription per plan - the service holds a per-user lock, checks the customer's live subscriptions against the plan before enrolling, and replays the existing enrollment instead of creating a duplicate (Maxio itself allows duplicate active subscriptions, so the check is done here). Both checks survive app restarts because they consult Maxio, not the local database.

The `MaxioSubscriptions` table is a local audit/mirror of enrollments; Maxio remains authoritative.

