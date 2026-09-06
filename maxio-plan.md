# Maxio Advanced Billing Integration — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

1. **Fetch subscription plans** — `ListProducts` to retrieve available plans from the configured product family
2. **Ensure Maxio customer exists** — `ReadCustomerByReference` (idempotent lookup by eShopOnWeb user ID) or `CreateCustomer` on missing
3. **Create subscription** — `CreateSubscription` to enroll the user in a selected plan
4. **Query user's subscriptions** — `ListCustomerSubscriptions` to populate account view
5. **HTTP endpoint binding** — Map JWT caller identity to eShopOnWeb user; persist Maxio customer ID mapping

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Controller | Method | Signature | Request Model + Fields | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| `Products` | `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `null` for all; use defaults or omit filter | `IReadOnlyList<ProductResponse>` — each element has `Product (product): Product` field. `Product` fields: `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `ExpirationInterval`, `ExpirationIntervalUnit`, `RequireCreditCard`, `Taxable`, `AccountingCode`, `ArchivedAt` | `SdkException<RawError>` (Case B) | manual `page`+`perPage` | `map/operations/Products.md` |
| `Customers` | `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `reference` (wire: `reference`) — eShopOnWeb user ID as string | `CustomerResponse` — wraps `Customer (customer): Customer` field. `Customer` fields: `Id`, `FirstName`, `LastName`, `Email`, `Organization`, `Reference`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `CreatedAt`, `UpdatedAt`, `Verified`, `TaxExempt`, `VatNumber`, `ParentId`, `Locale` | `SdkException<RawError>` (Case B) — 404 on not found | none | `map/operations/Customers.md` |
| `Customers` | `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` required explicitly | `CreateCustomerRequest` wraps `Customer (customer): CreateCustomer !req` — all fields optional except `FirstName`, `LastName`, `Email` (all `string` required). Optional: `CcEmails`, `Organization`, `Reference`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt` (bool), `TaxExemptReason`, `ParentId` (int), `SalesforceId` (all optional `string?` or nullable) | `CustomerResponse` — wraps `Customer (customer): Customer` field (same structure as ReadCustomerByReference) | `SdkException<CreateCustomerError>` (Case A): `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md` |
| `Subscriptions` | `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` required explicitly | `CreateSubscriptionRequest` wraps `Subscription (subscription): CreateSubscription !req` — key fields: `ProductHandle` (wire: `product_handle`), `ProductId` (wire: `product_id`), `ProductPricePointHandle`, `ProductPricePointId`, `CustomerId` (wire: `customer_id`), `CustomerReference` (wire: `customer_reference`), `PaymentCollectionMethod` (wire: `payment_collection_method`, enum `CollectionMethod`), `PaymentProfileId` (wire: `payment_profile_id`), `CouponCode` (wire: `coupon_code`), `CouponCodes` (wire: `coupon_codes`), `Reference`, `ReceivesInvoiceEmails` (wire: `receives_invoice_emails`), `NetTerms` (wire: `net_terms`), `InitialBillingAt` (wire: `initial_billing_at`, `DateTimeOffset?`), `NextBillingAt` (wire: `next_billing_at`, `DateTimeOffset?`), `DeferSignup` (wire: `defer_signup`, bool), `Currency`, `ExpiresAt` (wire: `expires_at`), `CustomerAttributes` (wire: `customer_attributes`), `Components` (wire: `components`, array of component allocations) | `SubscriptionResponse` — wraps `Subscription (subscription): Subscription?` field. `Subscription` fields: `Id`, `State` (enum), `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `ExpiresAt`, `CreatedAt`, `UpdatedAt`, `CanceledAt`, `CancelAtEndOfPeriod`, `CouponCode`, `PaymentCollectionMethod`, `Customer`, `Product`, `PaymentType`, `Reference`, `ProductPricePointId`, `NetTerms`, `Currency`, `Locale`, `ReceivesInvoiceEmails`, `DunningCommunicationDelayEnabled` | `SdkException<CreateSubscriptionError>` (Case A): `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md` |
| `Customers` | `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` (path param, wire: `customer_id`) — Maxio customer ID | `IReadOnlyList<SubscriptionResponse>` — each element wraps `Subscription (subscription): Subscription?` | `SdkException<RawError>` (Case B) | none | `map/operations/Customers.md` |

### Enums

From `MaxioAdvancedBilling.Models.Enums`:

**`CollectionMethod`** (wire values for `payment_collection_method` on subscription):
- `Automatic` (wire: `"automatic"`)
- `Remittance` (wire: `"remittance"`)
- `Invoice` (wire: `"invoice"`)
- Source: `map/models/enums.md`

**`SubscriptionState`** (read-only on subscription):
- `Active` (wire: `"active"`)
- `Pending` (wire: `"pending"`)
- `Trialing` (wire: `"trialing"`)
- `AwaitingSignup` (wire: `"awaiting_signup"`)
- `Canceled` (wire: `"canceled"`)
- `Expired` (wire: `"expired"`)
- `PastDue` (wire: `"past_due"`)
- `OnHold` (wire: `"on_hold"`)
- `Paused` (wire: `"paused"`)
- Source: `map/models/enums.md`

**`IntervalUnit`** (subscription period unit):
- `Day` (wire: `"day"`)
- `Month` (wire: `"month"`)
- `Year` (wire: `"year"`)
- `Week` (wire: `"week"`)
- Source: `map/models/enums.md`

### Client Construction & Auth

All operations use `client.Subscriptions`, `client.Customers`, `client.Products` controllers on `MaxioAdvancedBillingClient`.

**Auth (HTTP Basic):**
- `Username` ← `Maxio:ApiKey` configuration
- `Password` ← literal string `"x"`
- Namespace: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`

