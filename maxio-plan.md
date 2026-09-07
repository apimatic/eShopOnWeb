# eShopOnWeb Maxio Advanced Billing Integration — Contract Sheet

## Scope & Sequence

1. **Client initialization & authentication** — Configure SDK with API key and subdomain from configuration
2. **Customer management (idempotent)** — Ensure a Maxio customer exists for each eShopOnWeb user (create or retrieve by reference)
3. **Plan discovery** — List available products/plans from the product family
4. **Subscription creation** — Create a subscription linking customer to a plan
5. **Subscription query** — Read subscription state (plan, price, next billing date) for account display

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model & Fields | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` envelope contains: `Customer (customer): CreateCustomer !req` · Inner fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (wire: your eShopOnWeb UserId, unique per user), `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?` | `CustomerResponse` envelope: `Customer (customer): Customer !req` · Inner: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, + other fields | **Case A**: `SdkException<CreateCustomerError>` · Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md` |
| **UpdateCustomer** | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` | `UpdateCustomerRequest` envelope: `Customer (customer): UpdateCustomer !req` · Inner fields: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?` | `CustomerResponse` envelope: `Customer (customer): Customer !req` | **Case A**: `SdkException<UpdateCustomerError>` · Accessors: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md` |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param (wire): `reference` ← `reference` | `CustomerResponse` envelope: `Customer (customer): Customer !req` | **Case B**: `SdkException<RawError>` · StatusCode, ReadAsString(), ReadAsJson&lt;T&gt;() | None | `operations/Customers.md` |
| **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Path param: `productFamilyId` (wire: the product-family handle, e.g. `"eshop-subscribe"`) · Optional query params (pass `null` to skip): `dateField`, `filter`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `includeArchived`, `include` · Pagination: `page` (default 1), `perPage` (default 20) | `IReadOnlyList<ProductResponse>` · Each element envelope: `Product (product): Product !req` · Inner fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?` | **Case A**: `SdkException<ListProductsForProductFamilyError>` · Accessors: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | Manual `page`+`perPage` | `operations/ProductFamilies.md` |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` envelope: `Subscription (subscription): CreateSubscription !req` · Inner fields: `CustomerId (customer_id): int?` (Maxio customer ID; omit if using `customer_reference`), `CustomerReference (customer_reference): string?` (eShopOnWeb UserId; alternative to `customer_id`), `ProductHandle (product_handle): string?` (wire: plan handle, e.g. `"eshop-pro"` or `"basic-plan"`), `ProductId (product_id): int?` (alternative to handle), `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `Reference (reference): string?` (optional: eShopOnWeb subscription reference for idempotency), `CouponCode (coupon_code): string?`, `PaymentProfileId (payment_profile_id): int?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?` (credit card or bank; not required per your sandbox setup), `CustomerAttributes (customer_attributes): CustomerAttributes?` (create customer inline if needed), `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` (metered component usage, if applicable) | `SubscriptionResponse` envelope: `Subscription (subscription): Subscription?` · Inner fields: `Id (id): int?` (Maxio subscription ID), `State (state): SubscriptionState?` (e.g. "active", "trial_ended"), `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date), `ActivatedAt (activated_at): DateTimeOffset?`, `ExpiresAt (expires_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `Reference (reference): string?`, `Customer (customer): Customer?`, `Product (product): Product?` | **Case A**: `SdkException<CreateSubscriptionError>` · Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | None | `operations/Subscriptions.md` |
| **ReadSubscription** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | Path param: `subscriptionId` · Query param: `include` (optional; pass `null` to skip) | `SubscriptionResponse` envelope: `Subscription (subscription): Subscription?` · Inner: `Id`, `State`, `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `ExpiresAt`, `Reference`, `Customer`, `Product` | **Case B**: `SdkException<RawError>` · StatusCode, ReadAsString(), ReadAsJson&lt;T&gt;() | None | `operations/Subscriptions.md` |
| **ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Path: none · Query params (pass `null` to skip): `state` (filter by SubscriptionStateFilter enum), `product`, `metadata`, etc. · Pagination: `page` (default 1), `perPage` (default 20) | `IReadOnlyList<SubscriptionResponse>` · Each element envelope: `Subscription (subscription): Subscription?` | **Case B**: `SdkException<RawError>` | Manual `page`+`perPage` | `operations/Subscriptions.md` |

### Enum Values

**`SubscriptionState`** (namespace `MaxioAdvancedBilling.Models.Enums`):
Member names: `Trialing`, `AssertingFutureRenewal`, `Recurring`, `Expired`, `Expiring`, `Suspended`, `SuspendedAwaitingUpdatedPaymentMethod`, `Canceled`, `Paused`, `Pending`, `FailedToCreate`, `AwaitingSignup`, `PendingCancellation`

**`IntervalUnit`** (namespace `MaxioAdvancedBilling.Models.Enums`):
Member names: `Day`, `Month`, `Year`

**`SubscriptionStateFilter`** (namespace `MaxioAdvancedBilling.Models.Enums`):
Member names: `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `Paused`, `Pending`, `Suspended`, `SuspendedAwaitingUpdatedPaymentMethod`, `Trialing`

### Client Construction & Server Configuration

From `sdk-map.md`:
- **Client class**: `MaxioAdvancedBillingClient` (namespace `MaxioAdvancedBilling`)
- **Options class**: `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`)
- **Auth**: HTTP **Basic** — set `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`)
- **Environment**: `options.Environment = ServerEnvironment.Us` (namespace `MaxioAdvancedBilling.Servers`) — default, sufficient for US hosting
- **Server override (if needed)**: `options.Server.Production.Us.Site = "<subdomain>"` or `options.Server.Production.Us.BaseUrl = "<override_url>"` (namespace `MaxioAdvancedBilling.Servers`)
- **Retry/timeout configuration**: via `options.Retry` (type `RetryOptions`, namespace `MaxioAdvancedBilling.Core.Configuration`)

