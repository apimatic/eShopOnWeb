# Maxio Advanced Billing integration plan — eShopOnWeb `src/PublicApi`

Billing system of record: Maxio Advanced Billing sandbox site `cp-exp-1`. Three JWT-authenticated
endpoints: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.
Everything keys off **handles** (product family `eshop-subscribe`, products `eshop-pro` / `basic-plan`);
numeric IDs are unstable. A metered component `api-call` exists in the catalog but is out of scope.

SDK: NuGet **`AsadAli.AdvancedBilling.Sdk`** (root namespace `MaxioAdvancedBilling` — package id ≠
using namespace). `netstandard2.0`, fine on net8.0 / C# 12. Install: `dotnet add package AsadAli.AdvancedBilling.Sdk`.

## 1. Scope & sequence

| Step | Work | SDK operations used |
|---|---|---|
| 1 | Add NuGet package; bind `MaxioOptions` from `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) | — |
| 2 | Construct + DI-register `MaxioAdvancedBillingClient` (Basic auth; site or BaseUrl override) | — |
| 3 | `GET /api/subscription-plans` — list products in the configured family, map to `{ handle, name, price, interval }` | `ProductFamilies.ListProductsForProductFamily` (fallback: `Products.ListProducts` + client-side family filter) |
| 4 | `POST /api/subscriptions` — find-or-create customer by reference (= eShopOnWeb user id); check existing subs; create if none active | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` · `Customers.ListCustomerSubscriptions` → `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — find customer by reference, list subs, map `{ plan, price, state, nextBillingDate }` | `Customers.ReadCustomerByReference` · `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary: translate SDK exceptions to HTTP responses | (all of the above) |
| 7 | Tests for the integration layer | (fake the `HttpClient` seam) |

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

**Using-directives needed:** `MaxioAdvancedBilling` (client/options) ·
`MaxioAdvancedBilling.Core.Authentication.Basic` (`BasicAuthCredentials`) ·
`MaxioAdvancedBilling.Servers` (`ServerEnvironment`, `ProductionOptions`) ·
`MaxioAdvancedBilling.Core.Configuration` (`RetryOptions`) ·
`MaxioAdvancedBilling.Core.Exceptions` (`SdkException<T>`) ·
`MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`) ·
`MaxioAdvancedBilling.Models` (records) · `MaxioAdvancedBilling.Models.Enums` (enums) ·
`MaxioAdvancedBilling.Errors` (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`).

### Op 1 — List plans (products in family) · `operations/ProductFamilies.md`

- **Call:** `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`
  - The 8 params `dateField … include` are nullable with **no C# default → must be passed explicitly** (pass `null`). Use named arguments.
  - `productFamilyId` is a `string` path segment (`GET /product_families/{product_family_id}/products.json`). Pass `"handle:" + handle` (i.e. `"handle:eshop-subscribe"`). The map documents the `handle:<name>` format for product-family lookup on a sibling op (`ReadProductFamily`), and this op's string-typed path param accepts it mechanically, but the map does not state it for *this* endpoint — **`UNVERIFIED`**: if it returns 404, fall back to Op 1b.
- **Returns:** `IReadOnlyList<ProductResponse>`; envelope `ProductResponse.Product (product): Product` (**required**, one level down).
- **`Product` fields read** (`records-3-Of-Su.md`): `Handle (handle): string?` · `Name (name): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `ArchivedAt (archived_at): DateTimeOffset?` (skip archived: `ArchivedAt != null`) · nested `ProductFamily (product_family): ProductFamily?` with `ProductFamily.Handle (handle): string?`.
- **Error:** `SdkException<ListProductsForProductFamilyError>` — **Case A**. Accessors: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback].
- **Pagination:** manual `page`/`perPage` (defaults 1/20) — loop until a short page.

### Op 1b — Fallback plan listing (site-wide + client-side filter) · `operations/Products.md`

- **Call:** `client.Products.ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — same 8 must-pass-explicitly params, all `null` here.
- **Returns:** `IReadOnlyList<ProductResponse>`; filter in code: `p.Product.ProductFamily?.Handle == options.ProductFamilyHandle`.
- **Error:** `SdkException<RawError>` — **Case B** (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`).
- **Pagination:** manual `page`/`perPage`.

### Op 2 — Find customer by reference · `operations/Customers.md`

- **Call:** `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` (`GET /customers/lookup.json?reference=…`). `reference` = the eShopOnWeb user id (stable string).
- **Returns:** `CustomerResponse`; envelope `CustomerResponse.Customer (customer): Customer` (**required**).
- **`Customer` fields read** (`records-2-Cr-Ne.md`): `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName`/`LastName`.
- **Error:** `SdkException<RawError>` — **Case B**. **Not-found = `ex.Error.StatusCode == HttpStatusCode.NotFound`** → that is the "create" branch of find-or-create, not a failure.

