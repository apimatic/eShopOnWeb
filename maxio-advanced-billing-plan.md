# Maxio Advanced Billing recurring subscriptions — implementation plan

## Scope & sequence

| # | Implementation step | Operations / repository pattern |
| --- | --- | --- |
| 1 | Add the plugin-local SDK project reference to `src/PublicApi/PublicApi.csproj`; create a `MaxioAdvancedBilling` options type bound to exactly `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`; populate the first three through the PublicApi user-secrets ID / deployment environment variables, with `BaseUrl` optional. Register a named `HttpClient` and singleton SDK client, validate nonblank required settings at host start, and enable `UseAuthentication()` before `UseAuthorization()`. | Client construction / Basic auth |
| 2 | Make the JWT carry the stable `ApplicationUser.Id` as `ClaimTypes.NameIdentifier`; use it, not a route/body user id, for all three endpoints. Create an EF-owned subscription-operation claim aggregate/table and migration in `CatalogContext`, plus a unique primary/alternate key that atomically rejects a duplicate customer or subscription claim. This ledger is only an idempotency/reconciliation record; Maxio remains the subscription system of record. | Existing `CatalogContext` + `IRepository<T>` / EF configuration pattern |
| 3 | Add authenticated `GET /api/subscription-plans`. Resolve the configured family by listing families and matching `ProductFamily.Handle`; then list its products using `ProductFamilyId = "handle:{ProductFamilyHandle}"`. Return only non-archived plans, keyed by `Product.Handle`, with name, description, price-in-cents, interval, and currency where returned. Bound the page-number loop and report truncation in the response. | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | Add authenticated `POST /api/subscriptions` whose body accepts a product handle only (never a Maxio numeric ID). Before the write, list the configured family's products and reject a handle not returned by that call. Atomically claim `(userId, productHandle)`; ensure the customer with the deterministic reference `eshop:{userId}` by read-by-reference then create-on-404; create the subscription using the selected product handle, customer ID, and deterministic subscription reference from the claim. Record the returned Maxio IDs/reference, then return a DTO mapped from Maxio's subscription response. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` |
| 5 | Add authenticated `GET /api/my-subscriptions`. Resolve the same deterministic Maxio customer reference, then list that customer's subscriptions; map plan (`Product.Handle`/`Product.Name`), price (`ProductPriceInCents`), state (`State`), and next billing (`NextAssessmentAt`) directly from Maxio. A customer not found returns an empty list, not a locally reconstructed history. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Add unit/integration tests using the repository's MSTest/PublicApi patterns and the SDK `HttpMessageHandler` seam: auth, plan-family isolation and invalid-handle rejection, create customer/subscription payloads, duplicate claim conflict/replay, 404 customer create path, provider 422 mapping, connection-loss reconciliation, response mapping, config startup validation, and bounded list behavior. Run restore/build/test, then sandbox live verification with a dedicated test user/product family and inspect the first SDK request/response log line. | `tests/PublicApiIntegrationTests`; SDK HTTP stub seam |

## CONTRACT SHEET

WARNING: signatures are generated code: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.

WARNING: every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Operation (controller property) | Method signature | Request record + members | Body model + fields used | Response envelope + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ProductFamilies.ListProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductFamiliesRequest request, RequestOptions? = null, CancellationToken = default)` | optional `DateField`, `StartDate`, `EndDate`, `StartDatetime`, `EndDatetime`; pass empty record | none | `IReadOnlyList<ProductFamilyResponse>` → optional `ProductFamily` → `Handle`, `Id`, `ArchivedAt` | B: `ApiException<RawError>`; status/read accessors | SDK map default: none | `map/operations/ProductFamilies.md`; `Models/ProductFamilyResponse.cs`, `Models/ProductFamily.cs`, `Requests/ProductFamilies/ListProductFamiliesRequest.cs` |
| `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, RequestOptions? = null, CancellationToken = default)` | `ProductFamilyId: string, required`; optional `Page` (default 1), `PerPage` (default 20/max 200), filters, `IncludeArchived`, `Include` | none | `IReadOnlyList<ProductResponse>` → required `Product` → `Handle`, `Name`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `ProductPricePointHandle` | A: `ApiException<ListProductsForProductFamilyError>`; `TryGetString(out string)` 404, `TryGetRawError(out RawError)` fallback | SDK map default says none, despite explicit page/per-page request controls; implementation must loop page numbers until a short/empty page and expose truncation after its application cap | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, RequestOptions? = null, CancellationToken = default)` | `Reference: string, required` | none | `CustomerResponse` → required `Customer` → `Id`, `Reference`, `Email`, `FirstName`, `LastName` | B: `ApiException<RawError>`; status/read accessors | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `Customers.CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, RequestOptions? = null, CancellationToken = default)` | optional `Body: CreateCustomerRequest?` (must be supplied) | `CreateCustomerRequest.Customer (customer): CreateCustomer, required`; `CreateCustomer.FirstName (first_name)`, `LastName (last_name)`, `Email (email)` required; set `Reference (reference)` to deterministic `eshop:{userId}` | `CustomerResponse` → required `Customer` → `Id`, `Reference` | A: `ApiException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` 422, raw fallback | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `Subscriptions.CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, RequestOptions? = null, CancellationToken = default)` | optional `Body: CreateSubscriptionRequest?` (must be supplied) | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required`; set `ProductHandle (product_handle)` and `CustomerId (customer_id)` (each conditionally required by source prose) plus deterministic `Reference (reference)`. Do not set `ProductId`, `CustomerReference`, inline `CustomerAttributes`, custom price, or payment data for this scope. | `SubscriptionResponse` → optional `Subscription` → `Id`, `Reference`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `Product` | A: `ApiException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` 422, raw fallback | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| `Subscriptions.FindSubscription` | `FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, RequestOptions? = null, CancellationToken = default)` | optional `Reference: string?`; pass stored deterministic subscription reference | none | `SubscriptionResponse` → optional `Subscription` → same fields as create/list mapping | A: `ApiException<FindSubscriptionError>`; `TryGetNoContent(out RawError)` 404, raw fallback | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, RequestOptions? = null, CancellationToken = default)` | `CustomerId: int, required` | none | `IReadOnlyList<SubscriptionResponse>` → optional `Subscription` → `Id`, `Reference`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `Product.Handle`, `Product.Name` | B: `ApiException<RawError>`; status/read accessors | none | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs`, `Models/Product.cs` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `productHandle` accepted by `POST /api/subscriptions` must be a `Product.Handle` returned for the configured family; numeric product IDs are never accepted. | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` | Subscription endpoint/service validates the submitted handle against the complete, bounded family product list before the claim and write. |
| The customer ID used for subscription creation must be the ID returned for the authenticated user's deterministic reference. | `Subscriptions.CreateSubscription` ← `Customers.ReadCustomerByReference` / `Customers.CreateCustomer` | Customer-ensure service validates non-null returned `Customer.Id` before `CreateSubscription`. |

### Client construction, authentication, and server facts

| Fact | Decision / exact contract | Source |
| --- | --- | --- |
| SDK client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; package is plugin-local `sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`, not NuGet. | `sdk-map.md`; `dotnet-getting-started` |
| Auth | Basic auth: `BasicAuthCredentials.Username = ApiKey`, `Password = "x"`; all scoped calls require it. | `sdk-map.md` Servers & auth; `dotnet-authentication` |
| Environment/server | Scoped operations use `Production`; `ServerEnvironment.Us` default or `ServerEnvironment.Eu`; set configured subdomain on the selected `options.Server.Production.{Us|Eu}.Site`. If nonblank, `BaseUrl` overrides the selected `options.Server.Production.{Us|Eu}.BaseUrl`. | `sdk-map.md` Servers & auth; `dotnet-configuration-resilience` |
| Namespace rule | Use fully qualified source-derived namespaces: requests are `MaxioAdvancedBilling.Requests.{Controller}`, models `MaxioAdvancedBilling.Models`, errors `MaxioAdvancedBilling.Errors`, exceptions `MaxioAdvancedBilling.Core.Exceptions`, raw errors `MaxioAdvancedBilling.Core.ErrorResponse`. | `sdk-map.md` Namespaces by content type |

## Trap notes

| Step | Hazard |
| --- | --- |
| 1 | Missing Basic-auth values otherwise result in an unauthenticated request and late 401 rather than a startup failure. **MUST load maxio-advanced-billing:dotnet-authentication** |
| 1 | SDK client lifetime, named HTTP pipeline, DNS refresh, and options capture have non-obvious ownership effects. **MUST load maxio-advanced-billing:dotnet-client-initialization** |
| 1, 3–5 | Environment selection, BaseUrl override, request-wide deadline, retry verb policy, logging/redaction, and manual page cap differ from intuitive defaults. **MUST load maxio-advanced-billing:dotnet-configuration-resilience** |
| 3–5 | Generated calls require one request record, body envelopes are separate models, and cancellation is the third/named parameter. **MUST load maxio-advanced-billing:dotnet-calling-endpoints** |
| 3–5 | Nullable envelope members, open enums, wire names, and model extension data must be handled without silently fabricating billing values. **MUST load maxio-advanced-billing:dotnet-models** |
| 4–5 | Scoped operations mix typed and raw API exceptions; malformed success/error bodies surface through a separate exception path. **MUST load maxio-advanced-billing:dotnet-error-handling** |
| 6 | The SDK test seam is its injected `HttpClient`; retries and connection loss require behavior-level assertions. **MUST load maxio-advanced-billing:dotnet-testing** |

## REQUIRED READING

- maxio-advanced-billing:dotnet-authentication · step 1 credentials and startup validation
- maxio-advanced-billing:dotnet-client-initialization · step 1 client/DI/HTTP lifetime
- maxio-advanced-billing:dotnet-configuration-resilience · steps 1 and 3–5 timeout, retries, server, logging, pagination
- maxio-advanced-billing:dotnet-calling-endpoints · steps 3–5 generated SDK invocation
- maxio-advanced-billing:dotnet-models · steps 3–5 body and response model mapping
- maxio-advanced-billing:dotnet-error-handling · steps 4–5 error boundary
- maxio-advanced-billing:dotnet-testing · step 6 HTTP stub and failure tests
- A body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `MaxioAdvancedBilling` in PublicApi options and `ValidateOnStart()` nonblank `ApiKey`, `Subdomain`, and `ProductFamilyHandle`; failure names only the missing binding key. `BaseUrl` remains optional. |
| 2 | Secret sourcing & rotation | Store `ApiKey` via `dotnet user-secrets set "MaxioAdvancedBilling:ApiKey"` for the existing PublicApi `UserSecretsId`; use `MaxioAdvancedBilling__ApiKey` in deployments. Client options are built once into the singleton, so rotation requires process restart. |
| 3 | Total timeout budget | Enforce one 30-second linked cancellation-token budget at the Maxio service boundary (also linked to request abort); set named `HttpClient.Timeout` and SDK retry per-attempt timeout to 10 seconds. |
| 4 | Write-retry ownership | Keep SDK defaults that do not retry POST; `CreateCustomer` and `CreateSubscription` are sent once. GET reads retain default retry policy within the service deadline. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer` has no request-record idempotency key; its real provider reconciliation reference is `CreateCustomer.Reference`, re-read using `ReadCustomerByReference`. `CreateSubscription` likewise has no request-record idempotency key; use `CreateSubscription.Reference`, re-read using `FindSubscription`. Do not cite/generated `Idempotency-Key` header as an idempotency key. |
| 6 | Observability | SDK logger uses explicitly assigned host `ILoggerFactory`, request/response bodies and headers stay off. Log operation name, local correlation ID, user ID, Maxio resource/reference IDs, status, and the provider correlation/request ID only if observed from response headers or error body; never log API key, request body, or email. |
| 7 | Sensitive data | This scope sends customer first/last name and email (personal data), though no payment details. `LogRequestBody = false`, explicit `LoggerFactory`, no request-body diagnostics, and no sensitive form fields are introduced. |
| 8 | Environment selection | All scoped calls resolve through `Production`: US `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com`; set `Environment` explicitly per deployment and selected `.Site` from `Subdomain`. Sandbox deployment uses its sandbox site subdomain with the matching US/EU environment; optional `BaseUrl` only supports controlled mock/proxy override. `Ebb` is declared by the SDK but untouched. |
| 9 | Duplicate prevention under concurrency | Use durable EF claims before either Maxio write; direct provider read-before-write alone is insufficient. |
| 10 | Partial results | Plans response includes `IsTruncated` and returns HTTP 206 (or a documented `partial` response field) when its maximum page count is reached; subscription-list operation has no pagination per map. |
| 11 | Unknown outcomes | The write's own connection/timeout catch re-reads by its deterministic provider reference; if absent, preserve a local `Unknown` claim for a background/manual recovery path rather than report a false failure. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | New EF `MaxioOperationClaim` aggregate in `CatalogContext`, primary key `customer:eshop:{userId}` | `DbUpdateException` / tracked primary-key conflict from `SaveChanges` before the SDK call | Customer-ensure service catches the persistence conflict, loads the existing claim, and returns/reconciles its result | `MaxioSubscriptionService.GetOrClaimAsync` → `EnsureCustomerAsync` |
| `Subscriptions.CreateSubscription` | Same EF aggregate, primary key `subscription:{userId}:{productHandle}` | `DbUpdateException` / tracked primary-key conflict from `SaveChanges` before the SDK call | Subscription-create service catches the persistence conflict, loads the existing claim, and returns/reconciles its result | `MaxioSubscriptionService.GetOrClaimAsync` → `SubscribeAsync` |

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | Application `MaxPlanPages` (10) × SDK maximum `PerPage` 200 | `SubscriptionPlansResponse.IsTruncated` and HTTP 206 | `MaxioSubscriptionService.GetPlansAsync` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `Customers.ReadCustomerByReference` | `eshop:{userId}` | `MaxioSubscriptionService.ReconcileCustomerAsync` | Live sandbox validation exercises the normal success path; SDK handler seam remains the failure-path test boundary. |
| `Subscriptions.CreateSubscription` | `Subscriptions.FindSubscription` | Claim-persisted deterministic subscription reference | `MaxioSubscriptionService.ReconcileSubscriptionAsync` | Live sandbox validation exercises the normal success path; SDK handler seam remains the failure-path test boundary. |

