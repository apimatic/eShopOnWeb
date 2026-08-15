# Maxio Advanced Billing — "Subscribe" hero flow: implementation plan + contract sheet

SDK: `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · client
`MaxioAdvancedBillingClient` · source stamp `v1.0.2` (`15db14b`). Auth = HTTP Basic
(username = API key, password = literal `"x"`). Environment US.

Every fact below is grounded in the bundled SDK map; each row cites its map page. Load the
`dotnet-*` companion skills listed in REQUIRED READING **before** writing code — the trap notes
name hazards but deliberately do not resolve them.

---

## 1. Scope & sequence

| # | Step | Operations (controller.method) |
|---|---|---|
| 1 | Build + register the SDK client (subdomain-derived, or explicit BaseUrl override) | client construction + `options.Server`/`options.BasicAuth` |
| 2 | Ensure customer exists (idempotent on `reference`) | `Customers.ReadCustomerByReference` → (404) `Customers.CreateCustomer` |
| 3 | List plans in a product family (by handle) | `ProductFamilies.ListProductFamilies` (handle→id) → `ProductFamilies.ListProductsForProductFamily` |
| 4 | Create a subscription (no payment method) | `Subscriptions.CreateSubscription` (dedupe first via step 5) |
| 5 | List a customer's subscriptions ("my-subscriptions") | `Customers.ListCustomerSubscriptions` |

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

### 2a. Namespaces used in this flow (`using` directives)

| Type(s) | Namespace | Map source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | sdk-map.md (root) |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | sdk-map.md "Getting a client" |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | sdk-map.md "Servers & auth" |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | sdk-map.md options table |
| `options.Server.*` (ServerOptions/ProductionOptions properties, set inline — no new `using`) | `MaxioAdvancedBilling` / `MaxioAdvancedBilling.Servers` | sdk-map.md "Servers & auth" |
| All request/response records (`CreateCustomerRequest`, `CreateCustomer`, `Customer`, `CustomerResponse`, `CreateSubscriptionRequest`, `CreateSubscription`, `Subscription`, `SubscriptionResponse`, `Product`, `ProductResponse`, `ProductFamily`, `ProductFamilyResponse`, `ErrorListResponse1`, `CustomerErrorResponse1`, `Errors`) | `MaxioAdvancedBilling.Models` | records pages |
| Enums (`SubscriptionState`, `IntervalUnit`, `CollectionMethod`) | `MaxioAdvancedBilling.Models.Enums` | enums.md |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` | sdk-map.md namespaces table |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | sdk-map.md error model |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | sdk-map.md error model |
| `HttpStatusCode` | `System.Net` | BCL |

### 2b. Step 1 — client construction & auth

Only constructor (sdk-map.md): `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
DI alternative: `services.AddMaxioAdvancedBillingClient(o => { ... })`.

Options (sdk-map.md options table): `Environment: ServerEnvironment`, `Retry: RetryOptions`,
`Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?`.

- **Auth**: `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`.
- **Environment**: `options.Environment = ServerEnvironment.Us` (value `US`, default). US Production
  base-URL template is `https://{site}.chargify.com`.
- **Standard case (subdomain-derived)**: set `options.Server.Production.Us.Site = <Maxio:Subdomain>`
  (e.g. `"cp-exp-1"` ⇒ `https://cp-exp-1.chargify.com`).
- **Explicit BaseUrl override — SUPPORTED**: set `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`
  verbatim, in place of deriving from subdomain. Override point named in sdk-map.md "Servers & auth"
  (`options.Server.Production.Us.BaseUrl` / `.Us.Site`; sources `Server.cs`, `ServerOptions.cs`,
  `Servers/ProductionOptions.cs`). **No gap — do not STOP.** (Ebb/events group is not used by this flow.)

Cite: sdk-map.md "Getting a client" + "Servers & auth".

### 2c. Operations table

