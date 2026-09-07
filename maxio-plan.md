# Maxio Subscription Billing Integration — eShopOnWeb

## Scope & Implementation Sequence

| Step | Operations | Notes |
|------|-----------|-------|
| 1 | Client & DI registration | Register `MaxioAdvancedBillingClient` with Basic auth (API key + "x") |
| 2 | Configuration from environment | Read `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional) |
| 3 | `GET /api/subscription-plans` endpoint | Call `ListProductsForProductFamily` with the configured product family handle; return list of plans (ID, name, price) |
| 4 | `POST /api/subscriptions` endpoint | Look up customer by reference (JWT `sub`); create if not found; create subscription with product handle; return subscription ID |
| 5 | `GET /api/my-subscriptions` endpoint | Look up customer by reference; call `ListCustomerSubscriptions`; filter by active state; return user's subscriptions |
| 6 | In-memory user subscription map | Store userId ↔ Maxio subscription ID mapping in memory for this run only |

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 1. Create Customer (Idempotent)

**Read → Create pattern.** First attempt `ReadCustomerByReference(reference)` using the authenticated user's ID (JWT `sub` claim). If 404, proceed to `CreateCustomer`. Store mapping userId ↔ Maxio customerId in memory.

| Aspect | Value | Source |
|--------|-------|--------|
| **Operation (if not found)** | `client.Customers.CreateCustomer(...)` | `Api/Customers.cs` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | Signature: param named `body`, nullable, no default → **must pass explicitly** |
| **Request model** | `MaxioAdvancedBilling.Models.CreateCustomerRequest` with fields: `Customer (customer): Customer !req` | `Models/CreateCustomerRequest.cs` |
| **Request inner** | `MaxioAdvancedBilling.Models.Customer` — field names below (wire name in parens, `!req` = required): `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, …other optional fields… | `Models/Customer.cs` |
| **Response envelope** | `MaxioAdvancedBilling.Models.CustomerResponse` with exactly one field: `Customer (customer): Customer !req` — **read from the `.Customer` property** | `Models/CustomerResponse.cs` |
| **Response inner** | `MaxioAdvancedBilling.Models.Customer` — fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, …other fields… | `Models/Customer.cs` |
| **Error Case** | **Case A (typed)** — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` | `Api/Customers.cs` source; error class `Errors/CreateCustomerError.cs` |
| **Error accessors** | `.Error.TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | `Errors/CreateCustomerError.cs` |
| **Pagination** | None | — |

---

### 2. Read Customer By Reference (Lookup for Idempotence)

