# Integration Plan: Maxio Advanced Billing into eShopOnWeb

## Scope & Sequence

1. **Client Setup & Configuration** — Register the Maxio SDK client in DI, configure auth (API key, subdomain from environment), and override base URL if needed for sandbox.
2. **Customer Mapping Layer** — Create or update database schema to track eShopOnWeb user ↔ Maxio customer relationship; implement idempotent customer lookup/creation (use customer `reference` = eShopOnWeb user ID).
3. **Endpoint: `GET /api/subscription-plans`** — Fetch available products (Basic, Pro) from Maxio by handle, return plan metadata to client.
4. **Endpoint: `POST /api/subscriptions`** — Receive authenticated user + selected plan; create/link Maxio customer (idempotent by reference); create subscription; persist mapping; return subscription details.
5. **Endpoint: `GET /api/my-subscriptions`** — Fetch authenticated user's Maxio customer; list their active subscriptions; return subscription state and billing info.
6. **Error Boundary** — Catch SDK exceptions (typed `{Operation}Error` and `RawError`), translate to HTTP status, handle JSON deserialization failures gracefully.
7. **Testing** — Stub Maxio client in unit tests; verify idempotent customer creation and subscription state retrieval.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation Signatures & Models

| Controller & Op | Signature | Request Model + Fields | Response Envelope + Inner Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **Products.ReadProductByHandle** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — Reads product by `api_handle` (e.g., `"eshop-pro"`, `"basic-plan"`). | (query: `apiHandle` ← `api_handle`) | `ProductResponse` wraps `Product (product): Product !req` with: `Id`, `Name`, `Handle`, `AccountingCode`, `Description`, `Interval`, `IntervalUnit`, `PriceInCents`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `TrialType`, `InitialChargeInCents`, `InitialChargeAfterTrial`, `ExpirationInterval`, `ExpirationIntervalUnit`, `UpdatedAt`, `CreatedAt`, `ArchivedAt` | Case B: `SdkException<RawError>` with accessors `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Products.md` |
| **Products.ListProducts** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — Lists all products with optional filtering. 8 nullable params (all must pass explicitly or pass `null`); defaults: `page` = 1, `perPage` = 20. | (queries: `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `include_archived` ← `includeArchived`, `include` ← `include`, `page` ← `page`, `per_page` ← `perPage`) | `IReadOnlyList<ProductResponse>` — array of `ProductResponse` (each wraps a `Product` as above). | Case B: `SdkException<RawError>` | Manual `page` + `perPage` | `operations/Products.md` |
| **Customers.CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — Creates a new customer; validates `reference` uniqueness. | `CreateCustomerRequest` wraps `Customer (customer): CreateCustomer !req` with required: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)` — all `string !req`; optional: `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?` (use eShopOnWeb user ID here), `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse` wraps `Customer (customer): Customer !req` with: `Id`, `FirstName`, `LastName`, `Email`, `CcEmails`, `Organization`, `Reference`, `CreatedAt`, `UpdatedAt`, `Address`, `Address2`, `City`, `State`, `StateName`, `Zip`, `Country`, `CountryName`, `Phone`, `Verified`, `PortalCustomerCreatedAt`, `PortalInviteLastSentAt`, `PortalInviteLastAcceptedAt`, `TaxExempt`, `VatNumber`, `ParentId`, `Locale`, `DefaultSubscriptionGroupUid`, `SalesforceId`, `TaxExemptReason`, `DefaultAutoRenewalProfileId`, `Maxioid` | Case A: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] and fallback `TryGetRawError(out RawError)` | None | `operations/Customers.md` |
| **Customers.ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — Looks up customer by `reference` (eShopOnWeb user ID). Returns single exact match or 404. | (query: `reference` ← `reference`) | `CustomerResponse` (same shape as CreateCustomer response) | Case B: `SdkException<RawError>` | None | `operations/Customers.md` |
| **Subscriptions.CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — Creates a subscription for an existing or new customer. | `CreateSubscriptionRequest` wraps `Subscription (subscription): CreateSubscription !req` with optional fields (see Scope Notes below): `ProductHandle (product_handle): string?` (use `"eshop-pro"` or `"basic-plan"`), `ProductId (product_id): int?` (or use `ProductHandle`), `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?` (or use `CustomerReference`), `CustomerReference (customer_reference): string?` (eShopOnWeb user ID for idempotent lookup), `CustomerAttributes (customer_attributes): CustomerAttributes?` (to create inline), `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (set to `Automatic` or `Prepaid`; spec says payment method not required), `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false`, `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `Reference (reference): string?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` (for metered usage allocations), `Metafields (metafields): IReadOnlyDictionary<string, string>?` | `SubscriptionResponse` wraps `Subscription (subscription): Subscription?` with: `Id`, `State` (wire: `subscription_state`; type `SubscriptionState?` enum), `TrialEndsAt (trial_ends_at)`, `NextBillingAt (next_billing_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CurrentPeriodEndsAt (current_period_ends_at)`, `ActivatedAt (activated_at)`, `ExpiresAt (expires_at)`, `CanceledAt (canceled_at)`, `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `Customer (customer): Customer?`, `Product (product): Product?`, `CreditCard (credit_card): PaymentProfile?`, `Group (group): NestedSubscriptionGroup?`, `PaymentMethod (payment_method): PaymentProfile?`, `CustomPrice (custom_price): SubscriptionCustomPrice?` (and many other optional fields) | Case A: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422] and fallback `TryGetRawError(out RawError)` | None | `operations/Subscriptions.md` |
| **Subscriptions.ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — Lists subscriptions with optional filtering; 14 nullable params (all pass `null` to skip); defaults: `page` = 1, `perPage` = 20. Can filter by `state` (wire: `state` ← `SubscriptionStateFilter`), product, date range, metadata, sort. | (queries: `state` ← `state`, `product` ← `product`, `product_price_point_id` ← `productPricePointId`, `coupon` ← `coupon`, `coupon_code` ← `couponCode`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `metadata` ← `metadata`, `direction` ← `direction`, `sort` ← `sort`, `include` ← `include`, `page` ← `page`, `per_page` ← `perPage`) | `IReadOnlyList<SubscriptionResponse>` — array of `SubscriptionResponse` (same shape as CreateSubscription response). | Case B: `SdkException<RawError>` | Manual `page` + `perPage` | `operations/Subscriptions.md` |
| **Subscriptions.ReadSubscription** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — Retrieves a subscription by its Maxio-assigned ID. | (path: `subscriptionId` ← `subscription_id`; query: `include` ← `include`) | `SubscriptionResponse` (same shape as CreateSubscription response) | Case B: `SdkException<RawError>` | None | `operations/Subscriptions.md` |

### Scope Notes — Required Fields & Omissions

**CreateCustomer**: All three (`FirstName`, `LastName`, `Email`) are `required` in C# (`!req`). Pass them from authenticated user claims or eShopOnWeb profile.

**CreateSubscription**: The operation's `Notes` state "Payment information may be required to create a subscription, depending on the options for the Product being subscribed." For the sandbox (no trial, no setup fee, payment method not required), you can omit payment fields. Use `ProductHandle` (not `ProductId`) to name the plan handle (e.g., `"eshop-pro"`). Use `CustomerReference` (eShopOnWeb user ID) for idempotent lookup; if Maxio has no customer with that reference, pass `CustomerAttributes` to create one inline (avoids double-create if the request retries). **Only these fields should be sent; all others omitted**: `ProductHandle`, `CustomerReference` (for lookup/idempotent create) or `CustomerId`, `CustomerAttributes` (if creating inline), `PaymentCollectionMethod` (optional; `Automatic` if set), `Reference` (optional; an order/request ID for dedup if you retry from client). Fields **NOT sent**: trial fields (no trial), setup fees, payment profile fields, dunning/ACH agreement fields, 3DS flow fields.

**ListSubscriptions**: To list a single customer's subscriptions, query the `Customers` API instead (`ListCustomerSubscriptions`) if you have the Maxio customer ID, or loop subscriptions filtered by metadata/reference if you want to use this endpoint. No single built-in filter by customer reference; use `ReadCustomerByReference` + `ListCustomerSubscriptions` (Customers.cs) instead for user's active subscriptions.

### Enum Values (Relevant to Scope)

| Enum | Members Needed |
|---|---|
| `SubscriptionStateFilter` (wire filter on ListSubscriptions) | `Active (active)` — filter to only active subscriptions in list. |
| `SubscriptionState` (response field on Subscription) | `Trialing (trialing)`, `Active (active)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)` |
| `CollectionMethod` (on CreateSubscription) | `Automatic (automatic)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `IntervalUnit` (on Product/Subscription period) | `Day (day)`, `Month (month)` |

