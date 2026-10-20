# Maxio Advanced Billing plan: "Subscribe" on eShopOnWeb PublicApi

Source root for every `source` cell below: `sdk/dotnet/` inside the maxio-advanced-billing plugin (plugin-relative). `map` = `sdk/dotnet/sdk-map.md`; `ops/X` = `sdk/dotnet/map/operations/X.md`; `src/...` = `sdk/dotnet/src/MaxioAdvancedBilling/...`.

## 1. Scope & sequence

Repo survey (pattern -> exemplar to imitate; nothing copied inline):

| Convention | Exemplar |
| --- | --- |
| Endpoint = `IEndpoint<IResult, TRequest, TDeps>` with `AddRoute` using `app.MapGet/MapPost("api/...")`, `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`, `.Produces<T>()`, `.WithTags(...)`; auto-registered by `AddEndpoints()`/`MapEndpoints()` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs`, list: `CatalogItemListPagedEndpoint.cs` |
| Request/response classes in sibling files `X.XRequest.cs` / `X.XResponse.cs`, deriving `BaseRequest`/`BaseResponse` (CorrelationId) | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.CreateCatalogItemRequest.cs` |
| Error boundary: `ExceptionMiddleware` maps `DuplicateException` -> 409, everything else -> 500 `ErrorDetails` | `src/PublicApi/Middleware/ExceptionMiddleware.cs`, `src/ApplicationCore/Exceptions/` |
| Records live in EF `CatalogContext` (DbSet + `IEntityTypeConfiguration<T>` in `Data/Config`, picked up by `ApplyConfigurationsFromAssembly`); in-memory vs SQL chosen by `UseOnlyInMemoryDatabase` | `src/Infrastructure/Data/CatalogContext.cs`, `src/Infrastructure/Data/Config/BasketConfiguration.cs`, `src/Infrastructure/Dependencies.cs` |
| Services behind `ApplicationCore/Interfaces`, implemented in Infrastructure, registered in `PublicApi/Program.cs` | `Program.cs` (`ITokenClaimsService`) |
| JWT identity: the token carries ONLY `ClaimTypes.Name` (username) and `ClaimTypes.Role` (read in `src/Infrastructure/Identity/IdentityTokenClaimService.cs`). No user-id, email or name claim exists. Caller identity = `ClaimTypes.Name`. | `IdentityTokenClaimService.cs`; consumer pattern `src/Web/Controllers/OrderController.cs` (`User.Identity.Name`) |
| Tests: MSTest + `WebApplicationFactory<Program>` with `appsettings.test.json` (`UseOnlyInMemoryDatabase: true`) and `ApiTokenHelper`; unit tests MSTest + NSubstitute | `tests/PublicApiIntegrationTests/ProgramTest.cs`, `ApiTokenHelper.cs`, `tests/UnitTests/` |

Layering decision (YOUR CALL, source: this survey): interface `ISubscriptionService` + domain DTOs/exceptions in `ApplicationCore`; claim entities in `ApplicationCore/Entities` and EF configs/DbSets on `CatalogContext`; the Maxio-backed implementation, `MaxioOptions`, options validator and an `AddMaxioBilling(IConfiguration)` extension in `Infrastructure` (ProjectReference to the SDK csproj goes on `Infrastructure.csproj`; `dotnet add reference <plugin-root>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`; this reference is machine-local). `AddMaxioBilling` is called only from `PublicApi/Program.cs`, NOT from `Dependencies.ConfigureServices` (Web also calls that and must not demand Maxio config).

Steps (in order):

