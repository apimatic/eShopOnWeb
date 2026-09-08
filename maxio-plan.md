# Maxio Advanced Billing — PublicApi subscription plan

## 1. Overview

Add Maxio Advanced Billing subscription capability to `src/PublicApi` (ASP.NET Core, net8.0):

- `GET /api/subscription-plans` — list saleable plans (product = "plan") of the `eshop-subscribe`
  product family, with handle/name/price(+currency).
- `POST /api/subscriptions` — idempotent enroll: find-or-create ONE Maxio customer per eShopOnWeb user
  (`reference` = local user id, server-enforced unique), create a subscription to the requested plan by
  plan **handle**, **no payment profile** (site configured so payment method is not required), return the
  resulting subscription.
- `GET /api/my-subscriptions` — list the current user's customer's subscriptions.

A local store (the app's decision) maps userId ↔ Maxio customer id (+ subscription ids); across restarts
the customer is re-found by `reference`. Maxio config binds from `Maxio:ApiKey`, `Maxio:Subdomain`,
`Maxio:BaseUrl` (optional override).

**SDK identity (source: sdk-map.md / maxio-getting-started):**

| Fact | Value |
|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` — add with `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` (map pinned to tag `v1.0.2`, commit `15db14b`) |
| Root namespace (using) | `MaxioAdvancedBilling` |
| Target framework | `netstandard2.0` → consumable from net8.0 (runs on .NET 10 SDK via roll-forward) — confirmed compatible |
| Client class | `MaxioAdvancedBillingClient` (`MaxioAdvancedBilling` namespace); only ctor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBillingClientOptions` (`MaxioAdvancedBilling` namespace) |
| Auth | HTTP **Basic** — `Username` = API key, `Password` = literal `"x"` (`BasicAuthCredentials`, namespace `MaxioAdvancedBilling.Core.Authentication.Basic`) |
| Environments | `ServerEnvironment.Us` (default) / `ServerEnvironment.Eu` — namespace `MaxioAdvancedBilling.Servers` |
| Base-URL template (Production group) | US `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com`; `{site}` ← `options.Server.Production.Us.Site` |
| Server override | `options.Server.Production.Us.BaseUrl` (default contains `{site}` which the Site value fills; a placeholder-less override replaces the host outright) — source `Servers/ProductionOptions.cs` |
| Error base | Every op is **throw-only** (no no-throw variants). Error statuses throw `SdkException<TError>` (namespace `MaxioAdvancedBilling.Core.Exceptions`) with `.Error` of type `TError` |

## 2. Assumptions & Blockers

| # | Item | Status |
|---|---|---|
| A1 | Sandbox is US-hosted (default `ServerEnvironment.Us`). The brief's three config keys carry no environment switch; if the sandbox subdomain is an EU-hosted site you must also select `ServerEnvironment.Eu` (extra config key = app's decision). | ASSUMPTION — verify against site host |
| A2 | `Product.PriceInCents` in a product-list payload equals the product's default price-point amount on this sandbox (field may be legacy). Fallback read op is provided in the sheet. | UNVERIFIED — only live payload confirms |
| A3 | Signup without a payment profile returns the subscription synchronously in whatever terminal-ish state Maxio assigns; do **not** assume `Active` — the sheet's directive is to trust and surface the returned `State` and re-read (`ReadSubscription`) for fresh state. | UNVERIFIED — live-only |
| A4 | `ReadCustomerByReference` signals "no customer for reference" with HTTP 404 surfaced as `SdkException<RawError>` (`RawError.StatusCode == NotFound`). Detect no-match that way; treat any other status as a real failure. | UNVERIFIED — live-only status |
| A5 | `CreateSubscription` response embeds the nested `product`/`customer` objects on the wire. The SDK models them as nullable; defensive directive: if `Subscription.Product` is null after create, re-read via `ReadSubscription` before building the DTO. | UNVERIFIED — live-only |
| A6 | The site's single-currency `Site.Currency` is the currency of plan price points (only true when the site is not multi-currency; price-point `currency_prices` exist otherwise). Catalog currency decision below is `YOUR CALL`. | UNVERIFIED — live-only |
| A7 | Neither catalog filtering ("saleable") nor idempotency-race handling can be fully done server-side: filter archived products client-side and resolve the customer create race via the 422 + re-lookup (sheet row). Store design and which local store survives restarts are the app's. | YOUR CALL — not in the map |
| — | **No blockers:** every operation the three endpoints need exists in the map. Nothing requires inventing a data path. | |

