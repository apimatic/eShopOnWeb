# Maxio Advanced Billing — integration plan & contract sheet (eShopOnWeb `src/PublicApi`)

Grounded against the bundled SDK map for `AsadAli.AdvancedBilling.Sdk` (source commit `15db14b`, tag
`v1.0.2`) and, where the map does not carry a full body, against the SDK source files the map's rows name.
Every row below cites the map page or the named source file it came from.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 0 | Bind `Maxio:*` configuration and register the SDK client (auth, subdomain vs. verbatim base URL, retry hooks). | — (`AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient`) |
| 1 | Resolve the configured **product family handle → numeric family id** once per process (cache it in memory; see §5 for why this step exists at all). | `client.ProductFamilies.ListProductFamilies` |
| 2 | `GET /api/subscription-plans` — list sellable plans in that family, project handle/name/description/price/interval/trial/setup. | `client.ProductFamilies.ListProductsForProductFamily` |
| 3a | `POST /api/subscriptions` — ensure the Maxio **customer** for the caller: look up by the per-user reference key first, create only when the lookup 404s, and re-read on a create conflict. | `client.Customers.ReadCustomerByReference` → `client.Customers.CreateCustomer` |
| 3b | Same request — ensure the **subscription**: list the customer's subscriptions, return an existing live one for the requested product handle if present, otherwise create. | `client.Customers.ListCustomerSubscriptions` → `client.Subscriptions.CreateSubscription` |
| 3c | Same request — project plan/price/state/next-billing-date onto the response. | (from the `Subscription` returned above) |
| 4 | `GET /api/my-subscriptions` — resolve the customer by reference, list their subscriptions, project the same shape. | `client.Customers.ReadCustomerByReference` → `client.Customers.ListCustomerSubscriptions` |
| 5 | One integration-wide error boundary translating SDK exceptions to HTTP results. | — (see §3, §4) |
| 6 | *(secondary)* Read the family's components to expose `api-call`. **In scope and cheap**, but it needs the numeric family id from step 1. | `client.Components.ListComponentsForProductFamily` |

Step 1 is not optional: **no operation in this SDK accepts a product-family handle as a typed handle
parameter.** `ReadProductFamily` takes `int id`, and `ListComponentsForProductFamily` takes
`int productFamilyId`. Only `ListProductsForProductFamily` takes the family as `string`, and the map gives no
evidence that a `handle:` value is accepted there (see §5). Resolving handle → id by listing families and
matching on `ProductFamily.Handle` is the one fully map-grounded path, and it also supplies the `int` that
step 6 requires.

Product *handles* are used directly wherever the SDK does model them: `client.Products.ReadProductByHandle`
and `CreateSubscription.ProductHandle`. No numeric product ids are needed anywhere in this feature.

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

### 2.1 Package & namespaces

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (install by this id — it is **not** the namespace) | `sdk-map.md` |
| Version pinned by the map | `v1.0.2` (source commit `15db14b`) | `sdk-map.md` |
| Target framework | `netstandard2.0` — loads fine into the `net*` `PublicApi` project | `sdk-map.md` |
| Transitive deps | `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents` | `maxio-getting-started` skill body |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Controllers (`client.X`) | `MaxioAdvancedBilling.Api` | `sdk-map.md` (namespaces table) |
| Records (`Product`, `Customer`, `Subscription`, request/response envelopes, error payloads) | `MaxioAdvancedBilling.Models` | `sdk-map.md`; `map/models/records-*.md` |
| Enums (`SubscriptionState`, `IntervalUnit`, …) | `MaxioAdvancedBilling.Models.Enums` | `map/models/enums.md` |
| Typed error classes (`CreateCustomerError`, …) | `MaxioAdvancedBilling.Errors` | `sdk-map.md` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md`; `Core/Exceptions/SdkException.cs` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md`; `Core/ErrorResponse/RawError.cs` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `RetryOptions`, `RetryAttempt` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md`; `Core/Configuration/RetryOptions.cs` |
| `ServerEnvironment`, `ProductionOptions` (+ its nested `UsOptions`/`EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| `ServerOptions`, `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServiceCollectionExtensions` | `MaxioAdvancedBilling` (root — these files sit at the repo root) | `sdk-map.md`; `ServerOptions.cs` |

C# does **not** import child namespaces transitively: a file that touches a plan projection needs
`using MaxioAdvancedBilling;`, `using MaxioAdvancedBilling.Models;`, `using MaxioAdvancedBilling.Models.Enums;`,
`using MaxioAdvancedBilling.Core.Exceptions;`, `using MaxioAdvancedBilling.Core.ErrorResponse;` and
`using MaxioAdvancedBilling.Errors;` — six separate directives, not one.

### 2.2 Client construction, auth, subdomain vs. verbatim base URL

**The only constructor** (source: `MaxioAdvancedBillingClient.cs`, cited by `sdk-map.md`):

```csharp
MaxioAdvancedBilling.MaxioAdvancedBillingClient(
    System.Net.Http.HttpClient httpClient,
    MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)
```

`MaxioAdvancedBillingClientOptions` — the **complete** property set (source: `MaxioAdvancedBillingClientOptions.cs`;
map row in `sdk-map.md`). There is no `BaseUrl`, no `Timeout` and no logging property at this level:

| Property | Type (fully qualified) | Initialized to |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new()` |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` |

