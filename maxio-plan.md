# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Client registration & authentication** – Initialize `MaxioAdvancedBillingClient` with API key from config, register in DI.
2. **Subscription plans endpoint** – GET /api/subscription-plans: list available plans from configured Product Family.
3. **Idempotent customer creation** – In subscription creation flow, ensure customer exists (lookup by app user ID); create if missing.
4. **Subscription creation endpoint** – POST /api/subscriptions: create subscription to chosen plan; return plan, price, state, next billing date.
5. **Subscriber subscriptions endpoint** – GET /api/my-subscriptions: list active subscriptions for authenticated user.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Controller | Operation | Signature & params | Request model + fields | Response envelope + fields | Error case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| `ProductFamilies` | ListProductsForProductFamily | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` must pass explicitly; remaining optional params (all nullable) must pass explicitly (pass `null` to skip); defaults `page=1, perPage=20` | N/A (GET, no body) | `IReadOnlyList<ProductResponse>` — each wraps `Product { Id (id): int?, Name (name): string?, Handle (handle): string?, PriceInCents (price_in_cents): long?, Interval (interval): int?, IntervalUnit (interval_unit): IntervalUnit?, ... }` | Case A: `SdkException<ListProductsForProductFamilyError>` with `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | Manual `page`+`perPage` | `operations/ProductFamilies.md` |
| `Customers` | ReadCustomerByReference | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` must pass explicitly (query param `reference`) | N/A (GET, no body) | `CustomerResponse` wraps `Customer { Id (id): int?, FirstName (first_name): string?, LastName (last_name): string?, Email (email): string?, Reference (reference): string?, CreatedAt (created_at): DateTimeOffset?, ... }` | Case B: `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Customers.md` |
| `Customers` | CreateCustomer | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `CreateCustomerRequest` wraps `CreateCustomer { FirstName (first_name): string !req, LastName (last_name): string !req, Email (email): string !req, CcEmails (cc_emails): string?, Organization (organization): string?, Reference (reference): string?, Address (address): string?, Address2 (address_2): string?, City (city): string?, State (state): string?, Zip (zip): string?, Country (country): string?, Phone (phone): string?, Locale (locale): string?, VatNumber (vat_number): string?, TaxExempt (tax_exempt): bool?, TaxExemptReason (tax_exempt_reason): string?, ParentId (parent_id): int?, SalesforceId (salesforce_id): string? }` | `CustomerResponse` wraps `Customer { Id (id): int?, ... }` (same fields as ReadCustomerByReference) | Case A: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback]; `CustomerErrorResponse1` payload has `Errors (errors): Errors?` field | None | `operations/Customers.md` |
| `Subscriptions` | CreateSubscription | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `CreateSubscriptionRequest` wraps `CreateSubscription { ProductHandle (product_handle): string?, ProductId (product_id): int?, ProductPricePointHandle (product_price_point_handle): string?, ProductPricePointId (product_price_point_id): int?, CustomPrice (custom_price): SubscriptionCustomPrice?, CouponCode (coupon_code): string?, CouponCodes (coupon_codes): IReadOnlyList<string>?, PaymentCollectionMethod (payment_collection_method): CollectionMethod?, ReceivesInvoiceEmails (receives_invoice_emails): string?, NetTerms (net_terms): string?, CustomerId (customer_id): int?, NextBillingAt (next_billing_at): DateTimeOffset?, InitialBillingAt (initial_billing_at): DateTimeOffset?, DeferSignup (defer_signup): bool? = false, StoredCredentialTransactionId (stored_credential_transaction_id): int?, SalesRepId (sales_rep_id): int?, PaymentProfileId (payment_profile_id): int?, Reference (reference): string?, CustomerAttributes (customer_attributes): CustomerAttributes?, PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?, CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?, BankAccountAttributes (bank_account_attributes): BankAccountAttributes?, Components (components): IReadOnlyList<CreateSubscriptionComponent>?, CalendarBilling (calendar_billing): CalendarBilling?, Metafields (metafields): IReadOnlyDictionary<string, string>?, CustomerReference (customer_reference): string?, Group (group): GroupSettings?, ... [other optional fields] }` | `SubscriptionResponse` wraps `Subscription { Id (id): int?, State (state): SubscriptionState?, BalanceInCents (balance_in_cents): long?, TotalRevenueInCents (total_revenue_in_cents): long?, ProductPriceInCents (product_price_in_cents): long?, ProductVersionNumber (product_version_number): int?, CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?, NextAssessmentAt (next_assessment_at): DateTimeOffset?, TrialStartedAt (trial_started_at): DateTimeOffset?, TrialEndedAt (trial_ended_at): DateTimeOffset?, ActivatedAt (activated_at): DateTimeOffset?, ExpiresAt (expires_at): DateTimeOffset?, CreatedAt (created_at): DateTimeOffset?, UpdatedAt (updated_at): DateTimeOffset?, CancellationMessage (cancellation_message): string?, CancellationMethod (cancellation_method): CancellationMethod?, CancelAtEndOfPeriod (cancel_at_end_of_period): bool?, CanceledAt (canceled_at): DateTimeOffset?, CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?, PreviousState (previous_state): SubscriptionState?, SignupPaymentId (signup_payment_id): int?, SignupRevenue (signup_revenue): string?, DelayedCancelAt (delayed_cancel_at): DateTimeOffset?, CouponCode (coupon_code): string?, SnapDay (snap_day): string?, PaymentCollectionMethod (payment_collection_method): CollectionMethod?, Customer (customer): Customer?, Product (product): Product?, CreditCard (credit_card): CreditCardPaymentProfile?, Group (group): NestedSubscriptionGroup?, BankAccount (bank_account): BankAccountPaymentProfile?, PaymentType (payment_type): string?, ReferralCode (referral_code): string?, NextProductId (next_product_id): int?, NextProductHandle (next_product_handle): string?, CouponUseCount (coupon_use_count): int?, CouponUsesAllowed (coupon_uses_allowed): int?, ReasonCode (reason_code): string?, AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?, CouponCodes (coupon_codes): IReadOnlyList<string>?, OfferId (offer_id): int?, PayerId (payer_id): int?, CurrentBillingAmountInCents (current_billing_amount_in_cents): long?, ProductPricePointId (product_price_point_id): int?, ProductPricePointType (product_price_point_type): PricePointType?, NextProductPricePointId (next_product_price_point_id): int?, NetTerms (net_terms): int?, StoredCredentialTransactionId (stored_credential_transaction_id): int?, Reference (reference): string?, OnHoldAt (on_hold_at): DateTimeOffset?, PrepaidDunning (prepaid_dunning): bool?, Coupons (coupons): IReadOnlyList<SubscriptionIncludedCoupon>?, DunningCommunicationDelayEnabled (dunning_communication_delay_enabled): bool?, DunningCommunicationDelayTimeZone (dunning_communication_delay_time_zone): string?, ReceivesInvoiceEmails (receives_invoice_emails): bool?, Locale (locale): string?, Currency (currency): string?, ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?, CreditBalanceInCents (credit_balance_in_cents): long?, PrepaymentBalanceInCents (prepayment_balance_in_cents): long?, PrepaidConfiguration (prepaid_configuration): PrepaidConfiguration?, SelfServicePageToken (self_service_page_token): string? }` | Case A: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback]; `ErrorListResponse1` payload has `Errors (errors): IReadOnlyList<string> !req` | None | `operations/Subscriptions.md` |
| `Customers` | ListCustomerSubscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | N/A (GET, no body) | `IReadOnlyList<SubscriptionResponse>` — same structure as CreateSubscription response | Case B: `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Customers.md` |

### Enums needed

| Enum | Backing | Members (wire value) | Namespace | Source |
|---|---|---|---|---|
| `SubscriptionState` | StringEnum | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `MaxioAdvancedBilling.Models.Enums` | `models/enums.md` |
| `CollectionMethod` | StringEnum | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `MaxioAdvancedBilling.Models.Enums` | `models/enums.md` |
| `IntervalUnit` | StringEnum | `Day (day)`, `Month (month)` | `MaxioAdvancedBilling.Models.Enums` | `models/enums.md` |
| `CancellationMethod` | StringEnum | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` | `MaxioAdvancedBilling.Models.Enums` | `models/enums.md` |

