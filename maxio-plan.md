# Maxio subscription billing — eShopOnWeb PublicApi

## 1. Scope & sequence

Additive, parallel Subscribe flow on `src/PublicApi`. Maxio is system of record; no local user↔subscription table.

| Step | What | Operations |
| --- | --- | --- |
| 0 | Vendor Maxio .NET SDK (source build, not NuGet), `ProjectReference` from Infrastructure; `global.json` `rollForward: latestMajor`; bind `Maxio:*` from user-secrets + `MAXIO_*` overlay | client construction |
| 1 | DI: `MaxioClient` + `ISubscriptionBillingService`; fail-fast on blank credentials | — |
| 2 | `GET /api/subscription-plans` | `ProductFamilies.ListProductsForProductFamily` |
| 3 | Ensure Maxio customer (Identity `user.Id` as `reference`) | `Customers.ReadCustomerByReference` → on 404 `Customers.CreateCustomer`; 422 race → re-read |
| 4 | `POST /api/subscriptions` | `Subscriptions.FindSubscription` → miss: `Subscriptions.CreateSubscription`; 422 race → re-find |
| 5 | `GET /api/my-subscriptions` | `Customers.ReadCustomerByReference` (404 → empty list) → `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary on all SDK calls; unit tests (HttpClient seam) + live sandbox verify | — |

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim. Every parameter name is the literal C# identifier (cancellation token is `ct`, named arguments write `ct:`).
⚠ Every SDK type is fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type.

### Operations

| Controller · method | Signature | Request | Response (fields read) | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` · `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, Maxio.Models.Enums.BasicDateField? dateField, Maxio.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, Maxio.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `dateField`…`include` must pass explicitly (`null` to skip) | `productFamilyId`: either numeric id **or** handle prefixed `handle:` (`Api/ProductFamilies.cs` `<param>`). Call with `handle:{Maxio:ProductFamilyHandle}`. Pass `null` for date/filter/include; `includeArchived: false`; `page: 1`; `perPage: 200` (max allowed). | `IReadOnlyList<Maxio.Models.ProductResponse>` → `.Product` (`required`): `Handle`, `Name`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductPricePointHandle`, `ArchivedAt` | **Case A** `SdkException<Maxio.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out Maxio.Core.ErrorResponse.RawError)` [fallback] | Not a `Pageable`; caller must page via `page`/`perPage` | `map/operations/ProductFamilies.md`; `Api/ProductFamilies.cs`; `Models/ProductResponse.cs`; `Models/Product.cs`; `Errors/ListProductsForProductFamilyError.cs` |
| `client.Customers` · `ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = ASP.NET Identity `ApplicationUser.Id` | `Maxio.Models.CustomerResponse` → `.Customer` (`required`): `Id`, `Reference`, `Email` | **Case B** `SdkException<Maxio.Core.ErrorResponse.RawError>` — `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()`. 404 = no customer | none | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `client.Customers` · `CreateCustomer` | `CreateCustomer(Maxio.Models.CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must pass explicitly | Envelope `CreateCustomerRequest.Customer` (`required`, wire `customer`) → `Maxio.Models.CreateCustomer`: **required** `FirstName` (`first_name`), `LastName` (`last_name`), `Email` (`email`); **send** `Reference` (`reference`, Identity user id). `<remarks>`: only one customer per reference; reference must be unique. Omitted: address/org/locale/tax/parent/salesforce/branding | `Maxio.Models.CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<Maxio.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out Maxio.Models.CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback]. Payload `.Errors` (`Maxio.Models.AnyOf.Errors1`): `TryGetCustomerError(out Maxio.Models.CustomerError)` (`.Customer` string) **or** `TryGetListOfString(out IReadOnlyList<string>)` | none | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Errors/CreateCustomerError.cs`; `Models/CustomerErrorResponse1.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `client.Subscriptions` · `FindSubscription` | `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `reference` must pass explicitly | `reference` = `eshop:{userId}:{productHandle}` | `Maxio.Models.SubscriptionResponse` → `.Subscription` (**nullable**): `Id`, `State`, `Product` (`.Handle`, `.Name`), `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Reference` | **Case A** `SdkException<Maxio.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Errors/FindSubscriptionError.cs` |
| `client.Subscriptions` · `CreateSubscription` | `CreateSubscription(Maxio.Models.CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must pass explicitly | Envelope `CreateSubscriptionRequest.Subscription` (`required`, wire `subscription`) → `Maxio.Models.CreateSubscription`: **send** `ProductHandle` (`product_handle`; required unless `product_id` — we never send numeric product id), `CustomerId` (`customer_id`), `Reference` (`reference`), `PaymentCollectionMethod` = `Maxio.Models.Enums.CollectionMethod.Remittance` (`remittance`) — live 422 “No payment method was on file” when omitted (automatic collection). No field is `required` on the C# record; acceptance is the remarks: product via handle **or** id; customer via id **or** `customer_reference` **or** `customer_attributes`. Omitted: price point (site default), coupons, components, cards, `customer_attributes`, `next_billing_at` | `Maxio.Models.SubscriptionResponse` → `.Subscription` (nullable): same fields as Find | **Case A** `SdkException<Maxio.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out Maxio.Models.ErrorListResponse1)` [422] (`.Errors`: `IReadOnlyList<string>`) · `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Errors/CreateSubscriptionError.cs`; `Models/ErrorListResponse1.cs` |
| `client.Customers` · `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` = `Customer.Id` (int; if null on a successful customer read → treat as unexpected and fail the call) | `IReadOnlyList<Maxio.Models.SubscriptionResponse>` — same inner fields as Find | **Case B** `SdkException<RawError>` | none | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/SubscriptionResponse.cs` |

