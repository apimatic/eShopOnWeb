# Maxio Advanced Billing Integration — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Step 1: Client registration** — Initialize the Maxio SDK client with Basic auth, configure for sandbox.
2. **Step 2: List subscription plans** — GET `/api/subscription-plans`; call `client.Products.ListProducts` to fetch plans by product family.
3. **Step 3: Idempotent customer resolution** — POST `/api/subscriptions`; before creating subscription: try `ReadCustomerByReference(user email)`, fall back to `CreateCustomer` if 404.
4. **Step 4: Subscribe user to plan** — POST `/api/subscriptions`; call `CreateSubscription` with resolved customer ID and plan product handle; capture subscription ID, price, state, next billing date.
5. **Step 5: List user's subscriptions** — GET `/api/my-subscriptions`; call `ListCustomerSubscriptions(customerId)` to retrieve active subscriptions.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation: ListProducts (List Subscription Plans)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Products` |
| **Method Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Required params** | All 8 params before `page` must pass explicitly (pass `null` to skip each). Defaults: `page = 1`, `perPage = 20`. |
| **Wire names** | `dateField` ← `date_field`, `endDate` ← `end_date`, `endDatetime` ← `end_datetime`, `startDate` ← `start_date`, `startDatetime` ← `start_datetime`, `includeArchived` ← `include_archived`, `perPage` ← `per_page` |
| **Request body** | None (query-only). |
| **Response envelope** | `IReadOnlyList<ProductResponse>` — array of products. Each element is a `ProductResponse` wrapping `Product (product): Product !req`. |
| **Response model** | `Product` fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?` (nested), others. |
| **Error case** | **Case B** — `SdkException<RawError>`. Accessors: `.Error.StatusCode: HttpStatusCode`, `.Error.ReadAsString(): string`, `.Error.ReadAsJson<T>(): T?`, `.Error.ReadAsBytes(): ReadOnlyMemory<byte>` |
| **Pagination** | Manual: `page` and `perPage` (defaults 1, 20). Loop incrementing `page` until response array is empty or shorter than `perPage`. |
| **Notes** | Endpoint accepts all query filters but for scope only fetch by `filter` (pass structured filter object to narrow by product family handle or archived status). No required fields — all filters are optional. |
| **Source** | `operations/Products.md` |

### Operation: ReadCustomerByReference (Lookup Customer by Email/External ID)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Required params** | `reference` (string, no default) — the customer's external reference value (e.g. user email or app-assigned ID). |
| **Wire names** | `reference` ← `reference` (query param) |
| **Request body** | None. |
| **Response envelope** | `CustomerResponse` wrapping `Customer (customer): Customer !req`. |
| **Response model** | `Customer` fields: `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, others. |
| **Error case** | **Case B** — `SdkException<RawError>`. On 404 (customer not found), `.Error.StatusCode == HttpStatusCode.NotFound`. Accessors: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`. |
| **Pagination** | None. |
| **Notes** | Returns a **single exact match** or 404. Used for idempotent customer lookup: call this first with user email; if 404, create the customer. If any other error, propagate. |
| **Source** | `operations/Customers.md` |

### Operation: CreateCustomer (Create Customer if Not Found)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Required params** | `body` (nullable, no default) — **must pass explicitly**. |
| **Request model** | `CreateCustomerRequest` wrapping `Customer (customer): CustomerAttributes !req`. Nested `CustomerAttributes` fields: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `TaxExempt (tax_exempt): bool?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`, others. Wire names use snake_case (e.g. `first_name`). |
| **Required fields in request** | None are marked `!req` at SDK level; all optional. **Notes** (from operation page) require `reference` be unique and identifies the customer from your app (e.g. user ID or email). Recommendation: always set `reference` and `email`. |
| **Response envelope** | `CustomerResponse` wrapping `Customer (customer): Customer !req`. |
| **Response model** | `Customer` as above; response includes assigned Maxio `Id (id): int?`. |
| **Error case** | **Case A** — `SdkException<CreateCustomerError>`. Accessor: `ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 — validation error], `ex.Error.TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1` wraps `Errors (errors): Errors?` with field-level error arrays. |
| **Pagination** | None. |
| **Notes** | Endpoint validates uniqueness of `reference`. If duplicate (e.g. same email sent twice), 422 is returned. To ensure idempotence, **always call `ReadCustomerByReference` first**; only call `CreateCustomer` on 404. ISO country/state codes required if `country`/`state` are set. |
| **Source** | `operations/Customers.md` |

