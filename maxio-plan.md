# Maxio subscription billing — eShopOnWeb PublicApi

## 1. Scope & sequence

Additive JWT endpoints on `src/PublicApi` under `/api/`. Maxio is system of record; no local userId↔subscription table.

| Step | What | Operations |
| --- | --- | --- |
| 0 | SDK build reference + `Maxio:` options + fail-fast + `AddMaxioClient` | client construction / Basic auth / Production server |
| 1 | `GET /api/subscription-plans` | `ProductFamilies.ListProductsForProductFamily` |
| 2 | `POST /api/subscriptions` — ensure customer | `Customers.ReadCustomerByReference` then `Customers.CreateCustomer` |
| 3 | `POST /api/subscriptions` — enroll | `Products.ReadProductByHandle`; `Subscriptions.FindSubscription`; `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` | `Customers.ReadCustomerByReference` then `Customers.ListCustomerSubscriptions` |
| 5 | Tests (HttpClient seam + PublicApi JWT tests) | same operations, faked transport |

**Repo conventions (pattern + one exemplar):**

| Pattern | Exemplar |
| --- | --- |
| Minimal API `IEndpoint` + JWT `[Authorize]` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| List GET endpoint | `src/PublicApi/CatalogBrandEndpoints/CatalogBrandListEndpoint.cs` |
| Request/response DTOs + `BaseResponse` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.CreateCatalogItemRequest.cs` |
| JWT identity (`ClaimTypes.Name` = username) | `src/Infrastructure/Identity/IdentityTokenClaimService.cs` |
| Authenticate for bearer token | `src/PublicApi/AuthEndpoints/AuthenticateEndpoint.cs` |
| Application service + interface | `src/ApplicationCore/Interfaces/IBasketService.cs` + `src/ApplicationCore/Services/BasketService.cs` |
| Options POCO | `src/ApplicationCore/CatalogSettings.cs` |
| Host DI / config | `src/PublicApi/Program.cs` |
| Exception → HTTP | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
| Unit tests (xunit + NSubstitute) | `tests/UnitTests/ApplicationCore/Services/BasketServiceTests/AddItemToBasket.cs` |
| PublicApi tests (MSTest + WAF) | `tests/PublicApiIntegrationTests/AuthEndpoints/AuthenticateEndpointTest.cs` |
| Shopper token helper | `tests/PublicApiIntegrationTests/ApiTokenHelper.cs` |

---

## 2. CONTRACT SHEET

Signatures are generated code, verbatim. Every parameter name is the literal C# identifier (the cancellation-token parameter really is named `ct`, so named arguments write `ct:`).

Every SDK type below is fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

### Operations

| Controller · method | Signature (verbatim) | Request | Response fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `ProductFamilies` · `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `dateField`…`include` must be passed explicitly (`null` to skip) | `productFamilyId`: family's id **or** handle prefixed with `handle:` (`Api/ProductFamilies.cs` `<param>`). Optional filters omitted (`null`). `page`/`perPage` used to walk pages (`perPage=200` max). | `IReadOnlyList<Maxio.Models.ProductResponse>` → `.Product` (`required`): `Handle` (`handle`), `Name` (`name`), `Description` (`description`), `PriceInCents` (`price_in_cents`), `Interval` (`interval`), `IntervalUnit` (`interval_unit`), `ArchivedAt` (`archived_at`) | Case A `Maxio.Core.Exceptions.SdkException<Maxio.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out Maxio.Core.ErrorResponse.RawError)` | Not a `Pageable`; caller loops `page` | `map/operations/ProductFamilies.md`; `Api/ProductFamilies.cs`; `Models/ProductResponse.cs`; `Models/Product.cs`; `Errors/ListProductsForProductFamilyError.cs` |
| `Products` · `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `apiHandle` = product handle from our POST body | same `Product` fields as above | Case B `SdkException<RawError>` (`StatusCode`, `ReadAsBytes`, `ReadAsString`, `ReadAsJson<T>`) | none (default) | `map/operations/Products.md`; `Models/ProductResponse.cs` |
| `Customers` · `ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = shopper username from JWT | `Maxio.Models.CustomerResponse.Customer` (`required`): `Id` (`id`, `int?`), `Reference` (`reference`), `Email` (`email`) | Case B `SdkException<RawError>` — 404 is unmatched lookup | none | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `Customers` · `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must be passed explicitly | Envelope `Maxio.Models.CreateCustomerRequest` `required Customer` (`customer`) → `Maxio.Models.CreateCustomer`: **required** `FirstName` (`first_name`), `LastName` (`last_name`), `Email` (`email`); optional used: `Reference` (`reference`). Left out: address, locale, tax, parent, salesforce, branding, cc, org, phone, vat, surcharging | same `Customer` fields as Read | Case A `SdkException<Maxio.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out Maxio.Models.CustomerErrorResponse1)` [422] · `TryGetRawError`. 422 `.Errors` is `Maxio.Models.AnyOf.Errors1`: `TryGetCustomerError(out Maxio.Models.CustomerError)` (`customer` string) or `TryGetListOfString(out IReadOnlyList<string>)` | none | `map/operations/Customers.md`; `Api/Customers.cs` (reference **must be unique**); `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Errors/CreateCustomerError.cs`; `Models/CustomerErrorResponse1.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `Subscriptions` · `FindSubscription` | `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `reference` must be passed explicitly | query `reference` = `{customerReference}:{productHandle}` | `Maxio.Models.SubscriptionResponse.Subscription` (**nullable**): `Id` (`id`), `State` (`state`), `ProductPriceInCents` (`product_price_in_cents`), `NextAssessmentAt` (`next_assessment_at`), `CurrentPeriodEndsAt` (`current_period_ends_at`), `Reference` (`reference`), nested `Product` (`product`) | Case A `SdkException<Maxio.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Errors/FindSubscriptionError.cs` |
| `Subscriptions` · `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must be passed explicitly | Envelope `Maxio.Models.CreateSubscriptionRequest` `required Subscription` (`subscription`) → `Maxio.Models.CreateSubscription` (nothing `required` on the inner record). Fields we send (docs tie acceptance to these): `ProductHandle` (`product_handle`) **or** `ProductId`; `CustomerId` (`customer_id`) **or** `CustomerReference` (`customer_reference`) **or** `CustomerAttributes`; `Reference` (`reference`) for our idempotency key; `PaymentCollectionMethod` (`payment_collection_method`) = `CollectionMethod.Remittance` so the first period is invoiced rather than collected automatically (no card on file). Left out: payment profile/card/bank, coupons, components, custom price, billing dates, metafields, offer, prepaid, 3DS, agreement | same `Subscription` fields as Find. Docs: payment info may be required depending on product options; catalog here is payment-method-not-required | Case A `SdkException<Maxio.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out Maxio.Models.ErrorListResponse1)` [422] (`errors`: `IReadOnlyList<string>`) · `TryGetRawError` | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/Enums/CollectionMethod.cs`; `Models/ErrorListResponse1.cs`; `Errors/CreateSubscriptionError.cs` |
| `Customers` · `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` = Maxio customer `Id` | `IReadOnlyList<SubscriptionResponse>` — same inner fields | Case B `SdkException<RawError>` | none (no page params) | `map/operations/Customers.md`; `Models/SubscriptionResponse.cs` |

### Enums used

| Type | Members needed | Source |
| --- | --- | --- |
| `Maxio.Models.Enums.IntervalUnit` (`StringEnum`, not a C# enum) | `Day`=`day`, `Month`=`month`; read `.Value` | `Models/Enums/IntervalUnit.cs` |
| `Maxio.Models.Enums.CollectionMethod` | `Automatic`=`automatic`, `Remittance`=`remittance`, `Prepaid`=`prepaid`, `Invoice`=`invoice`. Relationship Invoicing uses remittance / automatic / prepaid. We send `Remittance`. | `Models/Enums/CollectionMethod.cs`; `Models/CreateSubscription.cs` |
| `Maxio.Models.Enums.SubscriptionState` | `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup`; read `.Value` for API JSON | `Models/Enums/SubscriptionState.cs` |
| `Maxio.Servers.ServerEnvironment` | `Us`=`US` (default), `Eu`, `MaxioApiGateway` | `Servers/ServerEnvironment.cs`; `sdk-map.md` Servers & auth |

### Client construction / auth / server

| Fact | Value | Source |
| --- | --- | --- |
| Client | `Maxio.MaxioClient(HttpClient httpClient, Maxio.MaxioClientOptions options)` only ctor; DI `AddMaxioClient` | `sdk-map.md`; `MaxioClient.cs`; `ServiceCollectionExtensions.cs` |
| Auth (this scope) | Basic: `options.BasicAuth = new Maxio.Core.Authentication.Basic.BasicAuthCredentials { Username = ApiKey, Password = "x" }`. Do not set Bearer. Us/EU only; gateway rejects Basic | `sdk-map.md` Servers & auth; `MaxioClientOptions.cs` |
| Environment | `ServerEnvironment.Us` | `sdk-map.md`; task sandbox uses chargify.com US hosting |
| Site | `options.Server.Production.Us.Site` = `Maxio:Subdomain` (template default `"subdomain"`) | `sdk-map.md`; `Servers/ProductionOptions.cs`; `ServerOptions.cs` |
| Base URL override | When `Maxio:BaseUrl` is non-blank, set `options.Server.Production.Us.BaseUrl` to that string verbatim (default template `https://{site}.chargify.com`) | `sdk-map.md`; `Servers/ProductionOptions.cs`; task |
| Server group | All in-scope ops use **Production** (default) | `sdk-map.md` defaults table |
| Retry defaults | `HttpMethodsToRetry` = GET, HEAD, PUT, OPTIONS; `MaxRetries`=3; `Timeout`=100s; `RetryOptions.Default()` / `Disabled()` | `Core/Configuration/RetryOptions.cs`; `sdk-map.md` |
| Logging | `Maxio.Core.Configuration.LoggingOptions`: `LogRequestBody`, `LoggerFactory`; env `MAXIOCLIENT_LOG` | `Core/Configuration/LoggingOptions.cs`; `dotnet-getting-started` |
| Injected header | Every non-GET sends `Idempotency-Key: Guid.NewGuid()` — **not** a real key | `Api/Customers.cs`, `Api/Subscriptions.cs`; `dotnet-getting-started` Idempotency |

