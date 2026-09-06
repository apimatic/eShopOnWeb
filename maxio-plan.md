# Maxio Integration Plan — eShopOnWeb Subscription Capability

## Scope & Sequence

1. **Client setup** — Initialize the Maxio SDK client with Basic auth (API key + "x"), register in DI, configure for the Maxio site.
2. **Fetch plans** — `ListProducts` to populate the plan catalog (filter by the `eshop-subscribe` family via product IDs or by listing with filter).
3. **Ensure customer exists** — `ReadCustomerByReference` (idempotent lookup via eShopOnWeb user ID as `reference`); create with `CreateCustomer` if not found, seeding `reference = userId`.
4. **Create subscription** — `CreateSubscription` to enroll the authenticated user in a selected plan (product ID), referencing the Maxio customer.
5. **List subscriptions** — `ListSubscriptions` filtered to the current customer to show active subscriptions in the account view.
6. **Error & state handling** — Wrap all calls with SDK exception handling, distinguish typed vs raw errors, extract error details for user-facing messages.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Step | Operation | Signature | Request Model | Response | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 2 | List plans | `client.Products.ListProducts(dateField: null, filter: null, endDate: null, endDatetime: null, startDate: null, startDatetime: null, includeArchived: null, include: null, page: 1, perPage: 20, ct: default)` Returns `IReadOnlyList<ProductResponse>` — each `.Product` is a `Product` record with `Id`, `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`, etc. | None (query-only) | `IReadOnlyList<ProductResponse>` — unwrap each to `.Product` for display | Case B: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | Manual: `page` (default 1), `perPage` (default 20) | `operations/Products.md` |
| 3a | Lookup customer by reference | `client.Customers.ReadCustomerByReference(reference: userId, ct: default)` Returns `CustomerResponse` | None (query param `reference`) | `CustomerResponse` with `.Customer: Customer` field (`Id`, `Email`, `FirstName`, `LastName`, `Reference`, etc.) | Case B: `SdkException<RawError>` — 404 when not found; caller must handle via `.Error.StatusCode` | None | `operations/Customers.md` |
| 3b | Create customer (if not found) | `client.Customers.CreateCustomer(body: request, ct: default)` Returns `CustomerResponse` | `CreateCustomerRequest` containing `Customer: CreateCustomer` with required fields `FirstName`, `LastName`, `Email` (all `string`), optional `Reference` (for idempotency); pass eShopOnWeb `userId` as `Reference` wire name `reference` | `CustomerResponse` with `.Customer: Customer` | Case A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 validation]; `.TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| 4 | Create subscription | `client.Subscriptions.CreateSubscription(body: request, ct: default)` Returns `SubscriptionResponse` | `CreateSubscriptionRequest` containing `Subscription: CreateSubscription` with required *OR* conditional fields: `CustomerId` (int, Maxio customer ID from step 3) **OR** `CustomerAttributes` (nested customer creation — not used; we use customer from step 3); `ProductId` (int, Maxio product ID — e.g. 7126957 for Pro Plan) **OR** `ProductHandle` (string); all other subscription fields optional (e.g. `Reference` for eShopOnWeb subscription ID, `InitialBillingAt`, `PaymentProfileId` if payment method pre-registered). See Notes: at minimum, requires customer ID + product identifier. | `SubscriptionResponse` with `.Subscription: Subscription` field (`Id`, `State: SubscriptionState`, `ProductPriceInCents`, `CurrentPeriodEndsAt: DateTimeOffset?`, `NextAssessmentAt: DateTimeOffset?`, `ProductId`, `CustomerId`, etc.) | Case A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422 validation errors in `.Errors: IReadOnlyList<string>`]; `.TryGetRawError(out RawError)` [fallback] | None | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| 5 | List subscriptions | `client.Subscriptions.ListSubscriptions(state: null, product: null, productPricePointId: null, coupon: null, couponCode: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, metadata: null, direction: null, sort: null, include: null, page: 1, perPage: 20, ct: default)` Returns `IReadOnlyList<SubscriptionResponse>` | None (query-only); optional filters: `state: SubscriptionStateFilter?` to filter by state (e.g. `Active`); no direct customer-ID filter — caller must list all/filtered and match by `subscription.CustomerId` in app logic | `IReadOnlyList<SubscriptionResponse>` — unwrap each to `.Subscription` for display (read `.State`, `.ProductPriceInCents`, `.NextAssessmentAt`, etc.) | Case B: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | Manual: `page` (default 1), `perPage` (default 20) | `operations/Subscriptions.md` |

### Models (field details)

#### `CreateCustomerRequest`
- Namespace: `MaxioAdvancedBilling.Models`
- Fields: `Customer (customer): CreateCustomer !req`
- Wire shape: `{ "customer": { "first_name": "…", … } }`

#### `CreateCustomer` (unwrapped inside request)
- Namespace: `MaxioAdvancedBilling.Models`
- Required: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`
- Optional: `Reference (reference): string` (use for eShopOnWeb `userId` to enable idempotent lookup via `ReadCustomerByReference`)
- Source: `records-1-Ac-Cr.md`

