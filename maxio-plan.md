# Maxio Advanced Billing — integration plan & CONTRACT SHEET (eShopOnWeb)

Scope: additive recurring-subscription billing on `src/PublicApi`, Maxio Advanced Billing as system of
record. Grounded against the bundled SDK map (pages cited per row) and, where the map does not carry the
body, against the SDK source at the revision the map was generated from (tag `v1.0.2`, commit `15db14b`);
source-grounded rows cite the SDK file by name (e.g. `ServerOptions.cs`).

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Bind `Maxio:` config, register the SDK client in DI (`Program.cs` of `src/PublicApi`) | — (`AddMaxioAdvancedBillingClient`) |
| 2 | Resolve the configured product family by **handle** → numeric family id | `client.ProductFamilies.ListProductFamilies` |
| 3 | `GET /api/subscription-plans`: list non-archived products of that family, project handle/name/description/price/interval | `client.ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` (a): find-or-create the Maxio customer for the caller, keyed on a stable `reference` | `client.Customers.ReadCustomerByReference` → on not-found `client.Customers.CreateCustomer` → on 422 re-read by reference |
| 5 | `POST /api/subscriptions` (b): idempotency — does this customer already have an active subscription (to this product, and in general)? | `client.Customers.ListCustomerSubscriptions` (client-side filter on `State` / `Product.Handle`) |
| 6 | `POST /api/subscriptions` (c): create the subscription from the product **handle** + customer reference, **plus a future `NextBillingAt`** so no payment is captured at signup (§2.6a) | `client.Subscriptions.CreateSubscription` |
| 7 | `POST /api/subscriptions` (d) + `GET /api/my-subscriptions`: project the subscription DTO (plan name/handle, price, state, next billing date, id) | `client.Customers.ListCustomerSubscriptions` (+ optional `client.Subscriptions.ReadSubscription` for a single id) |
| 8 | Error boundary for all three endpoints | see §2.7 and the REQUIRED READING hazard rows |

Notes on sequencing that the SDK forces:

- **There is no "get product family by handle" operation.** `ReadProductFamily` takes `int id` (see §2.4), so
  the `handle:my-family` form the provider's prose mentions is **not expressible** through that method.
  Resolve the handle by listing families and matching `ProductFamily.Handle` client-side (step 2), then reuse
  the numeric id for step 3. Cache the resolved id for the process lifetime if you want; that is your call.
- **There is no customer-id filter on `ListSubscriptions`.** The customer-scoped list is on the *Customers*
  controller (`ListCustomerSubscriptions`), takes only `customerId`, and has **no state filter and no
  pagination** — filter by `State` in your own code.

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

### 2.1 Package, namespaces, client types

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` (SDK identity) |
| Package **version** | The sheet is grounded on source tag `v1.0.2` (commit `15db14b`); that revision's own `.csproj` declares `<Version>1.0.0</Version>`, so the published NuGet version for this revision cannot be settled offline. **Pin an explicit `Version=` in the `PackageReference` — never float.** If the pinned version does not restore, take the highest published version and re-verify the names in this sheet against the compiler before changing any of them. | `MaxioAdvancedBilling.csproj` (SDK source) — **UNVERIFIED** |
| Root namespace (differs from package id) | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` (`sealed`) | `sdk-map.md`; `MaxioAdvancedBillingClient.cs` |
| Only constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — no parameterless/builder form exists | `sdk-map.md`; `MaxioAdvancedBillingClient.cs` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` (plain class, `get;set;` properties, object-initializer or callback) | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs` |
| SDK target framework / transitive deps | `netstandard2.0`; pulls `Microsoft.Extensions.Http 10.0.8`, `Polly 8.6.5`, `System.Net.Http.Json 10.0.8`, `System.Net.ServerSentEvents 10.0.8` | `sdk-map.md`; `MaxioAdvancedBilling.csproj` |

