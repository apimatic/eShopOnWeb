# Maxio Advanced Billing Integration — eShopOnWeb

## Scope & Sequence

Three HTTP endpoints on the `src/PublicApi` project, using JWT auth (existing patterns).

| Step | Endpoint | SDK Operations Used |
|------|----------|-------------------|
| 1 | `GET /api/subscription-plans` | `client.Products.ListProductsForProductFamily` |
| 2 | `POST /api/subscriptions` | `client.Customers.ReadCustomerByReference` → `client.Customers.CreateCustomer` → `client.Subscriptions.CreateSubscription` |
| 3 | `GET /api/my-subscriptions` | `client.Customers.ReadCustomerByReference` → `client.Customers.ListCustomerSubscriptions` |

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation 1: ListProductsForProductFamily

| | |
|---|---|
| **Controller** | `client.Products` |
| **Signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Params** | `productFamilyId` — string, **must pass explicitly** (use handle `"eshop-subscribe"`). Remaining 8 nullable params — pass `null` to skip all. Defaults: `page=1`, `perPage=20` |
| **Returns** | `IReadOnlyList<ProductResponse>` |
| **Error** | `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)** |
| **Error accessors** | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | manual `page`+`perPage` |
| **Notes** | Retrieves a list of Products belonging to a Product Family. |

**Response envelope:** `ProductResponse` → one field `Product (product): Product !req`

**Key `Product` fields for plan display:**
- `Id (id): int?`
- `Name (name): string?`
- `Handle (handle): string?`
- `Description (description): string?`
- `PriceInCents (price_in_cents): long?`
- `Interval (interval): int?`
- `IntervalUnit (interval_unit): IntervalUnit?`
- `ProductFamily (product_family): ProductFamily?` — nested, has `Id`, `Name`, `Handle`
- `DefaultProductPricePointId (default_product_price_point_id): int?`
- `ProductPricePointName (product_price_point_name): string?`

**Source:** `operations/Products.md`, `records-3-Of-Su.md`

---

### Operation 2: ReadCustomerByReference

| | |
|---|---|
| **Controller** | `client.Customers` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Params** | `reference` — string, **must pass explicitly** (the user's eShopOnWeb ID) |
| **Returns** | `CustomerResponse` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| **Pagination** | none |
| **Notes** | Returns a customer by their unique reference ID. It will return a single match. |

**Response envelope:** `CustomerResponse` → one field `Customer (customer): Customer !req`

**Key `Customer` fields:**
- `Id (id): int?`
- `FirstName (first_name): string?`
- `LastName (last_name): string?`
- `Email (email): string?`
- `Reference (reference): string?`

**Source:** `operations/Customers.md`, `records-2-Cr-Ne.md`

---

### Operation 3: CreateCustomer

| | |
|---|---|
| **Controller** | `client.Customers` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Params** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `CustomerResponse` |
| **Error** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |
| **Notes** | Creates a new customer. The only validation restriction is that you may only create one customer for a given reference value. If provided, the `reference` value must be unique. |

**Request model:** `CreateCustomerRequest`
- `Customer (customer): CreateCustomer !req`

**`CreateCustomer` fields (all optional unless marked):**
- `FirstName (first_name): string !req`
- `LastName (last_name): string !req`
- `Email (email): string !req`
- `Reference (reference): string?` — **set this to the user's eShopOnWeb ID**
- `CcEmails (cc_emails): string?`
- `Organization (organization): string?`
- `Address (address): string?`
- `Address2 (address_2): string?`
- `City (city): string?`
- `State (state): string?`
- `Zip (zip): string?`
- `Country (country): string?`
- `Phone (phone): string?`
- `Locale (locale): string?`
- `VatNumber (vat_number): string?`
- `TaxExempt (tax_exempt): bool?`
- `TaxExemptReason (tax_exempt_reason): string?`
- `ParentId (parent_id): int?`
- `SalesforceId (salesforce_id): string?`

**Source:** `operations/Customers.md`, `records-1-Ac-Cr.md`

---

### Operation 4: CreateSubscription

| | |
|---|---|
| **Controller** | `client.Subscriptions` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Params** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `SubscriptionResponse` |
| **Error** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |
| **Notes** | Creates a Subscription for a customer and product. Specify the product with `product_id` or `product_handle`. Identify an existing customer with `customer_id` or `customer_reference`. Optionally, include an existing payment profile using `payment_profile_id`. To create a new customer, pass `customer_attributes`. |

**Request model:** `CreateSubscriptionRequest`
- `Subscription (subscription): CreateSubscription !req`

**`CreateSubscription` fields (all optional unless marked):**
- `ProductHandle (product_handle): string?` — **use this for plan handle** (e.g. `"eshop-pro"`)
- `ProductId (product_id): int?`
- `ProductPricePointHandle (product_price_point_handle): string?`
- `ProductPricePointId (product_price_point_id): int?`
- `CustomerId (customer_id): int?` — **set to the Maxio customer ID from step 2**
- `CustomerReference (customer_reference): string?` — alternative: set to the eShopOnWeb user ID
- `CustomerAttributes (customer_attributes): CustomerAttributes?` — for creating a new customer inline
- `Reference (reference): string?` — subscription reference
- `PaymentProfileId (payment_profile_id): int?`
- `CouponCode (coupon_code): string?`
- `CouponCodes (coupon_codes): IReadOnlyList<string>?`
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`
- `ReceivesInvoiceEmails (receives_invoice_emails): string?`
- `NetTerms (net_terms): string?`
- `NextBillingAt (next_billing_at): DateTimeOffset?`
- `InitialBillingAt (initial_billing_at): DateTimeOffset?`
- `DeferSignup (defer_signup): bool? = false`
- `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`
- `CalendarBilling (calendar_billing): CalendarBilling?`
- `Metafields (metafields): IReadOnlyDictionary<string, string>?`
- `Group (group): GroupSettings?`
- `Ref (ref): string?`
- `Currency (currency): string?`
- `ExpiresAt (expires_at): DateTimeOffset?`

