# Maxio Billing Integration — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

**Step 1: Client registration & DI** — `AddMaxioAdvancedBillingClient` with Basic auth (API key + "x")
**Step 2: Configuration** — wire Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, Maxio:BaseUrl (optional)
**Step 3: Plan listing endpoint** — GET `/api/subscription-plans` — calls `ListProducts`, filters by family handle
**Step 4: Idempotent customer creation** — lookup by reference via `ReadCustomerByReference`, create if missing via `CreateCustomer`
**Step 5: Subscription creation endpoint** — POST `/api/subscriptions` — calls `CreateSubscription` with customer reference
**Step 6: Subscription retrieval endpoint** — GET `/api/my-subscriptions` — calls `ListCustomerSubscriptions` by customer ID
**Step 7: Error boundary** — wrap all SDK calls in typed try-catch (Case A + Case B handlers)

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Controller | Signature | Request model | Response envelope | Error case | Pagination | Notes | Source |
|---|---|---|---|---|---|---|---|
| `client.Products` | `ListProducts(BasicDateField? dateField = null, ListProductsFilter? filter = null, DateTimeOffset? endDate = null, DateTimeOffset? endDatetime = null, DateTimeOffset? startDate = null, DateTimeOffset? startDatetime = null, bool? includeArchived = null, ListProductsInclude? include = null, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | **Query-only**: `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include` (all nullable, pass `null` to skip); pagination via `page` (default 1), `perPage` (default 20) | `IReadOnlyList<ProductResponse>` → each item has `ProductResponse.Product` (wrapped, required) → fields: `Id (int?)`, `Name (string?)`, `Handle (string?)`, `PriceInCents (long?)`, `Interval (int?)`, `IntervalUnit (IntervalUnit?)`, `ProductFamily (ProductFamily?)` | **Case B**: `SdkException<RawError>` → `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>` | Manual `page`+`perPage` | Lists all products belonging to the site; filter by Product Family handle in code post-fetch | `operations/Products.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | **Query param**: `reference` (required, pass explicitly) | `CustomerResponse` → `CustomerResponse.Customer` (wrapped, required) → fields: `Id (int?)`, `Email (string?)`, `FirstName (string?)`, `LastName (string?)`, `Reference (string?)`, `CreatedAt (DateTimeOffset?)`, `UpdatedAt (DateTimeOffset?)` | **Case B**: `SdkException<RawError>` → `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | Returns a single customer by their unique reference ID; use to check existence before creating idempotent customer; throws on 404 (not found) — wrap in try-catch and treat as "customer does not exist" signal | `operations/Customers.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | **Body** (required, pass explicitly): `CreateCustomerRequest` { `Customer` (required, nested): `CustomerAttributes` { `FirstName (string?)`, `LastName (string?)`, `Email (string?)`, `Reference (string?)`, `Address (string?)`, `City (string?)`, `State (string?)`, `Zip (string?)`, `Country (string?)`, `Phone (string?)`, plus optional org/cc-emails/verified/tax-exempt/metafields } } | `CustomerResponse` → `CustomerResponse.Customer` (required) → same shape as ReadCustomerByReference return | **Case A**: `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] → error payload: `CustomerErrorResponse1` { `Errors` (Errors?) { `PerPage (IReadOnlyList<string>?)`, `PricePoint (IReadOnlyList<string>?)` } } | None | Creates a new customer; validation: one customer per unique reference value enforced by provider. Wire name for `reference` is `reference`. Use customer email + shopper ID as unique reference to prevent duplicates. | `operations/Customers.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | **Body** (required, pass explicitly): `CreateSubscriptionRequest` { `Subscription` (required, nested): `CreateSubscription` { **MUST provide one of**: `ProductHandle (string?)` or `ProductId (int?)` — **Recommend handle match against config's `Maxio:ProductFamilyHandle`**; **Subscription identity**: `CustomerId (int?)` OR `CustomerReference (string?)` OR `CustomerAttributes (CustomerAttributes?)` (nested) — pick customer by one method only; **Billing**: `PaymentCollectionMethod (CollectionMethod?)` — optional for this sandbox (payment NOT required per task); optional fields: `CouponCode (string?)`, `CouponCodes (IReadOnlyList<string>?)`, `Reference (string?)` for subscription reference, `NextBillingAt (DateTimeOffset?)`, `InitialBillingAt (DateTimeOffset?)`, `DeferSignup (bool? = false)` } } | `SubscriptionResponse` → `SubscriptionResponse.Subscription` (required) → fields: `Id (int?)`, `State (SubscriptionState?)`, `ProductPriceInCents (long?)`, `CurrentPeriodEndsAt (DateTimeOffset?)`, `NextAssessmentAt (DateTimeOffset?)`, `ActivatedAt (DateTimeOffset?)`, `CreatedAt (DateTimeOffset?)`, `Customer (Customer?)`, `Product (Product?)` | **Case A**: `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] → error payload: `ErrorListResponse1` { `Errors (IReadOnlyList<string> !req)` } | None | Creates subscription for customer + product. Payment method NOT required per task spec (no payment card collected). Use `CustomerReference` (shopper ID) to link to idempotent-created customer. 3DS flow not required for sandbox. | `operations/Subscriptions.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | **Path param** (required): `customerId (int)` | `IReadOnlyList<SubscriptionResponse>` → each item `SubscriptionResponse.Subscription` (required) → same shape as CreateSubscription return | **Case B**: `SdkException<RawError>` → `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | Lists all subscriptions for a given customer ID (Maxio-assigned, not shopper reference). Must convert shopper reference to customer ID via ReadCustomerByReference first. | `operations/Customers.md` |

### Supporting Models (used as nested types in request bodies)

| Record | Fields | Required flag | Wire names | Notes | Source |
|---|---|---|---|---|---|
| `CustomerAttributes` | `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `CcEmails (cc_emails): string?`, `Verified (verified): bool?`, `TaxExempt (tax_exempt): bool?`, `VatNumber (vat_number): string?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`, others | None required (all optional) | Shown in parentheses | Nested inside `CreateCustomerRequest.Customer`; use shopper email + ID in `Reference` for idempotent dedup; ISO country codes required (2-char) | `records-2-Cr-Ne.md` |
| `CreateSubscription` | `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `Reference (reference): string?`, `PaymentProfileId (payment_profile_id): int?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false`, plus 30+ optional (trial, components, groups, prepaid config, etc.) | None required in the operation signature, but at least one of: product handle/ID + customer handle/ID/attributes | Wire names as shown | Nested inside `CreateSubscriptionRequest.Subscription`; **do NOT use CustomerAttributes if CustomerReference is supplied** — pass one or the other to avoid duplication; no payment method required for this sandbox | `records-2-Cr-Ne.md` |
| `Subscription` (response inner type) | `Id (id): int?`, `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `TrialStartedAt (trial_started_at): DateTimeOffset?`, `TrialEndedAt (trial_ended_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `Customer (customer): Customer?`, `Product (product): Product?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?` | None (all optional in response) | Shown in parentheses | Returned inside `SubscriptionResponse.Subscription`; use `State`, `NextAssessmentAt`, `ActivatedAt` to show to user | `records-3-Of-Su.md` |
| `Customer` (response inner type) | `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, plus address/phone/verified/tax fields | None (all optional in response) | Shown in parentheses | Returned inside `CustomerResponse.Customer` or nested in Subscription | `records-2-Cr-Ne.md` |
| `Product` (response inner type) | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?`, others | None (all optional in response) | Shown in parentheses | Returned inside `ProductResponse.Product` or nested in Subscription; use `Name`, `PriceInCents`, `Interval`, `IntervalUnit` to display plan details | `records-3-Of-Su.md` |

### Enum values (from `enums.md`)

| Enum | Member names (C# static) | Wire value | Used for | Source |
|---|---|---|---|---|
| `SubscriptionState` | `Active`, `Trialing`, `PastDue`, `Paused`, `Canceled`, `TrialEnded`, `AwaitingSignup` (+ others) | lowercase wire name | Subscription.State field; check for `Active` to confirm active subscription | `models/enums.md` |
| `IntervalUnit` | `Day`, `Month`, `Year`, plus `Week`, `BiWeekly`, `TenDays`, `QuarterYear`, `SemiAnnually` | lowercase wire name (e.g. `month`, `year`) | Product.IntervalUnit, Subscription nested Product.IntervalUnit; display as "per month", "per year" | `models/enums.md` |
| `CollectionMethod` | `Automatic`, `Remittance`, `Prepaid` (+ others) | lowercase wire name | optional CreateSubscription.PaymentCollectionMethod; not required for this sandbox | `models/enums.md` |

### Client construction & auth

**Namespace imports** (add these to top of file):
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;  // for client.Products, client.Customers, client.Subscriptions
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;  // for ServerEnvironment
using MaxioAdvancedBilling.Models;  // for all request/response records
using MaxioAdvancedBilling.Models.Enums;  // for SubscriptionState, IntervalUnit, CollectionMethod
using MaxioAdvancedBilling.Errors;  // for CreateCustomerError, CreateSubscriptionError
using MaxioAdvancedBilling.Core.ErrorResponse;  // for RawError
```

**Client registration (DI pattern — use this)**:
```csharp
services.AddMaxioAdvancedBillingClient(opts =>
{
    var apiKey = configuration["Maxio:ApiKey"];  // from IConfiguration bound to Maxio:ApiKey setting
    var subdomain = configuration["Maxio:Subdomain"];
    var baseUrlOverride = configuration["Maxio:BaseUrl"];  // optional override; default US is https://{subdomain}.chargify.com
    
    opts.BasicAuth = new BasicAuthCredentials
    {
        Username = apiKey,       // your Maxio/Chargify API key
        Password = "x"           // literal string "x"
    };
    
    opts.Environment = ServerEnvironment.Us;  // default for most accounts; switch to .Eu if needed
    
    // Optional: override base URL (e.g., for mock/dev)
    if (!string.IsNullOrEmpty(baseUrlOverride))
    {
        opts.Server = new ServerOptions
        {
            Production = new ProductionOptions
            {
                Us = new ServerInfo { BaseUrl = baseUrlOverride }
            }
        };
    }
    // Optional: set subdomain if not included in base URL template
    if (!string.IsNullOrEmpty(subdomain))
    {
        opts.Server ??= new ServerOptions();
        opts.Server.Production ??= new ProductionOptions();
        opts.Server.Production.Us ??= new ServerInfo();
        opts.Server.Production.Us.Site = subdomain;
    }
});
```

**Injected usage** (in controller/service):
```csharp
public MySubscriptionService(MaxioAdvancedBillingClient client) => _client = client;

// Call: var response = await _client.Products.ListProducts(
//     dateField: null, filter: null, endDate: null, ... , page: 1, perPage: 20, ct: cancellationToken);
```

---

## TRAP NOTES

⚠ **Step 1 (client registration)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. Per-operation timeouts and Polly retry semantics require separate wiring. **MUST load `dotnet-configuration-resilience`** before setting retry policy or timeouts.

⚠ **Step 2 (configuration)** — `Maxio:ApiKey` and `Maxio:Subdomain` must come from `IConfiguration` (environment, user-secrets, appsettings.json), never hardcoded or logged. The base URL auto-templates `https://{subdomain}.chargify.com` unless overridden at `Maxio:BaseUrl`. **MUST load `dotnet-authentication`** to confirm credential loading order and per-environment wiring.

⚠ **Step 3, 4, 5, 6 (all SDK calls)** — all operations are **throw-only**; there are no `…Result`/`…Async` no-throw variants. Every call must be wrapped in a try-catch ladder that handles **both Case A (typed error) and Case B (raw error)**. The two error paths are mutually exclusive per operation — see error-handling notes below.

⚠ **Step 4 (idempotent customer creation)** — `ReadCustomerByReference` throws `SdkException<RawError>` on 404 (customer not found). This is **not** an error to surface; catch, check `.StatusCode == HttpStatusCode.NotFound`, and treat as "customer does not exist" signal. **MUST load `dotnet-error-handling`** to handle the 404 pattern correctly.

⚠ **Step 5 (subscription creation)** — do **NOT** pass both `CustomerAttributes` and `CustomerReference`; choose one. For idempotent flow, pass `CustomerReference` (shopper ID). The `ProductHandle` field is a string; match against `Maxio:ProductFamilyHandle` to filter available products in Step 3 before showing to user.

⚠ **Step 7 (error boundary)** — **two JSON deserialization traps**:
  - A **malformed 2xx body** (missing required field in response) surfaces as `JsonException` from deserialization, **NOT** as `SdkException` — an SDK-exception-only catch ladder lets it escape; boundary must catch `JsonException` separately.
  - A **non-2xx body that does not match** the operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 500 reports a deserializable error as an outage, and a caller that retries 500 retries something that can never succeed.
  
  **MUST load `dotnet-error-handling`** to handle both patterns before writing the boundary.

---

## Assumptions & Blockers

**Assumptions:**
- Shopper account/authentication is separate from Maxio; the API controller receives a logged-in shopper ID (JWT claim or session) and uses it (+ email) to build a unique `Reference` for idempotent customer creation.
- No payment method is collected in the UI; `CreateSubscription` is called without `PaymentProfileAttributes` or payment method nonce. The sandbox allows this per task spec ("payment method not required").
- Product filtering by family is done in-code post-fetch from `ListProducts` (not via Maxio API filter); only products matching `Maxio:ProductFamilyHandle` are shown to shopper.
- Trial and setup fees are absent; `CreateSubscription` does not need trial/initial-charge wiring.
- Metered components are defined in Maxio (api-call component ID 3057195) but not subscribed to at signup; component allocation is out of scope.

**Blockers:** None. Map is complete; all signatures, error accessors, and model shapes are resolved from SDK source.

---

## REQUIRED READING

Load **before implementation starts**. These companion skills carry defaults, worked examples, and gotchas not captured in the contract sheet:

| Skill | Governs | Why |
|---|---|---|
| `dotnet-client-initialization` | Step 1 — Client & DI setup | HttpClient lifecycle, transient vs. long-lived wrappers, service registration pattern |
| `dotnet-authentication` | Step 2 — Basic auth wiring | Credential loading, per-environment configuration, password = "x" convention |
| `dotnet-calling-endpoints` | Steps 3–6 — Operation calls | Named vs. positional arguments for nullable params, async/await, cancellation token handling |
| `dotnet-models` | Response envelope unwrapping (all steps) | Understanding ProductResponse.Product, SubscriptionResponse.Subscription wrappers; union factory methods (not used here but referenced in other responses) |
| `dotnet-error-handling` | Step 7 — Error boundary | Case A vs. Case B error paths, `TryGet…` accessors, JSON deserialization traps (required reading — two traps documented above) |
| `dotnet-configuration-resilience` | Step 2 (retry/timeout), Step 1 (HTTP pipeline) | Per-attempt vs. total timeout semantics, Polly integration, HttpMethodsToRetry gate on status only (not transport failures), max-retry floor = 1 |
| `dotnet-testing` | Testing stubs (optional, out of scope for this plan) | HttpClient constructor seam for mocking |
