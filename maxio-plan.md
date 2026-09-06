# Maxio Integration Plan — eShopOnWeb Subscription Feature

## Scope & Sequence

1. **Client registration & configuration** — Register the SDK client with DI, wire Basic auth (API key + "x"), and configure the subdomain from `Maxio:Subdomain`.
2. **GET /api/subscription-plans** — List available plans by calling `ListProducts`, filtering by product family via configuration.
3. **POST /api/subscriptions** — Create a subscription: idempotent customer creation/lookup via `CreateCustomer` + `ReadCustomerByReference`, then call `CreateSubscription` with plan handle/ID.
4. **GET /api/my-subscriptions** — Retrieve user's subscriptions by customer reference: lookup customer, then call `ListCustomerSubscriptions`.
5. **Error boundary** — Catch `SdkException<…>` with typed and raw error accessors, and map `JsonException` from deserialization failures separately.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **Create Customer (idempotent)** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` <br/> (all params required; pass body explicitly) | `CreateCustomerRequest` wraps `CreateCustomer !req` → `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse` wraps `Customer ?` → read `.Customer` field; contains: `Id (id): int?`, `Email (email): string?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case A:** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| **Lookup Customer by Reference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` <br/> (reference required, passed explicitly) | **Query param:** `reference` (wire: `reference`) | `CustomerResponse` wraps `Customer ?` → read `.Customer` field; same shape as Create response | **Case B:** `SdkException<RawError>` → `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | none | `operations/Customers.md` |
| **List Products** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` <br/> (8 params before pagination; pass `null` to skip; defaults: page=1, perPage=20) | **Query params (optional):** `filter` (wire: `filter`), `page` (wire: `page`), `per_page` (wire: `per_page`), others omitted for scope | Returns `IReadOnlyList<ProductResponse>` → each wraps `Product !req` → read `.Product` field; contains: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `Description (description): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case B:** `SdkException<RawError>` → `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | Manual: `page` + `perPage` | `operations/Products.md`, `records-3-Of-Su.md` |
| **Create Subscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` <br/> (body required, pass explicitly) | `CreateSubscriptionRequest` wraps `CreateSubscription !req` → `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Reference (reference): string?`, `DeferSignup (defer_signup): bool? = false`, and ~30 optional fields (most unused for basic subscription); key fields: `ProductHandle` (wire: `product_handle`) OR `ProductId` (wire: `product_id`), `CustomerId` (wire: `customer_id`) OR `CustomerReference` (wire: `customer_reference`), `Reference` (wire: `reference`) for idempotent lookup | `SubscriptionResponse` wraps `Subscription ?` → read `.Subscription` field; contains: `Id (id): int?`, `State (state): SubscriptionState?`, `Customer (customer): Customer?`, `Product (product): Product?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CouponCode (coupon_code): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `ReceivesInvoiceEmails (receives_invoice_emails): bool?` | **Case A:** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| **Find Subscription by Reference** | `FindSubscription(string? reference, CancellationToken ct = default)` <br/> (reference required, passed explicitly) | **Query param:** `reference` (wire: `reference`) | `SubscriptionResponse` wraps `Subscription ?` → read `.Subscription` field; same shape as Create response | **Case A:** `SdkException<FindSubscriptionError>` → `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| **Read Subscription** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` <br/> (subscriptionId required, include optional) | **Path param:** `subscription_id` (wire: `subscription_id`); **Query param:** `include` (wire: `include`, optional) | `SubscriptionResponse` wraps `Subscription ?` → read `.Subscription` field; same shape as above | **Case B:** `SdkException<RawError>` → `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | none | `operations/Subscriptions.md` |
| **List Customer Subscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` <br/> (customerId required) | **Path param:** `customer_id` (wire: `customer_id`) | Returns `IReadOnlyList<SubscriptionResponse>` → each wraps `Subscription ?` → read `.Subscription` field; same shape as above | **Case B:** `SdkException<RawError>` → `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | none | `operations/Customers.md` |

### Enum Values (wire format in parentheses)

**`SubscriptionState`** (wire values for filter/check): `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `PastDue (past_due)`, `Trialing (trialing)`, `Unpaid (unpaid)`, `Suspended (suspended)`, `OnHold (on_hold)`, `Paused (paused)`, `TrialEnded (trial_ended)`, `AwaitingSignup (awaiting_signup)`, `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Assessing (assessing)`, `SoftFailure (soft_failure)`.

**`CollectionMethod`** (wire values): `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`.

### Client Registration & Configuration

- **Client class:** `MaxioAdvancedBillingClient` (namespace: `MaxioAdvancedBilling`)
- **Options class:** `MaxioAdvancedBillingClientOptions` (namespace: `MaxioAdvancedBilling`)
- **Auth:** HTTP Basic — `BasicAuthCredentials { Username = "<api_key>", Password = "x" }` (namespace: `MaxioAdvancedBilling.Core.Authentication.Basic`)
- **Environment:** `ServerEnvironment.Us` (default, namespace: `MaxioAdvancedBilling.Servers`)
- **Base URL override:** Set `options.Server.Production.Us.Site = "<subdomain>"` (wire format: maps to `https://{subdomain}.chargify.com`)
- **DI:** `services.AddMaxioAdvancedBillingClient(opts => { opts.BasicAuth = new(…); })` registers a transient client wrapper over a reused `IHttpClientFactory`-backed `HttpClient`.