`using` directives, per type kind (C# does **not** import child namespaces transitively):

| Contents you touch | Namespace |
|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `AddMaxioAdvancedBillingClient` | `MaxioAdvancedBilling` |
| `ServerEnvironment`, `ProductionOptions` (+ nested `ProductionOptions.UsOptions` / `.EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `RetryOptions`, `RetryAttempt` | `MaxioAdvancedBilling.Core.Configuration` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| Controller **types** (`Customers`, `Products`, `ProductFamilies`, `Subscriptions`) | `MaxioAdvancedBilling.Api` |
| Records (`Customer`, `Product`, `Subscription`, `CreateSubscriptionRequest`, …) | `MaxioAdvancedBilling.Models` |
| Enums (`SubscriptionState`, `IntervalUnit`, `SubscriptionStateFilter`, `BasicDateField`, …) | `MaxioAdvancedBilling.Models.Enums` |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `FindSubscriptionError`) | `MaxioAdvancedBilling.Errors` |
| Unions | **none in scope** — no operation, request or response field this plan touches is a `OneOf`/`AnyOf` (checked against `map/models/unions.md`) |

⚠ Name-collision hazard (plain C#, no skill needed): the controller **types** are called `Customers`,
`Products`, `Subscriptions` — importing `MaxioAdvancedBilling.Api` next to your own similarly named types
produces `CS0104`. You do **not** need that `using` if you only reach controllers through the client
properties (`client.Customers.…`) and hold intermediates in `var`. Likewise `MaxioAdvancedBilling.Models`
contains a record literally named `Errors`.

### 2.2 Auth (Basic) and the two ways to set the base address

Auth is HTTP **Basic**: `Username` = the Maxio API key, `Password` = the literal `"x"`.
`BasicAuthCredentials.Username`/`.Password` are `required` + `init`-only, so both must be set in the object
initializer. Source: `sdk-map.md` (Servers & auth); `Core/Authentication/Basic/BasicAuthCredentials.cs`.

> **If `options.BasicAuth` is left `null` the client silently sends no `Authorization` header at all** —
> every call then fails 401 with no client-side error. Source: `AuthSchemes.cs` +
> `Core/Authentication/Basic/BasicAuthScheme.cs` (`Create(null)` → `NoneAuthScheme.Instance`).

Server/base-URL model (source: `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Core/TemplateParamsFactory.cs`):

| Fact | Value |
|---|---|
| `MaxioAdvancedBillingClientOptions.Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` — a `StringEnum` record, **not** a C# enum. Members: `ServerEnvironment.Us` (wire `US`), `ServerEnvironment.Eu` (wire `EU`); `ServerEnvironment.Default()` returns `Us`. Property default is `ServerEnvironment.Default()`. |
| `MaxioAdvancedBillingClientOptions.Server` | `MaxioAdvancedBilling.ServerOptions` (default `new()`), with `Production: ProductionOptions` and `Ebb: EbbOptions` — both `get;set;`, both default-constructed. |
| Subdomain override point | `options.Server.Production.Us.Site` (`string`, default `"subdomain"`) |
| Base-URL override point | `options.Server.Production.Us.BaseUrl` (`string`, default `"https://{site}.chargify.com"`) |
| EU counterparts | `options.Server.Production.Eu.Site` / `.Eu.BaseUrl` (default `"https://{site}.ebilling.maxio.com"`) |
| **Is a fully verbatim custom base address possible?** | **Yes.** The base URL is expanded by plain `{site}` string replacement and then `TrimEnd('/')`-joined with the path; a URL containing no `{site}` placeholder is used **verbatim** (`Core/TemplateParamsFactory.ExpandTemplate`). So `Maxio:BaseUrl` can be assigned straight to `.Production.Us.BaseUrl`, and `Site` is then simply unused. |
| ⚠ Environment/override pairing | `Environment` selects **which** of `Us`/`Eu` is read (`ProductionOptions.Resolve`). Setting `.Us.BaseUrl` while `Environment = ServerEnvironment.Eu` has **no effect**. This plan uses `Us` throughout. |
| ⚠ Options are snapshotted | The constructor builds the server/auth/retry pipeline once from the `options` instance; mutating `options` after `new MaxioAdvancedBillingClient(...)` changes nothing. Source: `MaxioAdvancedBillingClient.cs`. |

**Complete construction code** (both cases; `Maxio:BaseUrl` unset ⇒ derive from subdomain):

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth   = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // "x" is literal
    Environment = ServerEnvironment.Us,
};

// (a) site subdomain — leaves the default template "https://{site}.chargify.com"
options.Server.Production.Us.Site = subdomain;          // e.g. "cp-exp-2"

// (b) explicit verbatim base-address override (only when Maxio:BaseUrl is non-empty)
if (!string.IsNullOrWhiteSpace(baseUrl))
    options.Server.Production.Us.BaseUrl = baseUrl;     // used verbatim; {site} optional

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

Configuration binding keys (the four the brief fixes) and where each lands:

| Binding key | Target | Default if unset |
|---|---|---|
| `Maxio:ApiKey` | `BasicAuthCredentials.Username` (password is the literal `"x"`) | none — `null` ⇒ unauthenticated calls (see warning above) |
| `Maxio:Subdomain` | `options.Server.Production.Us.Site` | SDK default is the placeholder string `"subdomain"` (`Servers/ProductionOptions.cs`) — i.e. requests would go to `https://subdomain.chargify.com`; treat a missing value as a startup failure. |
| `Maxio:BaseUrl` | `options.Server.Production.Us.BaseUrl`, only when non-empty | SDK default `"https://{site}.chargify.com"` |
| `Maxio:ProductFamilyHandle` | your own code (step 2 handle match) — no SDK surface | none |

### 2.3 DI registration & HttpClient ownership

| Fact | Value | Source |
|---|---|---|
| DI helper | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — extension on `IServiceCollection`, namespace `MaxioAdvancedBilling` | `ServiceCollectionExtensions.cs` |
| What it registers | calls `services.AddHttpClient()`, then registers **`MaxioAdvancedBillingClient` as a SINGLETON**, built from `IHttpClientFactory.CreateClient()` (the default unnamed client) | `ServiceCollectionExtensions.cs` |
| Options lifetime | the `configure` callback runs **once at registration time** against a plain `new MaxioAdvancedBillingClientOptions()`; the resulting instance is captured in the singleton factory closure. It does **not** read `IOptions<T>` and does **not** re-read configuration on reload. Bind `Maxio:` values before/inside that callback. | `ServiceCollectionExtensions.cs` |
| Consuming | inject `MaxioAdvancedBillingClient` into your services/endpoints. A singleton client is safe to inject into scoped/transient consumers; the reverse is not. | `ServiceCollectionExtensions.cs` |
| Manual alternative | if you need the `HttpClient` to come from a **named/typed** factory client (own handler, timeout, logging), skip the helper and register your own singleton that resolves `IHttpClientFactory`, calls `CreateClient("<name>")`, and news up the client with your options. | `MaxioAdvancedBillingClient.cs` |

⚠ The trade-off between "singleton client holding one `IHttpClientFactory`-created `HttpClient`" and handler
rotation/DNS staleness is a lifetime question this sheet does not settle — see the trap note in §3.

### 2.4 Operations (verbatim signatures)

Every method returns `Task<T>` (`Task` for `void` rows) and is throw-only — **this SDK generates no
`…Result`/no-throw variants for any operation**. Every signature's last parameter is `CancellationToken ct = default`;
pass `HttpContext.RequestAborted` from the endpoints. Parameters listed *before* `ct` that are nullable with
**no default must be passed explicitly** (pass `null` to skip) — call these with **named arguments**.

| # | Use | Controller property · signature (verbatim) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| O1 | Find product family by handle (step 2) | `client.ProductFamilies` · `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all five must be passed explicitly (`null`) | none (GET) | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` → **nullable, check for null** → `ProductFamily.Handle`, `.Id` | **Case B** — `SdkException<RawError>`; `Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | none — returns the whole list, no `page`/`per_page` params exist | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| O2 | List plans in the family (step 3) | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` must be passed explicitly | `productFamilyId` = the numeric family id from O1, as a string. `ListProductsFilter` (`Models/ListProductsFilter.cs`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — **no handle filter**; pass `filter: null`. Pass `includeArchived: false`. | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product !req` → see §2.5 for the `Product` fields | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [all other statuses]. ⚠ see §2.7 row E3 | manual `page` + `perPage` (defaults `1` / `20`) — loop pages until a page returns fewer than `perPage` items | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `records-2-Cr-Ne.md` |
| O3 | (alt) read one plan by handle | `client.Products` · `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none (GET) | `ProductResponse` → `.Product` (`!req`) | **Case B** — `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, …) | none | `operations/Products.md` |
| O4 | (alt) list all site products | `client.Products` · `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **note the parameter ORDER differs from O2** (`endDate`/`endDatetime` come before `startDate`/`startDatetime`) | as O2 | `IReadOnlyList<ProductResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Products.md` |
| O5 | Look up the caller's customer by stable reference (step 4) | `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query param `reference`) | none (GET) | `CustomerResponse.Customer (customer): Customer !req` → `Customer.Id`, `.Email`, `.Reference` | **Case B** — `SdkException<RawError>`; read `Error.StatusCode`. **Not-found is an exception, not a null result.** | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| O6 | Create the customer (step 4) | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. `CreateCustomer` **required**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional ones this plan sets: `Reference (reference): string?`. Other optionals available: `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`. | `CustomerResponse` → `.Customer` (`!req`) → `.Id`, `.Reference` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [other]. ⚠ see §2.7 rows E1/E2 | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| O7 | (fallback) find customer by email | `client.Customers` · `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — the 7 params `direction`…`q` must be passed explicitly; **note the date params are `string?` here**, unlike O1/O2 | `q` = free-text search (Notes: matches email, id, organization, your reference value, first/last name) — it is a **search, not an exact match**: re-check `Customer.Email` yourself, case-insensitively | `IReadOnlyList<CustomerResponse>` → `.Customer` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (defaults `1` / `50`) | `operations/Customers.md` |
| O8 | The caller's subscriptions (steps 5 & 7) | `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (GET) | `IReadOnlyList<SubscriptionResponse>`; `SubscriptionResponse.Subscription (subscription): Subscription?` → **nullable, check for null** → see §2.5 | **Case B** — `SdkException<RawError>` | **none** — no `page`/`per_page`, no `state` filter; filter on `Subscription.State` and `Subscription.Product.Handle` in your own code | `operations/Customers.md`, `records-4-Su-We.md`, `records-3-Of-Su.md` |
| O9 | Create the subscription (step 6) | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`. See §2.6 for the legal field combinations. | `SubscriptionResponse` → `.Subscription` (nullable) | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [other]. `ErrorListResponse1`: `Errors (errors): IReadOnlyList<string> !req` | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-1-Ac-Cr.md` |
| O10 | Read one subscription by id | `client.Subscriptions` · `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed explicitly (`null`) | none (GET) | `SubscriptionResponse` → `.Subscription` | **Case B** — `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| O11 | (optional) lookup a subscription by your own reference | `client.Subscriptions` · `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed explicitly | none (GET, query param `reference`) | `SubscriptionResponse` | **Case A** — `SdkException<FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [other]. ⚠ on 404 `TryGetRawError` returns **false** (see §2.7 row E1) | none | `operations/Subscriptions.md` |
| O12 | (not used — for completeness) site-wide subscription list | `client.Subscriptions` · `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params must be passed explicitly | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Subscriptions.md` |