### Client Registration & Auth

- **Package**: `AsadAli.AdvancedBilling.Sdk` (NuGet)
- **Root namespace**: `MaxioAdvancedBilling`
- **Client class**: `MaxioAdvancedBillingClient`
- **Options class**: `MaxioAdvancedBillingClientOptions`
- **Auth**: HTTP Basic — `Username` = API key (from `MAXIO_API_KEY` env var), `Password` = literal `"x"`
- **Environment**: `ServerEnvironment.Us` (default for most accounts; Eu only if account requested EU hosting)
- **Server override** (sandbox only): Set `options.Server.Production.Us.BaseUrl` to mock/dev URL if redirecting; set `options.Server.Production.Us.Site` to subdomain (`"cp-exp-1"` for sandbox)
- **DI registration** (via companion skill): `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = new(…); })` or manual `new MaxioAdvancedBillingClient(httpClient, options)`

### Namespace Summary

| Type Group | Namespace |
|---|---|
| Client, options, enums | `MaxioAdvancedBilling` (root) |
| Operations (`client.Products`, `client.Customers`, `client.Subscriptions`) | `MaxioAdvancedBilling.Api` |
| Models (`CreateCustomer`, `CreateSubscription`, `Product`, `Subscription`, etc.) | `MaxioAdvancedBilling.Models` |
| Enums (`SubscriptionState`, `CollectionMethod`, `SubscriptionStateFilter`, etc.) | `MaxioAdvancedBilling.Models.Enums` |
| Auth (`BasicAuthCredentials`) | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Servers (`ServerEnvironment`, `ServerOptions`) | `MaxioAdvancedBilling.Servers` |
| Configuration (`RetryOptions`) | `MaxioAdvancedBilling.Core.Configuration` |
| Error classes (`CreateCustomerError`, `CreateSubscriptionError`, etc.) | `MaxioAdvancedBilling.Errors` |

