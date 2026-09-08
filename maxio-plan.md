# Maxio Advanced Billing — eShopOnWeb PublicApi recurring-subscription integration

## 1. Scope & sequence

Additive capability in **`src/PublicApi`** (ASP.NET Core, JWT-authenticated). Maxio is the billing
system of record. New endpoints (Ardalis `ApiEndpoint` style, matching existing PublicApi conventions,
`[Authorize]`):

1. **`GET …/plans`** — browse plans: products in the configured product family.
   Ops: `client.ProductFamilies.ListProductsForProductFamily` (+ `client.Sites.ReadSite` for the site's
   default currency, because `Product` carries no currency field). Input handle read from config key
   `MAXIO_DEFAULT_PRODUCT_FAMILY` (documented in the brief; value e.g. `eshop-subscribe`).
2. **`POST …/subscribe`** — ensure the Maxio customer for the JWT caller exists (reference-keyed, see
   contract), then create a subscription to the requested plan **without any payment profile**.
   Ops: `ReadProductByHandle` (validate the plan handle) → `ReadCustomerByReference` →
   `CreateCustomer` (only on 404) → `CreateSubscription` → read-back.
3. **Confirm back to the caller** (part of the subscribe response): plan handle/name, price, state,
   next billing date — from the `Subscription` returned by the create/read-back, not from app-local
   state.
4. **`GET …/my-subscriptions`** — `ReadCustomerByReference` → `ListCustomerSubscriptions`.

The existing eShopWeb cart/checkout flow is untouched. All new calls go through **one integration
boundary class** (the SDK client + a thin Maxio-specific service) so the SDK never leaks past the
boundary; the boundary owns the catch ladder (§ 3) and the whole-call cancellation budget.

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**

> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### 2.1 Operations

All operations hit the **Production** server group. There is **no** `{Operation}Result` no-throw
variant for any of them — every call is throw-only (`SdkException<TError>`). Namespace facts:
`SdkException<T>` → `MaxioAdvancedBilling.Core.Exceptions` · `RawError` → `MaxioAdvancedBilling.Core.ErrorResponse` ·
per-op typed errors (`CreateCustomerError`, …) → `MaxioAdvancedBilling.Errors` · all `record` models
below → `MaxioAdvancedBilling.Models` · enums → `MaxioAdvancedBilling.Models.Enums`.

