# Maxio Advanced Billing — eShopOnWeb subscription billing (PublicApi)

Additive Maxio billing: browse plans (`GET /api/subscription-plans`), idempotent subscribe
(`POST /api/subscriptions`), my subscriptions (`GET /api/my-subscriptions`). No card capture, no 3-DS.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Add NuGet package, build/configure the client & DI registration (base URL from `Maxio:Subdomain` or verbatim `Maxio:BaseUrl`; API key via Basic auth). Load `dotnet-client-initialization`, `dotnet-configuration-resilience`, `dotnet-authentication` first. | constructor / `AddMaxioAdvancedBillingClient` |
| 2 | Error boundary + exception translation for PublicApi (both cases + JsonException rows below). Load `dotnet-error-handling` first. | — |
| 3 | `GET /api/subscription-plans` — read the family's products; read site currency; filter archived. | `ListProductsForProductFamily`, `ReadSite` |
| 4 | `POST /api/subscriptions` — idempotent customer find-or-create, plan validation by handle, subscription pre-check, create. | `ReadCustomerByReference`, `CreateCustomer`, `ReadProductByHandle`, `ListCustomerSubscriptions`, `FindSubscription`, `CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — list the user's subscriptions, filter "active" app-side. | `ListCustomerSubscriptions` |
| 6 | Tests (HttpClient seam). Load `dotnet-testing`. | all |

## 2. SDK package + client construction

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` — install by this id, **import `MaxioAdvancedBilling`** | `sdk-map.md` (identity) |
| Version | Map generated from source tag `v1.0.2`; package targets `netstandard2.0` (C# 14, nullable) ⇒ any published version works on net8.0. Install `dotnet add package AsadAli.AdvancedBilling.Sdk` (latest); if pinning, compiler is the backstop per the staleness rule. | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` (root ns `MaxioAdvancedBilling`); only ctor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options class | `MaxioAdvancedBillingClientOptions` (root ns). Members: `Environment` (`ServerEnvironment`, default Us), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`) | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| Server options | `ServerOptions` (root ns) → `Production` (`ProductionOptions`, ns `MaxioAdvancedBilling.Servers`) → `Us` (nested `UsOptions`: `BaseUrl` default `https://{site}.chargify.com`, `Site` default `"subdomain"`). EU analog exists; irrelevant here. | `sdk-map.md`, `Servers/ProductionOptions.cs`, `ServerOptions.cs` |
| Auth | HTTP **Basic**: `BasicAuthCredentials { Username = <API key>, Password = "x" }` — ns `MaxioAdvancedBilling.Core.Authentication.Basic`. Convention: username = API key, password = literal `"x"`. | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` extension, ns `MaxioAdvancedBilling`. HttpClient lifetime/registration mechanics are the skill's subject, not this table's. | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Environments | `ServerEnvironment.Us` (default) / `ServerEnvironment.Eu` (ns `MaxioAdvancedBilling.Servers`). **No sandbox environment exists** — a sandbox/test site shares the production host template; test-ness is a property of the site, not the SDK. | `sdk-map.md` |

Binding keys (`Maxio:` section) — refer by these names, never values:

| Binding key | Env var | Use | SDK target |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Basic username | `options.BasicAuth.Username` |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | derive base URL | `options.Server.Production.Us.Site` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | family handle for plan reads | string `"handle:" + value` |
| `Maxio:BaseUrl` (optional) | (optional) | when set, used **verbatim** as base address | `options.Server.Production.Us.BaseUrl` |

Base-URL rule: if `Maxio:BaseUrl` is set, assign it to `options.Server.Production.Us.BaseUrl` (no `{site}` braces in a verbatim URL ⇒ used literally). Otherwise assign `Maxio:Subdomain` to `options.Server.Production.Us.Site`, which fills the `{site}` placeholder in the Us template ⇒ `https://{subdomain}.chargify.com`. Shape:

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    Environment = ServerEnvironment.Us,
    BasicAuth = new BasicAuthCredentials { Username = config["Maxio:ApiKey"], Password = "x" },
};
if (!string.IsNullOrEmpty(config["Maxio:BaseUrl"]))
    options.Server.Production.Us.BaseUrl = config["Maxio:BaseUrl"];  // verbatim