---

## Trap Notes

⚠ **Step 1 (Client & DI setup)** — The SDK's `HttpClient` must be long-lived and reused via `IHttpClientFactory` (`AddHttpClient`), **not** rebuilt per request. The SDK client wrapper may be transient, but the underlying handler pipeline is expensive. **MUST load `dotnet-client-initialization`** to wire DI correctly.

⚠ **Step 2 (Authentication)** — Credentials must be set **before** client construction or in the DI callback; never hardcode the API key—load from configuration/secrets. The `Password` field is literally the string `"x"`, not a variable. **MUST load `dotnet-authentication`** before binding credentials.

⚠ **Step 3 (Idempotent customer creation)** — The `reference` field on `CreateCustomer` and the `CustomerReference` on `CreateSubscription` are your idempotency levers. Double-click prevention depends on **either** (A) creating the customer first with `CreateCustomer(reference: userId)`, catching 422 if it already exists, then creating the subscription, **or** (B) calling `CreateSubscription` with `CustomerReference` (which Maxio will look up and create if missing) **or** (C) calling `ReadCustomerByReference` first and passing the Maxio customer ID. **Which pattern you choose is YOUR CALL — not in the map**; implement the one your app's retry/idempotency model demands.

⚠ **Step 4 (Calling endpoints)** — Many optional parameters have **no C# default** and are marked nullable (`?`). You **must pass them explicitly** — either a value or `null` — in method calls. Passing nothing (positional call) will mis-bind. Always use **named arguments** for optional params. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ **Step 5 (Response envelope unwrapping)** — Every response type wraps its payload in exactly one field: `CustomerResponse.Customer`, `ProductResponse.Product`, `SubscriptionResponse.Subscription`. Reads go **one level down**; never access the wrapper directly. Same pattern for all operations.

⚠ **Step 6 (Error handling — two directions for `JsonException`):**
- A drifted or malformed **2xx** body (a missing `required` member like `Customer` in `CustomerResponse`) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary and surfaces as a 5xx.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
**MUST load `dotnet-error-handling`** — it carries the catch pattern that distinguishes these two cases and the defensive strategy for each.

⚠ **Step 7 (Retry/timeout configuration)** — `MaxRetries = 0` is rejected at client construction; the floor is 1. `HttpMethodsToRetry` gates only the **status** trigger (e.g., 503), so a `503` on a `POST` is **not** retried. A **transport failure** (`HttpRequestException`) is retried on **every** verb—including `POST`—so idempotent writes (via `reference`) can execute more than once and you need defensive dedup in the database layer. `Timeout` is **per-attempt**, not total. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

