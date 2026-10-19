# Maxio Advanced Billing — subscription billing for eShopOnWeb (PublicApi)

SDK source (plugin-relative): `sdk/dotnet/` — map `sdk-map.md` + `map/operations/*.md`. All `src/...` paths below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
|---|------|-----------|
| 0 | Env prep: `global.json` rollForward `latestMajor`; run with `DOTNET_ROLL_FORWARD=Major`; load `MAXIO_*` env values into PublicApi user-secrets (`Maxio:*` keys) | — |
| 1 | Reference SDK project from `src/PublicApi`; bind `MaxioSettings` (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional, `Maxio:Environment` optional, default `US`) with fail-fast validation | — |
| 2 | Register client singleton over named `HttpClient`; environment/site/BaseUrl override; retry/timeouts; explicit logging | — |
| 3 | `SubscriptionEnrollment` entity (ApplicationCore) + EF config + migration (Infrastructure, `CatalogContext`) — the local claim store | — |
| 4 | `MaxioBillingService`: list plans (family by handle), ensure customer (by reference), create subscription (by reference), list my subscriptions | `ProductFamilies.ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, `Customers.ListCustomerSubscriptions` |
| 5 | Endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` (MinimalApi.Endpoint `IEndpoint`, JWT, identity = `ClaimTypes.Name`) | — |
| 6 | Tests (PublicApiIntegrationTests, MSTest) over a stub `HttpMessageHandler`; live sandbox E2E | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names; never flat arguments.
> ⚠ Every SDK type is written with the namespace its source path implies (path given per type, below) — never inferred from a neighbouring type.

| Controller | Signature | Request record (members) | Body model (fields) | Response + fields read | Error | Paging | Source |
|---|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (id or `handle:<handle>`), `Page: int = 1` (min 1), `PerPage: int = 20` (max 200), `IncludeArchived: bool?` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`Product`, required) → `Id int?`, `Name`, `Handle`, `Description`, `PriceInCents long?`, `Interval int?`, `IntervalUnit IntervalUnit?`, `ArchivedAt`, `RequireCreditCard bool?` | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404], `TryGetRawError` [fallback] | none from SDK (plain list); page/per_page driven by us | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `src/MaxioAdvancedBilling/Models/Product.cs` |
| `client.Customers` | `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (required) → `Id int?`, `Reference`, `Email` | B: `ApiException<RawError>` (404 = not found) | — | `map/operations/Customers.md`; `src/MaxioAdvancedBilling/Models/CustomerResponse.cs`, `Customer.cs` |
| `client.Customers` | `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `CreateCustomer`: `FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?` | `CustomerResponse` → `.Customer.Id` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError` [fallback]; `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.AnyOf.Errors1?` → `TryGetListOfString(out IReadOnlyList<string>)` / `TryGetCustomerError(out CustomerError)` (`Customer (customer): string?`) | — | `map/operations/Customers.md`; `src/MaxioAdvancedBilling/Models/CreateCustomer.cs` |
| `client.Customers` | `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` → `.Subscription` (`Subscription?`) | B: `ApiException<RawError>` | none | `map/operations/Customers.md` |
| `client.Subscriptions` | `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` | — | `SubscriptionResponse` → `.Subscription` | A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404], `TryGetRawError` [fallback] | — | `map/operations/Subscriptions.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → `CreateSubscription`: `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (no member marked required; `<remarks>`: identify customer with `customer_id` or `customer_reference`, product by handle/id). Left out: payment profile / card / bank attributes (plans need no payment method), coupons, components, custom price. | `SubscriptionResponse` → `.Subscription` → `Id int?`, `State SubscriptionState?`, `ProductPriceInCents long?`, `CurrentBillingAmountInCents long?`, `NextAssessmentAt DateTimeOffset?`, `CurrentPeriodEndsAt`, `ActivatedAt`, `CreatedAt`, `Reference`, `Currency`, `Product` (`Handle`, `Name`, `Interval`, `IntervalUnit`) | A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors (errors): IReadOnlyList<string>, required`), `TryGetRawError` [fallback] | — | `map/operations/Subscriptions.md`; `src/MaxioAdvancedBilling/Models/CreateSubscription.cs`, `Subscription.cs` |

**Enums** (`MaxioAdvancedBilling.Models.Enums`): `SubscriptionState` — `Pending pending`, `FailedToCreate failed_to_create`, `Trialing trialing`, `Assessing assessing`, `Active active`, `SoftFailure soft_failure`, `PastDue past_due`, `Suspended suspended`, `Canceled canceled`, `Expired expired`, `Paused paused`, `Unpaid unpaid`, `TrialEnded trial_ended`, `OnHold on_hold`, `AwaitingSignup awaiting_signup` (open enum — emit `.Value`). `IntervalUnit` — `Day day`, `Month month`. `CollectionMethod` — `Automatic automatic`, `Remittance remittance`, `Prepaid prepaid`, `Invoice invoice` (source: `src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs`; prose: Relationship Invoicing uses remittance/automatic/prepaid, legacy Statements uses invoice/automatic).

