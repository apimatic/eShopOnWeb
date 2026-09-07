# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscription Capabilities

## Scope & Sequence

1. **Client registration & DI setup** — Register `MaxioAdvancedBillingClient` in the DI container with credentials from configuration (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional override).
2. **GET /api/subscription-plans** — Call `client.Products.ListProducts(...)` to fetch all products; extract plans by product family handle (`eshop-subscribe`).
3. **POST /api/subscriptions** — Idempotently create a customer via `client.Customers.CreateCustomer(...)` using the authenticated user's email as a reference key; then create a subscription via `client.Subscriptions.CreateSubscription(...)` linking to the customer and plan.
4. **GET /api/my-subscriptions** — Resolve the authenticated user's Maxio customer ID (from a stored mapping or by calling `client.Customers.ReadCustomerByReference(...)` with the user's email), then call `client.Customers.ListCustomerSubscriptions(customerId)` to list active subscriptions.

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation Signatures & Models

| Step | Controller.Method | Signature | Request Model | Response | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 | `Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | None (GET query params) | `IReadOnlyList<ProductResponse>` where each item wraps `Product (product): Product !req` containing `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?` (with `Handle (handle): string?`) | **Case B**: `SdkException<RawError>` — `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>` | Manual `page` + `perPage` (defaults 1, 20) | `operations/Products.md` |
| 2a | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` → `CreateCustomer { FirstName (first_name): string !req, LastName (last_name): string !req, Email (email): string !req, Reference (reference): string?, Address (address): string?, Address2 (address_2): string?, City (city): string?, State (state): string?, Zip (zip): string?, Country (country): string?, Phone (phone): string?, Organization (organization): string?, Locale (locale): string?, VatNumber (vat_number): string?, TaxExempt (tax_exempt): bool?, TaxExemptReason (tax_exempt_reason): string?, ParentId (parent_id): int?, SalesforceId (salesforce_id): string?, CcEmails (cc_emails): string? }` | `CustomerResponse { Customer (customer): Customer !req }` → `Customer { Id (id): int?, FirstName (first_name): string?, LastName (last_name): string?, Email (email): string?, Reference (reference): string?, CreatedAt (created_at): DateTimeOffset?, UpdatedAt (updated_at): DateTimeOffset?, ... }` | **Case A**: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md` |
| 2a (lookup) | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` Query param: `reference (reference): string` | None (query param) | `CustomerResponse { Customer (customer): Customer !req }` (same as CreateCustomer) | **Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Customers.md` |
| 2b | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` → `CreateSubscription { ProductHandle (product_handle): string?, ProductId (product_id): int?, ProductPricePointHandle (product_price_point_handle): string?, ProductPricePointId (product_price_point_id): int?, CustomerId (customer_id): int?, CustomerAttributes (customer_attributes): CustomerAttributes?, PaymentCollectionMethod (payment_collection_method): CollectionMethod?, ReceivesInvoiceEmails (receives_invoice_emails): string?, NetTerms (net_terms): string?, NextBillingAt (next_billing_at): DateTimeOffset?, InitialBillingAt (initial_billing_at): DateTimeOffset?, DeferSignup (defer_signup): bool? = false, Reference (reference): string?, CouponCode (coupon_code): string?, CouponCodes (coupon_codes): IReadOnlyList<string>?, ... }` | `SubscriptionResponse { Subscription (subscription): Subscription? }` → `Subscription { Id (id): int?, State (state): SubscriptionState?, BalanceInCents (balance_in_cents): long?, ProductPriceInCents (product_price_in_cents): long?, CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?, NextAssessmentAt (next_assessment_at): DateTimeOffset?, ActivatedAt (activated_at): DateTimeOffset?, CreatedAt (created_at): DateTimeOffset?, UpdatedAt (updated_at): DateTimeOffset?, CancellationMethod (cancellation_method): CancellationMethod?, Customer (customer): Customer?, Product (product): Product?, Reference (reference): string?, ... }` | **Case A**: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | None | `operations/Subscriptions.md` |
| 3 | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | None (customer ID in URL path) | `IReadOnlyList<SubscriptionResponse>` (same as CreateSubscription response item) | **Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Customers.md` |

### Enums & Constants

**CollectionMethod** (wire name ← C# member; namespace `MaxioAdvancedBilling.Models.Enums`):
- `Automatic (automatic)` — automatic billing collection
- `Remittance (remittance)` — remittance-based
- `Prepaid (prepaid)` — prepaid
- `Invoice (invoice)` — invoice-based

**IntervalUnit** (namespace `MaxioAdvancedBilling.Models.Enums`):
- `Day (day)` — billing interval in days
- `Month (month)` — billing interval in months

**SubscriptionState** (namespace `MaxioAdvancedBilling.Models.Enums`):
- `Active (active)` — normal, active, paid and up to date
- `Canceled (canceled)` — subscription has been canceled
- `Paused (paused)` — subscription is paused
- `PastDue (past_due)` — subscription payment is overdue
- `Trialing (trialing)` — subscription is in trial period
- ... (15 total states; see enums.md for full list)

**CancellationMethod** (namespace `MaxioAdvancedBilling.Models.Enums`):
- `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)`

### Client Construction & Configuration

| Element | Details | Source |
|---|---|---|
| **Root Namespace** | `MaxioAdvancedBilling` | `sdk-map.md` |
| **Client Class** | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| **Options Class** | `MaxioAdvancedBillingClientOptions { Environment: ServerEnvironment, Retry: RetryOptions?, Server: ServerOptions?, BasicAuth: BasicAuthCredentials? }` (namespace `MaxioAdvancedBilling`) | `sdk-map.md` |
| **Authentication** | HTTP Basic — `BasicAuthCredentials { Username: string, Password: string }` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`); set **Username = API key, Password = literal `"x"`** | `sdk-map.md` |
| **Environments** | `ServerEnvironment.Us` (default, `https://{site}.chargify.com`) or `ServerEnvironment.Eu` (`https://{site}.ebilling.maxio.com`) — namespace `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| **Server Override** | `options.Server.Production.Us.BaseUrl = "http://..."`; `options.Server.Production.Us.Site = "subdomain"` — for testing/override | `sdk-map.md` |
| **Controllers** | Accessed as properties on the client: `client.Products`, `client.Customers`, `client.Subscriptions` — namespace `MaxioAdvancedBilling.Api` | `sdk-map.md` |
| **Configuration Source** | Read from `IConfiguration` (use .NET built-in binding): `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional override) | YOUR CALL — not in the map |