1. Config + client: `Maxio:` section bind, fail-fast validation, DI client registration. (no operation)
2. Claim entities (`SubscriptionClaim`, `CustomerLink`) + EF config + DbSets. (no operation)
3. GET `/api/subscription-plans`: uses `client.ProductFamilies.ListProductsForProductFamily` with `ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`. Returns plans (handle, name, price cents, interval, intervalUnit) plus `isPartial`.
4. POST `/api/subscriptions` body `{ planHandle }`: (a) validate planHandle against step-3 list (invariant), (b) claim subscription key (userKey + planHandle), (c) ensure customer: claim `CustomerLink`, `ReadCustomerByReference` then `CreateCustomer` if absent, (d) pre-write provider check `ListCustomerSubscriptions` for a subscription already carrying our reference, (e) `CreateSubscription`, (f) mark claim Completed with subscription id and return plan/price/state/next-billing date from the response.
5. GET `/api/my-subscriptions`: `ReadCustomerByReference` (reference derived from token name; 404 -> empty list) then `ListCustomerSubscriptions`; project to plan/price/state/next billing date. Provider is the system of record, so the endpoint does not depend on the in-memory link table surviving a restart.
6. Error mapping, unknown-outcome settle path, logging.
7. Tests (unit with HttpClient stub seam; integration with `WebApplicationFactory`). Add DUMMY non-secret `Maxio:*` values to the test host config (`tests/PublicApiIntegrationTests/appsettings.test.json` and the FunctionalTests PublicApi host config), otherwise fail-fast validation stops every existing PublicApi test host at startup.
8. Secrets: values loaded with `dotnet user-secrets set` into the existing `UserSecretsId` of `PublicApi.csproj`; nothing written to the repo.

Operations used: `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription`, `ProductFamilies.ListProductsForProductFamily`.

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All operations: `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default` follow the request record. Throw-only, no `...Result` variants, no `Pageable` (map defaults), server group `Production`, auth `options.BasicAuth`.

