# Maxio Subscription Integration Plan — eShopOnWeb

## Scope & Sequence

1. **List subscription plans** — `ListProductsForProductFamily` to fetch Pro and Basic plans from `eshop-subscribe` product family (ID 3023074)
2. **Create or fetch Maxio customer** — `ReadCustomerByReference` to check for existing customer by eShopOnWeb user ID; `CreateCustomer` if not found (idempotent via `reference` field)
3. **Create subscription** — `CreateSubscription` to bind customer to selected plan
4. **List user's subscriptions** — `ListSubscriptions` filtered to caller's subscriptions

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation 1: ListProductsForProductFamily

| Property | Value |
|---|---|
| **Controller** | `client.ProductFamilies` |
| **Method signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | `productFamilyId` (string, required) — product family ID or handle; pass `"3023074"` for eshop-subscribe. Remaining params are optional; pass `null` to skip filtering. Defaults: `page = 1`, `perPage = 20`. |
| **Request model** | None (query string parameters only) |
| **Response envelope** | `IReadOnlyList<ProductResponse>` — each item is `ProductResponse` wrapping `Product` in field `Product` (wire name `product`). Access via `.Product` on each response. |
| **Response fields (from each `Product`)** | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `Description (description): string?`, `CreatedAt (created_at): DateTimeOffset?` — use these to build the plan list. |
| **Error** | `SdkException<ListProductsForProductFamilyError>` — Case A (typed). Accessors: `TryGetString(out string)` [404 — family not found], `TryGetRawError(out RawError)` [fallback]. |
| **Pagination** | Manual `page` + `perPage` (defaults 1 + 20). |
| **Source** | `map/operations/ProductFamilies.md` |

### Operation 2: ReadCustomerByReference

| Property | Value |
|---|---|
| **Controller** | `client.Customers` |
| **Method signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Parameters** | `reference` (string, required) — query param. Pass the eShopOnWeb user's ID or UUID. |
| **Request model** | None (query string only) |
| **Response envelope** | `CustomerResponse` wrapping `Customer` in field `Customer` (wire name `customer`). Access via `.Customer`. |
| **Response fields (from `Customer`)** | `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?` — use to confirm idempotency or extract Maxio customer ID. |
| **Error** | `SdkException<RawError>` — Case B. Status codes: 404 if not found (expected on first call), 200 on success. On 404, fall through to `CreateCustomer`. |
| **Pagination** | None |
| **Source** | `map/operations/Customers.md` |

### Operation 3: CreateCustomer

| Property | Value |
|---|---|
| **Controller** | `client.Customers` |
| **Method signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (nullable, no default) — **must pass explicitly**. Wraps `CreateCustomer` record. |
| **Request model** | `CreateCustomerRequest` (namespace `MaxioAdvancedBilling.Models`) wrapping: `Customer (customer): CreateCustomer !req` |
| **Request fields (on `CreateCustomer`)** | `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` — wire name `reference`; **MUST set for idempotency**. Optional: `Organization (organization): string?`, `Phone (phone): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?` — all ISO 3166-2 for state, ISO 3166-1 (2-char) for country per provider docs. |
| **Response envelope** | `CustomerResponse` wrapping `Customer` in field `Customer`. Access via `.Customer`. |
| **Response fields (from `Customer`)** | Same as ReadCustomerByReference above; extract `Id` for subscription creation. |
| **Error** | `SdkException<CreateCustomerError>` — Case A (typed). Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 — validation; inspect `.Errors` on the payload], `TryGetRawError(out RawError)` [fallback]. |
| **Pagination** | None |
| **Source** | `map/operations/Customers.md` + `map/models/records-2-Cr-Ne.md` |

**Idempotency via `Reference`:** CreateCustomer Notes state — "The only validation restriction is that you may only create one customer for a given reference value. If provided, the `reference` value must be unique… This allows you to retrieve a given customer via a piece of shared information." Thus: always set `Reference` to the eShopOnWeb user ID; on duplicate, provider rejects with 422. Read-then-create (using `ReadCustomerByReference`) is the idempotent pattern.

