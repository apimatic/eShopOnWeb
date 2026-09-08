# maxio-plan.md — Maxio Advanced Billing subscription capability for eShopOnWeb `src/PublicApi`

Target: .NET 8 · additive integration · package `AsadAli.AdvancedBilling.Sdk` (import `MaxioAdvancedBilling`) · Maxio **sandbox** · JWT-authenticated endpoints on `src/PublicApi` · secrets via user-secrets, never in repo · no Docker/extra infra.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Bind config section `Maxio:` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl`); construct/register the SDK client (Basic auth, sandbox host or verbatim base-URL override) | `MaxioAdvancedBillingClient` construction / `AddMaxioAdvancedBillingClient` |
| 2 | `GET /api/subscription-plans` — resolve product family by handle from config, list products under it, map to plan DTOs (handle, name, price in cents, interval) | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily`, (`Products.ReadProductByHandle` for a single pinned plan) |
| 3 | `POST /api/subscriptions` — idempotent: resolve-or-create customer by app-user reference, probe existing subscription by reference, create subscription for customer + product handle **without payment profile**, confirm plan/price/state/next-billing-date | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — customer by reference → their subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 5 | Error boundary around every SDK call (Case A/B ladder + `JsonException` hazards), mapping deterministic 4xx/422 rejections distinctly from outages | — |

Config keys are application bindings from the brief: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional verbatim override). How the authenticated eShopOnWeb user maps to the Maxio customer `reference` value is the app's identity decision — `YOUR CALL — not in the map`.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Client construction, auth, base URL

