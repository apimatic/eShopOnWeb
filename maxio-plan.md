# Maxio Subscription Billing Integration — eShopOnWeb

## Scope & Sequence

| Step | Operations in scope | Dependencies |
|------|---------------------|--------------|
| 1. Client registration & DI | Setup SDK client with configuration | Configuration (`Maxio:*` keys) |
| 2. Authentication wiring | Set Basic auth credentials (API key) | `Maxio:ApiKey`, `Maxio:Subdomain` |
| 3. Customer sync (idempotent) | `ReadCustomerByReference`, `CreateCustomer` | eShopOnWeb user identity |
| 4. List subscription plans | `ListProducts` filtered by family handle | `Maxio:ProductFamilyHandle`, cached in memory or DB |
| 5. Subscribe to plan (hero flow) | `CreateSubscription` with customer + product + reference | Customer ID (from step 3), product handle, user reference |
| 6. List user subscriptions | `ListSubscriptions` filtered by customer | Customer ID, internal correlation |

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Step | Controller.Method | Signature | Request model (fields) | Response envelope (inner fields) | Error case | Pagination | Source |
|------|-------------------|-----------|------------------------|----------------------------------|-----------|-----------|--------|
| 2 | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | None; `reference` is query param | `CustomerResponse` with field `Customer (customer): Customer !req` | Case B: `SdkException<RawError>` w/ `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Customers.md` |
| 2 | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req` containing: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, + optional: `CcEmails`, `Organization`, `Reference (reference): string?`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | `CustomerResponse` with field `Customer (customer): Customer !req` | Case A: `SdkException<CreateCustomerError>` w/ `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] + fallback `TryGetRawError(out RawError)` | None | `operations/Customers.md` |
| 3 | `Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | 8 query params (all nullable, pass `null` to skip): `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include`; page/perPage default to 1/20 | `IReadOnlyList<ProductResponse>` where each wraps `Product (product): Product !req` field | Case B: `SdkException<RawError>` w/ `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | Manual: `page`, `perPage` | `operations/Products.md` |
| 4 | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req` containing: `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (one or both), optional: `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?` (use for new-customer inline), `Reference (reference): string?` (your own idempotent ref), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, + many optional fields for trial, billing, components, etc. | `SubscriptionResponse` with field `Subscription (subscription): Subscription !req` | Case A: `SdkException<CreateSubscriptionError>` w/ `TryGetErrorListResponse1(out ErrorListResponse1)` [422] + fallback `TryGetRawError(out RawError)` | None | `operations/Subscriptions.md` |
| 5 | `Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | 14 query params (all nullable): `state`, `product`, `productPricePointId`, `coupon`, `couponCode`, `dateField`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `metadata`, `direction`, `sort`, `include`; page/perPage default to 1/20 | Query params only; no body | `IReadOnlyList<SubscriptionResponse>` where each wraps `Subscription (subscription): Subscription !req` field | Case B: `SdkException<RawError>` w/ `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | Manual: `page`, `perPage` | `operations/Subscriptions.md` |

### Key Model Fields (extracted from map)