**Environment selection:**
- `ServerEnvironment.Us` (default) — US hosting
- `ServerEnvironment.Eu` — EU hosting
- Namespace: `MaxioAdvancedBilling.Servers.ServerEnvironment`

**Server override (if Maxio sandbox):**
- `options.Server.Production.Us.Site` ← `Maxio:Subdomain` (e.g., `"cp-exp-1"`)
- Namespace: `MaxioAdvancedBilling` (client options)

---

## Trap Notes

⚠ **Step 1 (client initialization)** — The `HttpClient` you pass to `MaxioAdvancedBillingClient` must be long-lived and reused; do not create a new one per request. Use `IHttpClientFactory` or DI to manage it. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (authentication)** — Basic auth credentials must be set **before** constructing the client, or in the DI callback. The API key is `Maxio:ApiKey`; the password is always literal `"x"`. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 3 (calling endpoints)** — Many optional parameters on `ListProducts` have no C# default; passing positional args will mis-bind them. Use named arguments and explicitly pass `null` to skip optional filters. `ReadCustomerByReference` returns 404 (RawError) if the reference does not exist — catch and handle as "customer not found, create one." **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (response models)** — Responses wrap their payload in a single field (e.g., `ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`). Read one level down to get the actual model. Enums like `SubscriptionState` and `CollectionMethod` are `StringEnum<T>`, not C# enums; construct with `SubscriptionState.FromValue("active")` or use static members. **MUST load `dotnet-models`** before deserializing responses.

⚠ **Step 5 (error boundary)** — Two sources of `JsonException` require opposite handling:
  - A drifted/malformed **2xx body** (missing required `Customer` field on `CustomerResponse`) surfaces as `JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary.
  - A **non-2xx body** that does not match the operation's error shape throws `JsonException` *during error object construction*, replacing the `SdkException` and destroying the HTTP status with it — a boundary that maps every `JsonException` to 5xx reports a deterministic rejection as outage, and retries fail forever.

  **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ **Step 6 (resilience & config)** — Retry/timeout options do **not** bound a whole call; `Timeout` is per-attempt. `HttpMethodsToRetry` gates only the status trigger, not transport failures — `POST` calls are retried on `HttpRequestException` even if they are not listed. `MaxRetries` has a floor of 1 (0 is rejected). `Subdomain` must be set in server options or via configuration. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

⚠ **Step 7 (testing)** — The SDK has no `…Result` no-throw variants; every operation throws. The `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry their contents:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1: client & DI setup |
| `dotnet-authentication` | Step 2: basic auth credential wiring |
| `dotnet-calling-endpoints` | Step 3: endpoint calls & parameter binding |
| `dotnet-models` | Step 4: request/response deserialization & enum construction |
| `dotnet-error-handling` | Step 5: exception boundary & `JsonException` handling (mandatory even when trap notes are few — see two caveat rows below) |
| `dotnet-configuration-resilience` | Step 6: retry/timeout/base-URL configuration |
| `dotnet-testing` | Step 7: SDK stub patterns |

**Two mandatory hazard rows for the boundary** (from `dotnet-error-handling` coverage):
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb user IDs are stable strings suitable as Maxio customer reference values (no special characters that violate Maxio's reference uniqueness constraint).
- Maxio customer and subscription mappings are persisted in eShopOnWeb's own database (e.g., a `UserMaxioMapping` table with `UserId` → `MaxioCustomerId`, or a denormalized field on the User entity).
- The three HTTP endpoints (`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`) are authenticated via JWT token; the caller identity is extracted from the token claims and mapped to an eShopOnWeb user ID.
- Payment method is NOT required by Maxio for the configured plans (per requirement: "payment method not required"). If this changes, the implementation must add payment profile creation/capture logic.
- Subscription state `Active` is the only acceptable state for displaying in user's account; other states (pending, canceled, etc.) are handled separately or not shown.

**Blockers:** None identified. The SDK map and source are sufficient to ground all operations and models in scope.

---

## Notes on Configuration

| Config Key | Purpose | Type | Example | Source |
|---|---|---|---|---|
| `Maxio:ApiKey` | Basic auth username | `string` | (API key from Maxio sandbox) | `MAXIO_API_KEY` env var |
| `Maxio:Subdomain` | Maxio site subdomain | `string` | `"cp-exp-1"` | `MAXIO_SITE_SUBDOMAIN` env var |
| `Maxio:Environment` | US or EU hosting | `string` enum → `ServerEnvironment` | `"us"` or `"eu"` | `MAXIO_ENVIRONMENT` env var (inferred or explicit) |
| `Maxio:ProductFamilyHandle` | Default product family for lookups | `string` | `"eshop-subscribe"` | `MAXIO_DEFAULT_PRODUCT_FAMILY` env var |
| `Maxio:BaseUrl` | (Optional) override API base address | `string` | `"http://localhost:8080"` | Not in env; override in code if needed |

All configuration is bound via ASP.NET Core `IConfiguration` to a `MaxioSettings` or similar POCO, which is injected into the service layer.
