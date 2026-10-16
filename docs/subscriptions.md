# Subscription billing

PublicApi has three JWT-authenticated endpoints. Existing basket and order checkout is unchanged.

| Request | Result |
| --- | --- |
| `GET /api/subscription-plans` | Complete, unarchived recurring catalog from the configured family; handles, integer cents, intervals and payment/address requirements. |
| `POST /api/subscriptions` | Body: `{"productHandle":"<offered handle>","firstName":"Demo","lastName":"Shopper"}`. Returns `200 {status:"confirmed",subscription:{...}}`, or `202 {status:"pending",subscription:null}` with a location and retry hint. |
| `GET /api/my-subscriptions` | Current provider subscriptions plus unresolved enrollment claims for the authenticated shopper. |

Ownership and email come from the authenticated account. Customer IDs, references, custom prices and identity fields are not accepted as billing inputs. New JWTs contain the immutable application user ID; older username-only JWTs are resolved through UserManager. Names are required for Maxio customer creation. All subscription prices, states and dates come from Maxio; cents are not floating-point money. `nextBillingDate` is Maxio's next assessment timestamp and can be null when Maxio supplies no date. Plan prices use the site's default currency; a subscription response includes its currency explicitly.

This integration uses **remittance invoice billing**: Maxio issues the initial and recurring invoices at the normal catalog price and schedule without capturing a card in this API. An active subscription does not by itself establish that its invoice is paid. Payment capture, entitlements, cancellation, metered usage and a storefront UI are outside these endpoints.

## Verify locally

1. Use PowerShell 7 with the supplied sandbox environment variables and assigned port block. The required settings were loaded into PublicApi user-secrets during implementation. The verification script also imports any supplied variables into user-secrets without echoing their values.
2. From the repository root, run:

   ```powershell
   ./scripts/Verify-Subscriptions.ps1 -ProductHandle eshop-pro -AlternateProductHandle basic-plan
   ```

   Omit the handle arguments for another catalog: the script chooses an offered plan. It builds PublicApi, starts it detached on the assigned port block, authenticates the seeded demo shopper, lists plans, subscribes, repeats the POST and checks the same subscription ID in the account. It prints provider price/state/date and stops its process in `finally`. This creates real sandbox customers, subscriptions and invoices; it does not delete them. Logs stay in a temporary directory outside the repository.
3. Run the automated checks:

   ```powershell
   $env:DOTNET_ROLL_FORWARD = 'Major'
   dotnet test eShopOnWeb.sln
   ```

   Billing tests use the actual SDK and production configuration registration with a fake HttpMessageHandler; they never call the sandbox. They cover JWT enforcement, account isolation, concurrent/repeated enrollment, actual InMemory primary-key collisions, foreign/archived handles, unknown outcomes, malformed replies, pending recovery, catalog limits and configuration validation.

For manual Swagger testing, start PublicApi detached with `ASPNETCORE_ENVIRONMENT=Development`, `UseOnlyInMemoryDatabase=true`, `DOTNET_ROLL_FORWARD=Major` and HTTPS URLs inside your assigned port block. Authenticate through `POST /api/authenticate` using the seeded `demouser@microsoft.com` / `Pass@word1`, then use `Bearer <token>` in Swagger's Authorize box. A Web cookie is insufficient. Check the development certificate with `dotnet dev-certs https --check --trust`. Stop your process by PID before rebuilding or starting another host.

## Configuration and deployment

| .NET configuration key | Local source |
| --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` → user-secrets |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` → user-secrets |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` → user-secrets |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` → user-secrets; SDK region `US` or `EU`, default `US` |
| `Maxio:BaseUrl` | Optional user-secret or `Maxio__BaseUrl` deployment override, used verbatim in the selected SDK server node |

Missing/blank credentials, site or family, unsupported regions and invalid override URLs prevent startup. Sandbox is a dedicated Maxio site, not an SDK environment enum. Keep development credentials pointed at the supplied sandbox. Production can supply the same configuration keys through a secret store or .NET environment configuration. Settings and SDK options are captured once; restart to rotate credentials.

The plugin SDK is generated source installed **outside this repository**. `Infrastructure.csproj` references that copy; it is not a NuGet dependency. Teammates and CI must install the same Maxio plugin and, when its location differs, pass `-p:MaxioSdkProject=<absolute path to sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj>` to builds/tests. The SDK requires the .NET 10 compiler. `global.json` allows SDK major roll-forward. The JSON packages match the plugin SDK's required versions.

Use the existing SQL CatalogContext in production and deploy the `AddBillingClaims` migration through your normal migration process. The generated migration only creates BillingClaims and its index. PublicApi retains the reference app's startup migration behavior. SQL migration execution was not tested on this machine, which has no SQL Server. InMemory is for local development: application users and claims disappear on restart, including randomly generated seeded user IDs. Duplicate protection across restarts and app instances requires a shared durable database and stable user IDs.

## Recovery and operations

One customer belongs to each user/site; one enrollment belongs to each user/site/family/product handle. Different offered plans can coexist. Repeated POSTs return the current existing enrollment, even if it was canceled externally; a second lifecycle enrollment requires a separate future API contract.

Each provider write follows a persisted primary-key claim. Subscription intents awaiting customer reconciliation transition atomically to Pending using an EF concurrency token before sending. The SDK retries only GET/HEAD, never POST. A timeout, connection reset, unreadable success or ambiguous server response preserves the claim and re-reads Maxio by deterministic reference and verified ownership. A definite rejection releases the claim. The generated SDK's random Idempotency-Key header is not relied on for deduplication.

Requests have a shared 20-second normal deadline and an independent, at-most-10-second immediate recovery window, bounded by 30 seconds from enrollment request start. Reads and pages share that deadline. Plans stop after 25 pages of 200 and return `503` with `catalog_incomplete` instead of a partial success. Provider validation returns `422`; provider authentication faults map to `502`, quota faults to `503`. Response bodies contain safe messages and application trace IDs, never raw SDK errors or customer payloads.

GET/POST and a 30-second background sweep reconcile unsettled records. An intent awaiting its customer can proceed once that customer is found; a Pending/Unknown write is **never resent**, including after restart. A crash between claiming and sending can therefore leave a claim pending indefinitely. An operator must establish the provider outcome before repairing such a record; blindly deleting uncertain claims can duplicate billing. Monitor persistent pending rows and billing warnings. The sweep processes up to 100 unresolved claims per pass, and account requests also reconcile their own rows. No payment data is collected; SDK body/header logging is disabled explicitly, and application logs contain operation, elapsed time, status and trace identifiers.