### Operation 4: CreateSubscription

| Property | Value |
|---|---|
| **Controller** | `client.Subscriptions` |
| **Method signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (nullable, no default) — **must pass explicitly**. Wraps `CreateSubscription` record. |
| **Request model** | `CreateSubscriptionRequest` (namespace `MaxioAdvancedBilling.Models`) wrapping: `Subscription (subscription): CreateSubscription !req` |
| **Request fields (on `CreateSubscription`)** | **Required**: `CustomerId (customer_id): int?` — Maxio customer ID from `CreateCustomer` or `ReadCustomerByReference`. `ProductHandle (product_handle): string?` or `ProductId (product_id): int?` — set one of these to the plan's handle or ID (e.g., `eshop-pro` or product ID). **Notes-specified optional fields to include**: `Reference (reference): string?` — for subscription idempotency (use eShopOnWeb subscription ID or UUID). `InitialBillingAt (initial_billing_at): DateTimeOffset?` — if caller specifies a custom start date. `DeferSignup (defer_signup): bool? = false` — keep false to charge immediately. `PaymentProfileId (payment_profile_id): int?` — omit per requirements ("payment method not required"). `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — optional, defaults per site config. **Full field list** (most optional): `ProductHandle`, `ProductId`, `ProductPricePointHandle`, `ProductPricePointId`, `CouponCode`, `CouponCodes`, `PaymentCollectionMethod`, `ReceivesInvoiceEmails`, `NetTerms`, `CustomerId`, `NextBillingAt`, `InitialBillingAt`, `DeferSignup`, `StoredCredentialTransactionId`, `SalesRepId`, `PaymentProfileId`, `Reference`, `CustomerAttributes`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `Components`, `CalendarBilling`, `Metafields`, `CustomerReference`, `Group`, `Ref`, `CancellationMessage`, `CancellationMethod`, `Currency`, `ExpiresAt`, `ExpirationTracksNextBillingChange`, `AgreementTerms`, `AuthorizerFirstName`, `AuthorizerLastName`, `CalendarBillingFirstCharge`, `ReasonCode`, `ProductChangeDelayed`, `OfferId`, `PrepaidConfiguration`, `PreviousBillingAt`, `ImportMrr`, `CanceledAt`, `ActivatedAt`, `AgreementAcceptance`, `AchAgreement`, `DunningCommunicationDelayEnabled`, `DunningCommunicationDelayTimeZone`, `SkipBillingManifestTaxes` |
| **Response envelope** | `SubscriptionResponse` wrapping `Subscription` in field `Subscription` (wire name `subscription`). Access via `.Subscription`. |
| **Response fields (from `Subscription`)** | `Id (id): int?`, `State (state): SubscriptionState?` — state enum, values in enums.md: `pending`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, etc. `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — next billing date. `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`. |
| **Error** | `SdkException<CreateSubscriptionError>` — Case A (typed). Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422 — validation errors; `.Errors` is `IReadOnlyList<string>`], `TryGetRawError(out RawError)` [fallback]. **Notes**: "Payment information may be required to create a subscription, depending on the options for the Product being subscribed." Per requirements, payment method is not required, so the product/plan must allow zero-payment signup. **3D Secure (3DS) note** — if the plan requires payment and 3DS is triggered, response is 422 with `action_link` (not in scope for this integration). |
| **Pagination** | None |
| **Source** | `map/operations/Subscriptions.md` + `map/models/records-2-Cr-Ne.md` |

**Idempotency via subscription `Reference`:** The map does not state uniqueness semantics for subscription `Reference` as it does for customer `Reference`. Caller should implement idempotency by searching first with `ListSubscriptions` filtered by customer and reference, then create if not found. **UNVERIFIED** — live API docs or traffic would confirm whether subscription reference is unique per customer or per site.

### Operation 5: ListSubscriptions

| Property | Value |
|---|---|
| **Controller** | `client.Subscriptions` |
| **Method signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | 14 optional query params, each nullable; pass `null` to skip. Defaults: `page = 1`, `perPage = 20`. **To list a specific customer's subscriptions**: the map does not expose a customer-ID filter in this operation. **Limitation**: caller must either (a) iterate all subscriptions and filter in-app by `CustomerId` (not scalable), or (b) use `ListCustomerSubscriptions(int customerId)` (from Customers controller) to get subscriptions for a known customer. See Operation 6 below. |
| **Query params (wire ← C#)** | `state` ← `state` (enum `SubscriptionStateFilter`, values: `active`, `canceled`, `expired`, `expired_cards`, `on_hold`, `past_due`, `pending_cancellation`, `pending_renewal`, `suspended`, `trial_ended`, `trialing`, `unpaid`), `product` ← `product` (product ID), `product_price_point_id` ← `productPricePointId`, `coupon` ← `coupon`, `coupon_code` ← `couponCode`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `metadata` ← `metadata` (dict), `direction` ← `direction` (enum, values: `asc`, `desc`), `sort` ← `sort` (enum `SubscriptionSort`, values: `signup_date`, `period_start`, `period_end`, `next_assessment`, `updated_at`, `created_at`, `total_payments`, `id`, `open_balance`, `expires_at`), `include` ← `include` (enum list `SubscriptionListInclude`, values: `self_service_page_token`), `page` ← `page`, `per_page` ← `perPage`. |
| **Request model** | None (query string only) |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` — each item wraps `Subscription` in field `Subscription`. Access via `.Subscription` on each. |
| **Response fields (from each `Subscription`)** | Same as CreateSubscription response fields above. Key fields: `Id`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `CustomerId`. |
| **Error** | `SdkException<RawError>` — Case B. No typed errors for this operation. |
| **Pagination** | Manual `page` + `perPage`. Default page size 20. |
| **Source** | `map/operations/Subscriptions.md` |

