# Maxio Advanced Billing — integration plan (eShopOnWeb)

Add an **additive, parallel** recurring-subscription capability to eShopOnWeb, with Maxio
Advanced Billing as the system of record. Three JWT-authenticated endpoints on `src/PublicApi`:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.
Every Maxio contract fact below comes from the SDK map / map-named source; nothing from memory.

## 1. Scope & sequence

Layering follows eShopOnWeb Clean Architecture. The Maxio SDK is a build dependency of
**Infrastructure** only; **ApplicationCore** stays SDK-free (plain domain types); **PublicApi**
depends on the ApplicationCore abstraction.

1. **Vendor the SDK** (unpublished — see `dotnet-getting-started` *Install from source*) into the
   repo as a buildable project and `ProjectReference` it from Infrastructure. It is not on NuGet,
   so there is no `dotnet add package`.
2. **ApplicationCore** — `ISubscriptionBillingService` + domain records (`SubscriptionPlan`,
   `CustomerSubscription`) + `MaxioBillingException`. No `MaxioAdvancedBilling.*` types cross this
   boundary.
3. **Infrastructure/Maxio** — `MaxioSettings` (bound from `Maxio:` section), a
   `MaxioSubscriptionBillingService` wrapping `MaxioAdvancedBillingClient`, an `AddMaxioBilling`
   DI extension (credential fail-fast + client registration + error translation).
4. **PublicApi** — three endpoints (MinimalApi.Endpoint `IEndpoint` convention, as
   `CatalogBrandListEndpoint`/`CreateCatalogItemEndpoint`), request/response DTOs, identity from the
   JWT `ClaimTypes.Name` claim (issued by `IdentityTokenClaimService`).
5. **Program.cs** — call `AddMaxioBilling(configuration)`. Secrets loaded via `dotnet user-secrets`
   (PublicApi `UserSecretsId` already present).
6. **Tests** — Infrastructure unit tests faking the SDK's `HttpClient` seam; PublicApi integration
   tests for the three routes.

Operations used, in order:
- Plans → `client.Products.ListProductsForProductFamily(productFamilyHandle, …)`.
- Subscribe (idempotent) → `client.Customers.ReadCustomerByReference(reference)` (404 ⇒ absent) →
  `client.Customers.CreateCustomer(body)` when absent → `client.Customers.ListCustomerSubscriptions(customerId)`
  (dedupe non-terminal same-product) → `client.Subscriptions.CreateSubscription(body)`.
- My subscriptions → `ReadCustomerByReference` (404 ⇒ empty) → `ListCustomerSubscriptions(customerId)`.

No capability gap: every step maps to an SDK operation. No invented data paths.

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal C#
> identifier; the cancellation-token parameter is literally `ct`, so named args write `ct:`. Optional
> query/filter params have **no C# default** — pass them explicitly (`null` to skip); only `page`/`perPage`
> carry defaults.
> ⚠ Every SDK type is written **fully-qualified per the namespace its source path implies** (`Models/` ⇒
> `MaxioAdvancedBilling.Models`, `Models/Enums/` ⇒ `MaxioAdvancedBilling.Models.Enums`, `Errors/` ⇒
> `MaxioAdvancedBilling.Errors`, client/options/servers ⇒ `MaxioAdvancedBilling` / `MaxioAdvancedBilling.Servers`,
> `RetryOptions` ⇒ `MaxioAdvancedBilling.Core.Configuration`, `SdkException<>` ⇒ `MaxioAdvancedBilling.Core.Exceptions`,
> `RawError` ⇒ `MaxioAdvancedBilling.Core.ErrorResponse`). Take each type's namespace from its own path, never a neighbour's.

