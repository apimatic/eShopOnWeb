# Maxio Advanced Billing — eShopOnWeb Subscription Integration

## Scope & Sequence

| Step | Operations | Purpose |
|------|-----------|---------|
| 1. Client registration & configuration | (DI setup, auth init) | Register Maxio SDK client with IHttpClientFactory; load credentials from user-secrets |
| 2. Service: GetSubscriptionPlans | `Products.ListProducts` | Fetch available plans from Maxio (Basic, Pro plans) |
| 3. Service: EnsureCustomerExists | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` | Idempotent customer lookup/creation by userId reference |
| 4. Service: CreateSubscription | `Subscriptions.CreateSubscription` | Enroll user in chosen plan; return subscription state & next billing date |
| 5. Service: GetUserSubscriptions | `Customers.ListCustomerSubscriptions`, `Subscriptions.ReadSubscription` | Fetch active subscriptions for logged-in user |
| 6. Endpoints | PublicApi MinimalApi handlers | Three endpoints: GET /api/subscription-plans, POST /api/subscriptions, GET /api/my-subscriptions |

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation 1: List Products

| | Details |
|---|---------|
| **Controller & method** | `client.Products.ListProducts` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Required params** | 8 nullable params: `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `include`, `includeArchived` must all pass explicitly (pass `null` to skip). Pagination defaults: `page = 1`, `perPage = 20`. |
| **Request model** | None (query params only). |
| **Response envelope** | `IReadOnlyList<ProductResponse>` — each item has one field: `Product (product): Product !req`. Product fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`. Relevant wire names: `id`, `name`, `handle`, `description`, `price_in_cents`, `interval`, `interval_unit`. |
| **Error case** | **Case B**: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — accessors: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`. |
| **Pagination** | Manual `page` + `perPage` (query params). |
| **Source** | `map/operations/Products.md` |

---

### Operation 2: Create Customer

| | Details |
|---|---------|
| **Controller & method** | `client.Customers.CreateCustomer` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Required params** | `body` (nullable, must pass explicitly). |
| **Request model** | `MaxioAdvancedBilling.Models.CreateCustomerRequest` with one required field: `Customer (customer): CreateCustomer !req`. The `CreateCustomer` model has required fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional: `Reference (reference): string?` (your app's user ID for idempotency), `Organization (organization): string?`, `Address (address): string?`, etc. Wire names: `first_name`, `last_name`, `email`, `reference`, `organization`. |
| **Response envelope** | `MaxioAdvancedBilling.Models.CustomerResponse` — one field: `Customer (customer): Customer !req`. The Customer model: `Id (id): int?` (Maxio-assigned), `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`. Key wire names: `id`, `first_name`, `last_name`, `email`, `reference`, `created_at`. |
| **Error case** | **Case A**: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — accessors: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422 Unprocessable Entity], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback]. The `CustomerErrorResponse1.Errors` field is of type `MaxioAdvancedBilling.Models.Errors` (a record with specific `IReadOnlyList<string>?` properties like `PerPage` and `PricePoint`). To collect all error messages: `(errorResp.Errors?.PerPage ?? []).Concat(errorResp.Errors?.PricePoint ?? [])`. |
| **Pagination** | None. |
| **Source** | `map/operations/Customers.md` |

---

### Operation 3: Read Customer by Reference

| | Details |
|---|---------|
| **Controller & method** | `client.Customers.ReadCustomerByReference` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Required params** | `reference` (query param; literal string of your userId). |
| **Request model** | None (query param only). |
| **Response envelope** | `MaxioAdvancedBilling.Models.CustomerResponse` — one field: `Customer (customer): Customer !req`. Same structure as CreateCustomer response. |
| **Error case** | **Case B**: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — when customer not found (404) or other error. Check `StatusCode` to distinguish 404 (not found) from other statuses. |
| **Pagination** | None. |
| **Source** | `map/operations/Customers.md` |

---

### Operation 4: Create Subscription

| | Details |
|---|---------|
| **Controller & method** | `client.Subscriptions.CreateSubscription` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Required params** | `body` (nullable, must pass explicitly). |
| **Request model** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` with one required field: `Subscription (subscription): CreateSubscription !req`. The `CreateSubscription` model has **optional** fields; use those relevant to your hero flow. Key fields: `ProductHandle (product_handle): string?` or `ProductId (product_id): int?` (choose one); `CustomerId (customer_id): int?` (use Maxio customer ID from prior EnsureCustomerExists); `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum). No payment profile required per task spec. Wire names: `product_handle`, `product_id`, `customer_id`, `payment_collection_method`. |
| **Response envelope** | `MaxioAdvancedBilling.Models.SubscriptionResponse` — one field: `Subscription (subscription): Subscription?`. Key Subscription fields: `Id (id): int?` (Maxio subscription ID), `State (state): SubscriptionState?` (enum: "active", "trialing", "pending", etc.), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date), `ProductPriceInCents (product_price_in_cents): long?`, `Customer (customer): Customer?` (nested object; to access customer ID, use `subscription.Customer?.Id`), `ProductId (product_id): int?`, `CreatedAt (created_at): DateTimeOffset?`. Wire names: `id`, `state`, `current_period_ends_at`, `next_assessment_at`, `product_price_in_cents`, `customer`, `product_id`. |
| **Error case** | **Case A**: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — accessors: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback]. The `ErrorListResponse1.Errors` field is `IReadOnlyList<string>` with error messages. |
| **Pagination** | None. |
| **Source** | `map/operations/Subscriptions.md` |