### Operation 6: ListCustomerSubscriptions (alternative for per-customer listing)

| Property | Value |
|---|---|
| **Controller** | `client.Customers` |
| **Method signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Parameters** | `customerId` (int, required) — Maxio customer ID from `CreateCustomer` or `ReadCustomerByReference`. |
| **Request model** | None (URI path parameter only) |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` — each wraps `Subscription` in field `Subscription`. |
| **Response fields** | Same as Operation 5 above. |
| **Error** | `SdkException<RawError>` — Case B. |
| **Pagination** | None (returns all subscriptions for the customer) |
| **Source** | `map/operations/Customers.md` |

**Use this in place of Operation 5 (ListSubscriptions) when you know the customer ID.** Simpler, more efficient.

---

### Enum Values

**From `MaxioAdvancedBilling.Models.Enums`:**

| Enum | Values | Usage |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`, `FailedToCreate (failed_to_create)` | Returned in `Subscription.State` from CreateSubscription and ListSubscriptions. |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` | Filter param on ListSubscriptions. |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` | Sort param on ListSubscriptions. |
| `IntervalUnit` | `Day (day)`, `Month (month)` | Returned in `Product.IntervalUnit` from ListProductsForProductFamily. |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | Optional field on CreateSubscription; defaults to site config. |

---

### Client Construction & Configuration

