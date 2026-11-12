# Maxio Advanced Billing plan: eShopOnWeb "Subscribe"

SDK root (plugin-relative): `sdk/dotnet/` (in the Maxio plugin). All `src/...` paths below are relative to it. Map: `sdk-map.md` + `map/operations/*.md`. Business rules: `business-rules/` (plugin root). Project: `src/PublicApi`, `src/Infrastructure`, `src/ApplicationCore`.

Terminology: Maxio "product" = eShop "plan" (handles `eshop-pro`, `basic-plan`), "product family" = `eshop-subscribe`. The API calls them products (`business-rules/every-request.md` catalog-experience rule: UI Plans = API products).

## 1. Scope & sequence

Repo survey (read-only; exemplars to imitate, not snippets):

| Convention | Exemplar file |
| --- | --- |
| Endpoints: MinimalApi `IEndpoint<IResult, TReq, TDeps>` with `AddRoute` + `HandleAsync`, `.Produces<>().WithTags()`, response DTO derived from `BaseResponse`, request from `BaseRequest`; registered by `AddEndpoints()`/`MapEndpoints()` (the Ardalis `EndpointBaseAsync` is used only by `AuthEndpoints/AuthenticateEndpoint.cs`; catalog endpoints, the majority, are MinimalApi) | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (+ its `.Request.cs`/`.Response.cs` files) |
| JWT-protected route: `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` on the lambda | same file |
| Error boundary: `ExceptionMiddleware` maps `DuplicateException` to 409 and everything else to 500 | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
| EF entities + `IEntityTypeConfiguration<T>` picked up via `ApplyConfigurationsFromAssembly` | `src/Infrastructure/Data/CatalogContext.cs`, `src/Infrastructure/Data/Config/BasketConfiguration.cs` |
| DB choice: `UseOnlyInMemoryDatabase` -> in-memory provider (`Catalog`, `Identity` DB names); SQL Server otherwise, with migrations under `Data/Migrations` | `src/Infrastructure/Data/../Dependencies.cs` (`src/Infrastructure/Dependencies.cs`) |
| JWT carries ONLY `ClaimTypes.Name` (user name) and `ClaimTypes.Role`; no `sub`/`NameIdentifier`/email claim | `src/Infrastructure/Identity/IdentityTokenClaimService.cs` |
| Settings: `builder.Configuration.GetRequiredSection(...)` + `Configure<T>` | `src/PublicApi/Program.cs` |

Sequence:

| Step | What | Operations |
| --- | --- | --- |
| 1 | Settings: bind section `Maxio:` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` optional) to a settings type; validate on start (non-blank ApiKey/Subdomain/ProductFamilyHandle; BaseUrl, when set, an absolute http(s) URI); secrets via user-secrets, none in repo or `appsettings*.json` | none |
| 2 | Register `MaxioAdvancedBillingClient` once (singleton), options built at registration: `BasicAuth` (username = `Maxio:ApiKey`, password = `"x"`), `Environment = ServerEnvironment.Us`, `Server.Production.Us.Site = Maxio:Subdomain`, and when `Maxio:BaseUrl` is set `Server.Production.Us.BaseUrl = <value verbatim>`; retry/timeouts/logging per section 5 | none |
| 3 | EF: add `MaxioCustomerLink` (PK `UserId`) and `MaxioSubscriptionClaim` (composite PK `UserId` + `PlanHandle`) to `CatalogContext` with configurations; no migration needed for the in-memory run (SQL Server path would need one: see section 6) | none |
| 4 | `GET /api/subscription-plans` (anonymous or JWT: YOUR CALL, section 6): list non-archived products of the configured family, map to DTO (handle, name, price, interval, trial/setup/payment-method flags) | `Products` via `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` (JWT; body: plan handle only; identity from token name -> `UserManager.FindByNameAsync` -> `ApplicationUser.Id`/`Email`): validate plan handle against the plan list (invariants) -> claim customer link -> ensure Maxio customer (create, or reconcile by reference) -> claim subscription -> create subscription -> record result -> return plan/price/state/next billing date | `ProductFamilies.ListProductsForProductFamily`, `Customers.CreateCustomer`, `Customers.ReadCustomerByReference`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription`, `Subscriptions.ReadSubscription` |
| 6 | `GET /api/my-subscriptions` (JWT): load the user's claims from EF, read each stored subscription id from Maxio, return plan/price/state/next billing date | `Subscriptions.ReadSubscription` |
| 7 | Out of scope (no endpoint in the request): metered component `api-call` usage reporting, cancel, change plan, payment profiles. No capability is missing for the hero flow; the Blockers in section 6 are about plan inputs and config. | none |

