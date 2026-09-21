# Maxio Advanced Billing — integration plan (eShopOnWeb subscription billing)

Additive, parallel subscription-billing capability on `src/PublicApi`. Maxio Advanced Billing is the
system of record. No change to the existing Catalog/Basket/Order flow.

## 1. Scope & sequence

HTTP surface (JWT-authenticated, caller identity from the token) on `src/PublicApi`, following its
`MinimalApi.Endpoint` `IEndpoint<…>` convention (exemplar: `CatalogItemEndpoints/CatalogItemListPagedEndpoint.cs`):

| Endpoint | Maxio operations used |
| --- | --- |
| `GET /api/subscription-plans` | `ListProductFamilies` → resolve configured family handle to id → `ListProductsForProductFamily` |
| `POST /api/subscriptions` (hero: **Subscribe**) | ensure customer: `ReadCustomerByReference` → (miss) `CreateCustomer`; dedup: `ListCustomerSubscriptions`; enroll: `CreateSubscription` |
| `GET /api/my-subscriptions` | resolve customer: `ReadCustomerByReference`; `ListCustomerSubscriptions` |

Layering (mirrors repo convention — interfaces in ApplicationCore, impl in Infrastructure):
- `ApplicationCore/Interfaces/ISubscriptionBillingService.cs` + `ApplicationCore/Subscriptions/*` domain DTOs (no SDK reference).
- `Infrastructure/Maxio/MaxioSubscriptionBillingService.cs` (SDK caller), `MaxioSettings.cs`, `MaxioServiceCollectionExtensions.cs`.
- SDK is source-only (not on NuGet) → **vendored** into `lib/maxio-advanced-billing-sdk/` and referenced by `Infrastructure.csproj` as a `ProjectReference`. A folder-local `Directory.Packages.props` disables central package management for the vendored project so its pinned `PackageReference` versions build under this repo's CPM root. (Build reference only; distinct from the read-only map clone.)
- `PublicApi/SubscriptionEndpoints/*` three endpoints; DI wired in `PublicApi/Program.cs` via `AddMaxioSubscriptionBilling(Configuration)`.

Identity mapping: JWT `ClaimTypes.Name` (username) → `UserManager<ApplicationUser>.FindByNameAsync` → the
Identity user's stable `Id` is used as the **Maxio customer `reference`** (durable idempotency key that
survives the in-memory DB reset, because it lives in Maxio, not locally).

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim. Every parameter name is the literal C# identifier; the
cancellation-token parameter is named `ct`, so named arguments write `ct:`. Pass named arguments for the
multi-optional list operations.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`Models/` →
`MaxioAdvancedBilling.Models`; `Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`;
`Errors/` → `MaxioAdvancedBilling.Errors`; client/options → `MaxioAdvancedBilling`;
`ServerEnvironment` → `MaxioAdvancedBilling.Servers`; `BasicAuthCredentials` →
`MaxioAdvancedBilling.Core.Authentication.Basic`; `SdkException<>` → `MaxioAdvancedBilling.Core.Exceptions`;
`RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`; `RetryOptions` →
`MaxioAdvancedBilling.Core.Configuration`).