---

## Trap Notes

⚠ **Step 1 (client registration)** — The SDK's `Retry` options configure exponential backoff and status-code trigger filters (Polly). The HTTP verb `POST` is not in the default `HttpMethodsToRetry`, so a failed write does not auto-retry on `503`; however, **transport exceptions retry on every verb** — a non-idempotent write (subscription creation) can execute more than once if a connection drops mid-flight. **MUST load `dotnet-configuration-resilience`** before configuring or bypassing retry settings.

⚠ **Step 2a (customer creation, idempotency)** — There is no `upsert` endpoint. To avoid duplicate customers, use the `reference` field (set it to the user's email or ID) and rely on the map's note: *"you may only create one customer for a given reference value."* For true idempotency, query by reference first (`ReadCustomerByReference`); if 404, create. **MUST load `dotnet-calling-endpoints`** to understand when to pass nullable params explicitly (many optional params have no default and mis-bind in positional calls).

⚠ **Step 2b (subscription creation without payment method)** — The task specifies *"payment method not required"*. The CreateSubscription operation's `Notes` field states payment may be required depending on product options; the seeded product configuration must not require a card. Do not pass payment profile fields unless explicitly provided by the caller. Set `DeferSignup = false` to attempt immediate billing; a trial or zero-price setup will not error. **MUST load `dotnet-models`** to understand when a field is a union (e.g. `ComponentId` in `CreateSubscriptionComponent`); they must be constructed via factory methods.

⚠ **All read operations** — `ListProducts`, `ListCustomerSubscriptions`, `ReadCustomerByReference` are **Case B (RawError)**, not typed errors. `JsonException` from a malformed or missing required field in a 2xx response is **not** an `SdkException` and will escape an SDK-exception-only catch ladder. A non-2xx response body that does not match the expected shape throws `JsonException` *during error deserialization*, **destroying the HTTP status code**. **MUST load `dotnet-error-handling`** before writing the integration boundary to map `JsonException` correctly and preserve status context.

⚠ **Customer lookup by reference** — The operation's note names the query param as `reference` (wire name). The C# parameter is also named `reference` (no transformation needed). Do not assume a customer exists; a 404 (Not Found / no match) surfaces as Case B error.

---

## REQUIRED READING

Load **before implementation starts**. These are the integration layer skills the contract sheet does not carry:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1: Client & DI setup; HttpClient lifecycle; transient vs. long-lived wrapper |
| `dotnet-authentication` | Step 1: Credentials wiring; reading config into `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–3: How to pass nullable params explicitly; async/await; call list/search with named args |
| `dotnet-models` | Steps 2–3: Union construction (factory methods, `TryGet…`); `StringEnum<T>` building and comparison |
| `dotnet-error-handling` | Steps 1–3: Exception boundary; Case A/B distinction; `JsonException` handling; fallback to generic message **[CRITICAL]** |
| `dotnet-configuration-resilience` | Step 1: Retry/timeout semantics; when transport exceptions retry non-idempotent writes; why `MaxRetries = 0` is rejected |

---

**Both of the following hazards must be handled in the error boundary before implementation starts:**

1. **`System.Text.Json.JsonException` from drifted 2xx body** — A missing `required` field in a successful response (e.g., `Product.Id` omitted) surfaces as `JsonException` from deserialization, **NOT** as `SdkException`. An SDK-exception-only catch ladder lets it escape. Map every `JsonException` to a defensive 5xx at the boundary, but **preserve the original exception message** to distinguish this from network/server errors. **MUST load `dotnet-error-handling`**.

2. **`System.Text.Json.JsonException` from non-2xx error body mismatch** — When the HTTP status is not 2xx and the response body does not match the operation's generated `{Operation}Error` shape (e.g., a 422 with unexpected JSON structure), `JsonException` is thrown **while the error object is being constructed**, so the **exception replaces the `SdkException`** and the HTTP status is destroyed. A boundary that maps every `JsonException` to 5xx then reports it as an outage will retry indefinitely. Detect this by catching `JsonException` **before** the `SdkException` catch, logging the raw response body, and either extracting the HTTP status from the exception context (if available) or returning a 422 (Unprocessable Entity) as a safe default for probable validation errors. **MUST load `dotnet-error-handling`**.

---

## Assumptions & Blockers

| Item | Status | Details |
|---|---|---|
| **Seeded product family** | Assumed | The Maxio site (`cp-exp-1`) has a product family with handle `eshop-subscribe` containing the Basic Plan (`basic-plan`, $29/mo) and Pro Plan (`eshop-pro`, $299/mo). Verified during task brief. |
| **No trial, no setup fee** | Assumed | Seeded products are configured with no trial (`TrialPriceInCents = null`, `TrialInterval = null`) and no setup fees. The application does not override these. |
| **No payment method required** | Assumed | Product configuration does not mandate a payment profile at signup (e.g., `RequireCreditCard = false`). If this assumption is false, subscriptions will fail with a 422 error on the `create` call. |
| **User authentication** | Assumed | The endpoints are JWT-authenticated in PublicApi. The integration trusts the authenticated user's identity (from the JWT claim or principal). No separate user identity lookup is needed. |
| **Idempotent customer lookup** | Decision | To avoid duplicate customers, the app must either (a) store the Maxio customer ID in the user's profile after first creation, or (b) call `ReadCustomerByReference` on each subscription request. The plan assumes (a) for performance; the implementer may choose (b) for simplicity. |
| **Reference field strategy** | Decision | The `reference` field on both customer and subscription should be set to the authenticated user's email address (or internal ID) to enable lookups. The task requires idempotent creation; this is the recommended approach. |
| **No live Maxio testing** | Assumed | The `MAXIO_ENVIRONMENT` env var defaults to sandbox; production credentials are not in scope. All integration testing is against the Maxio sandbox. |
| **SDK runtime compatibility** | **BLOCKER** | `AsadAli.AdvancedBilling.Sdk v1.0.2` has a transitive runtime dependency on `Microsoft.Bcl.AsyncInterfaces v10.0.0.8`, which fails to load on .NET 8.0 at runtime (code compiles, but execution throws `Could not load file or assembly`). The SDK map documents target framework `netstandard2.0` (works on .NET 5–10+) but does not list this dependency. **Mitigation:** (1) Check nuget.org for a newer SDK version (> v1.0.2); if available, upgrade. (2) If stuck on v1.0.2, explicitly pin a compatible `Microsoft.Bcl.AsyncInterfaces` version (e.g., v8.0.0) in the project's `Directory.Packages.props`. (3) If neither resolves, contact Maxio support or verify the project's transitive dependency resolution. **UNVERIFIED** — this is a runtime/deployment issue outside the SDK contract; resolution requires external verification. |

---

## Notes on Implementation Details

### Step 1: Client Registration  
- Register the `MaxioAdvancedBillingClient` as a scoped or singleton dependency (long-lived, reused across requests).
- Use `IHttpClientFactory` to manage the underlying `HttpClient`; do not create a new client per request.
- Bind configuration from `IConfiguration["Maxio:*"]` into the `BasicAuthCredentials` and `ServerOptions` at startup.
- If `Maxio:BaseUrl` is provided, override `options.Server.Production.Us.BaseUrl` to redirect to the test/override host.

### Step 2a: Idempotent Customer Creation  
- Before calling `CreateCustomer`, attempt `ReadCustomerByReference(userEmail)` or `ReadCustomerByReference(userId)`.
- If the customer exists (200 OK), extract the `customer.Id` from the response and proceed to Step 2b.
- If the customer does not exist (404 / `SdkException<RawError>` with `StatusCode == NotFound`), create it.
- Store the returned `customer.Id` in the user's profile or session for future subscription lookups.
- Set `CreateCustomer.Reference = userEmail` (or userId) to match the lookup key.

### Step 2b: Subscription Creation  
- Call `CreateSubscription` with `ProductHandle = "eshop-pro"` or `"basic-plan"` (based on user selection).
- Set `CustomerId = maxioCustomerId` (from Step 2a).
- **Do NOT** pass payment profile fields; the product is pre-configured with no payment requirement.
- Set `ReceivesInvoiceEmails = "true"` (as a string, per the map) to enable invoicing.
- Set `Reference = userEmail` or a subscription-local reference for later tracking.
- A successful response returns `SubscriptionResponse.Subscription` with `State = SubscriptionState.Active` (or `Trialing` if applicable).

### Step 3: List User Subscriptions  
- Resolve the user's Maxio `customerId` from storage (set in Step 2a).
- Call `ListCustomerSubscriptions(customerId)`.
- Filter by `SubscriptionState.Active` if the endpoint does not filter server-side; the response is a flat list.
- Map each `Subscription` to a response DTO with essential fields: `Id`, `ProductName`, `State`, `NextAssessmentAt`, `BalanceInCents`.

### Error Handling at the Boundary  
- **Case A errors** (typed): Check operation-specific `TryGet…` methods first. For subscription creation (422), use `TryGetErrorListResponse1(out ErrorListResponse1 errorResp)` to access `errorResp.Errors` — **an `IReadOnlyList<string>` of validation error messages (not a dict)**. For customer creation (422), use `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)`.
- **Case B errors** (raw): Use `StatusCode` and `ReadAsString()` to log and classify (404, 401, 422, 5xx).
- **`JsonException`**: Catch before `SdkException` catch; extract raw response body if available; log and return 5xx or 422 depending on context (see trap notes).
- All errors: Return a user-friendly message (e.g., "Unable to create subscription; please try again") and log the full error context server-side. For subscription 422 errors, join or iterate the `Errors` list (e.g., `string.Join("; ", errorResp.Errors)`) to build a user message.

---

## Files Touched During Implementation

- `src/PublicApi/Startup.cs` or `Program.cs` — DI registration of `MaxioAdvancedBillingClient` and configuration binding.
- `src/PublicApi/Controllers/SubscriptionsController.cs` (new or modified) — Three endpoints.
- `src/PublicApi/Services/MaxioSubscriptionService.cs` or similar (new) — SDK call logic; idempotent customer creation; error mapping.
- `src/PublicApi/Models/SubscriptionPlanResponse.cs`, `SubscriptionResponse.cs`, etc. (new) — Public API DTOs.
- `.NET user-secrets` (local) — Store `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`.
- `appsettings.json` or `appsettings.Development.json` — Config bindings (stubs with placeholders; actual values in user-secrets or env vars in production).
