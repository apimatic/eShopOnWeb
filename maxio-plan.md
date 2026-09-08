# maxio-plan — Maxio Advanced Billing (subscription billing) for eShopOnWeb

Additive, parallel recurring-subscription billing on the existing one-time commerce app. Maxio is the
billing system of record for subscriptions only. No catalog/order changes; existing commerce untouched.
New surface: JWT-authenticated endpoints on the existing `src/PublicApi` project under `/api/`:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.
Grounding: bundled SDK map (`sdk-map.md` + `map/`), corroborated against SDK source files where map rows
exceeded display width (models named in the rows below). No Maxio API knowledge from memory.

## 1. Scope & sequence

| # | Implementation step | Maxio operations used |
|---|---|---|
| S1 | Add NuGet `AsadAli.AdvancedBilling.Sdk`; bind `Maxio:` config section; register/DI the client (`MaxioAdvancedBillingClient`) with Basic auth + server-node/base-URL selection | — (client construction only) |
| S2 | Catalog browse for `GET /api/subscription-plans`: resolve configured product family by `ProductFamilyHandle`, list its products, expose price/interval, prove the metered component (`api-call`) is present | `ProductFamilies.ListProductFamilies` · `ProductFamilies.ListProductsForProductFamily` · `Components.FindComponent` |
| S3 | Idempotent "ensure Maxio customer for the authenticated user" (shared helper): lookup by site-consistent `reference`, create when missing, reconcile the double-click race | `Customers.ReadCustomerByReference` · `Customers.CreateCustomer` |
| S4 | `POST /api/subscriptions`: validate requested plan handle + family; pre-check the user's existing subscriptions; create subscription **with no payment fields**; confirm plan/price/state/next-billing-date | `Products.ReadProductByHandle` · `Customers.ListCustomerSubscriptions` · `Subscriptions.CreateSubscription` · (optional) `Subscriptions.FindSubscription` |
| S5 | `GET /api/my-subscriptions`: customer by reference → list that customer's subscriptions → map to response DTOs | `Customers.ReadCustomerByReference` · `Customers.ListCustomerSubscriptions` |
| S6 | Error boundary + translation around every SDK call (Case A/B mechanics; both `JsonException` directions) | — |
| S7 | Tests for the service layer + endpoints | — |

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

### 2.1 Namespaces (`using` directives)