---

## Trap Notes

⚠ Step 1 (client initialization & auth) — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 1 (DI or manual client construction) — The `HttpClient` constructor argument must be a long-lived, reused instance (via `IHttpClientFactory`); do not create a new `HttpClient` per request. **MUST load `dotnet-client-initialization`** before writing the client construction.

⚠ Step 2 (customer idempotency) — `ReadCustomerByReference` is the query to check if a customer already exists; `CreateCustomer` is only called if the reference is not found. When creating, the `reference` field **must** map to eShopOnWeb's UserId and must be unique (Maxio enforces uniqueness). **MUST load `dotnet-calling-endpoints`** for parameter binding and calling patterns.

⚠ Step 3 (plan discovery) — `ListProductsForProductFamily` requires the product-family **handle** (not ID) as the path parameter. Pass `productFamilyId: "eshop-subscribe"` (from configuration). All other parameters are optional query params; pass `null` to skip. **MUST load `dotnet-calling-endpoints`** for optional-parameter semantics.

⚠ Step 4 (subscription creation) — `CreateSubscription` accepts either `customer_id` (Maxio int ID) or `customer_reference` (eShopOnWeb UserId string); do not pass both. The plan is identified by `product_handle` (e.g. `"eshop-pro"`) or `product_id` (int). Payment method is optional per your sandbox setup; omit it. **MUST load `dotnet-calling-endpoints`** to understand which fields must be passed explicitly vs. nullable vs. defaulted.

⚠ Step 4–5 (response envelope nesting) — `SubscriptionResponse` wraps `Subscription` in one field; reads go one level down: access `response.Subscription.NextAssessmentAt`, not `response.NextAssessmentAt`. Same applies to `CustomerResponse`, `ProductResponse`, etc. **MUST load `dotnet-models`** to understand envelope shapes and when to unwrap.

⚠ Step 5 (error handling, two directions for JsonException) — A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape; a non-2xx body that doesn't match the operation's `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed. **MUST load `dotnet-error-handling`** before writing the boundary.

---

## REQUIRED READING

Load these companion skills before implementation starts. The sheet deliberately does not carry their contents; each names the step it governs:

| Skill | Step |
|---|---|
| `dotnet-client-initialization` | Client & DI setup (step 1) |
| `dotnet-authentication` | Credentials wiring (step 1) |
| `dotnet-calling-endpoints` | All operation calls (steps 2–5) |
| `dotnet-models` | Request/response envelope shapes (steps 2–5) |
| `dotnet-configuration-resilience` | Retry, timeout, base-URL configuration (step 1) |
| `dotnet-error-handling` | Exception boundary (step 5) |

The following two caveat rows apply to **every** integration and belong in the FIRST sheet, not a later revision — the boundary is written early:

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

**Assumptions:**
1. The eShopOnWeb application maintains user identity in a `UserId` (string or int serialized to string) that can be passed as the Maxio customer `reference` field and will remain stable for the lifetime of the user account.
2. The Maxio site (`cp-exp-4`) is reachable from the application at `https://<subdomain>.chargify.com` (US endpoint; configured via `options.Server.Production.Us.Site` or read from `Maxio:Subdomain` config key).
3. Payment information is **not** required to create subscriptions in the sandbox (your spec confirms this; production may differ).
4. The application's PublicApi endpoints will validate JWT tokens independently; the Maxio SDK integration does not authenticate the user (it only uses the SDK credentials).

**Blockers:**
None identified. The map covers all required operations.

---

## Configuration Binding

All Maxio configuration (API key, subdomain, product family handle) is read from the .NET user-secrets store, bound to a `Maxio:` section. Never hard-code values. The application must wire these keys:

- `Maxio:ApiKey` — the API key (maps to BasicAuth username)
- `Maxio:Subdomain` — the site subdomain (maps to `options.Server.Production.Us.Site`)
- `Maxio:ProductFamilyHandle` — product-family handle to pass to `ListProductsForProductFamily` (e.g. `"eshop-subscribe"`)
- `Maxio:BaseUrl` (optional) — override the full base URL (if set, use verbatim; skip the subdomain pattern)

The application's DI container or configuration loader must populate these before the SDK client is constructed.