### Application API (YOUR CALL — not in the map)

| Item | Decision |
| --- | --- |
| Identity | JWT `ClaimTypes.Name` (username). Seeded shopper `demouser@microsoft.com` |
| Maxio customer `reference` | that username (stable across in-memory Identity restarts; user GUID is not) |
| Maxio subscription `reference` | `{customerReference}:{productHandle}` — double-click same plan returns the existing subscription |
| Subscribe body | `{ "productHandle": "<handle>" }` — handles only, never numeric Maxio ids |
| Collection method | `CollectionMethod.Remittance` on every CreateSubscription — YOUR CALL using a field that is on the model. Automatic collection 422s without a card even when the product does not require a payment method. |
| Next billing date | `NextAssessmentAt` if present, else `CurrentPeriodEndsAt` |
| Price on plans | `PriceInCents` / 100m + `Interval`/`IntervalUnit` |
| Price on subscription | `ProductPriceInCents` / 100m; plan handle/name from nested `Product` |
| Auth on all three routes | `[Authorize(AuthenticationSchemes = JwtBearer)]` — any authenticated shopper, not Administrators |
| Persistence | none local; Maxio is the mapping |
| SDK reference | ProjectReference to a build copy of the unpublished SDK (`Maxio.csproj`), CPM disabled on that project |