## 2. CONTRACT SHEET

> WARNING 1: signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
>
> WARNING 2: every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits (root `MaxioAdvancedBilling`; `.Models`; `.Models.Enums`; `.Errors`; `.Requests.<Controller>`; `.Core.Exceptions`; `.Core.ErrorResponse`; `.Core.Configuration`; `.Core.Authentication.Basic`; `.Servers`).

All operations: auth `options.BasicAuth`; throw-only (no `...Result`); no `Pageable` (plain lists; page via `Page`/`PerPage` yourself); server group `Production`; trailing params `MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken cancellationToken = default`. Source for every row below: map page named in the row plus the model files cited.

| Controller property | Method signature | Request record + members | Body model + fields | Response envelope + fields read | Error case / accessors / payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, ...)` | `ProductFamilyId: string, required` (value is the id OR the handle **prefixed `handle:`**, i.e. `"handle:" + Maxio:ProductFamilyHandle`); `Page: int` (default 1, min 1); `PerPage: int` (default 20, max 200); `IncludeArchived: bool?` (leave unset: archived are excluded); `Filter`, `DateField`, `StartDate`, `EndDate`, `StartDatetime`, `EndDatetime`, `Include` left out (not needed) | none | list of `ProductResponse { Product (product): Product, required }`; `Product` (`MaxioAdvancedBilling.Models.Product`) fields read: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `InitialChargeInCents (initial_charge_in_cents): long?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`, `DefaultProductPricePointId (default_product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` | A: `MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError` via `ApiException<...>` (`MaxioAdvancedBilling.Core.Exceptions`); `TryGetString(out string? value)` [404]; `TryGetRawError(out RawError? error)` [fallback] | none from SDK; manual page loop, empty page or short page ends it; business rules: past-last-page is empty not error, per_page>200 clamps to 200 (`listProductsForProductFamily` rules, `maxio-c4:listProducts.1`, `maxio-c4:listComponents.9`) | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, ...)` | `Body: CreateCustomerRequest?` (nothing marked required on the record; send it) | `CreateCustomerRequest { Customer (customer): CreateCustomer, required }`; `CreateCustomer`: `FirstName: string, required`, `LastName: string, required`, `Email: string, required`, `Reference: string?` (**set it**: the app's stable user key; business rule `maxio-c4:createCustomer.1`: reference must be unique on the site, a duplicate is refused); `Organization: string?` (omitted => Maxio fills it with first+last name, `createCustomer.12`); left out: address/tax/identifier/surcharging/branding fields. Wire names: the `CreateCustomer` file declares no `[JsonPropertyName]` attributes (request-side), so names are produced by the SDK serializer: check on first run with the request log line | `CustomerResponse { Customer (customer): Customer, required }`; `Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | A: `MaxioAdvancedBilling.Errors.CreateCustomerError`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1? value)` [422] (`CustomerErrorResponse1 { Errors (errors): Errors1? }`, `Models/CustomerErrorResponse1.cs`); `TryGetRawError` [fallback] | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `client.Customers` | `Task<CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, ...)` | `Reference: string, required` (query `reference`) | none | `CustomerResponse` as above (`Customer.Id`) | B: `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode` 404 = no such customer (the not-found signal; payload shape not typed) | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs` |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, ...)` | `Body: CreateSubscriptionRequest?` (nothing marked required on the record) | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription, required }`; `CreateSubscription` (all optional): `ProductHandle (product_handle): string?` (**set**: "required unless product_id given"), `CustomerId (customer_id): int?` (**set**: "required unless customer_reference or customer_attributes"; use the stored id; do NOT use `CustomerAttributes`, which would create a customer implicitly, rule `createSubscription.5`), `Reference (reference): string?` (**set**: deterministic per user+plan; the reconcile key), `ProductPricePointHandle`/`ProductPricePointId` left unset (product's default price point, see Assumptions), `PaymentCollectionMethod`, payment profile/credit card/bank fields, `NextBillingAt`, `InitialBillingAt`, `DeferSignup` (default false), `Components`, `Coupon*`, `CustomPrice`, `Currency`, `CalendarBilling` left out. Doc-tied acceptance: a paid product requires a payment method unless the product is configured not to (`createSubscription.7`); plans are stated "payment method not required", verified by `RequireCreditCard`/`RequestCreditCard` == false before the call | `SubscriptionResponse { Subscription (subscription): Subscription? }` (nullable: check); `Subscription` fields read: `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (the next-billing date; there is NO `next_billing_at` on the response model, see Assumptions), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Reference (reference): string?`, `Product (product): Product?` (`Handle`, `Name`), `Customer (customer): Customer?` (`Id`), `CancelAtEndOfPeriod`, `CreatedAt` | A: `MaxioAdvancedBilling.Errors.CreateSubscriptionError`; `TryGetErrorListResponse1(out ErrorListResponse1? value)` [422] (`ErrorListResponse1 { Errors (errors): IReadOnlyList<string>, required }`); `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Models/ErrorListResponse1.cs` |
| `client.Subscriptions` | `Task<SubscriptionResponse> FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, ...)` | query `reference` (member `Reference`; the page lists no required member, so set it) | none | `SubscriptionResponse` as above | A: `MaxioAdvancedBilling.Errors.FindSubscriptionError`; `TryGetNoContent(out RawError? value)` [404 = not found]; `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs` |
| `client.Subscriptions` | `Task<SubscriptionResponse> ReadSubscription(MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest request, ...)` | `SubscriptionId: int, required`; `Include: IReadOnlyList<SubscriptionInclude>?` (unset) | none | `SubscriptionResponse` as above | B: `ApiException<RawError>` (404 = gone) | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/ReadSubscriptionRequest.cs` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle a caller subscribes to must be one this application offers: a non-archived product of the configured family | `Subscriptions.CreateSubscription` (`ProductHandle`) <- `ProductFamilies.ListProductsForProductFamily` | In the POST subscriptions application service, before the customer claim and before `CreateSubscription`: it reads the family's plan list (same code path as `GET /api/subscription-plans`) and rejects a handle not in it with 404/422 (no Maxio write made) |
| The Maxio customer id sent as `CustomerId` must be the one this application stored for the authenticated user | `Subscriptions.CreateSubscription` (`CustomerId`) <- `Customers.CreateCustomer` / `Customers.ReadCustomerByReference` (result stored in `MaxioCustomerLink`) | Before `CreateSubscription`: id read from the user's own `MaxioCustomerLink` row, never from the request body |
| The subscription id read by `my-subscriptions` must be one this application recorded for that user | `Subscriptions.ReadSubscription` (`SubscriptionId`) <- `Subscriptions.CreateSubscription` / `FindSubscription` (result stored in `MaxioSubscriptionClaim`) | In the my-subscriptions service: ids come only from the caller's own claim rows; ownership additionally asserted by comparing the response `Subscription.Customer.Id` / `Reference` with the stored values |
| The product family handle sent to the list call is the configured one, never caller input | `ListProductsForProductFamily` (`ProductFamilyId`) <- `Maxio:ProductFamilyHandle` | At construction of the list request (`"handle:" + settings.ProductFamilyHandle`) |

### Enums actually needed

| Enum (`MaxioAdvancedBilling.Models.Enums.SubscriptionState`, open string enum, static members; branch with `TryGetKnownValue`/`Match`) | Wire |
| --- | --- |
| `Pending` `pending`, `FailedToCreate` `failed_to_create`, `Trialing` `trialing`, `Assessing` `assessing`, `Active` `active`, `SoftFailure` `soft_failure`, `PastDue` `past_due`, `Suspended` `suspended`, `Canceled` `canceled`, `Expired` `expired`, `Paused` `paused`, `Unpaid` `unpaid`, `TrialEnded` `trial_ended`, `OnHold` `on_hold`, `AwaitingSignup` `awaiting_signup` | as listed |
| `IntervalUnit` (for `Product.IntervalUnit`): not value-listed here, read the file `Models/Enums/IntervalUnit.cs` when mapping to a display string; unknown values pass through as raw string | n/a |

### Client construction, auth, server facts

| Fact | Value | Source |
| --- | --- | --- |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`; DI: `services.AddMaxioAdvancedBillingClient(options => ...)` | `sdk-map.md` Getting a client |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <ApiKey>, Password = "x" }` (username is the Maxio API key; password is `x`) | `sdk-map.md` Servers & auth |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `Eu` | `Servers/ServerEnvironment.cs` |
| Server groups | 2: `Production` (`https://{site}.chargify.com` Us / `https://{site}.ebilling.maxio.com` Eu) and `Ebb` (`https://events.chargify.com/{site}`). This scope touches ONLY `Production` (all five operations above) | `sdk-map.md` Servers & auth |
| Subdomain | `options.Server.Production.Us.Site = Maxio:Subdomain` (template default is the literal `"subdomain"`) | `Servers/ProductionOptions.cs` |
| BaseUrl override | `options.Server.Production.Us.BaseUrl = Maxio:BaseUrl`; `BaseUrl` is a template string; a literal URL with no `{placeholders}` is used as-is, so setting it verbatim satisfies "use verbatim as the API base address". Must be set BEFORE constructing the client, on the SELECTED environment's options (Us) or it is ignored | `skills/dotnet/dotnet-configuration-resilience` (ServerOptions section); `Servers/ProductionOptions.cs` |
| `MAXIO_ENVIRONMENT=US` | Not an SDK or `Maxio:` binding key; the plan does not read it. Environment is fixed to `Us`. See Assumptions | n/a |