else
    options.Server.Production.Us.Site = config["Maxio:Subdomain"];   // -> https://{site}.chargify.com
```

Client construction: `new MaxioAdvancedBillingClient(httpClient, options)`. Prefer the DI extension + long-lived `HttpClient` — see trap ⚠1.

## 3. CONTRACT SHEET

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

Namespaces for everything this integration touches (add a `using` per kind):

| Type kind | Namespace |
|---|---|
| Client, `MaxioAdvancedBillingClientOptions`, `ServerOptions` | `MaxioAdvancedBilling` |
| Controllers (`client.Products`, `client.ProductFamilies`, `client.Customers`, `client.Subscriptions`, `client.Sites`) | `MaxioAdvancedBilling.Api` |
| Records (requests/responses below) | `MaxioAdvancedBilling.Models` |
| `SubscriptionState`, `IntervalUnit` (+ all enums) | `MaxioAdvancedBilling.Models.Enums` |
| Typed errors (`…Error`) | `MaxioAdvancedBilling.Errors` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment`, `ProductionOptions` | `MaxioAdvancedBilling.Servers` |
| `RetryOptions` (only if tuning retry) | `MaxioAdvancedBilling.Core.Configuration` |

### Operations

| Operation & controller | Method signature (params in order; `M` = must pass explicitly) | Request model + fields you set | Response envelope → fields you read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| **List plans** · `client.ProductFamilies.ListProductsForProductFamily` — `GET /product_families/{product_family_id}/products.json` | `ListProductsForProductFamily(string productFamilyId, M BasicDateField? dateField, M ListProductsFilter? filter, M DateTimeOffset? startDate, M DateTimeOffset? endDate, M DateTimeOffset? startDatetime, M DateTimeOffset? endDatetime, M bool? includeArchived, M ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | none (read-only). `productFamilyId` = **`"handle:" + Maxio:ProductFamilyHandle`** — SDK doc: “Either the product family's id or its handle prefixed with `handle:`” (`Api/ProductFamilies.cs`). Pass `null` for the 8 `M` params (all nullable, **no C# default**). Note: this endpoint lists **products only** — the seeded `api-call` metered component is a sibling of products in the family and is **not** returned or touched here. | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product` (**!req** `Models/Product.cs`): `Handle (handle): string?` · `Name (name): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `ArchivedAt (archived_at): DateTimeOffset?` (skip rows where non-null) · `RequestCreditCard / RequireCreditCard (bool?)` (confirm card-free config). **No currency field on `Product`** — site currency comes from ReadSite (below). No “default product” flag on `Product` or `ProductFamily` — default is app-side by handle (see Assumptions A2). | **Case A** — `SdkException<ListProductsForProductFamilyError>` (ns `MaxioAdvancedBilling.Errors`): `ex.Error.TryGetString(out string)` [404 — unknown family handle] · `ex.Error.TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage`, defaults 1/20; sandbox family has 2 products ⇒ 1 page | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, source `Api/ProductFamilies.cs` |
| **Plan lookup by handle** (optional validate/price before subscribe) · `client.Products.ReadProductByHandle` — `GET /products/handle/{api_handle}.json` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none; `apiHandle` = plan handle verbatim (e.g. `eshop-pro`) | `ProductResponse` → `.Product` (**!req**) — fields as above (id also present, **unstable — don't persist**) | **Case B** — `SdkException<RawError>`: `ex.Error.StatusCode` (`HttpStatusCode`) · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. 404 = unknown handle | none | `operations/Products.md`, `records-3-Of-Su.md` |
| **Find customer by reference** (idempotent lookup) · `client.Customers.ReadCustomerByReference` — `GET /customers/lookup.json?reference=` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | none; `reference` = the app user's Maxio reference | `CustomerResponse` → `.Customer` (**!req**, `Models/Customer.cs`): `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` | **Case B** — `SdkException<RawError>` (accessors above). **404 = no customer yet → proceed to CreateCustomer.** 401 = auth config problem (see §5) | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| **Create customer** (idempotent create) · `client.Customers.CreateCustomer` — `POST /customers.json` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** | `CreateCustomerRequest` has exactly one member: `Customer (customer): CreateCustomer !req`. `CreateCustomer` (`Models/CreateCustomer.cs`) **required** (set in object initializer): `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req`. Set for the anchor: `Reference (reference): string?` — **server-enforced unique** (op Notes: “you may only create one customer for a given reference value” — this is the idempotency guarantee). All other fields optional; leave out. | `CustomerResponse` → `.Customer` (!req): `Id`, `Reference`, `Email` (read the created `Id`) | **Case A** — `SdkException<CreateCustomerError>`: `ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `ex.Error.TryGetRawError(out RawError)` [fallback]. **422 on create = the reference already exists** (lost a concurrent double-submit) → re-run ReadCustomerByReference and use that customer (never fail). Note: `CustomerErrorResponse1.Errors` maps to a sparse `Errors` record (`per_page`/`price_point` only) — a real 422 `{"errors":{…}}` body won't surface its message there; read the message only via `TryGetRawError().ReadAsString()` if you need text. | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| **Create subscription** · `client.Subscriptions.CreateSubscription` — `POST /subscriptions.json` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** | `CreateSubscriptionRequest` has exactly one member: `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` (`Models/CreateSubscription.cs`): **no member is C#-required** — carry the fields the op Notes tie to acceptance: identify existing customer with `CustomerReference (customer_reference): string?` **or** `CustomerId (customer_id): int?`; specify product with `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?` (we pass **handle**, stable; ids unstable). Optional but recommended for dedupe: `Reference (reference): string?` = deterministic subscription reference (see §4 idempotency). Optional: `Currency (currency): string?`. Payment fields (`payment_profile_attributes`, `credit_card_attributes`, …) **not set** — sandbox plans are configured card-free. Components list **not set** — `api-call` untouched. | `SubscriptionResponse` → `.Subscription` (**nullable `Subscription?`** — null-check; `Models/Subscription.cs`): `Id (id): int?` · `State (state): SubscriptionState?` · `Product (product): Product?` (nested → `Name`/`Handle`/`PriceInCents`/`Interval`/`IntervalUnit`) · `ProductPriceInCents (product_price_in_cents): long?` (amount actually billed) · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date) · `Currency (currency): string?` · `Customer (customer): Customer?` | **Case A** — `SdkException<CreateSubscriptionError>`: `ex.Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `ex.Error.TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req`. On 422, re-check existing subscriptions (§4) before surfacing — 422 can be “already subscribed” OR genuine validation (unknown plan handle). | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| **Find subscription by reference** (dedupe pre-check) · `client.Subscriptions.FindSubscription` — `GET /subscriptions/lookup.json?reference=` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **no default → must pass explicitly** | none | `SubscriptionResponse` → `.Subscription` (nullable, fields above) | **Case A** — `SdkException<FindSubscriptionError>`: `ex.Error.TryGetNoContent(out RawError)` [404 = not found → proceed to create] · `ex.Error.TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-4-Su-We.md` |
| **List a customer's subscriptions** (my-subscriptions) · `client.Customers.ListCustomerSubscriptions` — `GET /customers/{customer_id}/subscriptions.json` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none; `customerId` = the Maxio customer `Id` from the find-or-create step | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` (nullable) with `State`, `Product` (nested `Name`/`Handle`/`PriceInCents`), `ProductPriceInCents`, `NextAssessmentAt`, `CurrentPeriodEndsAt`, `Currency`. All states are returned — “active” filtering is app-side on `SubscriptionState` | **Case B** — `SdkException<RawError>` (404 = stale customer id; accessors above) | none | `operations/Customers.md`, `records-4-Su-We.md` |
| **Site currency** (for plan price display) · `client.Sites.ReadSite` — `GET /site.json` | `ReadSite(CancellationToken ct = default)` | none | `SiteResponse` → `.Site` (**!req**, `Models/Site.cs`): `Currency (currency): string?` (site default currency — the currency every `Product.PriceInCents` is quoted in) · `Subdomain (subdomain): string?` · `Test (test): bool?` (confirm sandbox) | **Case B** — `SdkException<RawError>` (accessors above) | none | `operations/Sites.md`, `records-3-Of-Su.md` |