`BasicAuthCredentials` has exactly two members, both `required string` and `init`-only: **`Username` = the
Maxio API key (`Maxio:ApiKey`), `Password` = the literal `"x"`** (source: `Core/Authentication/Basic/BasicAuthCredentials.cs`;
`sdk-map.md` "Servers & auth"). If `BasicAuth` is left `null` the client substitutes a no-auth scheme and
sends **no** `Authorization` header — construction still succeeds and the failure surfaces only as a runtime
401 (source: `Core/Authentication/Basic/BasicAuthScheme.cs` → `NoneAuthScheme.Instance`). Validate
`Maxio:ApiKey` at startup yourself; the SDK will not.

**`ServerOptions` shape** (source: `ServerOptions.cs`, `Servers/ProductionOptions.cs`):

```
options.Server                          : MaxioAdvancedBilling.ServerOptions
options.Server.Production               : MaxioAdvancedBilling.Servers.ProductionOptions
options.Server.Production.Us            : ProductionOptions.UsOptions  { string BaseUrl; string Site; }
options.Server.Production.Eu            : ProductionOptions.EuOptions  { string BaseUrl; string Site; }
options.Server.Ebb                      : MaxioAdvancedBilling.Servers.EbbOptions   (event ingest only — unused here)
```

Defaults, verbatim from `Servers/ProductionOptions.cs`:
`Us.BaseUrl = "https://{site}.chargify.com"`, `Us.Site = "subdomain"`;
`Eu.BaseUrl = "https://{site}.ebilling.maxio.com"`, `Eu.Site = "subdomain"`.

**(a) Subdomain-derived default** — leave `BaseUrl` alone and set the site:

```csharp
o.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us;   // also the default
o.Server.Production.Us.Site = cfg["Maxio:Subdomain"]!;               // → https://<subdomain>.chargify.com
```

**(b) Verbatim base-URL override — the hook exists.** `BaseUrl` is an ordinary string template; the URL
builder only replaces the literal token `{site}` when it is present, then trims a trailing `/` and appends the
operation path (source: `Core/TemplateParamsFactory.cs` `ExpandTemplate`, reached via `Core/UriFactory.cs`).
So a value with no `{site}` placeholder is used **verbatim**:

```csharp
if (!string.IsNullOrWhiteSpace(cfg["Maxio:BaseUrl"]))
    o.Server.Production.Us.BaseUrl = cfg["Maxio:BaseUrl"]!;          // used exactly as given
```

Two consequences that are easy to get wrong:
- `BaseUrl` and `Site` must be set on the node matching `Environment`. With `Environment = ServerEnvironment.Us`
  only `Server.Production.Us.*` is read; `Server.Production.Eu.*` is ignored (source: `ProductionOptions.Resolve`
  dispatches on the environment).
- When `Maxio:BaseUrl` is set, `Site` becomes dead configuration — nothing substitutes it, because there is no
  `{site}` token left to replace. Do not treat a set `Site` as proof the subdomain is in play.

**`ServerEnvironment` is US/EU hosting only.** It is a `StringEnum<ServerEnvironment>` with a **private**
constructor, exactly two members `Us` (`"US"`) and `Eu` (`"EU"`), a `Default()` returning `Us`, and **no public
`FromValue`** (source: `Servers/ServerEnvironment.cs`; map row in `sdk-map.md` "Servers & auth"). A Maxio
*sandbox* is a **site**, not a value of this type — it is expressed by the subdomain (or by `Maxio:BaseUrl`),
never by `Environment`. Binding a `Maxio:Environment` value of `"sandbox"` onto `ServerEnvironment` is not
expressible in this SDK; see §5.

**DI shape** (source: `ServiceCollectionExtensions.cs`, root namespace `MaxioAdvancedBilling`):

```csharp
services.AddMaxioAdvancedBillingClient(o => { /* set BasicAuth, Environment, Server, Retry here */ });
```

Facts about that extension, read from its body:
- it calls `services.AddHttpClient()` and registers `MaxioAdvancedBillingClient` as a **singleton**, built from
  `IHttpClientFactory.CreateClient()` (the default, unnamed client);
- the `Action<MaxioAdvancedBillingClientOptions>` callback is invoked **once at registration time**, against a
  plain options instance — it is not `IOptions<T>`, is not re-evaluated per scope, and never reads
  `IConfiguration` for you. Read `Maxio:ApiKey` / `Maxio:Subdomain` / `Maxio:BaseUrl` inside the callback from a
  configuration instance you already have at `Program.cs`;
- there is **no** overload that names or configures the underlying `HttpClient`. If you need a named/typed
  client, a specific handler lifetime, or your own resilience handler, register the client yourself with the
  two-argument constructor instead of using this extension.

**Thread-safety / lifetime.** `MaxioAdvancedBillingClient` is `sealed`, builds all controllers once in its
constructor, holds no mutable per-call state, and does **not** implement `IDisposable` — it neither owns nor
disposes the `HttpClient` you hand it (source: `MaxioAdvancedBillingClient.cs`). Singleton registration is
appropriate; the `HttpClient` behind it is yours to manage. Controllers (`client.Customers` etc.) are plain
properties — cache the client, not the controllers.

**Retry / timeout / logging hooks — what exists** (`RetryOptions`, source `Core/Configuration/RetryOptions.cs`;
map row in `sdk-map.md`). Every member is `required`, so you cannot object-initialize a partial instance:
start from `RetryOptions.Default()` and use a `with` expression.

| Member | Type |
|---|---|
| `StatusCodesToRetry` | `IReadOnlyList<System.Net.HttpStatusCode>` |
| `HttpMethodsToRetry` | `IReadOnlyList<System.Net.Http.HttpMethod>` |
| `MaxRetries` | `int` |
| `Delay` | `System.TimeSpan` |
| `Timeout` | `System.TimeSpan?` |
| `BackOffFactor` | `int` |
| `UseExponentialBackoff` | `bool` |
| `MaxJitter` | `System.TimeSpan` |
| `OnRetry` | `System.Action<MaxioAdvancedBilling.Core.Configuration.RetryAttempt>?` |

