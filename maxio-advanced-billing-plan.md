# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Additive, parallel subscription capability exposed on `src/PublicApi` (JWT-authenticated). Maxio
Advanced Billing is the **system of record**; eShopOnWeb stores no local subscription/customer
mapping (the environment's in-memory DB loses data on restart and ignores unique indexes, so it
cannot durably carry an idempotency claim — see §6). Identity is derived from the JWT
(`ClaimTypes.Name` → username → `UserManager` → user Id + Email). All Maxio identity is keyed by a
**deterministic reference** derived from the eShop user Id, which is what makes every operation
idempotent and reconciliable without local state.

## 1. Scope & sequence

1. **Vendor + reference the SDK.** Copy the Maxio .NET SDK source into `sdk/MaxioAdvancedBilling/`
   (unpublished to NuGet → build from source). Isolate it from the repo's Central Package
   Management with a local `Directory.Packages.props` (`ManagePackageVersionsCentrally=false`), and
   add a `ProjectReference` from `PublicApi`.
2. **Settings + fail-fast + client DI.** Bind `MaxioSettings` from the `Maxio:` section
   (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`), validate at startup (host refuses to
   start on any missing/blank required part), register `MaxioAdvancedBillingClient` as a singleton
   over an `IHttpClientFactory`-managed `HttpClient`. Uses operations: none (wiring only).
3. **Application service `IMaxioSubscriptionService` / `MaxioSubscriptionService`.**
   - `GetPlansAsync` → `client.ProductFamilies.ListProductsForProductFamily("handle:{familyHandle}", …)`.
   - `EnsureCustomerAsync(userId,email)` → `client.Customers.ReadCustomerByReference(reference)` then,
     if absent, `client.Customers.CreateCustomer(…)`, catching the duplicate-reference rejection and
     re-reading (§6, DUPLICATE CLAIMS).
   - `SubscribeAsync(userId,email,productHandle)` → ensure customer, look up an existing live
     subscription via `client.Customers.ListCustomerSubscriptions(customerId)`, else
     `client.Subscriptions.CreateSubscription(…)` with a deterministic `reference`, catching the
     duplicate-reference rejection and re-reading via `client.Subscriptions.FindSubscription(reference)`.
   - `GetMySubscriptionsAsync(userId,email)` → ensure customer, `ListCustomerSubscriptions(customerId)`.
4. **Three endpoints** following PublicApi's `IEndpoint<>` (MinimalApi.Endpoint) convention:
   `GET /api/subscription-plans` (JWT), `POST /api/subscriptions` (JWT), `GET /api/my-subscriptions` (JWT).
5. **Tests** (integration-layer, faking the `HttpClient` seam) + end-to-end manual verification.

No capability is missing from the map; §6 has no functional blockers.

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier; the cancellation-token parameter really is named `ct`, so named arguments write `ct:`.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies** (taken
> from the path the map gives for THAT type): records → `MaxioAdvancedBilling.Models`, enums →
> `MaxioAdvancedBilling.Models.Enums`, client/options → `MaxioAdvancedBilling`, servers →
> `MaxioAdvancedBilling.Servers`, errors → `MaxioAdvancedBilling.Errors`, `RawError`/`SdkException` →
> `MaxioAdvancedBilling.Core.*`.

| Operation | Signature (verbatim) | Request model + fields used | Response envelope + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = `"handle:" + familyHandle` (source: id **or** handle prefixed `handle:`). All filter params `null`; `includeArchived: false`. | `IReadOnlyList<ProductResponse>`; each `.Product` → `Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit(.Value), ProductPricePointHandle` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | offset (`page`/`perPage`, default 20, max 200) — **loop to exhaust** | map/operations/ProductFamilies.md; Models/Product.cs; Api/ProductFamilies.cs (`handle:` prose) |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` = deterministic customer ref | `CustomerResponse.Customer` → `Id, Reference, Email` | **Case B** `SdkException<RawError>` (404 when absent → `.Error.StatusCode`) | none | map/operations/Customers.md; Models/CustomerResponse.cs |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer = CreateCustomer{ required FirstName, LastName, Email; Reference } }` | `CustomerResponse.Customer` → `Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | none | map/operations/Customers.md; Models/CreateCustomer.cs |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` from ensured customer | `IReadOnlyList<SubscriptionResponse>`; `.Subscription` → `Id, State(.Value), Reference, Product, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt` | **Case B** `SdkException<RawError>` | none (per-customer list) | map/operations/Customers.md; Models/Subscription.cs |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ required Subscription = CreateSubscription{ ProductHandle, CustomerId, Reference } }` | `SubscriptionResponse.Subscription` → `Id, State(.Value), Product, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt, Reference` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | map/operations/Subscriptions.md; Models/CreateSubscription.cs |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` = deterministic subscription ref | `SubscriptionResponse.Subscription` | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none | map/operations/Subscriptions.md |

### CreateSubscription — optional fields carried, with purpose

| Field (wire) | Type | Purpose / why set (or omit) |
| --- | --- | --- |
| `ProductHandle` (`product_handle`) | `string?` | The plan to subscribe to; handle is stable (IDs are not). Alternative to `product_id`. **Set.** |
| `CustomerId` (`customer_id`) | `int?` | Bind subscription to the ensured Maxio customer. Chosen over `customer_reference` because we already resolved the numeric id and it is unambiguous. **Set.** |
| `Reference` (`reference`) | `string?` | App-provided idempotency/reconciliation key for the subscription itself (`eshop-sub:{userId}:{productHandle}`). **Set.** |
| `ProductPricePointHandle` (`product_price_point_handle`) | `string?` | omit → provider default price point for the product (both seeded plans have a default). **Omit.** |
| `PaymentCollectionMethod` (`payment_collection_method`) | `CollectionMethod?` | **Set to `Remittance`** (`YOUR CALL — not in the map`). omit → provider default `automatic`, which attempts to auto-charge the first period and fails on these no-card plans (verified live: *"No payment method was on file for the $299.00 balance"*). Remittance issues an invoice instead, so card-free signup succeeds — the task's stated intent. (Relationship-Invoicing sites use `remittance`; legacy Statements sites would use `invoice`.) |
| everything else (trial/card/coupon/currency/import fields) | — | omit → provider default. Payment method not required on these plans, so no card/payment_profile fields. **Omit.** |

### CreateCustomer — required + optional fields

| Field (wire) | Type | Purpose |
| --- | --- | --- |
| `FirstName` (`first_name`) | `required string` | Required by model. Derived from email local-part (no name fields on `ApplicationUser`). |
| `LastName` (`last_name`) | `required string` | Required by model. Set to `"(eShopOnWeb)"` sentinel — no surname available. |
| `Email` (`email`) | `required string` | The eShop user's email. |
| `Reference` (`reference`) | `string?` | **Set** — deterministic customer ref = eShop user Id. This is the idempotency claim (§6). omit would lose idempotency. |

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| The `productHandle` a caller may POST to `/api/subscriptions` must be one returned by the plans listing for the configured family | `CreateSubscription` ← `ListProductsForProductFamily` | implementation (endpoint validates the requested handle against the live plan list before subscribing) |
| The `customer_id` passed to `CreateSubscription` must be an id returned by `ReadCustomerByReference`/`CreateCustomer` | `CreateSubscription` ← `ReadCustomerByReference`/`CreateCustomer` | implementation (`EnsureCustomerAsync` resolves it immediately before the create) |

### Enums needed

| Enum | Members used (C# → wire) | Source |
| --- | --- | --- |
| `SubscriptionState` | read only, via `.Value` (e.g. `active`, `trialing`, `pending`, `assessing`, `awaiting_signup`, `past_due`, `soft_failure`, `unpaid`, `canceled`, `expired`, `failed_to_create`, `on_hold`, `suspended`, `paused`, `trial_ended`) | Models/Enums/SubscriptionState.cs |
| `IntervalUnit` | read only, via `.Value` (`month`/`day`) for plan display | Models/Enums/IntervalUnit.cs |

### Client construction / auth / server

- Construct: `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, options)` (only ctor).
- Auth: **Basic** — `options.BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }`
  (username = Chargify API key, password = `x`; Basic works only on US/EU/chargify.com). Source: sdk-map.md Servers & auth.
- Environment: `ServerEnvironment.Us` (sandbox is US-hosted chargify.com; `MAXIO_ENVIRONMENT=US`). Set
  `options.Server.Production.Us.Site = <Subdomain>`. If `Maxio:BaseUrl` is set, use it verbatim via
  `options.Server.Production.Us.BaseUrl = <BaseUrl>`. Source: sdk-map.md Servers & auth.

## 3. Trap notes

- Client/`HttpClient` lifetime: the handler pipeline must be long-lived / `IHttpClientFactory`-managed, not rebuilt per request — getting the singleton-vs-transient split wrong leaks sockets or captures a stale handler. **MUST load `maxio-platforms-team:dotnet-client-initialization`.**
- Auth wiring: credential must be set before/at client construction and sourced from config not literals; a never-set credential is silently skipped and surfaces as a 401, not a throw. **MUST load `maxio-platforms-team:dotnet-authentication`.**
- Calling list/search ops: many optional params have no C# default and mis-bind positionally — the hazard is silent wrong-argument binding. **MUST load `maxio-platforms-team:dotnet-calling-endpoints`.**
- Models: `StringEnum<T>` is not a C# enum and unions/extension-data have non-obvious access; the hazard is constructing/reading these wrong. **MUST load `maxio-platforms-team:dotnet-models`.**
- Error boundary: Case A vs Case B, `TryGet…` accessors, and `JsonException` reaching the boundary from two directions (see REQUIRED READING). **MUST load `maxio-platforms-team:dotnet-error-handling`.**
- Resilience/config: what `Timeout` bounds (per-attempt, not total), which verbs retry (POST never; PUT does), base-URL/server override, pagination, and that `LogRequestBody` does not redact JSON. **MUST load `maxio-platforms-team:dotnet-configuration-resilience`.**
- Testing seam: the `HttpClient` ctor arg is the fake seam; match the project's test framework. **MUST load `maxio-platforms-team:dotnet-testing`.**

## 4. REQUIRED READING (load all before implementation)

- `maxio-platforms-team:dotnet-client-initialization` — step 2, client/DI construction.
- `maxio-platforms-team:dotnet-authentication` — step 2, Basic-auth credential wiring.
- `maxio-platforms-team:dotnet-calling-endpoints` — step 3, every SDK call (named args).
- `maxio-platforms-team:dotnet-models` — step 3, building requests / reading `StringEnum`.
- `maxio-platforms-team:dotnet-error-handling` — step 3, the error boundary.
- `maxio-platforms-team:dotnet-configuration-resilience` — step 2/3, timeout budget, retries, pagination, logging.
- `maxio-platforms-team:dotnet-testing` — step 5, tests.

The sheet deliberately carries none of these skills' contents. Two mandatory `JsonException` hazards:
- A drifted/malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** an `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, **replacing** the `SdkException` and destroying the HTTP status.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; a `ValidateOnStart` validator rejects blank `ApiKey`, `Subdomain`, `ProductFamilyHandle` (each part checked independently; `BaseUrl` optional). Host refuses to start. Where: `MaxioServiceCollectionExtensions.AddMaxioSubscriptions`. |
| 2 | Secret sourcing & rotation | Secret is `Maxio:ApiKey` from .NET user-secrets (never in repo). Options built once at registration and captured in the singleton client → a rotated key takes effect on process restart only. Documented; no hot-rotation requirement for this demo. |
| 3 | Total timeout budget | Per-call budget enforced by a `CancellationToken` (linked, ~30s) created in the service and passed as `ct:` to every SDK call — because `RetryOptions.Timeout` is per-attempt, not total. Where: `MaxioSubscriptionService`. |
| 4 | Write-retry ownership | `CreateCustomer`/`CreateSubscription` are POST → the SDK's default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) never resends them. Reads (GET) may retry. We keep defaults; idempotency handled by §5.5/§6. |
| 5 | Idempotency & ambiguous writes | No real caller-supplied idempotency-key param exists on these ops (injected `Idempotency-Key` header is per-call `Guid` → not a key). Reconciliation instead: deterministic `reference` (`customer` = userId; `subscription` = `eshop-sub:{userId}:{productHandle}`) + re-read (`ReadCustomerByReference` / `FindSubscription`). |
| 6 | Observability | `ILogger` logs at Information (subscribe start/outcome with userId, plan handle, resulting subscription id + state) and Warning/Error on provider failures, including the provider error text pulled from `ErrorListResponse1.Errors` / `RawError.ReadAsString()`. `LogRequestBody` left **off**. |
| 7 | Sensitive data | Request models in scope (`CreateCustomer`, `CreateSubscription` w/o card fields) carry email + name — mildly personal, no card/bank data (payment method not required). `LogRequestBody` stays off and `options.Logging.LoggerFactory` is set explicitly so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on from outside code. Our own logs never echo request bodies. |
| 8 | Environment selection | One server group in scope: `Production` on `ServerEnvironment.Us` → `https://{Subdomain}.chargify.com` (sandbox). `Maxio:BaseUrl` overrides the base URL verbatim when set. No separate SDK "sandbox" environment exists; test traffic is kept off live systems by pointing `Subdomain`/`BaseUrl` at the sandbox site via config only (no hard-coded host). |
| 9 | Duplicate prevention under concurrency | Store = **Maxio (provider, system of record)**; column = customer `reference` and subscription `reference`. Maxio enforces per-site uniqueness of `reference`; the second concurrent create is rejected (HTTP 422) and the catch re-reads the winner. Cross-host, durable, no in-process lock. (Local relational unique constraint is unavailable here: the in-memory provider ignores unique indexes and loses data on restart — §6.) See DUPLICATE CLAIMS table. `where`: `MaxioSubscriptionService.EnsureCustomerAsync` (customer) and `MaxioSubscriptionService.SubscribeAsync` (subscription) 422 catch blocks. |
| 10 | Partial results | Only paged read is `ListProductsForProductFamily`. The service loops pages until a short page (`< perPage`) is returned, so the returned plan list is complete by construction; no truncation flag needed. `where`: `MaxioSubscriptionService.GetPlansAsync` page loop (`if (pageItems.Count < PlansPerPage) break;`). |
| 11 | Startup validation vs test host | The credential check runs in `AddMaxioSubscriptions` which `Program.cs` calls. The host-booting test project is `tests/PublicApi.IntegrationTests` (boots `Program` via `WebApplicationFactory`). It is given placeholder `Maxio:*` config (via `appsettings.test.json` / factory config) so the validator passes without real secrets, and the SDK client is present but never called by the existing tests. Ran and green (recorded in §5 verification). |
| 12 | Ordering & no-op side effects | No local store write precedes the provider call (Maxio is sole system of record — WRITE ORDER = none, justified). Idempotent transitions: subscribe returns the existing live subscription (found via `ListCustomerSubscriptions`) without a second create; no unconditional side effect (no email/notification) fires after subscribe. |
| 13 | Unknown outcomes | On transport failure after a create may have been received: the catch re-reads by the deterministic reference — customer via `ReadCustomerByReference(customerRef)`, subscription via `FindSubscription(subscriptionRef)` — and returns the found record instead of a definite failure. See UNKNOWN OUTCOMES. `where`: `MaxioSubscriptionService.EnsureCustomerAsync` and `SubscribeAsync` `catch when (IsTransportOrTimeout(...))` blocks. |
| 14 | Provider status & reconciliation clocks | `CreateSubscription`/`ListCustomerSubscriptions` return `Subscription.State`. Code sorts states into done (`active`,`trialing`) / not-yet (`pending`,`assessing`,`awaiting_signup`, absent/unreadable) / failed (`failed_to_create`,`canceled`,`expired`,`past_due`,`soft_failure`,`unpaid`,`on_hold`,`suspended`,`paused`,`trial_ended`) and returns the real state to the caller (never coalesces absent→active). No two-source reconciliation clock (single source: Maxio). See OPERATION OUTCOMES. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| CreateCustomer | Maxio customers, `reference` column (= eShop userId) | Maxio per-site uniqueness on `reference` → HTTP 422 `CustomerErrorResponse1` | catch in `EnsureCustomerAsync` → `ReadCustomerByReference` re-read returns the winner | `MaxioSubscriptionService.EnsureCustomerAsync`, `catch (SdkException<CreateCustomerError>)` → `ReadCustomerByReferenceOrNullAsync` |
| CreateSubscription | Maxio subscriptions, `reference` column (= `eshop-sub:{userId}:{productHandle}`) | Maxio per-site uniqueness on `reference` → HTTP 422 `ErrorListResponse1` | catch in `SubscribeAsync` → `FindSubscription(reference)` re-read returns the winner | `MaxioSubscriptionService.SubscribeAsync`, `catch (SdkException<CreateSubscriptionError>)` → `TryFindSubscriptionAsync` |