### Enums used

| Type | Members needed | Source |
| --- | --- | --- |
| `Maxio.Models.Enums.IntervalUnit` (`StringEnum`; `.Value` / `.ToString()` is the wire string) | `Day` = `day`, `Month` = `month` | `Models/Enums/IntervalUnit.cs`; `Core/Enum/TypedEnum.cs` |
| `Maxio.Models.Enums.SubscriptionState` | `Pending` `pending`, `FailedToCreate` `failed_to_create`, `Trialing` `trialing`, `Assessing` `assessing`, `Active` `active`, `SoftFailure` `soft_failure`, `PastDue` `past_due`, `Suspended` `suspended`, `Canceled` `canceled`, `Expired` `expired`, `Paused` `paused`, `Unpaid` `unpaid`, `TrialEnded` `trial_ended`, `OnHold` `on_hold`, `AwaitingSignup` `awaiting_signup` | `Models/Enums/SubscriptionState.cs` |
| `Maxio.Servers.ServerEnvironment` | `Us` = `US` (default) — sandbox site is US chargify.com | `Servers/ServerEnvironment.cs`; `sdk-map.md` Servers & auth |

### Client construction / auth / server node

| Fact | Detail | Source |
| --- | --- | --- |
| Client | `Maxio.MaxioClient(HttpClient httpClient, Maxio.MaxioClientOptions options)` only ctor. `services.AddMaxioClient(Action<MaxioClientOptions>?)` registers **singleton** client via `IHttpClientFactory.CreateClient()` | `MaxioClient.cs`; `ServiceCollectionExtensions.cs`; `sdk-map.md` Getting a client |
| Auth | HTTP Basic: `options.BasicAuth = new Maxio.Core.Authentication.Basic.BasicAuthCredentials { Username = …, Password = … }`. Username = Chargify API key, password = `x`. Basic works only with US/EU, not Maxio API Gateway. Do not set `BearerAuth` | `sdk-map.md` Servers & auth; `MaxioClientOptions.cs`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `options.Environment = ServerEnvironment.Us`. Scope uses server group **Production** only | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Site | `{site}` default `"subdomain"` → set `options.Server.Production.Us.Site` from `Maxio:Subdomain` | `sdk-map.md`; `Servers/ProductionOptions.cs`; `ServerOptions.cs` (`namespace Maxio`) |
| Base URL override | `options.Server.Production.Us.BaseUrl` default `https://{site}.chargify.com`. When `Maxio:BaseUrl` is non-blank, assign it **verbatim** | `Servers/ProductionOptions.cs`; task |
| Bound settings (names only) | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). Overlay env `MAXIO_API_KEY` / `MAXIO_SITE_SUBDOMAIN` / `MAXIO_DEFAULT_PRODUCT_FAMILY` at runtime; values live in user-secrets, never in repo files | YOUR CALL — not in the map |
| Retry defaults (do not restate semantics in trap notes) | `RetryOptions.Default()`: methods GET/HEAD/PUT/OPTIONS; MaxRetries 3; Timeout 100s | `Core/Configuration/RetryOptions.cs` |
| Logging | `LogRequestBody` default false. `MAXIOCLIENT_LOG` env var. Assign `LoggerFactory` explicitly | `Core/Configuration/LoggingOptions.cs`; `dotnet-getting-started` |
| Idempotency header | Generator injects `Idempotency-Key: Guid.NewGuid()` on every non-GET — **not** a caller key | `Api/Customers.cs`; `Api/Subscriptions.cs`; `dotnet-getting-started` |
| Real keys | CreateCustomer: uniqueness is body `reference` (no dedicated idempotency param). CreateSubscription: body `reference` (lookup via FindSubscription). Neither operation takes a dedicated idempotency-key parameter | map rows above |

