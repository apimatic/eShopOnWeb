# Maxio Advanced Billing — eShopOnWeb subscription capability — plan & contract sheet

## 1. Scope & sequence

Additive, parallel subscription capability on `src/PublicApi` (JWT-authenticated). Maxio Advanced Billing is the system of record. Sandbox site `cp-exp-1` (US), product family handle `eshop-subscribe` (from config, never hard-coded).

| # | Step | Maxio operations used |
| --- | --- | --- |
| 1 | `MaxioOptions` POCO + DI registration + fail-fast validation + client singleton (Infrastructure) | — |
| 2 | Entities `MaxioSubscription` (PK UserName+PlanHandle) + `MaxioCustomer` (PK UserName) + DbSets + EF configs (ApplicationCore/Infrastructure) | — |
| 3 | `ISubscriptionService` (ApplicationCore) + `MaxioSubscriptionService` (Infrastructure) | `ListProductsForProductFamily`, `ReadCustomerByReference`, `CreateCustomer`, `CreateSubscription`, `FindSubscription`, `ReadSubscription`, `ListCustomerSubscriptions` |
| 4 | Endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` + DTOs (PublicApi) | — |
| 5 | ExceptionMiddleware: map `SubscriptionPlanNotFoundException`→404, `MaxioProviderException`→502 (keep `DuplicateException`→409) | — |
| 6 | Unit tests for the service (fake HttpClient seam) | — |
| 7 | Build, run existing tests, manual end-to-end verification against sandbox | — |

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim.** Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own — never flat arguments.
>
> **Every SDK type is written fully-qualified with the namespace its source path implies**, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

### Per-operation rows

| Operation | Signature | Request record + members | Body model + fields | Response envelope + inner fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ListProductsForProductFamily` | `Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(ListProductsForProductFamilyRequest, RequestOptions?, CancellationToken)` | `ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (id or `handle:`-prefixed); `Page: int=1`; `PerPage: int=20` (max 200); `IncludeArchived: bool?`; others optional, unused | — (GET) | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product` → `Id:int?`, `Handle:string?`, `Name:string?`, `Description:string?`, `PriceInCents:long?`, `Interval:int?`, `IntervalUnit:IntervalUnit?`, `ArchivedAt:DateTimeOffset?` | **Case A** `ApiException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | paged (Page/PerPage); request `PerPage=200` | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs`; `Errors/ListProductsForProductFamilyError.cs` |
| `ReadCustomerByReference` | `Task<CustomerResponse> ReadCustomerByReference(ReadCustomerByReferenceRequest, RequestOptions?, CancellationToken)` | `ReadCustomerByReferenceRequest`: `Reference: string, required` | — (GET) | `CustomerResponse.Customer` → `Id:int?`, `Reference:string?` | **Case B** `ApiException<RawError>`; 404 = `ex.Error.StatusCode == NotFound` | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `CreateCustomer` | `Task<CustomerResponse> CreateCustomer(CreateCustomerOperationRequest, RequestOptions?, CancellationToken)` | `CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` | `CreateCustomerRequest` (wire `customer`): `Customer: CreateCustomer, required`. `CreateCustomer`: `FirstName: string, required`; `LastName: string, required`; `Email: string, required`; `Reference: string?`; others optional, unused | `CustomerResponse.Customer` → `Id:int?` | **Case A** `ApiException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Errors/CreateCustomerError.cs` |
| `CreateSubscription` | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionOperationRequest, RequestOptions?, CancellationToken)` | `CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `CreateSubscriptionRequest` (wire `subscription`): `Subscription: CreateSubscription, required`. `CreateSubscription`: `ProductHandle: string?`; `CustomerId: int?`; `Reference: string?`; others optional, unused | `SubscriptionResponse.Subscription` → `Id:int?`, `State:SubscriptionState?`, `ProductPriceInCents:long?`, `CurrentPeriodEndsAt:DateTimeOffset?`, `CreatedAt:DateTimeOffset?`, `Product:Product?` (Name/Handle/PriceInCents) | **Case A** `ApiException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Errors/CreateSubscriptionError.cs` |
| `FindSubscription` | `Task<SubscriptionResponse> FindSubscription(FindSubscriptionRequest, RequestOptions?, CancellationToken)` | `FindSubscriptionRequest`: `Reference: string?` | — (GET) | `SubscriptionResponse.Subscription` (as above) | **Case A** `ApiException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Errors/FindSubscriptionError.cs` |
| `ReadSubscription` | `Task<SubscriptionResponse> ReadSubscription(ReadSubscriptionRequest, RequestOptions?, CancellationToken)` | `ReadSubscriptionRequest`: `SubscriptionId: int, required`; `Include: IReadOnlyList<SubscriptionInclude>?` unused | — (GET) | `SubscriptionResponse.Subscription` (as above) | **Case B** `ApiException<RawError>` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/ReadSubscriptionRequest.cs` |
| `ListCustomerSubscriptions` | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(ListCustomerSubscriptionsRequest, RequestOptions?, CancellationToken)` | `ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — (GET) | `IReadOnlyList<SubscriptionResponse>`; each `SubscriptionResponse.Subscription` (as above) | **Case B** `ApiException<RawError>` | none | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs` |