`RetryAttempt` members: `AttemptNumber: int`, `Delay: TimeSpan`, `Reason: RetryReason` — all `required`
(source: `Core/Configuration/RetryAttempt.cs`). `OnRetry` is the **only** callback surface the SDK exposes;
there is no request/response logging property anywhere on the options object. What these settings actually
bound, which calls they cover, and what you must still wire yourself are resolved in the companion skill — see
the trap notes in §3.

### 2.3 Operations

| # | Controller & signature (verbatim) | Request model + fields | Response envelope → payload | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| O1 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters are nullable **with no default**: pass `null, null, null, null, null` explicitly | none (query only) | `IReadOnlyList<ProductFamilyResponse>`; each item → `.ProductFamily` (`ProductFamily?` — **nullable**). Read `Id (id): int?`, `Handle (handle): string?` | **Case B** — `SdkException<RawError>`; `RawError.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | **none** — the SDK exposes no `page`/`per_page` on this op | `operations/ProductFamilies.md`; `models/records-3-Of-Su.md` |
| O2 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable with no default; pass `null`. Pass the family **id** as a string (`familyId.ToString(CultureInfo.InvariantCulture)`) | `filter`: `ListProductsFilter` = `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — all optional; pass `null` | `IReadOnlyList<ProductResponse>`; each item → `.Product` (`Product`, **`required`/non-nullable**). Fields read: see §2.4 | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback]. ⚠ the 404 branch deserializes the body as a JSON **string** — see §3 | manual `page` + `perPage` (`page` ← `page`, `per_page` ← `perPage`); wire `include_archived` ← `includeArchived` | `operations/ProductFamilies.md`; `models/records-2-Cr-Ne.md` |
| O3 | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none — `GET /products/handle/{api_handle}.json` | `ProductResponse` → `.Product` (`Product`, **`required`**) | **Case B** — `SdkException<RawError>`; `.StatusCode` distinguishes 404 from the rest | none | `operations/Products.md` |
| O4 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` | none — `GET /customers/lookup.json?reference=…` | `CustomerResponse` → `.Customer` (`Customer`, **`required`/non-nullable**) | **Case B** — `SdkException<RawError>`; read `ex.Error.StatusCode == HttpStatusCode.NotFound` for the "absent" branch, `ReadAsString()` for the body | none | `operations/Customers.md`; `models/records-1-Ac-Cr.md` |
| O5 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable with no default: pass it explicitly | `CreateCustomerRequest` = `Customer (customer): CreateCustomer` **`required`**. `CreateCustomer` **required**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional and relevant here: `Reference (reference): string?`, `Organization`, `CcEmails`, `Locale`, `Phone`, address fields, `TaxExempt (tax_exempt): bool?`, `VatNumber`, `ParentId`, `SalesforceId` | `CustomerResponse` → `.Customer`. Read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName`, `LastName`, `CreatedAt (created_at): DateTimeOffset?` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ the two are **mutually exclusive** — see §2.5 | none | `operations/Customers.md`; `models/records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| O6 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none — `GET /customers/{customer_id}/subscriptions.json` | `IReadOnlyList<SubscriptionResponse>`; each item → `.Subscription` (`Subscription?` — **nullable**, unlike the product/customer envelopes) | **Case B** — `SdkException<RawError>` | **none** — no `page`/`per_page` parameter exists on this op | `operations/Customers.md`; `models/records-4-Su-We.md` |
| O7 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable with no default: pass explicitly | `CreateSubscriptionRequest` = `Subscription (subscription): CreateSubscription` **`required`**. `CreateSubscription` marks **nothing** required — the fields that decide acceptance are listed in §2.6 | `SubscriptionResponse` → `.Subscription` (`Subscription?` — **nullable**) | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1` = `Errors (errors): IReadOnlyList<string>` **required** | none | `operations/Subscriptions.md`; `models/records-2-Cr-Ne.md` |
| O8 | `client.Subscriptions.ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 nullable params with no default | none | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Subscriptions.md` |
| O9 | `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default: pass `null` | none | `SubscriptionResponse` → `.Subscription` (nullable) | **Case B** — `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| O10 | *(secondary)* `client.Components.ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 7 params `includeArchived`…`startDatetime` are nullable with no default. **Note the date params are `string?` here**, not `DateTimeOffset?` as on the product ops | `filter`: `ListComponentsFilter` = `Ids (ids): IReadOnlyList<int>?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` | `IReadOnlyList<ComponentResponse>`; each → `.Component` (`Component`, **`required`**) | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Components.md`; `models/records-1-Ac-Cr.md` |

**Not available — do not look for it.** There is no operation that filters subscriptions by customer id or
customer reference: `ListSubscriptions` (O8) has no customer parameter of any kind, and `FindSubscription`
looks up by the *subscription's* own `reference`, not the customer's. The only customer-scoped listing is O6,
which takes the **numeric** `customerId` and offers **no** state filter and **no** pagination. Filter by state
and by product handle **client-side** over O6's result.

### 2.4 `Product` — the fields this feature binds (namespace `MaxioAdvancedBilling.Models`)

Source: `map/models/records-3-Of-Su.md`. `ProductResponse` = `Product (product): Product` **required**.

| Ask | C# property (wire name) | Type |
|---|---|---|
| handle | `Handle (handle)` | `string?` |
| name | `Name (name)` | `string?` |
| description | `Description (description)` | `string?` |
| price in cents | `PriceInCents (price_in_cents)` | `long?` |
| interval | `Interval (interval)` | `int?` |
| interval unit | `IntervalUnit (interval_unit)` | `MaxioAdvancedBilling.Models.Enums.IntervalUnit?` |
| trial price | `TrialPriceInCents (trial_price_in_cents)` | `long?` |
| trial length | `TrialInterval (trial_interval)` / `TrialIntervalUnit (trial_interval_unit)` | `int?` / `IntervalUnit?` |
| setup fee | `InitialChargeInCents (initial_charge_in_cents)` | `long?` |
| setup fee timing | `InitialChargeAfterTrial (initial_charge_after_trial)` | `bool?` |
| expiry | `ExpirationInterval (expiration_interval)` / `ExpirationIntervalUnit (expiration_interval_unit)` | `int?` / `ExpirationIntervalUnit?` |
| taxable | `Taxable (taxable)` | `bool?` |
| card required | `RequireCreditCard (require_credit_card)` and `RequestCreditCard (request_credit_card)` — **two distinct fields**, both `bool?` | `bool?` |
| id / family / price point | `Id (id): int?`, `ProductFamily (product_family): ProductFamily?`, `DefaultProductPricePointId (default_product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` | |

`PriceInCents` is `long?`, not `int` and not `decimal`: format with
`(product.PriceInCents ?? 0L) / 100m` and a currency-aware formatter. A `null` here means the field was absent
from the payload, which is not the same as `$0.00` — decide which one your API surfaces.

`ProductFamily` (nested) = `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`,
`AccountingCode`, `Description`, `CreatedAt`, `UpdatedAt`, `ArchivedAt` (source: `models/records-3-Of-Su.md`).
`ProductFamilyResponse.ProductFamily` is **`ProductFamily?`** — nullable, so step 1 must null-guard each list
element before reading `.Handle`.

### 2.5 `Customer` — the fields this feature binds

Source: `map/models/records-1-Ac-Cr.md`. `CustomerResponse` = `Customer (customer): Customer` **required**.

| Ask | C# property (wire name) | Type |
|---|---|---|
| Maxio id (needed for O6) | `Id (id)` | `int?` |
| stable per-user key | `Reference (reference)` | `string?` |
| email | `Email (email)` | `string?` |
| names | `FirstName (first_name)` / `LastName (last_name)` | `string?` / `string?` |
| org, created | `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?` | |

`Customer.Id` is `int?` while `ListCustomerSubscriptions(int customerId, …)` takes a non-nullable `int` — the
call site must handle the null (a `Customer` with no `id` is a malformed payload, not a valid state to pass on).

**Lookup semantics of O4.** `ReadCustomerByReference` is Case B: any non-2xx becomes
`SdkException<RawError>` and the status is `ex.Error.StatusCode`. The SDK does **not** return `null` for
"not found" and has no no-throw variant — the miss *must* be handled in a `catch`. Whether Maxio answers a
reference miss with 404 (rather than a 2xx carrying an empty body) is a live-wire fact the map cannot settle —
`UNVERIFIED`, see §3 for the defensive shape.

**Lookup by email** — `client.Customers.ListCustomers(SortingDirection? direction, BasicDateField? dateField,
string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1,
int? perPage = 50, CancellationToken ct = default)` with `q` = the email (`q` ← `q`; note the date params here
are `string?`, unlike the product ops). Its map Notes call `q` a **search** across email/id/organization/
reference/name and say explicitly: *"To retrieve a single, exact match by reference, use the lookup endpoint."*
So `ListCustomers` returns fuzzy multi-matches and is the **less** reliable path for idempotency; O4 by
reference is the exact-match path and is what step 3a must use. Source: `operations/Customers.md`.

**Create-customer uniqueness, straight from the operation's Notes** (`operations/Customers.md`): *"you may only
create one customer for a given `reference` value. If provided, the `reference` value must be unique."* That is
the server-side guarantee your double-click protection rests on — a duplicate create is rejected, not silently
duplicated. It also dictates the recovery: on a create failure that is not a raw-status error, re-run O4 and
use the customer that the competing request created.

### 2.6 `CreateSubscription` — what to send, and what is deliberately omitted

`CreateSubscriptionRequest` = `Subscription (subscription): CreateSubscription` **required** — that envelope
field is the *only* `required` member anywhere in this request. Inside `CreateSubscription`, **nothing is
marked required**, so `required?` selects nothing for you; the fields that decide whether the call is accepted
come from the operation's Notes (`operations/Subscriptions.md`), quoted here:

> *"Specify the product with `product_id` or `product_handle`. … Identify an existing customer with
> `customer_id` or `customer_reference`. Optionally, include an existing payment profile using
> `payment_profile_id`. To create a new customer, pass `customer_attributes`."*

| Field to send | C# property (wire name) | Type | Why |
|---|---|---|---|
| product | `ProductHandle (product_handle)` | `string?` | the Notes-named handle path — send the plan handle from the route |
| customer | `CustomerId (customer_id)` | `int?` | the id from step 3a; prefer it over `CustomerReference` because you already resolved it and it is unambiguous |
| *(alternative)* customer | `CustomerReference (customer_reference)` | `string?` | Notes-sanctioned alternative if you skip the explicit lookup — but then you lose the 404-vs-created distinction step 3a depends on |

Deliberately **omitted**, and why — all are Notes-named or payment-related optionals that the seeded plans do
not need: `PaymentProfileId`, `CreditCardAttributes`, `PaymentProfileAttributes`, `BankAccountAttributes`,
`AgreementAcceptance`, `AchAgreement` (no payment method required), `CustomerAttributes` (the customer already
exists after step 3a — sending it as well would fight the idempotency), `ProductPricePointHandle` /
`ProductPricePointId` (the product's default price point is used), `CouponCode`/`CouponCodes`, `Components`,
`Currency`, `CalendarBilling`, `NextBillingAt`/`InitialBillingAt`/`PreviousBillingAt`, `ExpiresAt`,
`PaymentCollectionMethod`, `NetTerms`, `Reference`, `Ref`, `Metafields`, `OfferId`, `PrepaidConfiguration`,
`DeferSignup` (declared `bool? = false`), `SkipBillingManifestTaxes`.

**Creating without a payment profile is legal in the request shape**: no payment field is `required`, and the
Notes state payment information is required only *"depending on the options for the Product being subscribed"*.
The seeded plans are configured with payment not required, so the minimal body is well-formed. Whether the
site accepts it is the provider's call at runtime; a rejection would arrive as the 422 in O7.

`OfferId (offer_id): OfferId?` is tagged **(union)** in the map — do not `new` it. Not needed here; if it ever
is, build it via the factories in `map/models/unions.md`.

### 2.7 `Subscription` — the fields this feature binds

Source: `map/models/records-4-Su-We.md`. `SubscriptionResponse` = `Subscription (subscription): Subscription?`
— **nullable**.

| Ask | C# property (wire name) | Type |
|---|---|---|
| id | `Id (id)` | `int?` |
| state | `State (state)` | `MaxioAdvancedBilling.Models.Enums.SubscriptionState?` |
| previous state | `PreviousState (previous_state)` | `SubscriptionState?` |
| **next billing date** | `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` |
| next assessment | `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` |
| current period start | `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` |
| nested plan | `Product (product)` | `Product?` — the full `Product` of §2.4, incl. `Handle`, `Name`, `PriceInCents` |
| nested customer | `Customer (customer)` | `Customer?` |
| price on the subscription | `ProductPriceInCents (product_price_in_cents)` | `long?` |
| amount due next cycle | `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` |
| created / updated / activated | `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `ActivatedAt (activated_at)` | `DateTimeOffset?` |
| cancellation | `CanceledAt (canceled_at): DateTimeOffset?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CancellationMethod (cancellation_method): CancellationMethod?` | |
| trial | `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)` | `DateTimeOffset?` |
| price point | `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointType (product_price_point_type): PricePointType?` | |
| own reference | `Reference (reference)` | `string?` |

**Which field is "next billing date": `CurrentPeriodEndsAt`.** The provider's own prose in the
`UpdateSubscription` Notes (`operations/Subscriptions.md`) says it outright: *"The server response will not
return data under the key/value pair of `next_billing_at`. View the key/value pair of `current_period_ends_at`
to verify that the `next_billing_at` date has been changed successfully."* There is no `NextBillingAt` property
on `Subscription` at all — only on the *request* models. Surface `CurrentPeriodEndsAt` as the next billing
date and expose `NextAssessmentAt` alongside it if you want the assessment timestamp; both are `DateTimeOffset?`
and both can be `null` (e.g. before activation), so the DTO field must be nullable too.

**Price to display.** Three candidates, all `long?` cents: `Subscription.ProductPriceInCents` (the price of the
subscribed product), `Subscription.CurrentBillingAmountInCents` (what the next bill is expected to be, after
coupons/components) and `Subscription.Product?.PriceInCents` (the catalogue price). For a plan-price display,
`ProductPriceInCents` is the subscription-level figure; fall back to `Product?.PriceInCents` when it is null.

### 2.8 Enums (namespace `MaxioAdvancedBilling.Models.Enums`) — source `map/models/enums.md`

These are **not** C# enums. They are `StringEnum<T>` records: use the static members
(`SubscriptionState.Active`), compare with `==` (record value equality), read the wire text via `.Value`
(`string`) or `.ToString()`, and note the implicit conversion to `string`. Each also exposes
`FromValue(string)`, `IsKnownValue()` and `GetKnownValues()` (source: `Core/Enum/TypedEnum.cs`,
`Core/Enum/StringEnum.cs`).

`SubscriptionState` — C# member (wire value):

`Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`,
`Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`,
`Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`,
`OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.

`IntervalUnit` — `Day (day)`, `Month (month)`. **Only two members** — there is no `Year`; an annual plan is
`interval = 12, interval_unit = month`. Render the interval as `{Interval} × {IntervalUnit}`, never assume
"month".

`ExpirationIntervalUnit` — `Day (day)`, `Month (month)`, `Never (never)`. A separate type from `IntervalUnit`;
`Product.ExpirationIntervalUnit` uses this one, `Product.IntervalUnit` and `Product.TrialIntervalUnit` use
`IntervalUnit`.

`SubscriptionStateFilter` (query filter on O8, **not** the same type as `SubscriptionState`) —
`Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`,
`OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`,
`PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`,
`Unpaid (unpaid)`. Note it has `PendingCancellation`/`PendingRenewal`/`ExpiredCards` which `SubscriptionState`
does not, and lacks `Pending`/`Assessing`/`FailedToCreate`/`SoftFailure`/`Paused`/`AwaitingSignup` which it
does — the two lists are **not** interchangeable.

`PricePointType` — `Catalog (catalog)`, `Default (default)`, `Custom (custom)`.
`CancellationMethod` — `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`,
`BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)`.
`BasicDateField` — `UpdatedAt (updated_at)`, `CreatedAt (created_at)`.
`SortingDirection` — `Asc (asc)`, `Desc (desc)`.
`ListProductsInclude` — `PrepaidProductPricePoint (prepaid_product_price_point)` (single member).
`SubscriptionInclude` — `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)`.
`SubscriptionListInclude` — `SelfServicePageToken (self_service_page_token)`.
`CollectionMethod` — `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`,
`Invoice (invoice)`.

⚠ **Unknown wire values do not throw.** `StringEnum<T>.FromValueCore` falls back to constructing an instance
around whatever string arrived (source: `Core/Enum/StringEnum.cs`), so a state Maxio adds later deserializes
into a `SubscriptionState` that is `!= ` every static member. `sub.State == SubscriptionState.Active` is safe;
an exhaustive `switch` over the members with no default arm is not. `IsKnownValue()` tells you which case
you are in.

### 2.9 Collection types & nullability traps on the models being bound

- Every collection on every model and every list-returning operation is `IReadOnlyList<T>` (or
  `IReadOnlyDictionary<string,string>` for `metadata`/`metafields`). There is **no** `List<T>`, `IList<T>` or
  array anywhere in these contracts — a `List<T>` local will not bind to a request property, and a response
  collection cannot be mutated. `Component.OveragePrices`/`Prices` are `IReadOnlyList<ComponentPrice?>?` —
  nullable list of **nullable elements**.
- Envelope nullability is **inconsistent and matters**: `ProductResponse.Product`, `CustomerResponse.Customer`
  and `ComponentResponse.Component` are `required` (non-nullable), while `SubscriptionResponse.Subscription`
  and `ProductFamilyResponse.ProductFamily` are `T?`. Null-check the latter two at every use; for the former
  three, absence of the field in the body is a **deserialization failure**, not a null (see the `JsonException`
  rows in §4).
- Almost every scalar on `Product`, `Customer` and `Subscription` is `T?` — including `Id`. `int?`→`int`
  conversions are needed before every `ListCustomerSubscriptions(customerId)` / `ReadSubscription(id)` call;
  do not paper over them with `.Value` at the boundary of an external payload.
- Records are immutable with `init`-only setters; `required` members must be set in the object initializer.
  Modify with `with` expressions, never by assignment.
- Enum-typed properties are `StringEnum` **reference** records, so `IntervalUnit?` is a nullable reference, not
  a `Nullable<T>` — there is no `.Value`/`.HasValue`; use `is not null` and the `?.` operator.
- Request models are constructed positionally-free: `new CreateSubscriptionRequest { Subscription = new CreateSubscription { … } }`.

### 2.10 Error contract (what the boundary catches)

`SdkException<TError>` (source: `Core/Exceptions/SdkException.cs`) is `sealed`, derives from
`System.Exception`, and declares **exactly one** member:

```csharp
public sealed class SdkException<TError> : Exception { public required TError Error { get; init; } }
```

It does **not** carry a status code, does not override `Message`, and there is no non-generic `SdkException`
base to catch — so `catch (SdkException<RawError>)` does **not** catch `SdkException<CreateCustomerError>`.
Each operation's error type from §2.3 needs its own catch clause (or a catch of `Exception` that inspects).

- **Case B** (`SdkException<RawError>`): status is `ex.Error.StatusCode` (`System.Net.HttpStatusCode`); body via
  `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. This is the branch that gives you a clean
  404-vs-everything-else decision.