| Controller property | Method signature | Request record (members) | Body model (wire) | Response envelope + fields read | Error case / accessors / payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` (`MaxioAdvancedBilling.Api`) | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, ...)` | `ProductFamilyId: string, required` (doc: id OR handle prefixed `handle:`); `Page: int = 1`; `PerPage: int = 20` (max 200); `IncludeArchived: bool?`; others (`Filter`, dates, `Include`) left out | none | `ProductResponse.Product` (required) -> `MaxioAdvancedBilling.Models.Product`: `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `TrialInterval (trial_interval): int?`, `InitialChargeInCents (initial_charge_in_cents): long?`, `RequireCreditCard (require_credit_card): bool?` | Case A `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` (ns `MaxioAdvancedBilling.Core.Exceptions`); `TryGetString(out string)` [404] ; `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | No `Pageable`: the request has `Page`/`PerPage` query params; page by hand, stop on a page shorter than `PerPage`, carry a page cap | `ops/ProductFamilies`; `src/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `src/Models/Product.cs`, `ProductResponse.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, ...)` | `Reference: string, required` | none | `CustomerResponse.Customer` (required) -> `Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | Case B `ApiException<RawError>`; `StatusCode`, `ReadAsString()`. A missing reference is expected as `StatusCode == 404` (UNVERIFIED) | none | `ops/Customers`; `src/Requests/Customers/ReadCustomerByReferenceRequest.cs`; `src/Models/CustomerResponse.cs`, `Customer.cs` |
| `client.Customers` | `Task<CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateCustomerRequest?` (optional on record, send it) | `CreateCustomerRequest.Customer (customer): CreateCustomer, required`; `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (set to the deterministic user reference: real data key, see 5); not set: address/phone/org/tax fields | `CustomerResponse.Customer.Id` (int?) is stored in `CustomerLink` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422]; `TryGetRawError` [fallback] | none | `ops/Customers`; `src/Models/CreateCustomerRequest.cs`, `CreateCustomer.cs` |
| `client.Customers` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>> ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, ...)` | `CustomerId: int, required` | none | each `SubscriptionResponse.Subscription` (`Subscription?`, nullable, skip nulls): see CreateSubscription row | Case B `ApiException<RawError>` | none (no page params on the record) | `ops/Customers`; `src/Requests/Customers/ListCustomerSubscriptionsRequest.cs` |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest?` | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required`; `CreateSubscription` sets: `ProductHandle (product_handle): string?` (the plan handle; doc: required unless `product_id`), `CustomerId (customer_id): int?` (doc: required unless `customer_reference`/`customer_attributes`), `Reference (reference): string?` (our deterministic subscription key). Left out on purpose: `ProductPricePointHandle/Id` (plan default price point applies), `PaymentProfile*/CreditCardAttributes/BankAccountAttributes` (task: payment method not required), `Components` (metered `api-call` not subscribed at signup), coupons, `NextBillingAt`/`InitialBillingAt`/`DeferSignup` (would change state/charge timing), `PaymentCollectionMethod` left out in the first plan, CORRECTED after live traffic: the sandbox answered 422 "No payment method was on file for the $29.00 balance" for a card-less automatic-collection signup, so `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` is now set to `CollectionMethod.Remittance` (constant `SignupCollectionMethod` in the service; verified: 201, state `active`) | `SubscriptionResponse.Subscription (subscription): Subscription?` -> `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Reference (reference): string?`, `Product (product): Product?` (Handle, Name, `ProductFamily (product_family): ProductFamily?` with `Handle`), `Customer (customer): Customer?`, `CreatedAt (created_at)` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422]; `TryGetRawError` [fallback] | none | `ops/Subscriptions`; `src/Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `src/Models/CreateSubscriptionRequest.cs`, `CreateSubscription.cs`, `SubscriptionResponse.cs`, `Subscription.cs` |

Wire facts from `<remarks>` / source: `CreateSubscription` is `POST /subscriptions.json` and sends `Idempotency-Key` = fresh `Guid.NewGuid()` (not a key). Both writes are POST, so the default `HttpMethodsToRetry` (GET, HEAD, PUT, OPTIONS) never resends them. Payment information "may be required ... depending on the options for the Product"; for these two plans the task said it is not; VERIFIED FALSE against the sandbox for automatic collection (422), and `Remittance` collection signs up without a card (see the CreateSubscription row).

Neither `CreateCustomer` nor `CreateSubscription` has a key-shaped member in its request record or body that the provider is documented to deduplicate on; `Reference` is a plain data field (uniqueness enforcement UNVERIFIED, max length UNVERIFIED).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `planHandle` a caller subscribes to must be one of the plans this app offers, i.e. non-archived products returned for the configured product family | `CreateSubscription.ProductHandle` <- `ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`) | `SubscriptionService.SubscribeAsync`, before the claim is taken and before any `CreateSubscription`/`CreateCustomer` call; a handle not in the list -> 422 `unknown_plan`; if the list is partial (page cap hit) and the handle is absent, -> 503, not 422. The plan-list read is the same member that backs GET `/api/subscription-plans` |
| The Maxio customer id used for `CreateSubscription.CustomerId` must be the one for the token's user, never caller-supplied | `CreateSubscription.CustomerId` <- `CreateCustomer` result / `ReadCustomerByReference` result (stored in `CustomerLink`) | `SubscriptionService.EnsureCustomerAsync`, before `CreateSubscription`; the POST body carries no customer field |
| `my-subscriptions` shows only subscriptions of the token's own customer | `ListCustomerSubscriptions.CustomerId` <- `ReadCustomerByReference` (reference from token name) | `SubscriptionService.ListMineAsync`, before `ListCustomerSubscriptions` |

### Enum tables needed

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`src/Models/Enums/SubscriptionState.cs`, open string enum, static members; branch with `TryGetKnownValue`/`Match`, `otherwise` arm for unknown): `Pending pending`, `FailedToCreate failed_to_create`, `Trialing trialing`, `Assessing assessing`, `Active active`, `SoftFailure soft_failure`, `PastDue past_due`, `Suspended suspended`, `Canceled canceled`, `Expired expired`, `Paused paused`, `Unpaid unpaid`, `TrialEnded trial_ended`, `OnHold on_hold`, `AwaitingSignup awaiting_signup`. The API returns the wire string to the user (open-enum safe).
`MaxioAdvancedBilling.Models.Enums.IntervalUnit`: value members read from `src/Models/Enums/IntervalUnit.cs` at implementation time; the plan API returns its wire string.

### Client construction / auth / server facts

- Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`; DI: `services.AddMaxioAdvancedBillingClient(options => ...)` (`map` "Getting a client").
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (`map` Servers & auth: username is the API key, password is `x`).
- Environment: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) or `.Eu`; operations here use server group `Production` only. No sandbox member exists.
- Base address: `Maxio:Subdomain` -> `options.Server.Production.Us.Site` (template `https://{site}.chargify.com`). If `Maxio:BaseUrl` is set -> `options.Server.Production.Us.BaseUrl = <value>` verbatim (a literal URL with no placeholders is used as-is; per `dotnet-configuration-resilience`). Configure `Server` before constructing the client; set it on the SAME environment that `options.Environment` selects (`Us`).
- Retry: `MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with { ... }` (all members `required`).

