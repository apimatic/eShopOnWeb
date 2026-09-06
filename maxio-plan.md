# Maxio Advanced Billing Integration — eShopOnWeb Subscription Plan

**Target:** Add recurring subscription billing to PublicApi with three new endpoints — GET /api/subscription-plans, POST /api/subscriptions, GET /api/my-subscriptions — backed by the Maxio Advanced Billing SDK.

---

## Scope & Sequence

1. **Client registration** — Create or obtain the `MaxioAdvancedBillingClient` via DI, configured with Basic auth (API key from `Maxio:ApiKey` or env var `MAXIO_API_KEY`).
2. **Endpoint: GET /api/subscription-plans** — Call `client.ProductFamilies.ListProductsForProductFamily()` to fetch products for the seeded family (`eshop-subscribe`); return product handle + name + price as JSON.
3. **Endpoint: POST /api/subscriptions** — Idempotently ensure a Maxio customer exists for the authenticated user (by reference = user ID); then call `client.Subscriptions.CreateSubscription()` with product handle and customer reference.
4. **Endpoint: GET /api/my-subscriptions** — Look up the Maxio customer by user reference; fetch subscriptions via `client.Customers.ListCustomerSubscriptions()`; return active subscriptions (filter by state).

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Operation | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|------|-----------|-----------|----------------|-------------------|------------|-----------|--------|
| 2 | ListProductsForProductFamily | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — Called with: `productFamilyId = "eshop-subscribe"` (product family handle from config `Maxio:ProductFamilyHandle`), all filters null, pagination defaults. | Query params only (no body). Wire names: `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `page`, `per_page`, `include_archived`, `include`. | `IReadOnlyList<ProductResponse>` → extract `response[].Product.Handle`, `response[].Product.Name`, `response[].Product.PriceInCents` (divide by 100 for USD). Envelope: field `Product (product): Product !req` per `ProductResponse`. | **Case A (typed)** — `SdkException<ListProductsForProductFamilyError>`. Accessors: `TryGetString(out string)` [404 — family not found]. Fallback: `TryGetRawError(out RawError)`. | Manual page/perPage; not used in this step (use defaults). | `map/operations/ProductFamilies.md` |
| 3a | ReadCustomerByReference | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — Called with: `reference = userId` (string, app's user ID). | Query param: `reference` (wire name). No body. | `CustomerResponse` → extract `response.Customer.Id` (Maxio customer ID, `int`). Envelope: field `Customer (customer): Customer !req`. | **Case B (raw)** — `SdkException<RawError>`. 404 means customer does not exist; treat as signal to create. Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. | None. | `map/operations/Customers.md` |
| 3a | CreateCustomer | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — Called with body (must pass explicitly, even if null-default in signature means optional passing). | **Request body wrapper** `CreateCustomerRequest { Customer (customer): CustomerAttributes !req }`. **Request fields to set** (all optional): `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` — wire names in parentheses. Set `reference = userId` (idempotency key); populate first/last name and email from app's user record. | `CustomerResponse` → extract `response.Customer.Id`. Envelope: field `Customer (customer): Customer !req`. | **Case A (typed)** — `SdkException<CreateCustomerError>`. Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]. Fallback: `TryGetRawError(out RawError)`. ErrorResponse1 shape: `Errors (errors): Errors?` (nested object). | None. | `map/operations/Customers.md` |
| 3b | CreateSubscription | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — Called with body (must pass explicitly). | **Request body wrapper** `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. **Required/used request fields**: `CustomerReference (customer_reference): string?` (wire: `customer_reference`, idempotency — set to userId), `ProductHandle (product_handle): string?` (wire: `product_handle`, the handle from step 2), `ProductId (product_id): int?` (alternative; use handle, not ID), `CustomerId (customer_id): int?` (alternative to reference; use reference). **Optional but relevant**: `DeferSignup (defer_signup): bool? = false` (default false; set to true if payment is not required per spec). **Omitted**: payment method fields (spec: "no payment method required" — do NOT set `PaymentProfileId` or payment attributes). Other notable optional fields: `Reference (reference): string?` (subscription reference for lookup), `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` (components not used here). Provide no payment, no card, no bank account in request. | `SubscriptionResponse` → extract `response.Subscription.Id`, `response.Subscription.State` (enum `SubscriptionState`, e.g. `Active`, `AwaitingSignup`), `response.Subscription.Product.Handle`. Envelope: field `Subscription (subscription): Subscription?` (nullable, but present on 2xx). | **Case A (typed)** — `SdkException<CreateSubscriptionError>`. Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422]. Fallback: `TryGetRawError(out RawError)`. ErrorListResponse1 shape: `Errors (errors): IReadOnlyList<string> !req` (list of error strings). | None (one subscription per call). | `map/operations/Subscriptions.md` |
| 4 | ListCustomerSubscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — Called with: `customerId` (Maxio customer ID from step 3a). | URL param: `customer_id` (path segment, not query). No body, no query params. | `IReadOnlyList<SubscriptionResponse>` → extract `response[].Subscription.Id`, `response[].Subscription.State`, `response[].Subscription.Product.Handle`, `response[].Subscription.Product.Name`. Filter by state (show only `Active`, `Assessing`, `Trialing`, `Pending`; hide `Canceled`, `Expired`, `SoftFailure`, `PastDue`, `Suspended`). Envelope: each element has field `Subscription (subscription): Subscription?`. | **Case B (raw)** — `SdkException<RawError>`. Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. | None (returns full list, no manual pagination needed). | `map/operations/Customers.md` |

