# Maxio Subscription Billing Integration Plan — eShopOnWeb

## Scope & sequence

1. **Fetch available subscription plans** via `ReadProductByHandle` for "eshop-pro" and "basic-plan" → expose on `GET /api/subscription-plans`.
2. **Idempotent customer creation** via `ReadCustomerByReference` (lookup); if exists, use existing customer ID; else `CreateCustomer` → caller supplies user email.
3. **Subscription enrollment** via `CreateSubscription` → link customer ID + product handle + product price point → capture and return subscription ID + state.
4. **List user subscriptions** via `ListCustomerSubscriptions` filtered by customer reference → expose on `GET /api/my-subscriptions`.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Step 1 — List Plans (ReadProductByHandle)

| Aspect | Detail |
|--------|--------|
| **Operation** | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| **Controller** | `Products` |
| **HTTP** | GET `/products/handle/{api_handle}.json` |
| **Parameters** | `apiHandle` (wire: `api_handle`) — product handle string, e.g. `"eshop-pro"` |
| **Returns** | `MaxioAdvancedBilling.Models.ProductResponse` with envelope field `Product (product): MaxioAdvancedBilling.Models.Product !req` |
| **Response envelope** | Extract `response.Product` → contains `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, and all other product fields listed on `records-3-Of-Su.md`. Key billing fields: `PriceInCents`, `Interval`, `IntervalUnit`. |
| **Error** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B (raw)** |
| **Error accessors** | `StatusCode: System.Net.HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` |
| **No-throw variant** | absent |
| **Pagination** | none |
| **Source** | `operations/Products.md` |

### Step 2 — Idempotent Customer Lookup (ReadCustomerByReference)

| Aspect | Detail |
|--------|--------|
| **Operation** | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Controller** | `Customers` |
| **HTTP** | GET `/customers/lookup.json?reference={reference}` |
| **Parameters** | `reference` (wire: `reference`) — unique customer reference, e.g. user's ID from JWT claim |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` with envelope field `Customer (customer): MaxioAdvancedBilling.Models.Customer?` |
| **Response envelope** | Extract `response.Customer` → contains `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, and all other customer fields on `records-2-Cr-Ne.md`. |
| **Error** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B (raw)** |
| **Error accessors** | `StatusCode: System.Net.HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` |
| **No-throw variant** | absent |
| **Pagination** | none |
| **Idempotency note** | If the call returns 404 (customer not found), that is a valid signal to proceed to `CreateCustomer`. Catch `SdkException<RawError>` and check `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound` to detect the "not exists" case; do not parse the body string. |
| **Source** | `operations/Customers.md` |

### Step 3 — Create Customer (CreateCustomer)

| Aspect | Detail |
|--------|--------|
| **Operation** | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Controller** | `Customers` |
| **HTTP** | POST `/customers.json` |
| **Request body type** | `MaxioAdvancedBilling.Models.CreateCustomerRequest` with required envelope field `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req` |
| **Request model** | `MaxioAdvancedBilling.Models.CreateCustomer` — inner model for the envelope; fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`. **Required**: `FirstName`, `LastName`, `Email`. All others optional. |
| **Required fields to provide** | `FirstName`, `LastName`, `Email`; strongly recommended: `Reference` (set to user's identity from JWT for idempotency on future lookups). |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` with envelope field `Customer (customer): MaxioAdvancedBilling.Models.Customer !req` |
| **Response envelope** | Extract `response.Customer` → contains `Id (id): int?` (Maxio customer ID — use for subsequent subscription calls), plus all customer fields. |
| **Error** | `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] |
| **Error payload (422)** | `CustomerErrorResponse1` with field `Errors (errors): MaxioAdvancedBilling.Models.Errors?` — extract error details for validation feedback. |
| **No-throw variant** | absent |
| **Pagination** | none |
| **Source** | `operations/Customers.md`; request/response models on `records-1-Ac-Cr.md` and `records-2-Cr-Ne.md` |

### Step 4 — Create Subscription (CreateSubscription)

| Aspect | Detail |
|--------|--------|
| **Operation** | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Controller** | `Subscriptions` |
| **HTTP** | POST `/subscriptions.json` |
| **Request body type** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` with required envelope field `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req` |
| **Request model** | `MaxioAdvancedBilling.Models.CreateSubscription` — inner model; key fields: `CustomerId (customer_id): int?`, `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `Reference (reference): string?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?`, `Components (components): IReadOnlyList<MaxioAdvancedBilling.Models.CreateSubscriptionComponent>?`, `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `NetTerms (net_terms): string?`, `DeferSignup (defer_signup): bool? = false`, and many others on `records-2-Cr-Ne.md`. |
| **Required fields to provide** | At minimum: **one of** `CustomerId` (existing customer ID from step 3) **or** `CustomerReference` (user reference for idempotent lookup). **One of** `ProductHandle` (e.g., `"eshop-pro"`) **or** `ProductId` (Maxio numeric ID). Per the **Notes** on the operation page: "no trial, no setup fee, no payment method required" for the seeded plans means you **need not supply payment profile** — the spec allows nil payment profile when the product permits it. Confirm this on your site by checking if the product/plan requires payment upfront; if not, omit payment profile. `Reference` is recommended (set to a user-subscription pair identifier for future idempotency checks). |
| **Optional fields to consider** | `Reference` (for your own idempotent key), `CouponCode` or `CouponCodes` (if user has coupons), `Components` (for metered components like `api-call` with quantity). |
| **Skipped fields (left nil per scope)** | `PaymentCollectionMethod`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` (Maxio spec says no payment method required for these plans). |
| **Returns** | `MaxioAdvancedBilling.Models.SubscriptionResponse` with envelope field `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` |
| **Response envelope** | Extract `response.Subscription` → contains `Id (id): int?` (Maxio subscription ID), `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?` (e.g., `Active`, `Trialing`, `AwaitingSignup`), `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, and all other subscription fields on `records-3-Of-Su.md`. |
| **Error** | `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] |
| **Error payload (422)** | `ErrorListResponse1` with field `Errors (errors): IReadOnlyList<string> !req` — list of validation error messages. |
| **No-throw variant** | absent |
| **Pagination** | none |
| **Source** | `operations/Subscriptions.md`; request/response models on `records-2-Cr-Ne.md` and `records-3-Of-Su.md` |

### Step 5 — List Customer Subscriptions (ListCustomerSubscriptions)

| Aspect | Detail |
|--------|--------|
| **Operation** | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Controller** | `Customers` |
| **HTTP** | GET `/customers/{customer_id}/subscriptions.json` |
| **Parameters** | `customerId` (wire: `customer_id`) — Maxio numeric customer ID from step 3 or step 2 |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — **note: list is wrapped in envelope per SDK convention, but operation returns unwrapped list of `SubscriptionResponse` objects** |
| **Response model per item** | `MaxioAdvancedBilling.Models.SubscriptionResponse` with field `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` — extract each `response[i].Subscription` to get `Id`, `State`, `ProductPriceInCents`, etc. per subscription fields on `records-3-Of-Su.md`. |
| **Error** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B (raw)** |
| **Error accessors** | `StatusCode: System.Net.HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` |
| **No-throw variant** | absent |
| **Pagination** | none |
| **Source** | `operations/Customers.md` |

---

## Enums & Models

### Enum: IntervalUnit

From `map/models/enums.md`. Values used in product pricing:
- `Month` — monthly billing
- `Day` — daily
- `Week` — weekly
- `Year` — yearly

For eShopOnWeb seeded plans, both are monthly; wire value is `month`, C# member is `IntervalUnit.Month`.

### Enum: SubscriptionState

From `map/models/enums.md`. Subscription lifecycle states returned in responses:
- `Active` — subscription is active and billing
- `Trialing` — trial period (not applicable for our plans, but possible in API responses)
- `AwaitingSignup` — awaiting customer to complete signup
- `Cancelled` — subscription cancelled
- `PastDue` — payment past due (dunning state)
- `Suspended` — suspended

### Enum: CollectionMethod

From `map/models/enums.md`. Payment collection method (used optionally in CreateSubscription):
- `Automatic` — automatic collection (requires payment method)
- `Remittance` — send remittance (manual payment)
- `Prepaid` — prepaid

### Model: CollectionMethod enum values

Wire names and C# members:
- `"automatic"` ↔ `CollectionMethod.Automatic`
- `"remittance"` ↔ `CollectionMethod.Remittance`
- `"prepaid"` ↔ `CollectionMethod.Prepaid`

---

## Client Construction & Configuration

**Namespaces to add:**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using System.Net.Http;
```

**Client construction (per `sdk-map.md` and `dotnet-client-initialization`):**
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials
    {
        Username = apiKey,      // from Maxio:ApiKey configuration
        Password = "x"          // literal string "x"
    },
    Environment = ServerEnvironment.Us  // or .Eu per site hosting
};

