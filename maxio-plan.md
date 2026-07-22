# Maxio Advanced Billing — .NET SDK integration plan (eShopOnWeb)

Grounded entirely in the SDK map that ships with the SDK source (commit stamp `15db14b`, tag `v1.0.2`,
package `AsadAli.AdvancedBilling.Sdk`, root namespace `MaxioAdvancedBilling`). Every row cites the map page
it came from. Facts that only live traffic can confirm are labelled `UNVERIFIED`.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 0 | Add NuGet `AsadAli.AdvancedBilling.Sdk`; add `IBillingClient` (ApplicationCore) + `MaxioBillingClient` (Infrastructure). No project reference to SDK source. | — |
| 1 | Client construction + options binding (subdomain vs explicit base URL, Basic auth, retry, injected `HttpClient`). | `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` / `AddMaxioAdvancedBillingClient` |
| 2 | Resolve product family `eshop-subscribe` → numeric id (cache per process). | `ProductFamilies.ListProductFamilies` (or `ReadProductFamily`) |
| 3 | List plans / read a plan by handle. | `ProductFamilies.ListProductsForProductFamily`, `Products.ReadProductByHandle`, `Products.ListProducts` |
| 4 | Validate `api-call` component exists and is metered; capture its numeric id. | `Components.FindComponent`, `Components.ListComponentsForProductFamily`, `Components.ReadComponent` |
| 5 | Customer resolve-or-create by `reference` (idempotent). | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 6 | Create subscription by handles, no payment method. | `Subscriptions.CreateSubscription` |
| 7 | Read one subscription / list a customer's subscriptions. | `Subscriptions.ReadSubscription`, `Customers.ListCustomerSubscriptions`, `Subscriptions.ListSubscriptions` |
| 8 | Record usage + read period-to-date total. | `SubscriptionComponents.CreateUsage`, `SubscriptionComponents.ReadSubscriptionComponent`, `SubscriptionComponents.ListUsages` |
| 9 | Plan change: preview proration, then commit now-with-proration **or** at-next-renewal. | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `SubscriptionProducts.MigrateSubscriptionProduct`, `Subscriptions.UpdateSubscription` (delayed) |
| 10 | Lifecycle: pause / resume / cancel now / cancel at EOP / revoke delayed cancel / reactivate. | `SubscriptionStatus.PauseSubscription`, `.ResumeSubscription`, `.CancelSubscription`, `.InitiateDelayedCancellation`, `.CancelDelayedCancellation`, `.ReactivateSubscription` |
| 11 | UC0 seed tooling (operator-only path, separate from `IBillingClient` runtime surface). | `ProductFamilies.CreateProductFamily`, `Products.CreateProduct`, `Components.CreateMeteredComponent`, `Products.ArchiveProduct`, `Components.ArchiveComponent` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** (e.g.
> `MaxioAdvancedBilling.Models.Enums.SubscriptionState`,
> `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference`,
> `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`, and the **client-config types**:
> `MaxioAdvancedBilling.Servers.ServerEnvironment`, `MaxioAdvancedBilling.Core.Configuration.RetryOptions`,
> `MaxioAdvancedBilling.ServerOptions`). Do not drop these to the root or to `.Models`, or the implementer
> guesses the wrong `using` and the build breaks.

### 2.0 Namespaces (add a `using` per kind — C# does not import child namespaces transitively)

| Contents | Namespace |
|---|---|
| Client + options + `ServerOptions` | `MaxioAdvancedBilling` |
| Controllers (`client.X` property types) | `MaxioAdvancedBilling.Api` |
| Records (all request/response models) | `MaxioAdvancedBilling.Models` |
| Enums (`StringEnum<T>`, not C# enums) | `MaxioAdvancedBilling.Models.Enums` |
| Unions | `MaxioAdvancedBilling.Models.AnyOf` · `MaxioAdvancedBilling.Models.OneOf` |
| Typed error classes `{Operation}Error` | `MaxioAdvancedBilling.Errors` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `ApiError`, `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |
| `ServerEnvironment`, `ProductionOptions`, `EbbOptions` | `MaxioAdvancedBilling.Servers` |

Source: `sdk-map.md` (namespaces table); `ServerOptions`/`ProductionOptions` namespaces confirmed from
`ServerOptions.cs` and `Servers/ProductionOptions.cs` (the map's rows gave the file paths but not the
`namespace` line — root, **not** `Core.Configuration`).

### 2.1 Client construction, auth, base URL (constraints a + b)

**Only constructor** (`sdk-map.md` → "Getting a client"; source `MaxioAdvancedBillingClient.cs`):

```
MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient,
                           MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)
```

`MaxioAdvancedBillingClientOptions` properties (all four, `sdk-map.md`):

| Property | Type | Notes |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Us` (wire `"US"`, default) · `ServerEnvironment.Eu` (wire `"EU"`). It is a `StringEnum`, not a C# enum. `ServerEnvironment.Default()` returns `Us`. |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | All members `required` → build from `RetryOptions.Default()` and `with`-modify; do not `new RetryOptions{}` partially. |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | Base-URL override point, see below. |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `new BasicAuthCredentials { Username = "<API key>", Password = "x" }` — username = API key, password = the literal `"x"`. |

`ServerOptions` shape (source `ServerOptions.cs`, `Servers/ProductionOptions.cs` — read because the map's
row named the files but not the nested property names):

```
options.Server.Production.Us.BaseUrl  // default "https://{site}.chargify.com"
options.Server.Production.Us.Site     // default "subdomain"
options.Server.Production.Eu.BaseUrl  // default "https://{site}.ebilling.maxio.com"
options.Server.Production.Eu.Site     // default "subdomain"
options.Server.Ebb.Us.BaseUrl / .Us.Site   // default "https://events.chargify.com/{site}" (EU identical)
options.Server.Ebb.Eu.BaseUrl / .Eu.Site
```

Types: `ServerOptions.Production` is `MaxioAdvancedBilling.Servers.ProductionOptions`; `.Us` is the nested
class `MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions` (`.Eu` → `…ProductionOptions.EuOptions`);
`ServerOptions.Ebb` is `MaxioAdvancedBilling.Servers.EbbOptions`.

**Two configuration modes for the same build (constraint a):**

| Mode | Set |
|---|---|
| Subdomain-derived host (prod) | `options.Environment = ServerEnvironment.Us;` `options.Server.Production.Us.Site = "cp-exp-2";` → resolves `https://cp-exp-2.chargify.com`. Leave `BaseUrl` at its default template. |
| Explicit base URL (mock at `http://localhost:8080`) | `options.Server.Production.Us.BaseUrl = "http://localhost:8080";` (still with `Environment = ServerEnvironment.Us`, because the Us/Eu branch is what picks which options object is read). `Site` is then irrelevant. |

