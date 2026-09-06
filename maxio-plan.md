# Maxio Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Initialization** — Register Maxio SDK client with DI, configure auth (API key from config binding), set base URL override if using sandbox host
2. **List Products** — `GET /api/subscription-plans` calls `Products.ListProducts()` to fetch available plans, returns Product records with pricing
3. **Create/Lookup Customer** — `POST /api/subscriptions` resolves current user to Maxio customer via `Customers.ReadCustomerByReference(user.Id)`, or creates via `CreateCustomer()` (idempotent via `reference` = user ID)
4. **Create Subscription** — `POST /api/subscriptions` calls `Subscriptions.CreateSubscription()` with product handle and customer ID, awaits response, returns subscription ID + state to caller
5. **List User Subscriptions** — `GET /api/my-subscriptions` calls `Customers.ListCustomerSubscriptions(customerId)` to fetch all subscriptions for logged-in user
6. **Error Boundary** — Catch `SdkException<T>` (typed and raw), map HTTP status to domain errors (401 → Unauthorized, 4xx → Validation, 5xx → Infrastructure), return JSON error response following PublicApi conventions

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Controller · Method signature | Request model + fields (wire name: type, required?) | Response envelope · inner fields | Error case (A/B) · TryGet… accessors + type | Source |
|---|---|---|---|---|---|
| **List products (available subscription plans)** | `client.Products.ListProducts(null, null, null, null, null, null, null, null, 1, 20, ct)` — GET /products.json — 8 nullable filter params must pass as `null` to skip; defaults: `page=1`, `perPage=20` | None (query params only) | `IReadOnlyList<ProductResponse>` — each item: `Product (product): Product !req` — extract `product.Handle`, `product.Name`, `product.PriceInCents`, `product.Interval`, `product.IntervalUnit` | **Case B** — `SdkException<RawError>` — `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>` | `operations/Products.md` |
| **Create or lookup customer** | `client.Customers.CreateCustomer(new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = "...", LastName = "...", Email = "...", Reference = userIdFromClaims } }, ct)` — POST /customers.json | `CreateCustomerRequest` (envelope) · `Customer (customer): CreateCustomer !req` — nested fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` | `CustomerResponse` (envelope) · `Customer (customer): Customer !req` — extract `customer.Id` (Maxio customer ID), `customer.Reference` (echoes request reference for idempotency) | **Case A** — `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | `operations/Customers.md` |
| **Retrieve existing customer by reference** | `client.Customers.ReadCustomerByReference(userIdFromClaims, ct)` — GET /customers/lookup.json?reference={reference} | None (reference is query param) | `CustomerResponse` (envelope) · `Customer (customer): Customer !req` — extract `customer.Id` | **Case B** — `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | `operations/Customers.md` |
| **Create subscription** | `client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = "eshop-pro", CustomerId = maxioCustomerId, Reference = userIdFromClaims } }, ct)` — POST /subscriptions.json | `CreateSubscriptionRequest` (envelope) · `Subscription (subscription): CreateSubscription !req` — key fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?` (either handle or ID required), `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?` (either ID or reference), `Reference (reference): string?` (for idempotency), `PaymentProfileId (payment_profile_id): int?` (optional; if omitted may require `CreditCardAttributes` or `BankAccountAttributes`), all others optional | `SubscriptionResponse` (envelope) · `Subscription (subscription): Subscription?` — extract `subscription.Id`, `subscription.State` (e.g. `SubscriptionState.Active`), `subscription.ActivatedAt`, `subscription.CurrentPeriodEndsAt` | **Case A** — `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | `operations/Subscriptions.md` |
| **List subscriptions for customer** | `client.Customers.ListCustomerSubscriptions(maxioCustomerId, ct)` — GET /customers/{customer_id}/subscriptions.json | None (ID is path param) | `IReadOnlyList<SubscriptionResponse>` — each item: `Subscription (subscription): Subscription?` — extract `subscription.Id`, `subscription.State`, `subscription.Product.Handle` | **Case B** — `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | `operations/Customers.md` |

### Enum Values (used in responses, not requests)

From `map/models/enums.md`:

**`SubscriptionState`** (`MaxioAdvancedBilling.Models.Enums.SubscriptionState`) — StringEnum, wire values:
- `SubscriptionState.Active ("active")` — normal, paid, up-to-date subscription
- `SubscriptionState.Canceled ("canceled")` — subscription was canceled
- `SubscriptionState.PastDue ("past_due")` — payment failed, dunning in progress
- `SubscriptionState.Trialing ("trialing")` — in trial period
- `SubscriptionState.Paused ("paused")` — temporarily on hold
- (others: `Pending`, `AwaitingSignup`, `Expired`, `Suspended`, `SoftFailure`, `Unpaid`, `TrialEnded`, `OnHold`, `FailedToCreate`, `Assessing`)

