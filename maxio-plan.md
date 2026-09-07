# eShopOnWeb Maxio Subscription Integration — Contract Sheet

## Scope & sequence

1. **Client & DI registration** — Initialize `MaxioAdvancedBillingClient` with HTTP Basic auth
2. **List subscription plans** — GET endpoint calls `ListProducts` → display available plans
3. **Ensure customer exists** — GET endpoint calls `ReadCustomerByReference` (idempotent lookup) or `CreateCustomer` (if not found)
4. **Create subscription** — POST endpoint calls `CreateSubscription` with product handle, customer reference, and optional payment method
5. **List user's subscriptions** — GET endpoint calls `ListCustomerSubscriptions` or `ListSubscriptions` filtered by customer
6. **Return plan/subscription details** — Parse `SubscriptionResponse.Subscription` and `ProductResponse.Product` to build API responses

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Operation | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **List Plans** (GET /api/subscription-plans) | `client.Products.ListProducts(dateField: null, filter: null, endDate: null, endDatetime: null, startDate: null, startDatetime: null, includeArchived: null, include: null, page: 1, perPage: 20, ct: default)` | None (query-only) | `IReadOnlyList<ProductResponse>` — each item wraps `Product` in a single field; extract via `response[i].Product` to read `.Handle`, `.Name`, `.PriceInCents`, `.Interval`, `.IntervalUnit`, `ProductPricePointId` | **Case B:** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | Manual `page`+`perPage` (starts page=1, perPage=20) | `map/operations/Products.md` |
| **Get Plan by Handle** (GET /api/subscription-plans/{handle}) | `client.Products.ReadProductByHandle(apiHandle: string, ct: default)` | None (path param: `apiHandle`) | `ProductResponse` — unwrap via `.Product` to access `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductPricePointId` | **Case B:** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | None | `map/operations/Products.md` |
| **Lookup Customer by Reference** (GET /customers/lookup.json) | `client.Customers.ReadCustomerByReference(reference: string, ct: default)` | None (query param: `reference`) | `CustomerResponse` — unwrap via `.Customer` to access `Id`, `FirstName`, `LastName`, `Email`, `Reference` | **Case B:** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | None | `map/operations/Customers.md` |
| **Create Customer** (POST /customers.json) | `client.Customers.CreateCustomer(body: CreateCustomerRequest?, ct: default)` — `body` nullable but **must pass explicitly** | `CreateCustomerRequest` wraps `CreateCustomer` (**required** `first_name`, `last_name`, `email`; optional `reference`, `organization`, `phone`; others optional) | `CustomerResponse` — unwrap via `.Customer` to access `Id`, `FirstName`, `LastName`, `Email`, `Reference`, `CreatedAt` | **Case A:** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]; payload type: `CustomerErrorResponse1` with `Errors: Errors?` dict | None | `map/operations/Customers.md` |
| **Create Subscription** (POST /subscriptions.json) | `client.Subscriptions.CreateSubscription(body: CreateSubscriptionRequest?, ct: default)` — `body` nullable but **must pass explicitly** | `CreateSubscriptionRequest` wraps `CreateSubscription` — use `ProductHandle` (string) or `ProductId` (int) to identify product; `CustomerId` (int) or `CustomerReference` (string) to identify customer; `PaymentCollectionMethod` optional (defaults to site setting) — **at minimum, one of `{ProductHandle, ProductId}` and one of `{CustomerId, CustomerReference}` must be set** | `SubscriptionResponse` — unwrap via `.Subscription` to access `Id`, `State` (enum `SubscriptionState`), `ProductPriceInCents`, `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `NextAssessmentAt`, `ActivatedAt`, `CanceledAt`, `Product`, `Customer` | **Case A:** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]; payload type: `ErrorListResponse1` with `Errors: IReadOnlyList<string>` | None | `map/operations/Subscriptions.md` |
| **List Customer Subscriptions** (GET /customers/{customer_id}/subscriptions.json) | `client.Customers.ListCustomerSubscriptions(customerId: int, ct: default)` | None (path param: `customerId` is Maxio-assigned customer ID, **not** reference) | `IReadOnlyList<SubscriptionResponse>` — each item wraps `Subscription`; extract via `response[i].Subscription` to read `.Id`, `.State`, `.Product`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt` | **Case B:** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | None | `map/operations/Customers.md` |
| **Read Subscription** (GET /subscriptions/{subscription_id}.json) | `client.Subscriptions.ReadSubscription(subscriptionId: int, include: IReadOnlyList<SubscriptionInclude>?, ct: default)` — `include` nullable but **must pass explicitly** (pass `null` to omit) | None (path param: `subscriptionId` is Maxio-assigned subscription ID) | `SubscriptionResponse` — unwrap via `.Subscription` to access full subscription details | **Case B:** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | None | `map/operations/Subscriptions.md` |

