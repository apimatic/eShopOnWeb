# Maxio Subscription Billing Integration — eShopOnWeb

## Scope & Sequence

| Step | Operations | Description |
|------|-----------|-------------|
| 1 | — | Add NuGet package + configure SDK client (DI + auth + server node) |
| 2 | `ListProductsForProductFamily` | `GET /api/subscription-plans` — list products in the `eshop-subscribe` family |
| 3 | `ReadCustomerByReference`, `CreateCustomer`, `CreateSubscription` | `POST /api/subscriptions` — idempotent: find-or-create customer, then create subscription |
| 4 | `ListCustomerSubscriptions` | `GET /api/my-subscriptions` — list active subscriptions for authenticated user |

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

### SDK Identity

| | |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` |
| Root namespace | `MaxioAdvancedBilling` |
| Client class | `MaxioAdvancedBillingClient` |
| Options class | `MaxioAdvancedBillingClientOptions` |
| Auth | HTTP Basic — `Username` = API key, `Password` = literal `"x"` |
| Environments | `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com` |
| DI extension | `services.AddMaxioAdvancedBillingClient(o => { ... })` |

### Namespaces (add as `using`)

| Contents | Namespace |
|----------|-----------|
| Client & options | `MaxioAdvancedBilling` |
| Basic auth credentials | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Server environments | `MaxioAdvancedBilling.Servers` |
| Retry config | `MaxioAdvancedBilling.Core.Configuration` |
| Operation controllers | `MaxioAdvancedBilling.Api` |
| Records (models) | `MaxioAdvancedBilling.Models` |
| Enums | `MaxioAdvancedBilling.Models.Enums` |
| Error classes | `MaxioAdvancedBilling.Errors` |

### Step 2 — `ListProductsForProductFamily`

| | |
|---|---|
| Controller | `client.ProductFamilies` |
| Signature | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| `productFamilyId` | Pass the product family handle string (e.g. `"eshop-subscribe"`). The map says `string` and the path is `/product_families/{product_family_id}/products.json`. |
| Params `dateField`…`include` | 8 nullable params with no default → **must pass explicitly** (pass `null` to skip) |
| Returns | `IReadOnlyList<ProductResponse>` |
| Error | `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)** |
| Error accessors | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| Pagination | manual `page`+`perPage` |

**Response envelope** (`ProductResponse`, namespace `MaxioAdvancedBilling.Models`):

| Field (wire) | Type |
|---|---|
| `Product (product)` : `Product !req` | The inner product |

**Inner `Product` fields (selected for this integration):**

| CSharpName (wire_name) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Product ID |
| `Name (name)` | `string?` | Display name |
| `Handle (handle)` | `string?` | API handle — use this for subscription creation |
| `Description (description)` | `string?` | |
| `PriceInCents (price_in_cents)` | `long?` | Base price in cents |
| `Interval (interval)` | `int?` | Billing interval |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | `IntervalUnit.Month` or `IntervalUnit.Day` |
| `ProductFamily (product_family)` | `ProductFamily?` | Nested family object |
| `DefaultProductPricePointId (default_product_price_point_id)` | `int?` | Default price point |
| `ProductPricePointId (product_price_point_id)` | `int?` | Active price point ID |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | Active price point handle |

### Step 3a — `ReadCustomerByReference` (find existing customer)

| | |
|---|---|
| Controller | `client.Customers` |
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| Query params | `reference` ← `reference` |
| Returns | `CustomerResponse` |
| Error | `SdkException<RawError>` — **Case B** (404 = customer not found) |
| Error accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |

**Response envelope** (`CustomerResponse`):

| Field (wire) | Type |
|---|---|
| `Customer (customer)` : `Customer !req` | The inner customer |

**Inner `Customer` fields (selected):**

| CSharpName (wire_name) | Type |
|---|---|
| `Id (id)` | `int?` |
| `FirstName (first_name)` | `string?` |
| `LastName (last_name)` | `string?` |
| `Email (email)` | `string?` |
| `Reference (reference)` | `string?` |

### Step 3b — `CreateCustomer` (if not found)

