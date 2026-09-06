# Maxio Subscription Integration Plan for eShopOnWeb

## Scope & Sequence

### Step 1: Client & DI Setup
Register the Maxio client in the DI container with Basic auth and sandbox URL configuration.
- Operations: None (infrastructure only)

### Step 2: Integrate with eShopOnWeb User Identity
Map eShopOnWeb's current user ID to Maxio customer ID; support idempotent customer creation.
- Operations: `CreateCustomer`, `ReadCustomerByReference`

### Step 3: Expose Subscription Plans Endpoint
Retrieve plans from the `eshop-subscribe` product family for the UI.
- Operations: `ListProducts`

### Step 4: Handle Subscription Creation
Enroll a user in a plan; create Maxio customer on first subscription if needed.
- Operations: `CreateCustomer`, `CreateSubscription`

### Step 5: Retrieve User's Active Subscriptions
Return current subscriptions for authenticated user.
- Operations: `ListCustomerSubscriptions`, `ReadCustomer`

### Step 6: Error Handling & State Inspection
Implement resilient boundaries for API failures and network errors.
- All operations

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation: ReadCustomerByReference

Find or verify Maxio customer record by eShopOnWeb userId reference.

| | |
|---|---|
| **Controller** | `client.Customers` |
| **Method** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Parameters** | `reference` (string, required): eShopOnWeb userId as stored in Maxio `reference` field |
| **Request Model** | Query param only; no body. Wire: `reference` ← C# `reference` |
| **Response Envelope** | `CustomerResponse` (namespace `MaxioAdvancedBilling.Models`) containing `Customer (customer): Customer !req` — access the inner customer via `response.Customer` |
| **Response Fields** (inner `Customer`) | `Id (id): int?` — Maxio customer ID; `Reference (reference): string?` — eShopOnWeb userId; `Email (email): string?`; `FirstName (first_name): string?`; `LastName (last_name): string?` |
| **Error Case** | **Case B** — `SdkException<RawError>` |
| **Error Accessors** | `.Error.StatusCode: HttpStatusCode` (404 = not found, 422 = validation error) · `.Error.ReadAsString(): string` · `.Error.ReadAsJson<T>(): T?` |
| **Notes** | Returns exactly one match by reference or raises 404. No pagination. **UNVERIFIED:** 404 vs. empty list behaviour on no match — map says "single match," live traffic confirms. |
| **Source** | `operations/Customers.md`, `records-1-Ac-Cr.md` |

### Operation: CreateCustomer

Idempotent customer creation; use eShopOnWeb userId as the `reference` field to allow lookup on retry.