**Client / auth / servers**: `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <api key>, Password = "x" }` (map: password is `x`). `Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us | .Eu`. Group `Production` (all ops in scope): `https://{site}.chargify.com` (US) / `https://{site}.ebilling.maxio.com` (EU); `{site}` → `options.Server.Production.Us.Site` / `.Eu.Site`; override `options.Server.Production.<Env>.BaseUrl`. Group `Ebb` not touched. Retry: `MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with {...}`. Source: `sdk-map.md` *Getting a client*, *Servers & auth*.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| plan handle accepted by subscribe must be one the plans list returns for the configured family (non-archived) | `CreateSubscription` ← `ListProductsForProductFamily` | `MaxioBillingService.SubscribeAsync`, before `CreateSubscription` (looks the handle up in `GetPlansAsync`) |
| `customer_id` sent on subscribe must be the customer resolved/created for this caller's reference | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | `MaxioBillingService.EnsureCustomerAsync` result passed straight into `CreateSubscription` |

## 3. Trap notes

- Step 1/2 — a missing/blank credential part sends an unauthenticated call that only fails as 401 later — MUST load `maxio-advanced-billing:dotnet-authentication`.
- Step 2 — singleton client lifetime vs `HttpClient` DNS staleness and shared default client — MUST load `maxio-advanced-billing:dotnet-client-initialization`.
- Step 2 — what each timeout knob bounds, which verbs retry, and which option is read when (Environment vs Server) — MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Step 2 — logging is on under DI and env var can force body logging; customer email/name in bodies — MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Step 4 — request record vs body model naming (`…OperationRequest`), positional `requestOptions` — MUST load `maxio-advanced-billing:dotnet-calling-endpoints`.
- Step 4 — open enums and their string form leaving the process; nullable response members — MUST load `maxio-advanced-billing:dotnet-models`.
- Step 4 — Case A vs Case B catch ladders, deserialization failures, connection failures on writes — MUST load `maxio-advanced-billing:dotnet-error-handling`.
- Step 4 — a list driven by page/per_page with no SDK paging must not loop unbounded or truncate silently — MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Step 6 — stub seam, disposed request bodies, building SDK exceptions — MUST load `maxio-advanced-billing:dotnet-testing`.

## 4. REQUIRED READING

