# Maxio Advanced Billing .NET SDK Integration Plan
## eShopOnWeb Subscription Billing

---

## Scope & Sequence

1. **Client & DI setup** — Register `MaxioAdvancedBillingClient` with `IHttpClientFactory`, wire credentials from config (Maxio:ApiKey, Maxio:Subdomain).
2. **Authentication** — Configure HTTP Basic auth (username = API key, password = literal `"x"`) from user-secrets.
3. **Customer idempotence** — Create/lookup customer by `reference` (user ID from JWT claim) to avoid duplicates.
4. **Product listing** — List subscription plans (Basic Plan handle: `basic-plan` $29/mo; Pro Plan handle: `eshop-pro` $299/mo) from Maxio.
5. **Subscription creation** — Enroll customer in selected plan (POST /subscriptions), with idempotent check.
6. **Subscription retrieval** — Read customer's subscriptions (GET /customers/{id}/subscriptions) and return to caller.
7. **Error boundary** — Catch `SdkException<T>` and structured/raw errors; map to appropriate HTTP responses per operation.
8. **Configuration & resilience** — Load credentials from env vars (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_ENVIRONMENT, MAXIO_DEFAULT_PRODUCT_FAMILY), store in .NET user-secrets under Maxio:* keys. Optional override BaseUrl (Maxio:BaseUrl).

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations Summary

| Controller | Method Signature | Request Model + Fields | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **Customers** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · `body` **must pass explicitly** | `CreateCustomerRequest` wraps `Customer (customer): CreateCustomer !req` · `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`, `CcEmails (cc_emails): string?` | `CustomerResponse` wraps `Customer (customer): Customer !req` · Response payload contains `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, plus many optional fields | **Case A typed** `SdkException<CreateCustomerError>` · Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| **Customers** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · `reference` **must pass explicitly** (query param) | Query: `reference` ← `reference` (wire name) | `CustomerResponse` wraps `Customer (customer): Customer !req` | **Case B raw** `SdkException<RawError>` · Accessors: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` | none | `operations/Customers.md` |
| **Customers** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path: `customerId` | `IReadOnlyList<SubscriptionResponse>` · Each wraps `Subscription (subscription): Subscription !req` | **Case B raw** `SdkException<RawError>` | none | `operations/Customers.md` |
| **Products** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · 8 params nullable no default → **must pass explicitly** (pass `null` to skip) · defaults: `page` = 1, `perPage` = 20 | Query params: `page`, `per_page`, `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `include_archived`, `include` | `IReadOnlyList<ProductResponse>` · Each wraps `Product (product): Product !req` · Response fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (enum: `Day (day)`, `Month (month)`), plus many optional fields | **Case B raw** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Products.md` |
| **Subscriptions** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · `body` **must pass explicitly** | `CreateSubscriptionRequest` wraps `Subscription (subscription): CreateSubscription !req` · `CreateSubscription` (key fields): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `Reference (reference): string?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`), plus many optional fields | `SubscriptionResponse` wraps `Subscription (subscription): Subscription !req` · Response: `Id (id): int?`, `State (state): SubscriptionState?` (enum), `BalanceInCents (balance_in_cents): long?`, `CustomerId (customer_id): int?` (implicitly from customer object), `ProductPriceInCents (product_price_in_cents): long?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, plus many optional fields | **Case A typed** `SdkException<CreateSubscriptionError>` · Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| **Subscriptions** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` · `include` **must pass explicitly** (query param) | Query: `include` ← `include` (enum: `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)`) | `SubscriptionResponse` wraps `Subscription (subscription): Subscription !req` | **Case B raw** `SdkException<RawError>` | none | `operations/Subscriptions.md` |

### Enum Values Used