### Application HTTP (YOUR CALL — not in the map)

| Route | Auth | App body / response |
| --- | --- | --- |
| `GET /api/subscription-plans` | JWT any authenticated user | `{ catalogPlans: [ { handle, name, description, price, priceInCents, interval, intervalUnit } ] }` |
| `POST /api/subscriptions` | JWT | request `{ productHandle }` (required). 201 created or 200 existing. body `{ subscription: { id, productHandle, productName, state, priceInCents, price, nextBillingAt, reference } }` |
| `GET /api/my-subscriptions` | JWT | `{ subscriptions: [ …same dto ] }`. No Maxio customer → empty list |

Identity: JWT carries `ClaimTypes.Name` (username) only — exemplar `IdentityTokenClaimService.cs`. Resolve `ApplicationUser` via `UserManager`; Maxio customer `reference` = `user.Id`. Names: `FirstName` = email local-part (fallback `"Shopper"`), `LastName` = `"eShopOnWeb"`, `Email` = `user.Email`.

Subscribe idempotency key: `eshop:{userId}:{productHandle}`. If Find hits, return that subscription (any state). Double-click does not create a second row.

Next billing date: prefer `CurrentPeriodEndsAt`, else `NextAssessmentAt`.

Price to shopper: cents / 100m.

Layering: ApplicationCore port `ISubscriptionBillingService` (no Maxio types). Infrastructure adapter + `AddMaxioBilling`. PublicApi endpoints follow existing `IEndpoint` convention. No local mapping table.

## 3. Trap notes