#### `CustomerResponse`
- Namespace: `MaxioAdvancedBilling.Models`
- Fields: `Customer (customer): Customer !req`
- Unwrap `.Customer` to access `Id` (Maxio customer ID), `Email`, `FirstName`, `LastName`, `Reference`
- Source: `records-2-Cr-Ne.md`

#### `CreateSubscriptionRequest`
- Namespace: `MaxioAdvancedBilling.Models`
- Fields: `Subscription (subscription): CreateSubscription !req`
- Wire shape: `{ "subscription": { "customer_id": 123, "product_id": 7126957, … } }`

#### `CreateSubscription` (unwrapped inside request)
- Namespace: `MaxioAdvancedBilling.Models`
- Required: Either `CustomerId (customer_id): int?` (Maxio customer ID) **OR** `CustomerAttributes (customer_attributes): CustomerAttributes?` (for inline customer creation — not used in this plan; use step 3 customer)
- Required: Either `ProductId (product_id): int?` **OR** `ProductHandle (product_handle): string?` (e.g. `"eshop-pro"`)
- Optional: `Reference (reference): string?` (map to eShopOnWeb subscription ID for cross-reference)
- Optional: `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `NextBillingAt (next_billing_at): DateTimeOffset?`
- Notes per `Subscriptions.md`: "Creates a Subscription for a customer and product. … To set a specific product price point, use `product_price_point_handle` or `product_price_point_id`. Identify an existing customer with `customer_id` or `customer_reference`. … Payment information may be required to create a subscription, depending on the options for the Product being subscribed."
- Source: `records-2-Cr-Ne.md`

#### `SubscriptionResponse`
- Namespace: `MaxioAdvancedBilling.Models`
- Fields: `Subscription (subscription): Subscription?`
- Unwrap `.Subscription` to access `Id`, `State: SubscriptionState?`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `CreatedAt`, `CustomerId`, `ProductId`, `Reference`, etc.
- Source: `records-4-Su-We.md`

#### `ProductResponse`
- Namespace: `MaxioAdvancedBilling.Models`
- Fields: `Product (product): Product !req`
- Unwrap `.Product` to access `Id`, `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`, `Description`, etc.
- Source: `records-3-Of-Su.md`

#### Error payloads

**Case A (CreateCustomer, CreateSubscription):**
- `CustomerErrorResponse1` (from `TryGetCustomerErrorResponse1`):
  - Namespace: `MaxioAdvancedBilling.Models`
  - Fields: `Errors (errors): Errors?` where `Errors` has fields like `PerPage: IReadOnlyList<string>?`, `PricePoint: IReadOnlyList<string>?` (custom error keys)
  - Source: `records-2-Cr-Ne.md`
- `ErrorListResponse1` (from `TryGetErrorListResponse1` on CreateSubscription):
  - Namespace: `MaxioAdvancedBilling.Models`
  - Fields: `Errors (errors): IReadOnlyList<string> !req`
  - Source: `records-2-Cr-Ne.md`

**Case B (ListSubscriptions, ListProducts, ReadCustomerByReference):**
- `RawError`:
  - Namespace: `MaxioAdvancedBilling.Core.ErrorResponse`
  - Members: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`

