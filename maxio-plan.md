# maxio-plan.md — Recurring subscriptions for eShopOnWeb via Maxio Advanced Billing (.NET SDK)

SDK: NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · netstandard2.0 · map pinned to source `v1.0.2` (`15db14b`).

---

## 1. Scope & sequence

Additive, parallel capability on `src/PublicApi` (JWT-authenticated). Does not touch the existing cart/checkout flow.

| # | Step | Operations used |
|---|---|---|
| 1 | Bind options from the `Maxio:` config section — `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional verbatim base-address override). No hard-coded values. | — |
| 2 | Register the SDK client + auth in DI (PublicApi). | — |
| 3 | Catalog service: resolve product-family **id** from the configured **handle** (cached for the app lifetime is a `YOUR CALL`), list its products → `GET /api/subscription-plans`. | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` (fallback: `Products.ReadProductByHandle`) |
| 4 | Customer-ensure service: idempotent Maxio customer per eShopOnWeb user (lookup by `reference`, create on miss, re-lookup on 422). | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` (search fallback: `Customers.ListCustomers`) |
| 5 | Subscription service: pre-check by subscription `reference`, then create on `product_handle` + `customer_id`; return plan/price/state/next-billing-date. | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions`: customer by reference → list that customer's subscriptions → filter state client-side. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Error boundary on all three endpoints (exception translation + the two `JsonException` hazards in §3). | — |
| 8 | Tests for the services/endpoints. | — |

Not in map scope (application concerns, not set here): JWT claim extraction, endpoint style (minimal API vs MVC), persistence/caching, concurrency locks, DTO shapes returned to callers.

---

## 2. CONTRACT SHEET

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

### 2.0 Client construction, auth, base URL, DI lifetime

| Fact | Value | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **only** constructor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`; every API group is a property (`client.Customers`, `client.Subscriptions`, `client.Products`, `client.ProductFamilies`) | sdk-map.md, `MaxioAdvancedBillingClient.cs` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | sdk-map.md, `MaxioAdvancedBillingClientOptions.cs` |
| Auth (HTTP Basic) | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` — **username = API key, password = the literal string `"x"`** | sdk-map.md, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) or `.Eu` — **hosting region only. There is no sandbox/test enum in the SDK**; "sandbox" is an ordinary site (e.g. `cp-exp-1`) whose site-level test-mode setting lives in Maxio, not in client options | sdk-map.md (`Servers & auth`) |
| Base URL derivation (US) | Production group template `https://{site}.chargify.com`; `{site}` defaults to the **subdomain** — set `options.Server.Production.Us.Site = "<Maxio:Subdomain>"`. EU hosting (only if the account requested it) uses `https://{site}.ebilling.maxio.com`. The Ebb server group (`events.chargify.com`) is used only by `SubscriptionComponents` event-ingest endpoints — irrelevant here | sdk-map.md (`Servers & auth`) |
| `Maxio:BaseUrl` override | **Verbatim override** of the derived base address: `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` (e.g. to pin `https://cp-exp-1.chargify.com` or a mock host). Set `Site` from `Maxio:Subdomain` unconditionally; set `BaseUrl` only when `Maxio:BaseUrl` is configured | sdk-map.md (`Servers & auth`) |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — **every member is `required`**; build a full instance or start from `RetryOptions.Default()`. Do not pass a bare `new RetryOptions{}` | sdk-map.md (`RetryOptions` members) |
| DI registration | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — source-verified: it calls `AddHttpClient()`, builds the options once via the configure delegate, and registers `MaxioAdvancedBillingClient` as a **singleton** whose `HttpClient` comes from `IHttpClientFactory.CreateClient()`. Register via this extension (or replicate its shape); do **not** construct the client per request and do **not** register it scoped | SDK source `ServiceCollectionExtensions.cs` |
| Using directives needed | `MaxioAdvancedBilling` (client/options/DI) · `MaxioAdvancedBilling.Core.Authentication.Basic` · `MaxioAdvancedBilling.Servers` · `MaxioAdvancedBilling.Core.Configuration` (RetryOptions) · `MaxioAdvancedBilling.Core.Exceptions` (`SdkException<T>`) · `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`) · `MaxioAdvancedBilling.Models` · `MaxioAdvancedBilling.Models.Enums` · `MaxioAdvancedBilling.Errors`. C# does **not** import child namespaces transitively | sdk-map.md (`Namespaces`) |