- **Step 0/1 — HttpClient / client lifetime:** rebuilding the handler pipeline per request vs long-lived factory client will leak sockets or share a disposed handler; the constructor will not tell you which lifetime the wrapper should have. **MUST load maxio-platforms-team/dotnet-client-initialization**
- **Step 1 — Credentials on options:** constructing the client then mutating credentials, or hardcoding the API key, yields 401s that look like “wrong environment”. **MUST load maxio-platforms-team/dotnet-authentication**
- **Step 2/5 — List operations:** `ListProductsForProductFamily` has eight consecutive nullable no-default parameters; a positional call silently binds the wrong skip. **MUST load maxio-platforms-team/dotnet-calling-endpoints**
- **Step 3/4 — Request records:** `CreateCustomer` / `CreateSubscription` inner records mix `required` members with optional `T?`; missing a `required` member is a compile error, sending a null `body` is a runtime provider error. **MUST load maxio-platforms-team/dotnet-models**
- **Step 3 — CustomerError union:** 422 payload `errors` is `Errors1` (CustomerError **or** list of strings); treating it as a single shape drops the message. **MUST load maxio-platforms-team/dotnet-models**
- **Step 2–5 — Error cases mixed:** three Case A operations and two Case B; a single `catch (SdkException<RawError>)` misses typed 422s, and `TryGetRawError` is not a catch-all on typed errors. **MUST load maxio-platforms-team/dotnet-error-handling**
- **Step 6 — JsonException from a drifted/malformed 2xx body:** a missing `required` member on success deserializes as `System.Text.Json.JsonException`, not `SdkException`, so an SDK-exception-only catch ladder lets it escape. **MUST load maxio-platforms-team/dotnet-error-handling**
- **Step 6 — JsonException while constructing a non-2xx error object:** a body that does not match `{Operation}Error` throws `JsonException` *while the error object is being constructed*, replacing the `SdkException` and destroying the HTTP status. **MUST load maxio-platforms-team/dotnet-error-handling**
- **Step 1/6 — Timeout vs retries vs POST:** `Timeout` does not bound the whole call; POST CreateCustomer/CreateSubscription are outside default `HttpMethodsToRetry` while GET lookups are inside; a hung GET costs a multiple of Timeout. **MUST load maxio-platforms-team/dotnet-configuration-resilience**
- **Step 1/6 — Body logging:** JSON request bodies (customer email, names) log unredacted when `LogRequestBody` is on; leaving `LoggerFactory` unset lets `MAXIOCLIENT_LOG` turn that on from outside the process. **MUST load maxio-platforms-team/dotnet-configuration-resilience**
- **Step 6 — Tests:** `MaxioClient` is `sealed`; faking controller types is the wrong seam. **MUST load maxio-platforms-team/dotnet-testing**

## 4. REQUIRED READING

Load every skill below **before implementation starts**. This sheet deliberately does not carry their contents.

