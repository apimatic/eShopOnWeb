# Maxio Advanced Billing integration — plan & contract sheet

Adds recurring-subscription billing to **eShopOnWeb** with Maxio Advanced Billing as the
system of record. Additive/parallel to the existing Catalog→Basket→Order flow. Surfaced as
three JWT-authenticated HTTP endpoints on `src/PublicApi`:

- `GET  /api/subscription-plans`  — list plans in the configured product family
- `POST /api/subscriptions`       — ensure a Maxio customer for the caller, subscribe them
- `GET  /api/my-subscriptions`    — list the caller's subscriptions

Caller identity comes from the JWT (`ClaimTypes.Name` = eShop username/email). SDK: unpublished,
built from source and vendored into the repo (netstandard2.0, C# 14); consumed via
`services.AddMaxioAdvancedBillingClient(...)`.

---

## 1. Scope & sequence

1. **Vendor the SDK** into `src/Integrations/MaxioAdvancedBilling.Sdk/` (copied source, CPM opt-out),
   `ProjectReference` from `PublicApi`.
2. **Config + DI** — `MaxioSettings` bound from the `Maxio:` section; register the client via
   `AddMaxioAdvancedBillingClient`; fail-fast validation at startup.
3. **`IMaxioSubscriptionService`** (singleton) — the integration layer wrapping the SDK:
   - `ListPlansAsync` → `ProductFamilies.ListProductsForProductFamily`
   - `SubscribeAsync` → ensure customer (`Customers.ReadCustomerByReference` → else `Customers.CreateCustomer`)
     + dedupe (`Customers.ListCustomerSubscriptions`) + `Subscriptions.CreateSubscription`, all under a
     per-reference in-process lock.
   - `ListMySubscriptionsAsync` → ensure customer + `Customers.ListCustomerSubscriptions`
   - Error translation → `MaxioApiException(statusCode, messages)`.
4. **Three endpoints** (MinimalApi.Endpoint `IEndpoint<...>` convention) + DTOs, `[Authorize]` (JWT).
5. **Tests** (integration layer, faking the HttpClient seam).

No capability is invented: every data path maps to an operation below.

---

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C# identifier;
in named arguments use exactly those names (the cancellation-token parameter is `ct`, so write `ct:`).
⚠ **Every SDK type is fully-qualified by the namespace its source path implies** (taken from THAT
type's map/source path, never a neighbour's): `Models/` → `MaxioAdvancedBilling.Models`;
`Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`; `Errors/` → `MaxioAdvancedBilling.Errors`;
client/options root → `MaxioAdvancedBilling`; `Servers/` → `MaxioAdvancedBilling.Servers`;
`Core/Authentication/Basic/` → `MaxioAdvancedBilling.Core.Authentication.Basic`;
`Core/Exceptions/` → `MaxioAdvancedBilling.Core.Exceptions`; `Core/ErrorResponse/` → `MaxioAdvancedBilling.Core.ErrorResponse`.

| Op | Controller · signature | Request model + fields used | Response envelope → fields read | Error case + accessors | Pag. | Source |
|----|----|----|----|----|----|----|
| List plans | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass the 8 nullable filters as `null` explicitly | (query only) `productFamilyId` = family **handle**; `includeArchived: false` | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product` (required) → `Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit, ProductPricePointHandle` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/ProductFamilies.md · Models/ProductResponse.cs · Models/Product.cs |
| Read customer by ref | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | (query) `reference` | `CustomerResponse.Customer` (required) → `Id, FirstName, LastName, Email, Reference` | **Case B** `SdkException<RawError>` — `.Error.StatusCode` (404 ⇒ not found) | none | map/operations/Customers.md · Models/CustomerResponse.cs · Models/Customer.cs |
| Create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — body **must pass explicitly** | `CreateCustomerRequest{ Customer (required): CreateCustomer }`; `CreateCustomer{ FirstName* (first_name), LastName* (last_name), Email* (email), Reference? (reference) }` | `CustomerResponse.Customer` → `Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Customers.md · Models/CreateCustomerRequest.cs · Models/CreateCustomer.cs |
| List customer subs | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | (path) `customerId` | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` (see below) | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md · Models/SubscriptionResponse.cs |
| Create subscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — body **must pass explicitly** | `CreateSubscriptionRequest{ Subscription (required): CreateSubscription }`; `CreateSubscription{ ProductHandle? (product_handle), CustomerId? (customer_id) }` — all fields optional; we set ProductHandle + CustomerId | `SubscriptionResponse.Subscription` (nullable) → `Id, State, ProductPriceInCents, CurrentBillingAmountInCents, CurrentPeriodEndsAt, NextAssessmentAt, Product{ Id, Name, Handle }` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md · Models/CreateSubscriptionRequest.cs · Models/CreateSubscription.cs · Models/Subscription.cs |

**Response/error payload shapes** (source in cell):
- `SubscriptionResponse.Subscription : Subscription?` — `Id:int?`, `State:SubscriptionState?`, `ProductPriceInCents:long?`, `CurrentBillingAmountInCents:long?`, `CurrentPeriodEndsAt:DateTimeOffset?`, `NextAssessmentAt:DateTimeOffset?` (**this is the next-billing date**), `Product:Product?`. (Models/Subscription.cs)
- `ErrorListResponse1{ Errors: IReadOnlyList<string> (required, wire `errors`) }` — join for messages. (Models/ErrorListResponse1.cs)
- `CustomerErrorResponse1{ Errors: Errors1? (union) }` — for messaging, fall back to `RawError.ReadAsString()` + status. (Models/CustomerErrorResponse1.cs)
- `RawError{ StatusCode:HttpStatusCode; ReadAsString():string; ReadAsJson<T>():T? }` (Core/ErrorResponse/RawError.cs). `ApiError.TryGetRawError(out RawError)` is on every Case-A error (Core/ErrorResponse/ApiError.cs).
- `SdkException<TError>` is `sealed : Exception`, **only** member is `.Error` (no StatusCode). Catch the concrete `TError` per call site. (Core/Exceptions/SdkException.cs)

**Enums needed** (`Models/Enums/`, `StringEnum<T>` — build with static members or `T.FromValue("wire")`; render via `.Value`):
- `SubscriptionState`: `Active`(active) `Trialing`(trialing) `Assessing`(assessing) `Pending`(pending) `PastDue`(past_due) `SoftFailure`(soft_failure) `Suspended`(suspended) `Paused`(paused) `Unpaid`(unpaid) `OnHold`(on_hold) `AwaitingSignup`(awaiting_signup) `TrialEnded`(trial_ended) `Canceled`(canceled) `Expired`(expired) `FailedToCreate`(failed_to_create). "Live" (already-subscribed) = state ∉ {Canceled, Expired, FailedToCreate}. (Models/Enums/SubscriptionState.cs)
- `IntervalUnit`: `Day`(day) `Month`(month). (Models/Enums/IntervalUnit.cs)
- `CollectionMethod`: `Automatic Remittance Prepaid Invoice`. (Models/Enums/CollectionMethod.cs) — *not set* (see §6 UNVERIFIED).

**Client / auth / server (all `YOUR CALL` composition of map facts):**
- Register: `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` — registers a **singleton** `MaxioAdvancedBillingClient` over an `IHttpClientFactory` client; options built **once at registration**. (ServiceCollectionExtensions.cs)
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (Basic works with US/EU only). (Servers&auth · MaxioAdvancedBillingClientOptions.cs · Core/Authentication/Basic/BasicAuthCredentials.cs)
- Environment: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` → Production template `https://{site}.chargify.com`. (Servers/ServerEnvironment.cs)
- Subdomain: `options.Server.Production.Us.Site = <Maxio:Subdomain>` (fills `{site}`). (Servers/ProductionOptions.cs)
- BaseUrl override: when `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` verbatim (no `{site}` token ⇒ used as-is). (Servers/ProductionOptions.cs)

---

## 3. Trap notes

- **Building the payload models** (unions like `Errors1`, enums as `StringEnum`, extension-data bags, `required` init members) — a signature won't show factory-vs-`new`, `.Value` vs `.ToString()`, or which members are `required`. → **MUST load dotnet-models**.
- **Calling list/read ops** — `ListProductsForProductFamily`/`ListCustomers`-style ops have many nullable params with no C# default that mis-bind positionally; and whether a write takes a real idempotency key (vs the injected `Idempotency-Key` header) is per-op. → **MUST load dotnet-calling-endpoints**.
- **Error boundary** — `JsonException` reaches the boundary from two directions that need opposite handling; typed vs raw error cases; `TryGetRawError` is not a catch-all. → **MUST load dotnet-error-handling**.
- **Total-call timeout & write-retry eligibility** — `Retry.Timeout` bounds an *attempt* not the whole call; `HttpMethodsToRetry` gates every retry so which verbs resend is not obvious; `LogRequestBody` does not redact. → **MUST load dotnet-configuration-resilience**.
- **Client construction & lifetime** — the HttpClient/handler pipeline must be long-lived (IHttpClientFactory) while the SDK client wrapper's lifetime is a separate decision. → **MUST load dotnet-client-initialization**.
- **Setting credentials** — where/when credentials are set relative to client construction, and loading them from configuration not literals. → **MUST load dotnet-authentication**.
- **Faking the SDK in tests** — the `HttpClient` ctor arg is the seam. → **MUST load dotnet-testing**.

---

## 4. REQUIRED READING (load every one BEFORE implementation; this sheet deliberately omits their contents)

- `maxio-platforms-team:dotnet-client-initialization` — client construction + DI registration (step 2).
- `maxio-platforms-team:dotnet-authentication` — Basic credentials wiring (step 2).
- `maxio-platforms-team:dotnet-calling-endpoints` — every SDK call (step 3).
- `maxio-platforms-team:dotnet-models` — request/response/enum/union model construction (step 3).
- `maxio-platforms-team:dotnet-error-handling` — the error-translation boundary (step 3).
- `maxio-platforms-team:dotnet-configuration-resilience` — timeout/retry/base-url/logging tuning (step 2/3).
- `maxio-platforms-team:dotnet-testing` — integration-layer tests (step 5).

**Mandatory hazard rows (verbatim):**
- A drifted/malformed **2xx** body (a missing `required` member) surfaces as `System.Text.Json.JsonException` from *deserialization*, **not** as `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|----|----|
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`. Startup validation (`IValidateOptions` + `ValidateOnStart`) requires **non-blank** `ApiKey`, `ProductFamilyHandle`, and `Subdomain` (Subdomain optional only when `BaseUrl` is set). Each part checked separately — blank ≠ missing. Host refuses to start otherwise (not a first-call 401). |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` comes from **.NET user-secrets** (loaded by us from env var `MAXIO_API_KEY`; never in repo). `AddMaxioAdvancedBillingClient` captures the options object in the singleton at registration ⇒ a rotated key takes effect only on **process restart**. Documented; acceptable for this app. |
| 3 | Total timeout budget | SDK `Retry.Timeout` is **per attempt**; the only whole-call bound is a `CancellationToken`. We thread the endpoint/`HttpContext.RequestAborted` `ct` into **every** SDK call. Overall budget ≈ per-attempt timeout × (1+MaxRetries) unless the request is aborted first. MUST load dotnet-configuration-resilience. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes `CreateCustomer` and `CreateSubscription` are **POST** ⇒ never auto-resent by the SDK (no SDK-driven duplicate writes). The reads (`ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductsForProductFamily`) are GET ⇒ retryable, which is safe. |
| 5 | Idempotency & ambiguous writes | **No** operation in scope takes a real caller-supplied idempotency key (the injected `Idempotency-Key: Guid` header is not one). `CreateCustomer` is deduped by `ReadCustomerByReference` on a stable per-user `reference`. `CreateSubscription` is deduped by scanning `ListCustomerSubscriptions` for a live subscription (state ∉ {canceled,expired,failed_to_create}) to the same product. Both run inside a **per-reference `SemaphoreSlim`** so a double-click serializes → check-then-create sees the first result. Reconciliation path = the same reference/product lookup. **Caveat:** the lock is in-process (single-run in-memory host), matching the environment's persistence limits. |
| 6 | Observability | `ILogger` logs at Information: customer ensured (created vs found), subscribe outcome (subscription id, plan handle, state, correlationId), plans listed count. Provider failures logged at Warning/Error with **status + raw error string** (from `RawError.ReadAsString()` / typed `Errors`). `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Request models in scope carry **PII** (email, name) but **no** card/bank data — payment method is not required and we never send `credit_card_attributes`/`bank_account_attributes`. `LogRequestBody` stays off; `AddMaxioAdvancedBillingClient` assigns `Logging.LoggerFactory` to the app factory (non-null) ⇒ the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot switch body logging on. We never echo request bodies in our own logs. |
| 8 | Environment selection | Basic auth ⇒ **US/EU only**. We use `ServerEnvironment.Us` → `https://{site}.chargify.com` with `{site}` = configured subdomain (sandbox site `cp-exp-5`). Sandbox isolation is by pointing at the sandbox subdomain + sandbox API key (both from env→user-secrets); no live-site creds are present. `Maxio:BaseUrl` can override the host verbatim. Gateway/Bearer not used. |

---

## 6. Assumptions & Blockers

- **Assumption** — the eShop caller's identity is the JWT `ClaimTypes.Name` (username/email, e.g. `demouser@microsoft.com`). The Maxio customer `reference` is derived deterministically from it (`eshoponweb:{username}`), giving a stable idempotency handle across runs even though the in-memory userId is not stable.
- **Assumption** — `POST /api/subscriptions` takes an optional `planHandle`; when present it must match a product in the configured family (validated against the plans list) — this keeps the code catalog-agnostic (no hardcoded `eshop-pro`). When omitted, respond 400 listing available handles.
- **Assumption** — `CreateCustomer` needs first/last name; only an email/username is known, so `FirstName` = email local-part, `LastName` = `"(eShopOnWeb)"`; `Email` = the username if it is an email, else `{username}@users.eshoponweb.local`. Real identity lives in `reference` + `email`.
- **RESOLVED (was UNVERIFIED)** — live traffic showed that *payment method not required* does **not** by itself let `CreateSubscription` succeed: the default **automatic** collection tries to charge the balance immediately and returns `422 "No payment method was on file for the $299.00 balance"`. Fix: set `PaymentCollectionMethod` to **invoice** (configurable via `Maxio:PaymentCollectionMethod`, default `invoice`; `remittance` for a Relationship Invoicing site) so the subscription activates on an invoice with no card capture. The 422 provider `errors[]` are still surfaced verbatim for any other rejection. Product handle is used via `product_handle`; the family list path uses `handle:{family}` (the `{product_family_id}` segment takes an id or a `handle:`-prefixed handle).
- **No Blockers** — every needed capability exists in the map.
