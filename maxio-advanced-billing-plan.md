# Maxio Advanced Billing — integration plan (eShopOnWeb subscription billing)

Additive, parallel capability: recurring-subscription billing with Maxio as system of record.
Three JWT-authenticated endpoints on `src/PublicApi`. Caller identity comes from the token
(`ClaimTypes.Name` = eShop username = email). Maxio is reached only through the vendored
`MaxioAdvancedBilling` .NET SDK, wrapped behind an `ApplicationCore` abstraction.

## 1. Scope & sequence

1. **Vendor the SDK** into `src/MaxioAdvancedBilling.Sdk/` (source build, not on NuGet), isolated
   from the repo's central package management with its own `Directory.Packages.props`
   (`ManagePackageVersionsCentrally=false`); its csproj keeps `netstandard2.0` + explicit versions.
2. **ApplicationCore** — abstraction + domain models (no SDK dependency):
   `ISubscriptionBillingService`, `SubscriptionPlan`, `CustomerSubscription`, `SubscribeResult`.
3. **Infrastructure/Maxio** — `MaxioSettings`, `MaxioBillingService : ISubscriptionBillingService`
   (wraps the SDK client), `MaxioServiceCollectionExtensions.AddMaxioBilling` (bind + fail-fast +
   register SDK client via `AddMaxioAdvancedBillingClient` + register the service).
4. **PublicApi** — three endpoints + response DTOs, following the `IEndpoint<>` convention:
   - `GET  /api/subscription-plans`  → `ProductFamilies.ListProductsForProductFamily("handle:{family}")`
   - `POST /api/subscriptions`       → ensure customer (idempotent) → dedup existing → `Subscriptions.CreateSubscription`
   - `GET  /api/my-subscriptions`    → `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions`
5. Wire `AddMaxioBilling` into PublicApi `Program.cs`; load creds into user-secrets.
6. Tests: unit-test `MaxioBillingService` against a faked `HttpClient` seam.

**Hero flow (POST /api/subscriptions):** resolve identity → `ReadCustomerByReference(ref)`; on 404
`CreateCustomer` (on 422 reference-conflict from a concurrent create, re-read by reference) →
`ListCustomerSubscriptions` and if a *live* subscription to the same product handle already exists
return it (double-click safe; no idempotency key exists on CreateSubscription — reconciliation is the
path) → else `CreateSubscription({subscription:{product_handle, customer_id}})` → return
plan/price/state/next-billing-date.

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
> identifier; the cancellation-token parameter is literally named `ct`, so named args write `ct:`.
> Nullable params with no default **must be passed explicitly** (pass `null` to skip).
> ⚠ Every SDK type is written **fully-qualified against the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `.Models.Enums`,
> `Errors/` → `.Errors`, root → `MaxioAdvancedBilling`, `Servers/` → `.Servers`,
> `Core/Authentication/Basic/` → `.Core.Authentication.Basic`).

