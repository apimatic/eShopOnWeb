# Maxio Advanced Billing Integration — eShopOnWeb PublicApi

## Scope & Sequence

1. **Client initialization & DI setup** — construct `MaxioAdvancedBillingClient` with Basic auth; register via `AddMaxioAdvancedBillingClient`.
2. **Ensure customer exists** — call `CreateCustomer` with email as reference key; on 422, assume customer exists or retrieve by reference.
3. **Fetch available plans** — call `ListProducts` to enumerate plans (Pro and Basic from sandbox seeding).
4. **Create subscription** — call `CreateSubscription` binding customer ID + product handle.
5. **Read subscription state** — call `ReadSubscription` to confirm state, next billing date, and price.
6. **List customer subscriptions** — call `ListCustomerSubscriptions` to enumerate active subscriptions for a caller.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Controller | Method Signature | Request Model + Fields | Response Envelope + Inner Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| `Customers` (namespace: `MaxioAdvancedBilling.Api`) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | **Request wrapper:** `CreateCustomerRequest` (namespace: `MaxioAdvancedBilling.Models`) wraps `CreateCustomer !req` (namespace: `MaxioAdvancedBilling.Models`). **CreateCustomer fields:** `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`. **Wire binding:** Set `Reference` field to eShopOnWeb user ID (as string) or email for idempotent lookups. | **Response wrapper:** `SubscriptionResponse` (namespace: `MaxioAdvancedBilling.Models`) exposes exactly one field: `Customer (customer): Customer !req`. **Customer fields you'll read:** `Id (id): int?` (Maxio ID — store this to link subscriptions), `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`. | **Case A (typed):** `SdkException<CreateCustomerError>` (namespace: `MaxioAdvancedBilling.Errors`). **Accessor:** `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [HTTP 422]. Payload type: `CustomerErrorResponse1` (namespace: `MaxioAdvancedBilling.Models`) with `Errors (errors): Errors?` field. **Fallback:** `TryGetRawError(out RawError)` for non-422 statuses. | none | `operations/Customers.md` |
| `Products` (namespace: `MaxioAdvancedBilling.Api`) | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | **Optional query params (pass `null` to skip):** `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include`. **Wire bindings:** `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `page` ← `page`, `per_page` ← `perPage`, `include_archived` ← `includeArchived`, `include` ← `include`. **Defaults:** `page` = 1, `perPage` = 20. **Notes:** For hero flow, call with all optional params as `null` to fetch all products. Sandbox has plans with handles `eshop-pro` and `basic-plan`. | **Returns:** `IReadOnlyList<ProductResponse>` (namespace: `MaxioAdvancedBilling.Models`). **Each ProductResponse element** exposes exactly one field: `Product (product): Product !req`. **Product fields you'll read:** `Id (id): int?`, `Handle (handle): string?` (e.g., `eshop-pro`), `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?` (e.g., 29900 = $299/mo), `Interval (interval): int?` (billing interval count), `IntervalUnit (interval_unit): IntervalUnit?` (enum: `Month` / `Day`). | **Case B (raw):** `SdkException<RawError>` (namespace: `MaxioAdvancedBilling.Core.ErrorResponse`). **Members:** `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. | manual `page`+`perPage` | `operations/Products.md` |
| `Subscriptions` (namespace: `MaxioAdvancedBilling.Api`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | **Request wrapper:** `CreateSubscriptionRequest` (namespace: `MaxioAdvancedBilling.Models`) wraps `CreateSubscription !req` (namespace: `MaxioAdvancedBilling.Models`). **CreateSubscription fields (in scope):** `CustomerId (customer_id): int?`, `ProductHandle (product_handle): string?` (e.g., `eshop-pro`; alternative to `ProductId`), `ProductId (product_id): int?` (alternative to `ProductHandle`), `NextBillingAt (next_billing_at): DateTimeOffset?` (optional override for first billing date). **Notes:** Caller must supply **either** `CustomerId` OR `CustomerAttributes` (nested object with `FirstName`, `LastName`, `Email` to create customer inline). For hero flow: pass `CustomerId` (from ensure-customer step) and `ProductHandle`. No trial, no setup fee, no payment method required in this sandbox config. | **Response wrapper:** `SubscriptionResponse` (namespace: `MaxioAdvancedBilling.Models`) exposes exactly one field: `Subscription (subscription): Subscription?`. **Subscription fields you'll read:** `Id (id): int?` (Maxio subscription ID), `State (state): SubscriptionState?` (enum, e.g., `Active`, `Trialing`), `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date), `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`. | **Case A (typed):** `SdkException<CreateSubscriptionError>` (namespace: `MaxioAdvancedBilling.Errors`). **Accessor:** `TryGetErrorListResponse1(out ErrorListResponse1)` [HTTP 422]. Payload type: `ErrorListResponse1` (namespace: `MaxioAdvancedBilling.Models`) with `Errors (errors): IReadOnlyList<string> !req` field. **Fallback:** `TryGetRawError(out RawError)` for non-422 statuses. | none | `operations/Subscriptions.md` |
| `Subscriptions` (namespace: `MaxioAdvancedBilling.Api`) | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | **Required param:** `subscriptionId: int` (Maxio subscription ID from create response). **Optional param:** `include: IReadOnlyList<SubscriptionInclude>?` (namespace: `MaxioAdvancedBilling.Models.Enums`; pass `null` to fetch core fields only; enums: `Coupons`, `SelfServicePageToken`). **Wire binding:** `include` ← `include`. | **Response wrapper:** `SubscriptionResponse` (namespace: `MaxioAdvancedBilling.Models`) exposes exactly one field: `Subscription (subscription): Subscription?`. **Same fields as CreateSubscription response.** Typical read confirms state and next billing date. | **Case B (raw):** `SdkException<RawError>` (namespace: `MaxioAdvancedBilling.Core.ErrorResponse`). **Members:** `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. | none | `operations/Subscriptions.md` |
| `Customers` (namespace: `MaxioAdvancedBilling.Api`) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | **Required param:** `customerId: int` (Maxio customer ID from ensure-customer step). | **Returns:** `IReadOnlyList<SubscriptionResponse>` (namespace: `MaxioAdvancedBilling.Models`). **Each SubscriptionResponse element** exposes exactly one field: `Subscription (subscription): Subscription?`. **Read same fields as ReadSubscription.** Caller typically filters by `State` (enum value `Active`) to show active subscriptions only. | **Case B (raw):** `SdkException<RawError>` (namespace: `MaxioAdvancedBilling.Core.ErrorResponse`). **Members:** `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. | none (returns flat list) | `operations/Customers.md` |

### Enums (in scope)

| Enum | Namespace | Values | Usage |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending (pending)`, `Trialing (trialing)`, `Active (active)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`, `TrialEnded (trial_ended)`, `Unpaid (unpaid)`, `Assessing (assessing)`, `SoftFailure (soft_failure)`, `FailedToCreate (failed_to_create)`, `Paused (paused)` | Read from `Subscription.State` field after create or read operations. Filter to `Active` when listing customer subscriptions. |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` | Read from `Product.IntervalUnit` to confirm billing frequency (expect `Month` for all sandbox plans). |

### Client Construction & Auth

| Setting | Type | Value / How to Supply | Namespace | Notes |
|---|---|---|---|---|
| **API Key** | `string` | Load from config binding `Maxio:ApiKey` (env var or secrets) | — | HTTP Basic auth username; do **not** hardcode. |
| **Site Subdomain** | `string` | Load from config binding `Maxio:Subdomain` (default: `cp-exp-2` for sandbox) | — | Injected into base URL template `https://{subdomain}.chargify.com`. |
| **Auth Scheme** | `BasicAuthCredentials` | `new BasicAuthCredentials { Username = apiKey, Password = "x" }` | `MaxioAdvancedBilling.Core.Authentication.Basic` | Password is **literal string `"x"`** — not a placeholder. |
| **Environment** | `ServerEnvironment` | `ServerEnvironment.Us` (default) | `MaxioAdvancedBilling.Servers` | Selects hosting region; US endpoints used for sandbox. |
| **HttpClient** | `System.Net.Http.HttpClient` | Injected via `IHttpClientFactory` in DI; **must be long-lived, reused** | — | Do **not** instantiate per-request; register as named or default. |

---

## Trap Notes

⚠ **Step 1 (client registration)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 (authentication)** — API key is username; password is **literal `"x"`** (not interpolated). Set credentials **before** constructing the client or in the DI callback. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 4 (creating subscriptions)** — if you pass `ProductHandle`, the SDK looks it up by handle (e.g., `eshop-pro`); if you pass `ProductId`, it looks up by numeric ID. You **cannot** pass both. **MUST load `dotnet-calling-endpoints`** to confirm named-argument binding on this operation's many optional fields.

⚠ **Step 4 (subscription creation idempotency)** — Maxio has **no built-in idempotency key**. If you need to ensure "one subscription per user per plan", the application must check before calling `CreateSubscription` (query `ListCustomerSubscriptions`, filter by product, check if exists). **MUST load `dotnet-error-handling`** to distinguish a 422 "customer invalid" from a 422 "subscription already exists" (Maxio returns the same status for both, so you must parse the error message).

⚠ **Error boundary — TWO JsonException directions** — Both rows below are MANDATORY in your error handler:
  1. A drifted or malformed **2xx** body (e.g., `Subscription.State` field missing from response) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary unhandled.
  2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` (HTTP status is lost) — a boundary that maps every `JsonException` to a 500 then retries 5xx retries something that can never succeed.
  **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ **Step 5 (response envelope unwrapping)** — `SubscriptionResponse`, `ProductResponse`, and `CustomerResponse` are **response wrappers** (the SDK's standard pattern). Do **not** cast the response as-is to `Subscription` / `Product` / `Customer`. Unwrap via the single required field: `response.Subscription`, `response.Product`, `response.Customer` respectively. **MUST load `dotnet-models`** to understand union and record field access.

⚠ **Step 6 (listing subscriptions)** — `ListCustomerSubscriptions` returns `IReadOnlyList<SubscriptionResponse>`, **not** `IReadOnlyList<Subscription>`. Each element is a wrapper; unwrap each via `.Subscription` field before filtering or reading state. **MUST load `dotnet-calling-endpoints`** before the first list/iteration call to confirm iteration semantics.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; these are the one place to find defaults, worked examples, and gotchas that bind options and calls together.

| Skill | Step | Reason |
|---|---|---|
| `dotnet-client-initialization` | 1 (client & DI setup) | HttpClient lifetime, transient vs. singleton SDK client, DI patterns for options. |
| `dotnet-authentication` | 2 (auth setup) | How to load credentials, set them on options, rotate them safely; Basic auth username/password mapping. |
| `dotnet-calling-endpoints` | 4 (subscribe call) | Named-argument binding on multi-param operations; sync vs. async patterns; how to pass optional params as `null`. |
| `dotnet-models` | 3 & 5 (products, responses) | Record field access; unwrapping response envelopes; enum construction (`StringEnum<T>` **not** C# enums); union factories and `TryGet…`. |
| `dotnet-error-handling` | 2, 4, 5 (all error paths) | Case A vs. Case B operations (which throw typed errors, which throw raw); `SdkException<T>` pattern; `TryGet…` accessors; JsonException handling in the boundary. **This is critical:** `JsonException` from deserialization failure and `SdkException` wrap different root causes. |
| `dotnet-configuration-resilience` | 1 (client setup) | Retry semantics (status codes, transport failures, per-attempt vs. total timeout); what HttpMethodsToRetry actually gates; defaults for non-idempotent writes. |

---

## Assumptions & Blockers

- **Assumption:** Sandbox seeding (product family `eshop-subscribe`, plans `eshop-pro` and `basic-plan`) is stable and idempotent. If IDs change between environments, the application configuration must parameterize them (e.g., `Maxio:ProPlanHandle`, `Maxio:BasicPlanHandle` from config).
- **Assumption:** No custom components or metered billing will be used in the hero flow (plans are simple recurring subscriptions). If component usage is later required, **additional integration work is needed:** `CreateSubscriptionComponent` wire-up, component ID/handle binding, and usage reporting.
- **Assumption:** Customers in eShopOnWeb have a persistent identity (user ID, email) that can be safely passed as the `reference` field in Maxio (idempotent lookup key). If the application later changes identity strategy, the reference key must be updated in Maxio records.
- **No blocker:** all five operations (create customer, list products, create subscription, read subscription, list customer subscriptions) are available in the SDK and sandbox environment per seeding notes.