⚠ **`ReadProductFamily(int id, CancellationToken ct = default)` cannot take a handle** — the parameter is
`int`, so the provider's `handle:my-family` path form is unreachable from C#. (`operations/ProductFamilies.md`.)
Passing `"handle:eshop-subscribe"` as O2's `string productFamilyId` is *syntactically* possible, but the SDK
URL-escapes path template values (`Core/TemplateParamsFactory.cs` → `Uri.EscapeDataString`, so `:` becomes
`%3A`) and whether the provider accepts that encoding is **UNVERIFIED** — do not rely on it; use the
list-and-match route of step 2.

### 2.5 Response models — exact field names

`Product` (namespace `MaxioAdvancedBilling.Models`; source `records-3-Of-Su.md` / `Models/Product.cs`) — the fields this integration reads:

| C# property (wire name) | Type | Use |
|---|---|---|
| `Id (id)` | `int?` | internal |
| `Handle (handle)` | `string?` | plan handle (`eshop-pro`, `basic-plan`) |
| `Name (name)` | `string?` | plan name |
| `Description (description)` | `string?` | plan description |
| `PriceInCents (price_in_cents)` | `long?` | price, in cents |
| `Interval (interval)` | `int?` | interval count (e.g. `1`) |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` (enum, §2.8) | `day` / `month` |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | **the only archived/availability signal on this model** — non-null ⇒ archived |
| `ProductFamily (product_family)` | `ProductFamily?` | nested family reference (`Id`, `Name`, `Handle`, `AccountingCode`, `Description`, `CreatedAt`, `UpdatedAt`, `ArchivedAt`) |
| `RequestCreditCard (request_credit_card)` · `RequireCreditCard (require_credit_card)` | `bool?` each | two distinct fields, and **only the second means anything here**: `request_credit_card` is documented as *"Deprecated value that can be ignored unless you have legacy hosted pages"*; `require_credit_card` is *"Boolean that controls whether a payment profile is required to be entered for customers wishing to sign up on this product."* `require_credit_card = false` does **not** mean the signup charges nothing — see §2.6a. (`Models/Product.cs`) |
| `TrialPriceInCents (trial_price_in_cents)` · `TrialInterval (trial_interval)` · `TrialIntervalUnit (trial_interval_unit)` · `InitialChargeInCents (initial_charge_in_cents)` | `long?` / `int?` / `IntervalUnit?` / `long?` | assert "no trial, no setup fee" if you want to surface it |
| `ProductPricePointId (product_price_point_id)` · `ProductPricePointHandle (product_price_point_handle)` · `ProductPricePointName (product_price_point_name)` · `DefaultProductPricePointId (default_product_price_point_id)` | `int?` / `string?` / `string?` / `int?` | price-point identity of the returned price |

> `Product` carries **no currency field** (the whole field list is on `records-3-Of-Su.md`). Formatting
> `$299.00` therefore uses your own currency choice, not an SDK value — see §2.9. A subscription *does*
> carry `Currency`.
> There is also **no boolean "available/purchasable" flag** — `ArchivedAt` is the only availability signal
> the model exposes. Filter `ArchivedAt is null` in addition to passing `includeArchived: false`.

**Price-point note.** `Product.ProductPricePointHandle` is documented (on the create side, `Models/CreateSubscription.cs`)
only as *"The user-friendly API handle of a product's particular price point."* A `uuid:…`-shaped handle is
**not described anywhere in the map or in any generated doc** — the map's only price-point classification is
the `PricePointType` enum (`Catalog (catalog)`, `Default (default)`, `Custom (custom)`, documented as
*"default: a price point that is marked as a default price for a certain product … custom: a custom price
point … catalog: a price point that is not marked as a default…"*). What the `uuid:` prefix signifies is
therefore **UNVERIFIED**. **Omitting the price point on create is correct** regardless: the `CreateSubscription`
Notes say *"To set a specific product price point, use `product_price_point_handle` or `product_price_point_id`"* —
optional, and the sibling `UpdateSubscription`/`SubscriptionProducts` Notes state that when no price-point
identifier is passed *"the new product's default price point is used"*. Do not echo a `uuid:` handle back into
a create request.

`Subscription` (source `records-3-Of-Su.md` / `Models/Subscription.cs`) — the fields this integration reads:

| C# property (wire name) | Type | Use |
|---|---|---|
| `Id (id)` | `int?` | subscription id |
| `State (state)` | `SubscriptionState?` (enum, §2.8) | subscription state |
| `PreviousState (previous_state)` | `SubscriptionState?` | — |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | **use this as the displayed "next billing date".** Field doc (`Models/Subscription.cs`): *"Timestamp relating to the end of the current (recurring) period (i.e., when the next regularly scheduled attempted charge will occur)"*. The `UpdateSubscription` Notes on `operations/Subscriptions.md` agree: the API does **not** echo `next_billing_at`, and callers are told to read `current_period_ends_at` to confirm the next billing date. |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **not always the next billing date.** Field doc: *"Timestamp that indicates when capture of payment will be tried or retried. This value will usually track the current_period_ends_at, but will diverge if a renewal payment fails and must be retried. In that case, the current_period_ends_at will advance to the end of the next period… but the next_assessment_at will be scheduled for the auto-retry time (i.e. 24 hours in the future, in some cases)"*. So prefer `CurrentPeriodEndsAt ?? NextAssessmentAt` — the reverse order shows a dunning retry time as if it were the next bill. Neither field's doc is conditioned on `payment_collection_method`. |
| `CurrentPeriodStartedAt (current_period_started_at)` · `ActivatedAt (activated_at)` · `CreatedAt (created_at)` · `CanceledAt (canceled_at)` · `ExpiresAt (expires_at)` | `DateTimeOffset?` | optional display |
| `BalanceInCents (balance_in_cents)` | `long?` | **balance** |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | the subscription's product price |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | amount due next billing |
| `Product (product)` | `Product?` | nested plan — `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` (same model as above) |
| `Customer (customer)` | `Customer?` | nested customer — `Id`, `Email`, `Reference`, `FirstName`, `LastName` |
| `Currency (currency)` | `string?` | ISO currency of the subscription |
| `Reference (reference)` | `string?` | your own reference, if you set one on create |
| `ProductPricePointId (product_price_point_id)` · `ProductPricePointType (product_price_point_type)` | `int?` / `PricePointType?` (enum, §2.8) | — |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` (enum, §2.8) | — |
| `CancelAtEndOfPeriod (cancel_at_end_of_period)` · `DelayedCancelAt (delayed_cancel_at)` · `ScheduledCancellationAt (scheduled_cancellation_at)` | `bool?` / `DateTimeOffset?` | optional display |