| | |
|---|---|
| **Controller** | `client.Customers` |
| **Method** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (nullable, no default → must pass explicitly). Contains `CreateCustomerRequest` with nested `CreateCustomer` inside. |
| **Request Model** | Envelope: `CreateCustomerRequest (Models)` — one field: `Customer (customer): CreateCustomer !req` |
| | Inner model: `CreateCustomer` (Models) with fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set to eShopOnWeb userId), `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Organization (organization): string?`, `CcEmails (cc_emails): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason)?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` |
| **Response Envelope** | `CustomerResponse` (Models) containing `Customer (customer): Customer !req` — access inner via `response.Customer` |
| **Response Fields** (inner `Customer`) | `Id (id): int?` — Maxio customer ID (store for later use); `Reference (reference): string?`; `Email (email): string?`; `FirstName (first_name): string?`; `LastName (last_name): string?`; `CreatedAt (created_at): DateTimeOffset?` |
| **Error Case** | **Case A (typed)** — `SdkException<CreateCustomerError>` |
| **Error Accessors** | `.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] → `CustomerErrorResponse1.Errors (errors): Errors?` · `.Error.TryGetRawError(out RawError)` [fallback] |
| **Notes** | Returns 422 if `reference` already exists (violation of uniqueness). On idempotent retry after failure: catch 422 and call `ReadCustomerByReference` with the same `reference` to retrieve the existing customer. `required` fields: `FirstName`, `LastName`, `Email`. |
| **Source** | `operations/Customers.md`, `records-1-Ac-Cr.md` |

### Operation: ListProducts

Fetch all products in the `eshop-subscribe` product family; filter on caller side by family handle.

| | |
|---|---|
| **Controller** | `client.Products` |
| **Method** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | 8 nullable params (dateField, filter, endDate, endDatetime, startDate, startDatetime, includeArchived, include) — each must be passed explicitly or as null. Defaults: `page` = 1, `perPage` = 20. Pass `null` for all date/filter params. |
| **Query Params (wire ← C#)** | `page` ← `page`, `per_page` ← `perPage`, all others as above |
| **Request Model** | Query string only; no body. |
| **Response Envelope** | `IReadOnlyList<ProductResponse>` — a list of envelopes. Each `ProductResponse` (Models) contains `Product (product): Product !req` — access inner via iteration: `foreach (var resp in response) { var prod = resp.Product; … }` |
| **Response Fields** (inner `Product`) | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?` (price in cents, divide by 100 for dollars), `Interval (interval): int?` (billing period count), `IntervalUnit (interval_unit): IntervalUnit?` (enum: `Day` or `Month`), `ProductFamily (product_family): ProductFamily?` — contains `Handle (handle): string?`, `Name (name): string?`, `Id (id): int?` |
| **Error Case** | **Case B** — `SdkException<RawError>` |
| **Error Accessors** | `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` |
| **Pagination** | Manual; increment `page` and re-call. No total-page count in response; stop when returned list is empty or < perPage. |
| **Notes** | **No direct filter by ProductFamilyHandle in the operation** — caller must filter returned products: `response.Where(r => r.Product.ProductFamily?.Handle == "eshop-subscribe")`. Defaults to `page=1, perPage=20` but may return fewer results. To list all: increment page until response is empty. |
| **Source** | `operations/Products.md`, `records-3-Of-Su.md` |

### Operation: CreateSubscription

Enroll a user in a plan; creates Maxio customer on the fly if `CustomerAttributes` passed, or uses existing customer by `CustomerId`.

| | |
|---|---|
| **Controller** | `client.Subscriptions` |
| **Method** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (nullable, no default → must pass explicitly). Contains `CreateSubscriptionRequest` with nested `CreateSubscription` inside. |
| **Request Model** | Envelope: `CreateSubscriptionRequest` (Models) — one field: `Subscription (subscription): CreateSubscription !req` |
| | Inner model: `CreateSubscription` (Models) with relevant fields: `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (specify plan handle/id), `CustomerId (customer_id): int?` (existing Maxio customer ID, from `ReadCustomerByReference` or `CreateCustomer` response), `CustomerAttributes (customer_attributes): CustomerAttributes?` (if creating inline customer — not recommended for idempotent flow), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum; set to `Automatic`), `Reference (reference): string?` (idempotency key; set to eShopOnWeb userId + plan handle or similar unique key), `DeferSignup (defer_signup): bool? = false` (always false), `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `NetTerms (net_terms): string?`, plus optional: `CouponCode (coupon_code): string?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` (for metered/quantity-based components), `CalendarBilling (calendar_billing): CalendarBilling?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?` |
| **Response Envelope** | `SubscriptionResponse` (Models) containing `Subscription (subscription): Subscription !req` — access inner via `response.Subscription` |
| **Response Fields** (inner `Subscription`) | `Id (id): int?` — Maxio subscription ID (store for billing tracking), `State (state): SubscriptionState?` (enum: `Pending`, `Trialing`, `Active`, `PastDue`, `Suspended`, `Canceled`, `Expired`, etc.), `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `ProductPriceInCents (product_price_in_cents): long?` (active price), `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date), `CreatedAt (created_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `BalanceInCents (balance_in_cents): long?` (account balance, negative = credit), `TotalRevenueInCents (total_revenue_in_cents): long?` |
| **Error Case** | **Case A (typed)** — `SdkException<CreateSubscriptionError>` |
| **Error Accessors** | `.Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` · `.Error.TryGetRawError(out RawError)` [fallback] |
| **Notes** | Pass `ProductHandle` (plan handle, e.g. "eshop-pro") OR `ProductId`; prefer handle for stability. If customer does not yet exist in Maxio, pass existing `CustomerId` from previous `CreateCustomer` call (idempotent path). **Payment method not required** per task; field may be omitted. `DeferSignup=false` means subscription is active immediately. `Reference` field enables idempotency: if caller re-sends same request, Maxio may reject duplicate or return existing subscription (check docs or live behavior — **UNVERIFIED whether Maxio accepts duplicate references or returns 422**). |
| **Source** | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |

### Operation: ListCustomerSubscriptions

Retrieve all subscriptions for a given Maxio customer (the logged-in user).

| | |
|---|---|
| **Controller** | `client.Customers` |
| **Method** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Parameters** | `customerId` (int, required): Maxio customer ID (from `ReadCustomerByReference` or `CreateCustomer` response) |
| **Request Model** | URL path param only; no query or body. Wire path: `/customers/{customer_id}/subscriptions.json` |
| **Response Envelope** | `IReadOnlyList<SubscriptionResponse>` — a list of envelopes. Each `SubscriptionResponse` (Models) contains `Subscription (subscription): Subscription !req` — access inner via iteration: `foreach (var resp in response) { var sub = resp.Subscription; … }` |
| **Response Fields** (inner `Subscription`) | `Id (id): int?` — subscription ID, `State (state): SubscriptionState?` (enum), `ProductId (product_id): int?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `BalanceInCents (balance_in_cents): long?`, `Product (product): Product?` (optional; contains product details including handle), `Customer (customer): Customer?` (optional) |
| **Error Case** | **Case B** — `SdkException<RawError>` |
| **Error Accessors** | `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` |
| **Pagination** | None — returns all subscriptions for the customer in one call. |
| **Notes** | No filter or pagination parameters. Returns empty list if customer has no subscriptions. **UNVERIFIED:** whether response includes product details by default — check live payload or use `ReadSubscription` with include params if details are needed. |
| **Source** | `operations/Customers.md`, `records-2-Cr-Ne.md` (Subscription) |