How the host is derived (verified in `Servers/ProductionOptions.Resolve` + `Core/TemplateParamsFactory.cs`):
the selected environment picks `Us`/`Eu`; the SDK then does a plain string replace of `{site}` in `BaseUrl`
with `Site`, trims a trailing `/` off the base and joins the path. **A `BaseUrl` with no `{site}`
placeholder is passed through unchanged** — so `http://localhost:8080` is a valid explicit override, no
placeholder required. Bind both `Site` and an optional `BaseUrl` override from configuration and only assign
`BaseUrl` when the config value is non-empty.

**DI extension (constraint b):** `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`
in `MaxioAdvancedBilling` (static class `ServiceCollectionExtensions`, source `ServiceCollectionExtensions.cs`).
It calls `services.AddHttpClient()` and registers the client as a **singleton**, creating the `HttpClient`
itself from `IHttpClientFactory` — **it gives you no seam to inject your own `HttpClient`**. For the test
seam (and to control the handler), register the client yourself:

```csharp
services.AddHttpClient("maxio");                     // long-lived, factory-managed
services.AddSingleton(sp => new MaxioAdvancedBillingClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("maxio"),
    BuildOptions(sp.GetRequiredService<IOptions<MaxioSettings>>().Value)));
services.AddScoped<IBillingClient, MaxioBillingClient>();
```