| Op | Signature (verbatim) | Request → fields used | Response → fields read | Error case + accessors | Pag. | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductsForProductFamily` | `(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = `"handle:"+family` (id **or** `handle:`-prefixed — per `<param>` remarks); `includeArchived:false` | `IReadOnlyList<ProductResponse>` → `.Product` → `Id,Name,Handle,Description,PriceInCents,Interval,IntervalUnit` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | none | map/operations/ProductFamilies.md; Models/ProductResponse.cs; Models/Product.cs |
| `client.Products.ReadProductByHandle` | `(string apiHandle, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `apiHandle` = plan handle | `ProductResponse.Product` (validate plan exists + family membership + echo price) | **Case B** `SdkException<RawError>` (404 = unknown handle) | none | map/operations/Products.md; Models/Product.cs |
| `client.Customers.ReadCustomerByReference` | `(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = `eshoponweb:{username}` | `CustomerResponse.Customer` → `Id` | **Case B** `SdkException<RawError>` (404 = not found → create) | none | map/operations/Customers.md; Models/CustomerResponse.cs; Models/Customer.cs |
| `client.Customers.CreateCustomer` | `(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer = CreateCustomer{ required FirstName, LastName, Email; Reference } }` | `CustomerResponse.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none | map/operations/Customers.md; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs; Models/CustomerErrorResponse1.cs |
| `client.Customers.ListCustomerSubscriptions` | `(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md; Models/SubscriptionResponse.cs; Models/Subscription.cs |
| `client.Subscriptions.CreateSubscription` | `(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ required Subscription = CreateSubscription{ ProductHandle, CustomerId } }` | `SubscriptionResponse.Subscription` → `Id,State,ProductPriceInCents,CurrentPeriodEndsAt,NextAssessmentAt,Product` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs; Models/ErrorListResponse1.cs |

**Response envelopes are one level down** — `SubscriptionResponse.Subscription` (nullable), `CustomerResponse.Customer` (required), `ProductResponse.Product` (required). Reads go through the wrapper.

**Enums (read-only, `.Value` gives the wire string; `Models/Enums/`):**
- `SubscriptionState` — `active, trialing, assessing, pending, soft_failure, past_due, suspended, canceled, expired, paused, unpaid, trial_ended, on_hold, awaiting_signup, failed_to_create`. **Live** (dedup + "has subscription") = anything except `canceled, expired, failed_to_create, trial_ended, unpaid`.
- `IntervalUnit` — `day, month`.

**Client construction / auth / server (source: MaxioAdvancedBillingClientOptions.cs, ServerOptions.cs, Servers/ProductionOptions.cs, Core/Authentication/Basic/BasicAuthCredentials.cs, ServiceCollectionExtensions.cs):**
- Register via `services.AddMaxioAdvancedBillingClient(options => { ... })` (own `AddHttpClient` + singleton client). Namespace `MaxioAdvancedBilling`.
- `options.BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }` (Basic: username = Chargify API key, password = literal `x`; Basic works only on US/EU). `BasicAuthCredentials` in `MaxioAdvancedBilling.Core.Authentication.Basic`; both members `required`.
- `options.Environment = ServerEnvironment.Us` (`MaxioAdvancedBilling.Servers`; US default, sandbox is US).
- Base URL: `Us.BaseUrl` is a **template** `"https://{site}.chargify.com"` where `{site}` ← `Us.Site`. So: if `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <BaseUrl>` **verbatim** (no `{site}` placeholder → used as-is); else → `options.Server.Production.Us.Site = <Subdomain>`.

## 3. Trap notes

- SDK client + its `HttpClient`/handler pipeline must be **long-lived, reused via `IHttpClientFactory`** — not rebuilt per request; consequence if wrong: socket exhaustion / DNS staleness. `AddMaxioAdvancedBillingClient` already does this, but confirm lifetime before adding a second registration. **MUST load dotnet-client-initialization**.
- Credentials must be set **before/at** client construction and sourced from config, not literals; a never-set credential is silently skipped and surfaces only as a downstream 401. **MUST load dotnet-authentication**.
- List/read ops have many optional params with **no C# default** → a positional call mis-binds; and `product_handle` vs `customer_id`/`customer_reference` selection on `CreateSubscription` changes call acceptance. **MUST load dotnet-calling-endpoints**.
- Enums are `StringEnum<T>` (not C# enums) and response models carry an `AdditionalProperties` bag; reading `State`/`IntervalUnit` and building request records has non-obvious rules. **MUST load dotnet-models**.
- The error boundary must handle **two** `JsonException` directions plus Case A/B split; `TryGetRawError` is not a catch-all on typed errors, and there are no no-throw variants. **MUST load dotnet-error-handling**.
- `Timeout` is **per-attempt**, `HttpMethodsToRetry` defaults exclude POST (so `CreateCustomer`/`CreateSubscription` are never auto-resent — good, they are non-idempotent), and `LogRequestBody`/`MAXIOADVANCEDBILLINGCLIENT_LOG` can log bodies unredacted. **MUST load dotnet-configuration-resilience**.
- The test seam is the `HttpClient` constructor argument. **MUST load dotnet-testing**.

## 4. REQUIRED READING (load all before implementation; contents deliberately not copied here)

Two mandatory `JsonException` hazards for the error boundary:
- A drifted/malformed **2xx** body (missing a `required` member) surfaces as `System.Text.Json.JsonException` from **deserialization**, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is lost with it.

| Skill | Governs |
|---|---|
| `maxio-platforms-team:dotnet-client-initialization` | SDK client construction + DI lifetime |
| `maxio-platforms-team:dotnet-authentication` | Basic-auth credential wiring |
| `maxio-platforms-team:dotnet-calling-endpoints` | every `client.*` call (named args, body selection) |
| `maxio-platforms-team:dotnet-models` | building request records, reading enums/envelopes |
| `maxio-platforms-team:dotnet-error-handling` | the try/catch boundary in `MaxioBillingService` |
| `maxio-platforms-team:dotnet-configuration-resilience` | retry/timeout/logging posture at registration |
| `maxio-platforms-team:dotnet-testing` | faking the `HttpClient` seam in unit tests |

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `AddMaxioBilling` validates `Maxio:ApiKey` and `Maxio:Subdomain` (and, when `Maxio:BaseUrl` absent, Subdomain) are non-null/non-blank and `Maxio:ProductFamilyHandle` present; throws at registration so the host refuses to start. Basic auth has two parts but Password is the literal `"x"`, so only the ApiKey part is user-supplied and checked. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (`Maxio:*`), never from repo files. `AddMaxioAdvancedBillingClient` builds options **once at registration** and captures them in the singleton → a rotated key needs a process restart. Restart-to-rotate is acceptable here; documented. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. POST ops are not retried (verb list), so their budget ≈ one attempt. I pass a per-call `CancellationToken` from the request (`ct`) through every SDK call; the ASP.NET request-abort token bounds the whole call. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → `CreateCustomer`/`CreateSubscription` (POST) are **never** resent by the SDK, which is correct since neither is idempotent. Reads (GET) may be resent safely. Kept default. |
| 5 | Idempotency & ambiguous writes | Neither create op takes a caller-supplied idempotency key (the injected `Idempotency-Key: Guid.NewGuid()` header is not one). Reconciliation instead: **customer** — stable `reference = eshoponweb:{username}`, read-before-create, and on a 422 create-conflict re-read by reference; **subscription** — read `ListCustomerSubscriptions` and return an existing *live* subscription to the same product handle instead of creating a duplicate. Double-click safe. |
| 6 | Observability | `MaxioBillingService` logs at Information (customer ensured, subscription created/reused) and Warning/Error on SDK failures, including the Maxio error body string (`RawError.ReadAsString()` / typed error payload) as the correlation surface. `LogRequestBody` left **off**. |
| 7 | Sensitive data | Request models in scope (`CreateCustomer`, `CreateSubscription`) carry **no** card/bank data — payment method not required for these plans, no `credit_card_attributes` sent. Only email/username. So no PCI data in logs; `LogRequestBody` stays off and `LoggerFactory` is left to DI (the env-var log switch is not armed to trace in production config). |
| 8 | Environment selection | US only (`ServerEnvironment.Us`), Basic auth, `Production` server group. The sandbox site is selected via `Maxio:Subdomain` (or `Maxio:BaseUrl` override) — values come from configuration, never hard-coded. SDK has no separate "sandbox" environment — test traffic is kept off live systems by pointing `Subdomain`/`BaseUrl` at the sandbox site only; there is no prod Maxio site configured. `Ebb`/`Oauth` groups are untouched (no metered ingestion, no gateway). |

## 6. Assumptions & Blockers

- **Assumption:** eShop username (JWT `ClaimTypes.Name`) is a valid email (seeded users `demouser@microsoft.com` etc.) → used as customer `email`. FirstName/LastName are not in eShop identity → derived (`FirstName` = username, `LastName` = `"eShopOnWeb"`), since Maxio requires them. Minor; proceed.
- **Assumption:** `planHandle` is a **required** field of the subscribe request (shopper picks from `GET /api/subscription-plans`) rather than hard-coding `eshop-pro` as a default — honoring "hard-code none of their values". `YOUR CALL — not in the map`. Proceed.
- **Assumption:** in-memory DB (`UseOnlyInMemoryDatabase=true`) means userId↔subscription mapping is not persisted; Maxio (via the stable `reference`) is the source of truth, so `my-subscriptions` re-reads from Maxio each call. Proceed.
- No Blockers: every capability the hero flow needs exists on the map.
