# Maxio Subscription Billing — eShopOnWeb Integration Plan

## Scope & Sequence

1. **List Subscription Plans** — `client.Products.ListProducts()` to retrieve all plans in the product family `eshop-subscribe`
2. **Idempotent Customer Lookup/Create** — `client.Customers.ReadCustomerByReference()` to find or `CreateCustomer()` to create a Maxio customer linked to the eShopOnWeb user by reference ID
3. **Create Subscription** — `client.Subscriptions.CreateSubscription()` to enroll the customer in a plan
4. **List Customer Subscriptions** — `client.Customers.ListCustomerSubscriptions()` to retrieve all subscriptions for a customer
5. **Expose HTTP Endpoints** — implement Controller actions under `/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions` with JWT authentication

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 1. List Subscription Plans

| Aspect | Details |
|--------|---------|
| **Operation** | `client.Products.ListProducts(…)` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Required Params (must pass explicitly)** | 8 nullable params: `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include` (pass `null` to skip); defaults: `page=1`, `perPage=20` |
| **Request Model** | None (query params only) |
| **Response Envelope** | `IReadOnlyList<ProductResponse>` — array of envelopes; each envelope wraps: `ProductResponse.Product: Product !req` |
| **Response Fields (extract from `Product`)** | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` |
| **Error Case** | `SdkException<RawError>` — **Case B** |
| **Error Accessors** | `StatusCode: HttpStatusCode`, `ReadAsBytes(): ReadOnlyMemory<byte>`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` |
| **Pagination** | Manual via `page`, `perPage` query params |
| **Notes** | Filter by product family handle via `filter` (construct `ListProductsFilter`, no required fields shown on map); to list plans in `eshop-subscribe`, either pass the family ID or paginate all products and filter in-app. The Notes entry does **not** state whether filtering by family is possible via this endpoint — verify filter structure in companion `dotnet-calling-endpoints` skill or source. |
| **Source** | `map/operations/Products.md` (ReadProduct) |

### 2. Idempotent Customer Lookup/Create

#### 2a. Read Customer by Reference (lookup only)

| Aspect | Details |
|--------|---------|
| **Operation** | `client.Customers.ReadCustomerByReference(…)` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Required Params** | `reference` (query param, wire name: `reference`) |
| **Request Model** | None (query param only) |
| **Response Envelope** | `CustomerResponse` wraps: `CustomerResponse.Customer: Customer !req` |
| **Response Fields (extract from `Customer`)** | `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` |
| **Error Case** | `SdkException<RawError>` — **Case B** |
| **Error Accessors** | `StatusCode: HttpStatusCode`, `ReadAsString(): string` (check for 404 if customer not found) |
| **Notes** | "Returns a customer by their unique reference ID. It will return a single match." — use eShopOnWeb user ID as the `reference` value for idempotency. **404 indicates customer does not exist; do not treat as error in idempotent flow.** |
| **Source** | `map/operations/Customers.md` |

#### 2b. Create Customer

| Aspect | Details |
|--------|---------|
| **Operation** | `client.Customers.CreateCustomer(…)` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Required Params** | `body` (must pass explicitly; nullable but no default) |
| **Request Model** | `CreateCustomerRequest` wraps: `CreateCustomerRequest.Customer: CreateCustomer !req` |
| **Customer Fields (required/optional)** | `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`, `CcEmails (cc_emails): string?` |
| **Response Envelope** | `CustomerResponse` wraps: `CustomerResponse.Customer: Customer !req` |
| **Error Case** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error Accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1) [422]`, `TryGetRawError(out RawError) [fallback]` |
| **Notes** | "The only validation restriction is that you may only create one customer for a given reference value. If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app." — set `Reference` to eShopOnWeb user ID. Required fields: `FirstName`, `LastName`, `Email`. |
| **Source** | `map/operations/Customers.md` |

### 3. Create Subscription

| Aspect | Details |
|--------|---------|
| **Operation** | `client.Subscriptions.CreateSubscription(…)` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Required Params** | `body` (must pass explicitly; nullable but no default) |
| **Request Model** | `CreateSubscriptionRequest` wraps: `CreateSubscriptionRequest.Subscription: CreateSubscription !req` |
| **Subscription Fields (extract critical set only; many optional)** | **Customer link:** `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`; **Product/Price:** `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`; **Optional common fields:** `Reference (reference): string?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false` |
| **Response Envelope** | `SubscriptionResponse` wraps: `SubscriptionResponse.Subscription: Subscription?` |
| **Response Fields (extract from `Subscription`)** | `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `Reference (reference): string?`, `Customer (customer): Customer?`, `Product (product): Product?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?` |
| **Error Case** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error Accessors** | `TryGetErrorListResponse1(out ErrorListResponse1) [422]`, `TryGetRawError(out RawError) [fallback]` |
| **Notes** | "Specify the product with `product_id` or `product_handle`. To set a specific product price point, use `product_price_point_handle` or `product_price_point_id`. Identify an existing customer with `customer_id` or `customer_reference`." — pass at least one of each pair. "Payment information may be required to create a subscription, depending on the options for the Product being subscribed." — sandbox plans (`eshop-pro`, `basic-plan`) have **no payment method required** per spec, so omit payment attributes. Set `Reference` to eShopOnWeb subscription ID for tracking. |
| **Source** | `map/operations/Subscriptions.md` |

### 4. List Customer Subscriptions