### Enums (required wire values)

| Enum | Namespace | Members (C# name ← wire value) | Use | Source |
|---|---|---|---|---|
| `SubscriptionStateFilter` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` | Filter subscriptions in `ListSubscriptions` query (step 5); pass `state: SubscriptionStateFilter.Active` to list only active plans | `enums.md` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | Read from subscription response `.State` field to determine if subscription is active, canceled, etc. | `enums.md` |

### Client Setup & Auth

- **Package**: `AsadAli.AdvancedBilling.Sdk` (NuGet)
- **Root namespace**: `MaxioAdvancedBilling`
- **Client class**: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`
- **Auth** — HTTP Basic:
  - Namespace: `MaxioAdvancedBilling.Core.Authentication.Basic`
  - Class: `BasicAuthCredentials { Username = apiKey, Password = "x" }`
  - Set in options: `options.BasicAuth = new BasicAuthCredentials { Username = config["Maxio:ApiKey"], Password = "x" };`
- **Environments**:
  - `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`
  - `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`
  - Set `options.Environment = ServerEnvironment.Us` (or `.Eu` if required)
- **Server override** (for custom base URL):
  - `options.Server.Production.Us.BaseUrl = customUrl`; or
  - `options.Server.Production.Us.Site = "subdomain"` (defaults to `options.Server.Production.Us.Site`)
- **Configuration properties** (all on `MaxioAdvancedBillingClientOptions`):
  - `Environment: ServerEnvironment`
  - `BasicAuth: BasicAuthCredentials?`
  - `Retry: RetryOptions?`
  - `Server: ServerOptions?`
- **DI alternative** — `ServiceCollectionExtensions.cs` provides `AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions> configure)`

---

## Trap Notes

⚠ **Step 1 (Client setup & DI)** — The SDK's retry/timeout options (`RetryOptions`) do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. The `Timeout` field is per-attempt; `MaxRetries` gates retries on specific HTTP status codes listed in `StatusCodesToRetry`, and transport failures (`HttpRequestException`) are retried on **every** verb (including `POST`), so non-idempotent writes can execute more than once. Configuration defaults matter. **MUST load `dotnet-configuration-resilience`** before wiring the client and choosing retry/timeout values.

⚠ **Step 1 (Auth)** — Basic auth requires `Username = API_KEY` and `Password = literal "x"` (not a placeholder). The API key is `Maxio:ApiKey` from configuration. Do **not** hardcode; load from `IConfiguration`. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 2–5 (Calling endpoints & error handling)** — The Subscriptions and Products controllers are `client.Subscriptions` and `client.Products`. All operations are throw-based; there are no no-throw `…Result` variants. `CreateCustomer` and `CreateSubscription` are **Case A (typed error)** — catch `SdkException<CreateCustomerError>` and `SdkException<CreateSubscriptionError>`, call `.Error.TryGet…()` to extract the payload; `ListSubscriptions`, `ListProducts`, and `ReadCustomerByReference` are **Case B (raw error)** — catch `SdkException<RawError>`. JSON deserialization errors from malformed 2xx responses surface as `JsonException` (not wrapped in an `SdkException`), so a boundary that catches SDK exceptions only will let malformed responses escape. **MUST load `dotnet-error-handling`** before writing the catch ladder and boundary.

⚠ **Step 3a (Lookup idempotency)** — `ReadCustomerByReference` returns a 404 (`StatusCode` in the raw error) when the customer does not exist. The caller **must** check `.Error.StatusCode == HttpStatusCode.NotFound` and branch to `CreateCustomer`. A 404 is not an exception in the business logic; it's a signal to create. Do **not** treat 404 as a failure — it is expected on first signup.

⚠ **Step 4 (Create subscription)** — The `CreateSubscription` operation requires **either** a `customer_id` (Maxio customer ID) **or** a `customer_reference` (wire name `customer_reference`, C# field `CustomerReference`). The plan uses the ID from step 3 (stored after customer creation). If the Maxio customer has a `Reference` set (eShopOnWeb user ID), `customer_reference` can be used instead, but the plan uses the `customer_id` path for clarity. Passing both is allowed (customer_id takes precedence). **MUST load `dotnet-calling-endpoints`** for the parameter order and which fields are truly required vs optional in the request builder.

⚠ **Step 5 (List subscriptions)** — The `ListSubscriptions` operation has **no** direct filter by customer ID. It returns subscriptions for the **entire site**. The caller **must** filter the results in app logic by comparing `subscription.CustomerId` (read from the unwrapped `.Subscription` record) against the current eShopOnWeb user's Maxio customer ID. Pagination is manual (`page`, `perPage`). **MUST load `dotnet-calling-endpoints`** for pagination semantics and the full parameter list.

**REQUIRED READING** — Load these companion skills **before implementation starts**. The sheet does not carry their contents; these are the gates that must open first:

- **`dotnet-client-initialization`** — Step 1: How to construct `MaxioAdvancedBillingClient`, DI registration, `HttpClient` lifecycle (long-lived, reused via `IHttpClientFactory`).
- **`dotnet-authentication`** — Step 1: How to set Basic auth credentials (username = API key, password = `"x"`), load from config, and wire into the client.
- **`dotnet-calling-endpoints`** — Steps 2–5: Operation signatures, required vs optional parameters, request/response envelope shapes, named vs positional arguments, async/await, cancellation token usage.
- **`dotnet-models`** — Steps 2–5: How to construct request records (immutable with `init` setters, `required` fields must be set in initializer), read response records, work with enums (`StringEnum<T>` — use static members or `.FromValue(wire)`), and unions (if any in payloads — construct via factory, read via `TryGet…`).
- **`dotnet-error-handling`** — Steps 2–5: Distinguishing Case A (typed error with `TryGet…` accessors) from Case B (raw error), catch hierarchy (SDK exceptions first, then `JsonException` for deserialization failures), reading error payloads, HTTP status extraction.
- **`dotnet-configuration-resilience`** — Step 1: Retry options (`MaxRetries`, `StatusCodesToRetry`, `HttpMethodsToRetry`), timeout semantics (per-attempt, not total), backoff strategy, and defaults.
- **`dotnet-testing`** — As needed: Mocking the SDK via the `HttpClient` test seam, matching your test framework, isolation patterns.

---

## Assumptions & Blockers

**Assumptions:**

1. The eShopOnWeb user's unique identifier (e.g. `User.Id`, or a persisted Maxio customer ID mapping) is available to the integration code, so it can be passed as the `Reference` field on customer creation and used for lookup in `ReadCustomerByReference`.
2. Plan listing (step 2) does not require dynamic filtering by product family; the UI will be populated from the Maxio products returned and the user will select by name/price. (The product family `eshop-subscribe` is known on the Maxio account, but the API fetch is agnostic to it; if product-family-level filtering is required later, a second plan can add a `ProductFamilies` call to enumerate families and filter.)
3. Payment method is handled separately (out of scope): either payment profiles are pre-registered on the Maxio customer, or the `CreateSubscription` call will fail with a 422 if payment is required and none is present. The plan assumes the sandbox/site configuration allows subscription creation without a payment method (e.g. invoice collection or on-account credit).
4. No subscription cancellation, pause, resume, or update logic is in scope; only creation and listing are implemented.

**Blockers:**

None identified. The map is complete; all required operations, models, and error types are in the SDK. The configuration (API key, subdomain, base URL override if needed) is external (env vars, config file) and assumed to be available.

---

*Plan written per maxio-plan.md contract and grounded in SDK map (version v1.0.2, commit 15db14b).*
