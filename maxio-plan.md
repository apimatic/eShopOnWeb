# Maxio Advanced Billing Integration Plan — eShopOnWeb

## Scope & Sequence

The integration adds three public endpoints to the eShopOnWeb PublicApi project, all JWT-authenticated:

1. **GET /api/subscription-plans** — List products (plans) available from Maxio (lazy-load, cache daily)
2. **POST /api/subscriptions** — Create a subscription for the authenticated user; ensure customer exists first (idempotent via eShop user ID reference)
3. **GET /api/my-subscriptions** — List subscriptions for the authenticated user (fetch by customer reference)

All operations read Maxio credentials from `IConfiguration` and pass the `CancellationToken` from the HTTP request to the SDK.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Operation | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param: `reference` (string) — eShop user ID, idempotent lookup | `CustomerResponse` → field `Customer (customer): Customer !req`; read `.Id`, `.FirstName`, `.LastName`, `.Email`, `.Reference` | Case B: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/Customers.md` |
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass explicitly (nullable but required) | `CreateCustomerRequest` → field `Customer (customer): CreateCustomer !req` with fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?` | `CustomerResponse` → field `Customer (customer): Customer !req` | Case A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| **ListProducts** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params nullable, no default → pass explicitly (pass `null` to skip); defaults: `page` = 1, `perPage` = 20 | Query params (wire ← C#): `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `page` ← `page`, `per_page` ← `perPage`, `include_archived` ← `includeArchived`, `include` ← `include`. For eShop: pass all search/date params as `null`; use pagination if needed | `IReadOnlyList<ProductResponse>` — each item is `ProductResponse` → field `Product (product): Product !req`; read `.Id`, `.Name`, `.Handle`, `.Description`, `.PriceInCents`, `.Interval`, `.IntervalUnit` | Case B: `SdkException<RawError>` | manual `page` + `perPage` | `operations/Products.md` |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass explicitly (nullable but required) | `CreateSubscriptionRequest` → field `Subscription (subscription): CreateSubscription !req` with required/optional fields: `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` | `SubscriptionResponse` → field `Subscription (subscription): Subscription?`; read `.Id`, `.State`, `.ProductPriceInCents`, `.CurrentPeriodStartedAt`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt`, `.Customer` (nested: `.Id`, `.Email`), `.Product` (nested: `.Name`, `.Handle`) | Case A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| **ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params nullable, no default → pass explicitly (pass `null` to skip); defaults: `page` = 1, `perPage` = 20 | Query params (wire ← C#): `state` ← `state`, `product` ← `product`, `product_price_point_id` ← `productPricePointId`, `coupon` ← `coupon`, `coupon_code` ← `couponCode`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `metadata` ← `metadata`, `direction` ← `direction`, `sort` ← `sort`, `include` ← `include`, `page` ← `page`, `per_page` ← `perPage`. **NOT available**: filter by customer — use Customers.ListCustomerSubscriptions instead. | `IReadOnlyList<SubscriptionResponse>` — each item is `SubscriptionResponse` → field `Subscription (subscription): Subscription?`; read `.Id`, `.State`, `.ProductPriceInCents`, `.CurrentPeriodStartedAt`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt`, `.Product` (nested: `.Name`, `.Handle`) | Case B: `SdkException<RawError>` | manual `page` + `perPage` | `operations/Subscriptions.md` |

### Enum Values Required

**Namespace:** `MaxioAdvancedBilling.Models.Enums`

#### SubscriptionStateFilter (StringEnum)
Wire values used to filter subscriptions by state. Construct via `SubscriptionStateFilter.Active`, `SubscriptionStateFilter.Canceled`, etc.
- `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`

**Source:** `models/enums.md`

#### SubscriptionDateField (StringEnum)
- `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_started_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)`

**Source:** `models/enums.md`

#### SubscriptionSort (StringEnum)
- `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)`

**Source:** `models/enums.md`

#### SortingDirection (StringEnum)
- `Asc (asc)`, `Desc (desc)`

**Source:** `models/enums.md`

#### SubscriptionListInclude (StringEnum)
- `SelfServicePageToken (self_service_page_token)`

**Source:** `models/enums.md`

#### IntervalUnit (StringEnum) — *in ProductResponse.Product*
- `Day (day)`, `Month (month)`

**Source:** `models/enums.md`

#### SubscriptionState (StringEnum) — *in SubscriptionResponse.Subscription*
- `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`

**Source:** `models/enums.md`

### Client Construction & Authentication

**Namespace:** `MaxioAdvancedBilling` (root)

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    // HTTP Basic: Username = API key, Password = literal "x"
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = configuration["Maxio:ApiKey"], 
        Password = "x" 
    },
    // Select environment: ServerEnvironment.Us (default) or ServerEnvironment.Eu
    Environment = serverEnvironment, // ServerEnvironment.Us for US hosting
};