## 3. Recommended PublicApi layering sketch (advisory — app architecture is YOUR CALL)

A thin `IMaxioSubscriptionService` (or three focused services: catalog, enrollment, my-subscriptions) behind
the JWT-authenticated controllers, holding all Maxio SDK types and mapping Maxio records to the app's own
DTOs; an injected single `MaxioAdvancedBillingClient` registered at startup from the config binding above;
and a `userId ↔ Maxio customer id (+ subscription ids)` persistence seam (repository over the chosen local
store) that the service consults first and that doubles as the find-before-create cache, with
`ReadCustomerByReference`/`ListCustomerSubscriptions` as the re-find path when the mapping is cold. The
Maxio HTTP call surface itself never leaks past this layer.

---

## 4. CONTRACT SHEET

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

### 4.0 Client construction / auth / server node (facts)

Source: `sdk-map.md`; source files `MaxioAdvancedBillingClientOptions.cs`, `Servers/ProductionOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`.

| Fact | Value |
|---|---|
| Client ctor | `new MaxioAdvancedBillingClient(httpClient, options)` — the `HttpClient` is the test seam and must be long-lived/reused |
| Config → credentials | `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` — password is the **literal** `"x"`, never the secret |
| Config → subdomain | `options.Server.Production.Us.Site = <Maxio:Subdomain>` (property type `string`; namespace of `ProductionOptions.Us` member set is `MaxioAdvancedBilling.Servers`) |
| Config → override | if `Maxio:BaseUrl` present: `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`; a placeholder-less URL replaces the whole host (see A1 for EU) |
| Environment | `options.Environment = ServerEnvironment.Us` (default); EU hosting → `ServerEnvironment.Eu` |
| DI | `services.AddMaxioAdvancedBillingClient(o => …)` (per `dotnet-client-initialization`) |
| 401 / wrong host | failure is config-shaped: check `Maxio:ApiKey`/`Maxio:Subdomain`/host before touching call sites |

### 4.1 Operations

Param note: an empty cell in *Must pass* means "only what the signature requires"; named arguments are
mandatory for any call with ≥2 parameters (many nullable params have no C# default and mis-bind
positionally — map header note on every operations page). `body` params shown `…?` are nullable with no
default: pass an instance, never omit.

#### OP-1 `GET /api/subscription-plans` → list products of the family

| | |
|---|---|
| Controller property / op | `client.ProductFamilies.ListProductsForProductFamily(...)` — `MaxioAdvancedBilling.Api` |
| Signature (params in order) | `Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must pass explicitly | `productFamilyId` (string): **family id OR its handle prefixed `handle:`** — i.e. pass `"handle:eshop-subscribe"` (param doc, source `Api/ProductFamilies.cs`); then `dateField`, `filter`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `includeArchived`, `include` — all nullable, no defaults: pass `null` (and `includeArchived: false` to exclude archived; also filter client-side on `ArchivedAt == null`) |
| Response envelope | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product` → `MaxioAdvancedBilling.Models.Product` |
| Fields read (wire name) | `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?` (see A2), `DefaultProductPricePointId (default_product_price_point_id): int?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?` |
| Error case | **A (typed)** — `catch (SdkException<ListProductsForProductFamilyError> ex)`; accessors: `ex.Error.TryGetString(out string)` [404 = family not found], `ex.Error.TryGetRawError(out RawError)` [fallback] |
| Pagination | manual `page` + `perPage` (defaults 1/20; max `perPage` 200) |
| Source | `map/operations/ProductFamilies.md`; record row `records-3-Of-Su.md` |

#### OP-2 (helper) price + currency for the catalog DTO