---

## 3. Trap notes

Do not treat these as how-to. Load the named skill before writing the corresponding code.

| Step | Hazard | Skill |
| --- | --- | --- |
| 0 DI | HttpClient/handler pipeline lifetime vs the SDK client wrapper lifetime — rebuilding the pipeline per request exhausts sockets | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-client-initialization` |
| 0 auth | Which `MaxioClientOptions` credential property is the Chargify API key, which value is the Basic password, and what using Bearer/Gateway on a Us site does to every call | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-authentication` |
| 1,4 lists | List/lookup operations with many optional parameters that have no C# default — a positional call mis-binds | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-calling-endpoints` |
| 2–3 writes | Whether CreateCustomer/CreateSubscription take a real caller-supplied idempotency parameter (vs the generator-injected header) | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-calling-endpoints` |
| 1–4 models | Envelope wrappers, `required` inner records, `StringEnum<T>` (not C# enums), `Errors1` union accessors, nullable `SubscriptionResponse.Subscription` | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-models` |
| 1–4 errors | Each operation is Case A or Case B with a different catch type; a ladder that only catches one case lets the other escape | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-error-handling` |
| 0 config | What `Retry.Timeout` actually bounds, which methods the SDK will resend, and that JSON bodies are logged unredacted when `LogRequestBody` is on | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-configuration-resilience` |
| 5 tests | Which constructor argument is the test seam; asserting real behaviour without depending on SDK internals | **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-testing` |

**Always (error boundary — both directions of `System.Text.Json.JsonException`):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it escape. **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-error-handling`
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it. **MUST load** `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-error-handling`

---

## 4. REQUIRED READING