- **Case A** (typed): `ex.Error` derives from `MaxioAdvancedBilling.Core.ErrorResponse.ApiError`, whose only
  public member is `TryGetRawError(out RawError)`; the generated subclass adds one `TryGet…` per modelled
  status.

⚠ **The typed accessor and `TryGetRawError` are mutually exclusive** (source: `Errors/CreateCustomerError.cs`,
`Errors/CreateSubscriptionError.cs`, `Errors/FindSubscriptionError.cs` — the constructor sets either the typed
`Optional` or the raw fallback, never both). Concretely, on `CreateCustomer`:

| HTTP status | Which accessor returns `true` | Can you read the status code? |
|---|---|---|
| 422 | `TryGetCustomerErrorResponse1` | **No** — `TryGetRawError` returns `false`. The status is implied by *which* accessor matched: 422. |
| anything else (401, 404, 5xx, …) | `TryGetRawError` | Yes — `raw.StatusCode` |

The same split holds for `CreateSubscriptionError` (422 typed / everything else raw). For
`ListProductsForProductFamilyError` the 404 branch is `TryGetString(out string)` and everything else is raw.
`FindSubscriptionError` is the friendly exception: its 404 branch is `TryGetNoContent(out RawError)`, so the
`RawError` (and its status) is available there.