| | |
|---|---|
| Controller property / op | `client.Sites.ReadSite()` — returns `SiteResponse` (`SiteResponse.Site` → `MaxioAdvancedBilling.Models.Site`, `!req`) |
| Field read | `Site.Currency (currency): string?` — the site's primary currency code (e.g. `"USD"`); base currency of plan price points when single-currency (A6) |
| Error case | **B (raw)** — `catch (SdkException<RawError> ex)`; read `ex.Error.StatusCode` / `ReadAsString()` |
| Source | `map/operations/Sites.md`; `records-3-Of-Su.md` |
| Fallback price helper | `client.ProductPricePoints.ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` nullable, no default: pass `null` (or `true` to also get per-currency prices). Build selectors `ProductIdModel.Int((int)product.Id)` / `PricePointIdModel.Int((int)product.DefaultProductPricePointId)` (namespace `MaxioAdvancedBilling.Models.AnyOf`); response `ProductPricePointResponse.PricePoint` → `ProductPricePoint.PriceInCents (price_in_cents): long?`, `.CurrencyPrices: IReadOnlyList<CurrencyPrice>?` (`CurrencyPrice.Currency`/`.FormattedPrice`). Case **B** error. Sources: `map/operations/ProductPricePoints.md`, `records-3-Of-Su.md`, `records-2-Cr-Ne.md`, `map/models/unions.md`. Policy: primary `Product.PriceInCents` (A2), fall back to price-point read |

#### OP-3 `POST /api/subscriptions` step (a) — find customer by `reference`

| | |
|---|---|
| Controller property / op | `client.Customers.ReadCustomerByReference(...)` |
| Signature | `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| Call | `ReadCustomerByReference(reference: <user id as string>)` — exact, single-match lookup endpoint `GET /customers/lookup.json?reference=…` (the map's Notes direct: "To retrieve a single, exact match by reference, use the lookup endpoint", not the `q` fuzzy search of `ListCustomers`) |
| Response envelope | `CustomerResponse.Customer` (`!req`) → `Customer`: read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName/LastName` |
| Error case | **B (raw)** — `catch (SdkException<RawError> ex)`; `ex.Error.StatusCode` / `ReadAsString()`. A `NotFound` status = no customer yet (A4) → proceed to OP-4 |
| Pagination | none |
| Source | `map/operations/Customers.md` (ReadCustomerByReference); `records-2-Cr-Ne.md` |

#### OP-4 create the customer (only when OP-3 says none)

| | |
|---|---|
| Controller property / op | `client.Customers.CreateCustomer(...)` |
| Signature | `Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| Request model | `MaxioAdvancedBilling.Models.CreateCustomerRequest` — `Subscription`-style wrapper: `Customer (customer): CreateCustomer !req` (C# `required`) |
| `CreateCustomer` members we set (wire name) | `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` (optional, **settable**; Notes: only one customer may exist per `reference` value — uniqueness is enforced server-side) |
| Omitted optional members | address/`State`/`Country` (ISO codes if ever sent), `Phone`, `Locale`, `Organization`, etc. — none needed for enroll |
| Response envelope | `CustomerResponse.Customer.Id` (`int?`) → the Maxio customer id to persist + pass to OP-5 |
| Error case | **A (typed)** — `catch (SdkException<CreateCustomerError> ex)`; accessors: `ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `ex.Error.TryGetRawError(out RawError)` [fallback]. A 422 here (incl. a double-click race on the same `reference`) means "create did not happen" → **re-run OP-3 and use the winner's customer id** (the typed 422 payload `CustomerErrorResponse1.Errors: Errors?` only models `per_page`/`price_point` keys — read the meaningful body via `TryGetRawError`/`ReadAsString` when you need the message) |
| Source | `map/operations/Customers.md` (CreateCustomer); `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`; error payload rows `records-2-Cr-Ne.md` (`CustomerErrorResponse1`, `Errors`) |

#### OP-5 create the subscription (no payment profile)