### 2.1 Product family lookup by handle

There is **no by-handle read operation that compiles for a string**: `ReadProductFamily(int id, ct)` takes an `int` even though its Notes mention a `handle:my-family` URL format — the generated signature cannot carry it. Resolve the handle by listing all families and matching.

| Operation | Signature | Returns / envelope | Error | Pagination | Source |
|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — the 5 nullable params have no default → **must pass explicitly** (pass `null` to skip) | `IReadOnlyList<ProductFamilyResponse>`; envelope `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` — read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<RawError>` | none | operations/ProductFamilies.md; models `records-3-Of-Su.md` |
| Match rule | Select the element where `ProductFamily?.Handle` == `Maxio:ProductFamilyHandle`; take `ProductFamily.Id.Value` (int) for the products call. Numeric family id `3023074` is **never** hard-coded — it is read from this response | — | — | — | YOUR CALL — not in the map (matching logic) |

### 2.2 Products listing for a family (→ `GET /api/subscription-plans`)

| Operation | Signature | Returns / envelope | Error | Pagination | Source |
|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` is a **string** (pass the numeric family id as `family.Id.Value.ToString()`); the 8 nullable params have no default → pass explicitly | `IReadOnlyList<ProductResponse>`; each envelope: `ProductResponse.Product (product): Product` (required) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | **manual** `page` + `perPage` (defaults 1/20); no total/count is returned — stop when a page returns fewer than `per_page` items | operations/ProductFamilies.md |
| Fields read on `Product` | `Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` · `Description (description): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` (day/month) · `TrialInterval (trial_interval): int?` · `TrialPriceInCents (trial_price_in_cents): long?` · `RequireCreditCard (require_credit_card): bool?` · `Taxable (taxable): bool?` · `ExpirationIntervalUnit (expiration_interval_unit): ExpirationIntervalUnit?` (day/month/never) · `ProductFamily (product_family): ProductFamily?` (nested id/handle — use to verify the plan belongs to the configured family) | — | — | — | models `records-3-Of-Su.md` |
| Alternate direct fetch | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse` (envelope `Product`, required) — **Case B** `SdkException<RawError>`. Use to validate the plan handle a caller sends to `POST /api/subscriptions` | — | — | none | operations/Products.md |

### 2.3 Customer ensure (idempotent)

`CreateCustomer` Notes: *"you may only create one customer for a given reference value. If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app."* → **`reference` is the idempotency key: set it to the eShopOnWeb user's id (string).**

| Operation | Signature | Returns / envelope | Error | Source |
|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` | `CustomerResponse`; envelope `CustomerResponse.Customer (customer): Customer` (required) — read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName`, `LastName` | **Case B** `SdkException<RawError>` — a miss is `StatusCode == HttpStatusCode.NotFound`; do not parse the 404 body's shape (best-effort string only, `UNVERIFIED`) | operations/Customers.md |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CustomerResponse` (envelope as above) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | operations/Customers.md |
| Request model | `CreateCustomerRequest { Customer (customer): CreateCustomer — required }` (`MaxioAdvancedBilling.Models`). Inner `CreateCustomer` fields: `FirstName (first_name): string` **req** · `LastName (last_name): string` **req** · `Email (email): string` **req** · `Reference (reference): string?` · `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `CcEmails`, `VatNumber`, `TaxExempt: bool?`, `TaxExemptReason`, `ParentId: int?`, `SalesforceId` — all optional | — | — | models `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 422 payload drift (source-verified) | `MaxioAdvancedBilling.Errors.CustomerErrorResponse1 { Errors (errors): MaxioAdvancedBilling.Models.Errors? }` and `Models.Errors` carries **only** `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` — it does **not** model the field-level messages (e.g. duplicate `reference`) the wire sends on this 422. **Directive:** treat `TryGetCustomerErrorResponse1 == true` as "a 422 occurred", extract best-effort, fall back to `TryGetRawError(out raw)` + `raw.ReadAsString()` for the message. On any 422 during ensure, **re-run `ReadCustomerByReference(reference)`** and use the returned customer — that path is the idempotency guarantee, not the error payload (`UNVERIFIED` exact wire body) | — | — | SDK source `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs` |
| Search fallback (optional) | `client.Customers.ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — `q` searches email / AB id / organization / reference / name; **Case B**; manual pagination (defaults 1/50). Prefer the exact-match `ReadCustomerByReference` for the ensure flow | — | — | operations/Customers.md |

### 2.4 Subscription create (→ `POST /api/subscriptions`)

| Fact | Value | Source |
|---|---|---|
| Operation | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` → `SubscriptionResponse`; **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] where `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` (required) is a flat list of human-readable messages · `TryGetRawError(out RawError)` [fallback] | operations/Subscriptions.md; models `records-2-Cr-Ne.md` |
| Request model | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription — required }` (`MaxioAdvancedBilling.Models`) | models `records-2-Cr-Ne.md` |
| **product_id vs handle** | Inner `CreateSubscription` accepts **both**: `ProductHandle (product_handle): string?` and `ProductId (product_id): int?`. **Use `product_handle`** (e.g. `eshop-pro`) — no numeric product id needed. Same for the customer: `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`. Price point override via `ProductPricePointHandle`/`ProductPricePointId` — not needed (default price point) | models `records-2-Cr-Ne.md` (full field list read from source `Models/CreateSubscription.cs`) |
| Fields to set | `ProductHandle = plan handle` · `CustomerId = ensured customer's `Id.Value`` · `Reference = your idempotency key` (recommended: derived from user id + plan handle; note **Maxio documents uniqueness enforcement only for customer `reference`** — subscription `reference` uniqueness is not documented in the map → `UNVERIFIED`; keep the pre-check below regardless) · optionally `PaymentCollectionMethod: CollectionMethod?` (leave unset to use the product default) | models `records-2-Cr-Ne.md` |
| Fields deliberately left out | `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `Components`, `CustomPrice`, `CouponCode(s)`, `NextBillingAt`, `InitialBillingAt`, `DeferSignup`, `CalendarBilling`, `Metafields`, `Currency`, `ExpiresAt`, and the remaining `CreateSubscription` optionals — none are named by the operation Notes for a plain paid signup | models `records-2-Cr-Ne.md` |
| **Payment-method-not-required** | There is **no "skip payment" flag** on `CreateSubscription` — do not invent one. Whether payment info is required is a property of the **product** (`Product.RequireCreditCard`, `Product.RequestCreditCard` — the seeded plans have payment not required). Omit all payment fields; if the product did require a card the create returns a **422** whose `ErrorListResponse1.Errors` carries the messages | operations/Subscriptions.md Notes; models `records-3-Of-Su.md` |
| Response envelope | `SubscriptionResponse { Subscription (subscription): Subscription? }` — **nullable**; null-check before reading. Read: `Id (id): int?` · `State (state): SubscriptionState?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (**this is the next-billing date**) · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `ProductPriceInCents (product_price_in_cents): long?` · `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` · `Product (product): Product?` (`Handle`, `Name`, `PriceInCents`) · `Customer (customer): Customer?` · `ActivatedAt`, `CreatedAt`. **There is no `current_price_in_cents` and no `next_billing_at` member on `Subscription`** | models `records-4-Su-We.md` (full field list read from source `Models/Subscription.cs`) |
| Pre-check (double-click) | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — query `reference`; → `SubscriptionResponse`; **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)`. Sequence: find by reference → found: return it; 404: create. Maxio does not document subscription-reference uniqueness, so also keep an application-side guard around the create | operations/Subscriptions.md |

### 2.5 Subscriptions listing for a customer (→ `GET /api/my-subscriptions`)

| Operation | Signature | Returns / envelope | Error | Pagination / filtering | Source |
|---|---|---|---|---|---|
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` (envelope `Subscription?`, nullable) | **Case B** `SdkException<RawError>` | **No pagination, no state filter parameter.** Filter client-side on `Subscription.State` (e.g. keep `SubscriptionState.Active` for "active") | operations/Customers.md |
| Site-wide alternative | `client.Subscriptions.ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 must-pass-explicitly params, manual pagination — **has no customer filter**, so it is not suitable for per-customer listing; noted only so the state-filter vocabulary is visible | `IReadOnlyList<SubscriptionResponse>` | **Case B** | manual `page`/`perPage` | operations/Subscriptions.md |

### 2.6 Enum value tables (only those in scope)

| Enum (`MaxioAdvancedBilling.Models.Enums`) | Kind | Values (C# member (wire value)) |
|---|---|---|
| `SubscriptionState` | StringEnum | `Pending (pending)` · `FailedToCreate (failed_to_create)` · `Trialing (trialing)` · `Assessing (assessing)` · `Active (active)` · `SoftFailure (soft_failure)` · `PastDue (past_due)` · `Suspended (suspended)` · `Canceled (canceled)` · `Expired (expired)` · `Paused (paused)` · `Unpaid (unpaid)` · `TrialEnded (trial_ended)` · `OnHold (on_hold)` · `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` (only if site-wide listing is ever used) | StringEnum | `Active (active)` · `Canceled (canceled)` · `Expired (expired)` · `ExpiredCards (expired_cards)` · `OnHold (on_hold)` · `PastDue (past_due)` · `PendingCancellation (pending_cancellation)` · `PendingRenewal (pending_renewal)` · `Suspended (suspended)` · `TrialEnded (trial_ended)` · `Trialing (trialing)` · `Unpaid (unpaid)` |
| `CollectionMethod` | StringEnum | `Automatic (automatic)` · `Remittance (remittance)` · `Prepaid (prepaid)` · `Invoice (invoice)` |
| `IntervalUnit` | StringEnum | `Day (day)` · `Month (month)` |
| `ExpirationIntervalUnit` | StringEnum | `Day (day)` · `Month (month)` · `Never (never)` |
| `SortingDirection` | StringEnum | `Asc (asc)` · `Desc (desc)` |
| `BasicDateField` | StringEnum | `UpdatedAt (updated_at)` · `CreatedAt (created_at)` |

Enums are **not** C# enums — they are `StringEnum<T>`: read the typed member (e.g. `sub.State` is already `SubscriptionState?`); build one with `SubscriptionState.FromValue("active")` or its static members when needed.

### 2.7 Error handling (per operation)

Exception type reaching `catch`: `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` with `.Error: TError`. Typed errors live in `MaxioAdvancedBilling.Errors`; `MaxioAdvancedBilling.Core.ErrorResponse.RawError` exposes `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()`. Every operation is **throw-only** — this SDK generates no `…Result` no-throw variants.

| Operation | Case | `catch` type | 4xx reading |
|---|---|---|---|
| `ListProductFamilies` | B | `SdkException<RawError>` | `ex.Error.StatusCode` + `ReadAsString()` |
| `ListProductsForProductFamily` | A | `SdkException<ListProductsForProductFamilyError>` | 404 → `TryGetString(out string)`; else `TryGetRawError` |
| `ReadProductByHandle` | B | `SdkException<RawError>` | `StatusCode` + body string (404 = unknown handle) |
| `ReadCustomerByReference` | B | `SdkException<RawError>` | 404 = no customer for that reference (expected on first ensure) |
| `CreateCustomer` | A | `SdkException<CreateCustomerError>` | 422 → `TryGetCustomerErrorResponse1` (payload drift — see §2.3) → best-effort, fall back to `TryGetRawError` |
| `CreateSubscription` | A | `SdkException<CreateSubscriptionError>` | 422 → `TryGetErrorListResponse1` → `ErrorListResponse1.Errors: IReadOnlyList<string>` (user-displayable messages) |
| `FindSubscription` | A | `SdkException<FindSubscriptionError>` | 404 → `TryGetNoContent(out RawError)` (expected on first subscribe) |
| `ListCustomerSubscriptions` | B | `SdkException<RawError>` | `StatusCode` + body string |

Sources: operations/Subscriptions.md, operations/Customers.md, operations/ProductFamilies.md, operations/Products.md; sdk-map.md (`Error-handling model`).

### 2.8 Sandbox / base-URL facts for the override logic

- US base-URL template: `https://{site}.chargify.com`; `{site}` defaults to the subdomain → for the seeded site `cp-exp-1` the derived address is `https://cp-exp-1.chargify.com`. (Source: sdk-map.md `Servers & auth`.)
- `Maxio:BaseUrl`, when configured, is assigned **verbatim** to `options.Server.Production.Us.BaseUrl`; the `{site}` template no longer applies for that group. Validate an override by expecting the configured string to be the complete API host (scheme + host, no path/template).
- `ServerEnvironment` selects US vs EU **hosting only** (`Us` → `chargify.com`, `Eu` → `ebilling.maxio.com`); it is not a sandbox/test toggle. Sandbox behaviour (test mode) is a Maxio site setting outside the SDK. (Source: sdk-map.md `Servers & auth` — no other environment values exist.)