---

### Operation 5: Read Subscription

| | Details |
|---|---------|
| **Controller & method** | `client.Subscriptions.ReadSubscription` |
| **Signature** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` |
| **Required params** | `subscriptionId` (int, positional). `include` (nullable query param, pass `null` to omit). |
| **Request model** | None (path + query params only). |
| **Response envelope** | `MaxioAdvancedBilling.Models.SubscriptionResponse` — same as CreateSubscription response. |
| **Error case** | **Case B**: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — check `StatusCode` for 404 (not found) or other errors. |
| **Pagination** | None. |
| **Source** | `map/operations/Subscriptions.md` |

---

### Operation 6: List Customer Subscriptions

| | Details |
|---|---------|
| **Controller & method** | `client.Customers.ListCustomerSubscriptions` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Required params** | `customerId` (int, path param). |
| **Request model** | None (path param only). |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` — each item has one field: `Subscription (subscription): Subscription?`. Same Subscription shape as above. |
| **Error case** | **Case B**: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — check `StatusCode`. |
| **Pagination** | None. |
| **Source** | `map/operations/Customers.md` |

---

## Models Summary: Enums and Key Types

### SubscriptionState enum (`MaxioAdvancedBilling.Models.Enums.SubscriptionState`)

Relevant values for this integration (all `StringEnum<T>`, use `.FromValue("wire")` or static members):

| C# Member | Wire Value | Meaning |
|-----------|-----------|---------|
| `Active` | `"active"` | Subscription is active and paid up-to-date. |
| `Trialing` | `"trialing"` | In trial period (if applicable). |
| `PastDue` | `"past_due"` | Payment overdue. |
| `Canceled` | `"canceled"` | Cancelled by customer or system. |
| `Expired` | `"expired"` | Subscription term ended. |
| `Pending` | `"pending"` | Awaiting activation. |
| `Suspended` | `"suspended"` | Temporarily suspended. |

**Source**: `map/models/enums.md`

### IntervalUnit enum (`MaxioAdvancedBilling.Models.Enums.IntervalUnit`)

| C# Member | Wire Value |
|-----------|-----------|
| `Day` | `"day"` |
| `Month` | `"month"` |

**Source**: `map/models/enums.md`

### CollectionMethod enum (`MaxioAdvancedBilling.Models.Enums.CollectionMethod`)

| C# Member | Wire Value |
|-----------|-----------|
| `Automatic` | `"automatic"` |
| `Remittance` | `"remittance"` |
| `Prepaid` | `"prepaid"` |
| `Invoice` | `"invoice"` |

**Source**: `map/models/enums.md`

---

## Client Construction & Authentication

