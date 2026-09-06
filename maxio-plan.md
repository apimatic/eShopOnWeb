# Maxio Advanced Billing Integration — eShopOnWeb Hero Flow

## Scope & Sequence

1. **List available subscription plans** — load plans from `eshop-subscribe` product family
   - Operation: `ListProductsForProductFamily`
2. **Ensure Maxio customer exists** — idempotent lookup by eShopOnWeb user ID (external reference)
   - Operations: `ReadCustomerByReference`, `CreateCustomer`
   - Locally track mapping: userId → Maxio customerId
3. **Subscribe user to selected plan** — create subscription, capture response, store locally
   - Operation: `CreateSubscription`
   - Locally track mapping: userId + planId → Maxio subscriptionId
4. **Fetch active subscriptions for user** — read subscription details, confirm state
   - Operation: `ListSubscriptions` (filtered by customer) or `ReadSubscription` (by ID)

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Step 1: List Plans in Product Family

| Element | Details | Source |
|---------|---------|--------|
| **Controller & method** | `client.ProductFamilies.ListProductsForProductFamily` | `operations/ProductFamilies.md` |
| **Signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `operations/ProductFamilies.md` |
| **Request fields** | `productFamilyId`: string !req — pass the product family handle or ID (e.g., `eshop-subscribe`). All other params are optional filters; pass `null` to omit. | `operations/ProductFamilies.md` |
| **Returns** | `IReadOnlyList<ProductResponse>` — each response wraps one `Product` object in its `Product` (wire: `product`) field. Extract plan ID, handle, name, and `PriceInCents` (wire: `price_in_cents`) from each `Product`. | `records-3-Of-Su.md` |
| **Error case** | `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)**. Accessor: `TryGetString(out string)` [404]. Fallback: `TryGetRawError(out RawError)`. | `operations/ProductFamilies.md` |
| **Pagination** | Manual via `page` + `perPage`; defaults `page = 1`, `perPage = 20`. | `operations/ProductFamilies.md` |

### Step 2: Lookup & Create Customer (Idempotent)

#### 2a. Read Customer by External Reference

| Element | Details | Source |
|---------|---------|--------|
| **Controller & method** | `client.Customers.ReadCustomerByReference` | `operations/Customers.md` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `operations/Customers.md` |
| **Request fields** | `reference`: string !req — pass the eShopOnWeb user ID string (unique identifier on caller side). | `operations/Customers.md` |
| **Returns** | `CustomerResponse` — wraps single `Customer` in `Customer` (wire: `customer`) field. Extract `Id` (Maxio customer ID, wire: `id`) and `Reference` (wire: `reference`). | `records-2-Cr-Ne.md` |
| **Error case** | `SdkException<RawError>` — **Case B**. Check `StatusCode` for 404 (not found) vs other errors. Call `ReadAsString()` for body. | `operations/Customers.md` |
| **Pagination** | None. | `operations/Customers.md` |
| **Notes** | If 404, customer does not exist — proceed to 2b. If 200, store `Customer.Id` in in-memory userId → customerId mapping. | `operations/Customers.md` |

#### 2b. Create Customer (If Lookup Failed with 404)

| Element | Details | Source |
|---------|---------|--------|
| **Controller & method** | `client.Customers.CreateCustomer` | `operations/Customers.md` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `operations/Customers.md` |
| **Request model** | `CreateCustomerRequest` wraps `CreateCustomer` in `Customer` (wire: `customer`) field. Populate `CreateCustomer` (namespace `MaxioAdvancedBilling.Models`) with: `FirstName` (wire: `first_name`) !req string, `LastName` (wire: `last_name`) !req string, `Email` (wire: `email`) !req string, `Reference` (wire: `reference`) optional string. Pass the eShopOnWeb user ID as `Reference` for future lookups. | `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| **Returns** | `CustomerResponse` — wraps `Customer` in `Customer` field. Extract `Id` and store in userId → customerId mapping. | `records-2-Cr-Ne.md` |
| **Error case** | `SdkException<CreateCustomerError>` — **Case A (typed)**. Accessor: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]. Fallback: `TryGetRawError(out RawError)`. For 422, `CustomerErrorResponse1.Errors` (wire: `errors`) may contain field-level errors. | `operations/Customers.md` |
| **Pagination** | None. | `operations/Customers.md` |

### Step 3: Create Subscription (Enroll User in Plan)