| Op (controller prop) | HTTP | Signature (params in order; `ct` last, `= default`) | Request model + fields the plan sets | Response envelope → read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| **Browse plans** — `client.ProductFamilies.ListProductsForProductFamily` | `GET /product_families/{product_family_id}/products.json` | `(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | none (query only) — pass `dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null, perPage: 200` (per-page max 200) | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product: Product !req` (unwrap; guard null) | **A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage`; stop when a page returns < `perPage` | operations/ProductFamilies.md; `Api/ProductFamilies.cs` |
| **Resolve one plan** — `client.Products.ReadProductByHandle` | `GET /products/handle/{api_handle}.json` | `(string apiHandle, CancellationToken ct = default)` | none | `ProductResponse.Product` (unwrap; guard null) | **B** `SdkException<RawError>`: `ex.Error.StatusCode` / `ReadAsString()` — missing product ⇒ 404 | none | operations/Products.md; `Api/Products.cs` |
| **Site currency** — `client.Sites.ReadSite` | `GET /site.json` | `(CancellationToken ct = default)` | none | `SiteResponse.Site !req` (unwrap); read `Site.Currency (currency): string?` | **B** `SdkException<RawError>` | none | operations/Sites.md; records-3 `Site`/`SiteResponse` |
| **Customer lookup** — `client.Customers.ReadCustomerByReference` | `GET /customers/lookup.json` | `(string reference, CancellationToken ct = default)` — the *app's* reference value is the query param | none | `CustomerResponse.Customer` (unwrap); `Customer.Id (id): int?`, `Customer.Reference (reference): string?` | **B** `SdkException<RawError>` — "no customer yet" is a **404**; branch on `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | operations/Customers.md; records-2 `Customer` |
| **Customer create** — `client.Customers.CreateCustomer` | `POST /customers.json` | `(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest.Customer: CreateCustomer !req`; `CreateCustomer` `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` — set `Reference` to the app user id. **ISO 3166 2-char codes for `Country`/`State` if supplied** | `CustomerResponse.Customer` | **A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback] | none | operations/Customers.md; records-1 `CreateCustomer`, records-2 `CustomerErrorResponse1` |
| **Subscription create (no card)** — `client.Subscriptions.CreateSubscription` | `POST /subscriptions.json` | `(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest.Subscription: CreateSubscription !req` (the `CreateSubscription` record itself has **no `required` members** — `Notes`/XML doc carry the acceptance rules, see Notes column) | `SubscriptionResponse.Subscription: Subscription?` — **nullable**; guard null before reading | **A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md; records-2 `CreateSubscription`, records-4 `SubscriptionResponse` |
| **Read-back** — `client.Subscriptions.ReadSubscription` | `GET /subscriptions/{subscription_id}.json` | `(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — pass `include: null` (skip self-service token) | none | `SubscriptionResponse.Subscription` (unwrap) | **B** `SdkException<RawError>` (404 = gone) | none | operations/Subscriptions.md |
| **My subscriptions** — `client.Customers.ListCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` | `(int customerId, CancellationToken ct = default)` — `customerId` is the Maxio numeric id from `Customer.Id` | none | `IReadOnlyList<SubscriptionResponse>`; unwrap `.Subscription` per item | **B** `SdkException<RawError>` | none (returns all) | operations/Customers.md |

**Notes (provider prose / generated XML docs — the "when a call is accepted" facts):**

- **CreateCustomer** — *"The only validation restriction is that you may only create one customer for a
  given reference value. If provided, the `reference` value must be unique."* ⇒ the customer-level
  idempotency key. A concurrent duplicate create with the same reference is **rejected with a 422**
  (accessor `TryGetCustomerErrorResponse1`) — do **not** treat that as a failure: re-run
  `ReadCustomerByReference` and continue with the winner's `Customer.Id`.
- **CreateSubscription** — product is addressed by `product_handle` **or** `product_id` (one required);
  customer by `customer_id` **or** `customer_reference` (one required); `reference` is *"the reference
  value (provided by your app) for the subscription itself."* This plan sets:
  `ProductHandle` = the validated plan handle; `CustomerReference` = the app user id (the same key the
  customer was created under); `Reference` = deterministic `$"{userRef}:{planHandle}"` (enables
  lookup/reconcile); `PaymentCollectionMethod = CollectionMethod.Automatic`; everything else about
  price/billing cycle/trial/setup fee comes from the **product and its default price point** — the
  `CreateSubscription` payload has **no fields** for trial/setup-fee/taxable/never-expires, so those
  cannot be set at subscription time (trial/tax are product attributes; `expires_at` here defaults to
  never when unset). **Leave unset** (the no-card path): `PaymentProfileId`, `PaymentProfileAttributes`,
  `CreditCardAttributes`, `BankAccountAttributes` — passing no payment profile is exactly the
  card-free create; whether the site **accepts** it depends on the product (`require_credit_card`/`request_credit_card`,
  readable from `Product`) and on the sandbox seeding.
- **First charge / state** — omit `next_billing_at`, `initial_billing_at`, `defer_signup` so the
  subscription activates **immediately** (state `active` when the product has no trial). Per the
  generated XML doc, a **future** `next_billing_at` suppresses trial/initial charges (import semantics);
  unset means normal signup behavior (assess/charge per product defaults). Exact sandbox behavior with
  no card is a live-wire fact — see § 6 (`UNVERIFIED`).
- **Next billing date wire names** — `Subscription.CurrentPeriodEndsAt (current_period_ends_at)` is the
  end of the current period = the next renewal date for a healthy subscription;
  `Subscription.NextAssessmentAt (next_assessment_at)` tracks the *payment-capture* attempt and diverges
  only when a renewal payment fails/retries. For "next billing date" prefer `current_period_ends_at`;
  on this no-card path there is nothing to fail at signup.
- **Subscription price/currency/state read** — `Subscription.ProductPriceInCents (product_price_in_cents): long?`
  (base-product charge), `Subscription.Currency (currency): string?` (site default currency, echoed onto
  the subscription), `Subscription.State (state): SubscriptionState?`, nested `Subscription.Product: Product?`
  (carries `handle`, `name`, `price_in_cents`, `interval`, `interval_unit`, `interval`).

### 2.2 Request-model construction requirements

- `CreateCustomerRequest` — `required CreateCustomer Customer`. On `CreateCustomer` three members are
  C# `required`: `FirstName`, `LastName`, `Email`. The app has no profile data beyond the JWT
  `ClaimTypes.Name` (= login name/email) and `ApplicationUser` (an empty `IdentityUser`); sourcing
  first/last name + email is an **app-side identity decision** — see § 5.
- `CreateSubscriptionRequest` — `required CreateSubscription Subscription`. **No member of
  `CreateSubscription` is `required` in C#** — every field is `T?`/optional, so the compiler will not
  stop you from omitting the plan or the customer. The provider-side acceptance rules (Notes column
  above) are the real gate; carry them into the constructing code. Fields the plan deliberately does
  **not** set: `ProductPricePointHandle/Id` (accept the product's default price point), `CustomPrice`,
  `Components`, `CalendarBilling`, coupons, `NetTerms`, `ReceivesInvoiceEmails`, `ExpiresAt`,
  `NextBillingAt`, `InitialBillingAt`, `Currency` (omit unless multi-currency is configured at the
  site), `AgreementAcceptance`.
- No `OneOf`/`AnyOf` union is constructed or read anywhere in this scope (enums and plain records
  only).

### 2.3 Enum value sets actually needed (wire values in parens; enums are `StringEnum<T>` records in `MaxioAdvancedBilling.Models.Enums`, built with static members or `FromValue`, read back via `.Value`)

| Enum | Members used |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `Trialing (trialing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `Paused (paused)`, `Assessing (assessing)`, `FailedToCreate (failed_to_create)` |
| `CollectionMethod` | `Automatic (automatic)` — sent on `CreateSubscription.PaymentCollectionMethod` |
| `IntervalUnit` | `Day (day)`, `Month (month)` — read `Product.IntervalUnit` / `Subscription.Product.IntervalUnit` for the billing cycle |

Full lists: `map/models/enums.md`. Other types touched but not constructed: `SubscriptionInclude`
(`Coupons`, `SelfServicePageToken`) and `ListProductsInclude` (`PrepaidProductPricePoint`) are skipped
by passing `null`, so no enum is needed for them.

### 2.4 Client construction, auth, servers — per-environment config

| Fact | Value | Source |
|---|---|---|
| Package (already referenced per brief — verify csproj) / `using` | `AsadAli.AdvancedBilling.Sdk` / `MaxioAdvancedBilling` | sdk-map.md |
| Client | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — the only ctor; **the SDK does not own the `HttpClient`** | sdk-map.md `Getting a client` |
| Auth | Basic: `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — `BasicAuthCredentials` (`MaxioAdvancedBilling.Core.Authentication.Basic`) has **`required string Username`** and **`required string Password`**; password is the literal `"x"`. Missing/incorrect credentials ⇒ 401 from the API | sdk-map.md `Servers & auth` |
| Environment | `options.Environment` (`MaxioAdvancedBilling.Servers`): `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`. **Read once at client construction** — to change environment, construct a new client | sdk-map.md |
| Subdomain / site | `options.Server.Production.Us.Site = "<subdomain>"` (template param `{site}`) — nested per server **and** environment. `ServerOptions` is in the root namespace; `ProductionOptions`/`UsOptions`/`EuOptions` in `MaxioAdvancedBilling.Servers`. `Server` is re-read per request, but configure before constructing | sdk-map.md + source `Servers/ProductionOptions.cs` |
| Base-URL override (mock/dev) | `options.Server.Production.Us.BaseUrl = "http://localhost:8080"` (set the one matching `options.Environment`) | sdk-map.md |
| Retry defaults | `StatusCodesToRetry` = 408/429/500/502/503/504 (a 429 is retried up to `MaxRetries` extra times with backoff before it surfaces); `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS (**status** trigger only); `MaxRetries = 3`; `Delay = 1s`; `BackOffFactor = 2`; `Timeout = 100s` (per attempt); `MaxJitter = 500ms`. `RetryOptions` → `MaxioAdvancedBilling.Core.Configuration`, all members `required` — build from `RetryOptions.Default() with { … }` | sdk-map.md + source `Core/Configuration/RetryOptions.cs` |

**Config keys.** `MAXIO_DEFAULT_PRODUCT_FAMILY` (value = family **handle**, e.g. `eshop-subscribe`) is
given in the brief — pass it to the list operation **prefixed `handle:`**
(`"handle:eshop-subscribe"`; the parameter doc says *"Either the product family's id or its handle
prefixed with `handle:`"*). The API key / site subdomain / US-vs-EU switch / optional BaseUrl override
have **no documented key names** in this repo's appsettings; name them when you add the config section:
`YOUR CALL — not in the map`.

## 3. Trap notes

- ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call; the
  DI extension registers the client as a **singleton** that holds one factory `HttpClient`, and the
  "Timeout" knobs are per-attempt. **MUST load `dotnet-client-initialization`** before writing the
  `AddMaxioAdvancedBillingClient`/factory registration.
- ⚠ Step 1 (credentials) — Maxio's Basic scheme means username = API key and password = literal `"x"`;
  the options object carries more knobs than this API accepts. **MUST load `dotnet-authentication`**
  before setting credentials.
- ⚠ Steps 1–4 (resilience) — `HttpMethodsToRetry` gates only the *status* retry trigger: a transport
  failure (`HttpRequestException`) is re-sent on **every verb, POST included**, and retries cannot be
  turned off — so a failed `CreateCustomer`/`CreateSubscription` may have executed more than once and
  the boundary must treat a failed write's outcome as unknown and reconcile by reading back. **MUST
  load `dotnet-configuration-resilience`** before choosing retry/timeout values.
- ⚠ Steps 2–4 (calls) — three operations here take a long leading run of nullable parameters with no C#
  default (`ListProductsForProductFamily`, `ReadSubscription`, …); positional calls mis-bind. Use named
  arguments with the literal parameter names. **MUST load `dotnet-calling-endpoints`** before the first
  `client.{Group}.{Operation}` call.
- ⚠ Steps 2–4 (models) — response "envelopes" nest the payload (`CustomerResponse.Customer`,
  `ProductResponse.Product`, `SubscriptionResponse.Subscription`) and several are nullable even where
  marked `!req`; enums are `StringEnum<T>` records, not C# enums. **MUST load `dotnet-models`** when
  constructing request bodies or mapping responses onto DTOs.
- ⚠ Step 2 boundary (error handling) — the operations mix Case A typed errors (`CreateCustomerError`,
  `CreateSubscriptionError`, `ListProductsForProductFamilyError`) and Case B `SdkException<RawError>`
  (all reads); each Case-A type has exactly two accessors and `TryGetRawError` is the **last** branch,
  not a catch-all. **MUST load `dotnet-error-handling`** before writing the boundary's catch ladder.
- ⚠ Step 2 boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch
  ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.
- ⚠ Step 2 boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error`
  shape throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.
- ⚠ Step 4 (verify on the wire) — path and query values (notably the `handle:`-prefixed family id and
  the `reference` query param) are not type-checked against the route; a wrong value compiles cleanly
  and shows up only as a runtime 404/422. **MUST load `dotnet-configuration-resilience`** and use its
  first-run logging-handler checklist.

## 4. REQUIRED READING

Load these **before implementation starts**; the contract sheet deliberately does not carry their
contents (resolving them inline would let a stale one-liner defeat the skill). The boundary is written
early, so the two `System.Text.Json.JsonException` hazard rows of § 3 (the 2xx-parse case and the
error-body-parse case, which need opposite handling) belong to the FIRST sheet and must shape the
boundary from the start — see § 3 and `dotnet-error-handling` below:

| Skill | Governs |
|---|---|
| `dotnet-authentication` | Basic credentials (username = API key, password = `"x"`), 401 diagnosis |
| `dotnet-calling-endpoints` | Controller properties, named arguments, envelopes, `ct:` |
| `dotnet-client-initialization` | Client construction, `HttpClient` ownership/lifetime, DI registration (singleton caveat) |
| `dotnet-configuration-resilience` | Retry semantics (status vs transport triggers), timeouts, site/BaseUrl, per-page limits, first-run wire check |
| `dotnet-error-handling` | The one boundary catch ladder: Case A accessor order, Case B `RawError`, the two `JsonException` directions, status-preserving mapping |
| `dotnet-models` | `StringEnum<T>` read-back, required members, nullable envelopes, collection/null handling |
| `dotnet-testing` | Stubbing the `HttpClient` seam; write-once assertions under both retry triggers |

## 5. Assumptions & Blockers

- **Assumption — first/last name source.** `CreateCustomer` requires `FirstName`, `LastName`, `Email`,
  but the JWT carries only `ClaimTypes.Name` (the login name/email) and `ApplicationUser` is an empty
  `IdentityUser` with no name fields. The app must resolve the caller's profile (or derive a stable
  name) through its own identity path before creating the Maxio customer. **YOUR CALL — not in the map.**
- **Assumption — customer reference value.** The Maxio `reference` = the app's stable user identifier
  derived from the authenticated principal (`ApplicationUser.Id` or the login name, as the app already
  holds it). It must be stable for the user's lifetime and must not collide across users — Maxio
  enforces reference uniqueness and the create-race recovery depends on it. **YOUR CALL — not in the
  map.**
- **Assumption — no duplicate-subscription race.** The brief requires a double-click/concurrent POST
  never to create two **customers or two subscriptions**. Customers are covered by the documented
  reference uniqueness. Subscriptions are covered here by (a) an SDK-level guard: the create carries a
  deterministic `Reference` and the flow reconciles by re-reading the customer's subscriptions after
  any ambiguous outcome, and (b) the app serializing one in-flight subscribe per user (its own
  concurrency rule to implement). **Whether Maxio's `POST /subscriptions.json` itself rejects a second
  create that reuses a subscription `reference` is not stated in the map or the SDK source** — do not
  rely on a rejection; keep the reconcile-and-return-existing behavior. `UNVERIFIED`.
- **Assumption — plan catalog is small / default price points.** The browse endpoint reads the
  products' **default price points** (price, interval) from `Product.PriceInCents/Interval/IntervalUnit`
  and assumes per-product currency is the site default (`ReadSite` → `Site.Currency`). If the seeded
  plans use non-default price points or per-product `currency_prices`, the price/currency shown here
  will not match the charged amount. `UNVERIFIED` against the live site.
- **Assumption — "no payment method required" really is configured.** The plan never sends card data,
  which the SDK happily permits, but the **provider** still rejects the create (422) if the product or
  site requires a payment method at signup. Confirmed working on sandbox per the brief, but the guard
  is provider-side: check `Product.RequireCreditCard`/`RequestCreditCard` from the browse data if a
  subscribe 422 is ever a surprise. `UNVERIFIED`.
- **Blocker — none.** Every operation, model, enum, and error type this plan names exists in the map
  (verified against SDK source `v1.0.2` where the map's error-accessor tables were abbreviated).

## 6. Source labels

Every row above cites its map page (`operations/…md`, `map/models/…md`); where the map page names a
source file it is cited as `Api/…cs`, `Models/…cs`, etc. Rows carrying `UNVERIFIED` are facts only live
traffic can settle. Rows carrying `YOUR CALL — not in the map` are application decisions the implementer
owns.