### Namespaces (add `using` directives)

- `MaxioAdvancedBilling` — client, options, server/auth types
- `MaxioAdvancedBilling.Api` — controller accessors on client (e.g. `client.Customers`, `client.Subscriptions`, `client.Products`)
- `MaxioAdvancedBilling.Models` — request/response records
- `MaxioAdvancedBilling.Models.Enums` — `SubscriptionState`, `CollectionMethod`
- `MaxioAdvancedBilling.Core.Authentication.Basic` — `BasicAuthCredentials`
- `MaxioAdvancedBilling.Servers` — `ServerEnvironment`
- `MaxioAdvancedBilling.Errors` — `CreateCustomerError`, `CreateSubscriptionError`, `FindSubscriptionError` (if you want typed catch)

---

## Trap Notes

⚠ **Step 1 (client registration)** — The SDK's `HttpClient` must be **long-lived and shared** via `IHttpClientFactory`; the client wrapper (`MaxioAdvancedBillingClient`) is transient, but the underlying HTTP handler is **not** reusable per-request. **MUST load `dotnet-client-initialization`** before wiring the DI factory.

⚠ **Step 1 (authentication)** — Basic auth: **username = API key, password = literal `"x"`**. Load credentials from config (binding key `Maxio:ApiKey`), **never hardcode**. **MUST load `dotnet-authentication`** before setting `BasicAuthCredentials`.

⚠ **Step 1 (configuration)** — The server `Site` (subdomain) is set via `options.Server.Production.Us.Site`. **MUST load `dotnet-configuration-resilience`** to understand the base URL template and when override points apply.

⚠ **Step 2 (list products)** — The `ListProducts` call returns **only the first page** by default (`page=1, perPage=20`). If you have > 20 products, **pagination must be explicit**: loop with page+perPage increments. **MUST load `dotnet-calling-endpoints`** before writing pagination logic.

⚠ **Step 3 (customer creation)** — **Idempotency via Reference field:** when POSTing `CreateCustomerRequest`, include the **`Reference` field** (your app's customer ID). Then **before creating**, call `ReadCustomerByReference(reference)` to check if the customer already exists. This prevents duplicate customers on double-click. **MUST load `dotnet-calling-endpoints`** for the lookup call pattern.

⚠ **Step 3 (subscription creation — payment method not required)** — The Maxio API allows subscriptions without an upfront payment profile when the plan has `RequestCreditCard = false`. **Do NOT pass `CreditCardAttributes`, `BankAccountAttributes`, or `PaymentProfileId`** in the request; the subscription will be created in `awaiting_signup` or `pending` state. Confirm plan configuration in the Maxio UI. **MUST load `dotnet-calling-endpoints`** to understand when fields are optional vs required.

⚠ **Step 5 (error handling — TWO JsonException paths)** — Read **both** of these carefully:
1. A **drifted or malformed 2xx body** (e.g. a missing `required` member in the response record) throws `JsonException` from deserialization, **not** an `SdkException` — a catch ladder that catches only `SdkException` will let the `JsonException` escape the integration boundary.
2. A **non-2xx body that does not match its operation's generated `{Operation}Error` shape** (e.g. an unexpected HTML error page from a proxy) throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status code is destroyed — mapping every `JsonException` to a 500 response then retrying will retry something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## REQUIRED READING

Load these companion skills **before implementation starts** — the sheet deliberately does not carry their contents:

| Skill | Step | Why |
|---|---|---|
| `dotnet-client-initialization` | 1 | HTTP client lifecycle, DI factory, transient vs long-lived patterns |
| `dotnet-authentication` | 1 | Basic auth wiring, credential rotation, config binding |
| `dotnet-configuration-resilience` | 1 | Base URL override, retry/timeout semantics, Timeout bounds (per-attempt not total) |
| `dotnet-calling-endpoints` | 2–4 | Named vs positional args, response envelope unwrapping, pagination loops, nullable params |
| `dotnet-error-handling` | 5 | `SdkException<T>` vs `RawError`, `TryGet…` accessors, `JsonException` paths, boundary mapping |

---

## Assumptions & Blockers

**Assumptions:**
- The application will provide `Maxio:ApiKey`, `Maxio:Subdomain` (and optionally `Maxio:BaseUrl`) via configuration (e.g. `appsettings.json` + user-secrets for local dev).
- The "Reference" field on customers maps to the application's internal customer ID (a unique string). For idempotency, the app will re-use the same Reference on subsequent calls.
- Product handles (`eshop-pro`, `basic-plan`) or product IDs will be available to the application (hard-coded per the Sandbox entity table, or looked up on startup).
- "Payment method NOT required" means the Maxio plan is configured to allow subscriptions without an upfront payment profile; the app does not need to collect or store card details for initial signup.
- The application owns the question of when to show the subscription endpoints (JWT-authenticated, etc.); the SDK integration is the Maxio contract layer, not the endpoint access control.

**Blockers:**
- **None identified.** The Maxio API surface supports all required flows (idempotent customer creation via Reference, subscription creation without payment method, plan listing, subscription retrieval by customer and by reference).

