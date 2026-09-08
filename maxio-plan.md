# Maxio Advanced Billing — recurring-subscription capability (PublicApi)

Contract sheet + plan for the additive parallel recurring-subscription feature in `src/PublicApi`
(eShopOnWeb ASP.NET Core API, JWT-authenticated). Maxio SDK: `AsadAli.AdvancedBilling.Sdk`
(namespace `MaxioAdvancedBilling`). This file is the contract source for the main agent — it may
**not** open the SDK map itself; it loads the `dotnet-*` skills named in §5 and asks the warm
maxio-sdk agent for any missing fact.

---

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Add the package + central version (`net8.0` host, CPM repo) | — (build wiring) |
| 2 | Register/configure the client from `Maxio:*` config (auth, site, optional base-URL override) | — |
| 3 | Catalog browse: plans/price/interval/handle for the family | `client.ProductFamilies.ListProductsForProductFamily` |
| 4 | Idempotent customer ensure (lookup by reference → create if absent) | `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer` |
| 5 | Enroll/subscribe card-less by product handle (+ duplicate-detection story) | `client.Customers.ListCustomerSubscriptions`, `client.Subscriptions.CreateSubscription`, (`client.Subscriptions.FindSubscription` — optional reference pre-check) |
| 6 | Confirm & report: plan name, price + currency, state, next billing date | `client.Subscriptions.ReadSubscription` |
| 7 | My subscriptions list for the app user | `client.Customers.ListCustomerSubscriptions` |
| 8 | Error boundary + tests | — |

---

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

### 2.1 Type-namespace key (each `using` is separate; child namespaces are not transitive)

| Contents | Namespace | Types used in this plan |
|---|---|---|
| Client, options, DI extension, `ServerOptions` | `MaxioAdvancedBilling` | `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServiceCollectionExtensions` |
| Controller accessors | `MaxioAdvancedBilling.Api` | `client.Customers`, `client.ProductFamilies`, `client.Subscriptions` (properties on the client) |
| Records / request+response models / error-payload records | `MaxioAdvancedBilling.Models` | `ProductResponse`, `Product`, `CustomerResponse`, `Customer`, `CreateCustomerRequest`, `CreateCustomer`, `SubscriptionResponse`, `Subscription`, `CreateSubscriptionRequest`, `CreateSubscription`, `CreateSubscriptionComponent`, `SubscriptionCustomPrice`, `ErrorListResponse1`, `CustomerErrorResponse1`, `ListProductsFilter` |
| Enums (`StringEnum<T>` / `IntEnum<T>`) | `MaxioAdvancedBilling.Models.Enums` | `SubscriptionState`, `IntervalUnit`, `CollectionMethod`, `SubscriptionInclude`, `BasicDateField`, `ListProductsInclude` |
| Unions | `MaxioAdvancedBilling.Models.AnyOf` / `.OneOf` | none used in the payloads below (error-body field `Errors` on `CustomerErrorResponse1` may be a union — read per `dotnet-models`) |
| Per-operation typed error classes | `MaxioAdvancedBilling.Errors` | `CreateCustomerError`, `CreateSubscriptionError`, `FindSubscriptionError`, `ListProductsForProductFamilyError` |
| Throw type + raw error | `MaxioAdvancedBilling.Core.Exceptions` (`SdkException<TError>`), `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`, `ApiError`) | catch clauses + `TryGetRawError` |
| Auth | `MaxioAdvancedBilling.Core.Authentication.Basic` | `BasicAuthCredentials` |
| Environment | `MaxioAdvancedBilling.Servers` | `ServerEnvironment`, `ProductionOptions` (`.Us.BaseUrl` / `.Us.Site`; its `UsOptions` is a nested class — reach it through the property, never by name) |

### 2.2 Operations