### Request Model Details

**`CreateCustomerRequest` → `CreateCustomer` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Customer (container) | `customer` | `CreateCustomer` | Yes (!req) | Create via object initializer; set fields via `init` |
| FirstName | `first_name` | `string` | Yes (!req) | |
| LastName | `last_name` | `string` | Yes (!req) | |
| Email | `email` | `string` | Yes (!req) | |
| Reference | `reference` | `string?` | No | Unique identifier in your system; use this to look up the customer later |
| Organization | `organization` | `string?` | No | |
| Phone | `phone` | `string?` | No | |
| Address | `address` | `string?` | No | |
| City | `city` | `string?` | No | |
| State | `state` | `string?` | No | ISO 3166-2 state code (2–3 chars) |
| Zip | `zip` | `string?` | No | |
| Country | `country` | `string?` | No | ISO 3166-1 country code (2 chars) |

**`CreateSubscriptionRequest` → `CreateSubscription` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Subscription (container) | `subscription` | `CreateSubscription` | Yes (!req) | Create via object initializer |
| ProductHandle | `product_handle` | `string?` | No (one of `{ProductHandle, ProductId}` must be set) | Plan handle from Maxio (e.g., `"eshop-pro"`) |
| ProductId | `product_id` | `int?` | No | Product ID from Maxio (alternative to handle) |
| CustomerId | `customer_id` | `int?` | No (one of `{CustomerId, CustomerReference}` must be set) | Maxio-assigned customer ID (from CreateCustomer or ReadCustomerByReference response) |
| CustomerReference | `customer_reference` | `string?` | No | Your app's customer reference; used to look up customer |
| PaymentCollectionMethod | `payment_collection_method` | `CollectionMethod?` | No | Enum: `Automatic`, `Remittance`, `Prepaid`, `Invoice`; defaults to site setting if omitted |
| CouponCode | `coupon_code` | `string?` | No | Optional coupon code to apply |
| Reference | `reference` | `string?` | No | Your app's subscription reference (for lookup) |

### Response Model Details

**`ProductResponse` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Product | `product` | `Product` | Yes (!req) | Unwrap this field to read plan details |

**`Product` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Id | `id` | `int?` | No | Maxio product ID |
| Name | `name` | `string?` | No | Display name of the plan |
| Handle | `handle` | `string?` | No | API handle (e.g., `"eshop-pro"`) |
| Description | `description` | `string?` | No | Plan description |
| PriceInCents | `price_in_cents` | `long?` | No | Price in cents (multiply by 100 to store as integer) |
| Interval | `interval` | `int?` | No | Billing cycle interval (e.g., 1 for monthly) |
| IntervalUnit | `interval_unit` | `IntervalUnit?` | No | Enum: `Day`, `Month` |
| TrialInterval | `trial_interval` | `int?` | No | Trial duration (if any) |
| TrialIntervalUnit | `trial_interval_unit` | `IntervalUnit?` | No | Trial duration unit |
| ProductPricePointId | `product_price_point_id` | `int?` | No | Price point ID (used when creating subscriptions) |

**`CustomerResponse` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Customer | `customer` | `Customer` | Yes (!req) | Unwrap this field to read customer details |

**`Customer` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Id | `id` | `int?` | No | Maxio customer ID (use for CreateSubscription) |
| FirstName | `first_name` | `string?` | No | |
| LastName | `last_name` | `string?` | No | |
| Email | `email` | `string?` | No | |
| Reference | `reference` | `string?` | No | Your app's customer reference (match on this) |
| CreatedAt | `created_at` | `DateTimeOffset?` | No | When the customer was created in Maxio |
| Organization | `organization` | `string?` | No | |

**`SubscriptionResponse` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Subscription | `subscription` | `Subscription` | Yes (!req) | Unwrap this field to read subscription details |

**`Subscription` (namespace: `MaxioAdvancedBilling.Models`)**
| Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| Id | `id` | `int?` | No | Maxio subscription ID |
| State | `state` | `SubscriptionState?` | No | Enum: `Active`, `Canceled`, `Expired`, `Paused`, `OnHold`, `PastDue`, `TrialEnded`, etc. |
| ProductPriceInCents | `product_price_in_cents` | `long?` | No | Current price in cents |
| CurrentPeriodStartsAt | `current_period_starts_at` | `DateTimeOffset?` | No | Billing period start date |
| CurrentPeriodEndsAt | `current_period_ends_at` | `DateTimeOffset?` | No | Billing period end date (next billing date) |
| NextAssessmentAt | `next_assessment_at` | `DateTimeOffset?` | No | Next renewal/billing date |
| ActivatedAt | `activated_at` | `DateTimeOffset?` | No | When the subscription became active |
| CanceledAt | `canceled_at` | `DateTimeOffset?` | No | When the subscription was canceled (null if active) |
| Product | `product` | `Product?` | No | Nested product details (handle, name, price, interval) |
| Customer | `customer` | `Customer?` | No | Nested customer details |
| CouponCode | `coupon_code` | `string?` | No | Applied coupon (if any) |