| Op (controller.method) | Signature (verbatim) | Request model + fields used | Response envelope → inner fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `Client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (all null) | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` → `Handle`, `Id` | **Case B** `SdkException<RawError>` → `StatusCode` | none | map/operations/ProductFamilies.md; Models/ProductFamilyResponse.cs, Models/ProductFamily.cs |
| `Client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = resolved family **id** (string); `includeArchived: false`; rest null | `IReadOnlyList<ProductResponse>` → `.Product` → `Handle`, `Name`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductFamily.Handle` | **Case A** `SdkException<ListProductsForProductFamilyError>` → `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | page/perPage (single call, perPage 200; not walked as Pageable — plain list) | map/operations/ProductFamilies.md; Models/ProductResponse.cs, Models/Product.cs |
| `Client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop user Id | `CustomerResponse` → `.Customer` (required) → `Id` | **Case B** `SdkException<RawError>` → `StatusCode` (`NotFound` ⇒ customer absent) | none | map/operations/Customers.md; Models/CustomerResponse.cs, Models/Customer.cs |
| `Client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest { Customer = CreateCustomer{...} }` — see CreateCustomer field notes below | `CustomerResponse` → `.Customer` → `Id` | **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Customers.md; Models/CreateCustomerRequest.cs, Models/CreateCustomer.cs, Models/CustomerErrorResponse1.cs |
| `Client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription = CreateSubscription{...} }` — see CreateSubscription field notes below | `SubscriptionResponse` → `.Subscription` (nullable) → `Id`, `State`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `NextAssessmentAt`, `CurrentPeriodEndsAt`, `Product.Handle`, `Product.Name`, `Reference` | **Case A** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs, Models/CreateSubscription.cs, Models/SubscriptionResponse.cs, Models/Subscription.cs, Models/ErrorListResponse1.cs |
| `Client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` = resolved Maxio customer id | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` → fields as above | **Case B** `SdkException<RawError>` → `StatusCode` | none | map/operations/Customers.md; Models/SubscriptionResponse.cs, Models/Subscription.cs |

### CreateCustomer body — field notes (Models/CreateCustomer.cs)

| Field (wire): type | required? | purpose |
| --- | --- | --- |
| `FirstName (first_name): string` | **required** | given name; derived from eShop username local-part (Identity user has no name fields). |
| `LastName (last_name): string` | **required** | family name; set to `"eShopOnWeb"` placeholder. |
| `Email (email): string` | **required** | eShop user email (== username). |
| `Reference (reference): string?` | optional | **set** = eShop user Id → the idempotency key; Maxio enforces per-site reference uniqueness (a duplicate create returns 422, handled by re-read). Omit → provider default (null, no dedup) — so we set it deliberately. |

All other `CreateCustomer` optionals **omitted → provider default** (no card/bank/address data captured — payment method not required for these plans).

### CreateSubscription body — field notes (Models/CreateSubscription.cs)

| Field (wire): type | required? | purpose |
| --- | --- | --- |
| `ProductHandle (product_handle): string?` | optional (required unless product_id) | **set** = requested plan handle. Handle chosen over `ProductId` per the model's own remark ("product ID is not currently published; use the API Handle"). |
| `CustomerId (customer_id): int?` | optional (one of customer_id / customer_reference / customer_attributes required) | **set** = resolved Maxio customer id (the ensure-customer step guarantees it exists). |
| `Reference (reference): string?` | optional | **set** = deterministic `eshop-{userId}-{productHandle}` for traceability/reconciliation. Omit → provider default (null). Not relied on as a unique key (see §5 row 5). |
| `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | optional | **omit → provider default.** Plans are configured "payment method not required"; leaving this unset lets the product/site config drive collection so subscribe succeeds with no card. Setting it would override that per-caller. |

All other `CreateSubscription` optionals **omitted → provider default** (no `payment_profile_attributes`/`credit_card_attributes` → no card capture / no 3-DS; no `next_billing_at`/`initial_billing_at` → activate immediately per product config; `defer_signup` left at its `false` model default).

### Enums (read-back only; Models/Enums/)

| Enum | Members used | Read as |
| --- | --- | --- |
| `SubscriptionState` (Models/Enums/SubscriptionState.cs) | wire values incl. `active`, `trialing`, `pending`, `assessing`, `past_due`, `soft_failure`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `suspended`, `failed_to_create`, `awaiting_signup` | `state.Value` (raw wire string). Non-terminal set for dedup = {active, trialing, pending, assessing, past_due, soft_failure, paused, unpaid, on_hold, suspended} |
| `IntervalUnit` (Models/Enums/IntervalUnit.cs) | `day`, `month` | `intervalUnit.Value` |

`StringEnum<T>` exposes `public TValue Value` — use `.Value` (never `ToString()`/interpolation, which give the record debug form).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| `product_handle` sent to `CreateSubscription` must be a handle returned by `ListProductsForProductFamily` for the configured family | `CreateSubscription` ← `ListProductsForProductFamily` | implementation — POST /api/subscriptions validates the requested handle against the family's live product handles before enrolling; unknown handle ⇒ 400. |
| `customer_id` sent to `CreateSubscription` must be an id returned by `ReadCustomerByReference`/`CreateCustomer` | `CreateSubscription` ← `ReadCustomerByReference`/`CreateCustomer` | implementation — ensure-customer step returns the id used. |