**404-vs-other, per operation used here** — this is what the idempotency logic keys on:

| Operation | "not found" detection | Other failures |
|---|---|---|
| O4 `ReadCustomerByReference` | Case B: `ex.Error.StatusCode == HttpStatusCode.NotFound` | same catch, any other `StatusCode` → propagate |
| O5 `CreateCustomer` | a 404 is *not* modelled: it lands in `TryGetRawError` with `StatusCode == NotFound` | 422 → `TryGetCustomerErrorResponse1` (status not readable) |
| O7 `CreateSubscription` | 404 → `TryGetRawError`, `StatusCode == NotFound` (this is how a bad product handle would arrive) | 422 → `TryGetErrorListResponse1` → `Errors: IReadOnlyList<string>` |
| O2 `ListProductsForProductFamily` | 404 → `TryGetString(out string)` — ⚠ see the `JsonException` hazard in §3 | else `TryGetRawError` |
| O1/O3/O6/O8/O9/O10 | Case B: `ex.Error.StatusCode` | Case B: `ex.Error.StatusCode` |

**Typed 422 payload shapes** (source: `map/models/records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`):

| Payload type | Fields |
|---|---|
| `ErrorListResponse1` (subscription create, product ops) | `Errors (errors): IReadOnlyList<string>` **required** — a flat list of message strings |
| `CustomerErrorResponse1` (customer create/update) | `Errors (errors): Errors?` where `Errors` = `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` |

