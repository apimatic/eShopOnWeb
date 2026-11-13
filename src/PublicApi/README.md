# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring subscriptions alongside the one-time Catalog → Basket → Order flow.
Maxio is the system of record for plans and subscriptions. All three endpoints need a JWT from `POST /api/authenticate`.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans offered: active, card-free products of the configured product family (`isComplete` is false if the list was cut short) |
| `POST /api/subscriptions` `{ "planHandle": "...", "firstName"?: "...", "lastName"?: "..." }` | Ensures a Maxio customer exists for the caller, then subscribes them. `201 created`, `200 already_subscribed`, `202 pending_confirmation` (outcome being reconciled); `400` unknown plan, `409` identical request in flight, `422` rejected by Maxio, `503` Maxio unavailable, `502` integration misconfigured |
| `GET /api/my-subscriptions` | The caller's subscriptions as Maxio reports them, plus any subscribe requests still being confirmed |

Configuration (section `Maxio`, validated at startup — the host refuses to start without the required keys):

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Secret. Development: `dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi`; elsewhere `Maxio__ApiKey` or a secret store |
| `Maxio:Subdomain` | unless `BaseUrl` is set | Site subdomain; the API host is derived from it |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans |
| `Maxio:BaseUrl` | no | When set, used verbatim as the API base address |
| `Maxio:Environment` | no | `US` (default) or `EU` |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default; invoice the customer, no card needed), `invoice` (legacy sites), `automatic`, `prepaid` |
| `Maxio:RequestBudgetSeconds` / `Maxio:PlanCacheSeconds` | no | Total Maxio time per API request (30) / plan-list cache (60, 0 disables) |

Double submits are refused by claim rows in `CatalogContext` (migration `AddSubscriptionBilling`), backed by Maxio's unique customer reference and a check for an existing live subscription before every create.
With `UseOnlyInMemoryDatabase=true`, the claim rows are lost on restart; Maxio-side checks still prevent duplicates.

The Maxio .NET SDK is referenced as a project from the `maxio` Claude Code plugin (see `src/Infrastructure/Infrastructure.csproj`). That path is machine-local, and building it needs the .NET 10 SDK (`global.json` rolls forward).