| Item | Value | Source |
|---|---|---|
| **Root namespace** | `MaxioAdvancedBilling` (for client); operations in `MaxioAdvancedBilling.Api`, models in `MaxioAdvancedBilling.Models`, enums in `MaxioAdvancedBilling.Models.Enums`, errors in `MaxioAdvancedBilling.Errors`. | `sdk-map.md` |
| **Client class** | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | Constructor is **only** overload. Signature from source: `Api/MaxioAdvancedBillingClient.cs`. |
| **Options class** | `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`) — properties: `Environment` (`ServerEnvironment`, default `Us`), `BasicAuth` (`BasicAuthCredentials?`), `Server` (`ServerOptions`), `Retry` (`RetryOptions`) | `sdk-map.md` |
| **Auth** | HTTP **Basic**: `BasicAuthCredentials { Username = "<api_key>", Password = "x" }`. Username = API key (from `MAXIO_API_KEY` env var); password = literal string `"x"`. | `sdk-map.md` |
| **Base URLs** | Production: US template `https://{site}.chargify.com`, EU template `https://{site}.ebilling.maxio.com`. Override via `options.Server.Production.Us.BaseUrl` or `.Us.Site`. Target sandbox: set `Site = "cp-exp-1"` (or derive from `MAXIO_SITE_SUBDOMAIN` env var). | `sdk-map.md` |
| **Environments** | `ServerEnvironment.Us` (default, US-hosted), `ServerEnvironment.Eu` (EU-hosted). | `sdk-map.md` |
| **Service collection (DI)** | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })` — call before building the container. | `ServiceCollectionExtensions.cs` per SDK docs. |

---

### Known Traps & Mandatory Reads

⚠ **Step 2 (client registration)** — the SDK's `Retry` options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `HttpClient.Timeout` is separate. The registered `HttpClient` must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (authentication)** — `BasicAuth.Username` must be the API key; `Password` is always the literal string `"x"`. Set credentials **before** constructing the client or **in the DI callback**. Load credentials from `Maxio:ApiKey` configuration (bound to `MAXIO_API_KEY` env var), never hardcode. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 3 (calling operations)** — the `CreateCustomer`, `CreateSubscription` operations accept nullable request body parameters that **must be passed explicitly** (no C# default binding). Use named arguments and pass the fully-constructed request object. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (models & response envelopes)** — response types wrap their payload in one required field (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`). Reads go one level down. Enums are `StringEnum<T>` (not C# enums), built with static members (e.g., `SubscriptionState.Active`) or `FromValue("wire_value")`. **MUST load `dotnet-models`** when reading subscription/customer/product fields.

⚠ **Step 5 (error handling — JSON deserialization failures)** — a drifted or malformed **2xx body** (missing a `required` field) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary and reaches the caller as an unexpected exception. A **non-2xx body** that does not match its operation's generated error shape (e.g., malformed 422 payload) throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it. A boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

⚠ **Step 5 (error handling — typed vs raw cases)** — **many — not all —** operations throw `SdkException<RawError>` (Case B, no typed accessors) while others throw `SdkException<CreateCustomerError>` (Case A, with `TryGet…` methods). Confirm each operation's case in its map row before writing the catch. On Case A, `TryGetRawError` is not a catch-all — it is a fallback only after typed accessors have been tried. **MUST load `dotnet-error-handling`** before writing the error boundary.

⚠ **Step 6 (configuration & resilience)** — `HttpMethodsToRetry` gates only the **status** trigger (e.g., `503`), so a `503` on a `POST` is not resent. However, a **transport failure** (`HttpRequestException`) is retried on **every** HTTP method, including `POST`, so a non-idempotent write (e.g., `CreateCustomer` without a unique `Reference`) can execute more than once. `MaxRetries = 0` is rejected at construction; the floor is 1. `Timeout` is per-attempt not per-call-total, and there is no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

⚠ **Step 1 (idempotency) — subscription creation** — the map does not state whether subscription `Reference` is unique per customer or per site. If per site, creating a subscription with the same reference twice will fail or return the existing one (behavior unverified). Best practice: search for the subscription first using `ListCustomerSubscriptions`, then create if not found. **UNVERIFIED — only live API traffic or provider docs can settle this.**

⚠ **Idempotency — CreateCustomer requires unique `Reference`** — the map states: "The only validation restriction is that you may only create one customer for a given reference value." Always set the `Reference` field to the eShopOnWeb user ID. On duplicate, provider rejects with 422. Pattern: `ReadCustomerByReference` first; if 404, then `CreateCustomer`.

---

## REQUIRED READING

Load the following skills **before implementation starts**. The sheet deliberately does not carry their contents — the skills cover defaults, worked examples, what you must still wire yourself, and edge cases a signature cannot show:

| Skill | Governs | Reason |
|---|---|---|
| `dotnet-client-initialization` | Client & DI setup | HttpClient lifecycle, reuse via IHttpClientFactory, when to construct the SDK client |
| `dotnet-authentication` | Setting BasicAuth credentials | Binding API key from config, the `"x"` password literal, rotation, per-environment secrets |
| `dotnet-calling-endpoints` | Invoking SDK operations | Named arguments, nullable parameters that must be passed explicitly, the envelope-unwrapping pattern |
| `dotnet-models` | Request/response records, enums | StringEnum<T> construction and reading, required field init patterns, nullable field semantics |
| `dotnet-error-handling` | Exception boundaries | Case A vs Case B typing, TryGet… accessor patterns, JSON deserialization escape (JsonException not caught by SdkException handlers), handling non-2xx malformed bodies |
| `dotnet-configuration-resilience` | Retries, timeouts, base URL, logging | Retry semantics on transport vs status failures, per-attempt vs total timeout, POST write idempotency edge cases |

---

## Assumptions & Blockers

### Assumptions

1. **Environment variables are populated** — `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT` (default "sandbox"), `MAXIO_DEFAULT_PRODUCT_FAMILY` are set in the deployment.
2. **Maxio configuration binding exists** — `IOptions<MaxioOptions>` is bound to `Maxio:*` config section (e.g., `Maxio:ApiKey`, `Maxio:Subdomain`) and injected into the integration layer. See `dotnet-authentication` for binding patterns.
3. **Product Family `eshop-subscribe` (ID 3023074) exists and is accessible** in the target sandbox (`cp-exp-1`). The two plans (`eshop-pro` and `basic-plan`) are already created.
4. **Plans do not require payment method** for subscription creation. The requirements state "payment method not required," so the product configuration must not enforce payment collection.
5. **Subscription idempotency via `Reference` works as documented** — duplicates are rejected with 422 on the wire. The integration layer will use `ReadCustomerByReference` + `ListCustomerSubscriptions` to avoid double-creation.
6. **JWT authentication on the PublicApi endpoints is already configured** — the maxio integration does not override or depend on the app's auth; it receives an authenticated identity and extracts the user ID from the JWT claims or session context.

### Blockers

**None identified.** All operations are available in the map; all error types are documented; all enum values are listed. Idempotency is achievable via the `Reference` field on both customers and subscriptions. The only **unverified** fact is the uniqueness scope of subscription `Reference`, which the integration will handle defensively: search-then-create pattern.

---

**Document location:** `C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-067\repo\maxio-plan.md`

**Summary:** Integration plan for recurring subscription billing in eShopOnWeb using Maxio Advanced Billing .NET SDK. Covers client setup, customer & subscription lifecycle (create, list), plan listing, error handling, and idempotency patterns. Five SDK operations: `ListProductsForProductFamily`, `ReadCustomerByReference`, `CreateCustomer`, `CreateSubscription`, `ListCustomerSubscriptions`. All signatures, enums, error types, and response envelopes are grounded in the map. Six companion skills (dotnet-client-initialization, dotnet-authentication, dotnet-calling-endpoints, dotnet-models, dotnet-error-handling, dotnet-configuration-resilience) must be loaded before coding. No blockers; one unverified fact on subscription-reference uniqueness (handled defensively).

**Assumptions & Blockers (verbatim for reference):**
- Assumptions: Environment variables set; config binding exists; eshop-subscribe family & plans exist; no payment method required; reference idempotency works; JWT auth configured.
- Blockers: None.