### Enums needed (all in `MaxioAdvancedBilling.Models.Enums`; StringEnum wrappers, **not** C# enums — see trap ⚠4)

`IntervalUnit` — on `Product.IntervalUnit` (plan browse):

| Member | Wire value |
|---|---|
| `IntervalUnit.Day` | `day` |
| `IntervalUnit.Month` | `month` |

`SubscriptionState` — on `Subscription.State`:

| Member | Wire value |
|---|---|
| `SubscriptionState.Pending` | `pending` |
| `SubscriptionState.FailedToCreate` | `failed_to_create` |
| `SubscriptionState.Trialing` | `trialing` |
| `SubscriptionState.Assessing` | `assessing` (internal/transient — map doc: do not base access decisions on it) |
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

Casing pitfalls: C# identifiers are PascalCase (`FirstName`, `PriceInCents`, `CustomerReference`) — never write wire names (`first_name`) in code; the SDK maps via `[JsonPropertyName]`. Enum members are Pascal (`Active`), wire values snake (`active`) — construct/compare via the wrapper members (never `"active"` literals).

### 4. Idempotency anchors (facts + consequence)

- **Customer — server-guaranteed exactly-once per reference.** `ReadCustomerByReference` (404 → create) then `CreateCustomer`; a duplicate create returns 422 and you re-lookup. Two concurrent double-clicks cannot yield two customers with the same reference (CreateCustomer Notes: uniqueness enforced for `reference`). Consequence: every user maps to **one** Maxio customer via a stable reference derived from the JWT user id — never random.
- **Subscription — no documented server-side uniqueness.** `CreateSubscription` accepts an optional `Reference`, but nothing in the map claims two subscriptions with the same reference are rejected (that would only be confirmable against live traffic — `UNVERIFIED`). Consequence: make the subscription `Reference` deterministic (e.g. `{customerReference}:{planHandle}`), pre-check via `FindSubscription` + `ListCustomerSubscriptions` (skip create when a non-terminal subscription to that product exists), and on a 422 from create re-check and return the existing subscription. For the in-flight double-click window the SDK gives you no lock — serializing subscription creation per user is the application's own concurrency design (consequence of the above, not an SDK feature).
- **No payment method/card/3-DS anywhere** — not in the customer create, not in the subscription create. Card-free acceptance is a property of the sandbox product/site configuration (product `require_credit_card`/`request_credit_card` = false), not of anything the SDK call does. CreateSubscription Notes: “Payment information may be required … depending on the options for the Product” — read `RequestCreditCard`/`RequireCreditCard` off the product payload if you want a pre-flight check.