| Op | Controller · signature | Request model + fields used | Response envelope → inner fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| List plans | `client.Products.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none (path+query). Pass family **handle** as `productFamilyId`; `includeArchived: false`; all filter params `null` | `IReadOnlyList<`​`MaxioAdvancedBilling.Models.ProductResponse>`; each `.Product` (`MaxioAdvancedBilling.Models.Product`) → `Handle`, `Name`, `Description`, `PriceInCents (long?)`, `Interval (int?)`, `IntervalUnit (IntervalUnit?)`, `Id`, `ArchivedAt` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/ProductFamilies.md; Models/ProductResponse.cs; Models/Product.cs |
| Read customer by ref | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none (query `reference`) | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`MaxioAdvancedBilling.Models.Customer`) → `Id (int?)`, `Reference`, `Email` | **Case B** `SdkException<RawError>` — `StatusCode` **404 ⇒ customer absent** | none | map/operations/Customers.md; Models/CustomerResponse.cs; Models/Customer.cs |
| Create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — **body must pass explicitly** | `MaxioAdvancedBilling.Models.CreateCustomerRequest{ required Customer = MaxioAdvancedBilling.Models.CreateCustomer }`; `CreateCustomer` **required** `FirstName`,`LastName`,`Email`; optional `Reference` (wire `reference`) ← **idempotency reference** | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Customers.md; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs; Models/CustomerErrorResponse1.cs |
| List customer subs | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each `.Subscription` (nullable) | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md; Models/SubscriptionResponse.cs |
| Create subscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — **body must pass explicitly** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest{ required Subscription = MaxioAdvancedBilling.Models.CreateSubscription }`; `CreateSubscription` fields set: `ProductHandle` (wire `product_handle`), `CustomerId` (wire `customer_id`) **or** `CustomerReference`; leave payment fields unset (payment method not required). All fields optional (nothing `required`) | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs; Models/ErrorListResponse1.cs |