| Aspect | Details |
|--------|---------|
| **Operation** | `client.Customers.ListCustomerSubscriptions(…)` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Required Params** | `customerId` (path param) |
| **Request Model** | None (path param only) |
| **Response Envelope** | `IReadOnlyList<SubscriptionResponse>` — array of envelopes; each wraps: `SubscriptionResponse.Subscription: Subscription?` |
| **Response Fields (from `Subscription`, same as operation 3)** | `Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `CreatedAt`, `Reference`, `Customer`, `Product`, `CouponCodes` |
| **Error Case** | `SdkException<RawError>` — **Case B** |
| **Error Accessors** | `StatusCode: HttpStatusCode`, `ReadAsString(): string` |
| **Notes** | "Lists all subscriptions that belong to a customer." — straightforward; no pagination params in signature. |
| **Source** | `map/operations/Customers.md` |

---

## Enums & Key Types

### `SubscriptionState` (wire values — from enums.md reference)
Used in `Subscription.State` field. Common states include (from operation Notes context):
- `active` — subscription is active
- `past_due` — payment failed, dunning in progress
- `pending` — awaiting signup
- `canceled` — subscription has been canceled
- **Exact enum member names must be read from `map/models/enums.md`** — never construct from memory.

### `IntervalUnit` (wire values)
Used in `Product.IntervalUnit` and `CreateSubscription` custom pricing. Common values: `day`, `month`, `year`. 
**Exact enum member names must be read from `map/models/enums.md`.**

### `CollectionMethod` (wire values)
Used in `CreateSubscription.PaymentCollectionMethod`. Values include `automatic`, `remittance`, `prepaid`.
**Exact enum member names must be read from `map/models/enums.md`.**

---

## Namespaces (using directives required)

| Content | Namespace |
|---------|-----------|
| Client & options | `MaxioAdvancedBilling` |
| Operations (API groups) | `MaxioAdvancedBilling.Api` |
| Request/response models | `MaxioAdvancedBilling.Models` |
| Enums | `MaxioAdvancedBilling.Models.Enums` |
| Error classes | `MaxioAdvancedBilling.Errors` |

---

## Client Construction & Authentication

**Basic Auth setup** (from SDK map):
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = "<api_key>",    // Maxio API key from config
        Password = "x"             // Literal "x"
    },
    Environment = ServerEnvironment.Us  // or ServerEnvironment.Eu
};

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration keys** (per task):
- `Maxio:ApiKey` — API key from `MAXIO_API_KEY` env var
- `Maxio:Subdomain` — site subdomain from `MAXIO_SITE_SUBDOMAIN` env var (used to build base URL)
- `Maxio:ProductFamilyHandle` — handle `eshop-subscribe` from `MAXIO_DEFAULT_PRODUCT_FAMILY` env var
- `Maxio:BaseUrl` — optional override (default: `https://{Subdomain}.chargify.com`)

Load all from environment variables into user-secrets; never commit to repository.

---

## Trap Notes

⚠ **Step 1 (client registration)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 (customer lookup, create)** — when a customer reference is not found (404 on `ReadCustomerByReference`), the SDK throws `SdkException<RawError>` with `StatusCode = 404`; do not treat as an error in idempotent flow — catch and create instead. **MUST load `dotnet-error-handling`** for the correct pattern.

⚠ **Step 3 (create subscription)** — the request body has many optional fields, but the Notes entry names the fields that determine acceptance: at minimum pass `CustomerId` (or `CustomerReference`), one of `ProductId` or `ProductHandle`, and optionally a price point. Payment profile fields are **not required** for sandbox plans per the spec; omit them to keep the request lean. **MUST load `dotnet-calling-endpoints`** for named-argument pitfalls and `dotnet-models`** for union/enum handling if custom pricing is used.

⚠ **Step 5 (HTTP endpoints)** — every endpoint writes a response that reflects subscription state back to the user. The response envelope (e.g. `SubscriptionResponse.Subscription`) wraps the payload **one level deep** — always read `.Subscription` or `.Product` or `.Customer`, never the envelope itself. **MUST load `dotnet-models`** for the extraction pattern.

⚠ **JSON deserialization mismatch** — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

---

## REQUIRED READING

Before implementation starts, load the following companion skills — they are not optional; the contract sheet deliberately does not carry their contents because a signature link cannot teach concurrency, default behavior, or the failure modes a type signature hides:

| Skill | Step(s) | Purpose |
|-------|---------|---------|
| `dotnet-client-initialization` | Client DI registration | HttpClient lifecycle, transient vs long-lived, how the SDK wraps the client |
| `dotnet-authentication` | Credentials setup (step 1 client construction) | How to set Basic auth, where to load the API key, rotating credentials |
| `dotnet-calling-endpoints` | Every SDK call (steps 1–4) | Named-argument patterns, optional params that have no C# default, error on positional binding |
| `dotnet-models` | Request/response extraction (steps 2–4) | Union construction + `TryGet…` readers, `StringEnum<T>` factories, how response envelopes wrap payloads |
| `dotnet-error-handling` | Error boundary (step 5) | Case A vs Case B dispatch, `TryGet…` accessor patterns, `JsonException` handling in the boundary |
| `dotnet-configuration-resilience` | Retry/timeout config (client setup) | What `Timeout` bounds, `HttpMethodsToRetry` gates only status not transport, `MaxRetries` floor is 1 |
| `dotnet-testing` | Unit test stubs (step 5, if applicable) | `HttpClient` constructor test seam, matching project style |

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb user ID is a string that can be stored as the Maxio `customer.reference` without conflict (unique per eShopOnWeb user).
- The sandbox site `cp-exp-1` is already seeded with the product family, products, and metered component as stated in the task.
- HTTP layer will handle 401/403 (missing/invalid API key) by rejecting at auth boundary before calling Maxio.

**Blockers:**
- None identified. All required operations are present in the SDK map.

