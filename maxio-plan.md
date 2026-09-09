# maxio-plan.md — Maxio Advanced Billing recurring-subscription billing for eShopOnWeb `src/PublicApi`

SDK: NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · client `MaxioAdvancedBillingClient` · target `netstandard2.0` (runs fine on .NET 8 / rolled-forward runtimes — **no compat concern**). Auth: HTTP **Basic — `Username` = API key, `Password` = literal `"x"`**. Environments: `ServerEnvironment.Us` (default, `https://{site}.chargify.com`) / `ServerEnvironment.Eu`; the sandbox site `cp-exp-6` is a **US** site — there is no separate "sandbox" enum, it is US + your site subdomain.

---

## 1. Scope & sequence

1. **Config + client registration** — bind `Maxio:ApiKey`, `Maxio:Subdomain`, optional `Maxio:BaseUrl`; register the SDK client in DI. *(No SDK calls.)*
2. **`GET /api/subscription-plans`** — `ProductFamilies.ListProductsForProductFamily("handle:eshop-subscribe", …)` → map `ProductResponse` list to plan DTOs (name, handle, price, interval).
3. **`POST /api/subscriptions`** — ensure Maxio customer for the eShop user (idempotent, see below) → `Subscriptions.CreateSubscription` with `product_handle` + `customer_id` + `reference`, **no payment profile** → return subscription state.
4. **`GET /api/my-subscriptions`** — `Customers.ListCustomerSubscriptions(customerId)` for the current user's Maxio customer → list DTOs. Optional detail: `Subscriptions.ReadSubscription(id, null)`.
5. **Error boundary** — one translation layer for all SDK calls (see §2 error column and §3 trap notes).

### Idempotent customer creation — recommended pattern: **create-with-reference + lookup-by-reference**

- **Lookup**: `Customers.ReadCustomerByReference(reference)` — exact single-match endpoint (`GET /customers/lookup.json?reference=…`). Prefer it over `ListCustomers(q: email)`: `q` is a multi-match search across email/id/organization/reference/name and paginated; the lookup endpoint returns exactly one and is stable when the user's email changes.
- **Reference value**: the eShop user's stable id, e.g. `"eshop-user-{id}"` (the reference value is your app's identifier — provider Docs say so verbatim in `CreateCustomer` Notes).
- **Create**: `Customers.CreateCustomer` with `Reference` set. Provider-side guard (from `CreateCustomer` Notes): *"you may only create one customer for a given reference value"* — so a double-click race cannot produce two customers; the second create gets a **422**, and the handler should re-lookup by reference and continue (this is also why create-then-lookup-on-422 is safe to use as the primary path).
- **Subscription idempotency**: `CreateSubscription` accepts a `Reference` field and `Subscriptions.FindSubscription(reference)` looks a subscription up by it. Pattern: generate a deterministic reference per (user, plan) attempt, `FindSubscription(reference)` first, create on 404. **UNVERIFIED**: no operation's Notes states the provider enforces uniqueness of subscription *reference* (unlike customer reference, which is explicitly documented unique) — treat Find-before-create as best-effort; the hard double-click guarantee belongs to the app's design (YOUR CALL — not in the map).

### Subscription creation WITHOUT a payment method

`CreateSubscription` marks **no field required** — the wire contract's acceptance rules come from the operation Notes, which say: identify the product with `product_id` **or** `product_handle`, the price point with `product_price_point_handle`/`product_price_point_id`, and the customer with `customer_id`/`customer_reference`; *"Payment information may be required to create a subscription, depending on the options for the Product being subscribed"* — the seeded plans are configured with payment method not required, so sending **only** product + customer fields (no `payment_profile_id`, no card attributes) is the documented no-payment-method signup. Fields carried in the sheet below are the ones the Notes name; all other optional fields on `CreateSubscription` are deliberately left out (no compiler catches a dropped field, and none is needed here).

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

### 2.1 Client construction & configuration

| Fact | Detail | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **only** ctor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| API groups | `client.Customers`, `client.Subscriptions`, `client.Products`, `client.ProductFamilies` (namespace `MaxioAdvancedBilling.Api`) | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions { Environment: MaxioAdvancedBilling.Servers.ServerEnvironment; Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions; Server: MaxioAdvancedBilling.Servers.ServerOptions; BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials? }` | `sdk-map.md` |
| Credentials | `options.BasicAuth = new BasicAuthCredentials { Username = "<Maxio:ApiKey>", Password = "x" }` — password is the literal string `"x"` | `sdk-map.md`, `dotnet-authentication` |
| Subdomain | `options.Server.Production.Us.Site = "<Maxio:Subdomain>"` (→ `https://{site}.chargify.com`) | `sdk-map.md` |
| Base-URL override | `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` (dev/mock host redirect) | `sdk-map.md` |
| Environment | `options.Environment = ServerEnvironment.Us` (default) | `sdk-map.md` |
| Retry options | all `RetryOptions` members are `required` — build a full instance or start from `RetryOptions.Default()` | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` exists (`ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| Thread-safety / lifetime | hazard — not resolvable from a signature | see trap note ⚠ Step 1 |