// HttpClient must be long-lived and reused; do NOT create a new one per request.
// Use IHttpClientFactory or DI to get/manage the HttpClient.
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration binding (from environment variables):**
- `Maxio:ApiKey` ← reads from `MAXIO_API_KEY`
- `Maxio:Subdomain` ← reads from `MAXIO_SITE_SUBDOMAIN`
- `Maxio:ProductFamilyHandle` ← reads from `MAXIO_DEFAULT_PRODUCT_FAMILY`
- `Maxio:BaseUrl` (optional) ← `MAXIO_BASE_URL` (for override to localhost/mock)

Set `options.Server.Production.Us.Site = subdomain` if you need to override the site subdomain at runtime.

---

## Trap Notes

⚠ **Step 1 & 2 — Lookup calls return 404 (not found) on missing entities.** `ReadProductByHandle` and `ReadCustomerByReference` both return `SdkException<RawError>` with `StatusCode = NotFound` when the product handle or customer reference does not exist. Do **not** treat this as a failure; it is a **valid lookup signal**. Catch the exception, check `ex.Error.StatusCode`, and handle the 404 case as "create new" or "not found". Do **not** log this as an error. **MUST load `dotnet-error-handling`** before writing the boundary to understand Case B `RawError` accessors.

⚠ **Step 3 — Customer creation is **not** automatic when called via `CreateSubscription` with `customer_attributes`.** The operation map shows `CreateSubscription` accepts a nested `CustomerAttributes` (wire: `customer_attributes`) in the request body, which allows creating a customer and subscription in one call. **For this integration, use explicit two-step: lookup customer by reference, then create subscription with existing `customer_id`, or create customer first then subscription.** The explicit approach is more transparent for idempotency and aligns with the hero flow (user already exists in eShopOnWeb; you fetch or create in Maxio, then enroll). **MUST load `dotnet-calling-endpoints`** to understand how to build nested request bodies and when to pass nullable parameters explicitly vs. omit them.