**`IntervalUnit`** (`MaxioAdvancedBilling.Models.Enums.IntervalUnit`) — StringEnum, wire values:
- `IntervalUnit.Month ("month")`
- `IntervalUnit.Day ("day")`

### Request/Response Model Details

**`CreateCustomer`** (namespace `MaxioAdvancedBilling.Models`)
- `FirstName (first_name): string !req`
- `LastName (last_name): string !req`
- `Email (email): string !req`
- `Reference (reference): string?` — your internal customer ID; unique per site; enables idempotent creates + lookup
- `CcEmails`, `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber` — all optional

**`CreateSubscription`** (namespace `MaxioAdvancedBilling.Models`)
- **Product identifier** (one of):
  - `ProductHandle (product_handle): string?` — e.g. "eshop-pro"
  - `ProductId (product_id): int?`
- **Customer identifier** (one of):
  - `CustomerId (customer_id): int?` — Maxio customer ID from create/lookup response
  - `CustomerReference (customer_reference): string?` — your internal user ID; if provided, Maxio looks up by reference
- `Reference (reference): string?` — your internal subscription ID; enables idempotent creates
- `PaymentProfileId (payment_profile_id): int?` — if omitted, subscription may still succeed if product allows (e.g. invoice collection)
- `CouponCode`, `CouponCodes`, `Components` (metered component allocations) — optional
- All other fields optional; see full table in `records-2-Cr-Ne.md` row `CreateSubscription`

**`Product`** (response, namespace `MaxioAdvancedBilling.Models`)
- `Id (id): int?` — Maxio product ID
- `Handle (handle): string?` — product handle (e.g. "eshop-pro")
- `Name (name): string?` — display name
- `Description (description): string?`
- `PriceInCents (price_in_cents): long?` — monthly price in cents (e.g. 29900 = $299.00)
- `Interval (interval): int?` — billing period (e.g. 1 for monthly)
- `IntervalUnit (interval_unit): IntervalUnit?` — `Month`, `Day`
- `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit` — trial period (if any)
- `ExpirationInterval`, `ExpirationIntervalUnit` — auto-expiration (if any)
- `CreatedAt`, `UpdatedAt` — timestamps

**`Subscription`** (response, namespace `MaxioAdvancedBilling.Models`)
- `Id (id): int?` — Maxio subscription ID
- `State (state): SubscriptionState?` — current state
- `Product (product): Product?` — nested product info
- `Customer (customer): Customer?` — nested customer info (ID, reference, email, etc.)
- `ActivatedAt (activated_at): DateTimeOffset?` — when subscription became active
- `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`
- `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` — next billing date
- `CanceledAt (canceled_at): DateTimeOffset?` — if canceled
- `ExpiresAt (expires_at): DateTimeOffset?` — expiration date (if product has expiration)
- `TrialStartedAt`, `TrialEndedAt` — trial dates (if in trial)
- `Reference (reference): string?` — echoes your subscription ID
- `BalanceInCents (balance_in_cents): long?` — account balance (prepayment or credit)
- `ProductPriceInCents (product_price_in_cents): long?` — current subscription price

---

## Trap Notes

⚠ **Step 1 (client registration)** — The SDK's `RetryOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. `Timeout` is per-attempt, not total; the floor on `MaxRetries` is 1 (0 is invalid); and transport failures are retried on every verb including POST (non-idempotent writes can execute more than once if the network fails mid-response). **MUST load `dotnet-configuration-resilience`** before wiring retry and timeout settings.

⚠ **Step 2 (authentication)** — Maxio uses HTTP Basic auth: `Username = API_KEY`, `Password = literal "x"` (not empty, not a placeholder). The SDK's `BasicAuthCredentials` property must be set before the client is constructed, or in the DI callback. **MUST load `dotnet-authentication`** before wiring credentials from configuration.

⚠ **Step 3 (calling operations)** — Every operation is **throw-only**; there are no `…Result` no-throw variants in this SDK. Many list and read operations return `SdkException<RawError>` (Case B, no typed accessors); creation operations return `SdkException<TypedError>` (Case A, with `TryGet…` methods). You must catch by the specific generic type or the outer catch may not fire. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (error boundary—critical)** — **Two `JsonException` sources, opposite handling:**
  - **Malformed 2xx body** (e.g. missing `required` field in response) throws `JsonException` from deserialization **before the SDK exception is even constructed** → **not** an `SdkException` → an exception-only catch on `SdkException` lets it escape your boundary → 2xx that fail to deserialize must be treated as HTTP 500 infrastructure failures.
  - **Non-2xx body that doesn't match the operation's generated `{Operation}Error` shape** throws `JsonException` **while the `SdkException` itself is being constructed**, so the `JsonException` replaces the `SdkException` → HTTP status is lost → catch `JsonException` before (not inside) the `SdkException` block, map it to HTTP 500 if it arrives during error handling, and **never treat it as a validation error** (the provider sent bad error JSON, not bad user input).