⚠ **How far the customer-422 contract can be trusted: not far, and the map itself is the evidence.** The
generated payload for a *customer* validation failure can only express two keys — `per_page` and
`price_point` — neither of which is a customer field. The `Errors` record is shared and was evidently
generated from a different endpoint's error sample. Two things follow, and both are `UNVERIFIED` because only
live traffic can settle which occurs: if the real body is an object with other keys, they are dropped and you
get a `CustomerErrorResponse1` whose `Errors` is null or whose lists are null; if the real body's `errors` is
an **array** of strings (the shape `ErrorListResponse1` models, and the shape the sibling operations use),
deserializing it into the `Errors` **object** throws `System.Text.Json.JsonException` and the `SdkException`
never materialises. **Directive:** in the 422 branch of `CreateCustomer`/`UpdateCustomer`, extract messages
best-effort (`e.Errors?.PerPage`, `e.Errors?.PricePoint`, both null-guarded), never assume a non-empty list,
and fall back to a generic "customer could not be created" message; and make sure the boundary's
`JsonException` arm (§4) also covers this call, because that is a live possibility here rather than a
theoretical one.

⚠ **`ListProductsForProductFamily`'s 404 has the same defect, visible in source.** Its 404 branch is
`FromJson<string>` (source: `Errors/ListProductsForProductFamilyError.cs`) — the *entire* response body is
deserialized as a bare JSON string. That succeeds only if the body is a quoted JSON string literal; an object,
an empty body or HTML throws `JsonException` instead of producing the `SdkException`. `UNVERIFIED` which one
Maxio sends. **Directive:** resolve the family via O1 and pass a numeric id (step 1) so a 404 here means
"family really is gone" rather than "handle typo", treat a `JsonException` from this call as a possible 404
rather than an outage, and never depend on the string the accessor yields.