### Operation: ReadCustomer

Fetch full customer record by ID (used to verify customer state, fetch contact info for invoice display).

| | |
|---|---|
| **Controller** | `client.Customers` |
| **Method** | `ReadCustomer(int id, CancellationToken ct = default)` |
| **Parameters** | `id` (int, required): Maxio customer ID |
| **Request Model** | URL path param only. Wire path: `/customers/{id}.json` |
| **Response Envelope** | `CustomerResponse` (Models) containing `Customer (customer): Customer !req` — access inner via `response.Customer` |
| **Response Fields** (inner `Customer`) | `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Organization (organization): string?`, `TaxExempt (tax_exempt): bool?` |
| **Error Case** | **Case B** — `SdkException<RawError>` |
| **Error Accessors** | `.Error.StatusCode` (404 = not found), `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` |
| **Pagination** | None |
| **Notes** | Returns 404 if customer does not exist. Used to hydrate user profile or verify customer state before billing operations. |
| **Source** | `operations/Customers.md`, `records-1-Ac-Cr.md` |

---

## Enum Values (Wire → C# Member)

### CollectionMethod (wire ← C# enum member)
- `Automatic` (automatic) — for subscriptions; auto-bill on schedule
- `Remittance` (remittance)
- `Prepaid` (prepaid)
- `Invoice` (invoice)

**Usage:** Set `CreateSubscription.PaymentCollectionMethod = CollectionMethod.Automatic` to auto-bill at renewal.

### SubscriptionState (wire ← C# enum member)
- `Pending` (pending)
- `Trialing` (trialing)
- `Active` (active)
- `PastDue` (past_due)
- `Suspended` (suspended)
- `Canceled` (canceled)
- `Expired` (expired)
- `FailedToCreate` (failed_to_create)
- `SoftFailure` (soft_failure)
- `Unpaid` (unpaid)
- `OnHold` (on_hold)
- `AwaitingSignup` (awaiting_signup)
- `TrialEnded` (trial_ended)
- `Assessing` (assessing)
- `Paused` (paused)

**Usage:** After `CreateSubscription`, check `response.Subscription.State` to verify active status (should be `Active` immediately if `DeferSignup=false`).

### IntervalUnit (wire ← C# enum member)
- `Day` (day)
- `Month` (month)

