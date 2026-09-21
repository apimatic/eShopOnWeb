# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Additive, parallel capability on `src/PublicApi` (JWT). SDK: `MaxioAdvancedBilling` (.NET, from-source, netstandard2.0). All contract facts below come from the SDK map / map-named source files, not memory.

## 1. Scope & sequence

Clean-architecture layering (mirrors existing repo):

1. **Vendor the SDK for build.** SDK is not on NuGet → copy its generated source into the repo as a normal project `src/MaxioAdvancedBilling/`, add it to `eShopOnWeb.sln`, and `ProjectReference` it from `Infrastructure`. (The temp map-clone stays read-only and is never referenced.)
2. **ApplicationCore** — billing abstraction + plain DTOs (no SDK types leak past Infrastructure):
   - `Interfaces/ISubscriptionBillingService.cs` — `GetPlansAsync`, `SubscribeAsync`, `GetMySubscriptionsAsync`.
   - `Entities/SubscriptionBilling/` DTOs: `SubscriptionPlanInfo`, `SubscriptionInfo`, `SubscribeRequestModel`, `SubscribeOutcome`.
   - `Exceptions/SubscriptionBillingException.cs` (+ `PlanNotFoundException`) — domain-level, thrown by the service, translated to HTTP by PublicApi.
3. **Infrastructure/Maxio** — `MaxioSettings` (options), `MaxioSettingsValidator` (fail-fast), `MaxioServiceCollectionExtensions` (registers SDK client + service), `MaxioSubscriptionBillingService` (implements the interface via the SDK), per-user idempotency lock.
   - Operations used: `ListProductsForProductFamily` (plans) · `ReadCustomerByReference` + `CreateCustomer` (ensure customer, idempotent) · `ListCustomerSubscriptions` (subscription idempotency) · `CreateSubscription` (enroll).
4. **PublicApi/SubscriptionEndpoints** — three `IEndpoint` endpoints (repo's MinimalApi.Endpoint convention), all `[Authorize(AuthenticationSchemes=JwtBearer)]`, caller identity = `User` name claim:
   - `GET /api/subscription-plans` → `GetPlansAsync`
   - `POST /api/subscriptions` → `SubscribeAsync` (body `{ planHandle }`)
   - `GET /api/my-subscriptions` → `GetMySubscriptionsAsync`
5. **Program.cs** — call `AddMaxioSubscriptionBilling(configuration)`.
6. **Tests** — `tests/UnitTests` service tests faking the SDK seam (HttpClient) or the service via the abstraction; endpoint smoke via existing FunctionalTests pattern where feasible.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, **verbatim**. Every parameter name is the literal C# identifier; the cancellation-token param is named `ct`, so named args write `ct:`. Pass every no-default nullable explicitly (`null` to skip).
> ⚠ Every SDK type is written fully-qualified from the namespace its source path implies (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`, root → `MaxioAdvancedBilling`, `Errors/` → `MaxioAdvancedBilling.Errors`).

| Operation | Signature (verbatim) | Request model + fields used | Response envelope → inner fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = `Maxio:ProductFamilyHandle` (handle accepted); all filters `null`; `includeArchived: false` (**purpose:** hide archived plans); paginate to collect all | `IReadOnlyList<ProductResponse>` → each `.Product`: `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `Id`, `Description` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404 family not found] · `TryGetRawError(out RawError)` [fallback] | page/perPage (default 1/20) — loop until a short page | map/operations/ProductFamilies.md; Models/Product.cs |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop user identity (name claim) | `CustomerResponse` → `.Customer`: `Id`, `Reference`, `Email` | **Case B** `SdkException<RawError>` — `.StatusCode==NotFound` ⇒ customer absent (expected, not fatal) | none | map/operations/Customers.md; Models/Customer.cs |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer = CreateCustomer{ FirstName(req), LastName(req), Email(req), Reference = user identity } }` — Reference **purpose:** app-side idempotency key linking eShop user ↔ Maxio customer | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Customers.md; Models/CreateCustomer.cs |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` from ensured customer | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription`: `Product.Handle`, `State`, `Id`, `CurrentPeriodEndsAt`, `ProductPriceInCents` | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md; Models/Subscription.cs |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ Subscription = CreateSubscription{ CustomerId = ensured id, ProductHandle = requested plan handle } }`. **Omit** `PaymentCollectionMethod` (**omit → provider default**; plans are payment-method-not-required so provider makes it active w/o card). **Omit** all card/trial/date fields. | `SubscriptionResponse` → `.Subscription`: `Id`, `State`, `Product.Name/Handle`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md; Models/CreateSubscription.cs |
| `client.Customers.ListCustomers` (fallback only, unused unless needed) | — | — | — | Case B | page/perPage 1/50 | map/operations/Customers.md |