### Client construction / auth / server node

- Construct via DI `services.AddMaxioAdvancedBillingClient(options => …)` (source: ServiceCollectionExtensions.cs) — builds options once at registration, fills `Logging.LoggerFactory` from the container, registers the client singleton over an `IHttpClientFactory` client.
- Auth: `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (Servers & auth: username = Chargify API key, password = `x`; Basic works on US/EU).
- Environment: `options.Environment = ServerEnvironment.Us` (sandbox `*.chargify.com`; gateway rejects Basic).
- Server node: if `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` verbatim; else `options.Server.Production.Us.Site = <Maxio:Subdomain>` (fills `{site}` in `https://{site}.chargify.com`).
- `options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }` (per-attempt).

## 3. Trap notes

- Building request bodies uses nested records (`CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`) and reads `StringEnum.Value`, not `ToString()` — hazard: interpolating/`ToString()`-ing a `StringEnum` emits the record debug form, silently wrong on the wire and in logs. **MUST load maxio-platforms-team:dotnet-models.**
- Two Case-A errors (`CreateCustomer`, `CreateSubscription`) need one branch per declared `TryGet…` with `TryGetRawError` **last**; `System.Text.Json.JsonException` can reach the boundary from a drifted 2xx body (escapes an SDK-only catch) and can *replace* the `SdkException` on a non-2xx whose body mismatches the generated `{Operation}Error` (destroys the status). Case-B ops surface 404 as `SdkException<RawError>` not a null. **MUST load maxio-platforms-team:dotnet-error-handling.**
- `Timeout` on `RetryOptions` is per-attempt, not a call budget; only a `CancellationToken` deadline bounds a whole call; default `HttpMethodsToRetry` never resends `POST` (both writes here are POST → no SDK duplicate) but the per-attempt timeout multiplies on retryable verbs. **MUST load maxio-platforms-team:dotnet-configuration-resilience.**
- Client/HttpClient must be long-lived (singleton over `IHttpClientFactory`), not per-request. **MUST load maxio-platforms-team:dotnet-client-initialization.**
- A missing/blank credential must fail the host at startup, and adding that check obliges supplying the test host config in the same change. **MUST load maxio-platforms-team:dotnet-authentication.**
- List/multi-optional calls (`ListProductsForProductFamily`, `ListProductFamilies`) use named arguments; `requestOptions` must be counted or `ct:` passed by name. **MUST load maxio-platforms-team:dotnet-calling-endpoints.**
- The seam for tests is the `HttpClient` constructor arg; match the repo's MSTest stack. **MUST load maxio-platforms-team:dotnet-testing.**

## 4. REQUIRED READING (load all before implementing)

| Skill | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | Maxio client + DI singleton construction |
| `maxio-platforms-team:dotnet-authentication` | Basic-auth credentials + startup fail-fast + test host |
| `maxio-platforms-team:dotnet-calling-endpoints` | invoking operations, named args, `ct:` |
| `maxio-platforms-team:dotnet-models` | nested request records, `StringEnum.Value` |
| `maxio-platforms-team:dotnet-error-handling` | Case A/B catch ladders, JsonException from both directions |
| `maxio-platforms-team:dotnet-configuration-resilience` | retries/timeout budget, write-retry ownership, logging redaction |
| `maxio-platforms-team:dotnet-testing` | `HttpClient` stub seam, MSTest style |