| Enum | Values | Source |
|---|---|---|
| `IntervalUnit` (namespace `MaxioAdvancedBilling.Models.Enums`) | `Day (day)`, `Month (month)` | `models/enums.md` |
| `CollectionMethod` (namespace `MaxioAdvancedBilling.Models.Enums`) | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `models/enums.md` |
| `SubscriptionState` (namespace `MaxioAdvancedBilling.Models.Enums`) | `Pending (pending)`, `Active (active)`, `TrialEnded (trial_ended)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, others | `models/enums.md` |
| `SubscriptionInclude` (namespace `MaxioAdvancedBilling.Models.Enums`) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `models/enums.md` |
| `BasicDateField` (namespace `MaxioAdvancedBilling.Models.Enums`) | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `models/enums.md` |

### Client Construction & Auth

- **Client class**: `MaxioAdvancedBilling.MaxioAdvancedBillingClient`
- **Options class**: `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`
- **Auth** (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`): HTTP **Basic** — `BasicAuthCredentials { Username = "<api_key>", Password = "x" }`
- **Environment** (namespace `MaxioAdvancedBilling.Servers`): `ServerEnvironment.Us` (default) or `ServerEnvironment.Eu`; binds to base-URL template `https://{site}.chargify.com` (Production group)
- **Server override** (for sandbox site cp-exp-1 or custom host): `options.Server.Production.Us.Site = "cp-exp-1"` or `options.Server.Production.Us.BaseUrl = "http://custom-host"`
- **Namespaces** for all required `using` directives:
  - `MaxioAdvancedBilling` — root (client, options)
  - `MaxioAdvancedBilling.Api` — operation controllers
  - `MaxioAdvancedBilling.Models` — records (CreateCustomer, CustomerAttributes, CreateSubscription, etc.)
  - `MaxioAdvancedBilling.Models.Enums` — enums (IntervalUnit, CollectionMethod, SubscriptionState, SubscriptionInclude, BasicDateField)
  - `MaxioAdvancedBilling.Core.Authentication.Basic` — auth (BasicAuthCredentials)
  - `MaxioAdvancedBilling.Servers` — ServerEnvironment
  - `MaxioAdvancedBilling.Errors` — error types (CreateCustomerError, CreateSubscriptionError)
  - `MaxioAdvancedBilling.Core.ErrorResponse` — RawError

---

## Trap Notes

⚠ **Step 1 (client & DI setup)** — The `HttpClient` constructor argument must be a long-lived, reused instance from `IHttpClientFactory`, not a new instance per request. Do not rebuild the SDK client per call; make it transient or reuse it via DI. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (authentication)** — HTTP Basic auth credentials (API key as username, literal `"x"` as password) must be set on `options.BasicAuth` **before** constructing the client or in the DI callback. Load the API key from configuration (Maxio:ApiKey binding key), never hardcode it. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 3 (customer idempotence)** — Use `ReadCustomerByReference(reference, ct)` to check if a customer already exists (by user ID); only call `CreateCustomer` if not found. The `reference` field is the idempotence key (set it to the user's JWT subject or ID). **MUST load `dotnet-calling-endpoints`** for named-argument usage on nullable params.

⚠ **Step 4 (product listing)** — `ListProducts` has 8 nullable parameters (`dateField`, `filter`, etc.) with no C# default; pass `null` explicitly for filters you skip. Iterate results with manual pagination (page, perPage). Product handles (eshop-pro, basic-plan) come from wire response; match them in your lookup. **MUST load `dotnet-calling-endpoints`**.

⚠ **Step 5 (subscription creation)** — `CreateSubscription` accepts either `ProductHandle` or `ProductId` and either `CustomerId` or `CustomerReference`. To create a subscription idempotently on re-call, either:
  - Set `Reference` on the subscription request (lookup by reference before creating), or
  - Accept that a re-call will fail with a 422 if the customer has an active subscription for that product.
  
  The map Notes say "Identify an existing customer with `customer_id` or `customer_reference`." Use `customer_id` (the Maxio ID returned from customer create). **MUST load `dotnet-calling-endpoints`**.

⚠ **Step 6 (subscription retrieval)** — `ListCustomerSubscriptions` returns an array; cast the response to `IReadOnlyList<SubscriptionResponse>` and extract the `Subscription` payload from each response envelope. `ReadSubscription` returns a single `SubscriptionResponse`. Both are Case B (raw errors), so no typed accessor tree. **MUST load `dotnet-calling-endpoints`** and **`dotnet-error-handling`**.

⚠ **Step 7 (error boundary)** — **Two JsonException hazards arrive at the boundary; they need opposite handling:**
  - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
  - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  Typed error accessors (Case A: `TryGetCustomerErrorResponse1(out …)` [422], etc.) return structured error payloads; Case B raw errors expose status code and unparsed body. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ **Step 8 (configuration & resilience)** — Retry/timeout options (`RetryOptions`) are per-attempt, not per-call; `MaxRetries` floor is 1 (cannot disable retries). A **transport failure** (`HttpRequestException`) is retried on **every** HTTP verb, including `POST`, so a non-idempotent subscription create can execute more than once — customer idempotence (via reference lookup) is your only defense. `HttpMethodsToRetry` gates only the **status** trigger (e.g., 503), not transport failure. `Timeout` is per-attempt. Load configuration keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional override). Never commit credential values; use user-secrets in dev and secure storage in production. **MUST load `dotnet-configuration-resilience`** before setting retry/timeout or base-URL overrides.