| Type(s) | Namespace |
|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `AddMaxioAdvancedBillingClient` | `MaxioAdvancedBilling` (root) |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |
| Records (all request/response models below) | `MaxioAdvancedBilling.Models` |
| Enums (`SubscriptionState`, `IntervalUnit`, `ComponentKind`, …) | `MaxioAdvancedBilling.Models.Enums` |
| Typed operation errors (`CreateCustomerError`, `CreateSubscriptionError`, `FindSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` |

### 2.2 Client construction · auth · server node · config

| Fact | Value / note | Source |
|---|---|---|
| Package id / root namespace | install `AsadAli.AdvancedBilling.Sdk`; code `using MaxioAdvancedBilling;` | `sdk-map.md` identity |
| Client ctor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — only ctor | `sdk-map.md`; `MaxioAdvancedBillingClient.cs` |
| DI | `services.AddMaxioAdvancedBillingClient(o => { … })` — registers `AddHttpClient()` + a singleton client built on a factory `HttpClient`; long-lived/reuse semantics governed by companion skill | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs` |
| Auth scheme | HTTP Basic — `BasicAuthCredentials { Username = <API key>, Password = "x" }` (password is the **literal** `x`). `Username` is `required`. Set before/inside construction or the DI callback | `sdk-map.md`; `BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`. Only US/EU hosting — there is **no sandbox/prod environment enum**; "sandbox/test" is a Maxio *site* property, not a client setting | `sdk-map.md`; `ServerEnvironment.cs` |
| Server node | `options.Server.Production.Us` holds `Site` (default `"subdomain"`) and `BaseUrl` (default template above). Defaults alone give `https://subdomain.chargify.com` — wrong until configured | `ProductionOptions.cs` |
| Base-URL derivation | If `Maxio:BaseUrl` is **empty**: `options.Server.Production.Us.Site = <Maxio:Subdomain>` → base `https://{subdomain}.chargify.com`. If `Maxio:BaseUrl` **set**: `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` verbatim (full template replacement — `Site` becomes irrelevant) | `sdk-map.md`; `ProductionOptions.cs` |
| Config keys (exactly these) | `Maxio:ApiKey` ← `MAXIO_API_KEY` (→ `BasicAuth.Username`) · `Maxio:Subdomain` ← `MAXIO_SITE_SUBDOMAIN` (→ `Site`) · `Maxio:ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY` (→ family browse filter; sandbox value `eshop-subscribe`) · `Maxio:BaseUrl` ← `MAXIO_BASE_URL` (optional; when set, use verbatim as the API base address) | YOUR CALL — config contract dictated by the brief; no map default exists for any of these keys |

### 2.3 Operations in scope

Conventions: nullable-no-default parameters **must be passed explicitly** — pass `null` to skip. Every
operation is throw-only (no `…Result` variants anywhere in this SDK). Response envelopes wrap their payload
in a single field (`ProductResponse.Product`, `CustomerResponse.Customer`, `ComponentResponse.Component`,
`SubscriptionResponse.Subscription`, `ProductFamilyResponse.ProductFamily`).

| Operation (HTTP) | Signature — must-pass-explicit params | Request body model + fields you set | Response envelope — fields the integration reads | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `ProductFamilies.ListProductFamilies` (`GET /product_families.json`) | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — call `(dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct)` | none | `IReadOnlyList<ProductFamilyResponse>`; each `ProductFamilyResponse` → `ProductFamily (product_family): ProductFamily?` (nullable). Read `ProductFamily.Handle (handle)`, `Id (id)`, `Name (name)`. Resolve the configured family by `Handle == Maxio:ProductFamilyHandle`; its `Id` is used **only at runtime** for the next call (never hard-coded) | **B** — `SdkException<RawError>`: `.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` | none | `map/operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `ProductFamilies.ListProductsForProductFamily` (`GET /product_families/{product_family_id}/products.json`) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — call `(productFamilyId: <current family id as string>, dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null, ct: ct)` | none | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product !req`. Read per product: `Handle (handle)`, `Name (name)`, `PriceInCents (price_in_cents)`, `Interval (interval)`, `IntervalUnit (interval_unit)`, `RequireCreditCard (require_credit_card)`, `ArchivedAt (archived_at)` (skip archived), `ProductFamily.Handle (product_family.handle)` (family guard) | **A** — `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback]. 404 → configured family has no products | manual `page`/`perPage` (default 1/20; advance while a page returns `perPage` items) | `map/operations/ProductFamilies.md`; `records-3-Of-Su.md` (Product, ProductResponse) |
| `Components.FindComponent` (`GET /components/lookup.json`) | `FindComponent(string handle, CancellationToken ct = default)` — `(handle: "api-call", ct: ct)` — component handle is an app constant (seeded sandbox handle), **not** one of the 4 config keys | none | `ComponentResponse.Component (component): Component !req`. Read `Handle`, `Kind (kind): ComponentKind?`, `PricePerUnitInCents (price_per_unit_in_cents)`, `ProductFamilyHandle (product_family_handle)`, `ArchivedAt`. Presence = metered component available to subscriptions on this site | **B** — `SdkException<RawError>`; 404 → component absent | none | `map/operations/Components.md`; `records-1-Ac-Cr.md` (Component) |
| `Products.ReadProductByHandle` (`GET /products/handle/{api_handle}.json`) | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none | `ProductResponse.Product (product): Product !req`. Same fields as above; used in S4 to validate the requested plan (`RequireCreditCard` **false** = payment method not required; `ProductFamily.Handle` == configured family; `ArchivedAt` null) | **B** — `SdkException<RawError>`; 404 → unknown handle → 404 to caller | none | `map/operations/Products.md`; `records-3-Of-Su.md` |
| `Customers.ReadCustomerByReference` (`GET /customers/lookup.json`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — returns a single match by unique reference | none | `CustomerResponse.Customer (customer): Customer !req`. Read `Id (id)` (for path-param calls), `Reference`. 404 = no such customer yet | **B** — `SdkException<RawError>`; detect not-found via `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `map/operations/Customers.md`; `records-2-Cr-Ne.md` |
| `Customers.CreateCustomer` (`POST /customers.json`) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateCustomerRequest` (Models): `Customer (customer): CreateCustomer !req`. `CreateCustomer` (Models): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`. Country/state must be ISO codes if supplied (omitted here). **Map Notes: "you may only create one customer for a given reference value. If provided, the reference value must be unique"** — server-enforced, the atomic backstop of the idempotent find-or-create | `CustomerResponse.Customer (customer): Customer !req` — read `Id` | **A** — `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] (double-click race lands here — **re-read by reference and continue**, never fail) · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `Customers.ListCustomerSubscriptions` (`GET /customers/{customer_id}/subscriptions.json`) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>`; each `SubscriptionResponse.Subscription (subscription): Subscription?` (**nullable** — skip null entries). Read: `Id`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Product (product): Product?` (nested, optional — presence across endpoints `UNVERIFIED`), `Reference (reference): string?`, `CreatedAt (created_at)` | **B** — `SdkException<RawError>` | none | `map/operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| `Subscriptions.CreateSubscription` (`POST /subscriptions.json`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateSubscriptionRequest` (Models): `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` (Models) — fields you set: `ProductHandle (product_handle): string?` (handle is the stable key; docs: required unless `product_id`), `CustomerReference (customer_reference): string?` (docs: identify existing customer — one of `customer_id`/`customer_reference`/`customer_attributes` required), `Reference (reference): string?` (your app key for the subscription; **no uniqueness guarantee documented**). Do **not** set any payment field (`payment_profile_id`, `credit_card_attributes`, `payment_profile_attributes`, `bank_account_attributes`, `payment_collection_method`): with `RequireCreditCard == false` on the product no payment information is needed — op Notes: "Payment information may be required… depending on the options for the Product being subscribed" | `SubscriptionResponse.Subscription (subscription): Subscription?` (**nullable**). Read: `Id`, `State`, `ProductPriceInCents` (recurring amount currently subscribed), `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Reference`, `CreatedAt` | **A** — `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; `records-2-Cr-Ne.md` (CreateSubscriptionRequest/CreateSubscription); `Models/CreateSubscription.cs` (full body) |
| `Subscriptions.FindSubscription` (`GET /subscriptions/lookup.json`) — optional dedupe probe in S4 | `FindSubscription(string? reference, CancellationToken ct = default)` | none | `SubscriptionResponse.Subscription (subscription): Subscription?` | **A** — `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] = no subscription with that reference · `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md` |

### 2.4 Confirmation mapping (what "confirm plan/price/state/next-billing-date" reads)

| Confirmation | SDK source | Source |
|---|---|---|
| Plan (name/handle) | validated product from S4 `ReadProductByHandle` (request handle + `Product.Name`); `Subscription.Product (product): Product?` nested on read-back when present (presence `UNVERIFIED`) | `records-3-Of-Su.md`; `Products.md` |
| Price | `Subscription.ProductPriceInCents` — "the recurring amount of the product (and version) currently subscribed" — cents; and/or `Subscription.Product?.PriceInCents` | `Models/Subscription.cs` |
| Interval | from the S4-validated `Product.Interval` + `IntervalUnit` (default price-point recurrence) | `Models/Product.cs` |
| State | `Subscription.State: SubscriptionState?` — enum, **not** a C# enum (see 2.6) | `enums.md` |
| Next billing date | `Subscription.CurrentPeriodEndsAt` = "end of the current (recurring) period (i.e. when the next regularly scheduled attempted charge will occur)"; `Subscription.NextAssessmentAt` diverges only when a renewal payment fails and is scheduled for auto-retry — prefer `CurrentPeriodEndsAt`, and if the state is a payment-problem state read `NextAssessmentAt` instead | `Models/Subscription.cs` |

### 2.5 Models you read/write — field detail

| Model | Members (C# name `(wire_name)`: type · required?) |
|---|---|
| `CreateCustomer` | `FirstName (first_name): string · req` · `LastName (last_name): string · req` · `Email (email): string · req` · `Reference (reference): string?` · others optional (see `records-1-Ac-Cr.md` row) |
| `CustomerResponse` | single member `Customer (customer): Customer · req` (envelope) |
| `Customer` (read) | `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` … |
| `CreateSubscription` (fields you set only) | `ProductHandle (product_handle): string?` · `CustomerReference (customer_reference): string?` · `Reference (reference): string?` |
| `Subscription` (read) | `Id (id): int?` · `State (state): SubscriptionState?` · `ProductPriceInCents (product_price_in_cents): long?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `Reference (reference): string?` · `Product (product): Product?` · `Customer (customer): Customer?` · `CreatedAt (created_at): DateTimeOffset?` |
| `SubscriptionResponse` | single member `Subscription (subscription): Subscription?` — **nullable** envelope (unlike `Customer`/`Product`/`Component` envelopes whose payload is `!req`) |
| `Product` (read) | `Handle (handle): string?` · `Name (name): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `RequireCreditCard (require_credit_card): bool?` · `ArchivedAt (archived_at): DateTimeOffset?` · `ProductFamily (product_family): ProductFamily?` |
| `ProductFamily` (read) | `Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` |
| `Component` (read) | `Handle (handle): string?` · `Kind (kind): ComponentKind?` · `PricePerUnitInCents (price_per_unit_in_cents): long?` · `ProductFamilyHandle (product_family_handle): string?` · `ArchivedAt (archived_at): DateTimeOffset?` |

### 2.6 Enum values actually needed (all `StringEnum<T>` records — compare with the static member, never a string)

**`SubscriptionState`** (state of a subscription; wire value in parens). Doc categories: Live — `Active (active)`, `Trialing (trialing)`, `Assessing (assessing)`*, `Pending (pending)`*, `Paused (paused)`; Problem — `PastDue (past_due)`, `SoftFailure (soft_failure)`, `Unpaid (unpaid)`; End-of-Life — `Canceled (canceled)`, `Expired (expired)`, `FailedToCreate (failed_to_create)`, `OnHold (on_hold)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`; plus `AwaitingSignup (awaiting_signup)` (created via `defer_signup`/`initial_billing_at` — not used here). (*`assessing`/`pending` are transient creation/assessment states — docs warn not to base access decisions on them.) → deciding which existing subscription blocks a re-subscribe is the app's call; the doc categories above are the SDK's own. Source: `map/models/enums.md`; `Models/Subscription.cs` (state docs).
**`IntervalUnit`**: `Day (day)` · `Month (month)` — plan recurrence ($/month ⇒ `Interval=1`, `IntervalUnit.Month`). Source: `enums.md`.
**`ComponentKind`**: `MeteredComponent (metered_component)`, `QuantityBasedComponent`, `OnOffComponent`, `PrepaidUsageComponent`, `EventBasedComponent` — browse uses `Kind == ComponentKind.MeteredComponent`. Source: `enums.md`.
**`CollectionMethod`** (`Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`): referenced on `Subscription.PaymentCollectionMethod` (read-only here); we do **not** set it on create. Source: `enums.md`.

## 3. Trap notes (name the hazard + consequence; the skill carries the resolution)

> ⚠ S1 (client registration/DI) — the SDK wraps an `HttpClient` whose handler pipeline must be long-lived and
> reused (factory/DI-managed), not rebuilt per request; and the client wrapper's lifetime rules differ from the
> handler's. Getting this wrong leaks sockets or doubles connection setup per call. **MUST load
> `dotnet-client-initialization`** before writing the registration.
>
> ⚠ S1 (client config/resilience) — the retry/timeout options do **not** bound a whole call and are **not** the
> timeout on the `HttpClient` you register; and a **transport** failure (`HttpRequestException`) is retried on
> **every** verb, `POST` included (`MaxRetries = 0` is rejected; floor is 1), so a single logical Subscribe can be
> sent more than once by the SDK itself. **MUST load `dotnet-configuration-resilience`** before tuning options or
> reasoning about duplicate-subscription safety.
>
> ⚠ S1 (auth) — credentials must be present before the client is used (construction or DI callback), and the
> auth check that a 401 means is not the `Environment`/server-node setting. **MUST load
> `dotnet-authentication`** before wiring `Maxio:ApiKey`.
>
> ⚠ S2–S5 (every call) — several optional parameters have no C# default and mis-bind positionally; list/search
> ops are safest called with named arguments, passing `null` for every skip-able optional and `ct:` for
> cancellation. **MUST load `dotnet-calling-endpoints`** before the first SDK call.
>
> ⚠ S2–S5 (models) — enums are `StringEnum<T>` records (not C# enums): equality/switch use the static members
> (`SubscriptionState.Active`), never `"active"`; request records are immutable with `required` members set in the
> initializer; JSON wire names differ from C# names. **MUST load `dotnet-models`** before constructing payloads or
> mapping responses.
>
> ⚠ S3–S6 (errors) — each operation is either Case A (typed `SdkException<{Op}Error>` with status-specific
> `TryGet…` accessors) or Case B (`SdkException<RawError>`); there are **no** no-throw `…Result` variants — every
> call throws. 404s on `ReadCustomerByReference`/`ReadProductByHandle` are Case-B exceptions read via
> `ex.Error.StatusCode`; do not parse exception `.ToString()`. **MUST load `dotnet-error-handling`** before writing
> any `try/catch` or the boundary (S6).
>
> ⚠ S7 (tests) — the seam to fake the SDK is the `HttpClient` constructor argument; tests should exercise the
> mapped behaviour (find→create reconciliation, duplicate handling) rather than SDK internals. **MUST load
> `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load every skill listed **before implementation starts**; the sheet deliberately does not carry their contents.

> **`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
>   deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
>   integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException`
>   *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the
>   HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a
>   deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | S1 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | S1 — supplying the Basic credentials from config, per-environment setup |
| `dotnet-configuration-resilience` | S1 — retry/timeout semantics, base-URL/server selection; S2/S4 pagination and the transport-retry-on-`POST` duplicate-execution hazard |
| `dotnet-calling-endpoints` | S2–S5 — named-argument discipline, must-pass-explicit params, envelopes, cancellation |
| `dotnet-models` | S2–S5 — building request models (`required`/`init`), response mapping, `StringEnum<T>` enums |
| `dotnet-error-handling` | S3–S6 — Case A vs Case B, the two `JsonException` rows above, status/body reading |
| `dotnet-testing` | S7 — the `HttpClient` seam and behaviour-level assertions |

## 5. Assumptions & Blockers

Assumptions (no blockers — every SDK fact the plan needs was found in the map/source):

- **Idempotency of Subscribe (never two subscriptions) is not fully provided by the SDK contract.** The map
  documents a server-enforced uniqueness constraint for the **customer** `reference` (CreateCustomer Notes) — that
  alone guarantees a double-click never yields two Maxio customers (second create 422s and is reconciled by
  re-read). For **subscriptions**, the map documents no uniqueness on `reference` and no idempotency-key parameter,
  and the SDK can auto-retry a transport-failed `POST` (see trap notes) — so "never two subscriptions" needs an
  application-side gate the SDK cannot supply (e.g. per-user-and-plan serialization/lock or an own-storage record
  written before the Maxio create, then reconciled from `ListCustomerSubscriptions`). That design is the
  implementer's; the SDK facts and their consequence are recorded above. If the provider ever rejects the second
  of two racing creates, it is a 422 `CreateSubscriptionError` (`TryGetErrorListResponse1`), which the boundary
  should translate to "already exists → re-read" rather than an outage. | YOUR CALL — not in the map
- **Subscription `reference` uniqueness**: the map states only that FindSubscription "finds a subscription by its
  reference"; it does **not** state uniqueness. Do not rely on it as the correctness gate. | YOUR CALL — not in the
  map (absence of a documented guarantee)
- **Nested `Subscription.Product`/`Customer` presence** in create/list responses is not asserted by the map (the
  member exists and is nullable on the `Subscription` record). Whether every endpoint population it is a
  live-wire fact. Directive: extract best-effort; if the nested product is absent, fall back to the validated
  plan from the S4 read (handle/name) and never block the response on it. | UNVERIFIED
- **Customer identity fields**: `CreateCustomer` requires `FirstName`, `LastName`, `Email` — the PublicApi caller
  identity (JWT) must supply all three when the customer is first created; which claim/path provides each is the
  app's identity mapping, not an SDK fact. The site-consistent key for `reference` must be stable for the user's
  lifetime (choose it accordingly; Maxio re-seeds numeric ids, so the key must not be a Maxio id). | YOUR CALL —
  not in the map
- **Sandbox "development/sandbox"**: `cp-exp-5` in the brief is a test-mode *site*; SDK environments are only
  `ServerEnvironment.Us`/`.Eu` (default `Us`, `chargify.com`). If the live sandbox base differs, `Maxio:BaseUrl`
  must carry it verbatim. | YOUR CALL — not in the map
- **Metered-component handle `api-call`** is not one of the four config keys; it is an app constant (stable sandbox
  handle). | YOUR CALL — not in the map
- Subscription state semantics for "already subscribed" filtering (which states occupy the user's slot) is the
  app's product decision, using the Live/Problem/End-of-Life categories the SDK's own state docs give (2.6).
  | YOUR CALL — not in the map
- The three endpoints, their response DTOs, status codes, and error-shape conventions belong to the existing
  `src/PublicApi` conventions (the brief dictates `/api/` + JWT); this plan supplies only the Maxio call surface
  they sit on. | YOUR CALL — not in the map