---

## 3. Trap notes (attached to the step where each bites)

⚠ Step 2 (client registration) — the SDK's own DI extension registers the client as a **singleton** over an `IHttpClientFactory`-created `HttpClient`; the handler pipeline must be long-lived and pooled, and a client constructed with `new HttpClient(...)` per request defeats that. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ Step 2 (auth) — Maxio is HTTP Basic where the **username is the API key and the password is the literal `"x"`**; credentials must be set before/at client construction and loaded from configuration, and the per-environment story (sandbox vs production key) has traps of its own. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ Step 3 (first `client.X.Y(...)` call) — many list/search operations take nullable parameters with **no C# default** that mis-bind in positional calls; call operations with **named arguments** and pass `null` explicitly for skipped filters. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ Step 3–6 (models) — records have `init`-only setters and `required` members enforced in the object initializer; enums are `StringEnum<T>` built via `FromValue`/static members, not C# enums; unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** the moment a field isn't a plain string/number.

⚠ Step 5 (subscription create) — whether a failed create can safely be re-sent depends on the SDK's retry semantics, and a transport-level failure can re-execute the POST; check what `MaxRetries`/`Timeout` actually bound before tuning anything, and what the pre-check sequence protects against. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or relying on the pre-check.

⚠ Step 7 (error boundary) — the two-case `SdkException<T>` ladder, `TryGet…` accessors, and the status loss described below must be shaped before the endpoints go live. **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ Step 7 (both directions of `System.Text.Json.JsonException`):
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