### Enum Values

**`SubscriptionState` (namespace: `MaxioAdvancedBilling.Models.Enums` — use as `SubscriptionState.Active`, etc.)**
| Value | Wire Value | Meaning |
|---|---|---|
| `Active` | `active` | Normal, active subscription |
| `Canceled` | `canceled` | Subscription has been canceled |
| `Expired` | `expired` | Subscription period has ended |
| `Trialing` | `trialing` | In trial period |
| `TrialEnded` | `trial_ended` | Trial period completed |
| `Paused` | `paused` | Subscription temporarily paused |
| `OnHold` | `on_hold` | Subscription on hold |
| `PastDue` | `past_due` | Payment past due |
| `Suspended` | `suspended` | Subscription suspended |
| `Unpaid` | `unpaid` | Subscription unpaid |

**`CollectionMethod` (namespace: `MaxioAdvancedBilling.Models.Enums` — use as `CollectionMethod.Automatic`, etc.)**
| Value | Wire Value | Meaning |
|---|---|---|
| `Automatic` | `automatic` | Automatic payment collection (default) |
| `Remittance` | `remittance` | Invoice-based, customer remits payment |
| `Prepaid` | `prepaid` | Prepaid balance |
| `Invoice` | `invoice` | Invoice-based (legacy) |

**`IntervalUnit` (namespace: `MaxioAdvancedBilling.Models.Enums` — use as `IntervalUnit.Month`, etc.)**
| Value | Wire Value |
|---|---|
| `Day` | `day` |
| `Month` | `month` |

### Error Response Payloads

**`CreateCustomerError` — Case A (namespace: `MaxioAdvancedBilling.Errors`)**
- Accessor: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]
- Payload: `CustomerErrorResponse1` with field `Errors: Errors?` (dict of error arrays by field)
- Fallback: `TryGetRawError(out RawError)` for non-422 statuses

**`CreateSubscriptionError` — Case A (namespace: `MaxioAdvancedBilling.Errors`)**
- Accessor: `TryGetErrorListResponse1(out ErrorListResponse1)` [422]
- Payload: `ErrorListResponse1` with field `Errors: IReadOnlyList<string>` (list of error messages)
- Fallback: `TryGetRawError(out RawError)` for non-422 statuses

**`ListProducts`, `ReadProductByHandle`, `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ReadSubscription` — Case B (namespace: `MaxioAdvancedBilling.Errors`)**
- Exception type: `SdkException<RawError>`
- No typed accessors; read via: `error.StatusCode`, `error.ReadAsString()`, `error.ReadAsJson<T>()`

### Client & Server Setup

| Item | Type | Value / Notes | Source |
|---|---|---|---|
| HTTP Client lifetime | `System.Net.Http.HttpClient` | Long-lived, reused via `IHttpClientFactory`; **MUST NOT rebuild per-request** | `dotnet-client-initialization` |
| Auth scheme | HTTP Basic | Username = API key (from config); Password = literal `"x"` | `map/operations/` (header) |
| Environment | `ServerEnvironment` enum | `ServerEnvironment.Us` (US-hosted; default) or `ServerEnvironment.Eu` (EU-hosted) | `map/operations/` (header) |
| Base URL template | String (Production server) | `https://{site}.chargify.com` (US) or `https://{site}.ebilling.maxio.com` (EU); `{site}` = subdomain from config | `map/operations/` (header) |
| Namespace for enums | `MaxioAdvancedBilling.Models.Enums` | Use `SubscriptionState.FromValue("active")` or static members like `SubscriptionState.Active` | `map/models/enums.md` |
| Namespace for exceptions | `MaxioAdvancedBilling.Errors` | Error types: `CreateCustomerError`, `CreateSubscriptionError`, etc. | `map/operations/` (error columns) |
| Namespace for Core exception | `MaxioAdvancedBilling.Core.Exceptions` | `SdkException<TError>` base for all thrown errors | `sdk-map.md` (error-handling model) |

---

## TRAP NOTES

⚠ **Step 1 (client registration)** — The HTTP client must be long-lived and reused; the SDK client wrapper may be transient. Do NOT construct a new `HttpClient` per request or the client will leak socket handles and the app will eventually run out of connections. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ **Step 2 (authentication)** — Maxio uses HTTP Basic auth with **username = API key** (from `Maxio:ApiKey` config) and **password = literal `"x"`**. Store the API key in secure configuration (environment variables, user secrets, Key Vault), never hardcode it. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 3 (calling operations)** — Many optional parameters in read/list operations have no C# default and MUST be passed explicitly; mis-binding a positional call will bind the wrong parameter. Use **named arguments** (e.g., `dateField: null`, `filter: null`) to avoid position errors. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (response envelopes)** — All response types wrap their payload in a single field (e.g., `ProductResponse.Product`, `SubscriptionResponse.Subscription`). Reading the envelope field one level down is not optional — it is required by the generated type shape. Forgetting to unwrap will cause a compiler error or a runtime null-reference.

