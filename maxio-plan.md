# Maxio Advanced Billing Integration Plan — eShopOnWeb

## Scope & Sequence

This plan covers adding recurring subscription billing to the eShopOnWeb reference app via three HTTP endpoints on PublicApi:

1. **Prepare SDK client & DI** — register the Maxio client with .NET DI, wire credentials from configuration.
2. **GET /api/subscription-plans** — list available subscription plans by product family handle.
3. **POST /api/subscriptions** — create a subscription with idempotent customer logic (lookup by reference, create if missing).
4. **GET /api/my-subscriptions** — retrieve active subscriptions for the authenticated user.

All operations run against the Maxio sandbox (site subdomain configured in `Maxio:Subdomain`).

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations used

| Step | Operation | Controller | Signature | Request model | Response envelope | Error case | Source |
|---|---|---|---|---|---|---|---|
| 1 | `ListProducts` | `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | None (query params only) | `IReadOnlyList<ProductResponse>` — each element is a record wrapping the `Product` — access payload via `response[i].Product` | **Case B**: `SdkException<RawError>` · `error.StatusCode`, `error.ReadAsString()`, `error.ReadAsJson<T>()`, `error.ReadAsBytes()` | `operations/Products.md` |
| 2 | `ReadCustomerByReference` | `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param: `reference` (wire: `reference`) | `CustomerResponse` — access payload via `response.Customer` | **Case B**: `SdkException<RawError>` · same accessors as Case B above | `operations/Customers.md` |
| 3 | `CreateCustomer` | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` (wraps `CreateCustomer` at `Subscription` field) · required fields: `FirstName` (wire: `first_name`), `LastName` (wire: `last_name`), `Email` (wire: `email`), `Reference` (wire: `reference`, optional but recommended for lookup) · pass `null` for optional fields (`CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`) | `CustomerResponse` — access payload via `response.Customer` | **Case A**: `SdkException<CreateCustomerError>` · `error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `error.TryGetRawError(out RawError)` [fallback] | `operations/Customers.md` |
| 4 | `CreateSubscription` | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` (wraps `CreateSubscription` at `Subscription` field) · min required: `ProductHandle` or `ProductId` (wire: `product_handle` / `product_id`), `CustomerId` (wire: `customer_id`, alternative: `CustomerReference` wire: `customer_reference`), `ProductPricePointHandle` or `ProductPricePointId` (wire: `product_price_point_handle` / `product_price_point_id`), `DeferSignup` (wire: `defer_signup`, optional, default false), `Reference` (wire: `reference`, optional identifier for the subscription) | `SubscriptionResponse` — access payload via `response.Subscription` | **Case A**: `SdkException<CreateSubscriptionError>` · `error.TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `error.TryGetRawError(out RawError)` [fallback] | `operations/Subscriptions.md` |
| 5 | `ListSubscriptions` | `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Query params: `state` (wire: `state`, enum `SubscriptionStateFilter` — see enums below), custom lookup via `metadata` (wire: `metadata`, pass IReadOnlyDictionary; in this app, pass `null` and filter client-side by `customer_id`) | `IReadOnlyList<SubscriptionResponse>` — each element is a record; access payload via `response[i].Subscription` | **Case B**: `SdkException<RawError>` · same accessors as Case B above | `operations/Subscriptions.md` |

### Models — request shapes

| Model | Namespace | Required fields | Wire name + type | Optional fields (key ones for this scope) |
|---|---|---|---|---|
| `CreateCustomerRequest` | `MaxioAdvancedBilling.Models` | `Customer` (record, wraps inner `CreateCustomer`) | `customer`: `CreateCustomer` | — |
| `CreateCustomer` | `MaxioAdvancedBilling.Models` | `FirstName`, `LastName`, `Email` | `first_name`: string, `last_name`: string, `email`: string | `Reference` (wire: `reference`), `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` |
| `CreateSubscriptionRequest` | `MaxioAdvancedBilling.Models` | `Subscription` (record, wraps inner `CreateSubscription`) | `subscription`: `CreateSubscription` | — |
| `CreateSubscription` | `MaxioAdvancedBilling.Models` | None (all optional; Notes determine acceptance) | — | `ProductHandle` (wire: `product_handle`), `ProductId` (wire: `product_id`), `ProductPricePointHandle` (wire: `product_price_point_handle`), `ProductPricePointId` (wire: `product_price_point_id`), `CustomerId` (wire: `customer_id`), `CustomerReference` (wire: `customer_reference`), `DeferSignup` (wire: `defer_signup`, default false), `Reference` (wire: `reference`), `PaymentCollectionMethod` (wire: `payment_collection_method`, enum `CollectionMethod`) |

### Models — response shapes & fields used in this scope

| Model | Namespace | Envelope field | Payload type | Fields (selected) |
|---|---|---|---|---|
| `ProductResponse` | `MaxioAdvancedBilling.Models` | `Product` | `Product` | Access via `response.Product.Handle` (wire: `handle`), `.Name` (wire: `name`), `.PriceInCents` (wire: `price_in_cents`), `.Interval` (wire: `interval`), `.IntervalUnit` (wire: `interval_unit`, enum `IntervalUnit`), `.Id` (wire: `id`) |
| `CustomerResponse` | `MaxioAdvancedBilling.Models` | `Customer` | `Customer` | Access via `response.Customer.Id`, `.FirstName`, `.LastName`, `.Email`, `.Reference` |
| `SubscriptionResponse` | `MaxioAdvancedBilling.Models` | `Subscription` | `Subscription?` (nullable — check before accessing) | Access via `response.Subscription.Id`, `.State` (wire: `state`, enum `SubscriptionState`), `.CustomerId` (wire: `customer_id`), `.ProductId` (wire: `product_id`), `.NextAssessmentAt` (wire: `next_assessment_at`, DateTimeOffset), `.CurrentBillingAmountInCents` (wire: `current_billing_amount_in_cents`, long), `.Product` (nested `Product?` — see above for fields) |

### Enums used

| Enum | Namespace | Values (literal C# names as used in code) |
|---|---|---|
| `SubscriptionStateFilter` | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `Paused`, `PendingCancellation`, `Trialing`, `Unpaid`, `AwaitingSignup`, `ExpiredTrialEnded` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `Paused`, `PendingCancellation`, `Trialing`, `Unpaid`, `AwaitingSignup`, `ExpiredTrialEnded` (use in response checks; response includes `.State` as this enum) |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day`, `Month`, `Year` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic`, `Invoice`, `RemittanceAutomaticNetTerms`, `RemittanceNetTerms` |

### Client construction & auth

| Step | Implementation | Source |
|---|---|---|
| Credentials | API key from `Maxio:ApiKey` config binding; use `BasicAuthCredentials { Username = apiKey, Password = "x" }` | `sdk-map.md` (Servers & auth) |
| Subdomain override | Set `options.Server.Production.Us.Site = Maxio:Subdomain config value` before creating client | `sdk-map.md` (Servers & auth, override point) |
| Environment | Default `ServerEnvironment.Us`; pass to `MaxioAdvancedBillingClientOptions.Environment` | `sdk-map.md` |
| HttpClient | Pass the long-lived `IHttpClientFactory.CreateClient()` instance to constructor; SDK wraps it | `sdk-map.md` (Getting a client) |
| DI registration (alternative) | Call `services.AddMaxioAdvancedBillingClient(options => { options.BasicAuth = …; })` in startup | `sdk-map.md` (Getting a client, DI alternative) |

### Idempotent customer creation logic (pseudo-code guide)

```
try:
  customer = ReadCustomerByReference(userReference)