## 3. Trap notes

| Step | Hazard | Pointer |
| --- | --- | --- |
| 1 | Env-var names, user-secrets loading environment and `BaseUrl` vs `Site` vs `Environment` interact so that a value can be silently ignored (some option edits after construction have no effect) | MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 1 | Credential property, what to check for a multi-part credential, and which failures look like 401/403 | MUST load maxio-advanced-billing:dotnet-authentication |
| 1 | Who owns the `HttpClient` and the client's lifetime under DI, and when options are read | MUST load maxio-advanced-billing:dotnet-client-initialization |
| 3, 4, 5 | Which `Timeout` bounds what, and which calls the SDK resends on its own; a default left in place holds a request open for minutes | MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 3 | The product list has page params but no `Pageable`; an unbounded loop or silent truncation yields a wrong answer | MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 4 | A transport failure or malformed 2xx on a POST leaves the write in an unknown state; reporting a failure there is wrong | MUST load maxio-advanced-billing:dotnet-error-handling and dotnet-configuration-resilience |
| 4, 5 | Request bodies/records: nullable members, open enums, union types and wire names vs C# names | MUST load maxio-advanced-billing:dotnet-models |
| 3-5 | Request-record shape, return envelopes, cancellation token placement | MUST load maxio-advanced-billing:dotnet-calling-endpoints |
| 4 | `CreateCustomer` carries personal data; the SDK's body logging and `_LOG` env var can write it verbatim | MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 7 | Stubbing the `HttpClient`, building SDK exceptions, asserting the outgoing request | MUST load maxio-advanced-billing:dotnet-testing |

## 4. REQUIRED READING

Load all before implementation starts (plugin-qualified):