### Op 3 — Create customer · `operations/Customers.md`

- **Call:** `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly**.
- **Request:** `CreateCustomerRequest { Customer = new CreateCustomer { … } }` — `CreateCustomerRequest.Customer (customer): CreateCustomer` **required**.
  - `CreateCustomer` (`records-1-Ac-Cr.md`): **`FirstName (first_name): string` req · `LastName (last_name): string` req · `Email (email): string` req** · `Reference (reference): string?` ← set to the eShopOnWeb user id. Reference is unique per site — that uniqueness is what makes find-or-create safe.
- **Returns:** `CustomerResponse` → `.Customer` (required) → `Customer.Id`.
- **Error:** `SdkException<CreateCustomerError>` — **Case A**. Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback].
  - ⚠ `CustomerErrorResponse1.Errors (errors)` is typed as the **shared** `Errors` record, which the map shows carrying only `PerPage`/`PricePoint` fields (`records-2-Cr-Ne.md`) — it does not model customer field errors, and unmodeled JSON is dropped on deserialize. **Defensive directive:** extract 422 detail best-effort, but always fall back to `TryGetRawError(out var raw)` + `raw.ReadAsString()` for the message. Whether the live 422 body matches the generated shape is **`UNVERIFIED`**.
  - Race note: a concurrent create with the same reference can 422 — on 422, re-run `ReadCustomerByReference` and use the winner.

### Op 4 — List a customer's subscriptions · `operations/Customers.md`

- **Call:** `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` (`GET /customers/{customer_id}/subscriptions.json`). `customerId` = `Customer.Id` from Op 2/3.
- **Returns:** `IReadOnlyList<SubscriptionResponse>`; envelope `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable** (unlike the customer/product envelopes): null-check before reading.
- **`Subscription` fields read** (`records-3-Of-Su.md`): `Id (id): int?` · `State (state): SubscriptionState?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ← "next billing date" · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `ProductPriceInCents (product_price_in_cents): long?` · `Reference (reference): string?` · nested `Product (product): Product?` → `Product.Handle`, `Product.Name`, `Product.PriceInCents`, `Product.Interval`, `Product.IntervalUnit` · nested `Customer (customer): Customer?`.
- **Error:** `SdkException<RawError>` — **Case B**.
- **Pagination:** none.

### Op 5 — Create subscription · `operations/Subscriptions.md`

- **Call:** `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly**.
- **Request:** `CreateSubscriptionRequest { Subscription = new CreateSubscription { … } }` — `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription` **required** (`records-2-Cr-Ne.md`).
  - `CreateSubscription` fields used: **`ProductHandle (product_handle): string?`** ← the plan handle (`eshop-pro` / `basic-plan`) · **`CustomerId (customer_id): int?`** ← `Customer.Id` from find-or-create (alternative: `CustomerReference (customer_reference): string?` = user id — either identifies the customer; use `CustomerId` since we already resolved it) · `Reference (reference): string?` ← optionally set to `{userId}:{productHandle}` for extra traceability. No payment profile fields — these plans capture no card.
- **Returns:** `SubscriptionResponse` → `.Subscription` (**nullable** — null-check) → read `Id`, `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Product.Handle`/`Name`/`PriceInCents`.
- **Error:** `SdkException<CreateSubscriptionError>` — **Case A**. Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **required** (`records-2-Cr-Ne.md`).
- **Idempotency (app-side, required):** before creating, run Op 4 and match `Subscription.Product?.Handle == productHandle` with `State` in an active-ish set (`Active`, `Trialing`, `Assessing`); if found, return the existing subscription (HTTP 200) instead of POSTing. Maxio has no create-if-absent; the pre-check + customer-reference uniqueness is the dedup mechanism. A narrow double-click race remains (two concurrent POSTs after two empty pre-checks) — acceptable for this scope; note it in the endpoint docs.

### Enums needed · `map/models/enums.md` (all `StringEnum<T>`, namespace `MaxioAdvancedBilling.Models.Enums`; use static members, e.g. `SubscriptionState.Active`, or `SubscriptionState.FromValue("active")` — **not** C# enums, no lowercase members)