## 3. Trap notes

| Step | Hazard | Pointer |
| --- | --- | --- |
| 2 | Environment and the `Server` options are captured at different times; setting the override on the wrong environment object or after construction is silently ignored. | MUST load maxio:dotnet-configuration-resilience |
| 2 | Client lifetime, who owns the `HttpClient`, and what DI registration does with the options object. | MUST load maxio:dotnet-client-initialization |
| 2 | Credential binding: a blank half of the Basic credential is not "missing" to the SDK, and an env-var fallback can mask what your config check sees. | MUST load maxio:dotnet-authentication |
| 4-6 | Every `Timeout` knob in the SDK and `HttpClient` bounds something narrower than a whole call; the retried verbs are a short list. | MUST load maxio:dotnet-configuration-resilience |
| 5 | Request-record shape: which members are `required`, which hold the real identifiers, and the one-record-first-parameter rule. | MUST load maxio:dotnet-calling-endpoints |
| 5 | Model construction (`required` init members, open enums, nullable response envelope, `AdditionalProperties`). | MUST load maxio:dotnet-models |
| 5 | A write whose connection fails may already have landed; a claim row must outlive the process and be taken atomically. | MUST load maxio:dotnet-configuration-resilience |
| 4-6 | Which exception types reach a `catch`, Case A vs B accessors, and a body that does not match its declared type. | MUST load maxio:dotnet-error-handling |
| 5 | Request logging can write bodies (customer name and email) to logs; the logger source differs between DI and hand construction. | MUST load maxio:dotnet-configuration-resilience |
| tests | Stubbing the SDK at the `HttpClient` seam and constructing SDK exceptions for the error-path tests. | MUST load maxio:dotnet-testing |