---

## 3. Trap notes

⚠ **Step 0 (client registration)** — the SDK's `Retry`/`Timeout` options do **not** bound a whole call and are
**not** the timeout on the `HttpClient` you register, and the interaction between `MaxRetries`, the status
trigger and the method trigger decides whether a failed **write** (`POST /customers.json`,
`POST /subscriptions.json`) can be re-sent — which is the difference between your idempotency logic being
sufficient and being decorative. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 0 (client registration)** — `AddMaxioAdvancedBillingClient` registers a singleton over
`IHttpClientFactory`'s default client with no way to name it, so the handler lifetime, DNS refresh and any
resilience handler of your own are decided by how *you* register the `HttpClient`, not by the SDK. **MUST load
`dotnet-client-initialization`** before choosing between the DI extension and the two-argument constructor.

⚠ **Step 0 (auth)** — a missing/blank `Maxio:ApiKey` produces a client that constructs successfully and sends
no `Authorization` header; where the credential is read and when it is snapshot decides whether a rotated key
takes effect at all. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Steps 1–4 (every call)** — most parameters on the list operations are nullable **without** a C# default
(`ListProductFamilies` has five, `ListProductsForProductFamily` has eight, `ListSubscriptions` has fourteen),
so a positional call mis-binds silently and compiles. **MUST load `dotnet-calling-endpoints`** before writing
the first call.

⚠ **Steps 2–4 (projection)** — `StringEnum` comparison/exhaustiveness, the `init`/`required` construction
rules on request records, and the fact that unmodeled JSON fields are dropped on deserialize all bite when you
map SDK models onto your DTOs. **MUST load `dotnet-models`** before constructing request payloads or mapping
responses.

⚠ **Step 3 (idempotency)** — the whole "look up, then create" flow rests on telling a 404 from every other
failure, and the accessor that tells you *is not available* in the typed branch (§2.10). **MUST load
`dotnet-error-handling`** before writing the catch ladder.

⚠ **Step 5 (error boundary)** — see the two `JsonException` rows in §4; they need opposite handling and both
reach this boundary. **MUST load `dotnet-error-handling`**.

⚠ **Tests** — the `HttpClient` constructor argument is the seam; how you fake it determines whether the 404
and 422 branches above are actually covered. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **all** of these **before implementation starts**. This sheet deliberately does **not** carry their
contents — it names the hazard and stops.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — construction, options shape, `HttpClient` ownership/lifetime, DI registration. |
| `dotnet-authentication` | Step 0 — supplying and rotating the Basic credential. |
| `dotnet-configuration-resilience` | Step 0 — retries, backoff, timeouts, base-URL selection, pagination, logging. |
| `dotnet-calling-endpoints` | Steps 1–4, 6 — named arguments, optional-parameter binding, cancellation. |
| `dotnet-models` | Steps 2–4 — request construction, `required`/nullability, `StringEnum`, wire names. |
| `dotnet-error-handling` | Steps 3 and 5 — the catch ladder, reading status and bodies safely. |
| `dotnet-testing` | Tests for the integration layer. |

**Two hazard rows that must shape the boundary from the start** — `System.Text.Json.JsonException` reaches it
from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx
  then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can
  never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Both are live risks in *this* feature, not hypotheticals: `ProductResponse.Product`, `CustomerResponse.Customer`