| | |
|---|---|
| Controller | `client.Customers` |
| Signature | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| `body` | nullable, no default → **must pass explicitly** |
| Returns | `CustomerResponse` |
| Error | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| Error accessors | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |

**Request model** (`CreateCustomerRequest`, namespace `MaxioAdvancedBilling.Models`):

| Field (wire) | Type | Required? |
|---|---|---|
| `Customer (customer)` | `CreateCustomer` | **!req** |

**Inner `CreateCustomer` fields:**

| CSharpName (wire_name) | Type | Required? |
|---|---|---|
| `FirstName (first_name)` | `string` | **!req** |
| `LastName (last_name)` | `string` | **!req** |
| `Email (email)` | `string` | **!req** |
| `Reference (reference)` | `string?` | optional — **set this to the user's identity for idempotent lookup** |
| `CcEmails (cc_emails)` | `string?` | optional |
| `Organization (organization)` | `string?` | optional |
| `Address (address)` | `string?` | optional |
| `City (city)` | `string?` | optional |
| `State (state)` | `string?` | optional — ISO 3166-2 |
| `Zip (zip)` | `string?` | optional |
| `Country (country)` | `string?` | optional — ISO 3166-1 2-char |
| `Phone (phone)` | `string?` | optional |
| `Locale (locale)` | `string?` | optional |
| `TaxExempt (tax_exempt)` | `bool?` | optional |

### Step 3c — `CreateSubscription`

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Signature | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| `body` | nullable, no default → **must pass explicitly** |
| Returns | `SubscriptionResponse` |
| Error | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| Error accessors | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |

**Request model** (`CreateSubscriptionRequest`, namespace `MaxioAdvancedBilling.Models`):

| Field (wire) | Type | Required? |
|---|---|---|
| `Subscription (subscription)` | `CreateSubscription` | **!req** |

**Inner `CreateSubscription` fields (selected for this integration):**

| CSharpName (wire_name) | Type | Required? | Notes |
|---|---|---|---|
| `ProductHandle (product_handle)` | `string?` | optional | **Use this** — pass the plan handle (e.g. `"eshop-pro"`) |
| `ProductId (product_id)` | `int?` | optional | Alternative to product_handle |
| `CustomerReference (customer_reference)` | `string?` | optional | Pass the user's reference to link to existing customer |
| `CustomerId (customer_id)` | `int?` | optional | Alternative to customer_reference |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | optional | Specific price point |
| `ProductPricePointId (product_price_point_id)` | `int?` | optional | Specific price point ID |
| `Quantity (quantity)` | `int?` | optional | For quantity-based components |
| `CouponCode (coupon_code)` | `string?` | optional | |
| `CouponCodes (coupon_codes)` | `IReadOnlyList<string>?` | optional | |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | optional | |
| `Reference (reference)` | `string?` | optional | Subscription reference for lookup |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | optional | Create customer inline (not needed here) |
| `Components (components)` | `IReadOnlyList<CreateSubscriptionComponent>?` | optional | For component allocation |
| `Metafields (metafields)` | `IReadOnlyDictionary<string, string>?` | optional | |
| `CalendarBilling (calendar_billing)` | `CalendarBilling?` | optional | |
| `DeferSignup (defer_signup)` | `bool?` | optional, default `false` | |

**Response envelope** (`SubscriptionResponse`, namespace `MaxioAdvancedBilling.Models`):

| Field (wire) | Type |
|---|---|
| `Subscription (subscription)` | `Subscription?` |

**Inner `Subscription` fields (selected for this integration):**

| CSharpName (wire_name) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Subscription ID |
| `State (state)` | `SubscriptionState?` | Current state (enum) |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | Price in cents |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | Current billing period end |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | Next billing date |
| `ActivatedAt (activated_at)` | `DateTimeOffset?` | Activation timestamp |
| `CanceledAt (canceled_at)` | `DateTimeOffset?` | Cancellation timestamp |
| `CancelAtEndOfPeriod (cancel_at_end_of_period)` | `bool?` | Pending cancellation flag |
| `ProductId (product_id)` | `int?` | |
| `Customer (customer)` | `Customer?` | Nested customer |
| `Product (product)` | `Product?` | Nested product |