| | |
|---|---|
| Controller property / op | `client.Subscriptions.CreateSubscription(...)` |
| Signature | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| Request model | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription !req` |
| `CreateSubscription` members we set (wire name) | `ProductHandle (product_handle): string?` — plan by **handle** (Notes: "Specify the product with `product_id` or `product_handle`") · `CustomerId (customer_id): int?` — existing Maxio customer from OP-3/OP-4 (Notes: "Identify an existing customer with `customer_id` or `customer_reference`") |
| Expressing **no payment profile** | Omit **all** of: `PaymentProfileId (payment_profile_id)`, `PaymentProfileAttributes (payment_profile_attributes)`, `CreditCardAttributes (credit_card_attributes)`, `BankAccountAttributes (bank_account_attributes)` — every member of `CreateSubscription` is optional, so "no payment profile" = simply never set any of those four. Whether the site accepts it is site/product configuration (Notes: "Payment information may be required … depending on the options for the Product being subscribed") — the seeded site allows card-less signup (catalog fact; also filter plans with `Product.RequireCreditCard == false` if you want the guard). No explicit "no payment method" flag exists on the model |
| Optional (your call) | `Reference (reference): string?` subscription-level reference — usable with `FindSubscription(reference)`; per-site uniqueness not stated in the map (do not rely on it for idempotency) |
| Response envelope | `SubscriptionResponse.Subscription` (`Subscription?`, nullable) → `Subscription`: `Id (id): int?` · `State (state): SubscriptionState?` (enum table below) · `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `ProductPriceInCents (product_price_in_cents): long?` · `Currency (currency): string?` · `Product (product): Product?` (embedded name/handle) · `Customer (customer): Customer?` |
| Synchronous? | Map carries no async/pending state for this op; **surface the returned `State` verbatim**, do not assume `Active` (A3). If `Subscription.Product` is null after create, re-read via OP-6 before building the DTO (A5) |
| Error case | **A (typed)** — `catch (SdkException<CreateSubscriptionError> ex)`; accessors: `ex.Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`ErrorListResponse1.Errors: IReadOnlyList<string> !req` — e.g. unknown product handle), `ex.Error.TryGetRawError(out RawError)` [fallback] |
| Source | `map/operations/Subscriptions.md` (CreateSubscription); `records-2-Cr-Ne.md`, `records-4-Su-We.md`; `Models/CreateSubscription.cs` (full member list, source) |

#### OP-6 read a subscription (live state refresh, and post-create re-read per A5)

| | |
|---|---|
| Controller property / op | `client.Subscriptions.ReadSubscription(...)` |
| Signature | `Task<SubscriptionResponse> ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default: pass `null` |
| Response envelope | `SubscriptionResponse.Subscription` → same `Subscription` navigation as OP-5 (state, `NextAssessmentAt`, period dates, `Product`, `Currency`) |
| Error case | **B (raw)** — `catch (SdkException<RawError> ex)`; `ex.Error.StatusCode` / `ReadAsString()` |
| Source | `map/operations/Subscriptions.md` (ReadSubscription); `records-4-Su-We.md`, `records-3-Of-Su.md` |

#### OP-7 `GET /api/my-subscriptions` — list customer's subscriptions