### 5. Error model & sandbox gotchas

- Throw-based SDK: on any non-2xx the operation throws `SdkException<TError>` (ns `MaxioAdvancedBilling.Core.Exceptions`). No `…Result`/no-throw variants exist. Case letter is **per operation** (rows above): plan/list/read ops here are mostly **Case B** (`TError = RawError`, ns `MaxioAdvancedBilling.Core.ErrorResponse`: `StatusCode: HttpStatusCode`, `ReadAsBytes(): ReadOnlyMemory<byte>`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`); create ops are **Case A** (typed `…Error` in `MaxioAdvancedBilling.Errors`) with the named `TryGet…` accessors plus inherited `TryGetRawError(out RawError)` fallback. Never parse `Exception.ToString()` — status and body always come through the accessors.
- **401** = auth config problem, not a call bug: Basic username must be the API key (`Maxio:ApiKey`), password the literal `"x"`. 401 on a Case A op is reachable through `TryGetRawError().StatusCode`; on Case B directly. Wrong subdomain/base URL is not a 401 (it is DNS/404/connection).
- **404 semantics are per-operation and must be mapped deliberately**: `ReadCustomerByReference` 404 ⇒ “no customer yet” (create); `ReadProductByHandle` 404 ⇒ unknown plan handle (surface 404 to caller); `FindSubscription` 404 ⇒ “no such subscription reference” (create); `ListProductsForProductFamily` 404 (`TryGetString`) ⇒ unknown family handle (config error — fail loudly).
- **422** = validation. `CreateCustomer` 422 ⇒ reference already taken (re-lookup, don't fail). `CreateSubscription` 422 ⇒ `ErrorListResponse1.Errors: IReadOnlyList<string>`; may mean already-subscribed OR invalid payload — re-check existing subscriptions before deciding. Message text for bodies that don't fit the generated error shape is only reliably readable via `TryGetRawError().ReadAsString()`.
- Subscription `Reference`-uniqueness and the live 422 wording a duplicate create returns are `UNVERIFIED` (only live traffic settles them) — the flow above treats both defensively.

## 6. Trap notes (load each named skill — the sheet deliberately does not carry their contents)

- ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs retry (and that a transport failure re-sends `POST`) decides whether your idempotency design is even reachable. **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ Step 1 (client registration) — the `HttpClient` handed to the SDK must be long-lived and reused (factory-managed); re-creating the client/`HttpClient` per request leaks sockets and defeats pooling. **MUST load `dotnet-client-initialization`** before writing the factory/DI registration.
- ⚠ Step 1–2 (credentials) — which of the two Basic credential properties receives the API key and which the literal `"x"`, and what a 401 does and does not tell you about key vs host misconfiguration. **MUST load `dotnet-authentication`** before wiring credentials or diagnosing a 401.
- ⚠ Steps 3–4 (calls) — most signatures here carry long runs of nullable parameters with **no C# default** (`ListProductsForProductFamily` has 8); a positional call silently mis-binds. Call with named arguments. **MUST load `dotnet-calling-endpoints`** before the first SDK call.
- ⚠ Step 3 (models) — enums are `StringEnum<T>` wrappers, not C# enums (build/compare via members, nullable wrappers on responses); records are `init`-only; `SubscriptionResponse.Subscription` is nullable while `CustomerResponse.Customer`/`ProductResponse.Product` are required — one code path null-checks, another doesn't. **MUST load `dotnet-models`** before building request bodies or mapping responses.
- ⚠ Steps 4–5 (create ops + error boundary) — Case A vs Case B is per operation, and the typed 422 accessors do not carry every message body this API actually sends (sparse `CustomerErrorResponse1.Errors`); reaching for `.ToString()` instead of the accessors loses the status. **MUST load `dotnet-error-handling`** before writing any try/catch.
- ⚠ Step 6 (tests) — the `HttpClient` constructor argument is the test seam; stubbing the wrong seam tests your own mocks. **MUST load `dotnet-testing`** before writing integration tests.

## 7. REQUIRED READING

Load **before implementation starts**, in the order the steps above reach them. The sheet deliberately carries none of their contents.

- `dotnet-client-initialization` · step 1 — client construction, DI registration, HttpClient lifetime
- `dotnet-configuration-resilience` · step 1 — retry/timeout semantics, base-URL/server selection, pagination
- `dotnet-authentication` · steps 1–2 — Basic auth credential wiring and 401/403 diagnosis
- `dotnet-calling-endpoints` · steps 3–5 — controller access, named-argument discipline, envelope access
- `dotnet-models` · steps 3–5 — request construction, StringEnum wrappers, required/nullable members
- `dotnet-error-handling` · step 2 — exception boundary, Case A/B, safe status/body reads
- `dotnet-testing` · step 6 — faking the SDK seam

And always, verbatim, both of these hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

## 8. Assumptions & Blockers

Assumptions about **your** application (weigh against the task — SDK facts above are already settled):

- **A1 — customer reference derivation.** The app user → Maxio mapping uses `reference` = a stable string derived from the JWT user id (email is also stored on the customer but is not the lookup key — email can change and is not unique-enforced). The `reference` string must fit Maxio constraints (plain string; uniqueness is the only documented restriction). Which JWT claim carries the user id is read from the app's own identity path — `YOUR CALL — not in the map`.
- **A2 — default plan identification.** Neither `Product` nor `ProductFamily` carries a “default product” flag, and the `Maxio:` config section has no key for a default plan handle (its keys are fixed by your brief). “Which plan is the default” is therefore app-side logic comparing the plan `handle` against the seeded stable default `eshop-pro` (a constant in app code). If the default must ever change per environment, your config section would need a new key — out of scope of this sheet's keys.
- **A3 — currency display.** `Product` has no currency; plan prices are in the site's default currency. This plan reads it once from `ReadSite().Site.Currency` and applies it to every plan row (and as the fallback currency for subscription rows that lack `Subscription.Currency`). If you prefer a hard-coded currency symbol, that is an app presentation decision.
- **A4 — card-free acceptance.** The sandbox site is configured so both plans can be subscribed with no payment method (per your brief). If Maxio still returns 422 asking for a payment profile, that is a **site/product-configuration** problem (product `require_credit_card`), not a code one — verify in the Maxio UI. Which state a card-free create lands in (`Active` vs `trialing`/`awaiting_signup` for no-trial/no-card products) is environment-dependent (`UNVERIFIED`); the endpoint surfaces whatever state Maxio returns.
- **A5 — host derivation.** The SDK knows only `{site}.chargify.com` (Us) / `{site}.ebilling.maxio.com` (Eu) — there is no separate sandbox host in the SDK. Your sandbox subdomain against the Us template is what this plan assumes; if the sandbox lives on a different host, `Maxio:BaseUrl` (verbatim override) is the escape hatch. If the sandbox site is EU-hosted, `Environment = ServerEnvironment.Eu` is the only change needed.
- **A6 — “active” filter for `GET /api/my-subscriptions`.** The list endpoint returns all states; which states count as “active” for your shoppers is an app decision (`Active` is the obvious core; `Assessing` is documented as transient — do not base access decisions on it).
- **A7 — next-billing-date field.** `Subscription` carries both `NextAssessmentAt` and `CurrentPeriodEndsAt` (both nullable `DateTimeOffset?`); this plan surfaces `NextAssessmentAt` as “next billing date” with `CurrentPeriodEndsAt` as fallback. Their exact live semantics vs one another are `UNVERIFIED` — if the returned dates look off during testing, that is a field-choice issue, not a deserialization bug.
- **A8 — scope of “one subscription per plan per user.”** The dedupe logic in §4 assumes a user may subscribe to at most one non-terminal subscription **per plan** (reference = `{customerReference}:{planHandle}`). If a user may hold the same plan twice simultaneously, the deterministic reference must change (e.g. drop the plan handle) — an app decision this plan does not make for you.

Blockers: **none** — every SDK operation the feature needs exists in the map with the shapes recorded above. (If live testing shows `CreateSubscription` rejects a card-free create for these plans, that becomes a site-configuration blocker outside the SDK, per A4.)