---

## REQUIRED READING

Load these skills **before implementation starts**. The sheet deliberately does not carry their contents; they are the source for best practices, worked examples, and trap resolution:

- **`dotnet-client-initialization`** — Step 1 (client & DI setup): HttpClient lifecycle, transient vs. singleton client wrapper, DI registration.
- **`dotnet-authentication`** — Step 2 (auth): setting Basic auth credentials, config loading, credential rotation.
- **`dotnet-calling-endpoints`** — Steps 3–6 (all operations): named arguments on nullable params, request/response envelope unwrapping, async/await, cancellation.
- **`dotnet-error-handling`** — Step 7 (error boundary): Case A vs. Case B error handling, TryGet accessors, JsonException hazards (2xx malformed, non-2xx mismatch), boundary design patterns.
- **`dotnet-configuration-resilience`** — Step 8 (config & resilience): RetryOptions, Timeout semantics, per-attempt vs. total, transport-failure retries on POST, idempotence strategy.

---

## Assumptions & Blockers

### Assumptions

1. **Customer identity mapping**: The application has a JWT-authenticated user (subject claim or ID) that will be stored in Maxio's `customer.reference` field for idempotent lookup.
2. **Product setup in Maxio**: Two plans (handles: `basic-plan` $29/mo, `eshop-pro` $299/mo) already exist in the Maxio site `cp-exp-1` under the product family named in `MAXIO_DEFAULT_PRODUCT_FAMILY`.
3. **No payment method required on signup**: Subscriptions will be created without passing payment profile details; if Maxio requires a payment method for the product, this integration will fail at subscription creation (deferred to implementation if payment is needed).
4. **HTTP 200–201 on success, 4xx on validation, 5xx on server error**: Standard REST semantics apply; retries are handled by Polly per retry config.
5. **Sandbox testing mode**: The target site (`cp-exp-1`) is a Maxio sandbox; no real charges will be processed. Test/bogus payment vaults are configured at the Maxio site level.

### Blockers

None currently. The SDK map covers all operations in scope; the sandbox site and product family are assumed to exist. If either is missing, the integration will fail at runtime with a 404 or 422 from Maxio.

---

**File**: `C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-057\repo\maxio-plan.md`

**Summary**: This plan integrates the Maxio Advanced Billing .NET SDK into eShopOnWeb's PublicApi to enable recurring subscription billing. It covers customer creation (idempotent via reference), product listing, and subscription enrollment/retrieval across five operations (CreateCustomer, ReadCustomerByReference, ListCustomerSubscriptions, ListProducts, CreateSubscription, ReadSubscription). All signatures, request/response envelopes, error types, and enum values are sourced from the SDK map. The error boundary must handle two JsonException hazards and implement Case A/B error parsing per operation. Five companion skills must be loaded before implementation to resolve auth, DI, resilience, and error-boundary traps.