**Response envelope:** `SubscriptionResponse` → one field `Subscription (subscription): Subscription?` (nullable)

**Key `Subscription` fields for display:**
- `Id (id): int?`
- `State (state): SubscriptionState?`
- `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`
- `NextAssessmentAt (next_assessment_at): DateTimeOffset?`
- `ActivatedAt (activated_at): DateTimeOffset?`
- `CreatedAt (created_at): DateTimeOffset?`
- `Product (product): Product?` — nested product info
- `Customer (customer): Customer?` — nested customer info
- `BalanceInCents (balance_in_cents): long?`
- `TotalRevenueInCents (total_revenue_in_cents): long?`

**Source:** `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`

---

### Operation 5: ListCustomerSubscriptions

| | |
|---|---|
| **Controller** | `client.Customers` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Params** | `customerId` — int, **must pass explicitly** |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| **Pagination** | none |
| **Notes** | Lists all subscriptions that belong to a customer. |

**Source:** `operations/Customers.md`

---

### Enum: SubscriptionState (for display filtering)

Namespace: `MaxioAdvancedBilling.Models.Enums`

Members: `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup`

**Source:** `enums.md`

---

### Enum: CollectionMethod (for subscription creation)

Namespace: `MaxioAdvancedBilling.Models.Enums`

Members: `Automatic`, `Remittance`, `Prepaid`, `Invoice`

**Source:** `enums.md`

---

### Client Construction & Auth

