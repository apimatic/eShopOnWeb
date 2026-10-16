# Subscription billing

PublicApi exposes a parallel subscription flow. Existing basket and checkout behavior remains intact.
Maxio owns the catalog, prices, subscription state, and billing dates. The application stores only
ownership/coordination claims in its existing Catalog database.

## Verify the sandbox

From the repository root in PowerShell 7:

1. Keep the installed Maxio Advanced Billing plugin available. The SDK is referenced directly from its
   installed source, as required by the plugin. This checkout has a default path to that installation.
   On another machine/CI, pass `-p:MaxioSdkProject=<absolute path to plugin/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj>`
   to builds/tests, or set the `MaxioSdkProject` environment variable. The SDK is not on NuGet and is not vendored.
   SDK 10 is required to compile the plugin's C# 14 source. `global.json` permits major roll-forward.
2. Load the supplied sandbox environment variables into PublicApi user-secrets:
   `./scripts/Initialize-MaxioSecrets.ps1`. This has already been done on this machine.
   It reads `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`, and the
   `MAXIO_ENVIRONMENT` region (`US` or `EU`). No values are written inside the repository.
   An explicit `-BaseUrl` is saved verbatim as `Maxio:BaseUrl`.
3. Check the dev certificate with `dotnet dev-certs https --check --trust`.
4. Stop any previous PublicApi instance, then run `./scripts/Verify-Subscriptions.ps1`.
   The script builds, starts PublicApi detached on the assigned port block, waits for readiness,
   obtains a JWT, lists plans, submits eight concurrent Pro requests, repeats the request, checks the
   account and anonymous access, and stops its process in `finally`. Local certificate verification
   is bypassed only by this development smoke script. Use `-ProductHandle basic-plan` for Basic, or a
   handle from the configured catalog on another site. This script creates real sandbox subscriptions.
5. Run the complete automated suite with `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test eShopOnWeb.sln`.
   Subscription tests use an isolated HTTP stub under the actual SDK and do not contact Maxio.

The smoke script uses the seeded `demouser@microsoft.com` / `Pass@word1` account. Its token stays in memory.
The default assigned PublicApi URL in this checkout is `https://localhost:39483`; the script derives
its ports from `APP_PORT_BLOCK_BASE` and `APP_PORT_BLOCK_SIZE` instead of assuming that value.

## HTTP contract

All three routes require `Authorization: Bearer <JWT>` from `POST /api/authenticate`.
The Web host's cookie is not accepted. Shopper identity is resolved from the token and Identity store.

| Method and path | Request | Response |
| --- | --- | --- |
| `GET /api/subscription-plans` | none | `{ plans: [...], isTruncated: false }`; handle, name, description, price in cents, billing interval, and payment-method requirement |
| `POST /api/subscriptions` | `{ "productHandle": "<handle returned above>" }` | 200 `{ subscription: {...}, isPending: false }`; 202 while provider outcome is pending, with account Location and Retry-After |
| `GET /api/my-subscriptions` | none | `{ subscriptions: [...], hasPendingRequests: false }` |

A confirmed subscription includes provider ID, plan handle/name, actual recurring price in cents,
currency, interval, provider state, next billing date (`next_assessment_at`), and current period end.
Missing provider values remain null; the application does not manufacture an active state or date.
Provider validation errors return 400/409/422; provider authentication errors return 502, quota errors
503, and read deadline expiry 504. Responses are sanitized ProblemDetails with a local trace ID.

Enrollment uses **remittance collection**: Maxio creates recurring invoices without capturing a card.
The initial invoice can be outstanding while the subscription is active. The sandbox rejected automatic
collection without a payment method even though the product's `require_credit_card` was false.
No price, trial, or billing-date override is sent. Sites must support Relationship Invoicing/remittance
for this card-free flow. This work does not add card collection, payment settlement, cancellation,
upgrades, resubscription after cancellation, or metered usage submission.

## Configuration and operations

Exactly four settings are bound from `Maxio`:

| Key | Source / behavior |
| --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` loaded into user-secrets; deployment secret providers may supply the configuration key directly |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN`; required |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY`; required; catalog is traversed by handle, never a seeded numeric ID |
| `Maxio:BaseUrl` | Optional literal override, used verbatim; otherwise SDK US template uses Subdomain. EU sites may supply the EU base address. |

Missing/blank required settings fail host startup. Options are frozen once at startup; restart after
rotation. The SDK has regional environments, not a sandbox switch: use the intended sandbox site/key.
Secrets are never logged. Request bodies/headers stay disabled; safe operation/status/request-ID
metadata and local trace IDs support diagnostics. Do not enable SDK per-call trace logging on customer paths.

One subscription is allowed per user and plan. Different plans share one customer. A primary-key claim
is inserted before each create, and conflicting requests reconcile the same deterministic reference.
POST is excluded from SDK retries. On a dropped connection, unreadable successful response, or uncertain
provider result, the same request attempts a bounded lookup. Unresolved claims return 202 and a hosted
worker continues lookups every 30 seconds. It never resends an uncertain create, including after restart.
Definitive rejections release the claim. A catalog cap is visible, and subscribe refuses incomplete catalogs.
Interactive work has one 30-second budget plus at most 10 seconds for write recovery; attempts use 10-second bounds.

With `UseOnlyInMemoryDatabase=true`, identities and claims disappear at restart. Maxio's sandbox data
remains, but newly seeded local identities have new IDs; use persistent Identity and Catalog databases
to retain ownership across restarts. Production instances must share those databases. The added
`AddSubscriptionBillingClaims` migration creates only the coordination table/index and is applied by
the existing Catalog seeding/migration path. Back up the claims together with Identity. An unresolved
claim that remains absent at Maxio is deliberately retained: do not delete it to retry until an operator
has established the original write cannot still complete. The app's existing demo JWT signing secret
and seeded credentials must be replaced through its normal authentication hardening before deployment.

Live verification succeeded for Pro and Basic, repeat enrollment, account reads, invalid-plan rejection,
and isolation between shoppers. Automated tests cover concurrent writes, configuration/base override,
transport ambiguity and later reconciliation, malformed responses, validation rejection recovery, and
catalog truncation. SQL migration generation is checked; no SQL Server is available on this machine
for a live SQL migration/concurrency test.