// Constructor: MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Subdomain override** (for site-specific base URL rewriting, e.g. to a dev/mock host):
```csharp
options.Server.Production.Us.Site = configuration["Maxio:Subdomain"]; // defaults to subdomain part of host
// or override base URL entirely:
// options.Server.Production.Us.BaseUrl = "http://localhost:8080";
```

All API group operations (e.g., `client.Customers`, `client.Products`, `client.Subscriptions`) are properties on the client.

**Source:** `sdk-map.md`, `MaxioAdvancedBillingClient.cs`, `MaxioAdvancedBillingClientOptions.cs`

### Configuration Binding

Read from `IConfiguration`:
- `Maxio:ApiKey` (string) → `BasicAuthCredentials.Username`
- `Maxio:Subdomain` (string) → `options.Server.Production.Us.Site`
- `Maxio:Environment` (string, optional) → `"US"` or `"EU"` → `ServerEnvironment.Us` / `.Eu`
- `Maxio:BaseUrl` (string, optional) → override `options.Server.Production.Us.BaseUrl`
- `Maxio:ProductFamilyHandle` (string, optional) — application context only, not SDK configuration

**Source:** `YOUR CALL — not in the map` (application design choice)

---

## Trap Notes

⚠ **Client instantiation & DI** — The `HttpClient` passed to the SDK client constructor must be long-lived and reused via `IHttpClientFactory`, never rebuilt per request. The SDK client wrapper over it may be transient (scoped per operation) or singleton (shared). **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(…)` or DI registration.

⚠ **Authentication** — Maxio uses HTTP Basic auth with username = API key, password = literal `"x"`. Set credentials **before** constructing the client, loaded from configuration, never hardcoded. **MUST load `dotnet-authentication`** before wiring the auth credentials into `BasicAuthCredentials`.

⚠ **Calling operations** — All parameters named in the operation signature must be passed as named arguments (e.g., `reference:`, `ct:`). Many query params have no C# default and mis-bind in positional calls. **MUST load `dotnet-calling-endpoints`** before the first `client.Customers.…` / `client.Products.…` / `client.Subscriptions.…` call.

⚠ **Response envelope shape** — Responses wrap their payload: `CustomerResponse.Customer`, `ProductResponse.Product`, `SubscriptionResponse.Subscription`. The map rows show the envelope; reads descend one level into the inner field. No accessor is hidden; inspect the record's fields in the maps. **MUST load `dotnet-models`** before reading response fields that are unions, nested records, or enums.

⚠ **Error types & Case A vs Case B** — Operations are **throw-only**; no `Result`-style no-throw variants. Case A operations (`CreateCustomer`, `CreateSubscription`) throw `SdkException<{Operation}Error>` with typed `TryGet…` accessors per status code. Case B operations (`ListSubscriptions`, `ListProducts`, `ReadCustomerByReference`) throw `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` only. **Confirm the error case for each operation in its map row.** **MUST load `dotnet-error-handling`** before writing any `try`/`catch` boundary.

⚠ **JsonException from 2xx body drift** — A drifted or malformed 2xx response body (missing `required` field) surfaces as `JsonException` from deserialization, **not** as an `SdkException`. An SDK-exception-only catch ladder lets it escape the integration boundary; a caller retrying on 5xx retries a malformed payload that can never succeed. Map every `JsonException` to a 5xx, and a deterministic rejection becomes an outage.

⚠ **JsonException from non-2xx parse failure** — A non-2xx body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is lost. A boundary that re-throws `JsonException` as-is loses the status; one that maps every `JsonException` to a 5xx then retries it retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing the error boundary; both issues require special handling.

⚠ **Configuration & resilience** — Retry options (`MaxRetries`, `Timeout`, `StatusCodesToRetry`, `HttpMethodsToRetry`) are per-attempt, not per-whole-call. `HttpMethodsToRetry` gates only the **status** trigger (e.g., 503), so a 503 on a `POST` is not resent; but a **transport failure** (e.g., `HttpRequestException`) is retried on **every** verb, `POST` included — non-idempotent writes can execute more than once. `MaxRetries` floor is 1; `MaxRetries = 0` is rejected at construction time. `Timeout` is per-attempt, not total; there's no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before tuning retry/timeout settings or wiring the Polly pipeline.

---

## REQUIRED READING

Load these skills **before implementation starts**. The sheet deliberately omits their contents; they carry defaults, worked examples, and wiring details this brief cannot express.

| Skill | Step(s) Governed |
|---|---|
| `dotnet-client-initialization` | Client & DI setup (steps 1–2) |
| `dotnet-authentication` | Credentials & auth scheme (step 2) |
| `dotnet-calling-endpoints` | Endpoint calls & request body construction (steps 1–3) |
| `dotnet-models` | Request/response field access, enums, nested records (all steps) |
| `dotnet-error-handling` | Error boundary; Case A/B dispatch; JsonException handling (steps 1–3) |
| `dotnet-configuration-resilience` | Retry/timeout config, per-attempt semantics (client registration) |

**Both of these hazards belong in the FIRST error boundary you write, not a later revision:**

- A drifted or malformed **2xx** body (missing `required` field) surfaces as `JsonException` from deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## Assumptions & Blockers

### Assumptions

1. **Customer reference** — eShop user ID (e.g., application user's `Id` or a custom field) is unique and used as the `reference` field on Maxio customers to enable idempotent lookups (`ReadCustomerByReference`). The eShop caller provides this identifier; the integration stores it or derives it from the authenticated user context.

2. **No payment profile** — Subscriptions are created **without** an upfront payment method; Maxio customer and subscription exist in a state ready to accept future payment (no `require_credit_card`, no dunning). This is a sandbox/trial-use assumption; production may require payment upfront.

3. **No trial, no setup fee** — Products are not marked with trial periods or setup fees in the sandbox (Maxio plan configuration is read-only from the SDK, not created by this integration).

4. **Product/plan discovery** — The integration lazily loads and caches products from `ListProducts` on GET /api/subscription-plans. Cache lifetime and staleness strategy are **`YOUR CALL — not in the map`** (application design; consider daily cache + on-demand invalidation endpoint).

5. **Metered components / usage tracking** — The user prompt mentions "metered component: api-call ($0.01/unit)" but this integration does not plan calls to `SubscriptionComponents` (allocate usage). If usage tracking is required, that is a future step; this plan covers subscription creation and retrieval only.

### Blockers

None. All operations and models are available in the SDK map. Configuration and error boundary design are settled by the companion skills.

---

## Implementation Checklist by Step

### Step 1: Register SDK client in DI

- Read configuration: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`
- Construct `BasicAuthCredentials` with username = API key, password = `"x"`
- Instantiate or DI-register `MaxioAdvancedBillingClient` with a long-lived `IHttpClientFactory`-backed `HttpClient`
- Export as `IMaxioAdvancedBillingClient` or singleton if simpler