- maxio-platforms-team **dotnet-client-initialization** — Step 0/1 client & DI
- maxio-platforms-team **dotnet-authentication** — Step 1 BasicAuth credentials
- maxio-platforms-team **dotnet-calling-endpoints** — Steps 2–5 operation calls (named args, `ct:`)
- maxio-platforms-team **dotnet-models** — Steps 3–4 request records, `Errors1` union, `StringEnum`
- maxio-platforms-team **dotnet-error-handling** — Step 6 catch ladder (always; Case A/B + both JsonException directions)
- maxio-platforms-team **dotnet-configuration-resilience** — Step 1/6 retries, timeout budget, logging, base URL
- maxio-platforms-team **dotnet-testing** — Step 6 HttpClient seam

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bind `IOptions<MaxioOptions>` from section `Maxio`. At registration, refuse to start if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is missing/whitespace. `BaseUrl` may be blank. Basic password is the literal `x` (not a configured secret). Testing host supplies dummy non-blank placeholders in `appsettings.test.json` (not real keys) so existing PublicApi tests still boot. |
| 2 | **Secret sourcing & rotation** | Values from env `MAXIO_API_KEY` / `MAXIO_SITE_SUBDOMAIN` / `MAXIO_DEFAULT_PRODUCT_FAMILY` loaded into PublicApi **user-secrets** (names `Maxio:ApiKey` etc.) and also overlaid onto `IConfiguration` at process start so non-Development hosts work without secrets.json. `AddMaxioClient` builds options **once** in the singleton factory — rotation requires process restart. Never write secret **values** into repo files. |
| 3 | **Total timeout budget** | Keep SDK `RetryOptions.Default()` Timeout (per attempt). Bound each incoming HTTP request with `HttpContext.RequestAborted` passed as `ct`. Writes (POST): one attempt, so budget = that Timeout. Reads (GET): eligible for retry, so budget = Timeout × (MaxRetries+1) unless the request token cancels sooner. No extra linked CTS. |
| 4 | **Write-retry ownership** | `CreateCustomer` and `CreateSubscription` are POST → SDK will **not** resend. `ReadCustomerByReference`, `FindSubscription`, `ListProductsForProductFamily`, `ListCustomerSubscriptions` are GET → SDK **may** resend. Application does not add its own retry around writes. |
| 5 | **Idempotency & ambiguous writes** | **CreateCustomer:** no dedicated key param. Application key = `CreateCustomer.Reference` = Identity user id. Path: Read-by-reference; on 404 create; on 422 re-read (duplicate reference). **CreateSubscription:** no dedicated key param; body `reference` = `eshop:{userId}:{productHandle}`. Path: FindSubscription; on 404 create; on 422 re-find. Generator `Idempotency-Key` is not cited as a key. Ambiguous POST (timeout after send): reconciliation is the same Find/Read, not a retry. |
| 6 | **Observability** | `ILogger` in the adapter: Information for successful ensure-customer / subscribe / list (userId, productHandle, maxio customer id, subscription id, state — no email dump). Warning for 422/404-as-conflict paths and provider errors. Error list strings from `ErrorListResponse1` may be logged. Do **not** enable `LogRequestBody`. Correlation: Case B `RawError.StatusCode` + truncated `ReadAsString()`; Case A typed messages. |
| 7 | **Sensitive data** | In-scope request fields include `email`, `first_name`, `last_name`, `reference`. No card/bank fields are sent. `LogRequestBody` stays **off**. `Logging.LoggerFactory` assigned from DI `ILoggerFactory` in the registration callback so `MAXIOCLIENT_LOG` cannot enable body logging by filling a null factory. Application logs never echo request JSON. |
| 8 | **Environment selection** | Server groups: Production / Ebb / Oauth. Scope = **Production** only. Deployment: `ServerEnvironment.Us` + `Server.Production.Us.Site` = `Maxio:Subdomain` → `https://{site}.chargify.com`. Optional `Maxio:BaseUrl` replaces `Server.Production.Us.BaseUrl` verbatim. Ebb and Oauth unused. No `MaxioApiGateway` (would require Bearer, not Basic). Sandbox traffic stays on the configured subdomain (this machine: site `cp-exp-1` via secrets, not committed). |

## 6. Assumptions & Blockers

**Assumptions**

- Seeded catalog on the bound site already contains family `Maxio:ProductFamilyHandle` and plans (handles, not numeric ids).
- JWT shopper identity is username → `UserManager` → stable `Id` suitable as Maxio customer `reference`.
- Payment-method-not-required on seeded products means CreateSubscription without payment profile is accepted.
- Re-subscribe after cancel using the same `eshop:{userId}:{productHandle}` returns the existing (canceled) row rather than creating a new one — out of hero-flow scope.
- PublicApi-only surface; Web storefront UI is unchanged.

**Blockers**

- None. All needed operations exist on the map (customers, subscriptions, list products for family).

## 7. Repo conventions to imitate (pattern + one exemplar)

| Pattern | Exemplar |
| --- | --- |
| PublicApi `IEndpoint` + `MapGet`/`MapPost` + `.Produces` + `.WithTags` | `src/PublicApi/CatalogTypeEndpoints/CatalogTypeListEndpoint.cs` |
| JWT `[Authorize(..., AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| Split request/response files + `BaseResponse` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.CreateCatalogItemRequest.cs` |
| ApplicationCore port, Infrastructure adapter | `src/Infrastructure/Logging/LoggerAdapter.cs` (and `IAppLogger<T>`) |
| Domain service tests: xunit + NSubstitute | `tests/UnitTests/ApplicationCore/Services/BasketServiceTests/AddItemToBasket.cs` |
| PublicApi boot tests: MSTest + `WebApplicationFactory<Program>` | `tests/PublicApiIntegrationTests/ProgramTest.cs` |
| DI & Identity seed | `src/PublicApi/Program.cs` |
| Exception → HTTP | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