| Element | Details | Source |
|---------|---------|--------|
| **Controller & method** | `client.Subscriptions.CreateSubscription` | `operations/Subscriptions.md` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `operations/Subscriptions.md` |
| **Request model** | `CreateSubscriptionRequest` wraps `CreateSubscription` in `Subscription` (wire: `subscription`) field. Populate `CreateSubscription` (namespace `MaxioAdvancedBilling.Models`) with: **required:** `CustomerId` (wire: `customer_id`) int — the Maxio customer ID from Step 2. **one of:** `ProductHandle` (wire: `product_handle`) string OR `ProductId` (wire: `product_id`) int — specify the selected plan (e.g., `eshop-pro`, `basic-plan`). **optional:** `Reference` (wire: `reference`) string — store a reference linking subscription to eShopOnWeb userId + context if needed. See Maxio docs for all other optional fields (`PaymentProfileId`, `Components`, `CouponCode`, etc.). | `records-2-Cr-Ne.md` |
| **Returns** | `SubscriptionResponse` — wraps `Subscription` in `Subscription` field. Extract: `Id` (wire: `id`, Maxio subscription ID), `State` (wire: `state`, enum `SubscriptionState`), `CurrentPeriodEndsAt` (wire: `current_period_ends_at`, next billing date), `NextAssessmentAt` (wire: `next_assessment_at`), `ProductPriceInCents` (wire: `product_price_in_cents`). Store subscriptionId in in-memory mapping. | `records-4-Su-We.md` |
| **Error case** | `SdkException<CreateSubscriptionError>` — **Case A (typed)**. Accessor: `TryGetErrorListResponse1(out ErrorListResponse1)` [422]. Fallback: `TryGetRawError(out RawError)`. For 422, `ErrorListResponse1.Errors` (wire: `errors`) is a list of error messages. | `operations/Subscriptions.md` |
| **Pagination** | None. | `operations/Subscriptions.md` |

### Step 4: Fetch Active Subscriptions for User

| Element | Details | Source |
|---------|---------|--------|
| **Controller & method** | `client.Subscriptions.ListSubscriptions` (recommended for querying state), OR `client.Subscriptions.ReadSubscription` (if you have subscriptionId). | `operations/Subscriptions.md` |
| **Signature (ListSubscriptions)** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `operations/Subscriptions.md` |
| **Request fields (ListSubscriptions)** | `state`: `SubscriptionStateFilter?` (namespace `MaxioAdvancedBilling.Models.Enums`) — optional, pass `SubscriptionStateFilter.Active` to filter only active subscriptions. All other params optional; pass `null` to omit. **Note:** `ListSubscriptions` does **not** accept a customer filter in query params; if you need to scope by customer, filter results client-side or use the returned list and match against your userId → customerId mapping. | `operations/Subscriptions.md` |
| **Signature (ReadSubscription)** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | `operations/Subscriptions.md` |
| **Request fields (ReadSubscription)** | `subscriptionId`: int !req — Maxio subscription ID (from Step 3 or stored mapping). `include`: optional list; pass `null` or omit. | `operations/Subscriptions.md` |
| **Returns (both)** | `IReadOnlyList<SubscriptionResponse>` (ListSubscriptions) OR `SubscriptionResponse` (ReadSubscription). Extract `Subscription.Id`, `State` (enum), `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ProductPriceInCents` for each. Confirm `State == SubscriptionState.Active` to identify active subscriptions. | `records-4-Su-We.md` |
| **Error case** | `SdkException<RawError>` — **Case B** (both operations). Check `StatusCode`; call `ReadAsString()` for body. | `operations/Subscriptions.md` |
| **Pagination (ListSubscriptions)** | Manual via `page` + `perPage`; defaults `page = 1`, `perPage = 20`. | `operations/Subscriptions.md` |

---

## Enum Values

### SubscriptionState (namespace: `MaxioAdvancedBilling.Models.Enums`)

Full list: `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.

For the hero flow, focus on: `Active` (subscription is current and up-to-date) and `Canceled` (subscription is terminated).

Access via: `SubscriptionState.Active`, `SubscriptionState.Canceled`, etc. Do not instantiate `StringEnum<SubscriptionState>` directly; use the static properties.

Source: `models/enums.md`

### SubscriptionStateFilter (namespace: `MaxioAdvancedBilling.Models.Enums`)

Used for list filtering: `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `OnHold (on_hold)`, `PastDue (past_due)`, `Trialing (trialing)`, `Unpaid (unpaid)`, etc. Access via `SubscriptionStateFilter.Active`.

Source: `models/enums.md`

---

## Client Construction & Auth

**Client registration (DI):**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;

services.AddMaxioAdvancedBillingClient(options =>
{
    // Load API key from configuration (e.g., IConfiguration)
    var apiKey = configuration["Maxio:ApiKey"];
    options.BasicAuth = new BasicAuthCredentials 
    { 
        Username = apiKey,    // API key goes in Username
        Password = "x"        // Literal string "x"
    };
    // Optionally override server/environment:
    // options.Environment = ServerEnvironment.Us;  // or .Eu
});
```

**Configuration keys (user-secrets or appsettings):**
- `Maxio:ApiKey` — Maxio API key
- `Maxio:Subdomain` — sandbox subdomain (e.g., `cp-exp-1`)
- `Maxio:ProductFamilyHandle` — product family handle for plans (e.g., `eshop-subscribe`)
- `Maxio:BaseUrl` (optional) — override base URL if not using the standard `https://{site}.chargify.com`