Rows below: all parameter names literal; nullable-no-default parameters must be passed explicitly
as `null` unless a value is stated. Envelope rule: a `…Response` type wraps its payload in exactly
one field (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`)
— reads go one level down.

| # | Operation · signature | Request model + fields you set (wire `name`) | Response envelope + fields you read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| 3 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Query only. `productFamilyId` accepts the family **id or its handle prefixed `handle:`** (source param doc on `Api/ProductFamilies.cs`) → pass `"handle:" + <Maxio:ProductFamilyHandle>`. Pass `null` for `dateField`, `filter`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `includeArchived`, `include` (default excludes archived). | Returns `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each `ProductResponse.Product` (`Product !req`, wire `product`) is a full `MaxioAdvancedBilling.Models.Product`. Read for render: `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?` (cents), `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`. **Product has no currency field** — currency is not renderable from the catalog; it is first available on the created subscription (§6). | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | manual `page`/`perPage` (defaults 1/20) | `operations/ProductFamilies.md` |
| 4a | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json?reference=` | Query: `reference` = the app user's stable id (`.ToString()` if a GUID). "Returns a customer by their unique reference ID. It will return a single match." | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`Customer !req`, wire `customer`). Read `Id (id): int?` (feed §5/§7 int params), `Reference (reference): string?` (echo the app user id back), plus `FirstName`, `LastName`, `Email` for display. | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — read `ex.Error.StatusCode` (404 = absent), `ex.Error.ReadAsString()` | none | `operations/Customers.md` |
| 4b | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`. `CreateCustomer` required members (must set in the initializer): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. The unique-reference field is **`Reference (reference): string?`** — set it to the app user id. Create op Notes (source doc): "you may only create one customer for a given reference value … the `reference` value must be unique." Optionals you may set: `Organization`, `Phone`, `Locale`, `CcEmails`, `Address`… `Country` (ISO-3166-1 alpha-2). | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer.Id` (`int?`) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out …RawError)` [fallback]. The 422 payload's shape is `CustomerErrorResponse1.Errors (errors): Errors?` — field type is a nested/union shape; read it via `dotnet-models` accessor rules. **The duplicate-reference rejection is expected to surface as this 422** (`UNVERIFIED` on exact payload shape) → on 422, re-run 4a and treat a hit as success. | none | `operations/Customers.md`, `records-1-Ac-Cr.md` (`CreateCustomerRequest`, `CreateCustomer`), `records-2-Cr-Ne.md` (`Customer`, `CustomerResponse`, `CustomerErrorResponse1`) |
| 5a | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `GET /customers/{customer_id}/subscriptions.json` | Path only: numeric `customerId` from §4a/4b. | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each `.Subscription` (`Subscription?`, wire `subscription`). For duplicate detection read `Subscription.Product (product): MaxioAdvancedBilling.Models.Product?` → `.Handle`, and `Subscription.State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`. | **Case B** `SdkException<RawError>` | none (one page, no params) | `operations/Customers.md` |
| 5b | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. `CreateSubscription` has **no required members** — the operative combos are documented on its fields (source `Models/CreateSubscription.cs`): `ProductHandle (product_handle): string?` — *"Required, unless a `product_id` is given instead"*; `CustomerReference (customer_reference): string?` — *"Required, unless a `customer_id` or a set of `customer_attributes` is given"*; and the app's own key `Reference (reference): string?` — *"The reference value (provided by your app) for the subscription itself"* (set a deterministic value, e.g. `{customerRef}:{productHandle}`, to enable §5c). Optional you will omit to stay card-less and plain: `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `Components`, `CouponCodes`, `ProductPricePointHandle/Id`, `CalendarBilling`, `NextBillingAt`. Optional `Currency (currency): string?` only if a non-site default is required. **No field opts into "no payment method"** — a card-less create succeeds only because the product's server-side `require_credit_card` is false (given for `eshop-pro`/`basic-plan`); the Notes on the create op say payment info "may be required … depending on the options for the Product." | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription.Id` (`int?`) — capture for §6. | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` — read this on 422 and surface it. | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` (`CreateSubscriptionRequest`, `CreateSubscription`, `ErrorListResponse1`) |
| 5c | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `GET /subscriptions/lookup.json?reference=` (optional pre-check if you key on the subscription `Reference` from 5b) | Query: `reference` — the value you stored in `CreateSubscription.Reference`. | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404 = not found] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| 6 | `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionInclude>? include, CancellationToken ct = default)` | Pass `subscriptionId` (numeric from §5b) and `include: null` (nullable, **no default** — must be passed explicitly). | `SubscriptionResponse` → `.Subscription`. Report fields: plan name `Subscription.Product (product): Product?` → `.Name`; plan price amount `Subscription.ProductPriceInCents (product_price_in_cents): long?` — *"the recurring amount of the product (and version) currently subscribed"* (cents) — NB differs from the product's current `PriceInCents` if the price changed since signup; currency `Subscription.Currency (currency): string?` (ISO 4217, e.g. `"USD"`); state `Subscription.State (state): SubscriptionState?`; next billing date `Subscription.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` — *"when the next regularly scheduled attempted charge will occur"*. | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md`, `records-3-Of-Su.md` (`Subscription`, `Product`), `records-4-Su-We.md` (`SubscriptionResponse`) |
| 7 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — same operation as 5a | Path only. | Same as 5a. Per item render: plan name `.Product.Name`; state `.State`; next billing `.CurrentPeriodEndsAt`; price `.ProductPriceInCents` + `.Currency`. | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |

### 2.3 Enum tables actually needed

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` (StringEnum; wire value shown). Member usage, equality and `.Value` mechanics are `dotnet-models` material — compare via the static members listed, never by raw string.

| Member (literal C# name) | Wire value | Meaning (from source doc on `Models/Subscription.cs`) |
|---|---|---|
| `SubscriptionState.Pending` | `pending` | Internal/transient — creation in progress. **"Do not base any access decisions in your app on this state"** |
| `SubscriptionState.Assessing` | `assessing` | Internal/transient — periodic assessment. **"Do not base any access decisions in your app on this state"** |
| `SubscriptionState.Active` | `active` | Normal, active; paid and up to date |
| `SubscriptionState.Trialing` | `trialing` | Valid trial; may transition to active once payment received when trial ends |
| `SubscriptionState.Paused` | `paused` | Internal; account in arrears |
| `SubscriptionState.PastDue` | `past_due` | Most recent payment failed; payment past due (dunning…) |
| `SubscriptionState.SoftFailure` | `soft_failure` | Assessment/processing failed for a cause the customer can't fix; retried automatically |
| `SubscriptionState.Unpaid` | `unpaid` | Retry period expired and dunning final action marked it unpaid |
| `SubscriptionState.Canceled` | `canceled` | Canceled (end of life) |
| `SubscriptionState.Expired` | `expired` | Ran its full (expiring) life cycle |
| `SubscriptionState.FailedToCreate` | `failed_to_create` | Signup failed |
| `SubscriptionState.OnHold` | `on_hold` | Billing temporarily stopped (end-of-life treatment) |
| `SubscriptionState.Suspended` | `suspended` | Prepaid subscription used up its prepayment balance |
| `SubscriptionState.TrialEnded` | `trial_ended` | No-obligation trial completed with no card on file |

`MaxioAdvancedBilling.Models.Enums.IntervalUnit` (StringEnum): `Day (day)` · `Month (month)` — `eshop-pro`/`basic-plan` are interval 1 / `Month`. Source: `map/models/enums.md`.

Which states 5a should treat as "already enrolled" is an application decision (consequence of the
transient-state caveats: `pending`/`assessing` are explicitly documented as unfit for access
decisions — the SDK will not give you a single "counts as enrolled" value).

### 2.4 Client construction, auth, server node

From `sdk-map.md` + source (`MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`):

- Only constructor: `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (root namespace). Every API group is a property: `client.Customers`, `client.ProductFamilies`, `client.Subscriptions`, …
- Options defaults (all non-null unless shown): `Environment = ServerEnvironment.Us` (i.e. `ServerEnvironment.Default()`), `Retry = RetryOptions.Default()`, `Server = new ServerOptions()`, `BasicAuth = null` (must be set).
- **Auth (HTTP Basic): `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`** — `Username` = the Maxio API key from config; `Password` = the literal string `"x"`; both members are C# `required`. Type lives in `MaxioAdvancedBilling.Core.Authentication.Basic`. Config binding key: **`Maxio:ApiKey`**.
- **Environment / sandbox:** there is no "sandbox" enum value — the sandbox site is reached as a normal US-hosted site. `ServerEnvironment.Us` (the default) → template `https://{site}.chargify.com`. Set nothing unless an EU-hosted account exists (out of scope; there is no EU config key).
- **Site:** `options.Server.Production.Us.Site = <Maxio:Subdomain>` (default in source: the literal string `"subdomain"`). Type path: `ServerOptions` (root ns, default `new()`) → `.Production` (`ProductionOptions`, `MaxioAdvancedBilling.Servers`, default `new()`) → `.Us` (nested `UsOptions`) → `.Site`.
- **Optional base-URL override, verbatim:** `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`. When set it **replaces** the `https://{site}.chargify.com` template entirely — `{site}`/`Subdomain` no longer affect the base address. If `Maxio:BaseUrl` is absent, leave the default and drive the host from `Maxio:Subdomain`.
- **Retry/timeout/`MaxRetries` semantics, `Timeout` bounds, and status-vs-transport retry gating are NOT summarized here** — see trap T1.