> Note: Maxio's server-side uniqueness of `reference` is a provider behaviour not stated in the SDK
> source → labelled **UNVERIFIED** in §6. The code is written to be correct either way: a pre-write
> read narrows the window, and *any* create failure is followed by a re-read by reference, so a
> duplicate is reconciled to the existing record rather than duplicated. This is not an in-process
> lock, not an existence-check-as-claim (the claim is the provider rejection + re-read), and not a
> single-host assumption.

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short | where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | `perPage` (≤200) per page | Not cut short: the service loops pages until a page shorter than `perPage`, returning the full set; the return type is the complete `IReadOnlyList` | `MaxioSubscriptionService.GetPlansAsync` (page loop; `MaxPlanPages` backstop only logs a warning, never truncates a real family) |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| POST /api/subscriptions (subscribe) | a pre-existing live subscription found via `ListCustomerSubscriptions` (or `FindSubscription` after a duplicate 422) means no new subscription was created | the `CreateSubscription` call itself is gated (skipped when a live subscription already exists); the response is marked `alreadySubscribed=true` and no create-side logging/side effect fires | `MaxioSubscriptionService.SubscribeAsync` — `existing?.Subscription is not null` early return before the `CreateSubscription` call |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| CreateCustomer | `ReadCustomerByReference` | customer `reference` (= eShop userId) | `MaxioSubscriptionService.EnsureCustomerAsync`, `catch (Exception ex) when (IsTransportOrTimeout(ex, callerCt))` → `ReadCustomerByReferenceOrNullAsync` (returns the found id, else throws unknown-outcome) |
| CreateSubscription | `FindSubscription` | subscription `reference` (= `eshop-sub:{userId}:{productHandle}`) | `MaxioSubscriptionService.SubscribeAsync`, `catch (Exception ex) when (IsTransportOrTimeout(ex, callerCt))` → `TryFindSubscriptionAsync` (returns the found subscription, else throws unknown-outcome) |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| CreateSubscription | `Subscription.State` (`SubscriptionState?`) | done → `active`, `trialing` (return success + state). not-yet → `pending`, `assessing`, `awaiting_signup`, **and** absent/unreadable state (return "provisioning" with the real state; never coalesced to active). failed → `failed_to_create`, `canceled`, `expired`, `past_due`, `soft_failure`, `unpaid`, `on_hold`, `suspended`, `paused`, `trial_ended` (surface as AttentionRequired with the real state). | `MaxioSubscriptionService.Classify` (state→`SubscriptionOutcome`) and `ToView`; surfaced to the caller as `SubscriptionDto.State` + `SubscriptionDto.Status` |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| (none) | — | — | none — Maxio is the sole system of record; no local store is written by any subscription write (justified in §6). Reconciliation uses the deterministic reference (`MaxioSubscriptionService.CustomerReference` / `SubscriptionReference`), derivable with zero local state. |