**Load:** `dotnet-client-initialization`, `dotnet-authentication`

### Step 2: Implement GET /api/subscription-plans

- Call `client.Products.ListProducts(…)` with all search params = `null`
- Unwrap: `response.Product` from each `ProductResponse`
- Map to a DTO (Name, Handle, PriceInCents, Interval, IntervalUnit)
- Cache result (application cache, ~24 hours, or per-request if minimal overhead acceptable)

**Load:** `dotnet-calling-endpoints`, `dotnet-models`

### Step 3: Implement POST /api/subscriptions

- Extract authenticated user ID / reference
- Call `client.Customers.ReadCustomerByReference(reference:)` 
  - If **404** (or not found in `RawError.StatusCode`): call `client.Customers.CreateCustomer(…)` with `CreateCustomerRequest` → `CreateCustomer` model (FirstName, LastName, Email, Reference)
  - If **200**: extract `.Customer.Id` from response
- Call `client.Subscriptions.CreateSubscription(…)` with `CreateSubscriptionRequest` → `CreateSubscription` model:
  - `CustomerId` (from customer lookup/create) OR `CustomerReference`
  - `ProductId` (from request body) OR `ProductHandle` (from request body)
  - No payment profile, no trial fields
- Unwrap: `response.Subscription` from `SubscriptionResponse`
- Return to caller (subscription ID, state, next billing date, etc.)

**Load:** `dotnet-calling-endpoints`, `dotnet-models`, `dotnet-error-handling`

### Step 4: Implement GET /api/my-subscriptions

- Extract authenticated user ID / reference
- Call `client.Customers.ReadCustomerByReference(reference:)` to get customer ID
- Call `client.Subscriptions.ListSubscriptions(…)` — **Note:** No customer filter in `ListSubscriptions`; instead use `client.Customers.ListCustomerSubscriptions(customerId:)` from Customers controller
  - OR manually filter results if needed
- Unwrap: `response.Subscription` from each `SubscriptionResponse`
- Map to a DTO and return

**Load:** `dotnet-calling-endpoints`, `dotnet-models`, `dotnet-error-handling`

### Step 5: Error boundary & logging

- Catch `SdkException<CreateCustomerError>` (Case A) → inspect `TryGetCustomerErrorResponse1(…)` for 422 details
- Catch `SdkException<CreateSubscriptionError>` (Case A) → inspect `TryGetErrorListResponse1(…)` for 422 details
- Catch `SdkException<RawError>` (Case B) → inspect `StatusCode`, `ReadAsString()` for details
- Catch `JsonException` → log as drift or parse failure, map to 5xx or graceful degradation per application policy
- Map Maxio-specific errors to HTTP 400/422 (validation) or 5xx (outage) before returning to caller

**Load:** `dotnet-error-handling`

---

**Generated with Maxio Advanced Billing .NET SDK specialist**