- `maxio-advanced-billing:dotnet-error-handling` · every `try/catch` around SDK calls and the HTTP error mapping (always listed)
- `maxio-advanced-billing:dotnet-getting-started` · contract facts, idempotency, sensitive-data sections
- `maxio-advanced-billing:dotnet-client-initialization` · step 1 DI registration
- `maxio-advanced-billing:dotnet-authentication` · step 1 Basic credentials and fail-fast check
- `maxio-advanced-billing:dotnet-configuration-resilience` · timeouts, retries, base URL, paging, logging, unknown outcomes, duplicate claims
- `maxio-advanced-billing:dotnet-calling-endpoints` · steps 3-5
- `maxio-advanced-billing:dotnet-models` · steps 3-5
- `maxio-advanced-billing:dotnet-testing` · step 7

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioOptions` bound from section `Maxio` (keys `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`). An `IValidateOptions<MaxioOptions>` with `ValidateOnStart()` in `AddMaxioBilling`: host refuses to start when `ApiKey`, `Subdomain` (unless `BaseUrl` is set and a valid absolute URL) or `ProductFamilyHandle` is null/empty/whitespace; `BaseUrl`, when present, must be an absolute http(s) URI. Checks every part: Username (ApiKey) and, since the password is the fixed `x`, nothing else; `ProductFamilyHandle` blank would make plan listing meaningless so it is also required. Source: YOUR CALL — not in the map; credential shape from `map` Servers & auth. |
| 2 | Secret sourcing & rotation | Local dev: `dotnet user-secrets set "Maxio:ApiKey" ...` etc. into the existing `UserSecretsId` in `PublicApi.csproj` (auto-loaded only in the Development environment; run Development or add `AddUserSecrets<Program>()` explicitly). Env names map onto keys in `Program.cs` with a small in-memory configuration source: `MAXIO_API_KEY`->`Maxio:ApiKey`, `MAXIO_SITE_SUBDOMAIN`->`Maxio:Subdomain`, `MAXIO_DEFAULT_PRODUCT_FAMILY`->`Maxio:ProductFamilyHandle` (.NET's own env provider would only read `Maxio__ApiKey`); precedence: the mapped env names are the lowest of the three, user-secrets and explicit `Maxio__*` win. `MAXIO_ENVIRONMENT` (sandbox) binds to no key (keys are exactly the four), it is only logged at startup. The client options are built ONCE at registration and captured by the singleton; a rotated key takes effect on process restart only. Rotation without restart is not required. Nothing secret is written to the repo; test hosts use obvious dummy values. |
| 3 | Total timeout budget | Numbers are constants in the service (a fifth config key is not allowed): per-attempt `options.Retry.Timeout` = 10 s; `HttpClient.Timeout` = 10 s backstop; reads (`GET`s) `MaxRetries = 2`; writes are never resent by the SDK. Every operation goes through ONE `Bounded(...)` helper that links `HttpContext.RequestAborted` and cancels after 25 s for the GET paths; the subscribe path gets 40 s for the whole request (customer ensure + pre-check read + create). The unknown-outcome reconcile read in a `catch` uses its own fresh 10 s token (the request's token may be spent). Worst case for a GET: 3 attempts x 10 s + 1 s + 2 s backoff = 33 s, so the 25 s total wins. Source: YOUR CALL numbers; mechanics from `dotnet-configuration-resilience`. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POST: the SDK never resends them with the default `HttpMethodsToRetry`; the plan keeps that list at its default (no widening) so the count of provider writes per claim stays at one. `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductsForProductFamily` are GET and may be resent by the SDK (harmless). Application-level resend of a POST happens only through the reconcile path, which re-reads first. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no provider key; real data key = `Customer.Reference` = `eshop-user:{userKey}` (userKey = `ClaimTypes.Name`), used for reconciliation via `ReadCustomerByReference`. `CreateSubscription`: no provider key; `Subscription.Reference` = `eshop-sub:{userKey}:{planHandle}`, reconciled via `ListCustomerSubscriptions`. The injected `Idempotency-Key` header is NOT used as a key. |
| 6 | Observability | `Information`: request accepted (correlation id from `BaseRequest.CorrelationId()`, userKey, plan handle, claim key), claim outcomes (new / duplicate / released), reconcile result, success with Maxio customer/subscription ids. `Warning`: claim in progress/stale, partial plan list, retry notices (SDK logger). `Error`: provider failures with operation name, HTTP status from `ApiException.StatusCode`, and for Case A the 422 error text; the provider's own correlation/request id header is not declared in the map (UNVERIFIED), so log any of `Request-Id`/`X-Request-Id`-style entries present in `ApiException.Headers` when found, plus our correlation id on every line. The SDK's built-in logger gets an explicit `LoggerFactory` from DI. Never log the API key or request bodies. |
| 7 | Sensitive data | `CreateCustomer` body carries name and email (personal data). `LogRequestBody = false`, `LogRequestHeaders/LogResponseHeaders = false`, `options.Logging.LoggerFactory` assigned explicitly (so `MAXIOADVANCEDBILLINGCLIENT_LOG=trace` cannot enable body logging), own logs print user key only (which is the user's email in eShop seed data: log it at Information only as the claim key, never email/name fields separately). No form-body keys apply (JSON bodies only). |
| 8 | Environment selection | Server groups in the map: `Production` (`https://{site}.chargify.com` Us / `https://{site}.ebilling.maxio.com` Eu) and `Ebb` (events). This scope touches `Production` only; `Ebb` is not used (no events/metered component write). Deployment sets `Maxio:Subdomain` (sandbox site) with `ServerEnvironment.Us`; the SDK declares NO sandbox environment, so test traffic is kept away from a live system purely by pointing `Subdomain` (or `BaseUrl`) at the sandbox site `<sandbox-site>`; whether that site resolves under `chargify.com` is UNVERIFIED, which is why `Maxio:BaseUrl` verbatim override exists. `Eu` is used only if a later deployment sets it (not bound by the four required keys: out of scope, Us fixed). |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. The in-memory store loses claims on restart (user-stated); the post-claim provider pre-check by `Reference` (4d) covers that case for subscriptions and `ReadCustomerByReference` for customers. SQL Server mode would persist claims, but no EF migration is added (in-memory is the target); mention in handoff. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