### 2.2 Operations

All operation methods are `async Task<T>` on the controller; every operation **throws** — there are no no-throw variants in this SDK.

| Step | Operation (controller property) | C# signature (verbatim) | Request model + fields | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 3 | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` ← `reference` | — | `CustomerResponse` → `.Customer` (`MaxioAdvancedBilling.Models.Customer?`-free: `Customer` is `!req`): read `Customer.Id: int?`, `Customer.Reference: string?`, `Customer.Email: string?` | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — 404 = not found: check `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| 3 | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest { Customer (customer): CreateCustomer **!req** }` → `CreateCustomer`: `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` · (all others optional — org, address, phone, locale…) | `CustomerResponse` → `.Customer.Id`, `.Customer.Reference` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` fallback — see the 422 caveat below | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 2 | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 nullable params have **no default** → pass explicitly (`null`); call with **named arguments**. `productFamilyId` accepts the id **or the handle prefixed `handle:`** — use `"handle:eshop-subscribe"` | — (query params: `filter` ← `filter`, `include_archived` ← `includeArchived`, …) | `IReadOnlyList<ProductResponse>`; each → `.Product`: `Name`, `Handle`, `PriceInCents: long?` (cents), `Interval: int?`, `IntervalUnit: IntervalUnit?` (day/month), `Description`, `RequireCreditCard: bool?`, `ProductFamily.Handle` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` fallback | manual `page`+`perPage` | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `Models/ProductFamilies.cs` (XML doc: "Either the product family's id or its handle prefixed with `handle:`") |
| 2 (alt) | `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` (same shape as above) | **Case B** `SdkException<RawError>` | none | `operations/Products.md` |
| 3 | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription **!req** }` → `CreateSubscription` fields used: `ProductHandle (product_handle): string?` · `CustomerId (customer_id): int?` · `Reference (reference): string?` · optionally `ProductPricePointHandle (product_price_point_handle): string?`. Also available (accepted, not needed here): `ProductId`, `ProductPricePointId`, `CustomerReference`, `PaymentCollectionMethod: CollectionMethod?`, `PaymentProfileId`, `CustomerAttributes`, `DeferSignup: bool? = false`. **Nothing is `!req`** on `CreateSubscription` | `SubscriptionResponse` → `.Subscription` (`Subscription?` — **null-check it**): `Id: int?`, `State: SubscriptionState?`, `CurrentPeriodEndsAt: DateTimeOffset?` (← *next billing date*), `Product: Product?` (plan name/handle), `ProductPriceInCents: long?`, `Reference: string?`, `Customer: Customer?` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] with `Errors: IReadOnlyList<string> !req` · `TryGetRawError(out RawError)` fallback | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md`, `records-3-Of-Su.md` |
| 4 | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — **this is the list-by-customer operation**; `Subscriptions.ListSubscriptions` has **no customer filter** (only product/coupon/state/date filters) | — | `IReadOnlyList<SubscriptionResponse>` → same `.Subscription` fields as above | **Case B** `SdkException<RawError>` | none (returns all subscriptions of the customer) | `operations/Customers.md`, `operations/Subscriptions.md` |
| 4 (detail) | `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` has **no default** → pass `null` | — | `SubscriptionResponse` → `.Subscription` | **Case B** `SdkException<RawError>` (404 check on `StatusCode`) | none | `operations/Subscriptions.md` |
| 3 (idempotency) | `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → pass explicitly | — | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` fallback | none | `operations/Subscriptions.md` |

### 2.3 Enum values actually needed (`MaxioAdvancedBilling.Models.Enums` — these are `StringEnum<T>` records, **not** C# enums; build with static members like `CollectionMethod.Automatic` or `Type.FromValue("wire")`)

| Enum | Members — `CSharpName (wire_value)` | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `models/enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `models/enums.md` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `models/enums.md` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `models/enums.md` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` | `models/enums.md` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `models/enums.md` |

### 2.4 Error-reading facts (the boundary)