⚠ **Step 8 (Testing)** — The `HttpClient` constructor argument is the seam for stubbing in unit tests; build a test `HttpClient` (via `HttpMessageHandler` mock) and pass it. The SDK has no built-in result/no-throw variant—all operations throw `SdkException`, so wrap each call in `try/catch` or a resilience policy. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING

Load these skills **before implementation starts**. The sheet deliberately does not carry their contents; each is authoritative for its step:

| Skill | Step |
|---|---|
| `dotnet-client-initialization` | Client & DI setup (register `MaxioAdvancedBillingClient`, wire long-lived `HttpClient`) |
| `dotnet-authentication` | Set credentials (load API key from config, set `BasicAuth`) |
| `dotnet-calling-endpoints` | Call SDK operations (named arguments, nullable params, response envelope unwrapping) |
| `dotnet-models` | Build/read request/response models (records, enums, unions — none are C# enums) |
| `dotnet-error-handling` | Catch & translate exceptions (Case A typed `{Op}Error` vs Case B `RawError`; `JsonException` from 2xx deserialization vs 4xx/5xx error body) |
| `dotnet-configuration-resilience` | Tune retries, timeouts, base URL (idempotency strategies, retry gates, per-attempt vs total timeout) |
| `dotnet-testing` | Stub the SDK (`HttpClient` mock; all ops throw, no result variant) |

**In addition, these two `JsonException` rows belong in the FIRST error-boundary implementation, not a later revision** — the boundary must handle both simultaneously:
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` while the error object is being constructed, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

### Assumptions

1. **eShopOnWeb user ID is stable and unique** — will be used as Maxio `Customer.reference` for idempotent lookups across requests/retries.
2. **Sandbox site (`cp-exp-1`) exists and has Product Family `eshop-subscribe` with plans `eshop-pro` ($299/mo) and `basic-plan` ($29/mo)** — hardcoded in endpoint logic; no dynamic product fetch needed unless UI shows all available plans.
3. **No payment method is required** in sandbox — spec says "payment method not required"; plan creates will succeed without card details. (If live Maxio behavior differs, the provider's error response will guide correction.)
4. **JWT authentication is already wired on eShopOnWeb endpoints** — this plan assumes the `[Authorize]` attribute and `User.FindFirst("sub")` (or equivalent) user ID extraction work; Maxio integration is orthogonal to auth.
5. **Database schema updates (user ↔ Maxio customer mapping) are within scope** — in-memory DB is acceptable for development; the main agent owns schema design.
6. **Retries & idempotency** — the implementer will choose (A) double-create guard via `reference`, (B) `ReadCustomerByReference` first + conditional create, or (C) inline `CustomerAttributes` on subscription create; the plan provides the map facts, not the retry choreography.

### Blockers

None identified. The Maxio SDK map fully covers the operations needed (`ReadProductByHandle`, `CreateCustomer`, `ReadCustomerByReference`, `CreateSubscription`, `ListSubscriptions`, `ReadSubscription`). Error types and enums are complete. The sandbox and product/plan handles are provided by the user.

---

## Database Schema (Optional — User's Call)

If using SQL or tracked in-memory DB, consider:

```csharp
// Pseudo-schema (actual ORM/migration is YOUR CALL — not in the map)
public class MaxioCustomerMapping
{
    public int UserId { get; set; }  // eShopOnWeb user ID
    public int MaxioCustomerId { get; set; }  // Maxio customer.id (returned from CreateCustomer)
    public string Reference { get; set; }  // Denormalized copy of customer.reference for audit
    public DateTime CreatedAt { get; set; }
}

public class MaxioSubscription
{
    public int SubscriptionId { get; set; }  // Maxio subscription.id
    public int UserId { get; set; }  // Link to eShopOnWeb user
    public string ProductHandle { get; set; }  // e.g., "eshop-pro"
    public string State { get; set; }  // Maxio SubscriptionState enum value (wire: e.g., "active")
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTime SyncedAt { get; set; }  // Last time we fetched from Maxio
}
```

This is **YOUR CALL** — the map says nothing about database structure. Use it only if you choose to cache or deduplicate client-side.