⚠ **Step 5 (models)** — `SubscriptionState` and `IntervalUnit` are **not** C# enums; they are `StringEnum<T>` records. Construct via static members (e.g. `SubscriptionState.Active`) or `SubscriptionState.FromValue("active")`. Enums do **not** go through the `new` keyword. Nested records like `Customer` and `Product` inside subscription responses are immutable `record` types with `init`-only setters. **MUST load `dotnet-models`** before reading any nested response field or comparing state.

⚠ **Step 6 (idempotency)** — The SDK provides no built-in idempotency keys. Idempotency is application-level: use the `Reference` field (your domain's customer/subscription ID) on create requests. Maxio will reject a second create with the same `reference` **only if the original succeeded**; a failed create does **not** reserve the reference. Always call `ReadCustomerByReference(userId)` first to check if the customer already exists before calling `CreateCustomer`. **MUST load `dotnet-calling-endpoints`** for the sequencing pattern.

⚠ **Step 6 (metered usage / component pricing)** — The Pro and Basic plans have **no** metered components by default; the `api-call` metered component (handle: `api-call`, $0.01/unit) is **separate**. To charge for API calls on a subscription, allocate usage via `SubscriptionComponents.CreateAllocation()` or `SubscriptionComponents.AllocateComponents()` — **not** included in initial subscription create unless `Components` is nested in `CreateSubscription`. Design determines whether this endpoint charges per-call (real-time), per-billing-period (batch), or hybrid. **MUST load `dotnet-calling-endpoints`** to understand which `SubscriptionComponents` operation is right for your billing model.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents. Each skill resolves a gotcha the signature hides.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1: client construction, `HttpClient` reuse, DI registration, `MaxioAdvancedBillingClientOptions` |
| `dotnet-authentication` | Step 2: `BasicAuthCredentials`, loading API key from configuration, setting `Username`/`Password` |
| `dotnet-calling-endpoints` | Step 3–4: calling controller methods, required vs optional parameters, async/await, cancellation tokens, sequencing (lookup before create) |
| `dotnet-models` | Step 4: `StringEnum<T>` construction + comparison, immutable `record` fields, nested objects, union types (if any response uses `OneOf`/`AnyOf`) |
| `dotnet-error-handling` | Step 5–6: catching `SdkException<T>` by generic type, `TryGet…` accessors on Case A errors, `JsonException` handling in the boundary, `RawError` on Case B |
| `dotnet-configuration-resilience` | Step 1: `RetryOptions`, `Timeout` per-attempt semantics, retry bounds, transport failure semantics, base URL override for sandbox/dev hosts |

**CRITICAL: Handle `JsonException` twice:**
- A drifted or malformed **2xx response body** (missing `required` field) surfaces as `JsonException` from deserialization, **not** `SdkException` → SDK-exception-only catch lets it escape → your boundary must map it to HTTP 500, never treat as validation error.
- A **non-2xx response body** that doesn't match the operation's `{Operation}Error` shape throws `JsonException` **while constructing the `SdkException`**, destroying the HTTP status → catch `JsonException` in the error-handling block before `SdkException`, map to 500, never assume the original status is recoverable.

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb's PublicApi currently has no Maxio integration; adding it does not conflict with existing subscription/billing logic (if any).
- The logged-in user's identity is available via JWT claims, e.g. `User.FindFirst(ClaimTypes.NameIdentifier)` gives a user ID that will be stable across Maxio calls (used as `Reference` for idempotency).
- The sandbox site `cp-exp-2` is accessible via API from the environment where the code runs (firewall/network allows `https://cp-exp-2.chargify.com`). Configuration binding will supply `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` (= "cp-exp-2"), and optionally `MAXIO_ENVIRONMENT` (defaults to `US`).
- The integration does NOT handle payment-method collection (credit card, bank account) in the PublicApi; subscription create will use an existing payment profile ID or inline card attributes (out of scope for hero flow). If a subscription create requires payment and none is provided, Maxio returns a 422 error with details; the API will relay this to the client.
- The `.NET 8 → .NET 10 SDK` mismatch is resolved on the developer/build machine via `DOTNET_ROLL_FORWARD=Major` (or pinning to .NET 10 before building). The deployed environment will have a compatible runtime.
- Database is in-memory (as configured); subscription records are **not** persisted locally (integration reads from Maxio on each call via `ListCustomerSubscriptions` etc.).

**Blockers:**
- None identified. Maxio API surface, SDK, and configuration are all available and documented in the map.