| Fact | Detail | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` (Getting a client) |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` (options table) |
| Auth scheme | HTTP **Basic** — `Username` = the Maxio API key, `Password` = the literal string `"x"` | `sdk-map.md` (Servers & auth) |
| Auth type | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username, Password }` | `sdk-map.md` (Servers & auth) |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default; US-hosted `https://{site}.chargify.com`) — sandbox subdomain goes in `Site`, below | `sdk-map.md` (Servers & auth) |
| Subdomain (derived URL) | `options.Server.Production.Us.Site = "<Maxio:Subdomain>"` → `https://{site}.chargify.com` | `sdk-map.md` (Servers & auth; `Servers/ProductionOptions.cs`) |
| Verbatim base-URL override | when `Maxio:BaseUrl` is set: `options.Server.Production.Us.BaseUrl = "<value used verbatim as API base address>"` (skips subdomain derivation) | `sdk-map.md` (Servers & auth; `Server.cs`, `Servers/ServerOptions.cs`, `Servers/ProductionOptions.cs`) |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })` (`ServiceCollectionExtensions.cs`) | `sdk-map.md` (Getting a client) |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — **all members `required`**; start from `RetryOptions.Default()` and mutate | `sdk-map.md` (RetryOptions table) |
| API groups | every controller is a property on the client: `client.Subscriptions`, `client.Customers`, `client.Products`, `client.ProductFamilies` (namespace `MaxioAdvancedBilling.Api`) | `sdk-map.md` (Operations index) |
| Secrets | `Maxio:ApiKey` (and `Subdomain`/`BaseUrl` where sensitive) via user-secrets / environment config on `src/PublicApi` — never in repo | YOUR CALL — not in the map (brief constraint) |

Shape (names verbatim from the map; do not copy values):

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    Environment = ServerEnvironment.Us,
    BasicAuth   = new BasicAuthCredentials { Username = apiKey, Password = "x" },
};
options.Server.Production.Us.Site = subdomain;      // derived host
// or, when Maxio:BaseUrl configured:
options.Server.Production.Us.BaseUrl = baseUrl;     // verbatim override
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

### 2.2 Operations

Parameter order is verbatim; "`—`" params are nullable with **no C# default → must pass explicitly** (pass `null` to skip); `ct` is the literal token parameter name.

| Operation | Signature (verbatim) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | — (query only) | `IReadOnlyList<ProductFamilyResponse>`; each → `.ProductFamily: ProductFamily?` — fields: `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `ArchivedAt (archived_at): DateTimeOffset?` | **Case B** `SdkException<RawError>` — `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | **none** (returns all families for the site — no `page`/`perPage` params) | `operations/ProductFamilies.md` |
| `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — (`productFamilyId` is a **string**; pass the family's numeric id as string) | `IReadOnlyList<ProductResponse>`; each → `.Product: Product !req` — identifying fields below (§2.3) | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1 / 20) | `operations/ProductFamilies.md` |
| `Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` → `.Product !req` | **Case B** `SdkException<RawError>` (404 → `StatusCode == HttpStatusCode.NotFound`) | none | `operations/Products.md` |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — query `reference` | `CustomerResponse` → `.Customer: Customer !req` — `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName (first_name): string?` · `LastName (last_name): string?` | **Case B** `SdkException<RawError>` (**404 = no customer with that reference** → create path) | none | `operations/Customers.md` |
| `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateCustomerRequest` — `Customer (customer): CreateCustomer !req`; `CreateCustomer` fields: `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` (dedupe key) · address/org/phone/locale optional | `CustomerResponse` → `.Customer !req` (`Id`, `Reference`) | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. **422 also fires when the `reference` already exists** (Notes: only one customer per reference value) — see Step-3 note | none | `operations/Customers.md` + Notes |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription: Subscription?` (fields §2.4) | **Case B** `SdkException<RawError>` | **none** (returns all of that customer's subscriptions — no page params) | `operations/Customers.md` |
| `Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | — query `reference` | `SubscriptionResponse` → `.Subscription: Subscription?` | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [**404 = not found**] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription !req`; `CreateSubscription` **marks nothing required** — fields this integration sets: `ProductHandle (product_handle): string?` · `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` · `Reference (reference): string?` (subscription-level app reference) · `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` · `NextBillingAt (next_billing_at): DateTimeOffset?` · `CouponCode (coupon_code): string?`. Payment-profile fields (`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`) are all optional and are **omitted** | `SubscriptionResponse` → `.Subscription: Subscription?` — fields read: `Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Product` → plan/price (§2.4) | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] where `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` + `records-2-Cr-Ne.md` (`CreateSubscription`, `ErrorListResponse1`) |

**Notes-named acceptance condition (CreateSubscription):** the provider's Notes state payment information *may be required depending on the options of the Product being subscribed*. The request model itself lets you omit every payment field; whether the omission is accepted is governed by the **product's** flags (`Product.RequireCreditCard (require_credit_card): bool?`, `Product.RequestCreditCard (request_credit_card): bool?` — visible in the catalog response). The brief states the seeded plans need no card; surface `Product.RequireCreditCard` in the plans response and reject subscribes to card-requiring plans at the endpoint rather than discovering it as a 422. *Catalog-side fact — `YOUR CALL — not in the map` for how you seed it.*

### 2.3 Product (plan) identification fields — `MaxioAdvancedBilling.Models.Product`

| Field | Wire name | Type | Use |
|---|---|---|---|
| `Id` | `id` | `int?` | product reference for `CreateSubscription.ProductId` alternative |
| `Handle` | `handle` | `string?` | identity; used as `CreateSubscription.ProductHandle` |
| `Name` | `name` | `string?` | display name |
| `PriceInCents` | `price_in_cents` | `long?` | price in cents |
| `Interval` | `interval` | `int?` | billing interval count |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` | `MaxioAdvancedBilling.Models.Enums.IntervalUnit`: `Day (day)` · `Month (month)` |
| `TrialPriceInCents` / `TrialInterval` / `TrialIntervalUnit` | `trial_price_in_cents` / `trial_interval` / `trial_interval_unit` | `long?` / `int?` / `IntervalUnit?` | trial terms, if any |
| `RequireCreditCard` / `RequestCreditCard` | `require_credit_card` / `request_credit_card` | `bool?` | whether the provider will demand payment info at signup |
| `ProductFamily` | `product_family` | `ProductFamily?` | `.Id`, `.Handle`, `.Name` — client-side family match if you list site-wide |

Source: `records-3-Of-Su.md` (`Product`, `ProductFamily`, `ProductResponse`, `ProductFamilyResponse`), `enums.md` (`IntervalUnit`).

### 2.4 Subscription fields read back — `MaxioAdvancedBilling.Models.Subscription`

| Field | Wire name | Type | Meaning |
|---|---|---|---|
| `Id` | `id` | `int?` | subscription id |
| `State` | `state` | `SubscriptionState?` | state — enum §2.5 |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | current price in cents |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | **next billing date** |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | next assessment |
| `CurrentPeriodStartedAt` | `current_period_started_at` | `DateTimeOffset?` | period start |
| `Product` | `product` | `Product?` | plan confirmation — `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit` (§2.3) |
| `Customer` | `customer` | `Customer?` | embedded customer |

Source: `records-3-Of-Su.md` (`Subscription`, line 156), `records-4-Su-We.md` (`SubscriptionResponse`).

### 2.5 Enums needed — `MaxioAdvancedBilling.Models.Enums` (these are `StringEnum<T>` records, **not** C# enums)

| Enum | Members (C# member (wire value)) | Used for |
|---|---|---|
| `SubscriptionState` | `Pending (pending)` · `FailedToCreate (failed_to_create)` · `Trialing (trialing)` · `Assessing (assessing)` · `Active (active)` · `SoftFailure (soft_failure)` · `PastDue (past_due)` · `Suspended (suspended)` · `Canceled (canceled)` · `Expired (expired)` · `Paused (paused)` · `Unpaid (unpaid)` · `TrialEnded (trial_ended)` · `OnHold (on_hold)` · `AwaitingSignup (awaiting_signup)` | branching/filtering on `Subscription.State`. Notes: `Assessing` is transient — do not base access decisions on it |
| `SubscriptionStateFilter` | `Active (active)` · `Canceled (canceled)` · `Expired (expired)` · `ExpiredCards (expired_cards)` · `OnHold (on_hold)` · `PastDue (past_due)` · `PendingCancellation (pending_cancellation)` · `PendingRenewal (pending_renewal)` · `Suspended (suspended)` · `TrialEnded (trial_ended)` · `Trialing (trialing)` · `Unpaid (unpaid)` | the `state` query filter on site-wide `ListSubscriptions` (NOT used by `ListCustomerSubscriptions`, which has no filters) |
| `IntervalUnit` | `Day (day)` · `Month (month)` | plan interval display |
| `CollectionMethod` | `Automatic (automatic)` · `Remittance (remittance)` · `Prepaid (prepaid)` · `Invoice (invoice)` | only if you set `CreateSubscription.PaymentCollectionMethod` |

Source: `enums.md` lines 21, 47, 96, 97.

**Listing by customer — important:** the site-wide `Subscriptions.ListSubscriptions` has **no customer/customer-reference filter parameter** (its filters are `state`, `product`, `productPricePointId`, `coupon(Code)`, date ranges, `metadata`, `sort`/`direction`). The per-user listing path is `Customers.ReadCustomerByReference(reference)` → `Customers.ListCustomerSubscriptions(customerId)`. `ListSubscriptions` is therefore **out of scope** for `GET /my-subscriptions`. Source: `operations/Subscriptions.md` (ListSubscriptions row), `operations/Customers.md` (ListCustomerSubscriptions row).

**Idempotency facts (grounded):**
- Customer create supports `CreateCustomer.Reference (reference): string?`, and the operation's Notes state the provider enforces **at most one customer per reference value** — that is the dedupe key (app-user id → reference; the mapping itself is `YOUR CALL — not in the map`).
- Lookup by reference exists: `Customers.ReadCustomerByReference(string reference)` (Case B; **404** = absent → create).
- A 422 from `CreateCustomer` where the typed accessor matches also means "reference already exists" per the Notes — treat as resolve-again-then-proceed, not as a failure, if you take the create-first path.
- Subscription create supports a `Reference (reference): string?` and `Subscriptions.FindSubscription(reference)` looks one up (404 via `TryGetNoContent`). **Uniqueness of a subscription reference is NOT documented in the map** — the FindSubscription Notes say only "Finds a subscription by its reference". Whether the provider rejects a duplicate subscription reference is `UNVERIFIED` (only live traffic could confirm). Defensive directive: probe `FindSubscription` before create and treat a hit as the idempotent replay; if concurrent double-clicks can interleave, dedupe must be guarded app-side (the provider-side guarantee you can rely on is the customer-reference uniqueness above). Label: `UNVERIFIED`.
- Customer create is idempotent-by-reference as documented; the recommended flow is **lookup first, create on 404, proceed on 422-duplicate-reference** — a double click then never yields two customers.

---

## 3. Trap notes (name + consequence — the pointed-to skill carries the resolution)

- ⚠ Step 1 (client registration) — the `HttpClient` you pass into `MaxioAdvancedBillingClient` must have the right lifetime/pipeline; naively `new`-ing clients per request or per resolve breaks handler reuse and can strand credentials wiring. **MUST load `dotnet-client-initialization`** before wiring the client into DI.
- ⚠ Step 1 (auth) — credentials must be set before/at client construction, from configuration (user-secrets), and the Basic convention (`Username` = API key, `Password` = `"x"`) is easy to invert — a 401 that looks like a bad key may be a swapped pair. **MUST load `dotnet-authentication`** before setting `BasicAuth`.
- ⚠ Steps 2–4 (calls) — many list/search operations have nullable parameters with **no C# default** (they must be passed explicitly, `null` to skip), and the token parameter is literally `ct`; positional calls with trailing optionals mis-bind. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Operation}` call.
- ⚠ Steps 2–4 (models) — `SubscriptionState`/`IntervalUnit` etc. are `StringEnum<T>` records built from static members or `FromValue`, request records are immutable with `required` members enforced only by the compiler at initializer time, and C# property names differ from JSON wire names. **MUST load `dotnet-models`** before constructing any request payload or mapping responses.
- ⚠ Step 5 (error boundary) — the SDK is throw-only (no no-throw variants); Case A operations expose `TryGet…` accessors **per status shape** while Case B operations throw `SdkException<RawError>`, and the accessors are not interchangeable. **MUST load `dotnet-error-handling`** before writing any `try/catch` around these calls.
- ⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** behave like the timeouts you may assume (per-attempt vs whole-call, and which failures are retried on which verbs — relevant to whether a double-click can double-execute a write). **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` or registering the client.
- ⚠ Step 5 (tests) — the `HttpClient` constructor argument is the test seam; faking the controllers is not. **MUST load `dotnet-testing`** before writing tests for the integration layer.

---

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic credentials wiring, config sourcing, 401 diagnosis |
| `dotnet-calling-endpoints` | Steps 2–4 — calling conventions, named arguments, `ct`, nullable-must-pass params |
| `dotnet-models` | Steps 2–4 — request records, `StringEnum<T>`, wire names, required members |
| `dotnet-error-handling` | Step 5 — Case A/B exception ladder, `TryGet…` accessors, the two `JsonException` hazards below |
| `dotnet-configuration-resilience` | Step 1 — retries, timeout semantics, base-URL/server selection, pagination mechanics |
| `dotnet-testing` | Step 5 — the test seam and edge-path coverage |

Mandatory hazard rows (both belong to the boundary you write in Step 5):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

1. **Assumption** — the sandbox site is a standard US-hosted Advanced Billing site; `Maxio:Subdomain` is its subdomain and the SDK derives `https://{subdomain}.chargify.com`. If the sandbox exposes a different host, `Maxio:BaseUrl` (used verbatim via `options.Server.Production.Us.BaseUrl`) covers it. The exact host the sandbox serves is `UNVERIFIED` until first live call.
2. **Assumption** — the seeded products under `Maxio:ProductFamilyHandle` have card requirement disabled (`require_credit_card`/`request_credit_card` off), so `CreateSubscription` without payment fields is accepted. The SDK contract permits omitting payment fields; whether the *catalog* accepts it depends on the product's own flags (see §2.2 note). If a plan is seeded card-requiring, creation returns a 422 via `ErrorListResponse1` — treat that as a catalog defect, not a code defect.
3. **Assumption / `UNVERIFIED`** — subscription-level `reference` uniqueness is not documented in the map. The idempotent-subscribe flow probes `FindSubscription` first; provider-side rejection of a duplicate subscription reference cannot be confirmed from the map or source. The app must not rely on the provider alone to dedupe subscriptions (customer dedupe **is** provider-documented).
4. **`UNVERIFIED`** — the 422 payload shape for `CreateCustomer`: the map's typed accessor targets `CustomerErrorResponse1` whose `Errors` member is typed as the record `Errors` carrying only `per_page`/`price_point` lists (`records-2-Cr-Ne.md` line 64) — a shared model that does not plausibly describe customer-validation messages. Directive: call `TryGetCustomerErrorResponse1` best-effort and extract nothing from it unless present; always fall back to `TryGetRawError(out RawError)` + `RawError.ReadAsString()` for the user-visible message. The live wire shape of that 422 body can only be confirmed by traffic.
5. **`YOUR CALL — not in the map`** items: mapping the authenticated eShopOnWeb user to the customer `reference` value and to subscription `reference` values; which app states count as "subscribable"; how `GET /my-subscriptions` filters the returned list (e.g. by `SubscriptionState`); plan DTO shape for `GET /subscription-plans`; user-secrets storage layout.
6. **Blockers** — none. Every operation the three endpoints need exists in the SDK with a documented contract.