| Step | Controller.method (signature verbatim) | Request model + fields | Response envelope + read-back fields | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|
| 2 lookup | `Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` ← `reference` | pass the eShop key (user id/email) as `reference` | `CustomerResponse` → `Customer (customer): Customer !req`; read `Customer.Id (id): int?`, `Customer.Reference (reference): string?`, `Customer.Email (email): string?` | **Case B** `SdkException<RawError>`. Not-found = **404** → `ex.Error.StatusCode == HttpStatusCode.NotFound` (treat as "create"); other statuses = real error via `ReadAsString()` | none | operations/Customers.md; records-2-Cr-Ne.md (`Customer`, `CustomerResponse`) |
| 2 create | `Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`. `CreateCustomer` **required**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Idempotency key: `Reference (reference): string?` (set to eShop user id/email). Optional: `Organization`, `Address`, `City`, `State` (ISO), `Zip`, `Country` (ISO-2), `Phone`, `Locale`, `VatNumber`, `TaxExempt (bool?)`, `ParentId (int?)` … | `CustomerResponse` → `Customer` (as above); read `Customer.Id`, `Customer.Reference` | **Case A** `SdkException<CreateCustomerError>`. `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback]. Payload `CustomerErrorResponse1 { Errors (errors): Errors? }` where `Errors { PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }` (see UNVERIFIED note below) | none | operations/Customers.md; records-1-Ac-Cr.md (`CreateCustomer`, `CreateCustomerRequest`); records-2-Cr-Ne.md (`CustomerErrorResponse1`, `Errors`) |
| 3 resolve family handle→id | `ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 params must be passed explicitly (pass `null` to skip) | none (query filters only) | `IReadOnlyList<ProductFamilyResponse>` → each `ProductFamily (product_family): ProductFamily?`; match `ProductFamily.Handle (handle): string?` == `<Maxio:ProductFamilyHandle>`, take `ProductFamily.Id (id): int?` | **Case B** `SdkException<RawError>` | none | operations/ProductFamilies.md; records-3-Of-Su.md (`ProductFamily`, `ProductFamilyResponse`) |
| 3 list plans | `ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params (`dateField`…`include`) must be passed explicitly (pass `null`) | `productFamilyId` = the numeric id from the resolve step, as a string (`id.ToString()`) | `IReadOnlyList<ProductResponse>` → each `Product (product): Product !req`; read `Product.Handle (handle): string?` (plan handle), `Product.Name (name): string?`, `Product.Id (id): int?`, **price** `Product.PriceInCents (price_in_cents): long?` — **UNIT = CENTS** (integer minor units), interval `Product.Interval (interval): int?` + `Product.IntervalUnit (interval_unit): IntervalUnit?` (enum Day/Month) | **Case A** `SdkException<ListProductsForProductFamilyError>`. `TryGetString(out string)` **[404]** (family not found) · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 1 / 20) — loop pages if a family can exceed 20 plans | operations/ProductFamilies.md; records-3-Of-Su.md (`Product`, `ProductResponse`) |
| 4 create subscription | `Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`. On `CreateSubscription`: customer via `CustomerId (customer_id): int?` (use the id from step 2) **or** `CustomerReference (customer_reference): string?`; product via `ProductHandle (product_handle): string?` (preferred) **or** `ProductId (product_id): int?`. Payment NOT required for these plans → omit all `*Attributes`/`PaymentProfileId`. Optional idempotency handle: `Reference (reference): string?`. (No `CreateSubscription` field is C# `required`; the customer+product identifiers are enforced server-side — a missing/invalid combo surfaces as 422.) | `SubscriptionResponse` → `Subscription (subscription): Subscription?` **(nullable — null-check before reading)**. Read: plan/product `Subscription.Product (product): Product?` (then `Product.Handle`/`Product.Name`/`Product.Id`), price `Subscription.ProductPriceInCents (product_price_in_cents): long?` (**CENTS**), state `Subscription.State (state): SubscriptionState?`, next-billing/current-period-end `Subscription.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (the API does NOT echo `next_billing_at` — read `current_period_ends_at`; per UpdateSubscription notes) and `Subscription.NextAssessmentAt (next_assessment_at): DateTimeOffset?`; also `Subscription.Id (id): int?` | **Case A** `SdkException<CreateSubscriptionError>`. `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback]. Payload `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` (flat message list). 3DS/SCA also returns 422 (not relevant for no-payment plans) | none | operations/Subscriptions.md; records-2-Cr-Ne.md (`CreateSubscription`, `CreateSubscriptionRequest`, `ErrorListResponse1`); records-3-Of-Su.md / records-4-Su-We.md (`Subscription`, `SubscriptionResponse`) |
| 5 list customer subs | `Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — filter is the path `customerId` (the id from step 2); no query filter needed | none | `IReadOnlyList<SubscriptionResponse>` → each `Subscription (subscription): Subscription?` (null-check). Same read-back fields as step 4: `Product`, `ProductPriceInCents`, `State`, `CurrentPeriodEndsAt`/`NextAssessmentAt` | **Case B** `SdkException<RawError>` | none | operations/Customers.md; records-4-Su-We.md (`SubscriptionResponse`) |

