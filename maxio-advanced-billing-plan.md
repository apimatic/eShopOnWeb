# Maxio Advanced Billing integration plan — eShopOnWeb subscriptions

Additive, parallel subscription-billing capability with **Maxio Advanced Billing as the system
of record**. Three JWT-authenticated endpoints on `src/PublicApi`, following that project's
`IEndpoint` minimal-API convention:

- `GET  /api/subscription-plans`  — list subscribable plans (products in the configured family)
- `POST /api/subscriptions`       — subscribe the caller (idempotent ensure-customer + subscribe)
- `GET  /api/my-subscriptions`    — list the caller's subscriptions

Caller identity comes from the JWT `ClaimTypes.Name` claim (the eShop username/email — see
`AuthenticateEndpoint`/`IdentityTokenClaimService`), used verbatim as the Maxio customer `reference`.

## 1. Scope & sequence

| # | Step | Maxio operations |
|---|------|------------------|
| 1 | Vendor SDK source into `src/MaxioAdvancedBilling/` (CPM off), ProjectReference from `Infrastructure` | — |
| 2 | Bind `Maxio:` settings, fail-fast validation, register client (Basic auth, US env, `{site}`=Subdomain, optional `BaseUrl` override) | client construction |
| 3 | `ApplicationCore`: `ISubscriptionBillingService` + plain DTOs (no SDK leak into PublicApi) | — |
| 4 | `Infrastructure/Maxio`: implement the service | `ProductFamilies.ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, `Customers.ListCustomerSubscriptions` |
| 5 | `PublicApi/SubscriptionEndpoints`: the three endpoints | — |
| 6 | Tests + test-host config; build; live end-to-end verify; reconcile §5 | — |

**Plan listing** = products in the configured family. **Subscribe** = ensure-customer (idempotent by
`reference`) → subscribe by `product_handle` + `customer_id`, idempotent by deterministic subscription
`reference`. **My-subscriptions** = list by customer id (after resolving the customer by `reference`).

## 2. CONTRACT SHEET

> ⚠ Signatures below are generated code, verbatim. Every parameter name is the literal C# identifier;
> the cancellation-token parameter is named `ct`, so named arguments write `ct:`. Optional query params
> that have no C# default (e.g. all the filter params) **must be passed explicitly** — pass `null` to skip.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (records →
> `MaxioAdvancedBilling.Models`, enums → `MaxioAdvancedBilling.Models.Enums`, errors →
> `MaxioAdvancedBilling.Errors`, client/options → `MaxioAdvancedBilling`, `ServerEnvironment` →
> `MaxioAdvancedBilling.Servers`, `RawError`/`SdkException` → `MaxioAdvancedBilling.Core.*`).

| Op (controller.method) | Signature (verbatim) | Request model → fields used | Response envelope → fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `Products` **not used** — plans come from the family, below | | | | | | |
| `ProductFamilies.ListProductsForProductFamily` | `(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = **`"handle:" + Maxio:ProductFamilyHandle`** (verified: raw handle 404s, `handle:` prefix 200s); pass `includeArchived: false`, rest `null` | `IReadOnlyList<ProductResponse>`; each `.Product` → `Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit, RequireCreditCard` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | page-based; `page`/`per_page`; loop until short page | `map/operations/ProductFamilies.md`; `Models/Product.cs` |
| `Customers.ReadCustomerByReference` | `(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` = eShop username | `CustomerResponse.Customer` → `Id, FirstName, LastName, Email, Reference` | **Case B** `SdkException<RawError>` (404 when absent → treat as "not found") | none | `map/operations/Customers.md`; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `Customers.CreateCustomer` | `(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest.Customer` = `CreateCustomer{ FirstName(req), LastName(req), Email(req), Reference }`. **`Reference` (reference)**: eShop username — *purpose:* the unique idempotency key; SDK remarks: "you may only create one customer for a given reference value … must be unique". All address/tax fields omitted (omit → provider default). | `CustomerResponse.Customer` → `Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md`; `Models/CreateCustomer.cs`, `Models/CustomerErrorResponse1.cs` |
| `Subscriptions.FindSubscription` | `(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` = deterministic subscription reference | `SubscriptionResponse.Subscription` (nullable) → `Id, State, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt, Product, CreatedAt` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| `Subscriptions.CreateSubscription` | `(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription` = `CreateSubscription{ ProductHandle, CustomerId, Reference, PaymentCollectionMethod }`. **`ProductHandle` (product_handle)**: the plan to subscribe — *purpose:* selects product (alt to product_id). **`CustomerId` (customer_id)**: id from ensure-customer — *purpose:* identify existing customer (alt to customer_reference/attributes). **`Reference` (reference)**: deterministic per (user,plan) — *purpose:* app idempotency key for the subscription. **`PaymentCollectionMethod` (payment_collection_method)** = `CollectionMethod.Remittance` — *purpose:* invoice/remittance billing so signup issues an invoice instead of attempting an immediate card charge. **LIVE-CONFIRMED necessary:** omitting it applies the provider default `automatic`, which despite `require_credit_card=false` returns 422 *"No payment method was on file for the $299.00 balance"*; `remittance` (or legacy `invoice`) creates the subscription `active` with an open invoice. | `SubscriptionResponse.Subscription` → `Id, State, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt, Product` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; `Models/CreateSubscription.cs`, `Models/Enums/CollectionMethod.cs`, `Models/ErrorListResponse1.cs` |
| `Customers.ListCustomerSubscriptions` | `(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` → as above + `Product.Name/Handle` | **Case B** `SdkException<RawError>` | none stated → single response | `map/operations/Customers.md`; `Models/SubscriptionResponse.cs` |

Enums read (from `Models/Enums/`): `SubscriptionState` (`Active`, `Trialing`, `Pending`, `PastDue`,
`Canceled`, `Expired`, `FailedToCreate`, `AwaitingSignup`, `OnHold`, `Suspended`, `Unpaid`, `TrialEnded`,
`SoftFailure`, `Assessing`, `Paused`) — `StringEnum<T>`, read via `.Value`. `IntervalUnit` — `.Value`.

**Client construction / auth / server (source: `sdk-map.md` Servers & auth):** `MaxioAdvancedBillingClientOptions`
`{ BasicAuth = new BasicAuthCredentials { Username = Maxio:ApiKey, Password = "x" }, Environment = ServerEnvironment.Us }`.
Set `options.Server.Production.Us.Site = Maxio:Subdomain` (the `{site}` template var). If `Maxio:BaseUrl` is
non-blank, set `options.Server.Production.Us.BaseUrl = Maxio:BaseUrl` verbatim (override). **As shipped**
(`MaxioServiceCollectionExtensions`): registered as a singleton over a **named** `IHttpClientFactory` client
(not the SDK DI extension) so the pipeline is scoped to this SDK and `Logging.LoggerFactory` is set
explicitly (PII-safe, disarms the env-var body-logging), with `Retry.Timeout` = 10s per attempt and a
`SocketsHttpHandler` `PooledConnectionLifetime`. `client.ProductFamilies` / `client.Customers` /
`client.Subscriptions` are the groups. Optional operational settings (additive, non-secret):
`Maxio:DefaultPlanHandle`, `Maxio:PaymentCollectionMethod`.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
|---|---|---|
| `product_handle` passed to CreateSubscription must be one the plan list returns for the configured family | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` | implementation — subscribe validates the requested handle against the live family-products list before calling Maxio; unknown handle → 400 without a provider call |
| `customer_id` passed to CreateSubscription must be an id returned by ensure-customer (read-by-reference / create) | `Subscriptions.CreateSubscription` ← `Customers.ReadCustomerByReference`/`CreateCustomer` | implementation |

## 3. Trap notes (hazard + skill pointer; not resolved here)

- **Client/HttpClient lifetime** — the DI extension owns the pipeline; getting singleton-vs-transient and
  `IHttpClientFactory` wrong leaks sockets or rebuilds per request. **MUST load dotnet-client-initialization.**
- **Basic-auth wiring & when credentials are applied** — a credential never set is silently skipped and the
  call still goes out, so a 401 can mean "nothing sent". **MUST load dotnet-authentication.**
- **Positional mis-binding on list ops** — `ListProductsForProductFamily`/`ListCustomerSubscriptions` have many
  optional params without C# defaults; call with named arguments or they mis-bind. **MUST load dotnet-calling-endpoints.**
- **`StringEnum<T>` is not a C# enum; unions use `TryGet…`; unknown response fields land in `AdditionalProperties`** —
  reading `State`/`IntervalUnit` and building request records. **MUST load dotnet-models.**
- **Two error directions + Case A/B mix** — `JsonException` on a drifted 2xx body is NOT an `SdkException`; a
  non-2xx body that doesn't match its `{Operation}Error` throws `JsonException` and destroys the status. Each op's
  case differs (some Case A typed, some Case B raw). **MUST load dotnet-error-handling.**
- **`Timeout` is per-attempt not total; `LogRequestBody` logs JSON unredacted; `MAXIOADVANCEDBILLINGCLIENT_LOG`
  can arm body logging unless `LoggerFactory` is set** — timeout budget, retry set, PII logging. **MUST load dotnet-configuration-resilience.**
- **Test seam is the `HttpClient` ctor arg; match the project's framework** — faking the SDK for unit tests.
  **MUST load dotnet-testing.**

## 4. REQUIRED READING (load all before implementation; contents deliberately not restated here)

| Skill (plugin: maxio-platforms-team) | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction & DI registration |
| `dotnet-authentication` | Step 2 — Basic-auth credentials |
| `dotnet-calling-endpoints` | Step 4 — all SDK calls, named-arg list ops |
| `dotnet-models` | Step 4 — building request records, reading `StringEnum`/unions |
| `dotnet-error-handling` | Step 4 — error boundary (always required) |
| `dotnet-configuration-resilience` | Step 2/4 — retries, timeout budget, logging/PII |
| `dotnet-testing` | Step 6 — unit tests for the seam |

**Two mandatory `JsonException` hazards (both directions):** a drifted/malformed **2xx** body (missing `required`
member) surfaces as `System.Text.Json.JsonException` from deserialization — **not** an `SdkException`, so an
SDK-exception-only catch ladder lets it escape; and a **non-2xx** body that doesn't match its operation's generated
`{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, **replacing** the
`SdkException` and destroying the HTTP status. The error boundary catches `JsonException` alongside `SdkException<…>`.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | **Credential fail-fast** | `MaxioSettings` validated at startup (a hosted `IValidateOptions`/startup check): `ApiKey`, `Subdomain`, `ProductFamilyHandle` each non-null/non-blank → host throws on start otherwise. `BaseUrl` optional (blank allowed). Every part checked (blank ≠ missing). |
| 2 | **Secret sourcing & rotation** | `Maxio:ApiKey` from **.NET user-secrets** (loaded from env var `MAXIO_API_KEY` by me; never written to any repo file). Options built once at registration and captured in the client singleton → rotation needs a process restart; acceptable for this sandbox app (documented; no hot-rotation requirement). |
| 3 | **Total timeout budget** | SDK `Retry.Timeout` is **per attempt**. Each service call is wrapped in a linked `CancellationTokenSource` with an overall deadline (e.g. 30s) passed as `ct`, which is the only thing bounding the whole retried call. Enforced in `MaxioBillingService`. |
| 4 | **Write-retry ownership** | SDK default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes are **POST** (`CreateCustomer`, `CreateSubscription`) → never resent by the SDK. No PUT/DELETE writes. Default retry set kept. |
| 5 | **Idempotency & ambiguous writes** | **Customer:** key = Maxio customer `reference` (= eShop username); uniqueness **confirmed** in SDK remarks. Read-by-reference first; on `CreateCustomer` 422 (concurrent dup) re-read by reference. **Subscription:** key = deterministic `reference` `eshop:{username}:{productHandle}`; `FindSubscription` first, then `CreateSubscription`; on 422 re-read via `FindSubscription`. Injected `Idempotency-Key` header (fresh Guid) is **not** used as a key. Subscription-reference uniqueness is now **LIVE-CONFIRMED** (Step 6): a duplicate create returns 422 *"Reference: must be unique - that value has been taken."* (see §6). Code: `MaxioBillingService.SubscribeAsync`/`EnsureCustomerAsync`. |
| 6 | **Observability** | Information: subscribe requested/created/reused, plan-list served. Warning/Error: provider errors with HTTP status + Maxio error text from `RawError.ReadAsString()` / typed error payloads (which carry per-field messages) into our `IAppLogger`. `LogRequestBody` **off**. No provider request-id header is documented; we log the operation + reference for correlation. |
| 7 | **Sensitive data** | `CreateCustomer` carries **email (PII)**. Therefore `LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is set explicitly at registration so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on from outside the code. Our own logs never echo the request body; we log username + reference only. |
| 8 | **Environment selection** | `ServerEnvironment.Us` (sandbox is the chargify site `{Subdomain}.chargify.com`). `{site}` bound from `Maxio:Subdomain`; optional `Maxio:BaseUrl` verbatim override. SDK has **no distinct sandbox environment** — test traffic is kept off any live system by binding the sandbox subdomain from config and hard-coding no subdomain/URL. Each deployment sets its own `Maxio:Subdomain` (and optionally `Maxio:BaseUrl`). |
| 9 | **Duplicate prevention under concurrency** | Store = **the Maxio site (provider)**; claim columns = **customer `reference`** and **subscription `reference`**; the provider's **reference-uniqueness constraint** rejects the second create with 422 and the code **catches that rejection** (`catch (SdkException<CreateSubscriptionError>)` / `catch (SdkException<CreateCustomerError>)`) and reconciles by re-reading by reference. No in-memory dict / semaphore / single-host assumption / bare existence-check is relied on for the guarantee (the read-first is only a fast path). Customer-ref uniqueness confirmed by SDK remarks; subscription-ref uniqueness **LIVE-CONFIRMED in Step 6** (duplicate create → 422 "Reference: must be unique"). The in-memory EF DB carries **no** claim (it can't — loses data on restart, ignores unique constraints), which is exactly why the claim lives in Maxio. |
| 10 | **Partial results** | `ListProductsForProductFamily` is page-based. We **loop pages** (`perPage=200`, advance `page` until a short page) so the full plan set is returned — no cap, no silent truncation. `ListCustomerSubscriptions` returns a single response (no pagination bullet on its row) — returned whole. |
| 11 | **Startup validation vs the existing test host** | `tests/PublicApiIntegrationTests` boots the real `Program` via `WebApplicationFactory<Program>`. It gets **placeholder** (non-secret) `Maxio:*` values added to its `appsettings.test.json` so the fail-fast check passes and the host boots. No live Maxio call happens at startup (validation only). This project is **run green** in Step 6. |
| 12 | **Ordering & no-op side effects** | Maxio is the record; we keep **no** separate local subscription row (in-memory DB loses it on restart; a persisted userId↔subscription map only survives one run). So "local write before provider call" is **N/A** — the durable record is the Maxio subscription keyed by `reference`, re-readable any time. We send **no** side effects of our own (no emails/notifications — Maxio owns customer comms); the idempotent subscribe returns the existing subscription without a second create when one is already present (create gated on `FindSubscription` miss). |
| 13 | **Unknown outcomes** | If the transport fails after a write may have been received: `CreateCustomer` → re-read via **`Customers.ReadCustomerByReference(reference)`**; `CreateSubscription` → re-read via **`Subscriptions.FindSubscription(reference)`**. Both search by the deterministic reference; a found record is treated as success, otherwise the error is surfaced. |
| 14 | **Provider status & reconciliation clocks** | `CreateSubscription`/`FindSubscription`/list return **`Subscription.State`**. We branch on it: `Active`/`Trialing` → reported as active; `Pending`/`AwaitingSignup`/`Assessing` → reported pending; `PastDue`/`SoftFailure`/`Unpaid`/`Canceled`/`Expired`/`FailedToCreate`/`OnHold`/`Suspended`/`TrialEnded` → surfaced verbatim as the state (not masked to success). No `status ?? "active"` defaulting. No two-source reconciliation clock (my-subscriptions reads live from Maxio each call). |

## 6. Assumptions & Blockers

- **Assumption (minor):** eShop username (JWT `ClaimTypes.Name`, an email) is a stable per-user identifier suitable
  as the Maxio customer `reference`. Confirmed by `IdentityTokenClaimService` (token carries only `ClaimTypes.Name`
  + roles). First/last name are split from the username local-part (no richer profile is exposed to PublicApi).
- **Assumption (minor):** default subscribe target when the request omits a plan is the optional
  `Maxio:DefaultPlanHandle` setting (set to `eshop-pro` for this sandbox via user-secrets), else the first plan
  the family returns — kept catalog-agnostic rather than hard-coding `eshop-pro`. Any plan handle the family
  returns is accepted. Both `eshop-pro` and `basic-plan` verified present with `require_credit_card=false`.
- **CONFIRMED (live, Step 6):** Maxio enforces subscription `reference` uniqueness — a duplicate create returns
  `422 "Reference: must be unique - that value has been taken."` The defensive path (`FindSubscription` before
  create + catch-422/re-read after) is therefore a real concurrency guarantee, not just a fast path.
- **CONFIRMED (live, Step 6):** despite `require_credit_card=false`, the default `automatic` collection method
  rejects card-free signup with 422 *"No payment method was on file…"*. The integration sets
  `payment_collection_method = remittance` (configurable via optional `Maxio:PaymentCollectionMethod`;
  legacy Statements sites use `invoice`) so subscriptions are created `active` on an open invoice.
- **No Blockers.** Every capability the flow needs (list family products, read/create customer, find/create
  subscription, list customer subscriptions) exists in the SDK map.
