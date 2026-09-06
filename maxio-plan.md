# Maxio Integration Plan — eShopOnWeb PublicApi

## Scope & Sequence

1. **Client setup** (application startup) — register Maxio SDK client via DI
2. **Read plans** — fetch Pro Plan (handle: `eshop-pro`) and Basic Plan (handle: `basic-plan`) on app startup or on demand
3. **Idempotent customer creation** — on subscription request, check if customer exists by reference (eShopOnWeb userId), create if missing
4. **Create subscription** — POST to Maxio with customer ID and product handle
5. **List user subscriptions** — fetch all subscriptions for a customer
6. **Map & return** — serialize Maxio response to API contracts; store Maxio customerId in app state

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### Operations & Models

| Controller | Operation | Signature | Request Model | Response / Error |
|---|---|---|---|---|
| `Customers` | **ReadCustomerByReference** | `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` · **Call**: `await client.Customers.ReadCustomerByReference(reference, ct)` (NO `Async` suffix) · params: `reference` (required, non-nullable) = eShopOnWeb userId | Query param: `reference` (wire: `reference`). No request body. | **Response**: `CustomerResponse` (namespace `MaxioAdvancedBilling.Models`) · inner field: `Customer (customer): Customer` (required). · `Customer` fields: `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`. · **Error** Case B: `SdkException<RawError>` (namespace `MaxioAdvancedBilling.Core.Exceptions`) — accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. · On 404 (not found), the exception is thrown; must be caught explicitly. |
| `Customers` | **CreateCustomer** | `Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · **Call**: `await client.Customers.CreateCustomer(body, ct)` (NO `Async` suffix) · params: `body` (required — must pass explicitly, type is nullable but operation requires it) | **Request**: `CreateCustomerRequest` (namespace `MaxioAdvancedBilling.Models`) · Inner field: `Customer (customer): CreateCustomer` (required, set in init). · `CreateCustomer` fields (required to set): `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. · Optional: `Reference (reference): string?` (wire: `reference`) — **MUST** set to eShopOnWeb userId for idempotent lookups. Additional optional: `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`. | **Response**: `CustomerResponse` · `Customer (customer): Customer` (required). · Same fields as ReadCustomerByReference response. · **Error** Case A: `SdkException<CreateCustomerError>` (namespace `MaxioAdvancedBilling.Errors`) · **Accessors**: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] — shape: `Errors (errors): Errors?`. · Fallback: `TryGetRawError(out RawError)`. |
| `Products` | **ReadProductByHandle** | `Task<ProductResponse> ReadProductByHandle(string apiHandle, CancellationToken ct = default)` · **Call**: `await client.Products.ReadProductByHandle(apiHandle, ct)` (NO `Async` suffix) · params: `apiHandle` (required, non-nullable string) = product handle (e.g., `"eshop-pro"`) | Path param: `{api_handle}` (wire: product handle). No request body. | **Response**: `ProductResponse` (namespace `MaxioAdvancedBilling.Models`) · `Product (product): Product` (required). · `Product` fields: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (enum). · **Error** Case B: `SdkException<RawError>`. On 404, exception is thrown. |
| `Subscriptions` | **CreateSubscription** | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · **Call**: `await client.Subscriptions.CreateSubscription(body, ct)` (NO `Async` suffix) · params: `body` (required — must pass explicitly, type is nullable but operation requires it) | **Request**: `CreateSubscriptionRequest` (namespace `MaxioAdvancedBilling.Models`) · Inner field: `Subscription (subscription): CreateSubscription` (required, set in init). · `CreateSubscription` fields (required by logic): `CustomerId (customer_id): int?` (wire: `customer_id`) OR `CustomerAttributes (customer_attributes): CustomerAttributes?`. For this flow: set `CustomerId` (from step 3). · Product spec (required): `ProductHandle (product_handle): string?` (wire: `product_handle`) OR `ProductId (product_id): int?` (wire: `product_id`) — pass handle like `"eshop-pro"`. · Optional: `ProductPricePointHandle (product_price_point_handle): string?`, `Reference (reference): string?` (wire: `reference`), `NextBillingAt (next_billing_at): DateTimeOffset?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum: `Automatic`, `Remittance`, `Prepaid`, `Invoice`), `CouponCode (coupon_code): string?`. | **Response**: `SubscriptionResponse` (namespace `MaxioAdvancedBilling.Models`) · `Subscription (subscription): Subscription` (required). · `Subscription` fields: `Id (id): int?`, `State (state): SubscriptionState?` (enum: `Active`, `Pending`, etc.), `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `ProductPriceInCents (product_price_in_cents): long?`, `BalanceInCents (balance_in_cents): long?`, `Customer (customer): Customer?`, `Product (product): Product?`. · **Error** Case A: `SdkException<CreateSubscriptionError>` (namespace `MaxioAdvancedBilling.Errors`) · **Accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — `Errors (errors): IReadOnlyList<string>` (required). · Fallback: `TryGetRawError(out RawError)`. |
| `Customers` | **ListCustomerSubscriptions** | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` · **Call**: `await client.Customers.ListCustomerSubscriptions(customerId, ct)` (NO `Async` suffix) · params: `customerId` (required, non-nullable int) | Path param: `{customer_id}`. No query params, no request body. | **Response**: `IReadOnlyList<SubscriptionResponse>` — array of subscriptions. Each element: `SubscriptionResponse` (namespace `MaxioAdvancedBilling.Models`) containing `Subscription (subscription): Subscription` (required). · Same `Subscription` fields as CreateSubscription response. · **Error** Case B: `SdkException<RawError>`. |

### Enums

| Enum | Values | Source | Usage |
|---|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `Active (active)`, `PastDue (past_due)`, `SoftFailure (soft_failure)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Paused (paused)`, `Unpaid (unpaid)` | `Models/Enums/SubscriptionState.cs` | In `Subscription.State` — return to API caller for display; check for `Active` on success. |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `Models/Enums/IntervalUnit.cs` | In `Product.IntervalUnit` — billing period unit (e.g., "month" for Pro Plan). |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `Models/Enums/CollectionMethod.cs` | Optional on `CreateSubscription` — if not specified, Maxio defaults per product. |

### Client Construction & Auth

| Item | Value | Source |
|---|---|---|
| **Root namespace** | `MaxioAdvancedBilling` | `sdk-map.md` |
| **Client class** | `MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| **Options class** | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| **Auth scheme** | HTTP Basic: `Username` = API key (from config `Maxio:ApiKey`), `Password` = literal `"x"` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| **Environment** | `ServerEnvironment.Us` (default) | `Servers/ServerEnvironment.cs` — US hosting; EU is `ServerEnvironment.Eu` |
| **Base URL override** | `options.Server.Production.Us.BaseUrl` (if sandbox/test host needed) | `Server.cs`, `ServerOptions.cs` |
| **Subdomain** | From config `Maxio:Subdomain`; set via `options.Server.Production.Us.Site` | `ServerOptions.cs` — default templated into `https://{site}.chargify.com` |
| **DI registration** | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = new BasicAuthCredentials { … }; })` | `ServiceCollectionExtensions.cs` |
| **Retry options** | `options.Retry` (type `RetryOptions`, backed by Polly) | `Core/Configuration/RetryOptions.cs` — configurable per `MaxRetries`, `Timeout`, etc. |

---

## Trap Notes

⚠ **Step 1 (client registration)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. The `Timeout` property is per-attempt, not total; `MaxRetries` minimum floor is 1 (zero is rejected). Transport failures (e.g., connection timeout) trigger retries even on `POST` (idempotent or not) — a non-idempotent write can execute more than once. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 (read plans)** — `ReadProductByHandle` is Case B (RawError) — a 404 throws `SdkException<RawError>` with no typed accessor. Distinguish 404 (plan not found, raise user error) from other statuses (outage). **MUST load `dotnet-calling-endpoints`** for call patterns and `dotnet-error-handling`** for exception boundaries.

⚠ **Step 3 (idempotent customer)** — `ReadCustomerByReference` returns Case B (RawError). If reference is not found, it throws 404. Handle the exception and fall through to `CreateCustomer`. Do **not** assume a customer exists just because a prior subscription claim exists in your app — Maxio may have deleted the customer. `CreateCustomer` is Case A (`CreateCustomerError`) with a 422 accessor; validate the request before sending (e.g., required fields, email format). **MUST load `dotnet-error-handling`** for both exceptions.

⚠ **Step 4 (create subscription)** — `CreateSubscription` is Case A (`CreateSubscriptionError`) — read the 422 payload via `TryGetErrorListResponse1(out ErrorListResponse1)` to extract field-level errors. Required fields: `CustomerId` (int from step 3) and either `ProductHandle` or `ProductId` (use handle `eshop-pro` or `basic-plan` per request). A 422 does **not** mean the subscription was rejected outright — the payload may contain business-rule violations (e.g., payment method required, billing address missing if `request_credit_card=true` on the product). **MUST load `dotnet-calling-endpoints`** for named argument binding (many optional params have no C# default) and `dotnet-error-handling`** for the 422 shape.

⚠ **Step 5 (list subscriptions)** — `ListCustomerSubscriptions` is Case B (RawError). The response is an array; deserialization failure (e.g., a drifted field type on the Maxio side) may surface as `JsonException`, **not** `SdkException`. **MUST load `dotnet-error-handling`** for the boundary rule: `JsonException` from deserialization is a 5xx (outage), not a call-side error.

⚠ **Error handling boundary** — two `JsonException` sources require opposite handling:
  - A drifted or malformed **2xx** body (e.g., missing a `required` field on `Subscription`) surfaces as `JsonException` from deserialization, **not** `SdkException` — so an `SdkException`-only catch ladder lets it escape and becomes a 5xx to the caller.
  - A **non-2xx** body that does not match the operation's generated error shape (e.g., a 422 from Maxio that is not `ErrorListResponse1`) throws `JsonException` *while the error object is being constructed*, **replacing** the `SdkException` — the HTTP status is lost. A boundary that maps every `JsonException` to 5xx then reports a deterministic error as an outage, and a retry loop sees 5xx and retries something that will never succeed.

**MUST load `dotnet-error-handling`** before writing the boundary. This is critical — the error contract is not inferable from signatures alone.

---

## REQUIRED READING

Load **before implementation starts**:

| Skill | Step(s) Governed |
|---|---|
| `dotnet-client-initialization` | Step 1 — client registration, DI wiring, long-lived `HttpClient` reuse |
| `dotnet-authentication` | Step 1 — HTTP Basic credentials, configuration injection |
| `dotnet-calling-endpoints` | Steps 2–5 — operation calls, named argument binding for optional params, async/cancellation |
| `dotnet-models` | Steps 3–5 — request/response records, required field initialization, nullable semantics, enums as `StringEnum<T>` (not C# enums), unions (if any) |
| `dotnet-error-handling` | All steps — Case A/B exception handling, typed vs. raw error accessors, `JsonException` boundary rules, `SdkException<T>` throwing semantics |
| `dotnet-configuration-resilience` | Step 1 — retry/timeout semantics, per-attempt vs. total bounds, transport failure retries on idempotent operations |

All of these skills are part of the SDK integration contract and must be loaded before any code is written. The signatures in this sheet name the operations and field names; the skills carry the patterns that make them work correctly.

---

## Assumptions & Blockers

- **Assumption**: eShopOnWeb userId is a string and will be used as the Maxio `Customer.Reference` for idempotent lookups. If userId format changes or collides, the idempotence strategy may fail.
- **Assumption**: The two plans (`eshop-pro`, `basic-plan`) are pre-seeded in the Maxio sandbox (product family `eshop-subscribe`) and will not be deleted during testing.
- **Assumption**: No payment method is required on the product (no `request_credit_card=true` on the seeded plans). If this assumption is wrong, subscription creation will return a 422, and the API must surface that error to the caller.
- **Assumption**: The in-memory or database mapping of eShopOnWeb userId → Maxio customerId is the application's responsibility; the Maxio integration is read-only with respect to that mapping.
- **Blocker**: None identified. All required operations are available in the map and SDK.
