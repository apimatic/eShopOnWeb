# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

Subscription endpoints use Maxio Advanced Billing as the billing system of record and require a JWT issued by `POST /api/authenticate`:

* `GET /api/subscription-plans`
* `POST /api/subscriptions` with `{ "planHandle": "<handle from the plans response>" }`
* `GET /api/my-subscriptions`

Configure the PublicApi user-secrets store (or an equivalent protected configuration provider) with these `Maxio:` keys:

* `Maxio:ApiKey`
* `Maxio:Subdomain`
* `Maxio:ProductFamilyHandle`
* `Maxio:BaseUrl` (optional; overrides the derived site API address)

The customer and subscription references are deterministic application identifiers, so retries and repeated signup requests converge on the same Maxio records. The Identity database migration `AddMaxioSubscriptionMappings` creates the local correlation index; Maxio remains authoritative for subscription state and billing dates.
