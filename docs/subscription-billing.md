# Subscription billing verification

The PublicApi exposes an additive billing flow. The existing catalog, basket and order checkout remain available.

1. Use PowerShell 7 with the installed Maxio plugin and the supplied sandbox environment variables. This checkout references the plugin's generated SDK **in place**, outside the repository. Elsewhere, set `MaxioSdkRoot` to that installation's `sdk/dotnet` directory before building (environment variable or MSBuild `-p:MaxioSdkRoot=...`). The plugin SDK is not published on NuGet; CI also needs it installed. SDK source is not vendored.
2. Run `./scripts/Start-SubscriptionApi.ps1`. It imports the three supplied credential/catalog variables into PublicApi's .NET user-secrets without printing them, builds, checks the trusted developer certificate, and starts the host detached. It uses only assigned ports `APP_PORT_BLOCK_BASE + 3` (HTTPS) and `+ 4` (HTTP); logs and the PID live in the system temporary directory. The script prints the Swagger URL and stop command.
3. Run `./scripts/Verify-SubscriptionBilling.ps1`. It authenticates the demo shopper, lists plans, subscribes, checks the repeat request returns the same subscription, checks the account, and verifies anonymous requests and another user cannot see it. By default it selects the highest-priced offered plan. To select another plan, pass `-ProductHandle <handle-from-the-plans-response>`.
4. Stop the host using the printed `Stop-Process -Id <pid>` command before rebuilding or starting another instance.

PublicApi credentials are in its existing user-secret store. No credential values are stored in the checkout. Settings bind only from `Maxio`:

| Configuration | Supplied environment variable imported into user-secrets |
| --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` |
| `Maxio:BaseUrl` | Optional override, supplied through user-secrets or `Maxio__BaseUrl` |

The SDK's default region is US; `MAXIO_ENVIRONMENT` supplied for this sandbox is its region indicator, not a sandbox switch. Test versus live is determined by the configured site/credentials. The optional absolute HTTPS `BaseUrl` overrides the SDK's derived address verbatim, including any path prefix; HTTP is also accepted for loopback test servers. It also supports a different region's address. It is never assembled from seeded numeric IDs. Credentials and client options are captured once; restart after rotation. The first three keys are validated before the host accepts requests.

For manual requests, authenticate with `POST /api/authenticate`, JSON `{"username":"demouser@microsoft.com","password":"Pass@word1"}`, then send `Authorization: Bearer <token>` to each endpoint. The Web host's cookie is a separate authentication mechanism.

| Method / route | Request / response |
| --- | --- |
| `GET /api/subscription-plans` | Active, card-free plans in the configured family; handle, name, description, price in integer cents, interval and interval unit |
| `POST /api/subscriptions` | JSON `{"productHandle":"<offered-handle>","firstName":"Demo","lastName":"Shopper"}`; email and account ID come from Identity, never the request |
| `GET /api/my-subscriptions` | Provider-current subscriptions for the caller in the configured family, plus `hasPendingEnrollment` |

Enrollment returns HTTP 200 with `status: confirmed` and subscription ID, plan, price, currency, provider state and next billing date. HTTP 202 with `status: pending` means a concurrent or uncertain enrollment is still being reconciled; poll the account or repeat the same enrollment. Pending/assessing provider objects are never reported as confirmed. Terminal subscriptions can legitimately have no next date. Caller validation is HTTP 400; provider validation is sanitized HTTP 422; provider/configuration/transport failures become sanitized HTTP 503 ProblemDetails.

Card-free enrollment uses **remittance collection**: Maxio bills by invoice and tracks the balance; signup does not collect payment. Sandbox verification showed that omitting the collection method causes automatic collection to reject a paid plan without a payment profile, despite `require_credit_card=false`. No price, trial, setup fee, tax or billing-date overrides are sent. Plans requiring a card are excluded until a separate payment-capture flow is implemented. Metered usage and lifecycle changes are outside this feature.

There is one subscription per user per plan, including canceled/expired subscriptions: repeated POST is an idempotent enrollment, not a request for an additional subscription or reactivation. The API accepts only handles from the currently offered catalog. It does not trust caller-supplied user/customer IDs, prices, dates or states.

Primary-key claims in the existing Catalog database are inserted before each external create. The database rejects concurrent duplicate claims. All SDK retries are disabled. Deterministic provider references recover an existing enrollment after local mapping loss. A lost response triggers an immediate reference lookup; unresolved claims stay pending and a worker rechecks them every 30 seconds after their first minute. The interactive deadline is 45 seconds with up to 10 additional seconds for write reconciliation. Each SDK/HTTP attempt is capped at 10 seconds. Worker rows get 15 seconds and rotate by last-checked time.

Claims without an answer after an hour produce an operator alert containing the local claim ID. The worker only reads Maxio and updates local results; it never re-sends an uncertain charge. To resolve such a claim, inspect its reference and the provider record using the installed plugin: if it exists, let reconciliation complete; if Maxio support confirms the operation never committed and cannot still commit, delete that one claim in the application's database to permit a new enrollment. Do not expire unresolved claims automatically. Definitive provider refusals release the claim automatically. A crash between taking a claim and sending the request is deliberately treated as uncertain.

Development runs use `UseOnlyInMemoryDatabase=true` and `DOTNET_ROLL_FORWARD=Major`. In-memory Identity and claims are lost on restart, including seeded user IDs; a new run therefore creates new demo identities. Production uses the existing SQL connection and the included `AddBillingClaims` migration. Durable Identity plus the Catalog database are required for restart-safe and multi-instance claims. The migration is applied by the app's existing catalog startup path; apply it through your deployment migration process before rollout. SQL Server was not available for live verification on this machine.

Run regression checks with:

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet build eShopOnWeb.sln
dotnet test eShopOnWeb.sln
```

The tests use a fake HTTP transport with the real plugin SDK, never real billing credentials. They exercise concurrent claims, rejected writes, malformed responses, dropped connections after provider commits, background recovery, catalog limits, identity isolation and HTTP JWT endpoints. Existing dependency audit/package-source warnings predate this change.