### Operation: CreateSubscription (Subscribe User to Plan)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Method Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Required params** | `body` (nullable, no default) — **must pass explicitly**. |
| **Request model** | `CreateSubscriptionRequest` wrapping `Subscription (subscription): CreateSubscription !req`. Nested `CreateSubscription` is a complex record with many optional fields. Key fields for scope: `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Reference (reference): string?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, others. Wire names use snake_case. |
| **Required fields** | None marked `!req` at SDK model level; however **Notes** state: either `customer_id` or `customer_reference` must be provided to identify the customer. To subscribe, provide **one of** `product_id` or `product_handle` (e.g. `eshop-pro`). Specify `product_price_point_handle` or `product_price_point_id` to lock a specific price; if omitted, the product's default price point is used. |
| **Response envelope** | `SubscriptionResponse` wrapping `Subscription (subscription): Subscription !req`. |
| **Response model** | `Subscription` fields: `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `Customer (customer): Customer?`, `Product (product): Product?`, `Reference (reference): string?`, others. |
| **Error case** | **Case A** — `SdkException<CreateSubscriptionError>`. Accessor: `ex.Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422 — validation error], `ex.Error.TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1` wraps `Errors (errors): IReadOnlyList<string> !req` — array of error messages. |
| **Pagination** | None. |
| **Notes** | Per scope, no payment method is required (scope specifies "payment method NOT required"). Plan details: no trial, no setup fee, no tax, never expires. To ensure idempotence, use `reference` field to store a stable subscription handle (e.g. `{userId}-{planHandle}`) and always check `ReadSubscription` or search before retrying. |
| **Source** | `operations/Subscriptions.md` |

### Operation: ListCustomerSubscriptions (Retrieve User's Subscriptions)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Required params** | `customerId` (int, no default) — the Maxio customer ID (from `CreateCustomer` or `ReadCustomerByReference` response). |
| **Wire names** | N/A (URL path param). |
| **Request body** | None. |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` — array of subscriptions. Each element wraps a `Subscription` as above. |
| **Response model** | `Subscription` fields as listed in CreateSubscription (State, ProductPriceInCents, NextAssessmentAt, etc.). |
| **Error case** | **Case B** — `SdkException<RawError>`. Accessors: `.Error.StatusCode`, `.Error.ReadAsString()`. On 404 (customer not found), status will be 404. |
| **Pagination** | None (endpoint does not support pagination; assumes customer has manageable number of subscriptions). |
| **Notes** | Returns all subscriptions linked to the customer, regardless of state. Filter by `state` or `updated_at` on the app side if needed. |
| **Source** | `operations/Customers.md` |

---

### Enum Values — SubscriptionState

From `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (StringEnum):

- `Active (active)` — normal, active subscription, not in trial, paid and up to date.
- `Canceled (canceled)` — subscription has been canceled.
- `PastDue (past_due)` — payment is overdue.
- `Pending (pending)` — awaiting first assessment.
- `Trialing (trialing)` — in a trial period.
- `TrialEnded (trial_ended)` — trial period ended without renewal.
- `Expired (expired)` — subscription has expired (reached its end date).
- `OnHold (on_hold)` — subscription is on hold.
- `Suspended (suspended)` — dunning has suspended it.
- `Unpaid (unpaid)` — awaiting payment.
- `AwaitingSignup (awaiting_signup)` — awaiting initial signup completion.

Construct: `SubscriptionState.Active` or `SubscriptionState.FromValue("active")`.

### Enum Values — IntervalUnit

From `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (StringEnum):

- `Day (day)` — daily billing.
- `Month (month)` — monthly billing.

Construct: `IntervalUnit.Month` or `IntervalUnit.FromValue("month")`.

### Enum Values — CollectionMethod

From `MaxioAdvancedBilling.Models.Enums.CollectionMethod` (StringEnum):

- `Automatic (automatic)` — automatic payment collection.
- `Remittance (remittance)` — remittance-based (Relationship Invoicing).
- `Prepaid (prepaid)` — prepaid account balance.
- `Invoice (invoice)` — invoice (legacy Statements Architecture).

Construct: `CollectionMethod.Automatic` or `CollectionMethod.FromValue("automatic")`.

---

### Client Construction & Auth

**Using DI (`Startup.cs` or `Program.cs`):**

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;