```csharp
// Basic auth: Username = API key, Password = literal "x"
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us, // or .Eu
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Required namespaces:**
- `MaxioAdvancedBilling` — client, options
- `MaxioAdvancedBilling.Core.Authentication.Basic` — `BasicAuthCredentials`
- `MaxioAdvancedBilling.Servers` — `ServerEnvironment`
- `MaxioAdvancedBilling.Models` — records
- `MaxioAdvancedBilling.Models.Enums` — enums
- `MaxioAdvancedBilling.Errors` — error types (for catch blocks)

**Source:** `sdk-map.md`

---

## Trap Notes

⚠ **Step 1 (list plans)** — `ListProductsForProductFamily` is **Case A** (typed error), not Case B. Its 404 accessor is `TryGetString(out string)` — not a standard error shape. If the product family handle doesn't match, you get a string error body, not a structured object. **MUST load `dotnet-error-handling`** before writing the catch boundary.

⚠ **Step 2 (create customer)** — `ReadCustomerByReference` is **Case B** (`SdkException<RawError>`), but `CreateCustomer` is **Case A** (`SdkException<CreateCustomerError>`). Two operations on the same controller, two different error cases. A catch ladder that assumes uniform error handling will silently swallow the wrong shape. **MUST load `dotnet-error-handling`** before writing the try/catch for this flow.

⚠ **Step 2 (idempotent customer creation)** — `ReadCustomerByReference` returns `CustomerResponse` on success. On 404 (customer not found), it throws `SdkException<RawError>` with `StatusCode == 404`. You must catch the exception, check the status code, and then call `CreateCustomer`. This is a control-flow catch, not an error — the SDK has no "not found" return value. **MUST load `dotnet-error-handling`** for the Case B accessor pattern.

⚠ **Step 2 (subscription creation)** — `CreateSubscription` accepts either `customer_id` (int) or `customer_reference` (string). Using `customer_reference` skips the need to look up the Maxio customer ID, but you still need the customer to exist. The plan uses `customer_id` after explicit lookup/create for clarity. **MUST load `dotnet-calling-endpoints`** before wiring the call — many optional params have no C# default and mis-bind positionally.

⚠ **Step 3 (list subscriptions)** — `ListCustomerSubscriptions` takes `customerId` (int), not a reference string. You must pass the Maxio customer ID obtained from step 2. **MUST load `dotnet-calling-endpoints`** for the parameter order.

⚠ **Client construction** — The SDK's `HttpClient`/handler pipeline must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request. The SDK client wrapper over it may be transient. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Auth credentials** — Set credentials before constructing the client or in the DI callback. Load the key from configuration rather than hardcoding. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Retry semantics** — `HttpMethodsToRetry` gates only the **status** trigger, so a `503` on a `POST` is not resent. But a transport failure (`HttpRequestException`) is retried on **every** verb, `POST` included, so a non-idempotent write can execute more than once and no setting disables that (`MaxRetries = 0` is rejected at construction; the floor is 1). `Timeout` is per-attempt not total. **MUST load `dotnet-configuration-resilience`** before tuning retries.

---

## REQUIRED READING

These skills must be loaded **before implementation starts**. The contract sheet deliberately does not carry their contents — it names the hazard and hands you the skill that resolves it.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 1 — client construction, `IHttpClientFactory`, DI registration |
| `dotnet-authentication` | Step 1 — HTTP Basic auth wiring, credential sourcing from config |
| `dotnet-calling-endpoints` | Steps 1–3 — calling SDK operations, named arguments, async usage |
| `dotnet-models` | Steps 1–3 — building request models, required members, nullability, enums as `StringEnum<T>` |
| `dotnet-error-handling` | Steps 1–3 — `SdkException<T>` catch ladders, Case A vs Case B, `TryGet…` accessors |
| `dotnet-configuration-resilience` | Step 1 — retry/timeout tuning, base URL override, pagination |

**Hazard rows (load `dotnet-error-handling` before writing the error boundary):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

1. **Sandbox product family `eshop-subscribe` is pre-created** in the Maxio sandbox with the products (handles `eshop-pro`, `basic-plan`) and the metered component (handle `api-call`). The plan does not create these entities — it reads them by handle.

2. **Payment method is not required** for subscription creation per the sandbox entity spec (`require_credit_card: false`). If the live site requires payment, `CreateSubscription` will throw a 422 — the error boundary will surface it, but the integration does not handle payment-profile collection.

3. **No trial period** on either plan. If trials are added later, `CreateSubscription` already supports `DeferSignup` and trial-related fields — no contract change needed.

4. **`ReadCustomerByReference` returns 404 as `SdkException<RawError>`** (Case B). The plan assumes the implementer will catch this, check `StatusCode == 404`, and treat it as "customer not found" to trigger creation. This is the standard control-flow pattern for this SDK.

5. **`ListCustomerSubscriptions` returns all subscriptions** for a customer with no state filter. If filtering by state is needed later, the `Subscription` model's `State` field provides the value for client-side filtering.

6. **Configuration keys** match the listed env vars/config keys. No override of `BaseUrl` is assumed unless `MAXIO_ENVIRONMENT` signals a non-standard host.

7. **The `src/PublicApi` project** uses the existing JWT-authentication pattern. Maxio SDK calls are additive and do not affect existing endpoints.