⚠ **Step 3 — `CreateCustomer` email validation is strict; country code must be ISO 3166-1 alpha-2.** The operation **Notes** (not the signature) state: "Required Country Format: use ISO Standard Country codes when formatting country attribute (2 characters)." If you pass `Country` from user input, validate it is a 2-letter code before sending. Validation failures return 422 with error list. **MUST load `dotnet-error-handling`** to parse 422 payloads (Case A typed error `CreateCustomerError` → `TryGetCustomerErrorResponse1` accessor).

⚠ **Step 4 — `CreateSubscription` **no payment method required** is per the seeded product spec (the plans have `RequireCreditCard: false` in the Maxio backend). Omit `PaymentProfileAttributes`, `CreditCardAttributes`, and `BankAccountAttributes` entirely.** Do **not** pass them as null; leave them unset in the `CreateSubscription` record initializer. If the backend requires a payment method later (e.g., on a different product), the 422 error will list the validation failure. **MUST load `dotnet-models`** to understand record initialization and how to build union fields (if you later add component pricing).

⚠ **Step 5 — Idempotency on subscription creation is NOT automatic.** The SDK has no built-in idempotency key or deduplication on `CreateSubscription`. If you call it twice with the same parameters, two subscriptions are created. **Strategy: set `Reference` (wire: `reference`) to a composite key (e.g., `"{userId}_{productHandle}_{timestamp}"`) and query `ListCustomerSubscriptions` to detect an existing active subscription for that product before creating.** Or use a database lock at the application boundary. **MUST load `dotnet-error-handling`** and **`dotnet-configuration-resilience`** to understand retry semantics (a transport failure on a POST will be retried by default, and non-idempotent writes can execute multiple times if not guarded by application logic).

⚠ **Both Customers and Subscriptions: `Reference` field is your own, not Maxio's.** `CreateCustomer` and `CreateSubscription` both accept a `Reference` (string, optional) — this is a field **you populate** to store a unique identifier from your application (e.g., eShopOnWeb's user ID or subscription entity ID). Maxio returns it in the response but does not generate it. Use this field to implement idempotent lookups (`ReadCustomerByReference`, `FindSubscription` on wire name `reference`). **Never rely on email or name alone for idempotency; emails can change and names are not unique.** **MUST load `dotnet-calling-endpoints`** to understand how reference is wired in query parameters vs. body fields.

⚠ **System.Text.Json deserialization failures from malformed 2xx bodies escape as `JsonException`.** If the Maxio API response is a 2xx status but the JSON body is malformed or missing a `required` field (e.g., `Subscription` is null in `SubscriptionResponse`), the SDK's JSON deserialization throws `System.Text.Json.JsonException`, **not** `SdkException`. This `JsonException` is **not caught by an `SdkException<T>` catch block** and will propagate past your integration boundary. A boundary that assumes `SdkException<T>` is the only error class will let this escape. **MUST load `dotnet-error-handling`** before writing the boundary to understand both the signed (`SdkException<T>`) and unsigned (`JsonException`) error paths.

