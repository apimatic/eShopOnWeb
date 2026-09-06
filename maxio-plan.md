# Maxio Integration Plan: eShopOnWeb Subscription Module

## Scope & Sequence

The integration adds three HTTP endpoints to PublicApi supporting a parallel subscription capability:
1. **GET /api/subscription-plans** — list active products in the configured family
2. **POST /api/subscriptions** — create a subscription for a user, with idempotent customer creation
3. **GET /api/my-subscriptions** — fetch subscriptions for the authenticated user

Each endpoint calls the Maxio SDK in the following order:
- List plans: `ListProductsForProductFamily` on the configured family handle
- Subscribe: find-or-create customer via `ReadCustomerByReference` (idempotent) + `CreateSubscription`
- My subscriptions: fetch customer by reference → `ListCustomerSubscriptions`

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal
C# identifier. The cancellation-token parameter really is named `ct`: in named
arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take
each one from that type's own map row, never from where a neighbouring type sits. A members
table names the namespace outright; otherwise the row's source path implies it
(`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
namespace). Enums, unions, auth, server and client-config types are spread across different
child namespaces, and two types configured side by side in the same options object routinely
live in different ones. Dropping a type to the root or to `.Models` makes the implementer
guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField = null, ListProductsFilter? filter = null, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, DateTimeOffset? startDatetime = null, DateTimeOffset? endDatetime = null, bool? includeArchived = null, ListProductsInclude? include = null, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | None (family ID passed as URL param; 8 optional filters) | `IReadOnlyList<ProductResponse>` where `ProductResponse.Product: Product !req` contains `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `Description`, etc. | **Case A**: `SdkException<ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | Manual `page`/`perPage` | operations/ProductFamilies.md |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | None (reference as query param) | `CustomerResponse` where `CustomerResponse.Customer: Customer !req` contains `Id`, `FirstName`, `LastName`, `Email`, `Reference`, `CreatedAt`, `UpdatedAt`, `TaxExempt`, etc. | **Case B**: `SdkException<RawError>` · `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` | None | operations/Customers.md |
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest { Customer: CreateCustomer !req }` where `CreateCustomer` has: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `CcEmails (cc_emails): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?` | `CustomerResponse` where `CustomerResponse.Customer: Customer !req` | **Case A**: `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] — payload `Errors { PerPage?: string[], PricePoint?: string[] }` · `TryGetRawError(out RawError)` [fallback] | None | operations/Customers.md |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }` where `CreateSubscription` contains: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `CustomerAttributes (customer_attributes): CustomerAttributes?` (inline customer creation), `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, and 40+ other optional fields (see model) | `SubscriptionResponse` where `SubscriptionResponse.Subscription: Subscription?` contains `Id`, `State` (enum `SubscriptionState`), `CustomerId`, `ProductId`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `CreatedAt`, `BalanceInCents`, `TotalRevenueInCents`, `CouponCode`, `Customer`, `Product`, etc. | **Case A**: `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — payload `Errors: string[] !req` · `TryGetRawError(out RawError)` [fallback] | None | operations/Subscriptions.md |
| **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | None (customer ID in URL) | `IReadOnlyList<SubscriptionResponse>` where each `SubscriptionResponse.Subscription: Subscription?` (see CreateSubscription response) | **Case B**: `SdkException<RawError>` · `StatusCode`, `ReadAsString()` | None | operations/Customers.md |

### Request/Response Field Details

**CreateCustomer fields (wire names → C# type):**
- `first_name` → `FirstName: string` (required)
- `last_name` → `LastName: string` (required)
- `email` → `Email: string` (required)
- `reference` → `Reference: string?` (optional, unique per site — use user ID for idempotence)
- `organization` → `Organization: string?`
- `address`, `address_2` → `Address`, `Address2: string?`
- `city`, `state`, `zip`, `country` → `City`, `State`, `Zip`, `Country: string?`
- `phone` → `Phone: string?`
- `cc_emails` → `CcEmails: string?`
- `vat_number` → `VatNumber: string?`
- `tax_exempt` → `TaxExempt: bool?`

**CreateSubscription key fields (for typical use — model carries 40+ optional fields):**
- `product_handle` or `product_id` → `ProductHandle: string?` or `ProductId: int?`
- `product_price_point_handle` or `product_price_point_id` → optional; if absent, default PP used
- `customer_id` → `CustomerId: int?` (existing customer by Maxio ID)
- `customer_reference` → `CustomerReference: string?` (existing customer by your reference)
- `payment_collection_method` → `PaymentCollectionMethod: CollectionMethod?` — enum: `Automatic`, `Remittance`, `Prepaid`, `Invoice`
- `reference` → `Reference: string?` (your unique subscription key; enables `FindSubscription`)
- `coupon_code` / `coupon_codes` → `CouponCode: string?` or `CouponCodes: IReadOnlyList<string>?`
- `next_billing_at` → `NextBillingAt: DateTimeOffset?`
- `initial_billing_at` → `InitialBillingAt: DateTimeOffset?`
- `customer_attributes` → `CustomerAttributes: CustomerAttributes?` — inline creation; if set, `customer_id`/`customer_reference` must be absent

**CustomerAttributes (for inline customer creation on subscription signup):**
- `first_name`, `last_name`, `email` → `FirstName`, `LastName`, `Email: string?`
- `reference` → `Reference: string?` (your app's user ID)
- `organization`, `address`, `city`, `state`, `zip`, `country`, `phone` → matching wire names
- `cc_emails` → `CcEmails: string?`
- `vat_number` → `VatNumber: string?`
- `tax_exempt` → `TaxExempt: bool?`

**Subscription response key fields (SubscriptionResponse.Subscription):**
- `id` → `Id: int?`
- `state` → `State: SubscriptionState?` — enum: `Active`, `Trialing`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `OnHold`, `Paused`, `Unpaid`, `AwaitingSignup`, etc.
- `customer_id` → `CustomerId: int?`
- `product_id` → `ProductId: int?`
- `product_price_in_cents` → `ProductPriceInCents: long?`
- `current_period_ends_at` → `CurrentPeriodEndsAt: DateTimeOffset?`
- `next_assessment_at` → `NextAssessmentAt: DateTimeOffset?`
- `activated_at` → `ActivatedAt: DateTimeOffset?`
- `created_at`, `updated_at` → `CreatedAt`, `UpdatedAt: DateTimeOffset?`
- `balance_in_cents` → `BalanceInCents: long?`
- `coupon_code` → `CouponCode: string?`
- `reference` → `Reference: string?`
- Nested objects: `customer: Customer?`, `product: Product?`, `credit_card: CreditCardPaymentProfile?`, `bank_account: BankAccountPaymentProfile?`

### Enum Values (as C# static members — wire values in parens)

**CollectionMethod** (`MaxioAdvancedBilling.Models.Enums.CollectionMethod`):
- `CollectionMethod.Automatic` (wire: `automatic`)
- `CollectionMethod.Remittance` (wire: `remittance`)
- `CollectionMethod.Prepaid` (wire: `prepaid`)
- `CollectionMethod.Invoice` (wire: `invoice`)

**SubscriptionState** (`MaxioAdvancedBilling.Models.Enums.SubscriptionState`):
- `SubscriptionState.Pending` (wire: `pending`)
- `SubscriptionState.Trialing` (wire: `trialing`)
- `SubscriptionState.Assessing` (wire: `assessing`)
- `SubscriptionState.Active` (wire: `active`)
- `SubscriptionState.SoftFailure` (wire: `soft_failure`)
- `SubscriptionState.PastDue` (wire: `past_due`)
- `SubscriptionState.Suspended` (wire: `suspended`)
- `SubscriptionState.Canceled` (wire: `canceled`)
- `SubscriptionState.Expired` (wire: `expired`)
- `SubscriptionState.Paused` (wire: `paused`)
- `SubscriptionState.Unpaid` (wire: `unpaid`)
- `SubscriptionState.TrialEnded` (wire: `trial_ended`)
- `SubscriptionState.OnHold` (wire: `on_hold`)
- `SubscriptionState.AwaitingSignup` (wire: `awaiting_signup`)
- `SubscriptionState.FailedToCreate` (wire: `failed_to_create`)

### Error Payload Types

**CreateCustomerError (Case A, 422) — ErrorListResponse1:**
```csharp
namespace MaxioAdvancedBilling.Models
public record ErrorListResponse1
{
    public IReadOnlyList<string> Errors { get; init; } // required
}
```
Exception path: `catch (SdkException<CreateCustomerError> ex) { ex.Error.TryGetCustomerErrorResponse1(out var e422); }`

**ErrorListResponse1 (Case A, 422 on CreateSubscription):**
Same structure as above.

**RawError (Case B — ListCustomerSubscriptions, ReadCustomerByReference, fallback):**
```csharp
namespace MaxioAdvancedBilling.Core.ErrorResponse
public record RawError
{
    public HttpStatusCode StatusCode { get; }
    public string ReadAsString() { … }
    public T? ReadAsJson<T>() { … }
    public ReadOnlyMemory<byte> ReadAsBytes() { … }
}
```

---

## Trap Notes

⚠ **Step 1 (client construction & auth)** — the SDK's `HttpClient` parameter must be a **long-lived, reused instance** from `IHttpClientFactory`, not created fresh per request. Basic auth credentials (API key in Username, literal `"x"` in Password) must be set **before** client construction or in the DI callback. **MUST load `dotnet-client-initialization` and `dotnet-authentication`** before wiring the client and credentials.

⚠ **Step 1 (environment/subdomain)** — the SDK client's `options.Server.Production.Us.Site` property must be set to the Maxio account subdomain (e.g., `"your-site"`). The full URL becomes `https://{site}.chargify.com`. If your `.env` or config provides only the subdomain, pass it here; the SDK appends the domain. **The base URL can be overridden via `options.Server.Production.Us.BaseUrl`** if you need to redirect to a local mock or non-standard host. **MUST load `dotnet-configuration-resilience`** to understand server/environment setup.

⚠ **Step 2 (idempotent customer lookup)** — `ReadCustomerByReference` returns 404 as a `RawError` (Case B), not a typed error. A 404 is **not** an exception thrown by the SDK on 2xx; it is a **valid, expected response on a 404 status**. Code must catch `SdkException<RawError>`, check `ex.Error.StatusCode == HttpStatusCode.NotFound`, and treat it as "customer does not exist." If caught and statusCode is 404, proceed to `CreateCustomer`; if any other non-2xx status, re-throw or map to a retryable/non-retryable error. **MUST load `dotnet-error-handling`** for the Case A/B distinction and `TryGet…` vs. raw-error handling.

⚠ **Step 2 (customer reference uniqueness)** — Maxio enforces that `reference` (your customer ID) is **unique per site**. If a second subscription request arrives with the same reference **but different email/name**, the lookup will find the first customer; the subscription will be created on that same customer. This is the idempotent behaviour we want (no double customer). However, if the first customer's data is out of sync with your app, **you must update the customer via `UpdateCustomer`** on mismatch — the plan does not include customer updates, but the integration must decide the strategy (e.g., "email changed → call UpdateCustomer," or "email changed → error, tell user to contact support").

⚠ **Step 3 (subscription creation without payment method)** — the notes on `CreateSubscription` state that "payment information may be required … depending on the options for the Product being subscribed." Since the plan specifies "payment method not required" for sandbox, the subscription should succeed without card data. However, **if a product is configured to `require_credit_card` or a payment gateway enforces it, the call will fail with a 422 and an error message in the `Errors` list**. The integration must decide: accept sandbox subscriptions without cards (likely the intent), or collect a card upfront. The `CollectionMethod` parameter can steer behavior (e.g., `Prepaid` or `Remittance` may avoid immediate payment collection), but **this is a product-level, not SDK-level, constraint**. Test with the sandbox product configuration to confirm no-card subscriptions are allowed.

⚠ **Step 3 (subscription state transitions)** — a newly created subscription may start in `AwaitingSignup`, `Trialing`, `Active`, or `PastDue` depending on trial settings, payment success, and product config. The integration should **not assume** a subscription is immediately `Active`; check the `State` field in the response to inform the user (e.g., "your subscription is in trial" or "activation pending payment"). The `/api/my-subscriptions` endpoint should return all non-canceled states.

⚠ **Step 3 & 4 (payment collection & dunning)** — if a subscription enters `PastDue` or `Suspended`, Maxio's dunning system may attempt retries per site config. **This is outside SDK scope** — the integration owns how to surface payment-failure states to the user. Return the `State` from the subscription response to the UI so users can see if their sub is in trouble.

⚠ **Step 4 (list subscriptions by customer)** — `ListCustomerSubscriptions` returns all subscriptions (active, canceled, expired, etc.). The endpoint's intent is likely to show only **active** subscriptions to the user. Filter the response: `subscriptions.Where(s => s.State == SubscriptionState.Active || …)` before returning to the UI. **The map does not govern which states to show; that is a business decision** — the integration may choose to include `Trialing`, exclude `PastDue`, etc.

⚠ **Both operations (Customers, Subscriptions)** — a drifted or malformed **2xx** body (e.g., missing a `required` member in the response) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. If a non-2xx body does not match the operation's generated error shape, `JsonException` is thrown **while the error object is being constructed**, replacing the `SdkException` and destroying the HTTP status. A boundary that maps every `JsonException` to a 5xx and reports it as an outage, then a caller retrying 5xx, will retry something that can never succeed. **MUST load `dotnet-error-handling`** to design the error boundary correctly — it covers both cases and the defensive coding patterns.

⚠ **Configuration from environment** — the plan assumes binding keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optionally `Maxio:BaseUrl`. The integration must wire these from `IOptions<MaxioOptions>` (or equivalent DI pattern) into the SDK client options **before** calling any operation. A typo in the binding key name means the config is never read and auth fails silently. **Never hardcode credentials or subdomain**; always load from configuration. **MUST load `dotnet-configuration-resilience`** for how to plumb configuration into SDK options.

⚠ **Retry & idempotence** — `CreateCustomer` and `CreateSubscription` are non-idempotent by HTTP semantics (POST). However, Maxio's `reference` field allows **semantic idempotence**: if a customer or subscription with the given `reference` already exists, a retry with the same `reference` will return the existing entity (or fail with "already exists" — test with the sandbox). The integration must decide: use `reference` to enable semantic idempotence on retry, or document that retries may create duplicate customers/subscriptions. **MUST load `dotnet-configuration-resilience`** to understand SDK retry logic (only HTTP 503, etc., are retried by default; POST is not retried on transport errors unless explicitly configured).

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The contract sheet above does not carry their contents, and each governs a step where the signature alone cannot show the pitfall.

| Skill | Step(s) |
|---|---|
| `dotnet-client-initialization` | Client construction, DI registration, HttpClient lifecycle |
| `dotnet-authentication` | Basic auth setup, credential management |
| `dotnet-calling-endpoints` | Operation invocation, named vs. positional arguments, async/await |
| `dotnet-models` | Request/response record construction, enums (not C# enums), unions |
| `dotnet-error-handling` | Case A vs. B exceptions, error accessors, JsonException handling, boundary design |
| `dotnet-configuration-resilience` | Config binding, server/environment wiring, retry/timeout semantics |

Both of these error-boundary rows **must** be in the **FIRST** integration boundary (error handler middleware or try/catch block at operation calls), not a later revision:
- A drifted or malformed **2xx** body surfaces as `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the boundary.
- A **non-2xx** body that does not match the operation's generated error shape throws `JsonException` **while the error object is being constructed**, so `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed — a boundary that maps every `JsonException` to 5xx reports a deterministic rejection as an outage, and a caller retrying 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## Assumptions & Blockers

**Assumptions:**
1. The Maxio sandbox is pre-seeded with a Product Family (handle `eshop-subscribe`), two Products (handles `eshop-pro`, `basic-plan`), and a Metered component (handle `api-call`), as stated in the brief. The integration assumes these exist; **no creation/setup of these entities is in scope**.
2. The eShopOnWeb application has a user identity system (e.g., ASP.NET Identity or custom) with a user ID that can be passed as the Maxio `reference` field. The integration assumes a mapping from authenticated user → user ID → `reference`.
3. JWT authentication on `/api/subscriptions/*` endpoints is configured by the main application (not by this SDK integration). The integration inherits the authenticated user context.
4. The Maxio account subdomain and API key are available in application configuration (via `IConfiguration` binding to `Maxio:*` keys).
5. Payment method is **not** required to create a subscription (sandbox/product allows it). If a live or differently configured product requires a card, the subscription will fail with a 422 error on the `CreateSubscription` call, and the integration will surface that error to the user (not a blocker, but a constraint to test).

**Blockers:**
None identified. The SDK map and operations cover all required functionality. The integration depends on the Maxio sandbox being pre-seeded (assumption #1), but that is external setup, not an SDK gap.