### Step 4 — `ListCustomerSubscriptions`

| | |
|---|---|
| Controller | `client.Customers` |
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Error | `SdkException<RawError>` — **Case B** |
| Error accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none (returns all for customer) |

Response envelope is the same `SubscriptionResponse` as Step 3c.

### Key Enums

**`SubscriptionState`** (namespace `MaxioAdvancedBilling.Models.Enums`):

Members: `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`

**`SubscriptionStateFilter`** (for `ListSubscriptions` query filter):

Members: `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`

**`CollectionMethod`**: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`

**`IntervalUnit`**: `Day (day)`, `Month (month)`

### Error Handling Summary

| Operation | Error Case | Accessor Pattern |
|---|---|---|
| `ListProductsForProductFamily` | **Case A** — `SdkException<ListProductsForProductFamilyError>` | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| `ReadCustomerByReference` | **Case B** — `SdkException<RawError>` | `StatusCode` · `ReadAsString()` |
| `CreateCustomer` | **Case A** — `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| `CreateSubscription` | **Case A** — `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| `ListCustomerSubscriptions` | **Case B** — `SdkException<RawError>` | `StatusCode` · `ReadAsString()` |

---

## Trap Notes

⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 1 (client registration) — the SDK client wrapper may be transient; the `HttpClient`/handler pipeline must be long-lived and reused via `IHttpClientFactory`. **MUST load `dotnet-client-initialization`** before writing DI registration.

⚠ Step 1 (auth) — set credentials before constructing the client or in the DI callback; the auth pattern is HTTP Basic (username = API key, password = `"x"`). **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 2 (list plans) — `ListProductsForProductFamily` has 8 nullable params with no default that must be passed explicitly (pass `null` to skip). Positional calls will mis-bind. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Step 3 (create subscription) — `CreateSubscriptionRequest` wraps a `CreateSubscription` which is all-optional; the Notes say product can be identified by `product_handle` or `product_id`, and customer by `customer_id` or `customer_reference`. Choose the handle/reference path for this integration. **MUST load `dotnet-models`** before constructing request payloads.

⚠ Step 3 (idempotent customer) — `ReadCustomerByReference` is Case B (raw error); a 404 means "not found" — check `ex.Error.StatusCode == HttpStatusCode.NotFound` before creating. Do not parse the body for this check. **MUST load `dotnet-error-handling`** before writing error boundaries.

---

## REQUIRED READING

Load every `dotnet-*` skill below **before implementation starts**. The sheet deliberately does not carry their contents — it names the hazard and hands you the skill that resolves it.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, builder/options shape, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — HTTP Basic auth pattern, credential configuration |
| `dotnet-calling-endpoints` | Steps 2–4 — calling operations, required vs optional params, async usage |
| `dotnet-models` | Steps 2–4 — building request models, required members, enums, wire names |
| `dotnet-error-handling` | Steps 2–4 — which exception types reach catch blocks, how to read status codes and error bodies |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, base-URL/server selection |

**Two `System.Text.Json.JsonException` hazard rows (mandatory):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

1. **Product family handle is stable** — the task says `eshop-subscribe` is the handle. If the handle differs in the actual Maxio sandbox, the `ListProductsForProductFamily` call will 404. **YOUR CALL — verify in sandbox.**
2. **Customer reference = JWT identity** — the plan assumes the caller's identity from the JWT (e.g. user ID or email) is used as the Maxio customer `reference` for idempotent lookup. **YOUR CALL — which JWT claim to use.**
3. **No payment profile required** — the plan does not pass `payment_profile_id` or `credit_card_attributes` to `CreateSubscription`. If the product requires a credit card on file, the call will fail with a 422. **YOUR CALL — whether to collect payment info.**
4. **Metered component (`api-call`) is not wired in Step 3** — the task mentions it exists but the subscription creation endpoint does not require it. Usage reporting would be a separate integration step.
5. **No `BaseUrl` override needed** — if `Maxio:BaseUrl` is set in config, it must be applied to `options.Server.Production.Us.BaseUrl`. The plan assumes default US hosting unless overridden.