| Aspect | Value | Source |
|--------|-------|--------|
| **Operation** | `client.Customers.ReadCustomerByReference(...)` | `Api/Customers.cs` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Signature: param named `reference`, string, no default → **must pass explicitly** |
| **Query params** | `reference` ← `reference` (wire param name is same as C# param name) | Query string will be `?reference={value}` |
| **Response envelope** | `MaxioAdvancedBilling.Models.CustomerResponse` with field: `Customer (customer): Customer !req` — **read from the `.Customer` property** | `Models/CustomerResponse.cs` |
| **Response inner** | `MaxioAdvancedBilling.Models.Customer` (same shape as above) | `Models/Customer.cs` |
| **Error Case** | **Case B (raw)** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` | `Api/Customers.cs` source |
| **Error accessors** | `.Error.StatusCode: System.Net.HttpStatusCode` (404 if not found) · `.Error.ReadAsString(): string` · `.Error.ReadAsJson<T>(): T?` · `.Error.ReadAsBytes(): ReadOnlyMemory<byte>` | `Core/ErrorResponse/RawError.cs` |
| **Pagination** | None | — |

---

### 3. Create Subscription

| Aspect | Value | Source |
|--------|-------|--------|
| **Operation** | `client.Subscriptions.CreateSubscription(...)` | `Api/Subscriptions.cs` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | Signature: param named `body`, nullable, no default → **must pass explicitly** |
| **Request model** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` with fields: `Subscription (subscription): CreateSubscription !req` | `Models/CreateSubscriptionRequest.cs` |
| **Request inner** | `MaxioAdvancedBilling.Models.CreateSubscription` — field names (wire name in parens, only those used by this integration): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerAttributes (customer_attributes): CustomerAttributes?` (optional, for inline creation), `PaymentProfileId (payment_profile_id): int?` (optional, not required per requirements), `Reference (reference): string?` (optional), `CouponCode (coupon_code): string?` (optional), …other optional fields… | `Models/CreateSubscription.cs` |
| **Response envelope** | `MaxioAdvancedBilling.Models.SubscriptionResponse` with exactly one field: `Subscription (subscription): Subscription !req` — **read from the `.Subscription` property** | `Models/SubscriptionResponse.cs` |
| **Response inner** | `MaxioAdvancedBilling.Models.Subscription` — fields (sample): `Id (id): int?`, `State (state): SubscriptionState?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Reference (reference): string?`, …many other fields… | `Models/Subscription.cs` |
| **Error Case** | **Case A (typed)** — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` | `Api/Subscriptions.cs` source; error class `Errors/CreateSubscriptionError.cs` |
| **Error accessors** | `.Error.TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | `Errors/CreateSubscriptionError.cs` |
| **Pagination** | None | — |

---

### 4. List Products For Product Family (Available Plans)

| Aspect | Value | Source |
|--------|-------|--------|
| **Operation** | `client.ProductFamilies.ListProductsForProductFamily(...)` | `Api/ProductFamilies.cs` |
| **Signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Signature: `productFamilyId` (string, no default → **must pass explicitly**); params 2–9 nullable, no default → **must pass explicitly** (pass `null` to skip); params `page` and `perPage` have defaults (1 and 20) |
| **Query params** | `page` ← `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `filter` ← `filter`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `include_archived` ← `includeArchived`, `include` ← `include` | Wire names differ from C# names (e.g., `perPage` → `per_page`) |
| **Response envelope** | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — **no wrapper object, directly a list** | `Api/ProductFamilies.cs` return type |
| **Response items** | Each `MaxioAdvancedBilling.Models.ProductResponse` has exactly one field: `Product (product): Product !req` — **read from `.Product` property** | `Models/ProductResponse.cs` |
| **Response inner** | `MaxioAdvancedBilling.Models.Product` — fields (sample): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, …many other fields… | `Models/Product.cs` |
| **Error Case** | **Case A (typed)** — `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` | `Api/ProductFamilies.cs` source |
| **Error accessors** | `.Error.TryGetString(out string)` [404] · `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | `Errors/ListProductsForProductFamilyError.cs` |
| **Pagination** | Manual via `page`+`perPage` query params | For `GET /api/subscription-plans`, call once with defaults or first page; loop if needed |

---

### 5. List Customer Subscriptions

| Aspect | Value | Source |
|--------|-------|--------|
| **Operation** | `client.Customers.ListCustomerSubscriptions(...)` | `Api/Customers.cs` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Signature: param named `customerId`, int, no default → **must pass explicitly** |
| **Response envelope** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — **no wrapper object, directly a list** | `Api/Customers.cs` return type |
| **Response items** | Each `MaxioAdvancedBilling.Models.SubscriptionResponse` has exactly one field: `Subscription (subscription): Subscription !req` — **read from `.Subscription` property** | `Models/SubscriptionResponse.cs` |
| **Response inner** | `MaxioAdvancedBilling.Models.Subscription` (same shape as in CreateSubscription above) | `Models/Subscription.cs` |
| **Error Case** | **Case B (raw)** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` | `Api/Customers.cs` source |
| **Error accessors** | `.Error.StatusCode: System.Net.HttpStatusCode` · `.Error.ReadAsString(): string` · `.Error.ReadAsJson<T>(): T?` · `.Error.ReadAsBytes(): ReadOnlyMemory<byte>` | `Core/ErrorResponse/RawError.cs` |
| **Pagination** | None documented; operation returns list directly | Assume full list is returned; if large, check for pagination headers or docs |

---

## Enums Used

| Enum | Values (wire name in parens) | Usage | Source |
|------|---------|-------|--------|
| `SubscriptionState` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Unpaid (unpaid)`, `Paused (paused)`, `AwaitingSignup (awaiting_signup)`, `Assessing (assessing)`, `Trialing (trialing)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `Pending (pending)`, `FailedToCreate (failed_to_create)` | To filter active subscriptions in `GET /api/my-subscriptions`; construct via `SubscriptionState.Active` (static member, not `SubscriptionState.FromValue("active")`) | `Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | Billing interval unit for products returned by `ListProductsForProductFamily` | `Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | Payment collection method when creating subscription (if needed); defaults per product | `Models/Enums/CollectionMethod.cs` |

All enums are `StringEnum<T>` records (NOT C# enums). Construct via static member (e.g. `SubscriptionState.Active`) or `SubscriptionState.FromValue("active")`. Namespace: `MaxioAdvancedBilling.Models.Enums`.

---

## Client Construction & Configuration

| Aspect | Details | Source |
|--------|---------|--------|
| **Client class** | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| **Options class** | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| **Constructor** | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | Only constructor |
| **Auth** | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<api_key>", Password = "x" }` | HTTP Basic: username = Maxio API key, password = literal `"x"` |
| **Environment** | `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (or `.Eu`) | Default US; set from config if needed |
| **Base URL override** | `options.Server.Production.Us.BaseUrl = "https://...";` or `options.Server.Production.Us.Site = "subdomain"` | Optional; use `options.Server.Production.Us.Site = Maxio:Subdomain` to override the site |
| **DI registration** | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = new(...) { Username = key, Password = "x" }; })` | Alternative to direct construction |

**Namespaces for client construction:**
- `using MaxioAdvancedBilling;`
- `using MaxioAdvancedBilling.Core.Authentication.Basic;`
- `using MaxioAdvancedBilling.Servers;`

---

## Configuration Binding

| Setting Key | Environment Variable | Type | Example | Default | Notes |
|-------------|----------------------|------|---------|---------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | string | (your API key) | Required | Username for Basic auth |
| `Maxio:Subdomain` | `MAXIO_SUBDOMAIN` | string | `cp-exp-1` | `production` (implicit) | Sets the site subdomain in URLs |
| `Maxio:ProductFamilyHandle` | `MAXIO_PRODUCT_FAMILY_HANDLE` | string | `eshop-subscribe` | Required | Handle for product family containing seeded plans |
| `Maxio:BaseUrl` | `MAXIO_BASE_URL` | string | `https://custom.example.com` | (none) | Optional: full base URL override for sandbox/mocking |

Bind via `IConfiguration.GetSection("Maxio")` into an options class, or read individually.

---

## ⚠ Trap Notes

⚠ **Step 1 (client registration)** — `HttpClient` pipeline must be long-lived and shared (via `IHttpClientFactory`), not created per request. The SDK wraps it internally and expects a single, reusable HTTP handler. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (auth credentials)** — credentials must be set on `options.BasicAuth` BEFORE constructing the client (or in the DI callback). The API key goes in `Username`, not in a header or custom field. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 3 (calling operations)** — operation parameters are explicit; optional fields with no C# default must be passed as named arguments or explicitly as `null` to skip. Parameter names in the signature are literal C# identifiers (e.g., `ct:` for cancellation token, `perPage:` not `pageSize:`). **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (models)** — enums are `StringEnum<T>`, not C# enums; construct via static members (e.g. `SubscriptionState.Active`) or `.FromValue("wire_name")`. Response payloads wrap inner models in a single outer field (e.g., `ProductResponse.Product`); access via that property. Unmodeled JSON fields are dropped on deserialize; only mapped fields are available. **MUST load `dotnet-models`** before reading responses or building requests.

⚠ **Step 5 (error handling)** — not all operations throw the same error type. `CreateCustomer` is Case A (typed `CreateCustomerError` with `.TryGetCustomerErrorResponse1()` accessor); `ReadCustomerByReference` is Case B (raw `RawError` with `.StatusCode`, `.ReadAsString()`, etc.). Confirm each operation's error case in its map row. **Both rows below are critical:**
  - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
  - A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
  
  **MUST load `dotnet-error-handling`** before writing any error boundary.

⚠ **Step 6 (resilience)** — the SDK's `Timeout` property bounds **per-attempt**, not total time; `HttpMethodsToRetry` gates only the **status** trigger (`503` on a `POST` is not resent), but a **transport failure** (`HttpRequestException`) is retried on **every** verb including `POST`, so a non-idempotent write can execute more than once and no setting disables that. `MaxRetries = 0` is rejected at construction; the floor is 1. **MUST load `dotnet-configuration-resilience`** before tuning timeouts or retries.

⚠ **Step 7 (testing)** — the `HttpClient` constructor argument is the test seam; pass a mock or instrumented `HttpClient` to stub responses. Match the project's existing test framework. **MUST load `dotnet-testing`** before stubbing SDK calls.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; the skills carry the defaults, worked examples, and resolution steps your code will need.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 1: client construction, DI registration, `HttpClient` pipeline reuse, options wiring |
| `dotnet-authentication` | Step 2: Basic auth credentials, how to load from configuration, rotations |
| `dotnet-calling-endpoints` | Step 3: operation method signatures, named vs. positional args, async/await, cancellation-token usage |
| `dotnet-models` | Step 4: `StringEnum<T>` construction and wire-value mapping, response envelope unwrapping, union types |
| `dotnet-error-handling` | Step 5: error case detection (Case A vs. B), `.TryGet…()` accessors, JSON deserialization errors, exception mapping, boundary design |
| `dotnet-configuration-resilience` | Step 6: retry semantics (per-attempt vs. total), transport-error retry on all verbs, timeout configuration, exponential backoff |
| `dotnet-testing` | Step 7: mocking the `HttpClient`, test-fixture patterns, assertions on SDK calls |

---

## Assumptions & Blockers

| Item | Status | Note |
|------|--------|------|
| Sandbox plans already exist | Assumed | Seeded plans `eshop-pro` ($299/mo) and `basic-plan` ($29/mo) on product family `eshop-subscribe` must exist in sandbox site `cp-exp-1` before any test run |
| Payment method collection is not required | Assumed per scope | "payment method not required" — confirm the seeded products have `request_credit_card: false` or equivalent, or the integration must skip/mock payment collection |
| User ID is available from JWT | Assumed | The authenticated endpoint has access to `User.FindFirst(JwtClaimTypes.Subject)?.Value` or equivalent to get a unique user reference for idempotent customer creation |
| In-memory storage is acceptable for this run | Assumed | userId ↔ Maxio customerId mapping is lost on app restart; suitable for a single test/verification run, not production persistence |
| Environment variables are pre-populated | Blocker if not | `Maxio:ApiKey`, `Maxio:Subdomain` must be set in `appsettings.json` or the environment before the app starts; no fallback to hardcoded values |

---

## Endpoint Pseudo-Contracts (Application Layer)

| Endpoint | Method | Authenticated? | Request Body | Response (200) | Error Responses |
|----------|--------|----------------|---------------|---|---|
| `/api/subscription-plans` | `GET` | Yes (JWT) | None | `{ "plans": [ { "id": int, "name": string, "priceInCents": long, "interval": int, "intervalUnit": string }, … ] }` | 401 (no token), 500 (Maxio call failed) |
| `/api/subscriptions` | `POST` | Yes (JWT) | `{ "productHandle": string }` | `{ "subscriptionId": int, "customerId": int, "state": string, "activatedAt": string (ISO 8601) }` | 400 (invalid product), 401 (no token), 422 (Maxio validation), 500 (Maxio call failed) |
| `/api/my-subscriptions` | `GET` | Yes (JWT) | None | `{ "subscriptions": [ { "id": int, "state": string, "productId": int, "activatedAt": string (ISO 8601), "nextAssessmentAt": string (ISO 8601) }, … ] }` (filtered to state='active') | 401 (no token), 404 (customer not found), 500 (Maxio call failed) |

**Note:** These are application-facing contracts, not part of the Maxio SDK. The SDK returns SDK models (above table rows 1–5); the endpoint must serialize them to JSON for the client.