| | Details |
|---|---------|
| **Namespace** | `MaxioAdvancedBilling` (root); `MaxioAdvancedBilling.Models` (DTOs); `MaxioAdvancedBilling.Models.Enums` (enums); `MaxioAdvancedBilling.Errors` (error types); `MaxioAdvancedBilling.Core.Authentication.Basic` (auth); `MaxioAdvancedBilling.Core.ErrorResponse` (RawError); `MaxioAdvancedBilling.Servers` (ServerEnvironment). |
| **Client class** | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` |
| **Options class** | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` |
| **Auth** | HTTP Basic — `new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` where username is your Maxio API key, password is the literal string `"x"`. |
| **Environments** | `ServerEnvironment.Us` (US-hosted, default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (EU-hosted) → `https://{site}.ebilling.maxio.com`. For this task, use **US** (sandbox at `cp-exp-3.chargify.com`). |
| **DI registration** | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = new BasicAuthCredentials { ... }; o.Environment = ServerEnvironment.Us; })`. The client wraps a long-lived `IHttpClientFactory` instance; do not create a new client per request. |
| **Server overrides** | To override the base URL or site subdomain (e.g., for local testing): `o.Server.Production.Us.BaseUrl = "https://custom-host";` or `o.Server.Production.Us.Site = "your-subdomain";`. The `ServerOptions` has two server groups: `Production` (default API) and `Ebb` (event ingestion). **Note**: There is no `Default` property on `ServerOptions` — use `Production` instead. |
| **Source** | `sdk-map.md` (Getting a client section) |

---

## Assumptions & Blockers

### Assumptions

1. **User reference as customer lookup key**: The plan uses eShopOnWeb's user ID (`context.User.FindFirst(IdentityModel.JwtClaimTypes.Subject)?.Value` or equivalent) as the Maxio customer `reference` field for idempotent lookup and creation. This assumes one user ID maps to one Maxio customer.

2. **No payment collection required**: Per task spec, the sandbox plans require no payment method; `PaymentCollectionMethod` defaults to `automatic` and no `payment_profile_id` is passed.

3. **Plans stable**: The task names plans by handle (`eshop-pro`, `basic-plan`) and assumes Maxio catalog is pre-seeded. If plan handles drift after re-seed, the product name or ID fallback should be considered.

4. **In-memory database scope**: Since eShopOnWeb uses `UseOnlyInMemoryDatabase=true`, the userId ↔ subscriptionId mapping survives only within one app run. Persistence of this mapping is the application's responsibility, not the SDK's.

5. **JWT-authenticated endpoints**: The three new PublicApi endpoints assume JWT bearer authentication is already in place; Maxio authentication is orthogonal (handled by DI + options).

### Blockers

None identified. All operations are available in the sandbox. However:

- **Plan IDs may drift**: If the sandbox is re-seeded, numeric product IDs change; validate against the live sandbox before deployment.
- **UNVERIFIED: Subscription state on fresh create**: The map documents that a subscription is created in a specific state, but whether it is immediately "active" or "pending" depends on the product configuration and payment collection method. Test against the live sandbox to confirm the expected state transitions.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. Each governs a critical step and carries defaults, worked examples, and gotchas the map does not:

| Skill | Governs | Why |
|-------|---------|-----|
| **dotnet-client-initialization** | Step 1: Client & DI setup | The `HttpClient` must be long-lived via `IHttpClientFactory`, not rebuilt per request. SDK client may be transient. |
| **dotnet-authentication** | Step 1: Auth credentials | Credentials must be set before client construction or in the DI callback; API key is read from configuration, not hardcoded. |
| **dotnet-calling-endpoints** | Steps 2–6: All operation calls | Many operations accept nullable params that have no C# default; use named arguments to avoid mis-binding in positional calls. |
| **dotnet-models** | Steps 2–6: Request/response parsing | Enums are `StringEnum<T>`, not C# enums; construct via `Type.FromValue("wire")` or static members. Response envelopes wrap payloads in one field; read one level down (e.g., `response.Product` not `response`). |
| **dotnet-error-handling** | Steps 2–6: Exception handling | **CRITICAL**: **Two `JsonException` hazards**: (1) drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** `SdkException` — a catch-ladder for `SdkException` only lets it escape; (2) **non-2xx** body that doesn't match the typed error shape throws `JsonException` *while constructing the error object*, so `JsonException` **replaces** `SdkException` and the HTTP status is lost — a boundary that maps every `JsonException` to a 5xx then reports it as an outage lets a caller retry something that can never succeed. Map `JsonException` carefully: drifted 2xx → likely app bug (error-log + fail-open or fail-closed per app policy); malformed 4xx/5xx → provider issue (error-log + return the HTTP status the caller can retry on). **MUST load before writing the error boundary.** |
| **dotnet-configuration-resilience** | Step 1: Client configuration (retry, timeout, base URL) | `HttpMethodsToRetry` gates only **status** trigger, so a `503` on a `POST` is not resent — but a **transport failure** (`HttpRequestException`) is retried on **every** verb, `POST` included, even if non-idempotent. `MaxRetries = 0` is rejected; floor is 1. `Timeout` is per-attempt, not total. No built-in logging hook. |
| **dotnet-testing** | Step 7+ (out of scope): Stub the SDK | The `HttpClient` constructor argument is the seam; match existing test framework. |

---

### Critical JsonException Hazards (restate here for visibility)

**A drifted or malformed 2xx body** (e.g., missing `required` field) triggers `System.Text.Json.JsonException` during deserialization of the response model. This is **not** an `SdkException` — the HTTP status is `200`, and the SDK is constructing the response object when it fails. A catch-ladder that only catches `SdkException` and re-throws others lets this `JsonException` escape the integration boundary unhandled, and your app crashes. Your error boundary must catch `JsonException` separately, log the provider data-shape mismatch, and either fail-open (assume a sensible default) or fail-closed (reject the operation) per your app policy.

**A non-2xx body that doesn't match the operation's typed error shape** (e.g., the provider sends a 422 but the JSON doesn't match `CreateSubscriptionError`) throws `JsonException` **while the SDK is constructing the error type inside the `catch` block that handles the non-2xx status**. This means the `JsonException` escapes *instead of* the `SdkException` — the HTTP status is destroyed, and your error handler never sees it. A boundary that maps every `JsonException` to a generic 5xx then reports it as an outage will cause your app to retry a 4xx (which is never retryable) on the assumption it's a transient 5xx. Your error boundary must catch `JsonException` separately, preserve the HTTP status by other means (parse the response body as a string first, log it), and either map it to a provider-error category or return a deterministic 502/503 that the caller knows not to retry naively.

**MUST load `dotnet-error-handling`** before writing the boundary.