## 4. REQUIRED READING

Load before implementation starts:

- maxio:dotnet-error-handling: every catch ladder (steps 4-6)
- maxio:dotnet-client-initialization: client/DI registration (step 2)
- maxio:dotnet-authentication: Basic credential and startup check (step 2)
- maxio:dotnet-calling-endpoints: request records, cancellation (steps 4-6)
- maxio:dotnet-models: request/response models, open enums (steps 4-6)
- maxio:dotnet-configuration-resilience: base URL, timeouts, retries, logging, unknown outcomes (steps 2, 5)
- maxio:dotnet-testing: tests (all)

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Credentials are bound from `IConfiguration` section `Maxio:` (not the SDK env vars) into a settings type; `ValidateOnStart` (or an explicit guard before `app.Run()`) refuses to boot when `Maxio:ApiKey`, `Maxio:Subdomain` or `Maxio:ProductFamilyHandle` is null/blank (whitespace counts as blank), and when `Maxio:BaseUrl` is set but not an absolute http(s) URI. The Basic password is the constant `x`, so only the ApiKey half can be missing. Messages name the key, never the value. Source: `skills/dotnet/dotnet-authentication` |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` comes from user-secrets in dev (the project already has a `UserSecretsId` in `PublicApi.csproj`) / environment config in deploy; never in repo. The options object is built once at registration and captured by the singleton client, so a rotated key takes effect only after a process restart (no provider used). |
| 3 | Total timeout budget | Per attempt: `options.Retry.Timeout` = 10s and `HttpClient.Timeout` = 10s (both per attempt, neither a total). Total: one linked `CancellationTokenSource` per request (from `HttpContext.RequestAborted`) at one integration-boundary helper: 20s for `GET` endpoints, 45s for `POST /api/subscriptions` (up to 4 SDK calls: plan list, create customer, create subscription, plus a reconcile read), the token passed to every SDK call of the handler. YOUR CALL — not in the map (the numbers); the three-layer mechanics: `dotnet-configuration-resilience`. |
| 4 | Write-retry ownership | `HttpMethodsToRetry` stays default (GET, HEAD, PUT, OPTIONS): `CreateCustomer` and `CreateSubscription` are POST and the SDK never resends them on a transient failure; reads (`ListProductsForProductFamily`, `ReadCustomerByReference`, `FindSubscription`, `ReadSubscription`) may be retried (cap `MaxRetries` at 2). No PUT writes in scope. Do not widen the verb list. Source: `dotnet-configuration-resilience` Retries |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: real key = `CreateCustomer.Reference` (set to the app user id; Maxio refuses duplicate references, rule `maxio-c4:createCustomer.1`), reconcile with `ReadCustomerByReference`. `CreateSubscription`: no request-record idempotency member exists (record has only `Body`; the body has `Reference` which is a lookup key, not a documented dedupe key; the `uniqueness_token` of `every-request` rules is not a member of `CreateSubscriptionRequest`; treat as absent); reconciliation path = `FindSubscription` by `Reference` = `"<userId>-<planHandle>"`. The injected `Idempotency-Key` header is not a key and is not cited. |
| 6 | Observability | `Information`: request/response lines (SDK logger, via the container `ILoggerFactory`), claim taken / claim completed / reconciled, with user id and plan handle. `Warning`: provider 4xx/429, retries, stale-claim reconcile. `Error`: 5xx, deserialization failure, unknown outcome. Correlation: log `ApiException.StatusCode` and, for Case A 422s, the `Errors` strings of `ErrorListResponse1`/`CustomerErrorResponse1` (no PII in them is assumed: UNVERIFIED), plus the app's own `BaseRequest.CorrelationId()`. The provider error bodies carry no documented correlation id in the map; none is invented. Never log the ApiKey or the Authorization header. |
| 7 | Sensitive data | Scope carries customer name and email (request body of `CreateCustomer`, JSON). `LogRequestBody` = false; `LoggerFactory` assigned explicitly (container factory), so `MAXIO_ADVANCED_BILLING_LOG=trace` cannot force body logging; `LogRequestHeaders`/`LogResponseHeaders` false; the `reference` value (user id) appears in URL query of `ReadCustomerByReference`/`FindSubscription` and is masked by the SDK allow-list; our own logs log user id only, not email. No form bodies, so no deny-list entries needed. |
| 8 | Environment selection | Groups touched: `Production` only (`Ebb` untouched). Deployment sets: `Maxio:Subdomain` (sandbox `cp-exp-2`) with `Environment = Us` -> `https://cp-exp-2.chargify.com`; `Maxio:BaseUrl` overrides verbatim when present (also the way to point at a mock). SDK declares no sandbox environment: test traffic is kept away from live by the sandbox site subdomain/API key only. `MAXIO_ENVIRONMENT=US` is not read (maps to `Us`, the default); an EU deployment would need `Eu` selection or a `BaseUrl`, which the keys given do not provide (Assumptions). |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` (customer ensure) | `MaxioCustomerLink` table in `CatalogContext` (EF in-memory provider here), row `{UserId (PK), Reference, MaxioCustomerId?, Status (Pending/Linked/Unknown), ClaimedAtUtc}` inserted and saved BEFORE the SDK call. Provider-side `Reference` uniqueness sits beside the claim, not in place of it. | Primary key on `UserId`: the store refuses the second insert (in-memory provider enforces PKs on `SaveChanges`) | `catch (Microsoft.EntityFrameworkCore.DbUpdateException)` (and `InvalidOperationException` for same-context tracking collision) around the claim `SaveChanges`; on catch, load the existing row: `Linked` -> reuse its id, `Pending` -> 409 in progress (or settle a stale one via `ReadCustomerByReference`) | `SubscriptionService.EnsureCustomerAsync`: claim `SaveChangesAsync` (MaxioCustomerLinks.Add) then `Customers.CreateCustomer`; conflict caught by `IsClaimConflict` |
| `Subscriptions.CreateSubscription` | `MaxioSubscriptionClaim` table in `CatalogContext`, row `{UserId, PlanHandle (composite PK), Reference, MaxioSubscriptionId?, Status (Pending/Created/Unknown/Released), ClaimedAtUtc}` inserted and saved BEFORE the SDK call; row deleted (released) when Maxio refuses with a 422 | Composite primary key `(UserId, PlanHandle)` | `catch (DbUpdateException)` around the claim `SaveChanges`; then read the earlier row: `Created` -> return its subscription idempotently (200, not a second create); `Pending` -> 409 in progress | `SubscriptionService.EnsureSubscriptionAsync`: claim `SaveChangesAsync` (MaxioSubscriptionClaims.Add) then `Subscriptions.CreateSubscription`; conflict caught by `IsClaimConflict` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (plans list; used by `GET /api/subscription-plans` and the plan-handle check) | `PerPage = 200` per page and a page cap of 5 pages (YOUR CALL — not in the map) with a no-progress guard (stop on an empty/short page) | `SubscriptionPlansResponse.Truncated` boolean (true when the cap was hit with a full last page); the plan-handle check treats `Truncated` as "cannot verify" and fails closed (503), not as "not offered" | `SubscriptionService.ListProductsAsync` sets `truncated`; `ListPlansAsync` returns it as `PlanList.Truncated`; `SubscribeAsync` fails closed (503) |
| `ReadSubscription` fan-out in `GET /api/my-subscriptions` (not a paged list, but a cap-and-partial read) | at most 50 claim rows per user read; each read bounded by the call deadline | per-item `Available` flag + `Truncated` on `MySubscriptionsResponse`; an item whose Maxio read failed is returned from the stored claim row with `Available = false`, never dropped silently | `SubscriptionService.ListMySubscriptionsAsync` (`Truncated`) and `ReadExistingAsync` (`Available=false`) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `Customers.ReadCustomerByReference` (404 `ApiException<RawError>` = not created) | `Customer.Reference` = app user id | `SubscriptionService.EnsureCustomerAsync` `catch (Exception) when (IsUnknownOutcome)` -> `TryFindCustomerAsync`, else link `Unknown` | NOT AUTOMATED: settle path verified by code review only; (stub handler throws `HttpRequestException`/times out on the create; assert re-read happens, the link row becomes `Linked`, or `Unknown` when the re-read also fails) |
| `Subscriptions.CreateSubscription` | `Subscriptions.FindSubscription` (404 via `TryGetNoContent` = not created) | `Subscription.Reference` = `"<userId>-<planHandle>"` | `SubscriptionService.EnsureSubscriptionAsync` `catch (Exception) when (IsUnknownOutcome)` -> `TryFindSubscriptionAsync`, else claim `Unknown`; later settled by `ReadExistingAsync` and by the stale-claim branch | NOT AUTOMATED: code review only; (stub handler fails the create connection; assert the claim is settled from the re-read, or left `Unknown` and later settled when the same user calls again, and no second create is sent when the re-read finds it) |