**CreateCustomer (wire ← C#):**
- `first_name` ← `FirstName` (!req)
- `last_name` ← `LastName` (!req)
- `email` ← `Email` (!req)
- `reference` ← `Reference` (optional; used for idempotent lookup)
- `cc_emails`, `organization`, `address`, `address_2`, `city`, `state`, `zip`, `country`, `phone`, `locale`, `vat_number`, `tax_exempt`, `tax_exempt_reason`, `parent_id`, `salesforce_id` (all optional)

**CreateSubscription (wire ← C#) — fields in scope:**
- `product_handle` ← `ProductHandle` (optional; wire matches product's `api_handle`)
- `product_id` ← `ProductId` (optional; alternative to handle)
- `customer_id` ← `CustomerId` (optional; existing customer's Maxio ID)
- `customer_reference` ← `CustomerReference` (optional; your app's customer key)
- `reference` ← `Reference` (optional; your own subscription idempotent key)
- `payment_collection_method` ← `PaymentCollectionMethod` (optional; enum `CollectionMethod`)
- Omitted in this scope: trial fields, coupon codes, components, prepaid config, dunning settings, etc. (Notes field says payment not required for seeded plans)

**Product (response fields in scope):**
- `id (id): int?`
- `name (name): string?`
- `handle (api_handle): string?`
- `price_in_cents (price_in_cents): long?`
- `interval (interval): int?`
- `interval_unit (interval_unit): IntervalUnit?`
- `description (description): string?`

**Subscription (response fields in scope):**
- `id (id): int?`
- `state (state): SubscriptionState?` (enum; see enums table below)
- `customer_id (customer_id): int?`
- `product_handle (product_handle): string?`
- `product_id (product_id): int?`
- `current_period_ends_at (current_period_ends_at): DateTimeOffset?`
- `next_billing_at (next_billing_at): DateTimeOffset?`
- `activated_at (activated_at): DateTimeOffset?`
- `created_at (created_at): DateTimeOffset?`
- `canceled_at (canceled_at): DateTimeOffset?`

**Customer (response fields in scope):**
- `id (id): int?`
- `first_name (first_name): string?`
- `last_name (last_name): string?`
- `email (email): string?`
- `reference (reference): string?` (your app's customer key, if set)
- `created_at (created_at): DateTimeOffset?`

### Enums

| Enum | Namespace | Members | Wire values | Usage |
|------|-----------|---------|-------------|-------|
| `SubscriptionStateFilter` | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` | `active`, `canceled`, `expired`, `expired_cards`, `on_hold`, `past_due`, `pending_cancellation`, `pending_renewal`, `suspended`, `trial_ended`, `trialing`, `unpaid` | Query filter for `ListSubscriptions` (pass `null` to list all states) |
| `SubscriptionDateField` | `MaxioAdvancedBilling.Models.Enums` | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` | `current_period_ends_at`, `current_period_starts_at`, `created_at`, `activated_at`, `canceled_at`, `expires_at`, `trial_started_at`, `trial_ended_at`, `updated_at` | Query filter for `ListSubscriptions` (date field to filter on) |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic`, `Remittance`, `Prepaid`, `Invoice` | `automatic`, `remittance`, `prepaid`, `invoice` | Optional on subscription create; affects payment collection behavior |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day`, `Month`, `Year` | `day`, `month`, `year` | Product billing interval unit (read from products) |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | (Value list not extracted; check source) | — | Subscription state in response (see records-3 or source `Models/Enums/SubscriptionState.cs`) |

### Client Construction & Auth

**Root namespace**: `MaxioAdvancedBilling`

**Client class**: `MaxioAdvancedBillingClient`

**Client DI**: `ServiceCollectionExtensions.AddMaxioAdvancedBillingClient()`

**Auth**: HTTP Basic
- Username = API key (from `Maxio:ApiKey` config)
- Password = literal `"x"` (required by Maxio API)
- Namespace: `MaxioAdvancedBilling.Core.Authentication.Basic` (type: `BasicAuthCredentials`)

**Server environment** (namespace: `MaxioAdvancedBilling.Servers`):
- `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`
- Base URL can be overridden via `options.Server.Production.Us.BaseUrl`

**Configuration keys** (binding from appsettings via `Maxio:` section):
- `Maxio:ApiKey` — your Maxio/Chargify API key (→ auth username)
- `Maxio:Subdomain` — site subdomain (→ `{site}` in base URL template)
- `Maxio:ProductFamilyHandle` — product family handle (`eshop-subscribe`) for filtering plans
- `Maxio:BaseUrl` (optional override) — for mock/dev hosts

**Retries**: Configured via `RetryOptions` (namespace: `MaxioAdvancedBilling.Core.Configuration`); default floor is 1 retry, `MaxRetries=0` is rejected.

---

## Trap Notes

⚠ **Step 1 (client registration)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 (authentication)** — set credentials **before** constructing the client or in the DI callback; load the key from configuration (`Maxio:ApiKey`), never hardcode. Username = key, Password = literal `"x"`. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 3–5 (calling endpoints)** — call list/search ops with named arguments; many optional params have no C# default and mis-bind in positional calls. Every operation is throw-only (no Result variants). **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 3–5 (models)** — unmodeled JSON fields are dropped on deserialize; enums are `StringEnum<T>` not C# enums (construct via `Type.FromValue("wire")` or static members); unions are built with factory methods and read via `TryGet…` (no `new`). **MUST load `dotnet-models`** for any non-string/non-number field.

⚠ **Step 3–5 (error handling)** — `ReadCustomerByReference`, `ListProducts`, `ListSubscriptions` are Case B (raw `SdkException<RawError>`, no typed accessors); `CreateCustomer`, `CreateSubscription` are Case A (typed `SdkException<{Op}Error>` with `TryGet…` accessors). A drifted or malformed 2xx body (missing `required` field) surfaces as `JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape; a non-2xx body that doesn't match the typed error shape throws `JsonException` *while constructing the error object*, replacing the `SdkException` and destroying the HTTP status — the boundary must map every `JsonException` to a 5xx and a deterministic rejection as an outage, so a caller retrying 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ **Design decision** — whether a failed write (`CreateSubscription`, `CreateCustomer`) can be re-sent (idempotency via `reference` field): retries are not request-idempotent by default on non-GET verbs (see `dotnet-configuration-resilience` for `HttpMethodsToRetry` and the floor of 1 retry). A `CreateSubscription` with a `reference` value ensures Maxio rejects the duplicate if retried; a `CreateCustomer` with a `reference` value is similarly gated. **MUST load `dotnet-configuration-resilience`** to understand retry boundaries on `POST`.

---

## Implementation Roadmap

### 1. Setup: Configuration & Secrets

- Add user-secrets to PublicApi.csproj (use existing UserSecretsId: `4b45ed6d-08b7-413b-9c38-74ed4a02116d`)
- Map environment variables to user-secrets:
  - `MAXIO_API_KEY` → `Maxio:ApiKey`
  - `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
  - `MAXIO_ENVIRONMENT` → `Maxio:Environment` (optional; defaults to `Us`)
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`
- Bind configuration section: `services.Configure<MaxioConfiguration>(config.GetSection("Maxio"))` or direct `IConfiguration.GetSection` at call site

### 2. Install & Client Registration

- Add NuGet package: `dotnet add package AsadAli.AdvancedBilling.Sdk`
- Register client in DI: `services.AddMaxioAdvancedBillingClient(...)` with auth callback
- Or construct manually: `new MaxioAdvancedBillingClient(httpClient, options)` with `BasicAuthCredentials`

### 3. Feature: Get or Create Customer (Idempotent)

- Input: eShopOnWeb user (ID, email, name)
- Derive `reference` = user ID (string) or email hash — used as idempotent key
- **First attempt**: `ReadCustomerByReference(reference)` — if found, use returned Maxio customer ID
- **On 404/not found**: `CreateCustomer` with body wrapping `CreateCustomer` record; required fields: `FirstName`, `LastName`, `Email`; optional: `Reference` (set to user ID or email)
- **Error handling**: Catch `SdkException<RawError>` on read (Case B); catch `SdkException<CreateCustomerError>` on create (Case A) w/ `TryGetCustomerErrorResponse1` for 422
- **Output**: Maxio customer ID (stored in user profile or session)

### 4. Feature: List Available Plans

- Call `ListProducts(null, filter: new ListProductsFilter { ... }, page: 1, perPage: 20, ct: ct)`
- Filter by product family: Extract product family ID from Maxio, or pass handle via filter (check map for filter structure)
- **UNVERIFIED**: The `ListProductsFilter` type and whether it supports family handle filtering; may need to fetch all products and filter in-memory by family if API doesn't support family filter directly
- Map response: `ProductResponse.Product` → presentation DTO (plan name, price, interval, handle)
- Optionally cache in memory (e.g., `IMemoryCache`) with reasonable TTL (e.g., 1 hour)

### 5. Feature: Subscribe to Plan

- Input: eShopOnWeb user (reference), selected plan (handle or ID), optional coupon
- Call `CreateSubscription(body: new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = planHandle, CustomerReference = userReference, Reference = $"{userId}-{timestamp}" (idempotent), PaymentCollectionMethod = CollectionMethod.Prepaid or as configured }, ct: ct)`
- **Payment**: Per seeded plans, no payment method is required; subscription is immediately active if payment collection is `Prepaid` or payment is accepted
- **Error handling**: Catch `SdkException<CreateSubscriptionError>` (Case A) w/ `TryGetErrorListResponse1` for 422
- **Output**: `SubscriptionResponse.Subscription` with `Id`, `State`, `NextBillingAt`, `ActivatedAt` — map to response DTO

### 6. Feature: List User Subscriptions

- Input: Maxio customer ID (from step 3)
- Call `ListSubscriptions(state: null, product: null, ..., page: 1, perPage: 20, ct: ct)` (pass nulls for all unneeded filters; caller must filter by customer ID in-app if `ListSubscriptions` doesn't expose a customer_id param) — **UNVERIFIED**: Check if `ListSubscriptions` has a customer filter; if not, fetch all and filter in-memory, or use the customer ID in a separate call
- **Alternative**: Use `Customers.ListCustomerSubscriptions(customerId)` if it exists (check map for this operation under Customers controller)
- Map response: `SubscriptionResponse.Subscription` → presentation DTO (plan name, state, next billing date, activated date)
- **Error handling**: Catch `SdkException<RawError>` (Case B)

### 7. Error Boundary & Logging

- Wrap all Maxio calls in a centralized service/middleware that catches:
  - `SdkException<T>` for known errors → map to HTTP status + user message
  - `JsonException` → log, respond 5xx (see `dotnet-error-handling`)
  - `HttpRequestException` → log, respond 5xx (network failures)
  - `OperationCanceledException` → respond 408
- Log all calls (request/response) for debugging (check `dotnet-configuration-resilience` for logging hooks)

### 8. Endpoints (PublicApi)

**GET `/api/subscription-plans`**
- No auth or basic auth check
- Calls step 4 (List Products)
- Returns: `PlansResponse { Plans: IEnumerable<PlanDto> }`

**POST `/api/subscriptions`**
- Requires JWT auth (extract user from token)
- Input: `{ planHandle: string }`
- Calls step 3 (customer sync) + step 5 (subscribe)
- Returns: `SubscriptionResponse { Id: int, State: string, NextBillingAt: DateTime, ActivatedAt: DateTime }`

**GET `/api/my-subscriptions`**
- Requires JWT auth
- Calls step 3 (customer sync) + step 6 (list subscriptions)
- Returns: `UserSubscriptionsResponse { Subscriptions: IEnumerable<SubscriptionDto> }`

### 9. Data Models (Application Layer)

Create DTOs for responses (separate from Maxio SDK models):
- `PlanDto` — plan handle, name, price (formatted), interval
- `SubscriptionDto` — subscription ID, state, activated date, next billing date
- `PlanListResponse`, `SubscriptionResponse`, `UserSubscriptionsResponse` — wrap the above + correlation ID (BaseResponse pattern)

### 10. Testing & Verification

- Use mock `HttpClient` (test seam per `dotnet-testing`) to stub Maxio responses
- Verify idempotent create (same reference, repeated calls)
- Verify error paths (malformed requests, 404 on customer, 422 on duplicate subscription)
- Manual smoke test: call each endpoint against sandbox (credentials from user-secrets)

---

## REQUIRED READING

**Load these companion skills BEFORE implementation starts.** The sheet deliberately does not carry their contents; these skills carry defaults, worked examples, and gotchas the map cannot show.

| Skill | Step(s) it governs |
|-------|-------------------|
| `maxio-sdk:dotnet-client-initialization` | Step 2 (client & DI setup, `HttpClient` lifecycle) |
| `maxio-sdk:dotnet-authentication` | Step 2 (Basic auth, credential wiring, config keys) |
| `maxio-sdk:dotnet-calling-endpoints` | Steps 3–6 (named arguments, optional params, call patterns) |
| `maxio-sdk:dotnet-models` | Steps 3–6 (enums, unions, required fields, JSON wire names) |
| `maxio-sdk:dotnet-error-handling` | Steps 3–6 + 7 (Case A/B distinction, `JsonException` handling, boundary design) |
| `maxio-sdk:dotnet-configuration-resilience` | Steps 1–2, 7 (retries, timeouts, retry boundaries on `POST`, logging) |
| `maxio-sdk:dotnet-testing` | Step 10 (mocking `HttpClient`, test patterns) |

**Both of these hazard rows belong in the FIRST implementation pass** — the boundary is written early:
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

### Assumptions

1. **Customer reference strategy**: eShopOnWeb user IDs (or email) will be used as the `reference` field for idempotent customer lookups. If user IDs are not globally stable or change, this strategy must be reconsidered.

2. **Plan caching**: The implementation assumes plans are cached (in-memory or database) and refreshed on a schedule (e.g., 1 hour), rather than fetched on every request. Live product changes from the Maxio UI may have a cache-lag window.

3. **Payment collection**: Seeded plans use `payment_collection_method=prepaid` or no payment required. The implementation does not support credit-card collection at signup and assumes immediate subscription activation on successful `CreateSubscription`.

4. **Error messaging**: HTTP 422 errors from Maxio (e.g., invalid product, duplicate subscription) will be parsed from `ErrorListResponse1` and returned to the client as-is; if format is unstructured, a generic fallback message will be used.

5. **Concurrency**: No assumptions about concurrent subscription requests from the same user (race condition on idempotent create if two requests arrive in parallel with different references). Maxio's `reference` uniqueness is within a site, so repeated calls with the same reference are rejected; staggered calls with different references will create multiple subscriptions (application must gate this).

### Blockers

**None identified at planning stage.** All operations mapped, all required namespaces identified, all error cases grounded. The one item flagged as **UNVERIFIED** (product family filtering in `ListProducts` and whether `ListSubscriptions` supports a customer_id param) is a contract detail that can be resolved via map lookup or source inspection before implementation, not a blocker to planning.

---

**Generated with Maxio SDK .NET Helper Agent**