- `maxio-advanced-billing:dotnet-client-initialization` · step 2
- `maxio-advanced-billing:dotnet-authentication` · steps 1–2
- `maxio-advanced-billing:dotnet-configuration-resilience` · steps 2, 4
- `maxio-advanced-billing:dotnet-calling-endpoints` · step 4
- `maxio-advanced-billing:dotnet-models` · step 4
- `maxio-advanced-billing:dotnet-error-handling` · step 4 — hazard: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).
- `maxio-advanced-billing:dotnet-testing` · step 6

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; `[Required]` + custom validation (ApiKey, Subdomain unless BaseUrl set, ProductFamilyHandle; Environment ∈ {US, EU}; BaseUrl absolute https/http URI) with `ValidateOnStart()`; password half is the constant `x` from the map. Messages name the key, never the value. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets (PublicApi `UserSecretsId`) loaded from `MAXIO_*` env; prod: env vars / any `IConfiguration` source (`Maxio__ApiKey`). Options captured once into the singleton client — rotation requires a restart (accepted). |
| 3 | Total timeout budget | Per attempt 10 s (`Retry.Timeout` and `HttpClient.Timeout`); whole-operation budget 30 s via linked `CancellationTokenSource` in `MaxioBillingService.Bounded` (linked to `HttpContext.RequestAborted`); `Maxio:RequestBudgetSeconds` configurable. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) kept — our POSTs (`CreateCustomer`, `CreateSubscription`) are never resent by the SDK; reads retry up to 2 times. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no key member; dedupe by our deterministic `reference` (`Body.Customer.Reference`) + reconcile via `ReadCustomerByReference`. `CreateSubscription`: no key member; deterministic `Body.Subscription.Reference` + reconcile via `FindSubscription`. Whether Maxio rejects duplicate references: UNVERIFIED → never relied on; local claim prevents the second send. |
| 6 | Observability | SDK `LoggerFactory` = host's; request/response lines at Information, retries Warning; our service logs subscribe outcome (user reference hash, plan, subscription id) at Information, provider failures at Warning/Error with status + `RequestUri` path only. Maxio error bodies carry no correlation id in the generated models; we log status + error list. |
| 7 | Sensitive data | Yes — customer email/first/last name in `CreateCustomer` body. `LogRequestBody=false`, `LogRequestHeaders=false`, `LoggerFactory` set explicitly (disables `MAXIOADVANCEDBILLINGCLIENT_LOG`). JSON body, no form keys. |
| 8 | Environment selection | Group `Production` only (all 6 ops). `Maxio:Environment` US (default)/EU selects `ServerEnvironment`; `Maxio:Subdomain` → `Server.Production.<Env>.Site`; `Maxio:BaseUrl` if set → `Server.Production.<Env>.BaseUrl` verbatim. SDK has no sandbox environment — sandbox = a test site subdomain; dev user-secrets point at the sandbox site, prod config supplies the live site. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (+ the `CreateCustomer` it may need, same claim) | `CatalogContext.SubscriptionEnrollments`, row keyed by `UserReference` (deterministic hash of the user name) inserted with state `Pending` before any SDK write | primary key on `UserReference` (refused by SQL Server and by EF InMemory) | `catch (DbUpdateException)` / InMemory duplicate-key `ArgumentException` in `SubscriptionEnrollmentStore.TryClaimAsync` → returns existing enrollment (409 if still pending) | `SubscriptionEnrollmentStore.TryClaimAsync` (called first in `MaxioBillingService.SubscribeAsync`; takeover of stale/unknown claims via `SubscriptionEnrollmentStore.TryUpdateAsync` on the `ClaimToken` concurrency token) → `MaxioBillingService.CreateSubscriptionAsync` (`client.Subscriptions.CreateSubscription`); the customer write `MaxioBillingService.EnsureCustomerAsync` (`client.Customers.CreateCustomer`) runs only under the same claim. Test: `SubscriptionEndpointsTests.Subscribe_ConcurrentDoubleClick_CreatesExactlyOneSubscription` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (page/per_page loop, 200/page) | `MaxPlanPages` = 5 pages | `PlanCatalog.IsComplete` → `ListSubscriptionPlansResponse.IsComplete` | `MaxioBillingService.GetPlansAsync` sets `PlanCatalog.IsComplete` → surfaced by `SubscriptionPlanListEndpoint.HandleAsync` as `ListSubscriptionPlansResponse.IsComplete` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | customer `reference` = `eshop-<userhash>` | `catch (SdkConnectionException)` / budget-expired `catch (OperationCanceledException)` / 2xx `ResponseDeserializationException` in `MaxioBillingService.EnsureCustomerAsync` → `ReconcileCustomerAsync` (`ReadCustomerByReference`); unresolved → `EnrollAsync` calls `MarkUnknownAsync` (enrollment `Unknown`); the next `SubscribeAsync` takes the claim over and `EnsureCustomerAsync` reads by reference before any create | `SubscriptionEndpointsTests.CreateCustomerConnectionDrop_IsReconciledByReference` |
| `CreateSubscription` | `FindSubscription` | subscription `reference` = `eshop-sub-<userhash>` | `catch (SdkConnectionException)` / budget-expired `catch (OperationCanceledException)` / 2xx `ResponseDeserializationException` in `MaxioBillingService.CreateSubscriptionAsync` → `ReconcileSubscriptionAsync` (`FindSubscription`); unresolved → `MarkUnknownAsync`; settled by `MaxioBillingService.GetMySubscriptionsAsync` (marks Active when Maxio shows our reference) or by the next `SubscribeAsync` (`EnrollAsync` adopts via `FindSubscription` before creating) | `SubscriptionEndpointsTests.CreateSubscriptionConnectionDrop_IsReconciledByReference` |

## 6. Assumptions & Blockers

- Assumption (YOUR CALL — not in the map): one Maxio subscription per eShopOnWeb user via this flow; a second subscribe returns the existing subscription (same plan → 200, different plan → 409). Plan changes/cancellation are out of scope.
- Assumption: caller identity = JWT `ClaimTypes.Name` (user name = email in eShopOnWeb); Maxio customer first/last name derived from the email local part / "eShopOnWeb" since Identity stores no names.
- Assumption: in-memory DB loses claims on restart; FindSubscription-by-reference inside the claim adopts an existing Maxio subscription, so a restart cannot produce a duplicate.
- Assumption (YOUR CALL — not in the map): new subscriptions use `payment_collection_method = remittance` (configurable via `Maxio:PaymentCollectionMethod`). Verified live: with the default (`automatic`) the sandbox rejects with 422 "No payment method was on file for the $299.00 balance" — eShopOnWeb has no card capture, so invoices are paid out-of-band.
- Note: the SDK pulls System.Text.Json 10 into PublicApi; ASP.NET Core 8 TestServer's response PipeWriter lacks `UnflushedBytes`, so in-process tests install `TestServerPipeWriterFix` (Kestrel is unaffected — verified live).
- Note: the SDK is referenced as a project at the plugin path (`MaxioSdkProject` MSBuild property, default relative to this clone) — builds need the plugin installed there or the property overridden.
- Blockers: none.

## 7. Source labels

Rows cite map pages / SDK files above; idempotency of references is `UNVERIFIED`; all persistence/claim/API-shape choices are `YOUR CALL — not in the map`.