## Repository survey patterns to imitate

| Concern | Pattern + one exemplar |
| --- | --- |
| Endpoint registration | MinimalApi `IEndpoint` class with `AddRoute`, `Results.*`, tags and response metadata: `src/PublicApi/CatalogBrandEndpoints/CatalogBrandListEndpoint.cs` |
| Protected write route | Lambda-level JWT `[Authorize(... AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`: `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| API test host | `WebApplicationFactory<Program>` with MSTest: `tests/PublicApiIntegrationTests/ProgramTest.cs` |
| EF entity mapping | `IEntityTypeConfiguration<T>` via `ApplyConfigurationsFromAssembly`: `src/Infrastructure/Data/Config/OrderConfiguration.cs` |
| DbContext registration | PublicApi calls `Infrastructure.Dependencies.ConfigureServices`; test mode switches both contexts to in-memory: `src/Infrastructure/Dependencies.cs` |
| JWT claims | Existing issued JWT contains only name/roles, therefore the implementation must add a stable `NameIdentifier` before deriving Maxio references: `src/Infrastructure/Identity/IdentityTokenClaimService.cs` |

## Assumptions & Blockers

**Assumptions:** The authenticated application user has usable first name, last name, and email values in Identity (the current JWT contains only username/roles, so the implementation will look up the user server-side); `ProductFamilyHandle` is a configured Maxio handle, and the user will supply product handles rather than unstable IDs. “Confirm plan/price/state/next billing” means returning Maxio's subscription-specific `ProductPriceInCents`, `State`, and `NextAssessmentAt`, plus the embedded product handle/name; do not substitute current catalog price for the subscribed price.

**Blockers:** Production-grade Maxio customer creation requires a nonblank first name, last name, and email, but `ApplicationUser` shown in this repository only inherits Identity fields and the existing token does not establish which values are mandatory/populated. The plan must validate/derive those values or the product owner must specify the canonical profile source. Live sandbox verification is blocked until the operator supplies a sandbox `ApiKey`, `Subdomain`, `ProductFamilyHandle`, a product in that family that can be subscribed without unscoped payment collection data, and a disposable authenticated Identity user. The plugin-local SDK reference is machine-specific; CI and every developer machine need this plugin installed at a valid path (or an approved distributable SDK strategy) before builds can be portable.

## Source labels

SDK contract rows cite only plugin-relative `sdk/dotnet/sdk-map.md`, `map/operations/*.md`, and map-named `src/MaxioAdvancedBilling/...` files. Application routes, DTOs, claims, persistence, status codes, limits, and configuration binding choices are **YOUR CALL — not in the map**. Live request acceptance and sandbox catalog/payment configuration are **UNVERIFIED** until the planned sandbox verification.