| Fact | Detail | Source |
|---|---|---|
| Exception type | `SdkException<TError>` (`MaxioAdvancedBilling.Core`); `.Error` is `TError` | `sdk-map.md` |
| Case B payload | `RawError`: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()` — 404 detection = `StatusCode == HttpStatusCode.NotFound` | `sdk-map.md` |
| Case A fallback | every typed error inherits `TryGetRawError(out RawError)` — use it for statuses the typed accessors don't cover | `sdk-map.md` |
| CreateCustomer 422 shape caveat | `CustomerErrorResponse1.Errors` is typed as the shared `Errors` record, which only carries `PerPage` / `PricePoint` string lists — it cannot represent real customer field errors (e.g. duplicate-reference). **Directive:** do not read structured 422 messages from it; treat a 422 on create as "maybe duplicate reference" → re-lookup by `ReadCustomerByReference`, and for display fall back to `TryGetRawError` + `ReadAsString()`. Live wire shape of the real 422 body: `UNVERIFIED` | `records-2-Cr-Ne.md` (shared `Errors` model visible in the map) |
| CreateSubscription 422 shape | `ErrorListResponse1.Errors: IReadOnlyList<string> !req` — a flat list of messages; safe to join for display | `records-2-Cr-Ne.md` |
| Two `JsonException` hazard rows | **Mandatory — see §4 and load `dotnet-error-handling`.** (a) a malformed **2xx** body surfaces as `JsonException` from deserialization, not as `SdkException`; (b) a **non-2xx** body that doesn't match the generated `{Operation}Error` shape throws `JsonException` *while constructing the error object*, **replacing** the `SdkException` and destroying the HTTP status — a boundary mapping every `JsonException` to 5xx reports a deterministic rejection (422) as an outage, and callers retrying 5xx retry something that can never succeed | `sdk-map.md` error model + `dotnet-error-handling` |

---

## 3. Trap notes (hazard + pointer — load the named skill before that step; it carries the defaults and worked examples)

- ⚠ Step 1 (client registration) — the `HttpClient` argument's lifetime/handler pipeline must be managed (IHttpClientFactory, long-lived handler); constructing the client per request or newing up a handler per client silently breaks connection reuse and DNS refresh. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ Step 1 (credentials) — credentials must be set before the client's first call, from configuration (`Maxio:ApiKey`), never hardcoded; getting the Basic-auth username/password roles backwards yields 401s that look like config bugs. **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–4 (every call) — list operations take 7–14 nullable parameters with **no C# defaults**; a positional call mis-binds silently. Call with named arguments, passing `null` explicitly. **MUST load `dotnet-calling-endpoints`** before the first SDK call.
- ⚠ Steps 2–4 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `init`-only setters and `required` members enforced only by the compiler, JSON wire names differ from C# names, and unmodeled JSON fields are silently dropped on deserialize — price/state reads must go through the typed accessors above, not ad-hoc JSON. **MUST load `dotnet-models`** when building requests or mapping responses.
- ⚠ Step 5 (error boundary) — Case A vs Case B differs **per operation** (see §2.2 error column); `TryGetRawError` is not a catch-all on typed errors, and both `JsonException` hazard rows in §2.4 apply. **MUST load `dotnet-error-handling`** before writing any try/catch.
- ⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not** an `HttpClient` timeout; whether a failed `POST /subscriptions` can be re-sent, and what `Timeout` actually bounds, are decided by the retry configuration — and a transport-failure retry can execute a non-idempotent signup more than once. **MUST load `dotnet-configuration-resilience`** before tuning `RetryOptions` or the base URL.
- ⚠ Testing — the `HttpClient` constructor argument is the test seam for stubbing SDK responses; stubbing the controllers themselves doesn't exist. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load **before implementation starts** — the sheet deliberately does not carry their contents)

- `dotnet-client-initialization` · Step 1 — HttpClient ownership/lifetime, DI registration shape, whether the client wrapper may be transient.
- `dotnet-authentication` · Step 1 — Basic-credentials wiring (username = API key, password = `"x"`), per-environment config, rotation.
- `dotnet-calling-endpoints` · Steps 2–4 — named-argument calling, nullable-no-default parameters, async/cancellation (`ct`).
- `dotnet-models` · Steps 2–4 — request construction, `required` members, `StringEnum<T>` building/reading, wire names vs C# names.
- `dotnet-error-handling` · Step 5 — Case A/B catch ladders, `TryGet…` accessors, both `JsonException` hazards, status preservation.
- `dotnet-configuration-resilience` · Step 1 — what `Timeout`/`MaxRetries` actually bound, retry semantics on non-idempotent POSTs, base-URL/server override, pagination.
- `dotnet-testing` · tests — the `HttpClient` seam, covering error/edge paths, framework/assertion style matching.

---

## 5. Assumptions & Blockers

**Blockers** — none. Every operation the three flows need exists in the SDK and is documented in the map.

**Assumptions (minor):**
1. The eShop user has a stable, string-convertible id available inside `src/PublicApi` (JWT-authenticated) to use as the customer `reference` value (`"eshop-user-{id}"`).
2. The seeded sandbox products (`eshop-pro`, `basic-plan`) have no non-default price points to select — the sheet carries `ProductPricePointHandle` as an optional field; if the plans use non-default price points, supply the handle at call time.
3. `ListCustomerSubscriptions` returns *all* the customer's subscriptions with no state filter; if the endpoint must show only live subscriptions, filter client-side on `SubscriptionState` (values in §2.3) — a YOUR CALL on presentation, not a contract gap.
4. Numeric ids in the brief (product family 3023074, products 7126957/7126958) are usable as fallbacks only; all contracts above are handle-based (`handle:eshop-subscribe`, `product_handle`, `reference`) per the brief's stability requirement.
5. Whether Maxio rejects a duplicate subscription `reference` (provider-side uniqueness) is **UNVERIFIED** — no map Notes assert it; the double-click subscription guarantee must not rely on it.