Load every skill below **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
| --- | --- |
| `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-client-initialization` | Step 0 — constructing / DI-registering `MaxioClient` |
| `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-authentication` | Step 0 — BasicAuth credentials |
| `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-calling-endpoints` | Steps 1–4 — named arguments, write keys |
| `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-models` | Steps 1–4 — envelopes, required members, enums, unions |
| `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-error-handling` | Steps 1–4 + host boundary — Case A/B, both `JsonException` paths |
| `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-configuration-resilience` | Step 0 — retries, timeout budget, BaseUrl, logging |
| `marketplace/plugins/maxio-platforms-team/skills/dotnet/dotnet-testing` | Step 5 — HttpClient seam, assertions |

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bind `MaxioOptions` from section `Maxio` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) in PublicApi startup. Refuse to start if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is missing or whitespace. `BaseUrl` may be blank. Basic password is the documented constant `x`, not a configured part. |
| 2 | **Secret sourcing & rotation** | Values come from user-secrets (loaded from the process env at setup time) and from runtime env mapped onto `Maxio:*`. `AddMaxioClient` builds options once and the singleton captures them — rotation requires process restart. Never write secret **values** into repo files. |
| 3 | **Total timeout budget** | Set `Retry.Timeout` to 15s (per attempt). Keep default methods-to-retry (GET retries, POST does not). `MaxRetries` = 2 (up to 3 GET attempts ⇒ 45s worst case on lists). Every SDK call is passed `HttpContext.RequestAborted`. Subscribe is two POSTs + GETs on one request token — the request abort is the whole-call bound. |
| 4 | **Write-retry ownership** | `CreateCustomer` and `CreateSubscription` are POST — SDK will not resend under default `HttpMethodsToRetry`. We will not add POST to that list. Lookups/lists (GET) may be resent by the SDK. |
| 5 | **Idempotency & ambiguous writes** | **CreateCustomer:** real key is body `customer.reference` (JWT username). Path: Read-by-reference; if 404, Create; if 422, Read-by-reference again and use that customer. **CreateSubscription:** real key is body `subscription.reference` (`{username}:{productHandle}`). Path: FindSubscription; if 404, Create; if 422, Find again. The injected `Idempotency-Key` header is not a key. Neither operation has a separate idempotency parameter in its signature. |
| 6 | **Observability** | `ILogger` on the billing service: Information for operation + product handle + Maxio ids; Warning for 422/404-reconcile; Error for unexpected statuses. Log Case A `ErrorListResponse1.Errors` / `Errors1` string payloads and Case B `StatusCode`. Do not enable `LogRequestBody`. No provider request-id field exists on these error models — correlate with our `BaseResponse` correlation id + Maxio resource ids. |
| 7 | **Sensitive data** | `CreateCustomer` carries `email`, names (PII). Subscribe request does **not** send card/bank fields. `LogRequestBody` stays **off** and `LoggerFactory` is assigned explicitly from DI so `MAXIOCLIENT_LOG` cannot enable body logging. Application logs use customer **reference** (username), not a reconstructed request body. |
| 8 | **Environment selection** | Server groups: Production / Ebb / Oauth. This scope is **Production** only. Deployments: `Environment=Us`, `Server.Production.Us.Site=Maxio:Subdomain`, optional `Server.Production.Us.BaseUrl=Maxio:BaseUrl`. Not `MaxioApiGateway` (Bearer/connector). Sandbox is a Us site, not a separate enum member. |

---

## 6. Assumptions & Blockers

**Assumptions**

- All three HTTP routes are JWT-authenticated shopper routes (not admin-only).
- Callers identify a plan by **handle** (`eshop-pro`, `basic-plan`); numeric Maxio ids are never accepted or persisted.
- `productFamilyId` is passed as `handle:{Maxio:ProductFamilyHandle}` per `ListProductsForProductFamily` `<param>` docs.
- Omitting payment-profile / card fields is required (no card capture in this flow). Live create with default (automatic) collection returned 422 `"No payment method was on file for the $299.00 balance"`. We send `payment_collection_method=remittance` from the CreateSubscription model so Maxio invoices the first period instead of collecting it. We do not invent a payment-profile / 3-DS path.
- A shopper may hold one subscription **per product handle**; re-POST of the same handle returns the existing subscription (200).
- No Maxio customer → `GET /api/my-subscriptions` returns an empty list (not 404).

**Blockers**

- None. The map exposes list-products-for-family, customer lookup/create-by-reference, subscription lookup/create-by-reference, and list-customer-subscriptions.

**UNVERIFIED** (live traffic only; defensive coding)

- Exact 422 text when `reference` collides on customer or subscription — treat any 422 on those creates as “reconcile via lookup”, not as a hard failure, unless lookup still 404s.
- Whether `NextAssessmentAt` is always populated when payment is not required — fall back to `CurrentPeriodEndsAt`, then null.

---

## 7. Source index

All contract rows cite a map page or map-named declaring file, `UNVERIFIED`, or `YOUR CALL — not in the map` (see tables above).