Note on `Subscription.Product`: the nested `Product` is populated on the subscription payload
(same `MaxioAdvancedBilling.Models.Product` record as step 3), so plan handle/name/id and
`price_in_cents` read directly off it. `Subscription.CurrentBillingAmountInCents (long?)` is also
available if you want the current billed amount rather than the product list price. (records-3-Of-Su.md `Subscription`.)

### 2d. Enum value tables (only what this flow touches)

`SubscriptionState` — `StringEnum`, namespace `MaxioAdvancedBilling.Models.Enums` (write member name, not wire value):

| C# member | wire |
|---|---|
| `SubscriptionState.Pending` | `pending` |
| `SubscriptionState.Trialing` | `trialing` |
| `SubscriptionState.Active` | `active` |
| `SubscriptionState.SoftFailure` | `soft_failure` |
| `SubscriptionState.PastDue` | `past_due` |
| `SubscriptionState.Suspended` | `suspended` |
| `SubscriptionState.Canceled` | `canceled` |
| `SubscriptionState.Expired` | `expired` |
| `SubscriptionState.Paused` | `paused` |
| `SubscriptionState.Unpaid` | `unpaid` |
| `SubscriptionState.TrialEnded` | `trial_ended` |
| `SubscriptionState.OnHold` | `on_hold` |
| `SubscriptionState.AwaitingSignup` | `awaiting_signup` |
| `SubscriptionState.Assessing` | `assessing` |
| `SubscriptionState.FailedToCreate` | `failed_to_create` |

`IntervalUnit` — `StringEnum`: `IntervalUnit.Day` (`day`), `IntervalUnit.Month` (`month`). **Only these two — no Week/Year.** (enums.md)

`CollectionMethod` — `StringEnum` (only if you set `CreateSubscription.PaymentCollectionMethod`):
`Automatic` (`automatic`), `Remittance` (`remittance`), `Prepaid` (`prepaid`), `Invoice` (`invoice`). (enums.md)

Reminder (from `dotnet-models`): these are `StringEnum<T>` records, **not** C# enums — build with the
static member (`SubscriptionState.Active`) or `IntervalUnit.FromValue("month")`; compare by member, and
never `switch` as if it were a native enum without loading the skill.

### 2e. Error-handling contract (read once, applies to every call)

- All operations are **throw-only** (no `…Result`/no-throw variants). Every SDK call must be wrapped.
- **Case A (typed)** — `catch (SdkException<{Operation}Error> ex)`; then `ex.Error.TryGet…(out var payload)`
  for the status-specific shape, else `ex.Error.TryGetRawError(out RawError raw)` for anything else.
  Used here by: `CreateCustomer` (422), `CreateSubscription` (422), `ListProductsForProductFamily` (404).