### Enum Values Used

| Enum | Namespace | Values Referenced | Source |
|------|-----------|-------------------|--------|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Assessing (assessing)`, `Trialing (trialing)`, `Pending (pending)`, `AwaitingSignup (awaiting_signup)`, `Canceled (canceled)`, `Expired (expired)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`. Filter list endpoint to include only Active, Assessing, Trialing, Pending. | `map/models/enums.md` |

### Configuration & Client Setup

| Item | Type | Binding Key / Default | Notes | Source |
|------|------|----------------------|-------|--------|
| API Key | string | Env var `MAXIO_API_KEY` or config key `Maxio:ApiKey` | Read from environment or IConfiguration; used as `options.BasicAuth.Username`. **Never hardcode.** | YOUR CALL — not in the map |
| Site Subdomain | string | Env var `MAXIO_SITE_SUBDOMAIN` or config key `Maxio:Subdomain` | Used to construct base URL if `Maxio:BaseUrl` not set: `https://{subdomain}.chargify.com` (US) or `https://{subdomain}.ebilling.maxio.com` (EU). | YOUR CALL — not in the map |
| Environment | `ServerEnvironment` | Env var `MAXIO_ENVIRONMENT` (values: `US`, `EU`) or config key — default `US` | Determines which data center. `ServerEnvironment.Us` or `ServerEnvironment.Eu`. Maps to base URL template selection in the SDK. | `map/operations/sdk-map.md` Servers section |
| Product Family Handle | string | Config key `Maxio:ProductFamilyHandle` — default from spec is `eshop-subscribe` | The product family on the seeded Maxio sandbox site containing the plans. | YOUR CALL — not in the map |
| Base URL (optional override) | string | Config key `Maxio:BaseUrl` or null | If set, overrides the auto-constructed URL. Used for mocking or non-standard hosts. | `map/operations/sdk-map.md` Servers section |
| Password (Basic auth) | literal string | `"x"` (literal) | Basic auth password is always the literal string `"x"`. SDK documentation and map state this convention explicitly. | `map/operations/sdk-map.md` |
| HTTP Client | `System.Net.Http.HttpClient` | Injected via `IHttpClientFactory.CreateClient()` | **Do not** create a new `HttpClient` per request. Reuse via factory or long-lived field. | `map/operations/sdk-map.md` Client setup |

---

## Trap Notes (Hazards & Companion Skills)

