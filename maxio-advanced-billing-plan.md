# Scope & sequence

| Step | Work and Maxio operation(s) |
| --- | --- |
| 1 | Add the SDK project reference to `src/PublicApi`, bind/validate the `Maxio` options, and register one configured client. The binding keys are `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and `Maxio:BaseUrl`; development secrets use these same keys in PublicApi user-secrets. Sandbox-only selection is an implementation decision in Production Readiness. |
| 2 | Add durable subscription enrollment and customer-resolution records/configurations/migration to the existing catalog database; claims must be inserted before either provider write. |
| 3 | Add JWT-protected `GET /api/subscription-plans`, reading the configured offer handles with `Products.ReadProductByHandle`, checking their returned family handle against `Maxio:ProductFamilyHandle`, and returning only valid plans. |
| 4 | Add JWT-protected `POST /api/subscriptions`: derive the identity from the authenticated name claim, get/create the Maxio customer with `Customers.ReadCustomerByReference` / `Customers.CreateCustomer`, validate the submitted plan against the offered-plan read, then create/reconcile it with `Subscriptions.CreateSubscription` / `Subscriptions.FindSubscription`. |
| 5 | Add JWT-protected `GET /api/my-subscriptions`, resolving the caller's stored customer and using `Customers.ListCustomerSubscriptions`; return the subscription plan/price/state/next-billing projection. |
| 6 | Add focused PublicApi integration/unit coverage for authorization, plan allow-list/family rejection, repeated customer resolution, concurrent/double-click enrollment, provider error translation, and ambiguous-write reconciliation. |

# CONTRACT SHEET

WARNING: signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.

WARNING: every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller property · method signature | Request record + members (`Name: type, required?`) | Body model + fields (`Name (wire_name): type, required?`) | Response envelope + inner fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `Customers` · `CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?, no` | `CreateCustomerRequest`: `Customer (customer): CreateCustomer, required`; `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?, no`. Omit unrelated optional customer fields. | `CustomerResponse.Customer`; read `Customer.Id`, `Customer.Reference`. | A: `ApiException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` fallback. | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `Customers` · `ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `ReadCustomerByReferenceRequest`: `Reference: string, required` | none | `CustomerResponse.Customer`; read `Customer.Id`, `Customer.Reference`. | B: `ApiException<RawError>`; `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `Products` · `ReadProductByHandle(MaxioAdvancedBilling.Requests.Products.ReadProductByHandleRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `ReadProductByHandleRequest`: `ApiHandle: string, required` | none | `ProductResponse.Product`; read `Product.Handle`, `Name`, `PriceInCents`, `ProductFamily.Handle`. | B: `ApiException<RawError>`; `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | none | `map/operations/Products.md`; `Requests/Products/ReadProductByHandleRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs`; `Models/ProductFamily.cs` |
| `Subscriptions` · `CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?, no` | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required`; `CreateSubscription`: `ProductHandle (product_handle): string?, no`; `CustomerId (customer_id): int?, no`; `Reference (reference): string?, no`. Send exactly product handle, customer ID, and a deterministic enrollment reference; omit mutually unnecessary alternatives (`ProductId`, customer attributes, etc.). | `SubscriptionResponse.Subscription` (nullable); read `Id`, `Reference`, `Product`, `ProductPriceInCents`, `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt`. Treat absent `Subscription` as malformed success. | A: `ApiException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` fallback. | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs` |
| `Subscriptions` · `FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `FindSubscriptionRequest`: `Reference: string?, no` | none | `SubscriptionResponse.Subscription` (nullable); read same projection fields as create. | A: `ApiException<FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], `TryGetRawError(out RawError)` fallback. | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs` |
| `Customers` · `ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | none | `IReadOnlyList<SubscriptionResponse>`; each nullable `SubscriptionResponse.Subscription`; project `Product.Handle`/`Name`, `ProductPriceInCents`, `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt`. | B: `ApiException<RawError>`; `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | none | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Models/Product.cs` |

## CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A `planHandle` accepted by `POST /api/subscriptions` must be one of the explicitly offered handles returned by the two `ReadProductByHandle` calls, and its returned `ProductFamily.Handle` must equal `Maxio:ProductFamilyHandle`. | `CreateSubscription` ← `ReadProductByHandle` | The subscription endpoint's plan resolver, before `CreateSubscription`; it reads both configured offer handles (`basic-plan`, `eshop-pro`) rather than trusting caller input. |
| A provider customer ID used for subscription creation must be the customer returned by a lookup/create for the authenticated user's deterministic customer reference. | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | The customer-resolution member persists and returns the provider customer ID before the enrollment claim reaches `CreateSubscription`. |

## Client construction, auth, and server nodes

| Fact | Contract | Source |
| --- | --- | --- |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; DI extension is `AddMaxioAdvancedBillingClient`. | `sdk-map.md` |
| Auth | HTTP Basic via `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`; username is the Chargify API key and password is literal `x`. | `sdk-map.md` |
| Server | All scoped operations use `Production`; `ServerEnvironment.Us` default / `ServerEnvironment.Eu` available. Production URLs are US `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com`; set the corresponding Production Site to `Maxio:Subdomain` and use `Maxio:BaseUrl` only as an explicit Production base-URL override. `Ebb` exists but is untouched. | `sdk-map.md` |
| SDK idempotency | The SDK inserts a fresh `Idempotency-Key` header for every non-GET here; neither create request record has a caller-supplied idempotency member. It is not an idempotency key for this design. | `sdk-map.md`; `Api/Customers.cs`; `Api/Subscriptions.cs` |

## Trap notes

| Step | Hazard |
| --- | --- |
| 1 | The SDK client/options lifecycle and HTTP client ownership determine whether configuration and connections are safe in DI. **MUST load maxio-advanced-billing:dotnet-client-initialization** |
| 1 | Basic-auth credentials have non-obvious placement and blank values otherwise surface as runtime authorization failures. **MUST load maxio-advanced-billing:dotnet-authentication** |
| 3–5 | Controller calls take generated operation request records and their response envelopes include nullable nested models; wrong construction or projection will compile poorly or deserialize unexpectedly. **MUST load maxio-advanced-billing:dotnet-calling-endpoints** |
| 3–5 | Generated models/enums/unions use required/init and wire-name semantics that govern valid payloads and unknown values. **MUST load maxio-advanced-billing:dotnet-models** |
| 4–5 | Typed and raw provider failures, as well as deserialization failures, need a complete translation boundary. **MUST load maxio-advanced-billing:dotnet-error-handling** |
| 1, 3–5 | Retry verbs, timeout scope, logging/hooks, server overrides, and ambiguous provider writes change operational correctness. **MUST load maxio-advanced-billing:dotnet-configuration-resilience** |
| 6 | SDK calls must be tested through the generated client's supported HTTP seam, including retry/error/connection behavior. **MUST load maxio-advanced-billing:dotnet-testing** |

# REQUIRED READING

| Skill | Step it governs |
| --- | --- |
| `maxio-advanced-billing:dotnet-client-initialization` | SDK client DI registration and lifetime (step 1). |
| `maxio-advanced-billing:dotnet-authentication` | Basic credential binding and fail-fast validation (step 1). |
| `maxio-advanced-billing:dotnet-calling-endpoints` | All generated endpoint calls (steps 3–5). |
| `maxio-advanced-billing:dotnet-models` | Generated request/response model construction and projections (steps 3–5). |
| `maxio-advanced-billing:dotnet-error-handling` | Provider and transport error translation (steps 4–5). |
| `maxio-advanced-billing:dotnet-configuration-resilience` | Retries, total timeout, logging, hooks, base URL, and reconciliation (steps 1, 3–5). |
| `maxio-advanced-billing:dotnet-testing` | SDK HTTP stubs and failure-path tests (step 6). |
| Hazard | a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`). |

# PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `Maxio` once at PublicApi startup and validate `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and `BaseUrl` are nonblank; require the host to refuse startup before client registration if any is blank. |
| 2 | Secret sourcing & rotation | Store `Maxio:ApiKey` in PublicApi user-secrets for developer machines and deployment secret configuration for sandbox; bind it while registering the singleton client/options, so rotation requires process restart. No hot rotation is required. |
| 3 | Total timeout budget | YOUR CALL — not in the map: expose one 30-second end-to-end request budget via the endpoint cancellation token and configure the SDK retry/attempt behavior only after reading the resilience skill; the endpoint returns a controlled timeout rather than letting retries exceed 30 seconds. |
| 4 | Write-retry ownership | YOUR CALL — not in the map: do not automatically retry `CreateCustomer` or `CreateSubscription`; their outcomes are reconciled by provider reads. Reads may use the SDK's documented retry posture after the resilience skill is loaded. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer` has no real request-record key; use authenticated user name as Maxio `customer.reference`, re-read with `ReadCustomerByReference`. `CreateSubscription` has no real request-record key; use durable enrollment reference `subscription:{userName}:{planHandle}` and reconcile with `FindSubscription`. The SDK header is explicitly not used as either key. |
| 6 | Observability | Log operation name, application correlation ID, authenticated user ID hash/reference, plan handle, and provider response/error status at Information/Warning/Error without request bodies. Add an SDK response hook and propagate the provider correlation/request identifier only if the documented response headers expose one; otherwise record no invented provider ID. |
| 7 | Sensitive data | Customer creation carries PII (name/email). Set `Logging.LogRequestBody` off, assign `LoggerFactory` explicitly in production, and do not log request/response bodies or raw errors containing customer data; no card/bank fields are sent. |
| 8 | Environment selection | Sandbox only: all scoped calls use Production server group with `ServerEnvironment.Us` and a sandbox `Maxio:Subdomain`/`Maxio:BaseUrl`; do not register EU or Ebb for this feature. Deployment validation must reject known live subdomain/base URL values, keeping traffic away from live. |
| 9 | Duplicate prevention under concurrency | Use catalog-database claim rows with primary-key-enforced natural keys, inserted and saved before the relevant provider write; do not rely on a pre-read or a post-provider unique-index conflict. |
| 10 | Partial results | N/A: each scoped provider read has no SDK pagination according to the map; `ListCustomerSubscriptions` returns the operation's single `IReadOnlyList` response. |
| 11 | Unknown outcomes | For transport/timeout after either provider write, settle in that write's catch by the corresponding reference lookup; persist `Unknown` only if reconciliation itself fails, and return a retry-safe pending/outcome response rather than issuing another write. |

## DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | New catalog-context `MaxioCustomerResolution` aggregate keyed by authenticated username/customer reference. | The catalog database primary key refuses duplicate insert, including in-memory tests. | The resolution service catches the persistence duplicate/concurrency exception and reads the completed resolution; if pending, it waits/returns a deterministic in-progress outcome without calling Maxio. | `MaxioSubscriptionService.ResolveCustomerAsync` → `Customers.CreateCustomer` |
| `Subscriptions.CreateSubscription` | New catalog-context `MaxioSubscriptionEnrollment` aggregate keyed by authenticated username plus allowed plan handle (and stores the deterministic subscription reference). | The catalog database primary key refuses duplicate insert, including in-memory tests. | The enrollment service catches the persistence duplicate/concurrency exception and returns/reconciles the existing claim rather than calling Maxio. | `MaxioSubscriptionService.SubscribeAsync` → `Subscriptions.CreateSubscription` |

## PAGED READS

none — the scoped Maxio operations have no pagination according to `sdk-map.md` defaults and their operation rows.

## UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `Customers.ReadCustomerByReference` | Authenticated username serialized as `CreateCustomer.Reference`. | `MaxioSubscriptionService.ResolveCustomerAsync` | Focused SDK-handler test remains to be added. |
| `Subscriptions.CreateSubscription` | `Subscriptions.FindSubscription` | Persisted deterministic enrollment reference `subscription:{userName}:{planHandle}`. | `MaxioSubscriptionService.SubscribeAsync` | Focused SDK-handler test remains to be added. |

# Assumptions & Blockers

## Assumptions

1. The requested seeded catalog already exists in the Maxio sandbox: family handle `eshop-subscribe`, product handles `eshop-pro` and `basic-plan`; plan discovery will validate both returned products belong to that family before offering/enrolling them.
2. PublicApi's current JWT contains `ClaimTypes.Name` but not a stable Identity user ID. The implementation will use that name as the application-owned customer reference and normalize/validate it consistently; changing token claims to use the Identity ID would be a separate scope decision.
3. "Next billing" will prefer `Subscription.NextAssessmentAt` and fall back to `CurrentPeriodEndsAt` when the former is null, because both mapped fields describe future billing timing.

## Blockers

None. The existing `CatalogContext` is an application-owned durable store, so the implementation can add claim aggregates/configuration/migration necessary for concurrency-safe duplicate prevention.

# Source labels

Every SDK contract fact above is labeled with the map page and its map-named SDK source files. Endpoint routes, option binding, database aggregate names, security policy, response DTO shape, timeout, logging, sandbox guard, and customer/enrollment claim design are **YOUR CALL — not in the map**. Live sandbox catalog existence and the actual provider response header used for correlation are **UNVERIFIED** and are guarded by startup/operation validation rather than assumed.