### Enum values actually used

| Enum | Values used | Source |
| --- | --- | --- |
| `SubscriptionState` (OpenStringEnum) | read via `.Value` (wire string: `active`, `trialing`, `canceled`, …) — no Match needed | `Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` (OpenStringEnum) | read via `.Value` (`month`/`day`) | `Models/Enums/IntervalUnit.cs` |

### Client construction / auth / server-node facts

- Client: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` — only constructor. Namespace `MaxioAdvancedBilling`.
- Options: `BasicAuth = new BasicAuthCredentials { Username = <apiKey>, Password = "x" }` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`); `Environment = ServerEnvironment.Us` (namespace `MaxioAdvancedBilling.Servers`); `Server.Production.Us.Site = <subdomain>` OR `Server.Production.Us.BaseUrl = <baseUrl>` verbatim when `Maxio:BaseUrl` set (a BaseUrl without `{site}` leaves the `{site}` template param unused — `Core/TemplateParamsFactory.cs` only replaces placeholders that exist); `Logging = new LoggingOptions { LoggerFactory = <ILoggerFactory>, LogRequestBody = false }` (namespace `MaxioAdvancedBilling.Core.Configuration`); `Retry` default (`RetryOptions.Default()`).
- DI: `services.AddHttpClient()` + singleton client built once at registration (captures options → rotation needs restart). Mirror the SDK's own `ServiceCollectionExtensions.AddMaxioAdvancedBillingClient` shape.
- Namespaces needed: `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Api`, `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Requests.Customers`, `MaxioAdvancedBilling.Requests.Subscriptions`, `MaxioAdvancedBilling.Requests.ProductFamilies`, `MaxioAdvancedBilling.Errors`, `MaxioAdvancedBilling.Core.Exceptions`, `MaxioAdvancedBilling.Core.ErrorResponse`, `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Core.Configuration`, `MaxioAdvancedBilling.Servers`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| the plan handle a caller submits to `POST /api/subscriptions` must be one of the plans this app offers (the configured family's products) | `CreateSubscription` ← `ListProductsForProductFamily` | `MaxioSubscriptionService.SubscribeAsync` — lists the family's products and looks the handle up before `CreateSubscription` is called; not found → `SubscriptionPlanNotFoundException` |

## 3. Trap notes

- **Client/DI setup** — HttpClient must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request; the SDK client wrapper over it may be transient. **MUST load `dotnet-client-initialization`**.
- **Auth** — Basic auth: username = Maxio API key, password = `x`; set before constructing the client; load from configuration, never hard-code. **MUST load `dotnet-authentication`**.
- **Calling endpoints** — every input goes on the operation's request record; `required` members must be set; the generator-injected `Idempotency-Key: Guid.NewGuid()` header is not a real idempotency key. **MUST load `dotnet-calling-endpoints`**.
- **Models** — enums are `OpenStringEnum<T>` records (no public factory, read `.Value`), response envelopes wrap the payload in one field (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`), request records are never serialized. **MUST load `dotnet-models`**.
- **Error handling** — Case A vs Case B mechanics; `TryGetRawError` is not a catch-all on typed errors; a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`). **MUST load `dotnet-error-handling`**.
- **Configuration & resilience** — `Timeout` is per-attempt not total; `HttpMethodsToRetry` gates every retry trigger (default `GET, HEAD, PUT, OPTIONS` → `POST` is never resent); the base-URL override point is `options.Server.Production.Us.BaseUrl`; `LogRequestBody` does not redact JSON. **MUST load `dotnet-configuration-resilience`**.
- **Testing** — the `HttpClient` constructor argument is the test seam; match the project's existing framework and assertion style. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load every skill below **before implementation starts**; the sheet deliberately does not carry their contents.

| Skill | Step it governs |
| --- | --- |
| `dotnet-client-initialization` | client & DI setup (step 1) |
| `dotnet-authentication` | credentials (step 1) |
| `dotnet-calling-endpoints` | every SDK call (step 3) |
| `dotnet-models` | building request models / reading response models (step 3) |
| `dotnet-error-handling` | error boundary + middleware (steps 3, 5) |
| `dotnet-configuration-resilience` | retries, timeout budget, base URL, logging (steps 1, 3) |
| `dotnet-testing` | unit tests (step 6) |

## 5. PRODUCTION READINESS

| # | Concern | The decision the plan records |
| --- | --- | --- |
| 1 | **Credential fail-fast** | `MaxioOptions` bound from `Maxio:` section in `Infrastructure/Maxio/MaxioDependencies.cs` (called from `Dependencies.ConfigureServices`, i.e. at host startup). Validation throws `InvalidOperationException` at registration when `ApiKey`, `Subdomain` or `ProductFamilyHandle` is missing or blank (each checked — a blank part is not a missing one). `BaseUrl` optional. |
| 2 | **Secret sourcing & rotation** | Secrets come from .NET user-secrets (loaded once from `MAXIO_API_KEY` / `MAXIO_SITE_SUBDOMAIN` / `MAXIO_DEFAULT_PRODUCT_FAMILY` by the operator; values never enter the repo). DI builds the options object once at registration and captures it in the singleton client → a rotated secret takes effect only on process restart. |
| 3 | **Total timeout budget** | SDK `RetryOptions.Default()`: `Timeout` = 100 s **per attempt**, `MaxRetries` = 3, retries only `GET/HEAD/PUT/OPTIONS`. A hung read could otherwise cost up to 3×100 s. The service wraps each operation in a linked `CancellationTokenSource` with a 30 s deadline, so the caller's budget is 30 s per operation, enforced by the deadline. |
| 4 | **Write-retry ownership** | SDK default `HttpMethodsToRetry` = `GET, HEAD, PUT, OPTIONS` → `POST` (`CreateCustomer`, `CreateSubscription`) is **never** resent by the SDK. No `PUT`/`DELETE` in scope. |
| 5 | **Idempotency & ambiguous writes** | `CreateCustomer`: no real caller-supplied key on the request record (the injected `Idempotency-Key` header is not one); reconciliation key = customer `reference` (the eShopOnWeb username). `CreateSubscription`: no real key on the record; reconciliation key = subscription `reference` (`sub-{username}-{planHandle}`). Both sit **beside** the local claims (DUPLICATE CLAIMS below), never in place of them. |
| 6 | **Observability** | `Information`: subscribe success (subscription id, plan handle, customer id), plan list count. `Warning`: provider errors — log `ex.Error.ReadAsString()` (Case B) / typed error message (Case A) with the HTTP status; `SdkConnectionException`/`SdkTimeoutException` logged with the failed call name. `LogRequestBody` stays off (JSON bodies would be logged unredacted). |
| 7 | **Sensitive data** | Scope carries no card/bank data; request models in scope are `CreateCustomer` (first/last name, email, reference) and `CreateSubscription` (product handle, customer id, reference) — no fields you would not want in a log. Still: `LogRequestBody` stays off **and** `LoggerFactory` is assigned explicitly, so the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot switch body logging on from outside the code. |
| 8 | **Environment selection** | Server group `Production` only. US → `https://{site}.chargify.com`; EU → `https://{site}.ebilling.maxio.com`. This build targets the sandbox: `Environment = Us`, `Site = <subdomain>` (sandbox `cp-exp-1`), and `Maxio:BaseUrl` overrides verbatim when set. Test traffic stays on the sandbox because the credentials point at the sandbox site and no `BaseUrl` override is set. |
| 9 | **Duplicate prevention under concurrency** | DUPLICATE CLAIMS below. |
| 10 | **Partial results** | PAGED READS below. |
| 11 | **Unknown outcomes** | UNKNOWN OUTCOMES below. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateCustomer` (per user) | `MaxioCustomer` row (PK `UserName`) inserted before any customer SDK call | PK violation on insert (`DbUpdateException`) | `catch (DbUpdateException)` in `MaxioSubscriptionService.EnsureCustomerAsync` | TBD |
| `CreateSubscription` (per user+plan) | `MaxioSubscription` row (PK `UserName`+`PlanHandle`, `Status=pending`) inserted before any subscription SDK call | PK violation on insert (`DbUpdateException`) | `catch (DbUpdateException)` in `MaxioSubscriptionService.SubscribeAsync` | TBD |

Order per row: claim → SDK call → record the result. On failure after the claim, the claim row is deleted (released) before rethrowing. Provider references (`customer.reference`, `subscription.reference`) sit beside the claims as reconciliation keys.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | `PerPage=200` (max allowed) | The SDK returns a bare `IReadOnlyList<ProductResponse>` with no pagination metadata — no field or return type signals truncation. Decision: request `PerPage=200` and treat the result as the complete offer set; a family with >200 products is out of scope (`YOUR CALL — not in the map`). | `MaxioSubscriptionService.ListPlansAsync` / `SubscribeAsync` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | customer `reference` = username | `catch` in `MaxioSubscriptionService.EnsureCustomerAsync` (on `SdkConnectionException`/`SdkTimeoutException`/`ApiException<CreateCustomerError>` 422, re-read; found → use it, else rethrow) | unit test: fake HttpClient returns a connection failure after the create would have succeeded; asserts the re-read settles the outcome |
| `CreateSubscription` | `FindSubscription` | subscription `reference` = `sub-{username}-{planHandle}` | `catch` in `MaxioSubscriptionService.SubscribeAsync` (on `SdkConnectionException`/`SdkTimeoutException`/`ApiException<CreateSubscriptionError>` 422, re-read; found → use it, else rethrow) | unit test: fake HttpClient returns a connection failure after the create would have succeeded; asserts the re-read settles the outcome |

## 6. Assumptions & Blockers

- **Assumption — environment selection.** `Maxio:Environment` is not among the four mandated keys, so the SDK environment is fixed at `ServerEnvironment.Us`; an EU-hosted site must set `Maxio:BaseUrl` to its EU base URL. `MAXIO_ENVIRONMENT` is `US` and the sandbox is US-hosted, so this matches the target.
- **Assumption — customer reference.** The Maxio customer `reference` is the eShopOnWeb username (the JWT `ClaimTypes.Name`). Usernames are stable for the seeded users; a future username change would need a reference migration (out of scope).
- **Assumption — subscription reference.** `sub-{username}-{planHandle}` is deterministic so `FindSubscription` can reconcile. Cancellation/resubscription to the same plan is out of scope; a canceled subscription would collide with a later resubscribe (the local claim row would also still exist). Noted as a limitation.
- **Assumption — test host credentials.** `WebApplicationFactory<Program>` runs the app in `Development` (user-secrets loaded), so the fail-fast validation sees the `Maxio:` options and the existing `PublicApiIntegrationTests` keep starting. Verified empirically during implementation; if the test host does not load user-secrets, the fail-fast is made test-friendly (see implementation).
- **Blocker — none.** The map covers every operation the scope needs.

## 7. Sources

All rows cite their `map/operations/*.md` page and the map-named declaring file(s) in the SDK clone. Client/options facts: `sdk-map.md` (Getting a client, Error-handling model, Servers & auth), `MaxioAdvancedBillingClientOptions.cs`, `Servers/ProductionOptions.cs`, `Core/Configuration/RetryOptions.cs`, `Core/Configuration/LoggingOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`, `ServiceCollectionExtensions.cs`, `Core/TemplateParamsFactory.cs`.