services.AddMaxioAdvancedBillingClient(options =>
{
    options.BasicAuth = new BasicAuthCredentials
    {
        Username = "<API_KEY>",      // from Maxio:ApiKey config binding
        Password = "x"               // literal string "x"
    };
    options.Environment = ServerEnvironment.Us;  // or ServerEnvironment.Eu
    // Optional: override server base URL
    // options.Server.Production.Us.BaseUrl = "http://localhost:8080";
    // options.Server.Production.Us.Site = "<subdomain>";
});
```

**Configuration binding (appsettings.json):**

```json
{
  "Maxio": {
    "ApiKey": "your-api-key-here",
    "Subdomain": "your-site",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null
  }
}
```

**Namespace for client/options:**
- `using MaxioAdvancedBilling;`
- `using MaxioAdvancedBilling.Core.Authentication.Basic;`
- `using MaxioAdvancedBilling.Servers;`

---

### Models Namespace & Key Types

**All models live in: `using MaxioAdvancedBilling.Models;`**
**All enums live in: `using MaxioAdvancedBilling.Models.Enums;`**
**All error types live in: `using MaxioAdvancedBilling.Errors;`**

Key request/response models:
- `ProductResponse` — wraps `Product`
- `CustomerResponse` — wraps `Customer`
- `CreateCustomerRequest` — wraps `CustomerAttributes`
- `CreateSubscriptionRequest` — wraps `CreateSubscription`
- `SubscriptionResponse` — wraps `Subscription`
- `CreateCustomerError` — error payload for CreateCustomer [Case A]
- `CreateSubscriptionError` — error payload for CreateSubscription [Case A]
- `RawError` — fallback error for all Case B operations

---

## Trap Notes

⚠ **Step 1 (client registration)** — The `HttpClient` passed to the SDK constructor must be long-lived and reused (typically via `IHttpClientFactory`); the SDK client wrapper may be transient. SDK retries are governed by `options.Retry` (Polly-backed), not by the `HttpClient` factory's own pipeline. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 1 (auth)** — Credentials must be set **before** the client is constructed or via the DI callback (not swapped mid-session). HTTP Basic auth is username (API key) + password (`"x"` literal). **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 2 (list products)** — Query params are optional but must be passed explicitly to the method signature; passing `null` skips each. Pagination is manual: increment `page` until the response array is shorter than `perPage`. **MUST load `dotnet-calling-endpoints`** before writing the first operation call.

⚠ **Step 3 (customer lookup/creation)** — To guarantee idempotence, **always call `ReadCustomerByReference` first** with the user's email or external ID. If it returns 404, then call `CreateCustomer`. Any other error (500, 422) should be propagated immediately; do not retry as a create. If the create call also returns 422 (duplicate `reference`), the customer exists but the lookup missed it — investigate race conditions or stale data. **MUST load `dotnet-error-handling`** before writing the error boundary.

⚠ **Step 3 & 4 (models)** — `CreateCustomerRequest` wraps a nested `CustomerAttributes` object; do not instantiate `Customer` directly for requests. Similarly, `CreateSubscriptionRequest` wraps `CreateSubscription`. All field names in the C# model use PascalCase (e.g. `FirstName`); wire names (JSON) use snake_case (e.g. `first_name`). **MUST load `dotnet-models`** before building request objects.

⚠ **Step 4 (subscriptions)** — Per scope, no payment profile is required; ensure the request does **not** include payment methods. The plan is configured on Maxio's side with no trial, no setup fee, no expiration, no tax. Only send product handle/ID and customer reference. If a 422 is received with a payment-related error, the Maxio plan may have been misconfigured. **MUST load `dotnet-error-handling`** — case A operations throw typed errors with `TryGet…` accessors; case B throw raw errors with status codes.

⚠ **Step 4 (idempotent subscription)** — SDK provides no built-in deduplication. To prevent duplicate subscriptions on retry, store a subscription `reference` (e.g. `{userId}-{planHandle}-{timestamp}`) in the request and check `ListCustomerSubscriptions` or call `FindSubscription(reference)` before attempting a second create. **MUST load `dotnet-configuration-resilience`** — understand that `MaxRetries` has a floor of 1 (cannot be 0); failed transport calls (e.g. timeout) retry on all HTTP verbs including POST, so non-idempotent calls can execute twice without SDK-level protection.

⚠ **Step 5 (list subscriptions)** — No pagination; endpoint returns all subscriptions for the customer. If the response array is large, filter/sort on the app side (e.g. by state). **MUST load `dotnet-calling-endpoints`** for proper call building.

⚠ **All operations: JsonException on 2xx response with missing `required` field** — A response envelope (e.g. `ProductResponse`) declares fields as `required`. If the 2xx wire JSON lacks one, deserialization throws `System.Text.Json.JsonException`, **not** `SdkException`. A boundary catching only `SdkException` will let it escape unhandled. **MUST load `dotnet-error-handling`** — map all `JsonException` to a deterministic error response and never silently drop it.

⚠ **All operations: JsonException on non-2xx response if error shape doesn't match** — If a 422 or 500 response body does not deserialize to the operation's typed error class (e.g. `CreateCustomerError`), the SDK throws `JsonException` during error-object construction, **destroying the HTTP status code**. A boundary that maps all `JsonException` to 5xx will incorrectly label a 422 validation error as a server fault, causing retry loops that cannot succeed. **MUST load `dotnet-error-handling`** — design the boundary to handle both `SdkException` (with typed or raw error) and `JsonException` (with fallback to RawError inspection if available, or logged as a defect).

---

## REQUIRED READING

**Load these skills BEFORE implementation starts.** They govern the full integration surface; this sheet carries only the operation signatures and model field names, not the gotchas and wiring rules each skill documents.

| Skill | Step | Purpose |
|-------|------|---------|
| `dotnet-client-initialization` | 1 (client registration) | Client construction, DI wiring, `HttpClient` lifecycle, options builder. |
| `dotnet-authentication` | 1 (auth) | Basic auth credential injection, configuration binding, per-environment secrets. |
| `dotnet-calling-endpoints` | 2–5 (all operations) | Operation call signatures, required params, named vs. positional arguments, pagination, async/await, cancellation tokens. |
| `dotnet-models` | 3–4 (request/response building) | Model construction (immutable records, `init` setters), union types and their factories, enum instantiation via static members or `FromValue()`, nullable vs. required field distinction, JSON wire names. |
| `dotnet-error-handling` | 3–5 (error boundaries) | Case A vs. Case B error types, `TryGet…` accessors, `RawError` fallback, handling `JsonException` from both malformed 2xx responses and non-2xx error payloads, deterministic boundary design. |
| `dotnet-configuration-resilience` | 2–5 (retries, timeouts, server URL) | Retry configuration, timeout semantics (per-attempt vs. total), `MaxRetries` floor constraint, non-idempotent POST retry behavior, base URL override, logging hooks. |
| `dotnet-testing` | — (as needed for unit tests) | Test fixture patterns, `HttpClient` mocking seams, assertion patterns. |

**Both of these hazards belong in EVERY integration, regardless of the operations touched — read them before writing the error boundary:**

- **`System.Text.Json.JsonException` from 2xx deserialization** — Thrown when a `required` field is missing from a 2xx response body. It is **not** an `SdkException` and will escape a boundary that only catches `SdkException`. **MUST load `dotnet-error-handling`**.
- **`System.Text.Json.JsonException` replacing `SdkException` on non-2xx error deserialization** — Thrown when a non-2xx error response doesn't match its typed error class shape, destroying the HTTP status code in the process. A boundary that maps all `JsonException` to 5xx will cause retry loops that can never succeed. **MUST load `dotnet-error-handling`**.

---

## Assumptions & Blockers

### Assumptions

1. **User ↔ Subscription mapping** — Users are authenticated via JWT; their identity is available in `User.FindFirst("sub")` or equivalent. Subscriptions will be looked up via Maxio customer by email or external ID (recommendation: use email as `reference`).

2. **In-memory vs. persistent** — Maxio customer ID and subscription ID will be cached/persisted in the app's own database (not shown in this plan). On each request, look up the customer by reference first (idempotent); store the returned Maxio customer ID in the app's user record for subsequent calls.

3. **Idempotent customer creation** — The pattern is: `ReadCustomerByReference(email)` → if 404, `CreateCustomer`. If both fail (e.g. 500 on the create), the error is propagated and the call is not automatically retried at the handler level; retries are governed by `dotnet-configuration-resilience` (Polly retry policy on the SDK client).

4. **Subscription plan enumeration** — Plans are fetched via `ListProducts` filtered by product family handle (`eshop-subscribe`). The app caches the list (e.g. in memory or Redis) and does NOT re-fetch on every request unless explicitly invalidated. Frequency: once at startup or on an admin cache-clear action.

5. **Subscription state display** — The app receives `SubscriptionState` enum values (e.g. `Active`, `Canceled`) and renders them to the user. State machine transitions (e.g. `Trialing` → `Active` → `Canceled`) are managed entirely by Maxio; the app only reads and displays the current state.

6. **Configuration** — All Maxio settings (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl` override) are bound from `appsettings.json` under the `Maxio:` section. Secrets are stored in `.NET user secrets` or env vars (not checked into the repo).

7. **No payment method in scope** — Subscription creation does **not** accept payment profiles. The Maxio plans are configured to not require them (verified against sandbox setup: Pro/Basic plans, no trial, no setup fee, payment not required).

8. **HTTP response envelope unwrapping** — Operations return wrapped responses (e.g. `ProductResponse` containing a `Product` field). The app unpacks these one level down: `response.Product` (not just `response`).

### Blockers

None. All operations required by scope are present in the SDK map.

---

**Plan file written to:** `C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-005\repo\maxio-plan.md`

**Summary:** Contract sheet documents four core operations (list products, lookup/create customers, create subscriptions, list customer subscriptions) with exact signatures, request/response models, error types, and enum values. Each operation row includes wire names, required parameters, error accessors, and pagination rules. Seven dotnet-* companion skills are mandated; two JSON deserialization hazards are called out as critical to the error boundary design. Idempotent customer creation and per-subscription reference tracking are highlighted as requirements for safe retries.

**Assumptions & Blockers:** Eight assumptions cover user identity, state management, caching, Maxio configuration, and payment method scope; zero blockers remain.