Mandatory hazard rows (from the skill): a drifted/malformed **2xx** body surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException`, so an SDK-exception-only catch ladder lets it escape; a **non-2xx** body that does not match its `{Operation}Error` shape throws `JsonException` while the error object is constructed, **replacing** the `SdkException` and destroying the HTTP status. Both handled at the integration boundary. The sheet deliberately does not carry these skills' contents.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` and validated at registration in `AddMaxioSubscriptionBilling` (runs during `builder.Services` config, before `app.Run`): host throws `InvalidOperationException` naming the missing key if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is null/blank. Each part checked separately (a blank part ≠ a missing one). `BaseUrl` is optional. Message names the key, never echoes a value. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (`Maxio:ApiKey` etc.), loaded by `WebApplication.CreateBuilder` in Development; never in repo files. DI builds the options once at registration and captures them in the singleton, so a rotated key takes effect only on process restart — acceptable for this reference app; documented as restart-required. |
| 3 | Total timeout budget | Per-attempt `RetryOptions.Timeout = 15s`. The caller's whole-call budget is enforced by a linked `CancellationToken` (`HttpContext.RequestAborted` + `CancelAfter(30s)`) applied in the service `Bounded(...)` wrapper around every SDK call — the only thing that bounds a full call. |
| 4 | Write-retry ownership | Both writes (`CreateCustomer`, `CreateSubscription`) are POST; default `HttpMethodsToRetry` (`GET,HEAD,PUT,OPTIONS`) never resends them, so no SDK-side duplicate write. `HttpMethodsToRetry` left at default; no `PUT` used. |
| 5 | Idempotency & ambiguous writes | **Customer:** key = Maxio customer `reference` = eShop user Id; Maxio enforces per-site reference uniqueness → atomic provider-side guard. Flow: `ReadCustomerByReference`; on miss `CreateCustomer`; on `422` (create/create race) re-read by reference and use the winner. **Subscription:** no provider-enforced unique key exists in the map (subscription `reference` uniqueness is **UNVERIFIED** — not asserted by the SDK/map), so dedup is read-before-write: `ListCustomerSubscriptions` filtered to a non-terminal subscription on the same product handle ⇒ return it as a no-op (never a second create). Concurrent double-clicks within a process are serialized by a per-user `SemaphoreSlim`; a deterministic subscription `reference` is set for reconciliation. Residual limitation: a cross-process/near-simultaneous duplicate is not atomically excluded because the environment forbids new infra and the in-memory DB cannot carry a durable claim (see §6) — the dominant double-click case is fully closed. |
| 6 | Observability | `ILogger` in the service logs at Information (customer resolved/created, subscription created/reused) and Warning/Error on failure, carrying the eShop user Id and Maxio ids, plus the provider correlation surfaced from `RawError.ReadAsString()`/typed error messages. SDK built-in logger inherits the host `ILoggerFactory` at the host's level; `LogRequestBody` stays **off** (default). |
| 7 | Sensitive data | Scope carries no card/bank/PII-beyond-email data: `CreateCustomer` sends name+email+reference only; `CreateSubscription` sends handle+customer id+reference only (no `*_attributes`). `LogRequestBody` stays off; `LoggerFactory` is left to the DI-provided factory (host-controlled), and no request body is echoed in our own logs. Email is treated as ordinary contact data (already in the Identity store). |
| 8 | Environment selection | Deployments talk to `ServerEnvironment.Us` → `https://{Maxio:Subdomain}.chargify.com` (sandbox `cp-exp-1` here), or the verbatim `Maxio:BaseUrl` when set (covers a mock, proxy, EU, or self-hosted gateway host). Only the `Production`/`Us` group is touched. Test traffic is kept off the live system by pointing `Maxio:Subdomain`/`Maxio:BaseUrl` at a sandbox site via user-secrets; the functional test host uses non-secret placeholder Maxio config and never calls Maxio. |

## 6. Assumptions & Blockers

- **Assumption:** the eShop Identity user `Id` is the durable per-user key for the Maxio customer `reference`; username (JWT `ClaimTypes.Name`) resolves to it via `UserManager`. Minor — proceeding.
- **Assumption:** `ServerEnvironment.Us` (Chargify) is correct for the sandbox; `MAXIO_ENVIRONMENT=US` confirms. EU/other reachable via `Maxio:BaseUrl` override without a code change.
- **Not a Blocker (documented decision, §5 row 5):** the machine has no durable store (in-memory DB, no new infra permitted) to hold a cross-process idempotency claim for subscriptions, and subscription-`reference` uniqueness is not provider-guaranteed in the map. The task explicitly accepts the in-memory/no-infra environment and directs completion. The customer write has a real atomic provider guard; the subscription write is protected by read-before-write + per-process serialization, closing the realistic double-click. A production deployment with a durable store or Maxio-enforced subscription-reference uniqueness would upgrade this to a cross-process atomic guarantee.
- **Blocker:** none.