Error mapping to HTTP (single translation layer in the service/endpoint, extending the `ExceptionMiddleware` pattern with new domain exceptions mapped there; YOUR CALL placement):

| Condition | HTTP |
| --- | --- |
| Missing/invalid JWT | 401 (framework, `[Authorize]` with JwtBearer scheme) |
| Body missing/blank `planHandle` | 400 |
| `planHandle` not in offered plans | 422 `unknown_plan` |
| Token name not usable as an email for the Maxio customer (see Assumptions) | 422 |
| Claim already exists and Completed | 200 with the existing subscription (idempotent replay) |
| Claim exists and in progress (Pending, not stale) | 409 with `Retry-After` |
| Maxio 422 (`CreateCustomerError` / `CreateSubscriptionError` typed body) | 422 with sanitized provider message; claim released |
| Maxio 401/403 (Case B/fallback RawError) | 502 `billing_misconfigured` (no provider detail to caller); logged Error |
| Maxio 404 on product family (`TryGetString`) | 503 `catalog_unavailable` (config problem) |
| Maxio 429 or 5xx after SDK retries | 503 |
| `SdkTimeoutException` / `SdkConnectionException` / `ResponseDeserializationException` on a read | 504 / 502 / 502 |
| Same three on a write | reconcile first (UNKNOWN OUTCOMES); if settled -> normal 201/200; if still unknown -> 202 with state `Unknown`, claim kept |
| Caller disconnect (`OperationCanceledException` on `RequestAborted`) | no response; claim left for the reconcile path if the write was in flight |
| Anything else | 500 (existing middleware) |