⚠ **Step 5 (subscription state logic)** — The `Subscription.State` field is an enum (`SubscriptionState?`). Common states: `Active` = billing normally; `Canceled` = subscription ended; `Paused` = temporarily stopped; `PastDue` = payment failed; `Trialing` = in trial period. Use the enum members (or `FromValue(string)`) to match state, never string literals. **MUST load `dotnet-models`** before handling subscription state.

⚠ **Step 6 (idempotent customer lookup)** — Maxio customer records are looked up by the `reference` field (your app's customer ID). If the customer exists, `ReadCustomerByReference` returns it; if not, the call throws `SdkException<RawError>` with HTTP 404. To achieve idempotency: attempt `ReadCustomerByReference`; if it throws 404, call `CreateCustomer` and capture the returned `Customer.Id` for future subscription operations. **MUST load `dotnet-error-handling`** to distinguish 404 (customer not found) from other errors.

⚠ **Step 7 (subscription creation payment method)** — `PaymentCollectionMethod` can be set on the subscription or will default to the site's default. Valid values: `Automatic` (SDK collects payment on schedule), `Invoice` (customer invoiced), `Remittance` (customer sends payment), `Prepaid` (prepaid balance). If the subscription creation fails with 422, inspect `CreateSubscriptionError.TryGetErrorListResponse1(out var errors)` to read the error messages — common causes include missing payment profile, invalid product/customer reference, or plan restrictions.

⚠ **Step 8 (error boundary — JSON deserialization)** — Two unrelated failures surface as `System.Text.Json.JsonException`:
  1. **Drifted 2xx response** (missing `required` field in response JSON) throws `JsonException` during deserialization, **not** wrapped in `SdkException` — an SDK-exception-only catch ladder lets it escape; **you must map it to 500 in your boundary**.
  2. **Non-2xx response that doesn't match the operation's `{Operation}Error` shape** throws `JsonException` **while the error object is being constructed**, replacing the `SdkException` and destroying the HTTP status — if your boundary maps all `JsonException` to 500 and retries on 5xx, it will retry indefinitely on a shape mismatch. **MUST load `dotnet-error-handling`** to write a boundary that handles both cases.

⚠ **Step 9 (configuration & resilience)** — Retry options (`MaxRetries`, `Timeout`, `HttpMethodsToRetry`) do not bound a whole call; they are per-attempt, and `Timeout` is a `TimeSpan?` per individual request attempt, not total. `HttpMethodsToRetry` gates only the **status code** retry trigger (e.g., 503); transport failures (`HttpRequestException`) retry on **every** HTTP verb, including `POST`, so non-idempotent writes can execute more than once. **MUST load `dotnet-configuration-resilience`** before tuning retry/timeout settings.

---

## REQUIRED READING

Load **before implementation starts**:

| Skill | Governs | Rationale |
|---|---|---|
| `dotnet-client-initialization` | Step 1: Client & DI registration | HTTP client lifetime, `IHttpClientFactory` seam, avoiding connection leaks |
| `dotnet-authentication` | Step 2: Auth credentials | API key binding, secure config, credential rotation |
| `dotnet-calling-endpoints` | Step 3: Operation calls | Named argument discipline, required-but-nullable parameter passing, async/await |
| `dotnet-models` | Step 5: Request/response models, enums | Union construction/reading, `StringEnum<T>` factory, immutable record initialization |
| `dotnet-error-handling` | Step 7–8: Exception boundary | Case A vs Case B operations, `TryGet…` accessors, `JsonException` dual-path handling, status codes |
| `dotnet-configuration-resilience` | Step 9: Retry/timeout tuning | Per-attempt vs total timeout, HTTP verb retry rules, backoff strategy |

**These skills carry defaults, worked examples, and wiring patterns that this sheet does not repeat. Do not skip loading them — a one-line note on a retry setting is not a substitute for the full skill, and the skill covers parts (e.g. "what disables retries") a signature cannot show.**

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb user identity (e.g., `ApplicationUser.Id`) maps 1:1 to Maxio customer `reference` field for idempotent lookup.
- Subscription payment is handled outside this integration (e.g., the application has a payment profile on file or Maxio will invoice; the integration does not pass payment method details to `CreateSubscription`).
- The Maxio site is in sandbox mode (cp-exp-1) during development and production credentials will be supplied at deployment.

**Blockers:**
None. All required operations are available in the SDK map.

