# Maxio Advanced Billing Integration Plan — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

Implement three parallel endpoints on `src/PublicApi` to enable recurring subscription capability:

1. **Step 1: Fetch subscription plans** (`GET /api/subscription-plans`)
   - Operation: `ListProducts` (filtered by product family)
   - Returns: plan list with ID, name, price, billing interval
   
2. **Step 2: Create/enroll subscription** (`POST /api/subscriptions`)
   - Operations: `ReadCustomerByReference` (lookup by app user ID) → `CreateCustomer` (if not found) → `CreateSubscription`
   - Idempotency: customer lookup ensures no duplicate creation on double-click
   - Returns: subscription ID, state, next billing date

3. **Step 3: List user's subscriptions** (`GET /api/my-subscriptions`)
   - Operation: `FindSubscription` by reference (app's internal reference)
   - Returns: active subscriptions with state, plan, billing dates

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation Contracts

| Step | Controller.Method | Signature | Request Body | Response Envelope | Error Case | Notes | Source |
|---|---|---|---|---|---|---|---|
| 1 | `client.Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | (query params; all nullable — pass `null` to skip) | `IReadOnlyList<ProductResponse>`: array of objects, each is `{ product: { id, name, handle, price_in_cents, interval, interval_unit, … } }` | Case B: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | Filter to product family via handle or by pre-seeding product IDs (Pro Plan: 7126957, Basic: 7126958); per_page default 20, adjust as needed. Wire name `price_in_cents` ← C# `PriceInCents`. | `operations/Products.md` |
| 2a | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param `reference` (user's app ID) | `CustomerResponse`: `{ customer: { id, email, reference, first_name, last_name, … } }` | Case B: `SdkException<RawError>` | Lookup existing customer by app-internal reference (user ID). If 404 (not found), fall through to create. Wire name `first_name` ← C# `FirstName`. | `operations/Customers.md` |
| 2b | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest`: `{ customer: { first_name !req, last_name !req, email !req, reference?, … } }` | `CustomerResponse`: `{ customer: { id, reference, email, … } }` | Case A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | `reference` field is app's user ID (idempotency key). Wire names: `first_name`, `last_name`, `email` (C# properties same). Required fields only: first_name, last_name, email. | `operations/Customers.md` |
| 2c | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest`: `{ subscription: { product_id: int?, customer_id: int?, reference?: string, … } }` | `SubscriptionResponse`: `{ subscription: { id, state, customer_id, product_id, next_assessment_at, current_period_ends_at, … } }` | Case A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | Wire names: `product_id`, `customer_id`, `reference`. State enum values (wire names, not C# member names): `active`, `trialing`, `paused`, `canceled`, `expired`, `past_due`, etc. (map/models/enums.md for full list). Do NOT pass payment method — plan does not require payment on creation (per requirements). | `operations/Subscriptions.md` |
| 3 | `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | Query param `reference` (app's subscription reference) | `SubscriptionResponse`: `{ subscription: { id, state, next_assessment_at, … } }` | Case A: `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404], `TryGetRawError(out RawError)` [fallback] | Lookup subscription by reference (app's internal reference, set at creation). If 404, return empty or null to caller. | `operations/Subscriptions.md` |

### Request Model Details

**CreateCustomer** (inside CreateCustomerRequest.Customer field):
- `FirstName (first_name): string !req` — wire name `first_name`
- `LastName (last_name): string !req` — wire name `last_name`
- `Email (email): string !req` — wire name `email`
- `Reference (reference): string?` — wire name `reference` (set to app user ID for idempotency)
- `Address, City, State, Zip, Country, Phone, …` — optional fields per app needs

**CreateSubscription** (inside CreateSubscriptionRequest.Subscription field):
- `ProductId (product_id): int?` — wire name `product_id` (required unless ProductHandle set)
- `CustomerId (customer_id): int?` — wire name `customer_id` (required — from Maxio customer ID)
- `Reference (reference): string?` — wire name `reference` (set to app's internal subscription ref for lookup)
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — optional (defaults per product)
- **Do NOT include** `PaymentProfileAttributes`, `CreditCardAttributes`, or payment methods — products configured without payment requirement

### Response Model Details

**ProductResponse** returns envelope `{ product: { … } }`:
- `Product (product): Product !req` — the unwrapped payload
  - `Id (id): int?`
  - `Name (name): string?`
  - `Handle (handle): string?`
  - `PriceInCents (price_in_cents): long?` — wire name `price_in_cents` (store as cents; divide by 100 for display)
  - `Interval (interval): int?` — billing cycle count
  - `IntervalUnit (interval_unit): IntervalUnit?` — wire name `interval_unit` (enum: `Month`, `Day`)
  - `TrialInterval, TrialIntervalUnit, TrialPriceInCents` — trial configuration (if applicable)

**CustomerResponse** returns envelope `{ customer: { … } }`:
- `Customer (customer): Customer !req`
  - `Id (id): int?` — Maxio customer ID (store in app if needed for later calls)
  - `Email (email): string?`
  - `FirstName (first_name): string?`, `LastName (last_name): string?` — wire names with underscores
  - `Reference (reference): string?` — the app user ID passed at creation

**SubscriptionResponse** returns envelope `{ subscription: { … } }`:
- `Subscription (subscription): Subscription !req`
  - `Id (id): int?` — Maxio subscription ID
  - `State (state): SubscriptionState?` — enum (see Enums below)
  - `CustomerId (customer_id): int?`
  - `ProductId (product_id): int?`
  - `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — wire name `next_assessment_at` (next billing date)
  - `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` — wire name `current_period_ends_at`
  - `CurrentPeriodStartsAt (current_period_started_at): DateTimeOffset?` — wire name `current_period_started_at`
  - `ActivatedAt (activated_at): DateTimeOffset?`
  - `CanceledAt (canceled_at): DateTimeOffset?`
  - `TrialStartedAt (trial_started_at): DateTimeOffset?`, `TrialEndedAt (trial_ended_at): DateTimeOffset?`
  - `Reference (reference): string?` — the app's subscription reference (set at creation)

### Enums

**SubscriptionState** (`MaxioAdvancedBilling.Models.Enums.SubscriptionState` — StringEnum, NOT C# enum):
- Construct: `SubscriptionState.FromValue("active")` or use static members
- Members (wire values ← C# member names):
  - `Active (active)` — subscription is active and paid
  - `Trialing (trialing)` — in trial period
  - `Assessing (assessing)` — transient state during renewal
  - `PastDue (past_due)` — payment failed; dunning in progress
  - `Canceled (canceled)` — subscription canceled
  - `Paused (paused)` — subscription paused
  - `Suspended (suspended)` — suspended (e.g., payment failure)
  - `Expired (expired)` — subscription term expired
  - `Unpaid (unpaid)` — awaiting payment
  - `TrialEnded (trial_ended)` — trial period ended, subscription not activated
  - `OnHold (on_hold)` — subscription on hold
  - `Pending (pending)` — awaiting activation
  - `FailedToCreate (failed_to_create)` — signup failed
  - `AwaitingSignup (awaiting_signup)` — awaiting customer action to activate
  - For API returns, expect lowercase wire values (`active`, `canceled`, etc.); construct enum via `FromValue(wireValue)`

**IntervalUnit** (`MaxioAdvancedBilling.Models.Enums.IntervalUnit`):
- `Month (month)`, `Day (day)`

**CollectionMethod** (`MaxioAdvancedBilling.Models.Enums.CollectionMethod`):
- `Automatic (automatic)` — charge automatically
- `Remittance (remittance)` — invoice for manual payment
- `Prepaid (prepaid)` — prepaid subscription
- `Invoice (invoice)` — legacy invoice

---

## Client Construction & Configuration

From `sdk-map.md` and auth documentation:

### Client registration (namespaces):
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers; // ServerEnvironment
```

### Authentication (HTTP Basic):
- **Username** = Maxio API key (from config: `Maxio:ApiKey`)
- **Password** = literal string `"x"`
- Set in `MaxioAdvancedBillingClientOptions.BasicAuth` before client construction

### Server configuration:
- **Environment**: `ServerEnvironment.Us` (default; eShopOnWeb is US-hosted)
- **Subdomain override**: `options.Server.Production.Us.Site = "cp-exp-2"` (sandbox)

### Configuration keys (from environment via user-secrets):
- `Maxio:ApiKey` — API key (from Maxio admin)
- `Maxio:Subdomain` — site subdomain (e.g., `cp-exp-2` for sandbox)
- `Maxio:ProductFamilyHandle` — product family handle (e.g., `eshop-subscribe`; optional if filtering by product IDs)
- `Maxio:BaseUrl` — full base URL override (optional; rarely needed)

### Seeded entities in sandbox `cp-exp-2`:
- Product Family: `eshop-subscribe` (ID 3023074, may be stale — confirm via API)
- Pro Plan: `eshop-pro` (ID 7126957, $299/mo)
- Basic Plan: `basic-plan` (ID 7126958, $29/mo)
- Metered Component: `api-call` (ID 3057195, $0.01/unit)
- **Billing**: no trial, no setup fee, no payment method required at creation

---

## Trap Notes

⚠ **Step 1 (client registration)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2b (CreateCustomer) — idempotency boundary** — a duplicate `reference` (app user ID) on Maxio side returns 422 with validation error. **Defensive coding**: always call `ReadCustomerByReference` first; only create if lookup returns 404. Do not rely on Maxio's error to detect existing customer — make the lookup explicit. **MUST load `dotnet-error-handling`** to distinguish 404 (not found, safe to create) from 422 (validation error, likely duplicate).

⚠ **Step 2c (CreateSubscription) — wire model caveat** — the request body wraps the subscription object: `{ subscription: { product_id, customer_id, … } }`. The outer key is literally `"subscription"` (wire name `subscription`, C# property `Subscription`). Do not flatten. **MUST load `dotnet-calling-endpoints`** to confirm named-argument binding for optional params.

⚠ **JSON deserialization mismatch — two directions** (applies to all steps):
   - **Drifted 2xx body** (missing required field on response): surfaces as `JsonException` from deserialization, NOT an `SdkException` — SDK-exception-only catch ladder lets it escape. Boundary must catch `JsonException` and map to diagnostic error.
   - **Non-2xx body that does not match generated error shape**: throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and HTTP status is lost. Boundary that maps every `JsonException` to 5xx will report a deterministic rejection as transient, and caller that retries 5xx retries something that never succeeds.
   **MUST load `dotnet-error-handling`** before writing the integration boundary — the skill shows Case A vs Case B per operation and the TryGet accessor pattern.

⚠ **Wire name mapping** — Maxio uses snake_case wire names (`first_name`, `product_id`, `next_assessment_at`). SDK generates C# PascalCase properties (`FirstName`, `ProductId`, `NextAssessmentAt`). JSON serialization is automatic; but when logging or debugging, expect snake_case in wire payloads and PascalCase in C# models.

---

## REQUIRED READING

Load these skills **before implementation starts**. The sheet deliberately does not carry their contents — each skill carries worked examples, defaults, and defensive-coding patterns you must wire yourself:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Client & DI setup (step 1 — `new MaxioAdvancedBillingClient(…)` and `AddMaxioAdvancedBillingClient`) |
| `dotnet-authentication` | Basic auth credentials & secret loading (step 1 — username = API key, password = `"x"`) |
| `dotnet-calling-endpoints` | Operation calls, named-argument binding, response envelope unwrapping (steps 1–3 — how to call `ReadCustomerByReference`, `CreateSubscription`, etc.) |
| `dotnet-models` | Request/response models, enums (`StringEnum<T>`, not C# enums), field construction & access (steps 2b–2c — building `CreateCustomerRequest`, reading `SubscriptionState`) |
| `dotnet-error-handling` | Exception boundary (try/catch `SdkException<T>` vs `RawError` vs `JsonException`) — Case A vs Case B per operation, TryGet accessors (all steps — distinguish 404 from 422, handle parse failures) |
| `dotnet-configuration-resilience` | Retry, timeout, base-URL configuration (step 1 — what `Timeout` bounds, `HttpMethodsToRetry` semantics, why writes can re-execute) |
| `dotnet-testing` | Mocking/stubbing the `HttpClient` (test phase — how to replace live Maxio calls in unit tests) |

Both of these hazards belong in **all** error-handling boundaries (resolve early):
- `System.Text.Json.JsonException` from **drifted 2xx body** (missing required field during deserialization) surfaces as `JsonException`, not `SdkException` — SDK-exception-only catch lets it escape.
- `System.Text.Json.JsonException` from **non-2xx body mismatch** (error shape does not parse) throws during error object construction, **replacing the `SdkException`** and losing HTTP status — boundary must handle both `SdkException` and `JsonException`.

**MUST load `dotnet-error-handling`** before writing any catch block.

---

## Assumptions & Blockers

### Assumptions
- JWT token decoding/validation is handled by existing eShopOnWeb auth middleware; `User.FindFirst("sub")` or equivalent gives app user ID for `reference` field.
- Product Family `eshop-subscribe` exists in sandbox (or products are pre-seeded by ID; filter in Step 1 accordingly).
- No payment method is required at subscription creation (products configured as "no payment method required" in Maxio).
- Subscription reference field is app-managed (e.g., `{userId}-{productId}` or UUID); Maxio does not enforce uniqueness.
- Base URL for sandbox is `https://cp-exp-2.chargify.com` (default for Maxio US environment with subdomain `cp-exp-2`).

### Blockers
- **None identified.** All required operations are available in the SDK map; no API gaps detected.

---

## Notes

1. **Idempotency**: Always call `ReadCustomerByReference` before `CreateCustomer` to prevent duplicate customer creation on double-click. Same reference + failed network = safe retry.
2. **Subscription reference**: Store in app DB linked to user's subscription. Use for `FindSubscription` lookups in Step 3.
3. **Next billing date**: Returned as `NextAssessmentAt` in SubscriptionResponse; use `CurrentPeriodEndsAt` for display logic (which date the user sees in UI).
4. **State transitions**: Maxio manages state; app reads it. If subscription creation fails (422), do not retry with same data — inspect error and fix (e.g., invalid email).
5. **Metered components** (`api-call`): Out of scope for this phase (Steps 1–3 handle base subscriptions). Future work can add usage tracking via `SubscriptionComponents.UpdateAllocationExpirationDate` or `RecordUsage` operations.