Success codes: POST new -> 201 with `Location` `api/my-subscriptions`; response body `{ subscriptionId, planHandle, planName, priceInCents, interval, intervalUnit, state, nextBillingAt }`.

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (and with it the whole subscribe) | `SubscriptionClaim` rows (string PK `ClaimKey` = `{userKey}\|{planHandle}`, plus `Status` Pending/Completed/Unknown, `ClaimedAt`, `MaxioSubscriptionId?`) in `CatalogContext` (the app's EF DbContext) | The primary key: insert of the same `ClaimKey` is refused by the in-memory provider (and SQL) | `catch (Exception) when (DbUpdateException or InvalidOperationException or ArgumentException)` (the EF in-memory provider raises a raw `ArgumentException` for a duplicate key; verified live) around the claim `SaveChangesAsync`, in the claim helper, which then loads the existing row (Completed -> replay, Pending -> 409, stale/Unknown -> reconcile) | `MaxioSubscriptionService.TryClaimSubscriptionAsync` (`_db.SubscriptionClaims.Add` + `SaveChangesAsync`), then `MaxioSubscriptionService.ExecuteSubscribeAsync` -> `Run("CreateSubscription")` |
| `CreateCustomer` | `CustomerLink` rows (string PK `UserKey`, `Status`, `MaxioCustomerId?`, `ClaimedAt`) in `CatalogContext` | The primary key on `UserKey` | `catch (DbUpdateException)` (plus in-memory `InvalidOperationException`) around the `CustomerLink` insert; loser reads provider via `ReadCustomerByReference`: found -> uses that id; not found -> 409 customer provisioning in progress | `MaxioSubscriptionService.EnsureCustomerAsync` (`_db.CustomerLinks.Add` + `SaveChangesAsync` with the same catch), then `Run("CreateCustomer")` in the same method |

Order in both rows: claim -> SDK call -> record the result; a provider refusal (422, definite) deletes the claim row (release); a stale Pending claim (older than 5 min, constant) is treated as Unknown and reconciled. The claim key is deterministic from token name + plan, never `Guid.NewGuid()`. The subscribe claim is taken first, then the customer claim, so a double-click of the same plan is stopped at the first claim; two different plans racing for the same new user meet at the `CustomerLink` PK.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` for the configured family (plans) | `PerPage = 200` (provider max) and a page cap of 5 (constant, = 1000 products) | the plans response carries `isPartial: bool` (true when the cap hit with a full last page); the subscribe invariant check returns 503 rather than 422 when partial and the handle is absent | `MaxioSubscriptionService.ListPlansCoreAsync` sets `PlanList.IsPartial`; `ListSubscriptionPlansEndpoint.HandleAsync` copies it to `ListSubscriptionPlansResponse.IsPartial`; `SubscribeAsync` maps partial + absent handle to `SubscriptionFailure.Unavailable` (503) |
| `ListCustomerSubscriptions` | none (no page params on the record; single list) | N/A: not paged in this SDK | N/A |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Customer.Reference` = `eshop-user:{userKey}` | `MaxioSubscriptionService.EnsureCustomerAsync`: `catch (SubscriptionException ex) when (ex.OutcomeUnknown)` around `Run("CreateCustomer")` re-reads with `ReadCustomerIdAsync` on a fresh 10 s token; found -> `CompleteCustomerLinkAsync`; not found -> `MarkCustomerLinkUnknownAsync` + `CustomerOutcomeUnknownException` (caught in `ExecuteSubscribeAsync`, result 202 Unknown); an Unknown link is re-settled by the loser/leftover branch of `EnsureCustomerAsync` on the next request | `MaxioSubscriptionServiceTests.Subscribe_CreateCustomerConnectionFails_ReconcilesByReferenceAndContinues`, `Subscribe_CreateCustomerConnectionFails_NothingFound_RecordsUnknownAndSkipsTheSubscriptionWrite` (stub throws `HttpRequestException`) |
| `CreateSubscription` | `Customers.ListCustomerSubscriptions` (by the stored `CustomerId`) | `Subscription.Reference` = `eshop-sub:{userKey}:{planHandle}` | `MaxioSubscriptionService.ExecuteSubscribeAsync`: `catch (SubscriptionException ex) when (writeStarted && ex.OutcomeUnknown)` -> `ReconcileAfterUnknownSubscriptionWriteAsync` (fresh 10 s token, `ListSubscriptionsAsync` + `FindByReference`): found -> `CompleteClaimAsync` (201); not found after an answered 5xx -> `ReleaseClaimAsync` + 503; otherwise `MarkClaimUnknownAsync` (202 Unknown). Stale Pending / Unknown claims are settled in `SubscribeAsync` (switch default branch) and the pre-write check is in `ExecuteSubscribeAsync` before `Run("CreateSubscription")` | `MaxioSubscriptionServiceTests.Subscribe_CreateSubscriptionConnectionFails_ReconcilesAndFindsTheWriteThatLanded`, `..._NothingFound_RecordsUnknownAndNeverResends`, `Subscribe_CreateSubscriptionUnreadable2xx_IsTreatedAsUnknownAndReconciled`, `Subscribe_CreateSubscription503_NothingFound_ReleasesClaimAndReports503`, `Subscribe_StalePendingClaim_IsReconciledAgainstTheProvider` (stub throws `HttpRequestException`) |

If a reconcile finds a match, the claim becomes Completed with that subscription id; no match and the re-read succeeded after an explicit transport failure leaves it Unknown (not released: the write may land late), and the response is 202.

## 6. Assumptions & Blockers

**Blockers** (need the user's answer before or during implementation):

1. Runtime/TFM mismatch: the repo targets `net8.0` (`Directory.Packages.props`, `global.json` pins `8.0.x`) but this machine has only the .NET 9 and 10 runtimes/SDKs (`dotnet --list-runtimes`: 9.0.19, 10.0.11; `dotnet --version` in the repo reports 10.0.400). Running or testing `net8.0` apps will fail without roll-forward (`DOTNET_ROLL_FORWARD=Major`) or a retarget, and restoring a net8 targeting pack needs NuGet network access. The SDK itself is `netstandard2.0` with C# `LangVersion 14`, which needs the .NET 10 SDK compiler (present). Recommended default: do not change TFM; run and test with `DOTNET_ROLL_FORWARD=Major`; confirm.
2. Token has no email/first/last name (only `ClaimTypes.Name` and roles), but `CreateCustomer` requires `FirstName`, `LastName`, `Email`. Plan default: use the username as email when it contains `@` (eShop usernames are emails), first name = part before `@`, last name = fixed placeholder `eShopOnWeb User`; otherwise 422. Confirm, or approve looking up `ApplicationUser` in the identity DB.

**Resolved by the main agent before implementation**: (1) kept net8 TFM, run/test with `DOTNET_ROLL_FORWARD=Major`, `global.json` rollForward set to `latestMajor`; (2) customer email = lower-cased username when it is a valid address, first name = local part, last name `eShopOnWeb User`, otherwise 422; omitted `planHandle` -> 400. Implementation findings: the sandbox rejects card-less AUTOMATIC collection (422); `PaymentCollectionMethod = Remittance` is set (product decision to confirm); the net8 TestServer cannot write 2xx JSON bodies on the installed .NET 9/10 runtimes (12 of 15 pre-existing PublicApiIntegrationTests fail identically before and after this work), so success-body tests run the endpoint handlers directly.

**Assumptions**:

- POST `/api/subscriptions` body requires `planHandle`. The task names `eshop-pro` as the "default" target but also forbids hardcoding handles and allows exactly four config keys, so the default lives in the client (UI/caller), not the server. An omitted handle -> 400.
- Claim granularity = (user, plan): a second request for the same user and plan is a replay (200, existing subscription); the same user subscribing to a different plan is allowed (second subscription). Confirm if one active subscription per user is wanted instead.
- Plans offered = non-archived products (`ArchivedAt == null`, `IncludeArchived` not set) of the family `handle:{Maxio:ProductFamilyHandle}`; no plan handle or price is hardcoded; price shown is `Product.PriceInCents`, next billing date is `Subscription.NextAssessmentAt`.
- `my-subscriptions` is read from the provider through the token-derived customer reference, filtered to subscriptions whose `Product.ProductFamily.Handle` equals the configured handle when that field is populated (UNVERIFIED that it is). It does not depend on the in-memory link table.
- Provider behaviours that only live traffic can confirm (UNVERIFIED): 404 for a missing customer reference; uniqueness and max length of `Reference` for customers and subscriptions; payment profile not required on these plans (CORRECTED: required for automatic collection, not for remittance, verified live); sandbox site `<sandbox-site>` resolving at `https://<sandbox-site>.chargify.com`; availability of a request-id response header.
- No EF migration is created (in-memory DB is the target); SQL Server mode would need one.
- `MAXIO_ENVIRONMENT` is informational only; the SDK has no sandbox environment.
- No plugin capability is missing for this scope.