### Client construction & authentication

- **Auth model**: HTTP Basic (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`)
  - Construct: `new BasicAuthCredentials { Username = "<api_key>", Password = "x" }`
  - Username = the API key from config (`Maxio:ApiKey`)
  - Password = literal string `"x"` (not the password from config; this is the scheme contract)
- **Environment**: `ServerEnvironment.Us` (default; namespace `MaxioAdvancedBilling.Servers`) for US hosting
- **Server override** (optional): set `options.Server.Production.Us.Site = "subdomain-from-config"` or `options.Server.Production.Us.BaseUrl = "base-url-override"` if needed
- **Client instantiation**: `new MaxioAdvancedBillingClient(httpClient, options)` where `httpClient` is a long-lived `System.Net.Http.HttpClient` (reuse via `IHttpClientFactory`)
- **DI alternative**: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })`

### Notes on usage

- **Response envelopes**: responses wrap their payload. E.g. `ProductResponse.Product`, `SubscriptionResponse.Subscription`, `CustomerResponse.Customer`. Read unwrap the field.
- **Optional fields not all required**: `CreateSubscription` marks no fields `required`, but to create a valid subscription you must pass at least `ProductId` (or `ProductHandle`) and `CustomerId` (or `CustomerAttributes` + `Reference`). Check the operation's Notes on the map page for which fields the operation requires.
- **Idempotent customer creation**: call `ReadCustomerByReference(userReference)` first; on `SdkException<RawError>` with 404, create via `CreateCustomer`. On other errors or 200 with a customer, reuse the existing ID. The `reference` field is your application's user ID (from JWT claims).

---

## Trap notes

⚠ **Step 1 (client registration & DI)** — the `HttpClient` must be long-lived and reused via `IHttpClientFactory`, never created per-request. The SDK client wrapper over it may be transient. **MUST load `dotnet-client-initialization`** before constructing the client or registering in DI.

⚠ **Step 1 (authentication)** — API key and password scheme. Set credentials before constructing the client or in the DI callback. Load the key from `Maxio:ApiKey` binding (not hardcoded). **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 2–5 (calling endpoints)** — call list/search/create operations with **named arguments** when more than a few params exist; many optional params have no C# default and mis-bind in positional calls. **MUST load `dotnet-calling-endpoints`** before the first SDK operation call.

⚠ **Step 3–4 (models)** — `CreateSubscription` has optional fields that are not marked `required` in the record, but the operation's Notes on the map page say which are actually required for the operation to succeed (e.g., product + customer ID or customer attributes). **MUST load `dotnet-models`** when building request bodies.

⚠ **Step 2–5 (error handling)** — `ListProductsForProductFamily` and `ListCustomerSubscriptions` are **Case B** (throw `SdkException<RawError>`, no typed accessors); `CreateCustomer`, `CreateSubscription`, and `ReadCustomerByReference` are **Case A** (throw `SdkException<CreateCustomerError>` / `CreateSubscriptionError>` / raw depending on status). The `ReadCustomerByReference` 404 surfaces as `SdkException<RawError>` with `StatusCode == 404`; do not assume a `CustomerResponse` on 200 — always catch the exception branch. **MUST load `dotnet-error-handling`** before writing any `try/catch`. Additionally, malformed or missing-required-field 2xx bodies throw `JsonException` from deserialization (not `SdkException`), so a catch ladder that only catches SDK exceptions will let it escape. **Non-2xx bodies that don't match the operation's error shape throw `JsonException` while the error object is being constructed**, destroying the HTTP status with it — a boundary that maps every `JsonException` to 5xx will misreport recoverable errors as outages. **MUST load `dotnet-error-handling`** before writing the error boundary.

⚠ **Step 1 (configuration & resilience)** — the SDK's `HttpMethodsToRetry` gates only the **status** trigger (503 on POST is not resent), but **transport failures** (`HttpRequestException`) are retried on **every** verb (POST included), so non-idempotent writes can execute more than once; no setting disables this (`MaxRetries = 0` is rejected at construction). `Timeout` is per-attempt, not total; no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

---

## REQUIRED READING

Before implementation starts, load **all of these** — they carry the usage layer that signatures alone cannot show:

- `dotnet-client-initialization` — client construction, DI, `HttpClient` reuse, lifecycle
- `dotnet-authentication` — HTTP Basic credentials, auth scheme shape, when to set them
- `dotnet-calling-endpoints` — operation call syntax, request envelope shapes, named vs positional args, async usage, cancellation
- `dotnet-models` — request/response records, enums (`StringEnum<T>`, not C# enums), unions (factories + `TryGet…`), unmodeled JSON fields dropped on deserialize
- **`dotnet-error-handling`** — try/catch patterns (Case A typed errors vs Case B raw errors), when `JsonException` replaces `SdkException`, why deserialization errors escape typed-exception catch ladders, the two directions `JsonException` reaches the boundary (malformed 2xx, non-2xx body mismatch), how to write an error boundary that does not misreport 4xx/5xx as outages
- `dotnet-configuration-resilience` — retry/timeout semantics, `HttpMethodsToRetry` status vs transport triggers, why writes are not idempotent-safe, `Timeout` per-attempt not total

**These are mandatory:**

1. a drifted or malformed **2xx** body (a missing `required` member in the response model) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
2. a **non-2xx** body that does not match its operation's generated error shape (e.g. a 500 HTML error page instead of JSON) throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

**Assumptions:**
- The application's user ID (from JWT claims) is a stable identifier suitable as the `reference` field in Maxio customers; migrations of that ID will require corresponding updates in Maxio.
- Product Family `eshop-subscribe` exists and has products/plans already seeded in the sandbox; the integration will not create plans, only list and subscribe to them.
- The app does not require payment profile creation in eShopOnWeb itself; payment info is collected separately (out of scope for this plan).

**Blockers:** None identified. The operations needed to implement the three flows are available and well-documented in the map.