**Usage:** Access plan billing period via `Product.IntervalUnit` (e.g., `Month` for monthly billing).

---

## Client Construction & Configuration

Bind Maxio configuration from `Maxio:` section in `appsettings.json` (or user-secrets in development):

```json
{
  "Maxio": {
    "ApiKey": "<your-sandbox-api-key>",
    "Subdomain": "cp-exp-1",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": "https://cp-exp-1.chargify.com"
  }
}
```

**DI Registration** (in `Program.cs` or `ConfigureServices`):

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

services.Configure<MaxioOptions>(configuration.GetSection("Maxio"));

services.AddMaxioAdvancedBillingClient((options, sp) =>
{
    var maxioConfig = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
    options.BasicAuth = new BasicAuthCredentials
    {
        Username = maxioConfig.ApiKey,
        Password = "x"  // Literal "x" per SDK
    };
    options.Environment = ServerEnvironment.Us;
    // Optional: override BaseUrl if needed
    // options.Server.Production.Us.BaseUrl = maxioConfig.BaseUrl;
    // options.Server.Production.Us.Site = maxioConfig.Subdomain;
});
```

**Namespaces to add**:
```csharp
using MaxioAdvancedBilling;                          // Client, ServerEnvironment
using MaxioAdvancedBilling.Core.Authentication.Basic; // BasicAuthCredentials
using MaxioAdvancedBilling.Api;                       // Controller accessors (Customers, Products, Subscriptions)
using MaxioAdvancedBilling.Models;                    // Request/response records and enums
using MaxioAdvancedBilling.Models.Enums;              // Enum types (CollectionMethod, SubscriptionState, etc.)
using MaxioAdvancedBilling.Errors;                    // Error classes
using MaxioAdvancedBilling.Core.ErrorResponse;        // RawError
```

---

## Trap Notes (Load Companion Skills Before Implementation)

**⚠ Step 1 (Client & DI setup)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; the `Timeout` parameter is per-attempt, not total, and `HttpMethodsToRetry` gates only the status trigger (transport failures are retried on all verbs including `POST`). **MUST load `dotnet-configuration-resilience`** before wiring the client.

**⚠ Step 2 & 4 (Customer & subscription creation)** — Every operation is throw-only; there is **no** `{Op}Result` no-throw variant. `SdkException<TError>` is thrown on error (both Case A typed and Case B raw). For Case A operations (like `CreateCustomer`, `CreateSubscription`), the error object carries typed `TryGet…()` accessors for status-specific payloads. **MUST load `dotnet-error-handling`** before writing error boundaries.

**⚠ Step 2-5 (All operations)** — A drifted or malformed **2xx** body (a missing `required` member in the response) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. A **non-2xx** body that does not match the operation's generated error shape throws `JsonException` *while the error object is being constructed*, replacing the `SdkException` and destroying the HTTP status. A boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** to design this boundary defensively.

**⚠ Step 2 (Customer operations)** — `ReadCustomerByReference` is Case B (raw error), so check `.Error.StatusCode == 404` to distinguish "not found" from server error. `CreateCustomer` is Case A (typed), so use `.Error.TryGetCustomerErrorResponse1(out var err422)` for validation errors [422] before falling back to `TryGetRawError`. Idempotent flow: on 422, call `ReadCustomerByReference` with the same reference to retrieve the existing customer. **MUST load `dotnet-error-handling`** for typed-error mechanics.

**⚠ Step 3 (ListProducts)** — Response is `IReadOnlyList<ProductResponse>`, not a single envelope. Caller must filter by `ProductFamily.Handle == "eshop-subscribe"` on the application side; the operation has no ProductFamilyHandle filter param. Pagination is manual (increment `page` until list is empty or < 20 items). **MUST load `dotnet-calling-endpoints`** to understand manual pagination and list unwrapping.

**⚠ Step 4 (CreateSubscription)** — `Reference` field enables idempotency but **UNVERIFIED** whether Maxio rejects duplicate references with 422 or silently returns the existing subscription. Design for rejection (catch 422 and treat as success if customer already has an active subscription for that plan). Payment method not required; omit payment-profile fields. `DeferSignup` must be `false` for immediate activation. **MUST load `dotnet-models`** to construct `CreateSubscriptionRequest` and unwrap nested `CreateSubscription` record.

**⚠ Step 4 (Nested request models)** — `CreateSubscriptionRequest` wraps `CreateSubscription` (the actual payload). Same envelope pattern for `CreateCustomerRequest` wrapping `CreateCustomer`. Always construct the outer envelope, not the inner model directly. **MUST load `dotnet-models`** for union/envelope construction patterns.

**⚠ Step 5 (ListCustomerSubscriptions)** — Returns `IReadOnlyList<SubscriptionResponse>` (list of envelopes). Each `SubscriptionResponse` wraps a `Subscription` record. Iterate to access inners: `foreach (var resp in response) { var sub = resp.Subscription; … }`. **UNVERIFIED** whether product details are included by default — may need to call `ReadSubscription` with `include` params if hydration is needed. **MUST load `dotnet-calling-endpoints`** for response unwrapping.

---

## REQUIRED READING

Before implementation starts, load these skills in order. The contract sheet deliberately does not carry their contents — they are companions that resolve usage patterns, testing seams, and defensive coding practices this sheet cannot inline:

1. **`dotnet-client-initialization`** — Step 1: Client construction, DI registration, HttpClient lifetime and reuse.
2. **`dotnet-authentication`** — Step 2: Basic auth credential wiring, username/password set format, rotation.
3. **`dotnet-calling-endpoints`** — Steps 3–5: Calling operations, named vs. positional params, response envelope unwrapping, pagination.
4. **`dotnet-models`** — Step 4: Request model construction (nested records, `!req` fields, union types for optional components).
5. **`dotnet-error-handling`** — Steps 2–5: Case A/B error typing, `TryGet…()` accessors, `JsonException` from deserialization, boundary design.
6. **`dotnet-configuration-resilience`** — Step 1: Retry options (per-attempt timeout, HTTP method filters, transport-error retries on POST).
7. **`dotnet-testing`** — Testing: HttpClient constructor argument, mock seams, assertion patterns.

**Critical JsonException handling** (both must appear in the FIRST error boundary, not later revisions):
- A drifted or malformed **2xx** body (missing `required` field) surfaces as `JsonException` from deserialization, **not** as `SdkException` — an SDK-exception-only catch lets it escape.
- A **non-2xx** body that does not match the operation's error shape throws `JsonException` *while the error object is being constructed*, **replacing** the `SdkException` and destroying the HTTP status — a boundary mapping every `JsonException` to 5xx then reporting it as an outage makes retries fail forever.

---

## Assumptions & Blockers

### Assumptions
- eShopOnWeb user identity (userId) is a stable string that can be stored in Maxio's `reference` field and used for lookups across multiple subscription calls.
- Maxio subdomain `cp-exp-1` and product family `eshop-subscribe` already exist in the Maxio sandbox account, with plans `eshop-pro` and `basic-plan` pre-configured at the stated prices.
- "No payment method required" means the subscription can be created without passing payment-profile fields; Maxio will not auto-bill on renewal (or will use a default method if configured at the site level).
- "Idempotent customer creation" means the application will track Maxio customer IDs (store in eShopOnWeb user record) to avoid re-creating on retry; if not stored, `ReadCustomerByReference` is called to look up existing.
- JWT authentication on the PublicApi project gates all three endpoints; no additional Maxio-specific auth is needed on the response side.
- Response payloads (subscription state, product details, plan list) are JSON-serializable to ASP.NET Core's default `System.Text.Json` without custom converters (SDK's `StringEnum<T>` has built-in support).

### Blockers
None — all required API operations are available in the SDK map. Two facts are unverified by the map but can only be confirmed by live traffic:
1. Whether Maxio's wire payload for a subscription's `State` field truly matches the `SubscriptionState` enum member names — SDK deserialization will fail if it does not.
2. Whether passing a duplicate `reference` on `CreateSubscription` returns 422 (rejected) or silently returns the existing subscription (idempotent). Implementation should handle 422 and treat as a signal to look up the existing subscription.

These are not blockers to planning; they are runtime facts the integration will discover and document.