⚠ Step 8 (tests) — the `HttpClient` constructor argument is the test seam; stub there rather than faking SDK types, and match the project's existing test framework/assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **before implementation starts**. The sheet deliberately does not carry these skills' contents (defaults, worked examples, wiring steps) — each is loaded at its step.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — DI registration, options shape, HttpClient ownership/lifetime |
| `dotnet-authentication` | Step 2 — Basic credentials (`username = API key`, `password = "x"`), per-environment config |
| `dotnet-calling-endpoints` | Steps 3–6 — named arguments, must-pass-explicitly params, envelopes, async/cancellation |
| `dotnet-models` | Steps 3–6 — request construction, required members, `StringEnum<T>`, wire names |
| `dotnet-error-handling` | Step 7 — Case A/B catch ladder, `TryGet…` accessors, the two `JsonException` hazards |
| `dotnet-configuration-resilience` | Steps 2, 5 — retries/backoff, what `Timeout` bounds, pagination loops |
| `dotnet-testing` | Step 8 — the `HttpClient` seam, covering error paths |

---

## 5. Assumptions & Blockers

**Assumptions (application-side intent, not SDK contract):**
1. *Caller identity* — the JWT carries an identifier the app resolves to a stable user id; that id (stringified) is used as the Maxio customer `reference` and as the seed of the subscription `reference`. The exact claim is `YOUR CALL — not in the map`.
2. *POST body of `POST /api/subscriptions`* — assumed to carry a plan handle (default `eshop-pro` when omitted); validation = `ReadProductByHandle` + confirm the product's nested `ProductFamily.Handle` matches `Maxio:ProductFamilyHandle`. `YOUR CALL — not in the map`.
3. *Metered component `api-call`* — seeded but consumed by no route in this scope; it can be attached later via `CreateSubscription.Components` / the `SubscriptionComponents` controller. Out of scope here.
4. *Plan pricing shown to shoppers* — taken live from `Product.PriceInCents`/`Interval`/`IntervalUnit`, never hard-coded; the sandbox handles (`eshop-pro`, `basic-plan`) are the only stable identifiers relied upon.
5. *Endpoint style on `src/PublicApi`* — follows that project's existing conventions (minimal API vs MVC controllers); the sheet fixes routes and SDK calls, not the style. `YOUR CALL — not in the map`.

**UNVERIFIED (only live traffic could confirm; defensive handling is already directed):**
1. Exact 422 body of `CreateCustomer` — the generated `CustomerErrorResponse1.Errors` does not model field-level messages (§2.3); the directed fallback (re-lookup by reference, raw-body best-effort) does not depend on it.
2. Uniqueness of subscription `reference` (the map documents uniqueness only for customer `reference`) — the `FindSubscription` pre-check plus an application-side guard cover either answer.
3. The 404 body shape of Case-B lookup endpoints (`ReadCustomerByReference`, `ReadProductByHandle`) — status code alone is the decision input; bodies are read best-effort only.

**Blockers:** none. Every operation the hero flow needs exists in the map; no capability was missing and no invented data path was required.