**Inner `MaxioAdvancedBilling.Models.Subscription` fields read** (Models/Subscription.cs): `Id (int?)`,
`State (MaxioAdvancedBilling.Models.Enums.SubscriptionState?)`,
`CurrentPeriodEndsAt (DateTimeOffset?)` ← **next-billing-date** ("when the next regularly scheduled
attempted charge will occur"), `NextAssessmentAt (DateTimeOffset?)` (retry-adjusted, secondary),
`ProductPriceInCents (long?)`, `Product (MaxioAdvancedBilling.Models.Product?)` → `Handle`/`Name`,
`Customer (MaxioAdvancedBilling.Models.Customer?)` → `Id`/`Reference`.

**Enums / value reads** (all `StringEnum<T>`; read the wire string via `.Value`, PascalCase static members to construct):
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (Models/Enums/SubscriptionState.cs): members incl.
  `Active`("active"), `Trialing`, `Pending`, `Assessing`, `PastDue`, `SoftFailure`, `Paused`, `OnHold`,
  `AwaitingSignup`, `Suspended`, `Unpaid` (non-terminal) vs `Canceled`, `Expired`, `FailedToCreate`,
  `TrialEnded` (terminal). Read for display via `state?.Value`.
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (Models/Enums/IntervalUnit.cs) — display via `.Value`
  (e.g. `"month"`). `StringEnum` base (`Core/Enum/StringEnum.cs` → `Core/Enum/TypedEnum.cs`) exposes
  `.Value` and `ToString()==Value`.

**Client construction / auth / server (Servers & auth; MaxioAdvancedBillingClientOptions.cs; ServiceCollectionExtensions.cs):**
- Register via `services.AddMaxioAdvancedBillingClient(options => { … })` — builds options **once at
  registration**, resolves an `IHttpClientFactory`-created `HttpClient`, registers the client as a
  **singleton**. Root namespace `MaxioAdvancedBilling`.
- Auth = **HTTP Basic**: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
  { Username = <MAXIO_API_KEY value>, Password = "x" }` (Basic works on US/EU only; the API key is the
  username, password literally `x`). `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us`.
- Base URL: default template `https://{site}.chargify.com`. Set `options.Server.Production.Us.Site =
  <Maxio:Subdomain>`. If `Maxio:BaseUrl` set, set `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`
  **verbatim** (ProductionOptions.cs `UsOptions{ BaseUrl, Site }`; verbatim URL with no `{site}` leaves the
  site param unused — fine).

## 3. Trap notes (hazard + consequence + skill; not resolved here)

- **Client lifetime / HttpClient pipeline** — the `AddMaxioAdvancedBillingClient` extension owns the
  `HttpClient` via `IHttpClientFactory` and registers a singleton client; whether *I* must also manage
  handler lifetime, and how the singleton interacts with a rotated secret, is not visible in the
  signature. **MUST load `maxio-platforms-team:dotnet-client-initialization`.**
- **Auth application is silent on failure** — a credential never set is skipped and the request is sent
  anyway, so a misbinding surfaces as a confusing 401 rather than a throw; the multi-part Basic credential
  needs both parts. **MUST load `maxio-platforms-team:dotnet-authentication`.**
- **Positional mis-binding on list ops** — `ListProductsForProductFamily`/`ListCustomerSubscriptions` have
  many optional params with no C# default; a positional call mis-binds. **MUST load
  `maxio-platforms-team:dotnet-calling-endpoints`.**
- **Models: enums are `StringEnum<T>` not C# enums; `required` init-only members; unknown-field bag** — how
  to construct request records and read enum/union values without tripping the converter. **MUST load
  `maxio-platforms-team:dotnet-models`.**
- **Error boundary — two disjoint cases + JsonException from two directions** — Case A typed vs Case B raw;
  and `System.Text.Json.JsonException` reaches the boundary both from a drifted 2xx body and from error-object
  construction on a non-2xx body. **MUST load `maxio-platforms-team:dotnet-error-handling`.**
- **Timeout is per-attempt; POST not retried; JSON body logged unredacted** — the caller-visible time budget,
  which writes the SDK may resend, and what `LogRequestBody` leaks. **MUST load
  `maxio-platforms-team:dotnet-configuration-resilience`.**
- **Test seam** — the `HttpClient` constructor arg is the fake seam; match the repo's xUnit/NSubstitute style.
  **MUST load `maxio-platforms-team:dotnet-testing`.**

## 4. REQUIRED READING (load all before implementing; contents deliberately not inlined here)

| Skill (plugin-qualified) | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | client + DI registration (step 3/5) |
| `maxio-platforms-team:dotnet-authentication` | Basic credential wiring (step 3) |
| `maxio-platforms-team:dotnet-calling-endpoints` | every `client.*` call (steps 2–4) |
| `maxio-platforms-team:dotnet-models` | building request records / reading enums (steps 2–4) |
| `maxio-platforms-team:dotnet-error-handling` | error-translation boundary (step 3) — **always required** |
| `maxio-platforms-team:dotnet-configuration-resilience` | timeout budget, retries, logging posture (steps 3/5) |
| `maxio-platforms-team:dotnet-testing` | Infrastructure + endpoint tests (step 6) |

**Two mandatory `JsonException` hazard rows (verbatim):**
1. A drifted or malformed **2xx** body (a missing `required` member) surfaces as a
   `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — an
   SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
   `System.Text.Json.JsonException` **while the error object is being constructed**, so it **replaces**
   the `SdkException` and the HTTP status is destroyed with it.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AddMaxioBilling(configuration)` reads `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` and **throws `InvalidOperationException` at registration** if any is null/whitespace (each checked separately — a blank part ≠ a missing one). `Maxio:BaseUrl` is the only optional key. Host refuses to start rather than 401 on first call. (PublicApi integration-test host is given dummy non-secret placeholder `Maxio:*` values in its `appsettings.test.json` so it still boots — no real secret in repo.) |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` comes from **.NET user-secrets** (loaded from env var `MAXIO_API_KEY` by me via `dotnet user-secrets`), never a repo file. `AddMaxioAdvancedBillingClient` captures options in a **singleton at registration**, so a rotated key needs a process restart — acceptable for this reference app; documented, not silently assumed. |
| 3 | Total timeout budget | `RetryOptions.Timeout` is **per-attempt**, not total. I enforce a whole-call deadline with a `CancellationTokenSource` (≈30 s) in the service and pass its token as `ct:` to every SDK call, so a hung retried call cannot exceed the budget. `YOUR CALL — not in the map` (application decision; SDK fact = per-attempt timeout). |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = `GET, HEAD, PUT, OPTIONS`. The two writes in scope are **POST** (`CreateCustomer`, `CreateSubscription`) → the SDK **never resends** them, so no automatic duplicate-charge risk from retries. Reads (GET) may retry harmlessly. Left at default. `YOUR CALL — not in the map`. |
| 5 | Idempotency & ambiguous writes | Neither `CreateCustomer` nor `CreateSubscription` exposes a real caller-supplied idempotency key (their map rows show none; the generator-injected `Idempotency-Key` header is per-call `Guid.NewGuid()` and is **not** a key). **Reconciliation path**: (a) a per-user-reference in-process `SemaphoreSlim` gate serializes double-clicks within the run; (b) customer dedupe via `ReadCustomerByReference` before `CreateCustomer` (reference = eShop user's normalized username, stable across restarts); (c) subscription dedupe by scanning `ListCustomerSubscriptions` for a **non-terminal** subscription to the same product before `CreateSubscription`, returning the existing one. `YOUR CALL — not in the map`. |
| 6 | Observability | Structured `ILogger` logs at Information (customer ensured / subscription created, with customer id + subscription id + state) and Warning/Error on translated failures, including the Maxio error messages from `ErrorListResponse1.Errors` / `RawError.ReadAsString()`. `LogRequestBody` stays **off** (default), so no request body is emitted. No provider correlation-id header is consumed (none surfaced through the throw-based API return); errors log the raw provider message instead. |
| 7 | Sensitive data | In-scope request models (`CreateCustomer`: name/email; `CreateSubscription`: product/customer refs) carry **no PAN/bank/secret** — payment method is not required and no card fields are set. Still: `LogRequestBody` left off and `options.Logging.LoggerFactory` is **assigned explicitly** (the DI extension sets it from `ILoggerFactory`) so the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot switch body logging on from outside the code. My own diagnostics never echo a request body. |
| 8 | Environment selection | Only the **`Production`** server group is touched, on **`ServerEnvironment.Us`** → `https://{site}.chargify.com` with `{site}` = `Maxio:Subdomain` (the configured sandbox site), or `Maxio:BaseUrl` verbatim when set. `Ebb`/`Oauth` groups unused. Sandbox isolation is by **site subdomain** — the SDK has no separate "sandbox" environment enum; test traffic stays on the sandbox site because the subdomain/base-URL comes only from config, never hard-coded. |

## 6. Assumptions & Blockers

- **Assumption**: "the eShopOnWeb user" identity = the JWT `ClaimTypes.Name` claim (the username/email
  issued by `IdentityTokenClaimService`). The Maxio customer **reference** = that username normalized
  (`Trim().ToLowerInvariant()`), chosen over the in-memory `ApplicationUser.Id` GUID because the GUID is
  regenerated every run (in-memory DB) whereas the seeded username is stable across restarts → cross-run
  idempotent customer lookup.
- **Assumption**: default subscribe target = `Maxio:ProductFamilyHandle`'s Pro plan, but the endpoint
  accepts an explicit `planHandle`; when omitted it falls back to the first non-archived plan in the family
  (no hard-coded handle — the task requires the build to run against a different catalog).
- **Assumption**: dummy placeholder `Maxio:*` values in the test host's `appsettings.test.json` are
  acceptable (not real credentials) to keep existing integration tests booting under fail-fast.
- **Blockers**: none. Every required capability maps to an SDK operation.