### 2.5 DI / HttpClient-ownership notes (registering in PublicApi)

- The SDK ships `AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — an extension member on `IServiceCollection` declared in static class `ServiceCollectionExtensions`, namespace `MaxioAdvancedBilling` (source `ServiceCollectionExtensions.cs`). Callable as `services.AddMaxioAdvancedBillingClient(o => { … })` after `using MaxioAdvancedBilling;`. It calls `services.AddHttpClient()` and registers a **singleton** `MaxioAdvancedBillingClient` built from `IHttpClientFactory.CreateClient()` with **one options instance** captured at registration — configuration is fixed for the process lifetime; there is no per-request credential/URL switching.
- Manual alternative (only constructor): `new MaxioAdvancedBillingClient(httpClient, options)`.
- HttpClient ownership/lifetime, whether to register via the extension vs manually, and handler-pool rotation are `dotnet-client-initialization` material — see T2/T3. Never create a new `HttpClient` per request yourself.

---

## 3. Trap notes

| Trap | Where it bites | Note |
|---|---|---|
| **T1 — retry semantics hide in option names.** | Step 2 client config | The SDK's retry/timeout options do **not** bound a whole call the way their names suggest, and a transport failure on a **POST** (create customer / create subscription) can be re-sent — the shape of your idempotency design depends on what actually retries. **MUST load `dotnet-configuration-resilience`** before wiring `MaxioAdvancedBillingClientOptions`. |
| **T2 — registration pattern + HttpClient lifetime.** | Step 2 DI | The signature won't tell you how to own the `HttpClient`/handler pipeline, whether the SDK client wrapper is meant to be singleton/transient, or what the DI extension does to your factory. **MUST load `dotnet-client-initialization`** before writing `Program.cs`/`Startup` wiring. |
| **T3 — auth credential placement.** | Step 2 | `BasicAuthCredentials` members are `required`; where to set credentials relative to client construction and how to read them from `Maxio:*` config (never hardcode) is governed by the auth skill. **MUST load `dotnet-authentication`**. |
| **T4 — positional calls mis-bind.** | Steps 3–7 every call | Most optional parameters have **no C# default** — a positional call drops or mis-binds them; every SDK call in the sheet must be written with **named arguments** using the literal parameter names above (and `ct:` for the token). **MUST load `dotnet-calling-endpoints`** before the first call. |
| **T5 — StringEnum + envelope + union mechanics.** | Steps 4–6 (state comparisons, `CustomerErrorResponse1.Errors`, `SubscriptionResponse.Subscription` being nullable) | Enums are not C# `enum`; "is the customer present in 4a?" must be answered from the **Case B** status, and `CustomerErrorResponse1.Errors` is typed `Errors?`, not a flat string list — how to compare states and read those shapes is skill material, not guessable. **MUST load `dotnet-models`**. |
| **T6 — JsonException reaches the boundary from two directions.** | Step 8 error boundary | See the two verbatim hazard rows in §5 — they decide whether your catch ladder can even see the failures. **MUST load `dotnet-error-handling`** before writing the boundary. |
| **T7 — what a "duplicate" create looks like.** | Steps 4–5 | Idempotency depends on classifying a 422 (customer) / any post-create state check (subscription) as "already done" — the SDK only gives you the typed accessors and the enum; whether the site really rejects a duplicate customer reference with the documented 422 is `UNVERIFIED` (see §4). Design 4a/4b so the 422 path re-reads by reference instead of failing. |
| **T8 — test seam.** | Step 8 tests | The seam for faking the SDK and which test doubles to use is skill material. **MUST load `dotnet-testing`** before writing tests. |

---

## 4. Assumptions & Blockers

**Assumptions**
1. PublicApi targets `net8.0` — the csproj has no `TargetFramework`; it inherits the property set in `Directory.Packages.props` (`<TargetFramework>net8.0</TargetFramework>`, line 4). Package to add: **`AsadAli.AdvancedBilling.Sdk`, version `1.0.2`** in **`src/PublicApi/PublicApi.csproj`** — the only project that will call the SDK (the feature lives in PublicApi). `1.0.2` is the published NuGet version whose source tag (`v1.0.2`) the SDK map is generated from (NuGet lists `1.0.0`/`1.0.1`/`1.0.2`); the package TFM is `netstandard2.0`, compatible with net8.0. Because the repo centralizes versions (`ManagePackageVersionsCentrally=true`), the version goes in `Directory.Packages.props` as `<PackageVersion Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2" />` and the csproj gets a bare `<PackageReference Include="AsadAli.AdvancedBilling.Sdk" />`. Transitive runtime deps come with the package: Polly, Microsoft.Extensions.Http, System.Net.Http.Json, System.Net.ServerSentEvents.
2. "Sandbox" is reached through `Maxio:Subdomain` + `Maxio:ApiKey` on the normal US host (no sandbox-specific environment enum exists in the SDK); `Maxio:BaseUrl`, when set, is used verbatim via `options.Server.Production.Us.BaseUrl` and wins over the subdomain-derived host.
3. The stable per-user reference for Maxio is the app user id (stringified). `UNVERIFIED`: whether the live wire really rejects a second `CreateCustomer` with the same `reference` with a 422 — the map/source assert the uniqueness constraint ("you may only create one customer for a given reference value") and the 422 accessor exists; treat any create-422 as "possibly already created" and re-read by reference rather than failing.
4. Card-less subscribe success depends on the site's product settings (`require_credit_card` false for `eshop-pro`/`basic-plan` per the brief) — no request field enables it, and the SDK Notes state payment info "may be required depending on the options for the Product." `UNVERIFIED` against live traffic. `Product.RequireCreditCard`/`RequestCreditCard` are returned by the catalog browse (step 3) and can be surfaced in the subscribe screen as a pre-check.
5. `UNVERIFIED`: whether Maxio enforces uniqueness of the subscription-level `reference` server-side. Detection-before-create (5a match on product handle + live state, optionally keyed by the deterministic `Reference` from 5b + 5c) is the documented-support path; a true in-flight double-click race also needs the app's own concurrency guard, and the set of states that count as "already enrolled" is the app's decision (transient `pending`/`assessing` are documented as unfit for access decisions).

**Blockers**
- None. Every capability the plan calls for exists in the map/source; anything only live traffic can confirm is flagged `UNVERIFIED` above with its defensive-coding consequence, not planned around.

---

## 5. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, DI registration, HttpClient ownership/lifetime |
| `dotnet-authentication` | Step 2 — Basic credential wiring (`Username` = API key, `Password` = `"x"`), config sourcing |
| `dotnet-configuration-resilience` | Step 2 — retry/timeout/base-URL semantics (trap T1) |
| `dotnet-calling-endpoints` | Steps 3–7 — named-argument calling, envelope access, cancellation |
| `dotnet-models` | Steps 4–6 — StringEnum comparisons, request-model building, union/error-body reads (trap T5) |
| `dotnet-error-handling` | Step 8 — the exception/error boundary (trap T6/T7) |
| `dotnet-testing` | Step 8 — seams and doubles (trap T8) |

Always include, verbatim, **both** of these hazard rows — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the
FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives
afterwards arrives too late to shape it.
