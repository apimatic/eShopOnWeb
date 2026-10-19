# Subscriptions (Maxio Advanced Billing)

Recurring-subscription billing runs alongside the existing Catalog → Basket → Order flow; it does not
replace it. **Maxio Advanced Billing is the system of record** for customers, plans and subscriptions.
The capability is exposed only by `src/PublicApi`; the Web host does not register it and needs no Maxio
configuration.

## Endpoints

All three require a JWT bearer token from `POST /api/authenticate` (any authenticated user; no role).
The caller is identified by the token's name claim; the billing customer's e-mail is read from the
Identity store.

| Method & route | Purpose | Success |
| --- | --- | --- |
| `GET /api/subscription-plans` | Non-archived products of the configured Maxio product family | `200` `{ plans: [{ handle, name, description, price, interval, intervalUnit }], isComplete }` — `isComplete = false` means the listing stopped at its page cap |
| `POST /api/subscriptions` body `{ "planHandle": "<handle>" }` | Ensure a Maxio customer exists for the user (reference = user name), then subscribe them | `201` `status: "created"` · `200` `status: "already_subscribed"` (repeat for the same plan) · `202` `status: "pending_confirmation"` (Maxio may have created it; confirmed on the next call) — each with `subscription: { subscriptionId, planHandle, planName, price, state, nextBillingAt, createdAt }` |
| `GET /api/my-subscriptions` | The user's subscriptions as Maxio holds them | `200` `{ subscriptions: [...], hasPendingSubscription }` |

Errors are JSON `{ StatusCode, Message, Errors }` (`Errors` carries Maxio's validation messages on 422):

| Status | When |
| --- | --- |
| 400 | `planHandle` missing or not a plan of the configured family |
| 409 | The user already has a subscription to a different plan, or a subscribe call is already in progress |
| 422 | Maxio rejected the customer or subscription |
| 502 | Maxio returned an error that is not the caller's to fix (credentials, configuration, unreadable response) |
| 503 | Maxio unreachable or throttling, or the plan catalog could not be read completely |
| 504 | Maxio did not answer within the 30 s request budget |

Duplicate protection: a `SubscriptionEnrollments` row keyed by user name is inserted before any Maxio
write, so a double-click is refused by the database primary key. Because that table does not survive a
restart under `UseOnlyInMemoryDatabase=true`, every create is preceded by a Maxio check (subscription by its
deterministic reference, then the customer's live subscriptions on the family's plans); an existing
subscription is replayed (200) or reported as a different-plan conflict (409) instead of being duplicated.
One subscription per user is supported through this API (plan change and cancellation are out of scope).

Subscriptions are created with Maxio's `remittance` payment collection: the subscriber is invoiced and
needs no payment method on file. (Sites on Maxio's legacy Statements architecture use `invoice` instead.)

## Configuration

Bound from the `Maxio:` section. No values are committed to the repository.

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key (HTTP Basic user name) |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | Maxio site subdomain (US-hosted sites) |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are offered as plans |
| `Maxio:BaseUrl` | no | Overrides the API base address (EU-hosted sites, proxies, mocks); used as given (a trailing `/` is dropped) |

PublicApi validates these at startup and **refuses to start** when a required key is missing or blank.
The API key is read once when the Maxio client is first created; rotating it requires a restart.

### Local development (user-secrets)

PublicApi already has a `UserSecretsId`. From the repository root:

```bash
dotnet user-secrets set "Maxio:ApiKey" "<your Maxio API key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<your site subdomain>" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<product family handle>" --project src/PublicApi
```

User-secrets load in the `Development` environment (the default launch profile). Elsewhere, supply the
same keys through any configuration source, e.g. environment variables `Maxio__ApiKey`,
`Maxio__Subdomain`, `Maxio__ProductFamilyHandle`, `Maxio__BaseUrl`, or a secret store. Point
development and test at a Maxio sandbox site and sandbox API key; Maxio has no separate sandbox
environment, so the site you configure is what decides where traffic goes.

Run PublicApi without LocalDB by setting `UseOnlyInMemoryDatabase=true`. In that mode the
`SubscriptionEnrollments` claims live in memory and are lost on restart; Maxio-side reconciliation
(customer by reference, subscription by its deterministic reference) still prevents duplicates. With
SQL Server, apply the `AddSubscriptionEnrollments` migration.

## SDK dependency

The Maxio Advanced Billing .NET SDK is **not on NuGet**. It ships as source inside the
`maxio-advanced-billing` plugin and is referenced where it sits (never copied into this repository) from
`src/Infrastructure/Infrastructure.csproj` through the MSBuild property `MaxioSdkProjectPath`.
Its default is a relative path that assumes the plugin marketplace is checked out next to this repository
(`../marketplace/plugins/maxio-advanced-billing/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`
relative to the repository root). Re-point it with either:

```bash
dotnet build -p:MaxioSdkProjectPath=/path/to/MaxioAdvancedBilling.csproj
# or set an environment variable named MaxioSdkProjectPath
```

A machine or CI runner without the plugin cannot build the solution. The SDK compiles with C# 14, so the
**.NET 10 SDK** is required to build (the projects still target `net8.0`).

## Tests

- `tests/UnitTests/Infrastructure/Billing/` — the billing service against a stubbed `HttpClient` (the
  SDK's test seam) and the EF Core in-memory store: plan paging and completeness, the plan-handle check,
  customer create/re-read, duplicate claims, unknown write outcomes, settings validation.
- `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` — authentication and error mapping. The test
  host uses placeholder `Maxio:` values pointing at a closed loopback port, so no test reaches Maxio.