| Enum | Members (C# name = wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` (only if you set `PaymentCollectionMethod`; default is fine) |

State comparison: `StringEnum<T>` — compare against the static members (`sub.State == SubscriptionState.Active`) or the wire string per `dotnet-models`; do not `.ToString()`-parse.

### Client construction / auth / base URL · `sdk-map.md` (Sources: `MaxioAdvancedBillingClient.cs`, `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`)

- Only constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- Options properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?`.
- **Auth (Basic):** `BasicAuth = new BasicAuthCredentials { Username = maxioOptions.ApiKey, Password = "x" }` — password is the literal string `"x"`.
- **Site selection (default path):** `Environment = ServerEnvironment.Us` (→ template `https://{site}.chargify.com`) and `Server.Production.Us.Site = maxioOptions.Subdomain` (`"cp-exp-1"`). `{site}` defaults to `subdomain`; set it explicitly.
- **BaseUrl override (`Maxio:BaseUrl` set):** `Server.Production.Us.BaseUrl = maxioOptions.BaseUrl` — used **verbatim** as the API base address (replaces scheme+host template). Apply instead of, not in addition to, `Site`.
- DI alternative shipped by the SDK (`ServiceCollectionExtensions.cs`): `services.AddMaxioAdvancedBillingClient(o => { …same options… })`.
- `RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`): all members `required` — start from `RetryOptions.Default()` and mutate, never `new` it piecemeal.

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline must be long-lived and reused
> (socket exhaustion otherwise); how the SDK's DI helper vs manual construction splits ownership of
> `HttpClient` is not visible from the constructor. **MUST load `dotnet-client-initialization`.**

> ⚠ Step 2 (auth) — credentials must be set before the client is constructed / in the DI callback, and
> the API key must come from configuration, never source. **MUST load `dotnet-authentication`.**

> ⚠ Steps 3–5 (calling) — list ops have 8–14 nullable params with **no C# defaults** that mis-bind in
> positional calls; call with named arguments only (`ct:` for the token). **MUST load
> `dotnet-calling-endpoints`.**

> ⚠ Steps 3–5 (models) — enums are `StringEnum<T>` records (not C# enums), records are immutable with
> `required` init members, and unmodeled JSON fields are silently dropped on deserialize (bites the
> `CustomerErrorResponse1` 422 payload — see Op 3). **MUST load `dotnet-models`.**

> ⚠ Step 6 (error boundary) — the Case A / Case B split differs per operation (see each row above);
> `TryGetRawError` is not a catch-all on typed errors; this SDK has **no** no-throw `…Result` variants —
> every call is throw-only and must be wrapped. **MUST load `dotnet-error-handling`.**

> ⚠ Step 2 (resilience) — whether a failed `POST /subscriptions` can be re-sent by the retry layer, what
> `Timeout` actually bounds, and what pagination/logging you must wire yourself are not derivable from the
> option names; this matters directly to the create-subscription idempotency story. **MUST load
> `dotnet-configuration-resilience`.**

> ⚠ Step 7 (tests) — the test seam is the `HttpClient` constructor argument; which layer to fake and how
> to assert behaviour (not execution) is skill content. **MUST load `dotnet-testing`.**

## 4. REQUIRED READING — load all of these **before implementation starts**

This sheet deliberately does not carry these skills' contents; load each at its step.

- `dotnet-client-initialization` — Step 2 (client construction & DI registration)
- `dotnet-authentication` — Step 2 (Basic credentials wiring)
- `dotnet-calling-endpoints` — Steps 3–5 (every operation call)
- `dotnet-models` — Steps 3–5 (request/response model construction, enums, nullability)
- `dotnet-error-handling` — Step 6 (exception boundary)
- `dotnet-configuration-resilience` — Step 2 (retries, timeout, base URL, pagination)
- `dotnet-testing` — Step 7 (integration-layer tests)

Mandatory hazard rows for the error boundary (Step 6) — `System.Text.Json.JsonException` reaches the
boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape
  the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException`
  to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries
  something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumed:** the eShopOnWeb user id (JWT `sub`) is a stable string usable verbatim as the Maxio customer
  `reference`. Customer `FirstName`/`LastName`/`Email` are required by `CreateCustomer` — assumed available
  from the JWT/identity store (eShopOnWeb identity provides email; names may need a placeholder or profile claim).
- **Assumed:** `Maxio:BaseUrl`, when set, includes scheme and host (e.g. `https://mock.local`) and no path
  suffix; it is passed verbatim to `Server.Production.Us.BaseUrl`.
- **Assumed:** US hosting (`ServerEnvironment.Us`) for sandbox `cp-exp-1`; switch to `Eu` only if the account
  is EU-hosted.
- **`UNVERIFIED`:** `ListProductsForProductFamily` accepting `"handle:eshop-subscribe"` as its string
  `productFamilyId` (map documents the `handle:` format only for `ReadProductFamily`). Fallback Op 1b is
  fully map-grounded — implement the fallback path regardless and prefer whichever the sandbox confirms.
- **`UNVERIFIED`:** live 422 body for `CreateCustomer` matching the generated `CustomerErrorResponse1` shape
  (shared `Errors` model looks under-specified in the map) — defensive raw-body fallback is specified in Op 3.
- **Known gap (accepted):** create-subscription dedup is pre-check based; a concurrent double-POST race is
  not fully closed by the API. Documented in Op 5.
- No blockers.