catch SdkException<RawError> where StatusCode == 404:
  customer = CreateCustomer(CreateCustomerRequest { Customer = new CreateCustomer {
    FirstName = user.FirstName,
    LastName = user.LastName,
    Email = user.Email,
    Reference = user.Id.ToString()
  }})
```

**Do not catch all `SdkException<RawError>` — only 404 means "not found"; others are real errors.**

---

### Enum value tables (reference — use literal names in code)

From `enums.md`:

**`SubscriptionStateFilter` / `SubscriptionState` wire values:**
- `Active` (wire: `active`)
- `Canceled` (wire: `canceled`)
- `Paused` (wire: `paused`)
- `PendingCancellation` (wire: `pending_cancellation`)
- `Trialing` (wire: `trialing`)
- `Unpaid` (wire: `unpaid`)
- `AwaitingSignup` (wire: `awaiting_signup`)
- `ExpiredTrialEnded` (wire: `expired_trial_ended`)

**`IntervalUnit` wire values:**
- `Day` (wire: `day`)
- `Month` (wire: `month`)
- `Year` (wire: `year`)

**`CollectionMethod` wire values:**
- `Automatic` (wire: `automatic`)
- `Invoice` (wire: `invoice`)
- `RemittanceAutomaticNetTerms` (wire: `remittance_automatic_net_terms`)
- `RemittanceNetTerms` (wire: `remittance_net_terms`)

---

## Trap Notes

⚠ **Step 1 (client setup)** — The SDK's retry and timeout options do NOT bound a whole call and are NOT the timeout on the `HttpClient` you register. The client wraps a long-lived `HttpClient` that must be reused across requests (via `IHttpClientFactory` in .NET); the SDK's `RetryOptions` only govern retry backoff and per-attempt delay. **MUST load `dotnet-configuration-resilience`** before wiring the client and setting any timeout/retry parameters.

⚠ **Step 3–5 (reading responses)** — Response types wrap their payload in a single required or nullable field (`ProductResponse.Product`, `SubscriptionResponse.Subscription`, `CustomerResponse.Customer`). Reads must unwrap the envelope: e.g., `response.Product.Handle`, not `response.Handle`. On `SubscriptionResponse`, the inner `Subscription` is nullable (`Subscription?`) — **check before accessing nested fields**. **MUST load `dotnet-models`** to understand union/optional field handling if custom pricing or complex component scenarios arise.

⚠ **Step 2 (idempotent customer lookup)** — `ReadCustomerByReference` throws `SdkException<RawError>` with `StatusCode == HttpStatusCode.NotFound` (404) when the reference does not exist. This is the signal to create the customer; **catch only 404, not all `RawError` exceptions**. Any other error (401, 500, network) must propagate. **MUST load `dotnet-error-handling`** before writing the catch ladder.

⚠ **Step 3 (customer creation errors)** — `CreateCustomer` is Case A (typed `SdkException<CreateCustomerError>`), not Case B. Use `error.TryGetCustomerErrorResponse1(out var payload)` to extract validation errors [422]; `TryGetRawError` is the fallback. Do not assume all errors are validation — re-raise network/auth errors. **MUST load `dotnet-error-handling`** for the distinction between Case A/B and the correct accessor use.

⚠ **Step 4 (subscription creation — no payment method required)** — The sandbox plans (`eshop-pro`, `basic-plan`) have `payment_method_not_required: true`. The request does not demand a `PaymentProfileId` or `PaymentProfileAttributes`. However, if the API rejects creation (e.g., because a live plan requires a method), the error will be in the [422] response under `error.TryGetErrorListResponse1(out ErrorListResponse1)` — extract the `Errors` array and report the list to the caller. **MUST load `dotnet-error-handling`** to handle the envelope correctly.

⚠ **System.Text.Json.JsonException (drifted 2xx body)** — If a successful (2xx) response body lacks a required field (e.g., `Product` missing from `ProductResponse`), deserialization throws `JsonException`, not `SdkException`. The HTTP status is lost; the error boundary must catch both `SdkException<T>` and `JsonException` and report deterministically (not as "successful"). **MUST load `dotnet-error-handling`** before writing the boundary — the skill documents both directions and why they need opposite handling.

⚠ **System.Text.Json.JsonException (malformed non-2xx body)** — If a non-2xx response does not match the operation's typed error shape, `JsonException` is thrown *during error construction*, replacing the `SdkException` and destroying the HTTP status. A boundary that maps every `JsonException` to 5xx then logs "outage" when a caller retries 5xx. **MUST load `dotnet-error-handling`** — mishandling this is a silent catastrophe.

---

## REQUIRED READING

Before implementation starts, load these companion skills in order — they carry binding gates and worked examples this sheet deliberately does not repeat:

1. **`dotnet-client-initialization`** (Step 1) — how to construct and DI-register the client, and the critical `HttpClient` reuse rule.
2. **`dotnet-authentication`** (Step 1) — how to wire Basic auth (username = API key, password = `"x"`) before/during client construction.
3. **`dotnet-calling-endpoints`** (Steps 2–5) — operation calling semantics, parameter passing (named args on optional params, `null` to skip), async/await.
4. **`dotnet-models`** (Steps 2–5) — envelope unwrapping, optional field handling, enum construction (use `StringEnum<T>.FromValue("wire")` or the literal static members).
5. **`dotnet-error-handling`** (Steps 2–5) — the **mandatory first step before writing any exception boundary**. Must load BEFORE writing the catch ladder. Covers Case A (typed error with `TryGet…`) vs Case B (raw error), `TryGetRawError` fallback, and the two `JsonException` scenarios (drifted 2xx, malformed non-2xx).
6. **`dotnet-configuration-resilience`** (Step 1) — retry/timeout/backoff tuning, server-node override, per-attempt vs total timeout semantics (critical: `Timeout` is per-attempt, not total; `MaxRetries = 0` is rejected; non-idempotent writes can execute more than once on transport failure).

These skills are the integration layer. This sheet is the contract; the skills are the how-to. **Do not skip any skill** — each guards a different class of failure.

---

## Assumptions & Blockers

**Assumptions:**

1. User identity is available in the authenticated request context (via JWT claims or similar) and can be extracted to populate `first_name`, `last_name`, `email`, and a stable `reference` for the customer.
2. The eShopOnWeb `Maxio` configuration section is bound and populated (keys: `ApiKey`, `Subdomain`, optionally `Environment`, `BaseUrl`).
3. Product family handle `eshop-subscribe` and plan handles `eshop-pro`, `basic-plan` exist in the sandbox and are configured as described (payment not required).
4. Subscriptions are never edited or cancelled in this scope — only created and read. Future scope may require update/cancel logic.
5. The app does not need to display subscription detail (e.g., custom price points, components, prepaid allocations). Response unwrapping is confined to a few fields (`Id`, `State`, `NextAssessmentAt`, `CurrentBillingAmountInCents`).

**Blockers:**

None identified. The SDK covers all required operations; the Maxio sandbox is seeded with the necessary products and plans.

---

## File Structure & Build Order

| File | Purpose | Depends on |
|---|---|---|
| `appsettings.json` / configuration | Bind `Maxio:ApiKey`, `Maxio:Subdomain` | — |
| `Services/MaxioSubscriptionService.cs` | SDK client DI registration, customer lookup/create, subscription CRUD | Configuration |
| `Endpoints/SubscriptionEndpoints.cs` or controller | Three HTTP endpoints, JWT auth, call through service | `MaxioSubscriptionService` |

**Build order:**
1. Add NuGet package: `dotnet add package AsadAli.AdvancedBilling.Sdk`
2. Wire configuration binding + DI setup in `Program.cs` (calls `AddMaxioAdvancedBillingClient` or manual registration)
3. Implement `MaxioSubscriptionService` (client construction, customer/subscription operations, error boundary)
4. Implement endpoints

---

**Plan file location:** `C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-026\repo\maxio-plan.md`

**Summary:** Integrate Maxio Advanced Billing into eShopOnWeb via three HTTP endpoints on PublicApi, using the SDK's `Products`, `Customers`, and `Subscriptions` controllers. Customer creation is guarded by a lookup-first pattern (read by reference, create if 404). All operations are grounded in the SDK map; error handling (Case A/B, JsonException) and client resilience are delegated to the required companion skills — do not skip the reading.