Notes on settling: a claim row left `Pending`/`Unknown` past a lease (YOUR CALL: 2 minutes) is never blindly re-created: the next caller first re-reads by reference and settles the row, then creates only if the provider has nothing. The sweep is that "next caller" path plus the `GET /api/my-subscriptions` path, which settles `Unknown` rows it meets.

Error mapping (application boundary; typed ladder per operation, all also catching `ResponseDeserializationException`/`SdkConnectionException`/`SdkTimeoutException`/`ApiException<RawError>`):

| Condition | App response |
| --- | --- |
| `CreateCustomer` / `CreateSubscription` 422 (`TryGetCustomerErrorResponse1` / `TryGetErrorListResponse1`) | 422 with the provider error strings; claim released |
| Provider 401/403 (`RawError`) | 502 generic (deployment fault), `Error` log; no key echoed |
| Provider 429 or 5xx | 503 (+ no resend of POST); claim kept `Unknown` for subscription create if the request may have landed |
| `SdkTimeoutException` / `SdkConnectionException` on a write | reconcile first; if still unknown: 202-style "pending" is NOT used; return 504/502 with claim `Unknown` |
| `ResponseDeserializationException` | 502, `Error` log with status and target type |
| Plan handle not offered | 404 (no claim taken) |
| Claim conflict, earlier claim `Created` | 200 with the earlier subscription (idempotent) |
| Claim conflict, earlier claim `Pending` | 409 (the app's existing duplicate-to-409 convention) |

## 6. Assumptions & Blockers

Blockers:

1. **"Default" plan input is not definable without hard-coding.** The brief marks `eshop-pro` as "default" but says to hard-code no handle and offers only four settings keys. The map offers no "default product in a family" notion (`Product` has `DefaultProductPricePointId`, which is a price-point default, not a plan default). Plan: `POST /api/subscriptions` requires a plan handle in the body; "default" is interpreted as the product's default price point (no `ProductPricePointHandle` is sent). If a default-plan fallback when the handle is omitted is wanted, the main agent must ask the user for a rule (e.g. a fifth config key) before implementation.
2. **Payment method not required cannot be confirmed from the SDK/map.** The business rule `maxio-c4:createSubscription.7` says Maxio refuses a paid product with no payment method unless the product is configured otherwise. The plan checks `Product.RequireCreditCard`/`RequestCreditCard` before creating and returns a clear 409/422 if the sandbox plan actually requires a card; if the seeded sandbox plans do require one, subscribing cannot succeed without a payment-profile capability that is out of scope. UNVERIFIED until the first live call.

Assumptions (minor unless marked):

- Maxio API shape: next billing date is read from `Subscription.NextAssessmentAt`; the response model has no `next_billing_at` field (`Models/Subscription.cs`). Whether `next_assessment_at` equals the "next billing date" is UNVERIFIED (confirm on the first live read; fall back to `CurrentPeriodEndsAt`).
- `MAXIO_ENVIRONMENT=US` is not among the keys the user listed and the SDK does not read it. Environment is fixed to `Us`; EU would need a `Maxio:BaseUrl` override. Not a blocker for the sandbox.
- Identity: the JWT only carries the user NAME (`ClaimTypes.Name`). User id and email come from `UserManager<ApplicationUser>.FindByNameAsync(name)` on the identity store; no claim name is assumed beyond the one read in `IdentityTokenClaimService.cs`. `ApplicationUser` is an ASP.NET Identity user (id/email read via `IdentityUser` members: confirm at implementation).
- Customer `FirstName`/`LastName` are required by Maxio but the app stores neither (`ApplicationUser` not read in full): YOUR CALL — not in the map (derive from user name/email local part, never fabricate PII beyond that).
- Claim key: composite PK `(UserId, PlanHandle)` as the user asked ("user id (+plan)"): double-click on the same plan is stopped, while subscribing to two different plans is allowed. If one subscription per user is wanted, make the PK `UserId` only. YOUR CALL — not in the map.
- Auth on `GET /api/subscription-plans`: YOUR CALL — not in the map (default anonymous like the catalog GETs; the other two require JWT).
- Persistence: in-memory provider has no transactions and loses claims on restart (no outliving the process there); that is the only store this environment allows, so it is used. A SQL Server deployment would need an EF migration for the two new tables (not written; in-memory only).
- Metered component `api-call` is seeded but unused by the hero flow.
- Pagination caps, timeouts, claim lease and 50-row cap are YOUR CALL — not in the map.
- No project code has been read beyond the exemplars named in section 1.


## 7. Decisions settled during implementation (replaces every YOUR CALL above)

- Default plan (Blocker 1): `POST /api/subscriptions` takes `planHandle`; when omitted it falls back to optional key `Maxio:DefaultPlanHandle` (`MaxioSettings.DefaultPlanHandle`), else 400. Nothing hard-coded.
- Payment method (Blocker 2, resolved live): paid plans are refused by Maxio without a payment method ("No payment method was on file"), so `CreateSubscription` sends `PaymentCollectionMethod = Remittance` (`SubscriptionService.EnsureSubscriptionAsync`); verified live on the sandbox. The earlier `RequireCreditCard` pre-check was removed (the seeded plans report that legacy flag but subscribe fine); Maxio 422 is mapped to HTTP 422.
- Next billing date: `Subscription.NextAssessmentAt`, falling back to `CurrentPeriodEndsAt` (`ToDto`); verified live they agree.
- Customer names: first name = email local part, last name = `eShopOnWeb` (`EnsureCustomerAsync`).
- Claim key: composite PK (UserId, PlanHandle) (`MaxioSubscriptionClaimConfiguration`); customer claim PK UserId.
- `GET /api/subscription-plans` is anonymous; the other two require JWT (endpoint files).
- Budgets: per attempt 10s (`MaxioServiceCollectionExtensions`), total 20s reads / 45s subscribe (`SubscriptionService.Bounded`), claim lease 2 min, plan page cap 5x200, my-subscriptions cap 50.
- Environment fixed to `Us`; `Maxio:BaseUrl` is set verbatim on `Server.Production.Us.BaseUrl`.
- In-memory provider raises a duplicate-key `ArgumentException` (not `DbUpdateException`) on a second claim; `IsClaimConflict` covers both.