Controller accessors used below are properties on the client: `client.Products`, `client.ProductFamilies`,
`client.Components`, `client.Customers`, `client.Subscriptions`, `client.SubscriptionStatus`,
`client.SubscriptionProducts`, `client.SubscriptionComponents` (each page's "Accessor" line).

### 2.2 Operations

Legend: fields are `CSharpName (wire_name): Type` — `!req` = C# `required`. All response wrappers are
envelopes: read the single inner property named in the "Response envelope" column.

#### A. Product families (steps 2, 11)

| Operation | Signature (verbatim) | Request model | Response envelope → inner fields | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must be passed explicitly (`null` to skip) | — | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` (`ProductFamily?`) → `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description`, `CreatedAt`, `UpdatedAt`, `ArchivedAt (archived_at): DateTimeOffset?` | **Case B** `SdkException<RawError>` → `ex.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` | none (no page/perPage) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `client.ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse.ProductFamily` | **Case B** `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies.CreateProductFamily` | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateProductFamilyRequest { ProductFamily (product_family): CreateProductFamily !req }`; `CreateProductFamily { Name (name): string !req, Handle (handle): string?, Description (description): string? }` | `ProductFamilyResponse.ProductFamily` | **Case A** `SdkException<CreateProductFamilyError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/ProductFamilies.md`, `records-1-Ac-Cr.md` |

**Resolving `eshop-subscribe` → id:** `ReadProductFamily` takes an `int`, so it cannot take a handle in a
type-safe way (the endpoint doc mentions a `handle:my-family` form, but the C# parameter is `int` — that
form is **not reachable** through this SDK). Use `ListProductFamilies(null, null, null, null, null, ct: ct)`
and match `f.ProductFamily?.Handle == "eshop-subscribe"` (case-sensitive), then cache `Id`. This list
operation has **no pagination parameters at all**, so it returns whatever the server returns in one call —
if a site ever exceeds that, this lookup silently misses (`UNVERIFIED`: server-side cap on this endpoint).
Defensive directive: if no match, throw your own `BillingConfigurationException` naming the handle.

#### B. Products / plans (steps 3, 11)

| Operation | Signature (verbatim) | Request model | Response envelope → inner fields | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` must be passed explicitly | `ListProductsFilter { Ids (ids): IReadOnlyList<int>?, PrepaidProductPricePoint …, UseSiteExchangeRate (use_site_exchange_rate): bool? }` | `IReadOnlyList<ProductResponse>`; each `.Product` (`Product !req`) | **Case A** `SdkException<ListProductsForProductFamilyError>` → `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual `page`+`perPage` (defaults 1 / 20 — loop until a short page) | `operations/ProductFamilies.md`, `records-2-Cr-Ne.md` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse.Product` | **Case B** `SdkException<RawError>` (404 → `ex.Error.StatusCode == HttpStatusCode.NotFound`) | none | `operations/Products.md` |
| `client.Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse.Product` | **Case B** `SdkException<RawError>` | none | `operations/Products.md` |
| `client.Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` (site-wide) | as above | `IReadOnlyList<ProductResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Products.md` |
| `client.Products.CreateProduct` | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` | see §2.3 | `ProductResponse.Product` | **Case A** `SdkException<CreateProductError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | `operations/Products.md` |
| `client.Products.ArchiveProduct` | `ArchiveProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse.Product` (archived copy; check `ArchivedAt`) | **Case A** `SdkException<ArchiveProductError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | `operations/Products.md` |

**`Product` fields you asked for** (`records-3-Of-Su.md`, `Models/Product.cs`):
`Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `Description (description): string?` ·
`PriceInCents (price_in_cents): long?` **← price is CENTS, an integer (`$299.00` ⇒ `29900`)** ·
`Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` (StringEnum: `Day (day)`, `Month (month)`) ·
`ArchivedAt (archived_at): DateTimeOffset?` **← there is no `archived` bool on `Product`; "archived" = `ArchivedAt != null`** ·
`ProductFamily (product_family): ProductFamily?` (nested, gives family `Id`/`Handle`/`Name`) ·
`RequireCreditCard (require_credit_card): bool?` · `RequestCreditCard (request_credit_card): bool?` ·
`Taxable (taxable): bool?` · `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `InitialChargeInCents (initial_charge_in_cents): long?` ·
`DefaultProductPricePointId (default_product_price_point_id): int?`, `ProductPricePointHandle`, `ProductPricePointName` ·
`CreatedAt`, `UpdatedAt`, `VersionNumber`.
Monthly = `Interval == 1 && IntervalUnit == IntervalUnit.Month`.

#### C. Components (steps 4, 11)

| Operation | Signature (verbatim) | Request model | Response envelope → inner fields | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` (query `handle`; **no `handle:` prefix here — plain `"api-call"`**) | — | `ComponentResponse.Component` (`Component !req`) | **Case B** `SdkException<RawError>` | none | `operations/Components.md` |
| `client.Components.ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` may be the numeric id **or** the handle **prefixed** `"handle:api-call"` | — | `ComponentResponse.Component` | **Case B** `SdkException<RawError>` | none | `operations/Components.md` |
| `client.Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 7 `includeArchived`…`startDatetime` must be passed explicitly; note the date params here are `string?`, not `DateTimeOffset?` | `ListComponentsFilter { Ids (ids): IReadOnlyList<int>?, UseSiteExchangeRate (use_site_exchange_rate): bool? }` | `IReadOnlyList<ComponentResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Components.md`, `records-2-Cr-Ne.md` |
| `client.Components.CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` | see §2.3 | `ComponentResponse.Component` | **Case A** `SdkException<CreateMeteredComponentError>` → `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | `operations/Components.md` |
| `client.Components.ArchiveComponent` | `ArchiveComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | — | **`Component` directly — NOT `ComponentResponse`.** No envelope on this one. | **Case A** `SdkException<ArchiveComponentError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | `operations/Components.md` |

**`Component` fields you need** (`records-1-Ac-Cr.md`, `Models/Component.cs`):
`Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` ·
**`Kind (kind): ComponentKind?` ← the metered flag**; metered ⇒ `ComponentKind.MeteredComponent` (wire
`"metered_component"`) · `PricingScheme (pricing_scheme): PricingScheme?` (per-unit ⇒ `PricingScheme.PerUnit`,
wire `"per_unit"`) · `UnitName (unit_name): string?` ·
`UnitPrice (unit_price): string?` **← DOLLARS as a decimal string, e.g. `"0.01"`** ·
`PricePerUnitInCents (price_per_unit_in_cents): long?` **← the same price in cents; the two fields coexist,
which is the map-visible proof that `unit_price` is not cents** ·
`Archived (archived): bool?` + `ArchivedAt (archived_at): DateTimeOffset?` ·
`ProductFamilyId (product_family_id): int?`, `ProductFamilyHandle`, `ProductFamilyName` ·
`Prices (prices): IReadOnlyList<ComponentPrice?>?` where `ComponentPrice { Id, ComponentId, StartingQuantity (starting_quantity): int?, EndingQuantity (ending_quantity): int?, UnitPrice (unit_price): string?, PricePointId, FormattedUnitPrice (formatted_unit_price): string?, SegmentId }` ·
`Taxable`, `Recurring`, `DefaultPricePointId`, `Interval`, `IntervalUnit`, `CreatedAt`, `UpdatedAt`.

Validation for step 4: `var c = (await client.Components.FindComponent("api-call", ct)).Component;` then
require `c.Kind == ComponentKind.MeteredComponent` (StringEnum `==` compares by value) and keep `c.Id`.
Guard unknown wire values with `ComponentKind.TryGetKnownValue(...)` before comparing if you log the raw
value.

#### D. Customers (step 5)

| Operation | Signature (verbatim) | Request model | Response envelope → inner fields | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` (GET `/customers/lookup.json`, query `reference`) | — | `CustomerResponse.Customer` (`Customer !req`) | **Case B** `SdkException<RawError>` — a miss is a non-2xx, so **404 arrives as an exception, not a null**; treat `ex.Error.StatusCode == HttpStatusCode.NotFound` as "not found" and fall through to create | none | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer { FirstName (first_name): string !req, LastName (last_name): string !req, Email (email): string !req, Reference (reference): string?, CcEmails, Organization, Address, Address2 (address_2), City, State, Zip, Country, Phone, Locale, VatNumber, TaxExempt (tax_exempt): bool?, TaxExemptReason, ParentId, SalesforceId }` | `CustomerResponse.Customer` | **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)`. `CustomerErrorResponse1 { Errors (errors): Errors? }`, `Errors { PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }` — **this generated 422 shape carries only `per_page`/`price_point` buckets and cannot hold a `reference has already been taken` message**, so on 422 the useful text is not reachable through the typed accessor | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| `client.Customers.ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse.Customer` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| `client.Customers.ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 explicit params; `q` is the free-text search | — | `IReadOnlyList<CustomerResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (default 50) | `operations/Customers.md` |

`Customer` fields: `Id (id): int?`, `FirstName`, `LastName`, `Email`, `Reference (reference): string?`,
`Organization`, `CreatedAt`, `UpdatedAt`, … (`records-2-Cr-Ne.md`).

**Idempotent resolve-or-create (step 5):** try `ReadCustomerByReference(reference, ct)`; on
`SdkException<RawError>` with `StatusCode == NotFound` call `CreateCustomer`; on a `CreateCustomerError`
422 (race: another writer created it) **re-run the lookup once** and return that customer. Because the
typed 422 payload above cannot express the duplicate-reference message, do not branch on message text —
branch on "lookup now succeeds". Directive for the 422 log line: `TryGetCustomerErrorResponse1` first,
then `TryGetRawError` and log `raw.ReadAsString()` best-effort; `ReadAsJson<T>()` throws `JsonException`
on a non-JSON body, so prefer `ReadAsString()`.

#### E. Subscriptions — create / read / list (steps 6, 7)

| Operation | Signature (verbatim) | Request model | Response envelope → inner fields | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` — see §2.3 for the fields to set | `SubscriptionResponse.Subscription` (`Subscription?` — **nullable**, null-check it) | **Case A** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)`. `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` — join for the message | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed (`null` to skip) | — | `SubscriptionResponse.Subscription` | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — **by customer id only, no reference form, no paging params** | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| `client.Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 explicit params; **there is no customer filter here** | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — looks up by the **subscription's own** `reference`, not the customer's | — | `SubscriptionResponse.Subscription` | **Case A** `SdkException<FindSubscriptionError>` → `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none | `operations/Subscriptions.md` |

**"Subscriptions for a customer" — pick the path:** resolve the customer by reference (§D) then call
`ListCustomerSubscriptions(customerId, ct)`. `ListSubscriptions` cannot filter by customer at all
(no `customer_id`/`customer_reference` query param in its parameter list).

**`Subscription` fields you asked for** (`records-3-Of-Su.md`, `Models/Subscription.cs`):
`Id (id): int?` · `State (state): SubscriptionState?` · `Product (product): Product?` (full nested product —
handle, name, `PriceInCents`) · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ·
`NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?` ·
`TotalRevenueInCents (total_revenue_in_cents): long?` **(cents)** · `ProductPriceInCents (product_price_in_cents): long?` **(cents)** ·
`CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` · `BalanceInCents`, `CreditBalanceInCents`, `PrepaymentBalanceInCents` ·
`Customer (customer): Customer?` · `Reference (reference): string?` ·
`CancelAtEndOfPeriod (cancel_at_end_of_period): bool?` · `DelayedCancelAt (delayed_cancel_at): DateTimeOffset?` ·
`ScheduledCancellationAt`, `CanceledAt`, `CancellationMessage (cancellation_message): string?`,
`CancellationMethod (cancellation_method): CancellationMethod?` · `OnHoldAt (on_hold_at): DateTimeOffset?` ·
`AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` · `PreviousState (previous_state): SubscriptionState?` ·
`NextProductId (next_product_id): int?`, `NextProductHandle (next_product_handle): string?` **← how you read back a scheduled (delayed) product change** ·
`ProductPricePointId`, `ProductPricePointType`, `TrialStartedAt`, `TrialEndedAt`, `ActivatedAt`, `ExpiresAt`, `CreatedAt`, `UpdatedAt`.

#### F. Usage (step 8)

| Operation | Signature (verbatim) | Request model | Response envelope → inner fields | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` | `CreateUsageRequest { Usage (usage): CreateUsage !req }`; `CreateUsage { Quantity (quantity): double?, PricePointId (price_point_id): string?, Memo (memo): string?, BillingSchedule (billing_schedule): BillingSchedule?, CustomPrice (custom_price): ComponentCustomPrice? }` — **`Quantity` is `double?`, and negative values deduct** | `UsageResponse.Usage` (`Usage !req`) → `Id (id): long?`, `Quantity (quantity): Quantity1?` **(union int\|string)**, `Memo (memo): string?`, `CreatedAt (created_at): DateTimeOffset?`, `ComponentId`, `ComponentHandle (component_handle): string?`, `SubscriptionId`, `PricePointId`, `OverageQuantity` | **Case A** `SdkException<CreateUsageError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | `operations/SubscriptionComponents.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 explicit params | — | `IReadOnlyList<UsageResponse>` (same `Usage` shape) | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/SubscriptionComponents.md` |
| `client.SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — **both `int`; no handle form here** | — | `SubscriptionComponentResponse.Component` (`SubscriptionComponent?`) → `UnitBalance (unit_balance): int?` **← the running period-to-date metered total**, plus `Kind (kind): ComponentKind?`, `ComponentId`, `ComponentHandle`, `PricingScheme`, `UnitName`, `PricePointId/Handle/Name`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (union int\|string), `Enabled`, `Currency`, `Interval`, `IntervalUnit`, `HistoricUsages`, `ArchivedAt` | **Case A** `SdkException<ReadSubscriptionComponentError>` → `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | `operations/SubscriptionComponents.md`, `records-3-Of-Su.md` |
| `client.SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 explicit params | — | `IReadOnlyList<SubscriptionComponentResponse>` | **Case B** `SdkException<RawError>` | none | `operations/SubscriptionComponents.md` |

**Union arguments (required, positional, no implicit-null):**
`SubscriptionIdOrReference` (`MaxioAdvancedBilling.Models.AnyOf`) — factories `SubscriptionIdOrReference.Int(int)`
/ `.String(string)`, implicit from `int` and `string`; readers `TryGetInt` / `TryGetString`.
`ComponentIdModel` (same namespace) — `ComponentIdModel.Int(int)` / `.String(string)`, implicit from both.
**When passing the component by handle to `CreateUsage`/`ListUsages`, the string must be prefixed:
`ComponentIdModel.String("handle:api-call")`** (per the operation notes on `ListUsages`). `FindComponent`'s
`handle` query param is the one place with **no** prefix.

**Best-supported period-to-date total (your question):**
`ReadSubscriptionComponent(subscriptionId, componentId, ct).Component?.UnitBalance` — the SDK's own
`CreateUsage` notes state "The `quantity` from usage for each component is accumulated to the `unit_balance`
on the Component Line Item for the subscription", and `unit_balance` resets with the billing period for
metered components. This is one call and needs no client-side summing → use it as the primary. It requires
the **numeric** component id (cache it from step 4).
Fallback/audit path: `ListUsages(SubscriptionIdOrReference.Int(id), ComponentIdModel.String("handle:api-call"), sinceId: null, maxId: null, sinceDate: currentPeriodStartedAt, untilDate: null, ct: ct)` and sum
`u.Usage.Quantity` — note `Quantity` on the **response** is the union `Quantity1` (`TryGetInt` /
`TryGetString`), not a number, so parse: `if (q.TryGetInt(out var i)) … else if (q.TryGetString(out var s)) double.Parse(s, CultureInfo.InvariantCulture)`. `since_date` defaults to midnight of the given date
(operation note), so it is day-granular — `UNVERIFIED` whether it can exactly reproduce a mid-day period
boundary; prefer `UnitBalance` for anything user-facing and treat the summed list as diagnostic.
Third option: `ListSubscriptionComponents(..., include: [ListSubscriptionComponentsInclude.HistoricUsages], ...)`
returns `HistoricUsage { TotalUsageQuantity (total_usage_quantity): double?, BillingPeriodStartsAt, BillingPeriodEndsAt }`
for the last ten billing periods — its doc summary says "Optional for Event Based Components", so
`UNVERIFIED` whether it is populated for a plain metered component; do not depend on it.

#### G. Plan change (step 9)

| Operation | Signature (verbatim) | Request model | Response envelope → inner fields | Error | Map page |
|---|---|---|---|---|---|
| `client.SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` | `SubscriptionMigrationPreviewRequest { Migration (migration): SubscriptionMigrationPreviewOptions !req }`; `SubscriptionMigrationPreviewOptions { ProductId (product_id): int?, ProductPricePointId (product_price_point_id): int?, IncludeTrial (include_trial): bool? = false, IncludeInitialCharge (include_initial_charge): bool? = false, IncludeCoupons (include_coupons): bool? = true, PreservePeriod (preserve_period): bool? = false, ProductHandle (product_handle): string?, ProductPricePointHandle (product_price_point_handle): string?, Proration (proration): Proration?, ProrationDate (proration_date): DateTimeOffset? }`; `Proration { PreservePeriod (preserve_period): bool? }` | `SubscriptionMigrationPreviewResponse.Migration` (`SubscriptionMigrationPreview !req`) → `ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?`, `ChargeInCents (charge_in_cents): long?`, `PaymentDueInCents (payment_due_in_cents): long?`, `CreditAppliedInCents (credit_applied_in_cents): long?` — **all four are CENTS (`long`)** | **Case A** `SdkException<PreviewSubscriptionProductMigrationError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionProducts.md`, `records-4-Su-We.md`, `records-3-Of-Su.md` |
| `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` | `SubscriptionProductMigrationRequest { Migration (migration): SubscriptionProductMigration !req }`; `SubscriptionProductMigration` = same fields as the preview options **minus `ProrationDate`** (`ProductId`, `ProductPricePointId`, `IncludeTrial = false`, `IncludeInitialCharge = false`, `IncludeCoupons = true`, `PreservePeriod = false`, `ProductHandle`, `ProductPricePointHandle`, `Proration`) | `SubscriptionResponse.Subscription` (updated subscription) | **Case A** `SdkException<MigrateSubscriptionProductError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionProducts.md`, `records-4-Su-We.md` |
| `client.Subscriptions.UpdateSubscription` (**delayed / at-next-renewal change**) | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` | `UpdateSubscriptionRequest { Subscription (subscription): UpdateSubscription !req }`; set `ProductHandle (product_handle): string?` **and** `ProductChangeDelayed (product_change_delayed): bool?` = `true`. To **cancel** a scheduled change set `NextProductId (next_product_id): string?` to `""` (note: `string?`, not int). Other relevant fields: `ProductId (product_id): int?`, `ProductPricePointHandle`, `ProductPricePointId`, `NextBillingAt`, `Reference` | `SubscriptionResponse.Subscription` — read back `NextProductHandle` / `NextProductId` to confirm the schedule | **Case A** `SdkException<UpdateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/Subscriptions.md`, `records-4-Su-We.md` |

**Timing semantics — what the SDK actually exposes:**
- *Apply now with proration* → `MigrateSubscriptionProduct` with `ProductHandle = "eshop-pro"`. The migration
  endpoint is the prorating path; `PreservePeriod = true` keeps the current billing period, `false` (default)
  starts a new one.
- *At next renewal (no proration)* → **`UpdateSubscription` with `ProductChangeDelayed = true`** — the
  migration request model has **no delayed flag** (verified: `SubscriptionProductMigration` field list above).
- The migration models carry **two** proration knobs — a top-level `PreservePeriod` and a nested
  `Proration.PreservePeriod` — with no map-visible difference. `UNVERIFIED` which wins if both are set:
  set **only** the top-level `PreservePeriod` and leave `Proration = null`, and log the preview amounts you
  got versus the amounts charged so a divergence is visible.
- Migration requires the subscription to be `active` or `trialing` (operation note); migrating to the
  subscription's current product is the most common failure (422).

#### H. Lifecycle (step 10)

| Operation | Signature (verbatim) | Request model | Response envelope | Error | Map page |
|---|---|---|---|---|---|
| Pause/hold | `client.SubscriptionStatus.PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` (`POST …/hold.json`) | `PauseRequest { Hold (hold): AutoResume? }`; `AutoResume { AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset? }`. Pass `body: null` for an indefinite hold. | `SubscriptionResponse.Subscription` (expect `State == SubscriptionState.OnHold`, `OnHoldAt` set) | **Case A** `SdkException<PauseSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`, `records-3-Of-Su.md`, `records-1-Ac-Cr.md` |
| Change/remove auto-resume date | `client.SubscriptionStatus.UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` (`PUT …/hold.json`) | same `PauseRequest` (set `AutomaticallyResumeAt = null` to clear) | `SubscriptionResponse.Subscription` | **Case A** `SdkException<UpdateAutomaticSubscriptionResumptionError>` → `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md` |
| Resume | `client.SubscriptionStatus.ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` | **no body** — the only option is the query param `calendar_billing['resumption_charge']`; pass `null` for non-calendar-billing sites. Enum `ResumptionCharge`: `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` | `SubscriptionResponse.Subscription` | **Case A** `SdkException<ResumeSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`, `enums.md` |
| Cancel immediately | `client.SubscriptionStatus.CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` (`DELETE /subscriptions/{id}.json`) | `CancellationRequest { Subscription (subscription): CancellationOptions !req }`; `CancellationOptions { CancellationMessage (cancellation_message): string?, ReasonCode (reason_code): string?, CancelAtEndOfPeriod (cancel_at_end_of_period): bool?, ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?, RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool? }`. **Omit all schedule params (or pass `body: null`) to cancel immediately.** | `SubscriptionResponse.Subscription` (`State == SubscriptionState.Canceled`) | **Case A** `SdkException<CancelSubscriptionApiError>` (note the `ApiError` suffix — not `CancelSubscriptionError`) → `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError(out RawError)`. `CancelSubscriptionErrorResponse` is an **AnyOf union**: `TryGetErrorListResponse1(out …)` / `TryGetSingleErrorResponse1(out …)`; `SingleErrorResponse1 { Error (error): string !req }` | `operations/SubscriptionStatus.md`, `records-1-Ac-Cr.md`, `unions.md`, `records-3-Of-Su.md` |
| Cancel at end of period | `client.SubscriptionStatus.InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` (`POST …/delayed_cancel.json`) | same `CancellationRequest` (use it to pass `CancellationMessage` / `ReasonCode`) | **`DelayedCancellationResponse { Message (message): string? }` — no subscription is returned.** Re-read the subscription afterwards to surface `CancelAtEndOfPeriod` / `DelayedCancelAt`. | **Case A** `SdkException<InitiateDelayedCancellationError>` → `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`, `records-2-Cr-Ne.md` |
| Revoke delayed cancel | `client.SubscriptionStatus.CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` (`DELETE …/delayed_cancel.json`) — **idempotent**, safe when nothing was scheduled | — | `DelayedCancellationResponse { Message }` — again no subscription; re-read to confirm `CancelAtEndOfPeriod == false` | **Case A** `SdkException<CancelDelayedCancellationError>` → `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | `operations/SubscriptionStatus.md` |
| Reactivate a cancelled subscription | `client.SubscriptionStatus.ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` | `ReactivateSubscriptionRequest { CalendarBilling (calendar_billing): ReactivationBilling?, IncludeTrial (include_trial): bool?, PreserveBalance (preserve_balance): bool?, CouponCode (coupon_code): string?, UseCreditsAndPrepayments (use_credits_and_prepayments): bool?, Resume (resume): Resume? (union) }`. `Resume` = AnyOf `bool` \| `ResumeOptions` → `Resume.Bool(true)` or `Resume.ResumeOptions(new ResumeOptions { RequireResume (require_resume): bool?, ForgiveBalance (forgive_balance): bool? })`. `ReactivationBilling { ReactivationCharge (reactivation_charge): ReactivationCharge? = ReactivationCharge.Prorated }` (calendar-billing only) | `SubscriptionResponse.Subscription` (`active` or `trialing`) | **Case A** `SdkException<ReactivateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`, `records-3-Of-Su.md`, `unions.md` |
| (adjacent, if you dun) Retry a past-due sub | `client.SubscriptionStatus.RetrySubscription(int subscriptionId, CancellationToken ct = default)` | — | `SubscriptionResponse.Subscription` | **Case A** `SdkException<RetrySubscriptionError>` → `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md` |

Note: `PauseSubscription` fails if `next_billing_at` is within 24 hours (operation note) → surface that 422
message to the operator rather than swallowing it.

### 2.3 Write-request bodies for the seed tooling and subscription creation (step 6, 11)

**Create subscription (no payment method), by handles** — `CreateSubscription` fields to set
(`records-2-Cr-Ne.md`, `Models/CreateSubscription.cs`; everything else on that record is optional and can
be left unset):

| Field | Type | Use |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` | `"eshop-pro"` / `"basic-plan"` |
| `CustomerReference (customer_reference)` | `string?` | your stable reference — avoids needing the numeric customer id |
| `CustomerId (customer_id)` | `int?` | alternative to the above (use one) |
| `ProductPricePointHandle` / `ProductPricePointId` | `string?` / `int?` | omit → product's default price point |
| `Reference (ref)`… careful | `Ref (ref): string?` **and** `Reference (reference): string?` are **two distinct fields** on this record | set `Reference` for your own subscription reference |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | `CollectionMethod.Automatic (automatic)`, `.Remittance (remittance)`, `.Prepaid (prepaid)`, `.Invoice (invoice)` |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | only if you want create-customer-inline instead of step 5 |
| `CouponCode`, `CouponCodes`, `NextBillingAt`, `Components`, `Metafields`, `OfferId (union)` | — | not needed for the base flow |

There is **no** "skip payment" flag: passing no `payment_profile_id` / `credit_card_attributes` /
`bank_account_attributes` is how you create without a payment method, and it only succeeds if the product
has `require_credit_card = false` (which is why step 11 sets it). A product that requires a card returns
422 through `CreateSubscriptionError.TryGetErrorListResponse1`.

**Create product** — `CreateOrUpdateProductRequest { Product (product): CreateOrUpdateProduct !req }`;
`CreateOrUpdateProduct` (`records-1-Ac-Cr.md`):

| Field | Type | For `eshop-pro` |
|---|---|---|
| `Name (name)` | `string !req` | `"eShop Pro"` |
| `Description (description)` | `string !req` **← required, not optional** | non-empty text |
| `Handle (handle)` | `string?` | `"eshop-pro"` |
| `PriceInCents (price_in_cents)` | `long !req` **← CENTS** | `29900` ($299.00); `basic-plan` → `2900` |
| `Interval (interval)` | `int !req` | `1` |
| `IntervalUnit (interval_unit)` | `IntervalUnit !req` | `IntervalUnit.Month` (wire `month`) |
| `RequireCreditCard (require_credit_card)` | `bool?` | `false` |
| `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `TrialType` | `long? / int? / IntervalUnit? / TrialType?` | leave **unset** = no trial |
| `AccountingCode`, `ExpirationInterval`, `ExpirationIntervalUnit`, `AutoCreateSignupPage`, `TaxCode` | optional | leave unset (`AutoCreateSignupPage = false` if you want to be explicit) |

`CreateProduct` takes `string productFamilyId`. Pass the **numeric family id as a string**
(`familyId.ToString(CultureInfo.InvariantCulture)`); the `handle:eshop-subscribe` form is documented only
for `ReadProductFamily`, so using it here is `UNVERIFIED` — resolve the id in step 2 and pass that.

**Create metered component** — `CreateMeteredComponent { MeteredComponent (metered_component): MeteredComponent !req }`;
`MeteredComponent` (`records-2-Cr-Ne.md`):

| Field | Type | For `api-call` |
|---|---|---|
| `Name (name)` | `string !req` | `"API Call"` |
| `UnitName (unit_name)` | `string !req` | `"call"` |
| `PricingScheme (pricing_scheme)` | `PricingScheme !req` | `PricingScheme.PerUnit` (wire `per_unit`; others: `Stairstep (stairstep)`, `Volume (volume)`, `Tiered (tiered)`) |
| `Handle (handle)` | `string?` | `"api-call"` |
| `UnitPrice (unit_price)` | `UnitPrice1?` **union (string \| double)** | `UnitPrice1.String("0.01")` — **DOLLARS**, not cents. Prefer the string factory so the decimal is exact and culture-independent. |
| `Prices (prices)` | `IReadOnlyList<Price>?` | alternative to `UnitPrice` for tiered schemes; `Price { StartingQuantity: StartingQuantity !req (union int\|string), EndingQuantity: EndingQuantity? (union), UnitPrice: UnitPrice !req (union double\|string) }` — for `per_unit` use `UnitPrice`, not `Prices` |
| `Taxable (taxable)` | `bool?` | `false` |
| `Description`, `TaxCode`, `HideDateRangeOnInvoice`, `DisplayOnHostedPage`, `AllowFractionalQuantities`, `PublicSignupPageIds`, `Interval`, `IntervalUnit`, `PricePoints` | optional | leave unset |

Also `string productFamilyId` on this call → pass the numeric id as a string (same caveat as above).

### 2.4 Enum value tables actually needed (`enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`)

All are `StringEnum<T>` records — write `SubscriptionState.Active`, never `"active"`; read the wire value
back with `.Value`; `==` compares by value; use `T.FromValue(raw)` for a server value and
`T.TryGetKnownValue(raw, out var known)` / `instance.IsKnownValue()` to guard unknowns.

**`SubscriptionState`** (all 15 members, exact spelling):
`Pending (pending)` · `FailedToCreate (failed_to_create)` · `Trialing (trialing)` · `Assessing (assessing)` ·
`Active (active)` · `SoftFailure (soft_failure)` · `PastDue (past_due)` · `Suspended (suspended)` ·
`Canceled (canceled)` *(one "l")* · `Expired (expired)` · `Paused (paused)` · `Unpaid (unpaid)` ·
`TrialEnded (trial_ended)` · `OnHold (on_hold)` · `AwaitingSignup (awaiting_signup)`.
A paused subscription reports `on_hold` (`OnHold`); `paused` also exists as a separate value — map both to
your "paused" domain state.

**`SubscriptionStateFilter`** (the *filter* enum for `ListSubscriptions`, a different, smaller set):
`Active (active)` · `Canceled (canceled)` · `Expired (expired)` · `ExpiredCards (expired_cards)` ·
`OnHold (on_hold)` · `PastDue (past_due)` · `PendingCancellation (pending_cancellation)` ·
`PendingRenewal (pending_renewal)` · `Suspended (suspended)` · `TrialEnded (trial_ended)` ·
`Trialing (trialing)` · `Unpaid (unpaid)`.

**`ComponentKind`**: `MeteredComponent (metered_component)` · `QuantityBasedComponent (quantity_based_component)` ·
`OnOffComponent (on_off_component)` · `PrepaidUsageComponent (prepaid_usage_component)` · `EventBasedComponent (event_based_component)`.

**`PricingScheme`**: `Stairstep (stairstep)` · `Volume (volume)` · `PerUnit (per_unit)` · `Tiered (tiered)`.

**`IntervalUnit`**: `Day (day)` · `Month (month)` — **no `year`**; annual = `Interval = 12`, `IntervalUnit.Month`.

**`CollectionMethod`**: `Automatic (automatic)` · `Remittance (remittance)` · `Prepaid (prepaid)` · `Invoice (invoice)`.

**`ResumptionCharge`**: `Prorated (prorated)` · `Immediate (immediate)` · `Delayed (delayed)`.
**`ReactivationCharge`**: `Prorated (prorated)` · `Immediate (immediate)` · `Delayed (delayed)`.
**`CancellationMethod`** (read-only on `Subscription`): `MerchantUi (merchant_ui)` · `MerchantApi (merchant_api)` ·
`Dunning (dunning)` · `BillingPortal (billing_portal)` · `Unknown (unknown)` · `Imported (imported)`.
**`BasicDateField`** (list filters): `UpdatedAt (updated_at)` · `CreatedAt (created_at)`.
**`SortingDirection`**: `Asc (asc)` · `Desc (desc)`. **`SubscriptionInclude`**: `Coupons (coupons)` ·
`SelfServicePageToken (self_service_page_token)`. **`ListSubscriptionComponentsInclude`**: `Subscription (subscription)` ·
`HistoricUsages (historic_usages)`. **`TrialType`**: `NoObligation (no_obligation)` · `PaymentExpected (payment_expected)`.
**`ExpirationIntervalUnit`**: `Day (day)` · `Month (month)` · `Never (never)`.
**`ServerEnvironment`** (namespace `MaxioAdvancedBilling.Servers`, also a `StringEnum`): `Us (US)` · `Eu (EU)`.

### 2.5 Money & magnitude summary (read this before writing any pricing code)

| Where | Field | Unit / type |
|---|---|---|
| Product (read) | `PriceInCents (price_in_cents)` | **cents**, `long?` |
| Product (write) | `PriceInCents (price_in_cents)` | **cents**, `long` (`!req`) |
| Subscription | `TotalRevenueInCents`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `BalanceInCents`, `CreditBalanceInCents`, `PrepaymentBalanceInCents` | **cents**, `long?` |
| Migration preview | `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` | **cents**, `long?` |
| Component (read) | `UnitPrice (unit_price)` | **dollars as a decimal string** (`"0.01"`), `string?` |
| Component (read) | `PricePerUnitInCents (price_per_unit_in_cents)` | **cents**, `long?` |
| Component price tier (read) | `ComponentPrice.UnitPrice` / `.FormattedUnitPrice` | dollars string / display string |
| Metered component (write) | `MeteredComponent.UnitPrice` | **dollars**, union `UnitPrice1` (string \| double) — use `.String("0.01")` |
| Usage (write) | `CreateUsage.Quantity` | `double?` (unitless count; negative deducts) |
| Usage (read) | `Usage.Quantity` | union `Quantity1` (int \| string) |
| Subscription component | `UnitBalance (unit_balance)` | `int?` (unitless count) |

Convert cents → decimal in your domain layer once (`amountInCents / 100m`), never with `double`.

---

## 3. Trap notes (attach to the step named)

1. **Step 1 — `HttpClient` lifetime.** The SDK does *not* own the `HttpClient`: pass one long-lived instance
   from `IHttpClientFactory` and keep the client (or at least the handler) alive; never `new HttpClient()`
   per request. The generated DI extension registers the client as a **singleton** — safe, but it creates its
   own `HttpClient`, so use the manual registration shown in §2.1 to keep the test seam.
2. **Step 1 — auth ordering.** Set `BasicAuth` on the options *before* constructing the client (or inside
   the `AddMaxioAdvancedBillingClient` callback); it is read at construction. Load the API key from
   configuration/user-secrets — never a literal in code. Password is the literal `"x"`, not your key.
3. **Step 1 — retries.** `options.Retry` retries **idempotent verbs only** (`GET/HEAD/PUT/OPTIONS`), so
   `CreateSubscription`, `CreateUsage`, `CancelSubscription`, `MigrateSubscriptionProduct` (POST/DELETE) are
   **not** retried — your own retry there must be idempotency-aware (usage would double-post).
   `RetryOptions.Timeout` is **per-attempt**, not total. All `RetryOptions` members are `required` → start
   from `RetryOptions.Default()`.
4. **Step 1 — no logging hook.** There is no built-in logging; to trace requests attach a
   `DelegatingHandler` to the named `HttpClient`.
5. **Every list/search call — named arguments.** Ops like `ListProducts` (10 params), `ListSubscriptions`
   (16), `ListSubscriptionComponents` (13), `ListCustomers` (9) have many nullable params with **no C#
   default**; a positional call mis-binds silently. Always write named arguments and always
   `ct: cancellationToken`, never `cancellationToken:`.
6. **Every write — the envelope.** Requests wrap their payload exactly once
   (`CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`,
   `CreateUsageRequest.Usage`, `CancellationRequest.Subscription`,
   `SubscriptionProductMigrationRequest.Migration`, `CreateOrUpdateProductRequest.Product`,
   `CreateMeteredComponent.MeteredComponent`, `CreateProductFamilyRequest.ProductFamily`), and responses
   unwrap once (`.Product`, `.Customer`, `.Subscription`, `.Component`, `.Usage`, `.ProductFamily`,
   `.Migration`). Two exceptions to memorize: `ArchiveComponent` returns a bare `Component`, and
   `InitiateDelayedCancellation` / `CancelDelayedCancellation` return `DelayedCancellationResponse` with only
   a `Message`.
7. **Nullable envelopes.** `SubscriptionResponse.Subscription` and `ProductFamilyResponse.ProductFamily` are
   **nullable** (`?`), while `ProductResponse.Product`, `CustomerResponse.Customer`, `ComponentResponse.Component`,
   `UsageResponse.Usage` are `!req`. Null-check the first two before dereferencing.
8. **Enums are not C# enums.** `StringEnum<T>` records: use the constants, `FromValue(raw)` for server
   values, `.Value` to get the wire string, and `TryGetKnownValue`/`IsKnownValue` before switching — a new
   server-side state must not throw in your mapper.
9. **Unions need factories, never `new`.** `SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`,
   `UnitPrice1`, `AllocatedQuantity2`, `Resume`, `CancelSubscriptionErrorResponse` — construct with
   `Type.Variant(value)` (or the implicit conversion) and read with `TryGet…(out …)`.
10. **Unmodeled JSON is dropped.** Fields not on these records vanish on deserialize — if you need one, it
    isn't reachable through the SDK.
11. **Error handling — three usings, per-operation catch.** `Core.Exceptions` (+`Core.ErrorResponse`, and
    `.Errors` for Case A). Handle **every** `TryGet…` an operation lists and put `TryGetRawError` **last** —
    it is not a catch-all (a 422 with a typed payload leaves it `false`). Do not funnel typed errors through
    a helper typed as `ApiError`: the base only exposes `TryGetRawError`, so typed messages get lost and you
    print a bare type name.
12. **Error handling — no `…Result` variants.** This SDK generates none; every operation throws. Wrap all
    of them, reads included.
13. **Error handling — connection failures.** `HttpRequestException` / `TaskCanceledException` are not
    `SdkException<…>`. In `MaxioBillingClient` catch them at the boundary and rethrow your own
    `BillingUnavailableException` so ApplicationCore sees one failure type.
14. **`RawError.ReadAsJson<T>()` throws `JsonException`** on non-JSON bodies — use `ReadAsString()` for log
    lines; extract a message best-effort and fall back to the generic text.
15. **Handle-vs-id asymmetry (why you cache ids).** Handles work for: `ReadProductByHandle`,
    `FindComponent(handle)`, `ReadComponent(..., "handle:api-call")`, `CreateUsage`/`ListUsages`
    (`ComponentIdModel.String("handle:…")`), `CreateSubscription.ProductHandle`/`CustomerReference`,
    migration `ProductHandle`, `ReadCustomerByReference`. Handles do **not** work for:
    `ReadProductFamily(int)`, `ListComponentsForProductFamily(int)`, `ReadSubscriptionComponent(int,int)`,
    `ListCustomerSubscriptions(int)`, `ReadSubscription(int)`, all lifecycle/migration ops (`int subscriptionId`).
    So: resolve family id + component id at startup and cache them; keep the subscription id on your own
    aggregate.
16. **Testing.** The `HttpClient` constructor argument is the seam — stub with a custom
    `HttpMessageHandler`, and assert the exact `SdkException<{Operation}Error>` / `SdkException<RawError>`
    from the table above on error paths. Match eShopOnWeb's existing test stack (xUnit + Moq).
17. **Ebb server group.** Only the events-ingest ops (`RecordEvent`, `BulkRecordEvents`) use
    `options.Server.Ebb`; nothing in this scope does — metered usage goes through `CreateUsage` on the
    Production host.

---

## 4. Assumptions & Blockers

**Assumptions**

1. `eshop-subscribe`, `eshop-pro`, `basic-plan`, `api-call` are handles on the `cp-exp-2` site; the plan
   resolves them at runtime and never persists numeric ids beyond process cache.
2. Region US ⇒ `ServerEnvironment.Us` and the `Production` server group; the Ebb group stays at defaults.
3. "Metered usage" means classic metered components (`CreateUsage`), not Events-Based Billing streams.
4. Subscriptions are created without a payment profile, which requires the products to carry
   `require_credit_card = false` — step 11 sets it; for pre-existing products you must verify
   `Product.RequireCreditCard == false` before promising a card-free signup.
5. Collection method is left to the site default unless you explicitly set
   `CreateSubscription.PaymentCollectionMethod`.
6. UC0 seed tooling is operator-only (a console/admin path), not part of the `IBillingClient` runtime
   contract.

**Blockers / gaps in what the SDK exposes**

1. **No taxable flag on product create/update.** `CreateOrUpdateProduct` has no `taxable` field (it has
   `TaxCode` only), although `Product.Taxable` is returned. `taxable = false` for a product cannot be set
   through this SDK — set it in the Maxio UI, or rely on the site default.
2. **No setup fee on product create.** `CreateOrUpdateProduct` has no `initial_charge_in_cents`
   (`Product.InitialChargeInCents` is read-only here). "No setup fee" is therefore the default; a setup fee
   could not be set even if you wanted one.
3. **`Description` is required when creating a product** (`string !req`) — you must supply text even if the
   business doesn't want one.
4. **No un-archive operation** for products or components in the map (`operations/Products.md`,
   `operations/Components.md` list only `ArchiveProduct` / `ArchiveComponent`). Archiving is one-way through
   this SDK.
5. **`ListProductFamilies` has no pagination parameters** — no `page`/`perPage` exist on it, so a very large
   family list cannot be paged. `UNVERIFIED`: whether the server caps this response.
6. **`ListSubscriptions` cannot filter by customer**; use `ListCustomerSubscriptions(int customerId)`, which
   in turn has no paging parameters. `UNVERIFIED`: whether it caps at 50/100 for a customer with many
   subscriptions.
7. **No handle form for `ReadProductFamily`** through the SDK (`int id`), despite the endpoint doc
   mentioning `handle:my-family` — the family must be found by scanning `ListProductFamilies`.
8. **Delayed product change is not on the migration endpoint.** Apply-at-next-renewal must go through
   `UpdateSubscription` with `ProductChangeDelayed = true`, and there is no proration preview for that path
   (the preview endpoint models the migration, i.e. the immediate path).
9. **Two proration knobs, no documented precedence** — `SubscriptionProductMigration.PreservePeriod` and
   `SubscriptionProductMigration.Proration.PreservePeriod` both exist in the generated model.
   `UNVERIFIED` which the API honours if both are set; directive: set only the top-level one, leave
   `Proration = null`, and log preview-vs-actual amounts.
10. **`CreateCustomerError`'s typed 422 payload is `CustomerErrorResponse1 { Errors { per_page, price_point } }`** —
    a shape that cannot carry a duplicate-`reference` message. This is a map-visible mismatch between the
    generated error model and the errors this endpoint really returns. Directive: on 422, try the typed
    accessor, then `TryGetRawError` → `ReadAsString()` best-effort, and decide the control flow by re-running
    `ReadCustomerByReference`, not by parsing text. `UNVERIFIED`: the actual wire body for this 422.
11. **`Usage.Quantity` comes back as a `string|int` union** while you send a `double` — a fractional quantity
    round-trips through a shape that has no fractional variant. `UNVERIFIED` how a fractional usage is
    echoed; send whole numbers for `api-call`, and when reading, handle both union arms and parse the string
    with `CultureInfo.InvariantCulture`.
12. **Period-to-date total has no dedicated endpoint.** `UnitBalance` on the subscription component is the
    supported running total (per the `CreateUsage` operation note); everything else is client-side summing of
    `ListUsages`. `UNVERIFIED`: exact reset timing of `unit_balance` relative to the period boundary.
13. **No webhook/signature helpers are in scope here** — if you later need webhook verification, ask for a
    lookup on `operations/Webhooks.md`; nothing in this sheet covers it.
