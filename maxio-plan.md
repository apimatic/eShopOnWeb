# Maxio Integration Plan — eShopOnWeb Recurring Billing

## Scope & Sequence

1. **Client & DI setup** — Register `MaxioAdvancedBillingClient` with HTTP factory; set Basic auth credentials from `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`.
2. **Configuration binding** — Bind env vars to `.NET config section `Maxio:` (ApiKey, Subdomain, Environment, BaseUrl override).
3. **List subscription plans** — `ListProducts` to fetch all available plans (filter by product family handle `eshop-subscribe`).
4. **Get plan details** — `ReadProduct` or `ReadProductByHandle` to retrieve name, price (`PriceInCents`), billing frequency (`Interval`, `IntervalUnit`).
5. **Ensure Maxio customer exists** — Check for customer by eShopOnWeb user ID reference (`ReadCustomerByReference`); create if missing (`CreateCustomer`). Store Maxio customer ID in eShopOnWeb user record.
6. **Create subscription** — `CreateSubscription` binding customer and plan, no payment profile required (plans do not require payment method).
7. **List user subscriptions** — `ListCustomerSubscriptions` to show active subscriptions in user account.
8. **Get subscription state** — `ReadSubscription` to retrieve state, price, plan, and next billing date (`NextAssessmentAt`).

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### API Operations

| Operation | Signature | Request Model + Fields | Response Envelope + Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **ListProducts** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | None (query params only). Query: `dateField` (wire: `date_field`), `filter` (wire: `filter`), `endDate` (wire: `end_date`), `endDatetime` (wire: `end_datetime`), `startDate` (wire: `start_date`), `startDatetime` (wire: `start_datetime`), `includeArchived` (wire: `include_archived`), `include` (wire: `include`), `page` (wire: `page`, default 1), `perPage` (wire: `per_page`, default 20). | `IReadOnlyList<ProductResponse>` — each has `ProductResponse.Product` (wire: `product`). `Product` fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (enum `IntervalUnit`: members `Day (day)`, `Month (month)`), `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`. | **Case B** (`SdkException<RawError>`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | Manual `page` + `perPage` (query params). | `map/operations/Products.md` |
| **ReadProductByHandle** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | None. Query: `apiHandle` (wire: `api_handle`). | `ProductResponse` — `ProductResponse.Product` (wire: `product`). Same fields as above. | **Case B** (`SdkException<RawError>`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | None. | `map/operations/Products.md` |
| **CreateCustomer** `container: client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, must pass explicitly. | `CreateCustomerRequest` wrapper: `Customer (customer): CreateCustomer !req`. `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`. Pass eShopOnWeb user ID in `Reference (wire: reference)` for idempotency. | `CustomerResponse` — `CustomerResponse.Customer` (wire: `customer`). `Customer` fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`. | **Case A** (`SdkException<CreateCustomerError>`): `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. | None. | `map/operations/Customers.md` |
| **ReadCustomerByReference** `container: client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | None. Query: `reference` (wire: `reference`). | `CustomerResponse` — `CustomerResponse.Customer` (wire: `customer`). Same fields as CreateCustomer response. | **Case B** (`SdkException<RawError>`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. On 404, customer does not exist. | None. | `map/operations/Customers.md` |
| **CreateSubscription** `container: client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, must pass explicitly. | `CreateSubscriptionRequest` wrapper: `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` key fields: `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` OR `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — use `null` or `Automatic` for auto-renewal), `CouponCode (coupon_code): string?`, `Reference (reference): string?`, `PaymentProfileId (payment_profile_id): int?` (optional; payment method not required for these plans). | `SubscriptionResponse` — `SubscriptionResponse.Subscription` (wire: `subscription`). `Subscription` fields: `Id (id): int?`, `State (state): SubscriptionState?` (enum values below), `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CancellationMethod (cancellation_method): CancellationMethod?`, `Customer (customer): Customer?`, `Product (product): Product?`. | **Case A** (`SdkException<CreateSubscriptionError>`): `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. | None. | `map/operations/Subscriptions.md` |
| **ListCustomerSubscriptions** `container: client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | None. Path param: `customerId` (wire: `customer_id`). | `IReadOnlyList<SubscriptionResponse>` — each has same `Subscription` fields. | **Case B** (`SdkException<RawError>`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | None (no paging in this endpoint). | `map/operations/Customers.md` |
| **ReadSubscription** `container: client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, must pass explicitly (pass `null` if no includes needed). | None. Path param: `subscriptionId` (wire: `subscription_id`). Query: `include` (wire: `include`). | `SubscriptionResponse` — `SubscriptionResponse.Subscription` (wire: `subscription`). Same fields as CreateSubscription response. | **Case B** (`SdkException<RawError>`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | None. | `map/operations/Subscriptions.md` |

### Enums Required

**SubscriptionState** (`MaxioAdvancedBilling.Models.Enums`, StringEnum):
- `Active (active)`
- `Trialing (trialing)`
- `Expired (expired)`
- `Unpaid (unpaid)`
- `PastDue (past_due)`
- `Suspended (suspended)`
- `Canceled (canceled)`
- `PendingCancellation (pending_cancellation)`
- And others (wire value matches enum entry per `Models/Enums/SubscriptionState.cs`).

**CollectionMethod** (`MaxioAdvancedBilling.Models.Enums`, StringEnum):
- `Automatic (automatic)` — auto-renewal (recommended)
- `Remittance (remittance)`
- `Prepaid (prepaid)`
- `Invoice (invoice)`

**IntervalUnit** (`MaxioAdvancedBilling.Models.Enums`, StringEnum):
- `Month (month)` — monthly billing
- `Day (day)` — daily billing

### Client Construction & Auth

**Namespace**: `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Servers`.

**Client signature**: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.

**Auth (Basic)**:
```csharp
options.BasicAuth = new BasicAuthCredentials 
{ 
    Username = <api_key_from_config>,  // "MAXIO_API_KEY" env var
    Password = "x"  // literal string "x"
};
```

**Environment** (`ServerEnvironment`):
- `ServerEnvironment.Us` — US host (default: `https://{subdomain}.chargify.com`)
- `ServerEnvironment.Eu` — EU host (only if EU account): `https://{subdomain}.ebilling.maxio.com`

**Server override** (for sandbox/mock):
```csharp
options.Server.Production.Us.Site = <subdomain_from_config>;  // "Subdomain" from config
// OR (for dev):
options.Server.Production.Us.BaseUrl = "http://localhost:...";
```

**DI alternative** (`ServiceCollectionExtensions.AddMaxioAdvancedBillingClient`):
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = key, Password = "x" };
    o.Environment = env;
});
```

**Namespaces for types in this plan**:
- `MaxioAdvancedBilling` — client, options
- `MaxioAdvancedBilling.Core.Authentication.Basic` — `BasicAuthCredentials`
- `MaxioAdvancedBilling.Servers` — `ServerEnvironment`
- `MaxioAdvancedBilling.Api` — operation containers (`client.Customers`, `client.Subscriptions`, `client.Products`)
- `MaxioAdvancedBilling.Models` — request/response records (`CreateCustomerRequest`, `CreateSubscriptionRequest`, `CustomerResponse`, `SubscriptionResponse`, `ProductResponse`, `CreateCustomer`, `Subscription`, `Product`, etc.)
- `MaxioAdvancedBilling.Models.Enums` — `SubscriptionState`, `CollectionMethod`, `IntervalUnit`
- `MaxioAdvancedBilling.Errors` — error types (e.g., `CreateCustomerError`)
- `MaxioAdvancedBilling.Core.ErrorResponse` — `RawError`, `ApiError`

---

## Trap Notes

⚠ **Step 1 (client & DI)** — the HTTP factory and SDK client wrapper: `HttpClient` must be long-lived and reused via `IHttpClientFactory`; the SDK client may be transient. The `MaxioAdvancedBillingClient` constructor does **not** build the `HttpClient` — you pass one. **MUST load `dotnet-client-initialization`** before wiring DI or calling the constructor.

⚠ **Step 2 (auth & config)** — Basic auth credentials and environment: `Maxio:ApiKey` and `Maxio:Subdomain` bind from env vars; `Maxio:Environment` is `us` or `eu` (parsed to `ServerEnvironment` enum). Set credentials **before** constructing the client. **MUST load `dotnet-authentication`** before wiring credentials or reading auth in DI.

⚠ **Step 3 (operations & models)** — calling endpoints and building request bodies: `ListProducts` takes many optional query params (pass `null` to skip); `CreateCustomer`, `CreateSubscription` take nullable request bodies that **must** be passed explicitly even if `null`. Enums (`CollectionMethod`, `IntervalUnit`) are `StringEnum<T>`, not C# enums — build via static members (e.g., `CollectionMethod.Automatic`) or `Type.FromValue("wire_value")`. Unions (if any) are constructed via factory methods and read via `TryGet…` accessors. **MUST load `dotnet-calling-endpoints`** before writing the first operation call.

⚠ **Step 4 (request/response fields)** — model records: all fields are immutable, set in the object initializer; `required` fields must be present; nullable fields (`T?`) are optional. Response envelopes wrap the payload in one field (e.g., `ProductResponse.Product`, `SubscriptionResponse.Subscription`) — reads go one level down. Enum fields deserialize from the wire JSON string; if the wire carries an unexpected value, the enum may drop silently or throw. **MUST load `dotnet-models`** before referencing union types or handling enum fallback.

⚠ **Step 5 (error boundary)** — exception handling: operations throw `SdkException<TError>` (Case A with typed error + accessors, or Case B with `RawError`). Some operations are Case A (`CreateCustomer`, `CreateSubscription`); others are Case B (`ListProducts`, `ReadProduct`, `ListCustomerSubscriptions`, `ReadSubscription`). `TryGet…` accessors on Case A errors map HTTP status to a specific shape; `TryGetRawError` is a fallback but is **not** a catch-all on typed errors — two common deserialization failures:
  - A drifted or malformed **2xx** body (missing `required` field) surfaces as `JsonException` from deserialization, **not** `SdkException` — let it escape the integration boundary.
  - A **non-2xx** body that does not match the operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, replacing the `SdkException` and destroying the HTTP status — map `JsonException` deterministically (e.g., to 5xx) and caller must not retry as success can never result.

**MUST load `dotnet-error-handling`** before writing the error boundary. These caveats are foundational to safe exception mapping.

⚠ **Step 6 (resilience & timeouts)** — retry logic and configuration: `RetryOptions` is set in `MaxioAdvancedBillingClientOptions.Retry` (default available via `RetryOptions.Default()`). `HttpMethodsToRetry` gates **status** trigger (e.g., 503), so a `503` on `POST` is not re-sent by status rule. However, a **transport failure** (`HttpRequestException`) is retried on **every** verb including `POST` — a non-idempotent write can execute more than once. `Timeout` is per-attempt, not total; `MaxRetries = 0` is rejected (floor is 1). No built-in logging hook exists. Read the spec carefully before tuning. **MUST load `dotnet-configuration-resilience`** when writing retry/timeout config.

⚠ **Both of these are always required in the error boundary:**
  - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
  - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## REQUIRED READING

Load **before implementation starts**. The sheet deliberately does not carry their full contents:

| Skill | Step |
|---|---|
| `dotnet-client-initialization` | Step 1 — client & DI setup: `HttpClient` factory and SDK client wrapper. |
| `dotnet-authentication` | Step 2 — auth credentials (Basic) and environment. |
| `dotnet-calling-endpoints` | Step 3 — operation signatures, parameter order, named arguments, and cancellation. |
| `dotnet-models` | Step 4 — request/response record fields, enums, unions, and deserialization. |
| `dotnet-error-handling` | Step 5 — exception boundary, Case A/B distinction, typed vs. raw error accessors, `JsonException` handling. |
| `dotnet-configuration-resilience` | Step 6 — retry/timeout tuning, `RetryOptions`, per-attempt semantics, transport-failure handling. |

---

## Assumptions & Blockers

**Assumptions:**
1. The eShopOnWeb user model has a field to store the Maxio customer ID (initially `null`, populated on first subscription attempt).
2. Subscription plan handles in Maxio match the backend handle strings (e.g., `eshop-pro`, `basic-plan`).
3. Subscription idempotency is keyed by eShopOnWeb user ID passed as the Maxio `Reference` field; the same reference on two `CreateSubscription` calls succeeds once, then fails on the second (or Maxio deduplicates — TBD per Maxio behavior).
4. Email validation and customer name are available in eShopOnWeb user profile before calling Maxio.
5. No Maxio payment profile is required for these plans; `PaymentProfileId` and payment attributes are omitted.
6. Public API endpoints use JWT bearer auth; Maxio client is injected into controllers.
7. Configuration bindings (`Maxio:ApiKey`, etc.) are set up in application startup.

**Blockers:**
None identified. All operations are in the map; enums are fully defined. Live traffic may reveal whether `CreateSubscription` with a duplicate `reference` field truly idempotents (currently assumed to fail on the second attempt — to be verified post-integration).