and `ComponentResponse.Component` are `required` (direction 1), and `CreateCustomerError`'s 422 branch plus
`ListProductsForProductFamilyError`'s 404 branch both deserialize into shapes the map shows to be suspect
(direction 2 — §2.10).

---

## 5. Assumptions & Blockers

**Assumptions** (about intent, not about the SDK):

| # | Assumption |
|---|---|
| A1 | The three endpoints are additive: no eShopOnWeb entity, migration or existing controller is being changed, and the plan projections are new DTOs owned by `PublicApi`. |
| A2 | `ServerEnvironment` stays `Us` unless the deployment explicitly asks for EU hosting; the sandbox is reached by subdomain (or `Maxio:BaseUrl`), not by the environment value. |
| A3 | "Active subscription" for the idempotency check means a state you choose from `SubscriptionState` (`Active`, and probably `Trialing`/`PastDue`/`AwaitingSignup` too). Which states count as "already subscribed" is a product decision — the SDK just hands you the enum. |
| A4 | The family handle in `Maxio:ProductFamilyHandle` resolves to exactly one family. `ListProductFamilies` (O1) has no server-side handle filter, so the match is a client-side comparison over the whole list. |

**Facts that force an application decision** (the fact and its consequence; the decision is yours):

| Fact | Consequence | Label |
|---|---|---|
| No operation accepts a product-family **handle** as a typed parameter (`ReadProductFamily(int id)`, `ListComponentsForProductFamily(int productFamilyId)`) | you must resolve handle → id via O1 and hold the id somewhere; where that cache lives and how it is invalidated is an application design decision | `operations/ProductFamilies.md`, `operations/Components.md` |
| The per-user reference key | resolve it from the app's own identity path; the SDK only requires it be a `string` that is unique per customer | `YOUR CALL — not in the map` |
| O6 has no pagination and no state filter | a customer with many subscriptions returns them all in one call and you filter in memory; there is no SDK-side page loop to write | `operations/Customers.md` |
| O1 has no pagination parameters at all | the family list is whatever one call returns; if the site ever exceeds that, the SDK gives you no next page | `operations/ProductFamilies.md` |
| `AddMaxioAdvancedBillingClient` snapshots options at registration | key rotation or base-URL change requires a restart unless you register the client yourself | `ServiceCollectionExtensions.cs` |

**`UNVERIFIED` — only live traffic can settle these; each carries its defensive directive:**

| # | Uncertainty | Defensive directive |
|---|---|---|
| U1 | Whether a reference miss on O4 is a 404 (vs. a 2xx with an empty/absent `customer`) | Treat `StatusCode == NotFound` as "absent → create". Also treat a `JsonException` from this call as "absent", because `CustomerResponse.Customer` is `required` and a 2xx with no customer object throws rather than returning null. Any other status → propagate; never fall through to "create" on an unclassified failure, or a transient 500 becomes a duplicate customer. |
| U2 | Whether the real 422 body of `CreateCustomer` matches `CustomerErrorResponse1`/`Errors` (the generated shape can only express `per_page`/`price_point` — §2.10) | Extract best-effort (`e.Errors?.PerPage`, `e.Errors?.PricePoint`, null-guarded), fall back to the generic message, and ensure `JsonException` from this call is handled as a 4xx-class rejection rather than an outage. On a create failure that looks like a uniqueness conflict, **re-run O4 and use the existing customer** — the operation's Notes guarantee at most one customer per `reference`, so the loser of a double-click race finds the winner's customer. |
| U3 | Whether `ListProductsForProductFamily` accepts `"handle:eshop-subscribe"` in the path segment. The `ReadProductFamily` Notes mention the `handle:my-family` format, but that method's C# parameter is `int`, so the map offers no in-SDK confirmation for the `string` variant | Do not rely on it. Resolve the id via O1 (step 1) and pass the numeric id as a string. If you later try the `handle:` form, treat any non-2xx **and** any `JsonException` from it as "unsupported" and fall back to the resolved id. |
| U4 | Whether O6's payload actually populates the nested `Product` (there is no `include` parameter on that operation to request it) | Null-guard `sub.Product?.Handle` / `?.Name` everywhere. If the plan name is absent, either omit it from the DTO or fill it from the plans you already fetched in step 2 keyed by handle — do **not** issue an O3 call per subscription in the list path, and do not let a null product null out the whole row. |
| U5 | Whether the seeded plans really create without a payment profile (the request shape permits it; the Notes make acceptance depend on the product's own options) | The 422 branch of O7 is the authoritative answer at runtime — surface `ErrorListResponse1.Errors` (a `required IReadOnlyList<string>`) verbatim to the caller/log rather than collapsing it to a generic message, so a payment-required rejection is diagnosable on first run. |

**Blockers:** none. Every operation this feature needs exists in the map, and the handle-only constraint is
solvable within the SDK via step 1.

**Out of scope, confirmed as such:** usage recording for the `api-call` component (no `v1` requirement).
Listing the family's components is *in* scope and cheap — O10, with `Component.Handle (handle): string?`,
`Name`, `UnitName (unit_name): string?`, `UnitPrice (unit_price): string?` (a **string**, not cents),
`PricePerUnitInCents (price_per_unit_in_cents): long?`, `Kind (kind): ComponentKind?`,
`PricingScheme (pricing_scheme): PricingScheme?`, `Recurring (recurring): bool?`,
`ProductFamilyHandle (product_family_handle): string?` (source: `models/records-1-Ac-Cr.md`) — but it needs
the numeric family id from step 1.