⚠ **Step 1 (client registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. The `Timeout` property on `RetryOptions` is per-attempt, not per-call; if you need request-wide timeouts, they live on the `HttpClient` itself (via `HttpClient.Timeout`). There is no built-in setting to disable retries entirely (min is 1 retry). **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 1 (client registration)** — The SDK client constructor takes a `System.Net.Http.HttpClient` as its first argument. This `HttpClient` must be long-lived and reused (do **not** create a new one per request). Register it via `IHttpClientFactory` in DI and inject the factory into your controller/service. The `HttpClient` is the test seam; the SDK wrapper (`MaxioAdvancedBillingClient`) may be transient. **MUST load `dotnet-client-initialization`** before wiring DI and auth.

⚠ **Step 1 (authentication)** — Maxio uses HTTP Basic auth (username = API key, password = literal `"x"`). Credentials must be set on `MaxioAdvancedBillingClientOptions.BasicAuth` before constructing the client, or in the DI callback. Load the key from configuration or environment variables, **never hardcode**. The password is always the string `"x"`. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Steps 2–4 (calling endpoints)** — Many optional parameters on list/search operations have no C# default and will mis-bind if passed positionally. Use named arguments for optional params (e.g., `dateField: null`, `filter: null`). Omit optional params entirely rather than passing `null` when the default is acceptable. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ **Steps 2–4 (response envelopes & models)** — Response types **wrap their payload in exactly one field**: `ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`. Read the inner field, not the wrapper. Request wrappers work the same way: `CreateSubscriptionRequest.Subscription`, `CreateCustomerRequest.Customer`. Enums are `StringEnum<T>` or `IntEnum<T>` records, **not** C# enums — construct via static members (e.g., `SubscriptionState.Active`) or `Type.FromValue(wireValue)`. **MUST load `dotnet-models`** the moment a request/response field is not a plain string/number.

⚠ **Steps 3–4 (error handling, two critical cases)** — This SDK generates **no `{Operation}Result` / no-throw variants** — all operations throw on error.
- Step 3a: `CreateCustomer` is **Case A (typed)** — throws `SdkException<CreateCustomerError>`. If the customer already exists (422 Unprocessable Entity), `error.TryGetCustomerErrorResponse1(out var err422)` extracts the error details. On 404 or network errors, `error.TryGetRawError(out var raw)` provides the HTTP status and body.
- Step 3a: `ReadCustomerByReference` is **Case B (raw)** — throws `SdkException<RawError>`. On 404 (customer not found), `error.StatusCode == HttpStatusCode.NotFound`. Treat 404 as "customer doesn't exist" and proceed to create.
- Step 3b: `CreateSubscription` is **Case A (typed)** — throws `SdkException<CreateSubscriptionError>`. 422 errors are extracted via `error.TryGetErrorListResponse1(out var err422)`, which carries a list of error strings.
- **Critical: `System.Text.Json.JsonException` can arrive from two directions:** (1) A **2xx response with a malformed body** (missing required field) — the SDK's deserializer throws `JsonException`, **not** `SdkException`, so a catch-ladder that only catches `SdkException` **lets it escape**; (2) A **non-2xx response** with a body that doesn't match the typed error shape — the SDK throws `JsonException` **while constructing the `SdkException`**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is lost. Map all `JsonException` to a deterministic application error (e.g. 500) **at the boundary**, then check the HTTP status separately before routing to retry logic. **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ **Step 3a (idempotent customer creation, reference field)** — Set `CustomerAttributes.Reference` to the app's user ID (string). This is the idempotency key: Maxio's rule is "only one customer per reference value". When upserting, call `ReadCustomerByReference(reference)` first; if it throws 404, call `CreateCustomer(reference, …)`. If `CreateCustomer` throws 422, check the error — if it's "reference already exists", retry the read (race condition — another request created it in between). **MUST load `dotnet-error-handling`** for correct exception handling in this race-condition path.

⚠ **Step 4 (subscription list filtering by state)** — The `ListCustomerSubscriptions` endpoint returns all subscriptions for the customer. Maxio's `SubscriptionState` enum includes inactive states (`Canceled`, `Expired`, `SoftFailure`, etc.). **Filter in-memory** after receiving the list — the endpoint has no server-side state filter. Extract `response[].Subscription.State` and compare against the active set (Active, Assessing, Trialing, Pending); discard others. **MUST load `dotnet-models`** to construct/compare enum values.

---

## REQUIRED READING

Before implementation starts, load each of these skills **in order**. These skills carry defaults, worked examples, what you must still wire yourself, and traps the signature cannot show. The sheet deliberately does not carry their contents — you must read them to avoid build failures and runtime surprises.

| Skill | Applies To Step(s) | Notes |
|-------|-------------------|-------|
| `dotnet-client-initialization` | 1 (client registration, DI setup) | HttpClient reuse, lifecycle, test setup |
| `dotnet-authentication` | 1 (credentials & auth scheme) | Basic auth, credential rotation, per-environment config |
| `dotnet-calling-endpoints` | 2–4 (all operation calls) | Named args, optional params, async/await, cancellation |
| `dotnet-models` | 2–4 (request/response models, enums) | Immutable records, unions, enum construction & reading |
| `dotnet-error-handling` | 3–4 (try/catch, status routing) | Exception types, `TryGet…` accessors, JsonException from two directions, retry guards |
| `dotnet-configuration-resilience` | 1 (retry, timeout, base URL) | RetryOptions semantics, per-attempt timeout, redirect points |

**Critical: `System.Text.Json.JsonException` reaches the boundary from two directions:**
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

---

## Assumptions & Blockers

**Assumptions:**
- The authenticated user in PublicApi has an `Id` (string or convertible to string) that uniquely identifies them in the eShopOnWeb system and can be used as a Maxio customer reference.
- Maxio sandbox credentials (API key, subdomain) are available at deployment time via environment variables or configuration.
- The product family `eshop-subscribe` exists in the Maxio sandbox account with at least the two products specified (Pro Plan `eshop-pro`, Basic Plan `basic-plan`).
- "No payment method required" means the subscription can be created in `AwaitingSignup` state (or similar non-charged state). The Maxio SDK does **not** validate payment profiles in the request — if a subscription reaches `Active` state without a payment method, the provider rejects billing at that point (runtime, not API request time).

**Blockers:**
- None at planning time. The Maxio API contract is complete; all required operations are documented in the map.

---

## Files Generated

This plan only — no SDK source artifacts, no project edits at this stage.