Source: `sdk-map.md` (Servers & auth section)

---

## Trap Notes

⚠ **Step 1–4 (client registration & all calls)** — the SDK's `RetryOptions` (which gates only HTTP status-based retry logic) and `Timeout` (per-attempt, not per-call) are **not** global circuit-breaker settings. HTTP transport failures (e.g., connection drops) on non-idempotent verbs like `POST` **are** retried by Polly's default retry policy, meaning `CreateCustomer` or `CreateSubscription` could execute twice if a network error occurs mid-response. Review retry scope and idempotency requirements before production. **MUST load `dotnet-configuration-resilience`** for full semantics.

⚠ **Step 2b & 3 (CreateCustomer & CreateSubscription)** — both throw `SdkException<…Error>` with typed accessors for specific HTTP statuses, **not** `SdkException<RawError>`. When deserializing the error payload (e.g., `ErrorListResponse1` for 422 on create subscription), if the wire JSON is malformed or missing required fields, deserialization itself throws `JsonException` **before** the `SdkException` is constructed — the HTTP status is lost, and the error surface as a 5xx outage to callers. Write your error boundary to catch and log both `SdkException<T>` and `JsonException` separately, distinguishing malformed-response errors (likely a real API bug, not a retry-able transient) from provider-rejected-request errors. **MUST load `dotnet-error-handling`** to understand the full Case A/B exception model and the two paths `JsonException` can take.

⚠ **Step 2a (ReadCustomerByReference on 404)** — returns `SdkException<RawError>`, not a typed error. Check `ex.Error.StatusCode` against `HttpStatusCode.NotFound` to distinguish "customer does not exist" (proceed to create) from "auth failed" (401) or "server error" (5xx, retry). Do not parse `ex.Error.ReadAsString()` unless you need the body for logging; the status code is your contract. **MUST load `dotnet-error-handling`** for `RawError` accessors and the exception boundary shape.

⚠ **Step 3 (CreateSubscription)** — the response `SubscriptionResponse.Subscription.State` is a `StringEnum<SubscriptionState>`, **not** a C# `enum`. Do **not** use switch on the property directly; call the `TryGet…()` factory methods or compare against static properties (e.g., `subscription.State == SubscriptionState.Active`). **MUST load `dotnet-models`** for union/enum construction and access patterns.

⚠ **Step 1–4 (all responses)** — response envelopes wrap their payload in a single required field (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`). Extract the inner model one level down; do **not** assume the response body is the model itself. The map specifies the wrapper type; the implementation calls out the inner field and its type. **MUST load `dotnet-models`** before handling any response object.

---

## REQUIRED READING

Before implementation starts, load these companion skills in the order listed. Each carries the only place that skill's defaults, semantics, and gotchas live; the sheet deliberately omits their contents to keep the contract tight.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Client construction, DI registration, HttpClient factory pattern, lifetime management. |
| `dotnet-authentication` | Basic auth credential wiring, per-environment configuration, rotating credentials. Load before Step 1. |
| `dotnet-calling-endpoints` | Named argument calling conventions, positional vs. named params on list/search operations, optional param handling. Load before any Step 1–4 call. |
| `dotnet-models` | `StringEnum<T>` and union (`OneOf`/`AnyOf`) construction and read-back patterns. Load before extracting any response field. |
| `dotnet-error-handling` | Case A (typed `{Operation}Error`) vs. Case B (`RawError`) exception model, `TryGet…` accessors, the two `JsonException` paths. **Load before writing the API-boundary error handler.** |
| `dotnet-configuration-resilience` | Retry semantics, `Timeout` scope (per-attempt, not total), idempotency and retry interaction, retry policy configuration. Load before Step 1. |

**Two JsonException rows belong in the FIRST error boundary, not a later revision:**
- A drifted or malformed **2xx** response body (e.g., missing required model field) surfaces as `JsonException` from deserialization, **not** as `SdkException` — an SDK-exception-only catch ladder lets it escape; the caller sees a 200 with garbage data or a crash. A boundary that maps unhandled `JsonException` to a generic 5xx and re-logs the inner message avoids silent data loss.
- A **non-2xx** body that does not match the operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being deserialized**, so the `JsonException` **replaces** the `SdkException` and **destroys** the HTTP status code in flight. A caller that retries on 5xx will retry something that can never succeed (the error shape is simply wrong); a boundary that logs the full exception and reports deterministic rejection avoids infinite retry loops.

---

## Assumptions & Blockers

None. All facts required for planning are in the map and SDK source.