## 6. Assumptions & Blockers

- **Assumption (minor):** eShop username == email for demo users; where they differ, `EnsureCustomerAsync`
  resolves the real `Email` from `UserManager` by the JWT name claim. `ApplicationUser` has no
  first/last name → `FirstName` = email local-part, `LastName` = `"(eShopOnWeb)"`.
- **Assumption (minor):** Sandbox is US-hosted chargify.com, so `ServerEnvironment.Us` + Basic auth.
  `MAXIO_ENVIRONMENT=US` confirms this. No `Maxio:Environment` key is bound (not in the mandated key
  list); environment is fixed to US with `Maxio:BaseUrl` as the only host override.
- **UNVERIFIED (labelled):** Maxio enforces per-site uniqueness of customer/subscription `reference`
  (the DUPLICATE CLAIMS rejection). Not stated in SDK source; only live traffic confirms it. Handled
  by defensive coding: every create failure is followed by a re-read by reference (§5.9/§5.13), so
  correctness does not depend on the rejection firing — at worst a benign duplicate is avoided by the
  pre-write read, and reconciliation still returns the existing record.
- **No functional blockers.** Local durable persistence is intentionally not used (in-memory provider
  ignores unique indexes and loses data on restart; LocalDB absent) — Maxio is the system of record,
  which is a design decision, not a blocker.