| | |
|---|---|
| Controller property / op | `client.Customers.ListCustomerSubscriptions(...)` |
| Signature | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Call | `ListCustomerSubscriptions(customerId: <Maxio customer id from local map, or OP-3 lookup>)` |
| Response envelope | `IReadOnlyList<SubscriptionResponse>` — **each element wraps one subscription**: `element.Subscription` → same `Subscription` navigation as OP-5. Map every element to the DTO; an empty list (no subscriptions yet) is a valid 200 empty result |
| Error case | **B (raw)** — `catch (SdkException<RawError> ex)`; `ex.Error.StatusCode` / `ReadAsString()` |
| Pagination | none (returns the customer's subscriptions) |
| Source | `map/operations/Customers.md` (ListCustomerSubscriptions); `records-4-Su-We.md` |

### 4.2 Enum values actually needed

`MaxioAdvancedBilling.Models.Enums` (add `using MaxioAdvancedBilling.Models.Enums;`). Source: `map/models/enums.md`.

**`SubscriptionState`** (StringEnum — the type of `Subscription.State`):

| C# member | Wire value |
|---|---|
| `SubscriptionState.Active` | `active` |
| `SubscriptionState.Trialing` | `trialing` |
| `SubscriptionState.Pending` | `pending` |
| `SubscriptionState.FailedToCreate` | `failed_to_create` |
| `SubscriptionState.Assessing` | `assessing` |
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

(Not C# enums: compare via the static members; read/write wire mechanics per `dotnet-models`. Build the DTO
state string from the member, e.g. `state.Value` when present.)

### 4.3 Read-navigation quick map (response → member → member)

| DTO needs | Navigation | Types |
|---|---|---|
| Plan handle / name / price cents | `ProductResponse.Product` → `.Handle` / `.Name` / `.PriceInCents` (A2; fallback OP-2) | `string?` / `string?` / `long?` |
| Catalog currency | `SiteResponse.Site` → `.Currency` (A6) | `string?` |
| Subscription id / state / price / currency | `SubscriptionResponse.Subscription` → `.Id` / `.State` / `.ProductPriceInCents` / `.Currency` | `int?` / `SubscriptionState?` / `long?` / `string?` |
| Next billing / period | `.CurrentPeriodStartedAt`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt` — **`DateTimeOffset?`, not `DateTime`** (ISO-8601; convert to local UI time is the app's call) | `DateTimeOffset?` |
| Plan name on a subscription | `SubscriptionResponse.Subscription.Product` → `.Name` / `.Handle` (may be absent on create — re-read, A5) | embedded `Product?` |

### 4.4 Error-handling model (applies to every op)

Source: `sdk-map.md` §Error-handling model; core rows `Core/ErrorResponse/`.

| Fact | Value |
|---|---|
| Throw-only | Every operation throws on non-2xx; **no** `…Result` no-throw variants exist. Always wrap the call |
| Case A (typed) | `catch (SdkException<{Operation}Error> ex)` (namespace `MaxioAdvancedBilling.Core.Exceptions`); status-specific `TryGet…` accessors listed per op row; inherited `ex.Error.TryGetRawError(out RawError)` is the fallback on every typed error |
| Case B (raw) | `catch (SdkException<RawError> ex)`; `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString(): string`, `ex.Error.ReadAsJson<T>(): T?` (types in `MaxioAdvancedBilling.Core.ErrorResponse`) |
| Which ops are which | OP-2, OP-3, OP-6, OP-7 → Case B. OP-1 (404 string), OP-4 (422), OP-5 (422) → Case A |

---

## 5. REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry these skills' contents
(each governs a step above and carries defaults/mechanics the sheet must not inline):

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | §4.0 — client construction, DI registration (`AddMaxioAdvancedBillingClient`), HttpClient lifetime/reuse |
| `dotnet-authentication` | §4.0 — Basic credentials wiring and the config binding for `Maxio:ApiKey` (401 diagnosis) |
| `dotnet-calling-endpoints` | OP-1 … OP-7 — named-argument discipline, async/`ct:` usage |
| `dotnet-models` | All request construction + response envelope unrolling (`CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`, `SubscriptionResponse.Subscription`) and `SubscriptionState` (StringEnum) mechanics |
| `dotnet-configuration-resilience` | §4.0 — retry/timeout semantics, base-URL/server selection (`Maxio:Subdomain`/`Maxio:BaseUrl`), pagination loop for OP-1 |
| `dotnet-error-handling` | §4.4 — the exception boundary around every call |
| `dotnet-testing` | Stubbing the SDK for service tests (HttpClient constructor seam) |

The sheet must also flag, in its **first** revision, the two `System.Text.Json.JsonException` hazards the
error boundary must handle — the boundary is written early and a caveat that arrives later arrives too
late to shape it:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

### Source-key for §4 rows

`operations/…md` = `map/operations/{Controller}.md` under `maxio-getting-started`; `records-N-*.md`,
`enums.md`, `unions.md` = `map/models/`. "source (clone)" = the file the map row names in the pinned SDK
source (`Models/CreateSubscription.cs`, `Servers/ProductionOptions.cs`, etc.), consulted only where a map
row was truncated or a param doc was missing. `UNVERIFIED` = confirmable only against live traffic.
`YOUR CALL` = the application's decision, not an SDK fact.