- **Case B (raw)** — `catch (SdkException<RawError> ex)`; read `ex.Error.StatusCode` (a
  `System.Net.HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`.
  Used here by: `ReadCustomerByReference` (404 = "not found → create"), `ListProductFamilies`,
  `ListCustomerSubscriptions`.
- 404-on-lookup vs real error: for `ReadCustomerByReference` (Case B) branch on
  `ex.Error.StatusCode == HttpStatusCode.NotFound`; for `ListProductsForProductFamily` (Case A) the 404 is
  `TryGetString(out string)`.

---

## 3. Idempotency caveats (contract facts)

- **Customer** IS keyable: `CreateCustomer` notes state "you may only create one customer for a given
  `reference` value … must be unique." So the map-grounded idempotent pattern is:
  `ReadCustomerByReference(refKey)` → on **404** call `CreateCustomer` with `Reference = refKey`; a
  duplicate `reference` on create returns **422**. (operations/Customers.md)
- **Subscription is NOT deduped by the SDK/API on create.** `CreateSubscription` keys idempotency on
  nothing — calling it twice creates two subscriptions. To avoid duplicates you must dedupe yourself:
  before step 4, call `ListCustomerSubscriptions(customerId)` and skip creation if an active subscription
  to the target product already exists. (`Subscription.Reference` / `FindSubscription(reference)` exist but
  the map does NOT document any uniqueness enforcement on subscription reference — do not rely on it for
  dedupe.) (operations/Subscriptions.md, operations/Customers.md)
- This dedupe matters doubly because of the transport-retry hazard — see the Step 4 trap note.

---

## 4. Trap notes (load the named skill at the step where each bites)

> ⚠ Step 1 (client construction / DI) — the `HttpClient`/handler pipeline lifetime and how the SDK
> client wrapper is registered are not visible in the constructor signature; getting it wrong causes
> socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before writing
> `new MaxioAdvancedBillingClient(...)` or `AddMaxioAdvancedBillingClient(...)`.

> ⚠ Step 1 (base URL / server selection) — how `options.Server.Production.Us.Site` vs `.BaseUrl`
> interact with `ServerEnvironment`, and what the retry/timeout options actually bound (`Timeout` scope,
> which verbs/statuses retry), is not evident from the option names. **MUST load
> `dotnet-configuration-resilience`** before selecting the host or tuning retries.

> ⚠ Step 2 (auth) — where/when Basic credentials must be set relative to client construction, and loading
> the key from config rather than hardcoding, are usage rules the property type does not convey. **MUST
> load `dotnet-authentication`** before wiring `BasicAuth`.

> ⚠ Steps 3 & 5 (list calls) — `ListProductFamilies` (5), `ListProductsForProductFamily` (8) and
> `ListCustomers` (7) each have several nullable parameters with **no C# default**; a positional call
> mis-binds them. **MUST load `dotnet-calling-endpoints`** and call with named arguments (pass `null` to
> skip), using `ct:` for the token.

> ⚠ Steps 2–5 (models) — response enums are `StringEnum<T>` (not C# enums), union-typed fields need
> `TryGet…`, and unmodeled JSON fields are dropped on deserialize; the response envelope's inner payload
> (`SubscriptionResponse.Subscription`, `ProductFamilyResponse.ProductFamily`, `OfferResponse.Offer`) is
> **nullable** even though `CustomerResponse.Customer`/`ProductResponse.Product` are `!req`. **MUST load
> `dotnet-models`** before mapping SDK models onto eShop domain types.

> ⚠ Step 4 (create subscription — write-retry hazard) — whether a `CreateSubscription`/`CreateCustomer`
> POST that fails at the transport layer can be silently re-sent (and thus double-charge / double-create)
> is governed by retry semantics the option names hide; combine with the "no server-side dedupe" fact in
> §3. **MUST load `dotnet-configuration-resilience`** before relying on retries around these writes.

> ⚠ All steps (error boundary) — which exception types actually reach your catch blocks, and why a
> `JsonException` can bypass or replace an `SdkException`, is not derivable from the signatures. **MUST
> load `dotnet-error-handling`** before writing the try/catch ladder (see the two mandatory rows in
> REQUIRED READING).

> ⚠ Tests — the `HttpClient` constructor argument is the fake seam; match the eShop project's existing
> test framework/assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 5. REQUIRED READING (load BEFORE implementation starts)

These skills carry the defaults, worked examples, and semantics the sheet deliberately does not restate.
This flow writes an error boundary, so `dotnet-error-handling` is mandatory regardless.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-configuration-resilience` | Step 1 base URL/server selection + retry/timeout semantics; Step 4 write-retry hazard |
| `dotnet-authentication` | Step 2 — Basic credentials wiring |
| `dotnet-calling-endpoints` | Steps 3 & 5 — named-argument calls for multi-optional-param list ops |
| `dotnet-models` | Steps 2–5 — StringEnum enums, unions, nullable envelope inners, dropped fields |
| `dotnet-error-handling` | All steps — the exception boundary (Case A/B, `TryGet…`, JsonException traps) |
| `dotnet-testing` | Tests — faking the HttpClient seam |

Two hazard rows that MUST shape the error boundary from the first version (`System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 6. Assumptions & Blockers

**Assumptions**
- Environment is US ⇒ `ServerEnvironment.Us`, Production server group (`https://{site}.chargify.com`). The
  Ebb/events server group is not used by this flow.
- The customer `reference` idempotency key is a single stable string per eShop user (user id or email —
  pick one and use it consistently; the map enforces uniqueness only on whichever value you send).
- "Price" in plan listing and on subscriptions is `*_in_cents` (integer minor units, `long`), not dollars —
  convert for display.
- Product family is identified by a handle string (`Maxio:ProductFamilyHandle`), resolved to a numeric id
  before listing products (see below).

**Blockers / gaps to note (none hard-blocking)**
- **BaseUrl override IS supported** (`options.Server.Production.Us.BaseUrl`) — no gap; do not STOP on
  item 1.
- **Product-family handle→id resolution has no single dedicated SDK call.** `ProductFamilies.ReadProductFamily`
  takes an `int id` only (its notes mention a `handle:my-family` string form, but the C# signature does not
  accept a string). The reliable, fully map-grounded path is `ListProductFamilies(...)` then client-side match
  on `ProductFamily.Handle` to obtain `ProductFamily.Id`, then `ListProductsForProductFamily(id.ToString(), …)`.
  `UNVERIFIED`: whether passing the string `"handle:eshop-subscribe"` directly as
  `ListProductsForProductFamily`'s `productFamilyId` (the SDK interpolates it verbatim into the path) is
  accepted by the live API cannot be confirmed from the SDK source — the SDK only builds the URL. **Directive:**
  implement the `ListProductFamilies`→match→id path as the primary resolver; do not depend on the `handle:`
  path-prefix shortcut unless a live probe confirms it.
- **`CustomerErrorResponse1` 422 payload shape is suspicious.** Its `Errors` record exposes only
  `PerPage (per_page)` and `PricePoint (price_point)` string lists (records-2-Cr-Ne.md `Errors`) — fields that
  do not obviously correspond to customer-create validation (name/email/reference). This looks like a shared/generic
  generated error model. `UNVERIFIED`: the actual 422 field names Maxio returns for a duplicate/invalid customer
  cannot be confirmed from the map. **Directive:** in the 422 branch, extract messages best-effort from
  `CustomerErrorResponse1.Errors` if present, but fall back to `TryGetRawError(out raw)` +
  `raw.ReadAsString()` (or `raw.ReadAsJson<T>()`) for the generic message rather than assuming a specific field.
- No hard blockers: every capability the feature needs (client + BaseUrl override, lookup-by-reference,
  create-customer, list-families, list-products-for-family, create-subscription, list-customer-subscriptions)
  is exposed by the SDK.