Enums needed (read-back only, `StringEnum<T>` — compare via `.Value` / static members; PascalCase members):
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState` — `Active`, `Trialing`, `Pending`, `Assessing`, `PastDue`, `Canceled`, `Expired`, `OnHold`, `Suspended`, `Unpaid`, `TrialEnded`, `SoftFailure`, `Paused`, `FailedToCreate`, `AwaitingSignup`. **"Live/entitled" set for idempotency** = `Active`, `Trialing`, `Pending`, `Assessing` (transient-to-live). Source: Models/Enums/SubscriptionState.cs.
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (read-back of plan interval unit) — Source: Models/Enums/IntervalUnit.cs (read at impl).

Client construction / auth / server (from sdk-map.md *Getting a client* + *Servers & auth*, ServiceCollectionExtensions.cs, ProductionOptions.cs):
- `services.AddMaxioAdvancedBillingClient(options => {...})` registers a **singleton** `MaxioAdvancedBillingClient` over `IHttpClientFactory` (options built once at registration).
- Auth = **Basic**: `options.BasicAuth = new BasicAuthCredentials { Username = Maxio:ApiKey, Password = "x" }` (map: username=API key, password=`x`; Basic valid only US/EU).
- `options.Environment = ServerEnvironment.Us` (task fixes sandbox = US).
- Site: `options.Server.Production.Us.Site = Maxio:Subdomain`.
- BaseUrl override: when `Maxio:BaseUrl` set, `options.Server.Production.Us.BaseUrl = Maxio:BaseUrl` (used verbatim; template substitutes `{site}` only if present).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| The `planHandle` POST /api/subscriptions accepts as `CreateSubscription.ProductHandle` must be one of the handles `ListProductsForProductFamily` returns | `CreateSubscription` ← `ListProductsForProductFamily` | implementation — service resolves plan list first, 400/`PlanNotFoundException` if the requested handle is not present |

## 3. Trap notes

- **DI registration builds options once & captures in a singleton** → a rotated `Maxio:ApiKey` needs a process restart; also `HttpClient` must come from `IHttpClientFactory` and be long-lived, not per-request. `MUST load dotnet-client-initialization`.
- **Basic credential shape / when credentials are applied** (unset credential is silently skipped → failure looks like "no cred sent", not "bad cred"). `MUST load dotnet-authentication`.
- **List/search ops have many no-C#-default nullable params that mis-bind positionally** — call `ListProductsForProductFamily` / `ListCustomerSubscriptions` with named arguments. `MUST load dotnet-calling-endpoints`.
- **Building request bodies / reading `StringEnum<T>` & optional nullability** (enums are not C# enums; response fields are `T?`; `AdditionalProperties` bag). `MUST load dotnet-models`.
- **Error boundary**: which exception each op throws (Case A typed vs Case B raw), and `TryGet…` mechanics; plus the two `JsonException` directions below. `MUST load dotnet-error-handling`.
- **`Timeout` is per-attempt, `POST` is not retried by default, `LogRequestBody` logs JSON unredacted** — tune before shipping. `MUST load dotnet-configuration-resilience`.
- **Faking the SDK = the `HttpClient` ctor arg is the seam** — match existing xUnit/Moq style. `MUST load dotnet-testing`.

## 4. REQUIRED READING (load all before implementation; contents deliberately not restated here)

- `maxio-platforms-team:dotnet-client-initialization` — SDK client construction + DI singleton/HttpClient lifetime (step 3).
- `maxio-platforms-team:dotnet-authentication` — Basic credential wiring (step 3).
- `maxio-platforms-team:dotnet-calling-endpoints` — named-arg calls to list ops + create ops (step 3 service).
- `maxio-platforms-team:dotnet-models` — building `CreateCustomer`/`CreateSubscription`, reading `StringEnum`/nullable response fields (step 3 service).
- `maxio-platforms-team:dotnet-error-handling` — Case A/B catch ladder + `JsonException` directions (step 3 error boundary).
- `maxio-platforms-team:dotnet-configuration-resilience` — retries/timeout/logging posture (step 3 registration).
- `maxio-platforms-team:dotnet-testing` — SDK seam faking (step 6 tests).

**Two mandatory `JsonException` hazards at the error boundary** (both distinct from `SdkException`):
1. A drifted/malformed **2xx** body (missing a `required` member) surfaces as `System.Text.Json.JsonException` from deserialization, **not** an `SdkException` — an SDK-exception-only catch ladder lets it escape. Catch it.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is constructed**, replacing the `SdkException` and destroying the HTTP status. Catch it too.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettingsValidator` (`IValidateOptions`/startup check) refuses host start if `Maxio:ApiKey`, `Maxio:Subdomain`, or `Maxio:ProductFamilyHandle` is missing **or blank** (each checked separately). `Maxio:BaseUrl` optional. Prevents discovering a bad cred as a first-call 401. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (agent loads `MAXIO_*` env values into user-secrets for the PublicApi UserSecretsId; values never in repo). Options built once at DI registration & captured in the client singleton → **rotation requires a process restart**; acceptable for this sandbox integration and documented. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. The service passes a `CancellationToken` from the request (ASP.NET `RequestAborted`) into every SDK call so a whole call is bounded by the HTTP request lifetime; retries left at SDK default (GET/PUT only), so a POST is a single attempt. |
| 4 | Write-retry ownership | Writes in scope: `CreateCustomer` (POST), `CreateSubscription` (POST). Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS ⇒ **SDK never resends these POSTs** — good, no accidental double-charge from transport retry. Reads (`ReadCustomerByReference`, list ops = GET) may be retried, which is safe. |
| 5 | Idempotency & ambiguous writes | Neither `CreateCustomer` nor `CreateSubscription` takes a real caller-supplied idempotency key (injected `Idempotency-Key` GUID is not one). **Reconciliation path:** (a) customer — `ReadCustomerByReference(userIdentity)` before create, create only on NotFound, `Reference` links them; (b) subscription — `ListCustomerSubscriptions` before create, reuse any live-state subscription for the same product handle. Both wrapped in a **per-user-identity in-process lock** (`SemaphoreSlim` keyed by identity) so a double-click serializes and the check-then-create is atomic **within a run**. In-memory DB caveat: no cross-restart map, but Maxio itself is the source of truth via `Reference`, so restart re-discovers the customer. `YOUR CALL — not in the map`. |
| 6 | Observability | `ILogger` logs at Information: customer ensured (id+reference), subscription created (id/state/plan); Warning on 422/validation; Error on unexpected. Provider error correlation: `RawError.ReadAsString()` / typed error messages logged on failure. `LogRequestBody` left **off**. |
| 7 | Sensitive data | Request models in scope (`CreateCustomer`, `CreateSubscription`) carry name/email (PII) but **no card/bank data** (all payment-profile fields omitted; plans are payment-method-not-required). Posture: `LogRequestBody` stays off and `options.Logging.LoggerFactory` is set explicitly (from DI) so `MAXIOADVANCEDBILLINGCLIENT_LOG` env cannot switch body logging on; own logs never echo request bodies. |
| 8 | Environment selection | Only the `Production` server group is touched. `ServerEnvironment.Us` (sandbox), base URL `https://{site}.chargify.com` with `{site}` = the configured `Maxio:Subdomain` (or `Maxio:BaseUrl` verbatim). No live-system risk: the configured sandbox site is the only target; there is no separate "prod vs sandbox" env in this SDK beyond the site, so test traffic is confined by the configured subdomain/BaseUrl. |

## 6. Assumptions & Blockers

- **Assumption:** eShop user identity for the Maxio customer `reference` = the JWT name claim (`ClaimTypes.Name`, the username/email), which `IdentityTokenClaimService` puts in the token. Stable per user. For `CreateCustomer` required `FirstName`/`LastName`/`Email`: use the identity as email (eShop usernames are emails) and derive names; enrich from Identity user record if available.
- **Assumption:** `planHandle` is supplied in the POST body (shopper picks from the plan list). Validated against the family plan list. No hardcoded catalog handle in code.
- **Blockers:** none. Every operation the flow needs exists on the map.

## 7. Source labels — legend used above: map page or declaring file = verified fact; `YOUR CALL — not in the map` = application decision (rows 5 idempotency/lock, layering); no `UNVERIFIED` rows (all contract facts resolved from source).