`Customer` (source `records-2-Cr-Ne.md` / `Models/Customer.cs`) — fields used: `Id (id): int?`,
`Email (email): string?`, `Reference (reference): string?`, `FirstName (first_name): string?`,
`LastName (last_name): string?`, `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?`.
(Everything on `Customer` is nullable — including `Id`.)

**Envelope rule — reads always go one level down:**

| Envelope | Payload property | Required? |
|---|---|---|
| `ProductResponse` | `.Product (product): Product` | `!req` — a 2xx body without `product` throws (see hazard rows) |
| `ProductFamilyResponse` | `.ProductFamily (product_family): ProductFamily?` | nullable — null-check |
| `CustomerResponse` | `.Customer (customer): Customer` | `!req` |
| `SubscriptionResponse` | `.Subscription (subscription): Subscription?` | nullable — null-check |

### 2.6 `CreateSubscription` — legal field combinations

`CreateSubscriptionRequest.Subscription` is a `CreateSubscription` (source `records-2-Cr-Ne.md` /
`Models/CreateSubscription.cs`). **The model marks NOTHING `required`** — the compiler will not stop you
sending an empty body. The fields that decide whether the call is *accepted* come from the operation's
Notes on `operations/Subscriptions.md`:

| Purpose | Field(s) (wire name) | Notes-grounded rule |
|---|---|---|
| Which product | `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?` | "Specify the product with `product_id` or `product_handle`" — this plan uses `ProductHandle` (handles are the stable identifier per the brief) |
| Which price point (optional) | `ProductPricePointHandle (product_price_point_handle): string?` / `ProductPricePointId (product_price_point_id): int?` | "To set a specific product price point…" — omit both to get the product's default price point |
| Which customer (existing) | `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` | "Identify an existing customer with `customer_id` or `customer_reference`" — send **one**, not both |
| Which customer (new) | `CustomerAttributes (customer_attributes): CustomerAttributes?` | "To create a new customer, pass customer_attributes" — mutually exclusive with the existing-customer fields; this plan does **not** use it (step 4 creates the customer explicitly so the create is idempotent on `reference`) |
| Payment | `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` | "Payment information may be required… depending on the options for the Product". The seeded plans require no payment *profile* ⇒ **omit all four**. Omitting them is necessary but **not sufficient** — see §2.6a. |
| **Suppress the charge at signup** (required for a card-free create — §2.6a) | `NextBillingAt (next_billing_at): DateTimeOffset?` | Field doc, `Models/CreateSubscription.cs`: *"(Optional) Set this attribute to a future date/time to sync imported subscriptions to your existing renewal schedule… **If you provide a next_billing_at timestamp that is in the future, no trial or initial charges will be applied when you create the subscription. In fact, no payment will be captured at all.** The first payment will be captured, according to the prices defined by the product, near the time specified by next_billing_at. **If you do not provide a value for next_billing_at, any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created.**"* Cannot be combined with `CalendarBilling` (that field's doc: *"(Optional). Cannot be used when also specifying next_billing_at"*). |
| Collection method | `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum in `MaxioAdvancedBilling.Models.Enums`) | The only documented text is: *"The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are `remittance`, `automatic`, `prepaid`."* **Neither the map Notes nor any generated doc says a non-`automatic` collection method permits creation without a payment profile.** Do not ship it as the fix on its own. |
| Deferred / awaiting-signup enrollment | `InitialBillingAt (initial_billing_at): DateTimeOffset?` · `DeferSignup (defer_signup): bool? = false` | `initial_billing_at`: *"Set this attribute to a future date/time to create a subscription in the Awaiting Signup state, rather than Active or Trialing… When the initial_billing_at date hits, the subscription will transition to the expected state… **If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled.**"* `defer_signup`: *"Set this attribute to true to create the subscription in the Awaiting Signup Date state. Use this when you want to create a subscription that has an unknown first billing date."* Both park the subscription **outside** `Active`. |
| Invoice due window | `NetTerms (net_terms): string?` — **`string?` on create, `int?` on the `Subscription` response** | *"(Optional) Default: null The number of days after renewal (on invoice billing) that a subscription is due. A value between 0 (due immediately) and 180."* Not documented as affecting whether the create is accepted. |
| Your own idempotency/lookup key (optional) | `Reference (reference): string?` | round-trips to `Subscription.Reference` and is what `FindSubscription(reference)` searches. Whether the provider *enforces* uniqueness of a subscription reference is **UNVERIFIED** — treat it as a lookup aid, not as a duplicate-prevention guarantee; the double-click guard is the pre-check in step 5. |
| Fields deliberately left out | `CouponCode`/`CouponCodes`, `Components`, `CalendarBilling`, `Metafields`, `Group`, `OfferId`, `Currency`, `AgreementAcceptance`, `SkipBillingManifestTaxes`, … | none of these is named by the Notes or by a field doc as a condition of acceptance for a plain "existing customer + product handle, no payment method" signup |

Minimal body for this integration (see §2.6a — `NextBillingAt` is what makes it acceptable without a card):

```csharp
var body = new CreateSubscriptionRequest
{
    Subscription = new CreateSubscription
    {
        ProductHandle     = planHandle,        // e.g. "eshop-pro"
        CustomerReference = customerReference,  // the same value stored as CreateCustomer.Reference
        NextBillingAt     = firstBillingUtc,    // MUST be in the future: suppresses capture at signup
    }
};
var created = await client.Subscriptions.CreateSubscription(body, ct: cancellationToken);
var subscription = created.Subscription;      // nullable — null-check
```

### 2.6a Card-free signup — why the create is refused, and what the docs actually license

A live sandbox create with `ProductHandle` + `CustomerId` and no payment fields was rejected:
`SdkException<CreateSubscriptionError>`, `TryGetErrorListResponse1` → `Errors = [ "No payment method was on
file for the $299.00 balance" ]`. The generated docs explain it exactly, and correct two readings of the
`Product` flags:

| Signal | What the generated doc says | Source |
|---|---|---|
| `Product.RequestCreditCard (request_credit_card)` = `true` | *"**Deprecated value that can be ignored** unless you have legacy hosted pages. For Public Signup Page users, read this attribute from under the signup page."* → carries no meaning for API signups | `Models/Product.cs` |
| `Product.RequireCreditCard (require_credit_card)` = `false` | *"Boolean that controls whether a payment profile is **required to be entered** for customers wishing to sign up on this product."* → it removes the requirement to *supply a profile*; it does **not** remove the charge the signup itself generates | `Models/Product.cs` |
| `CreateSubscription.NextBillingAt` omitted | *"**If you do not provide a value for next_billing_at, any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created.**"* → this is the documented cause of the 422 | `Models/CreateSubscription.cs` |
| `CreateSubscription.NextBillingAt` set to a future timestamp | *"**…no trial or initial charges will be applied when you create the subscription. In fact, no payment will be captured at all.** The first payment will be captured… near the time specified by next_billing_at."* → **the only field in this model whose documentation states that a create captures no payment** | `Models/CreateSubscription.cs` |

**Ranking of the card-free levers, by what the documentation actually states (not by plausibility):**

1. **`NextBillingAt` (future timestamp)** — documented to capture no payment at creation. First choice.
2. **`DeferSignup = true`** or **`InitialBillingAt` (future)** — documented to create the subscription in the
   **Awaiting Signup** state instead of Active/Trialing. Use only if a non-`Active` enrollment is acceptable
   to the product; `initial_billing_at` additionally documents that *"if the payment is due at the
   initial_billing_at and it fails the subscription will be immediately canceled."*
3. **`PaymentCollectionMethod`** — **not documented anywhere in the map or the generated docs as permitting a
   card-free create.** The nearest adjacent statement is on a *different* operation (`IssueInvoice` Notes,
   `operations/Subscriptions.md`'s sibling page `operations/Invoices.md`): *"For Remittance subscriptions, the
   invoice will go into 'open' status and payment won't be attempted."* — that describes invoice issuing, not
   subscription creation. Treat "remittance/invoice lets you sign up without a card" as **UNVERIFIED**: it may
   well be true of the provider, but nothing in this SDK says so. If you send it, send it *in addition to*
   `NextBillingAt`, not instead of it, and treat a still-failing create as a site-configuration question.
4. **`NetTerms`** — documented only as the due-date window for invoice billing; nothing about create-time
   acceptance.
5. **`ProductPricePointHandle`** — omitting it is correct (the price point choice does not change whether a
   balance is charged at signup); see §2.5's price-point note.

Whether the sandbox site accepts option 1 in practice is **UNVERIFIED** until the call is made — but it is
the only option the SDK's own documentation licenses. The value of `NextBillingAt` (e.g. "now + one billing
interval", derived from `Product.Interval` + `Product.IntervalUnit`, or a fixed near-future date) is
`YOUR CALL — not in the map`; the field's doc requires only that it be in the future.

**Find-or-create customer (step 4), idempotency contract:** the `CreateCustomer` Notes on
`operations/Customers.md` state *"you may only create one customer for a given reference value. If provided,
the `reference` value must be unique."* That is the SDK-side guarantee the double-click guard rests on: two
concurrent creates with the same `reference` cannot both succeed. Sequence: `ReadCustomerByReference` →
on not-found `CreateCustomer` with `Reference` set → **if the create fails, re-read by reference and use the
existing customer** rather than surfacing an error (see §2.7 E2 for why you must not depend on parsing the
422 body to decide this).

### 2.7 Errors — types, ladder order, and the traps

| Fact | Value | Source |
|---|---|---|
| Exception type | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` — **`sealed`, generic, derives directly from `System.Exception`** | `Core/Exceptions/SdkException.cs` |
| Its only member | `public required TError Error { get; init; }` | same |
| Typed-error base | `MaxioAdvancedBilling.Core.ErrorResponse.ApiError` (abstract) with `bool TryGetRawError(out RawError error)` | `Core/ErrorResponse/ApiError.cs` |
| Raw error | `MaxioAdvancedBilling.Core.ErrorResponse.RawError`: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>` | `Core/ErrorResponse/RawError.cs` |
| No-throw variants | **none anywhere in this SDK** — every operation throws | `sdk-map.md` |

**E1 — there is no single catchable base for SDK failures.** `SdkException<T>` is sealed and generic;
`SdkException<CreateCustomerError>` and `SdkException<RawError>` are *unrelated* closed types with no common
non-generic ancestor other than `Exception`. A ladder needs **one `catch` clause per closed generic type the
call site can throw** — Case A operations throw only their own `SdkException<{Op}Error>`, Case B operations
throw only `SdkException<RawError>`. (Source: `Core/Exceptions/SdkException.cs`; `Core/Models/ApiResult.cs`
`GetResponseOrThrow`.)

**E2 — a typed error holds EITHER the typed payload OR the raw fallback, never both.** Each generated
`{Op}Error` selects one branch by status and passes `default` for the other Optional — e.g.
`CreateCustomerError.Create`: `422 => FromJson<CustomerErrorResponse1>(…)`, `_ => FromRawBody(…)`. Consequences
you must code for:
- on the status that has a typed shape, `TryGetRawError` returns **false** — so the **HTTP status code is not
  available from the error object at all**; the fact that the typed accessor returned `true` *is* the status
  (422 for `CreateCustomerError`/`CreateSubscriptionError`, 404 for `FindSubscriptionError` via
  `TryGetNoContent`, 404 for `ListProductsForProductFamilyError` via `TryGetString`);
- on every other status only `TryGetRawError` returns `true` (and gives you `StatusCode`).
Ladder order inside one `catch`: try the status-specific `TryGet…` accessors **first**, then `TryGetRawError`,
and have a final `else` that neither assumes a status nor claims a message.
(Sources: `Errors/CreateCustomerError.cs`, `Errors/CreateSubscriptionError.cs`, `Errors/FindSubscriptionError.cs`,
`Errors/ListProductsForProductFamilyError.cs`.)

**E3 — two generated error payloads in this scope look wrong, and both destroy the status when they are.**
Judged only from what the generated code shows:
- `CreateCustomerError`'s 422 payload is `CustomerErrorResponse1 { Errors (errors): Errors? }`, and `Errors`
  declares exactly two fields: `PerPage (per_page): IReadOnlyList<string>?` and
  `PricePoint (price_point): IReadOnlyList<string>?` (`Models/Errors.cs`, `records-1-Ac-Cr.md`,
  `records-2-Cr-Ne.md`). Those are pagination/price-point field names, not customer-validation ones, and the
  same shared `Errors` record is reused elsewhere — while the *other* 422 in this scope
  (`CreateSubscriptionError`) models its body as `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }`,
  i.e. an **array**. Two generated definitions of the same `errors` key that disagree on its JSON type.
  → **Directive (defensive):** around `CreateCustomer`, catch `SdkException<CreateCustomerError>` **and**
  `System.Text.Json.JsonException`, and in **both** cases fall back to "re-read the customer by reference; if
  it now exists, use it; otherwise surface the generic failure message." Never key the duplicate-reference
  recovery on text extracted from the 422 body. Which of the two actually happens on the wire is
  **UNVERIFIED**.
- `ListProductsForProductFamilyError`'s 404 branch is `FromJson<string>(response, ct)` — it deserializes the
  404 body **as a bare JSON string** (`Errors/ListProductsForProductFamilyError.cs`). If the provider's 404
  body is a JSON object, that deserialization throws and the `SdkException` never materialises.
  → **Directive (defensive):** the plans endpoint must also catch `JsonException` around O2 and map it to the
  same "billing provider unavailable/misconfigured" result as an SdkException whose status you could not read
  — never to a success with an empty plan list. Whether the body is an object is **UNVERIFIED**.

**E4 — `SdkException<T>` never sets an exception message.** The class passes nothing to `Exception`'s
constructor, so `ex.Message` is the framework's default `"Exception of type '…' was thrown."` and
`ex.ToString()` carries no provider text. Log/return `Error.StatusCode` + `Error.ReadAsString()` (or the typed
accessor's payload); do **not** put `ex.Message` in a response body or expect it to identify the failure.
(Source: `Core/Exceptions/SdkException.cs`.)

**E5 — "not found" is an exception on the Case B lookups.** `ReadCustomerByReference` (O5) throws
`SdkException<RawError>` for any non-2xx; treat `Error.StatusCode == HttpStatusCode.NotFound` as "no customer
yet" and **rethrow every other status**. That the provider answers 404 (rather than 200 with an empty body)
for an unknown reference is **UNVERIFIED** — so also code the 2xx side defensively: if the call succeeds but
the customer payload is unusable, treat it as "no customer" only when `Customer.Id` is null, never on a
`JsonException` (which must surface as a failure, not as "create a second customer").

**E6 — 401 / wrong host are configuration-shaped, not code-shaped.** A `RawError.StatusCode ==
Unauthorized` means the API key (`Maxio:ApiKey`) or the Basic convention is wrong, or `BasicAuth` was never
set (§2.2). A 404 on *every* route means `Maxio:Subdomain` / `Maxio:BaseUrl` point at the wrong site.

### 2.8 Enums (all `StringEnum<T>` records in `MaxioAdvancedBilling.Models.Enums` — NOT C# enums)

Construct with the static members (`SubscriptionState.Active`) or `Type.FromValue("wire")`. Read the wire
string with `.Value` (`string`); `IsKnownValue()` tells you whether a deserialized value was one of the
generated members; equality is record equality, so `sub.State == SubscriptionState.Active` is the correct
comparison. Sources: `map/models/enums.md`; `Core/Enum/TypedEnum.cs`, `Core/Enum/StringEnum.cs`.

| Enum | Members (`CSharpName (wire)`) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` — documented meanings (`Models/Enums/SubscriptionState.cs`): `active` = *"A normal, active subscription. It is not in a trial and is **paid and up to date**"*; `past_due` = *"the most recent payment has failed, and payment is past due"*; `unpaid` = *"marked unpaid if the retry period expires and you have configured your Dunning settings to have a Final Action of `mark the subscription unpaid`"*; `pending`/`assessing` = internal transient states, *"Do not base any access decisions in your app on this state"*. **`awaiting_signup` is the one member the enum's doc never describes** — its meaning comes only from the `defer_signup` / `initial_billing_at` field docs (§2.6). Which state a card-free subscription lands in is **not stated** by any map or doc page — read `State` back from the create response rather than assuming `Active`. |
| `SubscriptionStateFilter` (query filter on O12 — **a different type with a different member set**) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ExpirationIntervalUnit` (type of `Product.ExpirationIntervalUnit`) | `Day (day)`, `Month (month)`, `Never (never)` |
| `PricePointType` (type of `Subscription.ProductPricePointType`) | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `CollectionMethod` (type of `Subscription.PaymentCollectionMethod`) | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `CancellationMethod` (type of `Subscription.CancellationMethod`) | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `BasicDateField` (param on O1/O2/O4/O7) | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SortingDirection` (param on O7/O12) | `Asc (asc)`, `Desc (desc)` |
| `ListProductsInclude` (param on O2/O4) | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `SubscriptionInclude` (param on O10) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` (param on O12) | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionDateField` · `SubscriptionSort` (params on O12) | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` · `SignupDate`, `PeriodStart`, `PeriodEnd`, `NextAssessment`, `UpdatedAt`, `CreatedAt`, `TotalPayments`, `Id`, `OpenBalance`, `ExpiresAt` |
| `ServerEnvironment` (in `MaxioAdvancedBilling.Servers`, not `.Models.Enums`) | `Us (US)`, `Eu (EU)`; `ServerEnvironment.Default()` ⇒ `Us` |

**Unions:** none in scope — no field or parameter this plan touches is a `OneOf`/`AnyOf`. (`map/models/unions.md`.)

### 2.9 Facts that are yours to decide, not the SDK's

| Decision | What the SDK gives you | Label |
|---|---|---|
| Caller identity → the `reference` value stored on the Maxio customer | `CreateCustomer.Reference (reference): string?`, uniqueness guaranteed per the O6 Notes; looked up by `ReadCustomerByReference` | `YOUR CALL — not in the map` |
| Which claim carries the caller's email / user id | nothing — the SDK never sees your token | `YOUR CALL — not in the map` |
| `FirstName` / `LastName` for `CreateCustomer` (both `!req`, and eShopOnWeb identity may not have them) | the SDK only requires that non-null strings are supplied | `YOUR CALL — not in the map` |
| Currency symbol/format for `PriceInCents` on a **plan** | `Product` has no currency field; only `Subscription.Currency` exists | `YOUR CALL — not in the map` |
| Route paths, DTO shapes, status codes of your three endpoints; persistence of the Maxio customer id; per-user locking on double submit | — | `YOUR CALL — not in the map` |
| `perPage` value for O2 paging and any caching of the resolved family id | defaults `page = 1`, `perPage = 20`; the accepted maximum is not documented in the map | `YOUR CALL — not in the map` (max page size **UNVERIFIED**) |

---

## 3. Trap notes

- ⚠ **Step 1 (client registration & DI).** `AddMaxioAdvancedBillingClient` hands the singleton client one
  `HttpClient` taken from `IHttpClientFactory` at construction time — whether that satisfies handler
  rotation, and what the client-vs-`HttpClient` lifetime split should be in an ASP.NET Core app, is not
  settled by the registration signature. **MUST load `dotnet-client-initialization`** before wiring the
  client into `Program.cs`.
- ⚠ **Step 1 (timeouts, retries, cancellation).** `options.Retry` is a `RetryOptions`
  (`MaxioAdvancedBilling.Core.Configuration`) whose members are **all `required`** — you cannot set one
  property in isolation; start from `RetryOptions.Default()` and `with`-copy, or set every member. What
  `RetryOptions.Timeout` actually bounds, how it relates to the timeout on the `HttpClient` you register,
  which verbs and failure kinds are actually re-sent (this matters directly for `POST /api/subscriptions`,
  where a re-sent create could produce a second subscription), and what `MaxRetries` will and will not accept
  are **not** answered by this sheet. **MUST load `dotnet-configuration-resilience`** before you register or
  tune the client.
- ⚠ **Steps 2–7 (every call).** Most list/find parameters are nullable **without** C# defaults, so a
  positional call silently mis-binds (e.g. O4's `endDate` sits where O2 has `startDate`). Named arguments,
  and the token parameter is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ **Steps 3–7 (models).** Enums here are `StringEnum<T>` records, response envelopes wrap their payload,
  and unmodeled JSON fields are dropped on deserialize — how to build request records with `required`/`init`
  members and map them onto your own DTOs safely is the skill's subject. **MUST load `dotnet-models`** before
  you construct `CreateCustomerRequest` / `CreateSubscriptionRequest` or project responses.
- ⚠ **Step 4 (find-or-create) and step 8 (boundary).** Which exception types actually reach your `catch`,
  how to read a status safely, and the ordering rules for the ladder are the skill's subject — §2.7 gives the
  types, not the boundary technique. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ **Tests.** The `HttpClient` constructor argument is the seam; how to fake it without coupling to SDK
  internals is the skill's subject. **MUST load `dotnet-testing`** before writing tests for the billing
  service.
- ⚠ **Auth.** Where credentials are set relative to client construction, and how to source the key from
  configuration rather than hardcoding it, are the skill's subject. **MUST load `dotnet-authentication`**
  before wiring `Maxio:ApiKey`.

---

## 4. REQUIRED READING

Load **all** of these **before implementation starts**. This sheet deliberately does **not** carry their
contents — it carries the contract surface only.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `MaxioAdvancedBillingClient`, HttpClient ownership and lifetimes |
| `dotnet-authentication` | Step 1 — Basic credentials, where they are set, sourcing `Maxio:ApiKey` |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, timeouts, cancellation, base-URL/server selection, pagination |
| `dotnet-calling-endpoints` | Steps 2–7 — named arguments, must-pass-explicitly params, `ct`, response envelopes |
| `dotnet-models` | Steps 3–7 — request records, `required`/`init`, `StringEnum<T>`, mapping to your DTOs |
| `dotnet-error-handling` | Steps 4 & 8 — the catch ladder, reading status/body safely, the `JsonException` rows below |
| `dotnet-testing` | Tests for the billing service — the `HttpClient` seam |

**Two mandatory hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and
they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

Both are live in this scope, not hypothetical: `ProductResponse.Product` and `CustomerResponse.Customer` are
`required` (first row), and `CreateCustomerError` / `ListProductsForProductFamilyError` deserialize their
error bodies into shapes that look wrong (§2.7 E3 — second row).

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**

1. The caller's email (and a stable user id) can be obtained from the JWT the `src/PublicApi` endpoints
   already authenticate; the exact claim is the application's to choose (§2.9). This plan assumes one
   eShopOnWeb user ⇒ one Maxio customer, keyed by a single stable `reference` string you derive from that
   identity and never change.
2. `Maxio:ProductFamilyHandle` is `eshop-subscribe` on site `cp-exp-2`, and every purchasable plan lives in
   that one family. Plans outside it are out of scope of `GET /api/subscription-plans`.
3. ~~The seeded plans genuinely require no payment method… so `CreateSubscription` is sent with no payment
   fields at all.~~ **Corrected after a live 422** (*"No payment method was on file for the $299.00
   balance"*): `require_credit_card = false` only removes the need to *enter* a payment profile — the signup
   still assesses the period's charge unless `NextBillingAt` is set to a future timestamp. The plan now sends
   `NextBillingAt`; see §2.6a for the documented basis and the ranked alternatives. Whether the sandbox
   accepts that body is **UNVERIFIED** until re-run.
4. "Already has an active subscription" is decided client-side from `Subscription.State` (§2.8) over
   `ListCustomerSubscriptions`, since that operation offers no state filter. Which states count as "active
   enough to block a second signup" (`Active` only, or also `Trialing`/`PastDue`/`AwaitingSignup`) is a
   product decision, not an SDK fact.
5. The metered component `api-call` is out of scope; no component fields are set on create.
6. The SDK's transitive dependencies are `Microsoft.Extensions.Http 10.0.8`, `Polly 8.6.5`,
   `System.Net.Http.Json 10.0.8`, `System.Net.ServerSentEvents 10.0.8`. The SDK itself targets
   `netstandard2.0`, so it is referenceable from the app's TFM, but those dependency versions may raise
   NuGet resolution/downgrade warnings against the versions eShopOnWeb already pins. Verify at restore time.

**Blockers**

- None. Every capability the three endpoints need exists in the map; the two capabilities that are *absent*
  (no product-family-by-handle read, no customer filter on `ListSubscriptions`) are worked around with
  operations that do exist (§1), not with invented data paths.