⚠ **System.Text.Json deserialization failures from non-2xx bodies corrupt the HTTP status.** If a 422 or 5xx response body does not match the generated `{Operation}Error` shape, the SDK's error object construction throws `JsonException` **while building the exception**, so the `SdkException<T>` is never created and the `JsonException` **replaces it** — and the HTTP status code is lost. A boundary that maps every `JsonException` to a 5xx without checking the response body will report a validation error (422 from the provider) as an outage (5xx), and a caller that retries 5xx will retry forever on a condition that can never succeed. **MUST load `dotnet-error-handling`** before writing the error boundary to learn how to detect this case (e.g., check `ex.InnerException` or the response content before throwing a generic 5xx).

---

## REQUIRED READING

Load **before implementation starts.** These companion skills are mandatory; the sheet deliberately does not carry their contents because each is large and carries parts a one-line note cannot.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 0: How to construct the `MaxioAdvancedBillingClient`, wire the `HttpClient` to DI, and avoid the client-per-request trap. |
| `dotnet-authentication` | Step 0: Setting `BasicAuth` credentials (username = API key, password = `"x"`), reading them from configuration, and rotating or refreshing them. |
| `dotnet-calling-endpoints` | Steps 1–5: Calling operations, passing nullable parameters explicitly, and building request bodies. |
| `dotnet-models` | Steps 2–4: Record initialization (which fields are `required`), how to build union types (none in this plan, but may appear in component pricing), and reading enums from wire values. |
| `dotnet-error-handling` | Steps 2–5: Catching and parsing errors — understand Case A typed errors (e.g., `CreateCustomerError` with `TryGet…` accessors) vs. Case B raw errors, and how `JsonException` bypasses the `SdkException<T>` boundary. **Especially critical:** handling 404 from lookups and 422 validation errors. Both the signed (`SdkException<T>`) and unsigned (`JsonException`) error paths must be understood before writing the boundary. |
| `dotnet-configuration-resilience` | Steps 3–5: Retry semantics (transport failures on POST are retried; idempotent keys and application-level guards are your responsibility) and whether `Timeout` is per-attempt or total (it is per-attempt). |

**All six companion skills must be loaded before the first line of SDK integration code is written.** The contract sheet alone is not sufficient; each skill carries patterns, defaults, and worked examples that the signatures do not reveal.

---

## Assumptions & Blockers

### Assumptions

1. **Maxio site and plans are already seeded on `cp-exp-3`.** The plans (`eshop-pro` with ID 7126957, `basic-plan` with ID 7126958) exist and are accessible by handle. The product family `eshop-subscribe` (ID 3023074) exists. The integration does not create these; it only reads and uses them.

2. **Credentials are injected at runtime from configuration.** The implementation reads `Maxio:ApiKey` and `Maxio:Subdomain` from `IOptions<MaxioSettings>` or similar; no credentials are hardcoded or stored in the repository.

3. **JWT authentication on the PublicApi endpoints is already in place.** The `GET /api/subscription-plans`, `POST /api/subscriptions`, and `GET /api/my-subscriptions` endpoints receive a JWT token in the `Authorization: Bearer` header, and the integration extracts the user identity (ID, email, name) from that token's claims. The implementation trusts the token and does not re-validate credentials.

4. **Database row-level idempotency is sufficient.** The application does not require Maxio-side idempotency keys (e.g., X-Idempotency-Key headers); instead, the integration uses `Reference` field in Maxio and application-level checks (`ReadCustomerByReference` before `CreateCustomer`, `ListCustomerSubscriptions` before `CreateSubscription`) to avoid duplicates.

5. **User identity is stable.** The JWT claim used as the customer `Reference` (e.g., `sub` or `user_id`) does not change over the user's lifetime. If identity can be reassigned or rotated, idempotency lookups break.

6. **No metered component usage tracking is in scope for the hero flow.** The `api-call` component (ID 3057195) is seeded but not allocated in this plan. The hero flow creates a subscription to a plan; metered usage is a later feature.

### Blockers

None identified. All Maxio operations and models required for the hero flow are present in the SDK map.

---

**Document version:** 1.0  
**Generated:** 2026-09-07  
**SDK version pinned:** v1.0.2 (commit `15db14b`)
