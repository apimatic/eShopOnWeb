# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Client registration & DI** — Register `MaxioAdvancedBillingClient` with HttpClientFactory; wire HTTP Basic auth (API key).
2. **Configuration** — Bind Maxio settings (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`) to `Maxio:*` config section; override base URL if needed (sandbox vs. prod).
3. **GET /api/subscription-plans** — Enumerate products via `ListProducts()` scoped to the product family handle (`eshop-subscribe`), then read each by handle to get full details with `ReadProductByHandle()`.
4. **POST /api/subscriptions** — Idempotent customer creation via `ReadCustomerByReference()` (lookup by eShopOnWeb user ID as the reference); if not found, `CreateCustomer()` with the reference set. Then `CreateSubscription()` with the product handle.
5. **GET /api/my-subscriptions** — Fetch customer's subscriptions via `ListCustomerSubscriptions(customerId)` (post-lookup).

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Operation | Signature | Request Model + Fields | Response Envelope + Fields Read | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **List subscription plans** `Products.ListProducts()` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | **Request**: query-string filters only (no body). Pass `null` for filters you skip. **Fields to pass (sandbox scope)**: — filter for product family if `ListProductsFilter` allows (check map); otherwise post-filter in app code. — `perPage = 20` (default). | **Response**: `IReadOnlyList<ProductResponse>` — each `ProductResponse` has a single field `Product (product): Product !req`. **Fields to read from `Product`**: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `Description (description): string?`, `AccountingCode (accounting_code): string?`. | **Case B** (`SdkException<RawError>`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | manual `page` + `perPage` | `operations/Products.md` |
| **Get plan details by handle** `Products.ReadProductByHandle()` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | **Request**: `apiHandle` — the product's `api_handle` (e.g. `"eshop-pro"`, `"basic-plan"`). | **Response**: `ProductResponse` — unwrap to `Product` (same fields as above). | **Case B** (`SdkException<RawError>`) | none | `operations/Products.md` |
| **Lookup customer by reference (idempotent)** `Customers.ReadCustomerByReference()` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | **Request**: `reference` — the eShopOnWeb user ID (passed as a query string). | **Response**: `CustomerResponse` — unwrap to `Customer`. **Fields read**: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`. | **Case B** (`SdkException<RawError>`) — 404 if not found; handle by creating a new customer. | none | `operations/Customers.md` |
| **Create customer (idempotent)** `Customers.CreateCustomer()` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** (not optional in signature). | **Request**: `CreateCustomerRequest` wraps `Customer (customer): CreateCustomer !req` · **`CreateCustomer` required fields**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. **Optional fields**: `Reference (reference): string?` (set to eShopOnWeb user ID for idempotency), `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`. | **Response**: `CustomerResponse` — unwrap to `Customer` (same fields as lookup above). | **Case A** (`SdkException<CreateCustomerError>`): `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. | none | `operations/Customers.md` |
| **Create subscription** `Subscriptions.CreateSubscription()` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. | **Request**: `CreateSubscriptionRequest` wraps `Subscription (subscription): CreateSubscription !req` · **`CreateSubscription` required-or-Notes-tied fields**: `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (one required per Notes), `CustomerId (customer_id): int?` OR `CustomerAttributes (customer_attributes): CustomerAttributes?` (per Notes: identify existing customer or provide attributes for inline creation). **Optional fields supporting the scope**: `CustomerReference (customer_reference): string?` (alternative to CustomerId for lookup by reference), `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentProfileId (payment_profile_id): int?`, `Reference (reference): string?` (subscription reference for later lookup). **Components (metered/usage-based)**: `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` (for the metered `api-call` component, if provisioning usage). See notes below on which fields the spec requires per accept-gate. | **Response**: `SubscriptionResponse` — unwrap to `Subscription`. **Fields read**: `Id (id): int?`, `State (state): SubscriptionState?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ProductPriceInCents (product_price_in_cents): long?`, `BalanceInCents (balance_in_cents): long?`, `ActivatedAt (activated_at): DateTimeOffset?`, `Reference (reference): string?`, `Customer (customer): Customer?` (nested). | **Case A** (`SdkException<CreateSubscriptionError>`): `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. | none | `operations/Subscriptions.md` |
| **List customer subscriptions** `Customers.ListCustomerSubscriptions()` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | **Request**: `customerId` from the customer lookup/create step. | **Response**: `IReadOnlyList<SubscriptionResponse>` — each unwraps to `Subscription` (same fields as above). | **Case B** (`SdkException<RawError>`) | none (full list returned; no pagination params on this endpoint) | `operations/Customers.md` |
| **Find subscription by reference (optional fallback)** `Subscriptions.FindSubscription()` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass explicitly**. | **Request**: `reference` — the subscription reference. | **Response**: `SubscriptionResponse` — unwrap to `Subscription`. | **Case A** (`SdkException<FindSubscriptionError>`): `TryGetNoContent(out RawError)` [404], `TryGetRawError(out RawError)` [fallback]. | none | `operations/Subscriptions.md` |

### Enums Needed

| Enum | Values Needed | Source |
|---|---|---|
| `SubscriptionState` | `Active (active)`, `Trialing (trialing)`, `Assessing (assessing)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `AwaitingSignup (awaiting_signup)` | `Models/Enums/SubscriptionState.cs` — display to user in `GET /api/my-subscriptions`; record in domain model. |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `Models/Enums/IntervalUnit.cs` — read from product to show billing frequency (e.g. "monthly"). |

### Client Construction & Auth

| Config Key | Type | Default/Notes | Source |
|---|---|---|---|
| `Maxio:ApiKey` | `string` (required) | From `MAXIO_API_KEY` env var. HTTP Basic username. | `sdk-map.md` — Basic auth: Username = API key, Password = literal `"x"`. |
| `Maxio:Subdomain` | `string` (required) | From `MAXIO_SITE_SUBDOMAIN` env var (e.g. `"cp-exp-2"`). Becomes `{site}` in base URL. | `sdk-map.md` — `ServerOptions.Production.Us.Site` = subdomain. |
| `Maxio:BaseUrl` (optional) | `string?` | Omit to use default: `https://{site}.chargify.com` (US). Override for mock/dev host. | `sdk-map.md` — `ServerOptions.Production.Us.BaseUrl`. |
| `Maxio:Environment` | `ServerEnvironment` | Default: `ServerEnvironment.Us`; `ServerEnvironment.Eu` if EU-hosted. | `sdk-map.md` — supplied to `MaxioAdvancedBillingClientOptions.Environment`. |
| `Maxio:ProductFamilyHandle` | `string` (required) | From `MAXIO_DEFAULT_PRODUCT_FAMILY` env var (e.g. `"eshop-subscribe"`). Used to filter `ListProducts()` response. | YOUR CALL — not in the map (passed by caller to filter results). |

**Client Registration (DI):**
```csharp
// Add to ConfigureServices / AddServices:
services.AddMaxioAdvancedBillingClient(options =>
{
    var config = /* IConfiguration */;
    options.BasicAuth = new BasicAuthCredentials
    {
        Username = config["Maxio:ApiKey"],
        Password = "x"  // literal
    };
    options.Environment = Enum.Parse<ServerEnvironment>(config["Maxio:Environment"] ?? "Us");
    
    // Optional: override base URL for sandbox/dev
    if (!string.IsNullOrEmpty(config["Maxio:BaseUrl"]))
    {
        options.Server.Production.Us.BaseUrl = config["Maxio:BaseUrl"];
    }
    options.Server.Production.Us.Site = config["Maxio:Subdomain"];
});
```

---

## Trap Notes

⚠ **Step 1 (client registration) — the SDK's retry/timeout options do NOT bound a whole call and are NOT the timeout on the `HttpClient` you register. The `Timeout` property on `RetryOptions` is per-attempt, and `HttpMethodsToRetry` gates only the HTTP status trigger (so a non-idempotent write like `POST /subscriptions` can execute twice if a transport fault occurs, even on non-retryable statuses). MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 (customer idempotency) — `ReadCustomerByReference()` returns 404 (Case B, `SdkException<RawError>`) if the customer does not exist. Catch this, check `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound`, and call `CreateCustomer()`. Catching `SdkException<RawError>` directly is correct; do not try to parse the raw error for a more specific accessor. MUST load `dotnet-error-handling`** before writing the boundary.

⚠ **Step 3 (subscription creation) — the Notes on `CreateSubscription` state that a subscription requires either a product ID/handle AND either a customer ID or customer attributes (inline creation). Neither field is marked `!req` in the model, but the provider rejects the call if either pair is missing (422 response). The Notes are the contract; the model signature is silent. Carry `ProductHandle`, `CustomerId` (or `CustomerReference`), and any optional fields the scope names. MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (error boundary — `JsonException` from two directions) — a drifted or malformed 2xx body (a missing `required` member) surfaces as a `JsonException` from deserialization, NOT as an `SdkException`, so an SDK-exception-only catch lets it escape; a non-2xx body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` while the error object is being constructed, destroying the HTTP status with it. A boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary. This applies even if the scope feels small — every integration writes an error boundary, and the skill carries the parts a note cannot.

⚠ **Step 5 (pagination on `ListProducts`) — manual `page` + `perPage` with defaults `page = 1`, `perPage = 20`. If the product family contains more than 20 products, loop and collect across pages. For the sandbox, this is unlikely, but the pattern matters. MUST load `dotnet-calling-endpoints`** for pagination semantics.

⚠ **Step 6 (metered component provisioning — optional scope extension) — if the implementation later adds metered `api-call` component usage, `CreateSubscription()` accepts `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`. Each component specifies `ComponentId` or `ComponentHandle`, and optionally `Quantity`. For now, this is out of scope (step 4 creates the subscription without components), but the field is there if usage tracking is added later. MUST load `dotnet-models`** when wiring component objects.

---

## REQUIRED READING

**Load BEFORE implementation starts.** These are non-optional: the contract sheet intentionally does not carry their contents, and the skill carries parts a note cannot.

- `dotnet-client-initialization` — Step 1 (client registration, HttpClientFactory reuse, DI shape).
- `dotnet-authentication` — Step 1 (Basic auth credential shape and timing: set before client construction or in DI callback).
- `dotnet-calling-endpoints` — Step 3+ (operation signatures, required vs. optional params, named-argument binding, pagination). **The Notes field on every operation row is the provider's own prose and the only place the map says when a call is accepted — always check Notes when a field is missing `!req`.**
- `dotnet-models` — Step 4 (unions are built with factories, read via `TryGet…`; enums are `StringEnum<T>`, not C# enums; unmodeled JSON fields are dropped).
- `dotnet-error-handling` — Step 2 (error-handling boundary; Case A vs. Case B; `TryGet…` accessors; `JsonException` from two directions — drifted 2xx body and malformed non-2xx error object).
- `dotnet-configuration-resilience` — Step 1 (retry/timeout semantics; `Timeout` is per-attempt, not total; `HttpMethodsToRetry` gates status only; non-idempotent writes can execute twice on transport faults).

**Both of these hazard rows belong in the FIRST error boundary, written early:**
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb user ID (from `IdentityUser.Id`) is unique, stable, and < 255 chars (fits Maxio `Reference` field, which is string).
- Configuration keys `Maxio:ApiKey` and `Maxio:Subdomain` are set in the deployment environment or `appsettings.json`; missing keys will fail at client registration (expected behavior).
- JWT authentication is already in place on `PublicApi` endpoints; Maxio integration does not require changes to auth.
- The in-memory database (EF Core `InMemoryDatabase`) loses data on restart; no persistence of subscription state across app bounces (acceptable for reference app, not production).
- Maxio sandbox accepts the provided credentials and product family handle `eshop-subscribe` exists on site `cp-exp-2`.

**Blockers:**
None identified. The SDK supports all required operations; the map is complete and matches the spec.

---

## Notes on Contract Fields

### CreateSubscription Required-vs-Optional Clarity

The `CreateSubscription` model shows:
- `ProductHandle (product_handle): string?`
- `ProductId (product_id): int?`
- `CustomerId (customer_id): int?`
- `CustomerAttributes (customer_attributes): CustomerAttributes?`

None are marked `!req`. **The Subscriptions.md Notes state:**
> "Specify the product with `product_id` or `product_handle`. … Identify an existing customer with `customer_id` or `customer_reference`. Optionally, include an existing payment profile using `payment_profile_id`."

This means:
- One of `product_id` OR `product_handle` is required (provider-level, not C# level).
- One of `customer_id` OR `customer_reference` (or `customer_attributes` for inline creation) is required.
- No payment method is required for the scope (sandbox entities have no payment requirement).

**Carry into the request:**
```csharp
new CreateSubscriptionRequest
{
    Subscription = new CreateSubscription
    {
        ProductHandle = "eshop-pro",  // or read from ListProducts
        CustomerId = customerId,       // from lookup/create
        Reference = subscriptionReference,  // optional but recommended for later FindSubscription
        // PaymentProfileId omitted (not required per spec)
    }
};
```

### Product Handle vs. ID

The scope refers to plans by handle (`eshop-pro`, `basic-plan`). `ListProducts` returns both `Id` and `Handle`. For the endpoint contract, use `Handle` (strings are more stable than IDs). If the app later switches to ID-based lookup, both paths are available in the response.

---

## Configuration Binding Reference

| Environment Variable | Config Section | Default | Required |
|---|---|---|---|
| `MAXIO_API_KEY` | `Maxio:ApiKey` | — | Yes |
| `MAXIO_SITE_SUBDOMAIN` | `Maxio:Subdomain` | — | Yes |
| `MAXIO_ENVIRONMENT` | `Maxio:Environment` | `Us` | No (defaults to US) |
| `MAXIO_DEFAULT_PRODUCT_FAMILY` | `Maxio:ProductFamilyHandle` | `eshop-subscribe` | No (hardcode or read) |

**Bind in `Program.cs` or config builder** (before DI registration). Example:
```csharp
var config = builder.Configuration;
// Env vars are auto-bound to IConfiguration if they match the key pattern
// Otherwise, explicitly bind:
config.GetSection("Maxio:ApiKey") ?? config.GetConnectionString("MaxioApiKey") ?? Environment.GetEnvironmentVariable("MAXIO_API_KEY");
```
