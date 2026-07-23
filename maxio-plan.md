# Maxio Advanced Billing — .NET 8 integration contract sheet

Generated from the bundled SDK map (`sdk-map.md` + `map/operations/*` + `map/models/*`), SDK
`v1.0.2` / commit `15db14b`. Rows that were resolved against SDK source name the source file.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Client setup: `HttpClient` + options, Basic auth, site/base-URL/region from configuration | — |
| 2 | Catalog read by handle: product family, its products, its components | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily`, `Products.ReadProductByHandle`, `Components.FindComponent`, `Components.ListComponentsForProductFamily` |
| 3 | Customer idempotency: lookup by reference, else create | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 4 | Subscribe: create subscription on `product_handle` for an existing customer | `Subscriptions.CreateSubscription` |
| 5 | Read/refresh subscription state; list a customer's subscriptions | `Subscriptions.ReadSubscription`, `Customers.ListCustomerSubscriptions`, `Subscriptions.ListSubscriptions` |
| 6 | Usage: record a metered event; period-to-date total | `SubscriptionComponents.CreateUsage`, `SubscriptionComponents.ReadSubscriptionComponent`, `SubscriptionComponents.ListUsages` |
| 7 | Plan change: preview proration → commit now, or schedule at renewal | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `SubscriptionProducts.MigrateSubscriptionProduct`, `Subscriptions.UpdateSubscription` |
| 8 | Lifecycle: hold/resume/cancel/delayed-cancel/clear/reactivate | `SubscriptionStatus.*` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it**
> (e.g. `MaxioAdvancedBilling.Models.Enums.SubscriptionState`,
> `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference`,
> `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`, and the
> **client-config types**: `MaxioAdvancedBilling.Servers.ServerEnvironment`,
> `MaxioAdvancedBilling.Core.Configuration.RetryOptions`,
> `MaxioAdvancedBilling.ServerOptions`). Do not drop these to the root or `.Models`,
> or the implementer guesses the wrong `using` and the build breaks.
> **Correction to the usual pattern:** `ServerOptions` is in the **root** namespace
> `MaxioAdvancedBilling` (verified in source `ServerOptions.cs`), *not*
> `MaxioAdvancedBilling.Core.Configuration`. `RetryOptions` *is* in
> `MaxioAdvancedBilling.Core.Configuration` (`Core/Configuration/RetryOptions.cs`).

### 2.0 SDK identity, client construction, auth, servers

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (install by this id) | `sdk-map.md` |
| Version pinned by this sheet | `v1.0.2` (commit `15db14b`) | `sdk-map.md` |
| Root namespace (the `using`) | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client type | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Only constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Target framework | `netstandard2.0` → fine on `net8.0` | `sdk-map.md` |
| DI extension | `IServiceCollection.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` in namespace `MaxioAdvancedBilling` | `sdk-map.md`, source `ServiceCollectionExtensions.cs` |

`MaxioAdvancedBillingClientOptions` properties (all four, verbatim; source `MaxioAdvancedBillingClientOptions.cs`):

| Property | Type (fully-qualified) | Default |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` (= `Us`) |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new()` |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` |

`BasicAuthCredentials` (source `Core/Authentication/Basic/BasicAuthCredentials.cs`): `public required string Username { get; init; }`, `public required string Password { get; init; }`.
**Username = the Maxio/Chargify API key; Password = the literal `"x"`.**

`ServerEnvironment` (source `Servers/ServerEnvironment.cs`) is a `StringEnum`, **not** a C# enum:
`ServerEnvironment.Us` (wire `"US"`), `ServerEnvironment.Eu` (wire `"EU"`), `ServerEnvironment.Default()` → `Us`.
Bind from configuration with `ServerEnvironment.FromValue(configValue)` when the value isn't known at compile time.

`ServerOptions` shape (source `ServerOptions.cs`, `Servers/ProductionOptions.cs`):

```
MaxioAdvancedBilling.ServerOptions
  .Production : MaxioAdvancedBilling.Servers.ProductionOptions
      .Us : ProductionOptions.UsOptions { string BaseUrl = "https://{site}.chargify.com"; string Site = "subdomain"; }
      .Eu : ProductionOptions.EuOptions { string BaseUrl = "https://{site}.ebilling.maxio.com"; string Site = "subdomain"; }
  .Ebb : MaxioAdvancedBilling.Servers.EbbOptions   // .Us/.Eu, BaseUrl "https://events.chargify.com/{site}" — only used by the Ebb event-ingest ops (RecordEvent/BulkRecordEvents); NOT used by anything in this plan
```

Canonical construction (everything in this plan uses the **Production** group):

```csharp
using MaxioAdvancedBilling;                                   // client, options, ServerOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;         // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                           // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth   = new BasicAuthCredentials { Username = cfg["Maxio:ApiKey"]!, Password = "x" },
    Environment = ServerEnvironment.FromValue(cfg["Maxio:Region"] ?? "US"),   // "US" | "EU"
};
options.Server.Production.Us.Site = cfg["Maxio:Subdomain"]!;   // fills {site} in the US template
options.Server.Production.Eu.Site = cfg["Maxio:Subdomain"]!;   // fills {site} in the EU template

// Arbitrary base URL (mock/dev host) — set the BaseUrl of the group for the SELECTED environment:
if (!string.IsNullOrWhiteSpace(cfg["Maxio:BaseUrl"]))
    options.Server.Production.Us.BaseUrl = cfg["Maxio:BaseUrl"]!;   // e.g. "http://localhost:8080"

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Base-URL override semantics (verified in source `Core/TemplateParamsFactory.cs`):** the final URL is
`ExpandTemplate(BaseUrl, [site=Site]).TrimEnd('/') + "/" + path`. Expansion is a literal
`template.Replace("{site}", value)`, so a `BaseUrl` with **no** `{site}` placeholder (e.g.
`http://localhost:8080`) is used verbatim — `Site` is simply never substituted. Overriding
`Production.Us.BaseUrl` only affects the **US** environment; if `Environment = Eu` you must override
`Production.Eu.BaseUrl` instead. Safest: set both `.Us.BaseUrl` and `.Eu.BaseUrl` when a config override is present.

Controller accessors used here: `client.ProductFamilies`, `client.Products`, `client.Components`,
`client.Customers`, `client.Subscriptions`, `client.SubscriptionComponents`,
`client.SubscriptionProducts`, `client.SubscriptionStatus`.

### 2.1 PRODUCT FAMILIES & PRODUCTS

| Op | Signature (verbatim) | Request model | Response envelope → inner | Error case | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must be passed explicitly (`null` to skip) | — | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` → `ProductFamily?` | **B**: `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse.ProductFamily` | **B**: `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` must be passed explicitly | — | `IReadOnlyList<ProductResponse>`; each `.Product` → `Product` (**required**, non-null) | **A**: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | `page` / `perPage` (wire `page`, `per_page`) | `operations/ProductFamilies.md` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse.Product` | **B**: `SdkException<RawError>` | none | `operations/Products.md` |
| `client.Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse.Product` | **B**: `SdkException<RawError>` | none | `operations/Products.md` |
| `client.Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — | `IReadOnlyList<ProductResponse>` | **B**: `SdkException<RawError>` | `page` / `perPage` | `operations/Products.md` |

**BY-HANDLE PATHS (numeric IDs are not stable):**
- **Product by handle → `Products.ReadProductByHandle("eshop-pro", ct: ct)`.** This is the only true
  by-handle product read; use it for `eshop-pro` and `basic-plan`.
- **Product family by handle → there is NO by-handle read.** `ReadProductFamily` takes `int id`, so the
  documented `handle:my-family` path string is **not reachable** through this signature. Resolve the family
  once via `ListProductFamilies(null, null, null, null, null, ct: ct)` and match
  `r.ProductFamily?.Handle == "eshop-subscribe"`, then cache the `Id` for the process lifetime.
- `ListProductsForProductFamily` takes `string productFamilyId` — pass the resolved id as
  `familyId.ToString(CultureInfo.InvariantCulture)`.

`ProductFamily` (`records-3-Of-Su.md`, `Models/ProductFamily.cs`) — all optional:
`Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `AccountingCode (accounting_code): string?`,
`Description (description): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`,
`ArchivedAt (archived_at): DateTimeOffset?`.
`ProductFamilyResponse`: `ProductFamily (product_family): ProductFamily?` — **nullable**, unlike `ProductResponse.Product`.

`Product` fields you asked for (`records-3-Of-Su.md`, `Models/Product.cs`; all nullable):

| Need | Field |
|---|---|
| id / name / handle / description | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?` |
| **price** | `PriceInCents (price_in_cents): long?` — **CENTS** (integer). Display = `PriceInCents / 100m`. |
| interval | `Interval (interval): int?` |
| interval unit | `IntervalUnit (interval_unit): IntervalUnit?` → `MaxioAdvancedBilling.Models.Enums.IntervalUnit` = `Day (day)`, `Month (month)` |
| payment method required | `RequireCreditCard (require_credit_card): bool?` (hard requirement) and `RequestCreditCard (request_credit_card): bool?` (asked-for-but-optional). Gate the checkout flow on `RequireCreditCard == true`. |
| other useful | `InitialChargeInCents (initial_charge_in_cents): long?` (cents), `TrialPriceInCents (trial_price_in_cents): long?` (cents), `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`, `ExpirationInterval (expiration_interval): int?`, `ExpirationIntervalUnit (expiration_interval_unit): ExpirationIntervalUnit?` (`Day`/`Month`/`Never`), `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `Taxable (taxable): bool?` |

`ListProductsFilter` (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — pass `null`.

### 2.2 COMPONENTS

| Op | Signature (verbatim) | Response envelope → inner | Error case | Pagination | Map page |
|---|---|---|---|---|---|
| `client.Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` — `GET /components/lookup.json?handle=` | `ComponentResponse.Component` → `Component` (**required**) | **B**: `SdkException<RawError>` | none | `operations/Components.md` |
| `client.Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 7 params `includeArchived`…`startDatetime` must be passed explicitly. **Note the date params here are `string?`, not `DateTimeOffset?`.** | `IReadOnlyList<ComponentResponse>` | **B**: `SdkException<RawError>` | `page` / `perPage` | `operations/Components.md` |
| `client.Components.ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` accepts `"handle:api-call"` (the `handle:` prefix is required for handle form) | `ComponentResponse.Component` | **B**: `SdkException<RawError>` | none | `operations/Components.md` |

**BY-HANDLE PATH for the metered component:** prefer `client.Components.FindComponent("api-call", ct: ct)` —
it needs no family id and no `handle:` prefix. `ReadComponent(familyId, "handle:api-call", ct: ct)` is the
fallback when you want the family-scoped read.

`Component` (`records-1-Ac-Cr.md`, `Models/Component.cs`) — the fields you asked for:

| Need | Field |
|---|---|
| id / name / handle | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?` |
| **kind** | `Kind (kind): ComponentKind?` — type is `MaxioAdvancedBilling.Models.Enums.ComponentKind`, a **`StringEnum`, not a C# enum**. **The metered constant is `ComponentKind.MeteredComponent` (wire `metered_component`).** Compare with `==` (records compare by value) or `component.Kind?.Value == "metered_component"`. |
| pricing scheme | `PricingScheme (pricing_scheme): PricingScheme?` → `MaxioAdvancedBilling.Models.Enums.PricingScheme` = `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| **unit price** | `UnitPrice (unit_price): string?` — **DOLLARS as a decimal string** (parse with `decimal.Parse(s, CultureInfo.InvariantCulture)`); and `PricePerUnitInCents (price_per_unit_in_cents): long?` — **CENTS**. Prefer `PricePerUnitInCents` when non-null; fall back to parsing `UnitPrice`. |
| price tiers | `Prices (prices): IReadOnlyList<ComponentPrice?>?`, `OveragePrices (overage_prices): IReadOnlyList<ComponentPrice?>?` — **note the element type is nullable `ComponentPrice?`, so null-check each element** |
| other | `UnitName (unit_name): string?`, `Description (description): string?`, `Recurring (recurring): bool?`, `Archived (archived): bool?`, `ProductFamilyId (product_family_id): int?`, `ProductFamilyHandle (product_family_handle): string?`, `DefaultPricePointId (default_price_point_id): int?`, `AllowFractionalQuantities (allow_fractional_quantities): bool?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` |

`ComponentPrice` (`records-1-Ac-Cr.md`, `Models/ComponentPrice.cs`): `Id (id): int?`, `ComponentId (component_id): int?`,
`StartingQuantity (starting_quantity): int?`, `EndingQuantity (ending_quantity): int?`,
`UnitPrice (unit_price): string?` — **DOLLARS decimal string**, `PricePointId (price_point_id): int?`,
`FormattedUnitPrice (formatted_unit_price): string?` (already money-formatted for display),
`SegmentId (segment_id): int?`.

`ListComponentsFilter` (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — pass `null`.

### 2.3 CUSTOMERS

| Op | Signature (verbatim) | Request model | Response envelope → inner | Error case | Map page |
|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json?reference=` | — | `CustomerResponse.Customer` → `Customer` (**required**) | **B**: `SdkException<RawError>` | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` | `CustomerResponse.Customer` | **A**: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/Customers.md` |
| `client.Customers.ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse.Customer` | **B**: `SdkException<RawError>` | `operations/Customers.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` → `Subscription?` | **B**: `SdkException<RawError>` | `operations/Customers.md` |

`CreateCustomer` (`records-1-Ac-Cr.md`, `Models/CreateCustomer.cs`) — **`FirstName`, `LastName`, `Email` are C# `required`**:
`FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`,
then optional: `CcEmails`, `Organization`, `Reference (reference): string?`, `Address`, `Address2 (address_2)`,
`City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber (vat_number)`, `TaxExempt (tax_exempt): bool?`,
`TaxExemptReason (tax_exempt_reason)`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id)`.

```csharp
var created = await client.Customers.CreateCustomer(
    new CreateCustomerRequest
    {
        Customer = new CreateCustomer
        {
            FirstName = firstName, LastName = lastName, Email = email,
            Reference = userEmail,          // our idempotency key — must be unique per customer
        }
    },
    ct: ct);
var customer = created.Customer;           // envelope: CustomerResponse.Customer
```

`Customer` (read side, `records-2-Cr-Ne.md`, `Models/Customer.cs`) — all optional:
`Id (id): int?`, `FirstName`, `LastName`, `Email`, `Reference (reference): string?`, `Organization`,
`CreatedAt/UpdatedAt: DateTimeOffset?`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`,
`Verified (verified): bool?`, `Locale`, `ParentId`, `Maxioid (maxioid): string?`, plus tax/portal fields.

**"Not found" surfaces as an EXCEPTION, never `null`.** Every operation in this SDK is throw-only (no
`…Result` variants), and `ReadCustomerByReference` returns a non-nullable `CustomerResponse`. So the
idempotent get-or-create is:

```csharp
MaxioAdvancedBilling.Models.Customer? existing = null;
try { existing = (await client.Customers.ReadCustomerByReference(userEmail, ct: ct)).Customer; }
catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound) { /* absent */ }
```

`UNVERIFIED (live-traffic only): the exact status the lookup returns for an unknown reference (404 vs an
empty 200 body).` **Defensive directive:** treat `HttpStatusCode.NotFound` **and** a successful response whose
`.Customer` is unusable (null/`Id == null`) as "customer absent", then create. Do not treat any other status
as absent — rethrow it. Note `CustomerResponse.Customer` is declared `!req`, so a truly empty 200 body would
throw a deserialization error rather than yield null — catch that path as a hard failure, not as "absent".

### 2.4 SUBSCRIPTIONS

| Op | Signature (verbatim) | Request model | Response envelope → inner | Error case | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` | `SubscriptionResponse.Subscription` → `Subscription?` (**nullable**) | **A**: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed explicitly (`null` to skip) | — | `SubscriptionResponse.Subscription` | **B**: `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| `client.Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` must be passed explicitly | — | `IReadOnlyList<SubscriptionResponse>` | **B**: `SdkException<RawError>` | `page` / `perPage` | `operations/Subscriptions.md` |
| `client.Subscriptions.UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` | `UpdateSubscriptionRequest { Subscription (subscription): UpdateSubscription !req }` | `SubscriptionResponse.Subscription` | **A**: `SdkException<UpdateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | — | `SubscriptionResponse.Subscription` | **A**: `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

**"List subscriptions filtered by customer": there is NO `customer_id` query param on `ListSubscriptions`.**
Use `client.Customers.ListCustomerSubscriptions(customerId, ct: ct)` (no pagination — returns the full list)
for per-customer listing; use `ListSubscriptions` only for site-wide listing with a state filter.

`CreateSubscription` (`records-2-Cr-Ne.md`, `Models/CreateSubscription.cs`) — **all fields are optional; NONE of
the identity fields are unions.** The ones this integration needs:

| Field | Type | Use |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` | **plain string — set `"eshop-pro"` / `"basic-plan"`. This is the by-handle product selector.** |
| `ProductId (product_id)` | `int?` | alternative to `ProductHandle` |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | optional price-point by handle |
| `ProductPricePointId (product_price_point_id)` | `int?` | optional price-point by id |
| `CustomerId (customer_id)` | `int?` | existing customer by id |
| `CustomerReference (customer_reference)` | `string?` | existing customer by our reference |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | create customer inline instead |
| `Components (components)` | `IReadOnlyList<CreateSubscriptionComponent>?` | attach components at signup |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` (`Automatic`/`Remittance`/`Prepaid`/`Invoice`) | |
| `PaymentProfileId (payment_profile_id)` | `int?` | existing card |
| `Reference (reference)` | `string?` | our subscription reference |
| `ProductChangeDelayed (product_change_delayed)` | `bool?` | (also present at create) |
| `CouponCode (coupon_code)` / `CouponCodes (coupon_codes)` | `string?` / `IReadOnlyList<string>?` | |
| `NextBillingAt` / `InitialBillingAt` / `PreviousBillingAt` / `ExpiresAt` / `CanceledAt` / `ActivatedAt` | `DateTimeOffset?` | |
| `Currency (currency)` | `string?` | |

> **The ONLY union on `CreateSubscription` is `OfferId (offer_id): OfferId?`** (`unions.md`). Everything you
> need — `CustomerId`, `CustomerReference`, `CustomerAttributes`, `ProductHandle`, `ProductId` — is a plain
> scalar/record, so **no factory methods are needed here**; just set the one you want and leave the others null.
> "Union" only appears elsewhere on the create body (`OfferId`) and inside `CreateSubscriptionComponent`
> (`ComponentId: ComponentId1?`, `AllocatedQuantity: AllocatedQuantity3?`, `PricePointId: PricePointId2?`).

```csharp
var resp = await client.Subscriptions.CreateSubscription(
    new CreateSubscriptionRequest
    {
        Subscription = new CreateSubscription
        {
            CustomerId    = customerId,       // OR CustomerReference = userEmail — set exactly one
            ProductHandle = "eshop-pro",      // by handle; ids are not stable
        }
    },
    ct: ct);
var subscription = resp.Subscription;         // SubscriptionResponse.Subscription — NULLABLE
```

`Subscription` (`records-3-Of-Su.md`, `Models/Subscription.cs`) — the fields you asked for (all nullable):

| Need | Field |
|---|---|
| id | `Id (id): int?` |
| **state** | `State (state): SubscriptionState?` → `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (StringEnum). Constants: `Active (active)`, `Trialing (trialing)`, `PastDue (past_due)`, `Canceled (canceled)`, `Paused (paused)`, `OnHold (on_hold)`, plus `Pending`, `FailedToCreate`, `Assessing`, `SoftFailure`, `Suspended`, `Expired`, `Unpaid`, `TrialEnded`, `AwaitingSignup` |
| period dates | `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` |
| nested product | `Product (product): Product?` (the full `Product` record above) |
| nested customer | `Customer (customer): Customer?` |
| balance / revenue | `BalanceInCents (balance_in_cents): long?` **CENTS**, `TotalRevenueInCents (total_revenue_in_cents): long?` **CENTS**, `ProductPriceInCents (product_price_in_cents): long?` **CENTS**, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` **CENTS**, `CreditBalanceInCents (credit_balance_in_cents): long?` **CENTS**, `PrepaymentBalanceInCents (prepayment_balance_in_cents): long?` **CENTS** |
| cancellation | `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CanceledAt (canceled_at): DateTimeOffset?`, `DelayedCancelAt (delayed_cancel_at): DateTimeOffset?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `CancellationMessage (cancellation_message): string?`, `CancellationMethod (cancellation_method): CancellationMethod?`, `ReasonCode (reason_code): string?` |
| pending plan change | `NextProductId (next_product_id): int?`, `NextProductHandle (next_product_handle): string?`, `NextProductPricePointId (next_product_price_point_id): int?` |
| hold/resume | `OnHoldAt (on_hold_at): DateTimeOffset?`, `AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` |
| misc | `Reference (reference): string?`, `PreviousState (previous_state): SubscriptionState?`, `TrialStartedAt`/`TrialEndedAt`/`ActivatedAt`/`ExpiresAt`/`CreatedAt`/`UpdatedAt: DateTimeOffset?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `ProductPricePointId (product_price_point_id): int?`, `SelfServicePageToken (self_service_page_token): string?` (only with `include`), `Currency (currency): string?` |

`SubscriptionInclude` enum (for `ReadSubscription`): `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)`.
`SubscriptionListInclude` (for `ListSubscriptions`): `SelfServicePageToken (self_service_page_token)`.
`SubscriptionStateFilter` (for `ListSubscriptions.state`) — **a different enum from `SubscriptionState`**:
`Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`,
`Suspended`, `TrialEnded`, `Trialing`, `Unpaid` (`enums.md`). Note it has **no `Paused`** member.

### 2.5 USAGE (metered component `api-call`)

| Op | Signature (verbatim) | Request model | Response envelope → inner | Error case | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` | `CreateUsageRequest { Usage (usage): CreateUsage !req }` | `UsageResponse.Usage` → `Usage` (**required**) | **A**: `SdkException<CreateUsageError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/SubscriptionComponents.md` |
| `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 4 params `sinceId`…`untilDate` must be passed explicitly | — | `IReadOnlyList<UsageResponse>` | **B**: `SdkException<RawError>` | `page` / `perPage` (wire `page`, `per_page`) | `operations/SubscriptionComponents.md` |
| `client.SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — **both `int`, no handle form here** | `SubscriptionComponentResponse.Component` → `SubscriptionComponent?` (**property is named `Component`, typed `SubscriptionComponent`**) | **A**: `SdkException<ReadSubscriptionComponentError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/SubscriptionComponents.md` |
| `client.SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionComponentResponse>` | **B**: `SdkException<RawError>` | **none** | `operations/SubscriptionComponents.md` |

**Unions on the usage path (`unions.md`, namespace `MaxioAdvancedBilling.Models.AnyOf`):**

| Union | Factories | Readers | Implicit from |
|---|---|---|---|
| `SubscriptionIdOrReference` | `SubscriptionIdOrReference.Int(int)`, `SubscriptionIdOrReference.String(string)` | `TryGetInt(out int)`, `TryGetString(out string)` | `int`, `string` |
| `ComponentIdModel` | `ComponentIdModel.Int(int)`, `ComponentIdModel.String(string)` | `TryGetInt(out int)`, `TryGetString(out string)` | `int`, `string` |
| `Quantity1` (on `Usage.Quantity`) | `Quantity1.Int(int)`, `Quantity1.String(string)` | `TryGetInt(out int)`, `TryGetString(out string)` | `int`, `string` |
| `AllocatedQuantity2` (on `SubscriptionComponent.AllocatedQuantity`) | `AllocatedQuantity2.Int(int)`, `AllocatedQuantity2.String(string)` | `TryGetInt(out int)`, `TryGetString(out string)` | `int`, `string` |

**BY-HANDLE PATH for usage:** `ComponentIdModel` accepts a string, and the route segment is
`{component_id}` on `/subscriptions/{subscription_id_or_reference}/components/{component_id}/usages.json`.
Pass the handle form as `ComponentIdModel.String("handle:api-call")` (same `handle:` prefix convention the
`ReadComponent`/`UpdateComponent` notes document for this path segment). If you prefer zero ambiguity,
resolve the numeric id once via `Components.FindComponent("api-call", ct: ct)` and pass
`ComponentIdModel.Int(component.Id!.Value)` — that also gives you the id `ReadSubscriptionComponent` requires.

`CreateUsage` (`records-2-Cr-Ne.md`, `Models/CreateUsage.cs`) — all optional:
`Quantity (quantity): double?`, `PricePointId (price_point_id): string?`, `Memo (memo): string?`,
`BillingSchedule (billing_schedule): BillingSchedule?`, `CustomPrice (custom_price): ComponentCustomPrice?`.
**`Quantity` is `double?` on the write side.**

```csharp
var usage = await client.SubscriptionComponents.CreateUsage(
    SubscriptionIdOrReference.Int(subscriptionId),
    ComponentIdModel.Int(componentId),                 // or .String("handle:api-call")
    new CreateUsageRequest { Usage = new CreateUsage { Quantity = 1d, Memo = "api call" } },
    ct: ct);
var recorded = usage.Usage;                            // UsageResponse.Usage (required, non-null)
```

`Usage` (read side, `records-4-Su-We.md`, `Models/Usage.cs`):
`Id (id): long?`, `Memo (memo): string?`, `CreatedAt (created_at): DateTimeOffset?`,
`PricePointId (price_point_id): int?`, **`Quantity (quantity): Quantity1?` (union — `int` or `string`)**,
`OverageQuantity (overage_quantity): int?`, `ComponentId (component_id): int?`,
`ComponentHandle (component_handle): string?`, `SubscriptionId (subscription_id): int?`.
**Read the quantity as:** `if (u.Quantity is { } q && (q.TryGetInt(out var i) ? … : q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) …))` — handle **both** variants.

**WHICH OPERATION GIVES THE RUNNING PERIOD-TO-DATE TOTAL:**
`client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, componentId, ct: ct)` →
`.Component.UnitBalance (unit_balance): int?`. The SDK's own `PreviewRenewal` documentation
(`operations/SubscriptionStatus.md`) states the renewal preview uses "Current metered usage `unit_balance`
for metered components", i.e. `unit_balance` **is** the current-period accumulated metered usage. Use it as
the period-to-date total; use `ListUsages` (summing `Usage.Quantity`) only when you need the itemized events
or a custom `sinceDate`/`untilDate` window — and remember `ListUsages` is paged (`perPage` default 20), so
you must loop pages before summing or you will silently undercount.

`SubscriptionComponent` (`records-3-Of-Su.md`, `Models/SubscriptionComponent.cs`) — key fields:
`Id (id): int?`, `ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`,
`Name (name): string?`, `Kind (kind): ComponentKind?`, `UnitName (unit_name): string?`, `Enabled (enabled): bool?`,
**`UnitBalance (unit_balance): int?`**, `AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (union),
`PricingScheme (pricing_scheme): PricingScheme?`, `PricePointId/Handle/Name/Type`, `SubscriptionId (subscription_id): int?`,
`Currency (currency): string?`, `HistoricUsages (historic_usages): IReadOnlyList<HistoricUsage>?`
(only when `include=historic_usages`; `HistoricUsage` = `TotalUsageQuantity (total_usage_quantity): double?`,
`BillingPeriodStartsAt`, `BillingPeriodEndsAt`), `Interval`, `IntervalUnit`.

`UNVERIFIED (live-traffic only): whether this site's metered component reports its period-to-date usage in
`unit_balance` vs. only through the usage list.` **Defensive directive:** read `UnitBalance` first; when it is
null, fall back to paging `ListUsages` and summing `Usage.Quantity` over the current period
(`sinceDate: subscription.CurrentPeriodStartedAt`), and if that also yields nothing show "usage unavailable"
rather than 0.

### 2.6 PLAN CHANGE — preview, commit now, commit at renewal

**(a) Proration preview** — `client.SubscriptionProducts.PreviewSubscriptionProductMigration`
(`operations/SubscriptionProducts.md`):

- Signature: `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly.
- Request: `SubscriptionMigrationPreviewRequest { Migration (migration): SubscriptionMigrationPreviewOptions !req }`.
- `SubscriptionMigrationPreviewOptions` (`records-4-Su-We.md`, `Models/SubscriptionMigrationPreviewOptions.cs`):
  `ProductId (product_id): int?`, `ProductPricePointId (product_price_point_id): int?`,
  `IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge (include_initial_charge): bool? = false`,
  `IncludeCoupons (include_coupons): bool? = true`, `PreservePeriod (preserve_period): bool? = false`,
  **`ProductHandle (product_handle): string?`** ← the by-handle selector,
  `ProductPricePointHandle (product_price_point_handle): string?`,
  `Proration (proration): Proration?` (record: `PreservePeriod (preserve_period): bool?`),
  `ProrationDate (proration_date): DateTimeOffset?`.
- Response: `SubscriptionMigrationPreviewResponse { Migration (migration): SubscriptionMigrationPreview !req }`.
- **`SubscriptionMigrationPreview` fields — exact names and units (all `long?`, all CENTS):**

  | Your name | **Actual SDK field** | Unit |
  |---|---|---|
  | charge_in_cents | `ChargeInCents (charge_in_cents): long?` | **cents** |
  | payment_due_in_cents | `PaymentDueInCents (payment_due_in_cents): long?` | **cents** |
  | credit_in_cents | **there is NO `credit_in_cents`** — the field is `CreditAppliedInCents (credit_applied_in_cents): long?` | **cents** |
  | (extra) | `ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?` | **cents** |

- Error: **A** — `SdkException<PreviewSubscriptionProductMigrationError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback].

**(b1) Commit "apply now with proration"** — `client.SubscriptionProducts.MigrateSubscriptionProduct`:

- Signature: `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)`.
- Request: `SubscriptionProductMigrationRequest { Migration (migration): SubscriptionProductMigration !req }`.
- `SubscriptionProductMigration` (`records-4-Su-We.md`, `Models/SubscriptionProductMigration.cs`) — **same shape as the preview options minus `ProrationDate`**:
  `ProductId (product_id): int?`, `ProductPricePointId (product_price_point_id): int?`,
  `IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge (include_initial_charge): bool? = false`,
  `IncludeCoupons (include_coupons): bool? = true`, `PreservePeriod (preserve_period): bool? = false`,
  **`ProductHandle (product_handle): string?`**, `ProductPricePointHandle (product_price_point_handle): string?`,
  `Proration (proration): Proration?`.
- Response: `SubscriptionResponse.Subscription`.
- Error: **A** — `SdkException<MigrateSubscriptionProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback].
- Pass the **same** `ProductHandle` + flags you used in the preview so the committed numbers match the previewed ones.

**(b2) Commit "at next renewal, no proration" (delayed product change)** —
`client.Subscriptions.UpdateSubscription` with `ProductChangeDelayed = true`:

```csharp
await client.Subscriptions.UpdateSubscription(
    subscriptionId,
    new UpdateSubscriptionRequest
    {
        Subscription = new UpdateSubscription
        {
            ProductHandle        = "basic-plan",
            ProductChangeDelayed = true,      // schedules the change for the next renewal; no proration
        }
    },
    ct: ct);
```

- `UpdateSubscription` (`records-4-Su-We.md`, `Models/UpdateSubscription.cs`) — relevant fields:
  `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`,
  **`ProductChangeDelayed (product_change_delayed): bool?`**,
  **`NextProductId (next_product_id): string?` — note the type is `string?`, not `int?`**,
  `NextProductPricePointId (next_product_price_point_id): string?`,
  `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`,
  `NextBillingAt (next_billing_at): DateTimeOffset?`, `Reference (reference): string?`,
  `SnapDay (snap_day): SnapDay1?` (union), `NetTerms (net_terms): NetTerms1?` (union),
  `Components (components): IReadOnlyList<UpdateSubscriptionComponent>?`, `CustomPrice`, `CreditCardAttributes`.
- **How to CLEAR a scheduled (delayed) product change:** set `NextProductId` to the **empty string** —
  `new UpdateSubscription { NextProductId = "" }`. The SDK's own `UpdateSubscription` notes state: *"To cancel a
  delayed product change, set `next_product_id` to an empty string."* That is exactly why the field is typed
  `string?` here while the read-side `Subscription.NextProductId` is `int?`.
- Verify the schedule afterwards by reading the subscription: `Subscription.NextProductId (int?)` /
  `Subscription.NextProductHandle (string?)` are non-null while a delayed change is pending.

### 2.7 LIFECYCLE (all on `client.SubscriptionStatus`, page `operations/SubscriptionStatus.md`)

| Action | Signature (verbatim) | Request model | Returns | Error case |
|---|---|---|---|---|
| **Pause / hold** | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `POST /subscriptions/{id}/hold.json` | `PauseRequest { Hold (hold): AutoResume? }`; `AutoResume { AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset? }`. Pass `null` body for an indefinite hold. | `SubscriptionResponse.Subscription` | **A**: `SdkException<PauseSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` |
| **Change / clear the auto-resume date** | `UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `PUT /subscriptions/{id}/hold.json`. Set `Hold.AutomaticallyResumeAt = null` to remove the resume date. | `PauseRequest` | `SubscriptionResponse.Subscription` | **A**: `SdkException<UpdateAutomaticSubscriptionResumptionError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| **Resume** | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — **no body model; the option is a query param** `calendar_billing['resumption_charge']`. Pass `null` for non-calendar-billing sites. | `MaxioAdvancedBilling.Models.Enums.ResumptionCharge` = `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` | `SubscriptionResponse.Subscription` | **A**: `SdkException<ResumeSubscriptionError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| **Cancel immediately** | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `DELETE /subscriptions/{id}.json`. **Omit schedule params (or pass `null` body) to cancel now.** | `CancellationRequest { Subscription (subscription): CancellationOptions !req }` | `SubscriptionResponse.Subscription` | **A**: `SdkException<CancelSubscriptionApiError>` — **note the `ApiError` suffix** — `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError` |
| **Cancel at end of period (delayed)** | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `POST /subscriptions/{id}/delayed_cancel.json` | `CancellationRequest` | `DelayedCancellationResponse { Message (message): string? }` — **NOT a subscription; re-read the subscription to refresh UI state** | **A**: `SdkException<InitiateDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| **Clear a delayed cancel** | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` — `DELETE /subscriptions/{id}/delayed_cancel.json`; **idempotent** (safe when nothing was scheduled) | — | `DelayedCancellationResponse` | **A**: `SdkException<CancelDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` |
| **Reactivate a canceled subscription** | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `PUT /subscriptions/{id}/reactivate.json` | `ReactivateSubscriptionRequest { CalendarBilling (calendar_billing): ReactivationBilling?, IncludeTrial (include_trial): bool?, PreserveBalance (preserve_balance): bool?, CouponCode (coupon_code): string?, UseCreditsAndPrepayments (use_credits_and_prepayments): bool?, Resume (resume): Resume? (union) }` | `SubscriptionResponse.Subscription` | **A**: `SdkException<ReactivateSubscriptionError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` |

`CancellationOptions` (`records-1-Ac-Cr.md`, `Models/CancellationOptions.cs`) — all optional:
`CancellationMessage (cancellation_message): string?`, `ReasonCode (reason_code): string?`,
`CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`,
`ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`,
`RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool?`.

`Resume` union (`unions.md`, `MaxioAdvancedBilling.Models.AnyOf.Resume`): variants `bool`, `ResumeOptions` —
factories `Resume.Bool(bool)`, `Resume.ResumeOptions(ResumeOptions)`; readers `TryGetBool`, `TryGetResumeOptions`;
implicit from `bool` and `ResumeOptions`. Use `Resume.Bool(true)` to resume the original billing period.

```csharp
// immediate cancel with a reason
await client.SubscriptionStatus.CancelSubscription(
    subscriptionId,
    new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "user requested", ReasonCode = "other" } },
    ct: ct);

// cancel at period end
await client.SubscriptionStatus.InitiateDelayedCancellation(
    subscriptionId,
    new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "user requested" } },
    ct: ct);

// undo it
await client.SubscriptionStatus.CancelDelayedCancellation(subscriptionId, ct: ct);
```

`UNVERIFIED (live-traffic only): which `SubscriptionState` a held subscription reports — the SDK declares BOTH
`SubscriptionState.OnHold (on_hold)` and `SubscriptionState.Paused (paused)`, and `SubscriptionStateFilter`
declares only `OnHold`.` **Defensive directive:** treat `OnHold` **and** `Paused` as "paused" in every UI/branching
decision, and additionally treat a non-null `Subscription.OnHoldAt` as paused; never write
`state == SubscriptionState.OnHold` alone. When filtering via `ListSubscriptions`, use
`SubscriptionStateFilter.OnHold` (there is no `Paused` filter member).

### 2.8 ERRORS — exact types and the one correct catch pattern

Namespaces (three distinct ones — `Core.*` is not a single namespace):

| Type | Namespace |
|---|---|
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `ApiError`, `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `{Operation}Error` classes | `MaxioAdvancedBilling.Errors` |

- `SdkException<TError>` exposes exactly one property: `public required TError Error { get; init; }`.
- **Case B** (`TError` = `RawError`): read `ex.Error.StatusCode` (`System.Net.HttpStatusCode`),
  `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`. There are **no** `TryGet…`
  accessors and **no** `TryGetRawError` on `RawError`. `ReadAsJson<T>()` **throws `JsonException`** on a
  non-JSON body — prefer `ReadAsString()`.
- **Case A** (`TError` = a typed `{Operation}Error : ApiError`): the **only** way to get the status code is
  through an accessor's `out RawError` — the typed error itself carries no `StatusCode`. Write one branch per
  accessor and put `TryGetRawError` **last** (it is NOT a catch-all: a status with a more specific accessor
  leaves it `false`).
- **No `…Result` / no-throw variants exist anywhere in this SDK** — every call must be wrapped.

**Case A vs Case B for every operation in this plan:**

| Operation | Case | Exception type | Accessors |
|---|---|---|---|
| `ProductFamilies.ListProductFamilies` | **B** | `SdkException<RawError>` | — |
| `ProductFamilies.ReadProductFamily` | **B** | `SdkException<RawError>` | — |
| `ProductFamilies.ListProductsForProductFamily` | **A** | `SdkException<ListProductsForProductFamilyError>` | `TryGetString(out string)` [404] · `TryGetRawError` |
| `Products.ReadProductByHandle` | **B** | `SdkException<RawError>` | — |
| `Products.ReadProduct` / `Products.ListProducts` | **B** | `SdkException<RawError>` | — |
| `Components.FindComponent` | **B** | `SdkException<RawError>` | — |
| `Components.ListComponentsForProductFamily` | **B** | `SdkException<RawError>` | — |
| `Components.ReadComponent` | **B** | `SdkException<RawError>` | — |
| `Customers.ReadCustomerByReference` | **B** | `SdkException<RawError>` | — |
| `Customers.ReadCustomer` / `Customers.ListCustomerSubscriptions` / `Customers.ListCustomers` | **B** | `SdkException<RawError>` | — |
| `Customers.CreateCustomer` | **A** | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` |
| `Subscriptions.CreateSubscription` | **A** | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` |
| `Subscriptions.ReadSubscription` / `Subscriptions.ListSubscriptions` | **B** | `SdkException<RawError>` | — |
| `Subscriptions.UpdateSubscription` | **A** | `SdkException<UpdateSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `Subscriptions.FindSubscription` | **A** | `SdkException<FindSubscriptionError>` | `TryGetNoContent(out RawError)` [404] · `TryGetRawError` |
| `SubscriptionComponents.CreateUsage` | **A** | `SdkException<CreateUsageError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `SubscriptionComponents.ListUsages` | **B** | `SdkException<RawError>` | — |
| `SubscriptionComponents.ReadSubscriptionComponent` | **A** | `SdkException<ReadSubscriptionComponentError>` | `TryGetNoContent(out RawError)` [404] · `TryGetRawError` |
| `SubscriptionComponents.ListSubscriptionComponents` | **B** | `SdkException<RawError>` | — |
| `SubscriptionProducts.PreviewSubscriptionProductMigration` | **A** | `SdkException<PreviewSubscriptionProductMigrationError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `SubscriptionProducts.MigrateSubscriptionProduct` | **A** | `SdkException<MigrateSubscriptionProductError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `SubscriptionStatus.PauseSubscription` | **A** | `SdkException<PauseSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `SubscriptionStatus.UpdateAutomaticSubscriptionResumption` | **A** | `SdkException<UpdateAutomaticSubscriptionResumptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `SubscriptionStatus.ResumeSubscription` | **A** | `SdkException<ResumeSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `SubscriptionStatus.CancelSubscription` | **A** | `SdkException<CancelSubscriptionApiError>` | `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError` |
| `SubscriptionStatus.InitiateDelayedCancellation` | **A** | `SdkException<InitiateDelayedCancellationError>` | `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `SubscriptionStatus.CancelDelayedCancellation` | **A** | `SdkException<CancelDelayedCancellationError>` | `TryGetNoContent(out RawError)` [404] · `TryGetRawError` |
| `SubscriptionStatus.ReactivateSubscription` | **A** | `SdkException<ReactivateSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |

**The 422 payload models (`records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `unions.md`):**

| Payload type | Shape |
|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` — **non-nullable**, so `string.Join("; ", e.Errors)` needs no null-coalesce. Used by `CreateSubscription`, `UpdateSubscription`, `CreateUsage`, `Migrate…`, `Preview…Migration`, `Pause/Resume/Reactivate/InitiateDelayedCancellation`. |
| `SingleErrorResponse1` | `Error (error): string !req` |
| `CustomerErrorResponse1` | `Errors (errors): Errors?` where `Errors` = `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` — see the trust note below. |
| `CancelSubscriptionErrorResponse` | **a UNION** (`MaxioAdvancedBilling.Models.AnyOf`) of `ErrorListResponse1` \| `SingleErrorResponse1`; read with `TryGetErrorListResponse1(out …)` / `TryGetSingleErrorResponse1(out …)`. So the `CancelSubscription` 422 branch needs a **second, nested** TryGet after `TryGetCancelSubscriptionErrorResponse`. |

> **Trust judgment on `CustomerErrorResponse1` (evidence: the map's own definitions).** Its only field is
> typed `Errors`, a generated model whose members are `per_page` and `price_point` — fields that have nothing
> to do with a customer-validation error, and which look like a shared/misbound generated model. Treat the
> `CreateCustomer` 422 payload as **structurally untrustworthy**: extract best-effort
> (`e.Errors?.PerPage`/`.PricePoint` are almost certainly empty) and fall through to
> `TryGetRawError → raw.ReadAsString()` for the actual message. This is a defect visible in the generated
> definitions themselves, not a claim about live traffic.

**A catch block that works for both cases** — the two `SdkException<…>` closed generics are unrelated types,
so you need one `catch` clause per case (there is no shared base to catch), plus a connection-failure clause:

```csharp
using System.Net;
using MaxioAdvancedBilling.Core.Exceptions;      // SdkException<TError>
using MaxioAdvancedBilling.Core.ErrorResponse;   // RawError
using MaxioAdvancedBilling.Errors;               // CreateSubscriptionError, ... (Case A only)

try
{
    var resp = await client.Subscriptions.CreateSubscription(body, ct: ct);
}
catch (SdkException<CreateSubscriptionError> ex)          // Case A — one clause per Case-A op you call
{
    if (ex.Error.TryGetErrorListResponse1(out var validation))
        throw new BillingException(string.Join("; ", validation.Errors), ex);          // 422 payload
    else if (ex.Error.TryGetRawError(out var raw))                                     // ALWAYS LAST
        throw new BillingException($"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}", ex);
    else
        throw new BillingException("Maxio returned an unrecognized error.", ex);
}
catch (SdkException<RawError> ex)                          // Case B
{
    var status = ex.Error.StatusCode;                      // System.Net.HttpStatusCode
    throw new BillingException($"HTTP {(int)status}: {ex.Error.ReadAsString()}", ex);
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    throw new BillingException("Maxio unreachable.", ex);  // connection failure — NOT an SdkException
}
```

**Directive:** put this translation in ONE wrapper service (`IBillingService`) so the rest of eShopOnWeb sees a
single `BillingException`. Do **not** write a shared `string Describe(ApiError e)` helper — the typed `TryGet…`
accessors live on the concrete `{Operation}Error`, not on `ApiError`, so such a helper can only reach
`TryGetRawError` and will silently drop every 422 body. Read typed accessors **inside** the per-operation catch.

`UNVERIFIED (live-traffic only): the exact JSON shape a 422 puts into `ErrorListResponse1` /
`CustomerErrorResponse1` on this site.` **Defensive directive:** extract best-effort (join the errors
collection when present) and **fall back to the generic message** (`raw.ReadAsString()`, else a fixed
"billing request rejected") — never index into the payload assuming a non-empty collection.

### 2.9 MONEY — every price field you will display

| Where | Field (C# / wire) | Type | **Unit** |
|---|---|---|---|
| Product price | `Product.PriceInCents` / `price_in_cents` | `long?` | **CENTS** → `/100m` for display |
| Product setup fee | `Product.InitialChargeInCents` / `initial_charge_in_cents` | `long?` | **CENTS** |
| Product trial price | `Product.TrialPriceInCents` / `trial_price_in_cents` | `long?` | **CENTS** |
| Component unit price (cents) | `Component.PricePerUnitInCents` / `price_per_unit_in_cents` | `long?` | **CENTS** — prefer this |
| Component unit price (string) | `Component.UnitPrice` / `unit_price` | `string?` | **DOLLARS** as a decimal string (no `_in_cents` suffix, and it coexists with `price_per_unit_in_cents`) — parse invariant |
| Component tier price | `ComponentPrice.UnitPrice` / `unit_price` | `string?` | **DOLLARS** decimal string |
| Component tier price (display) | `ComponentPrice.FormattedUnitPrice` / `formatted_unit_price` | `string?` | already formatted — display as-is |
| Proration charge | `SubscriptionMigrationPreview.ChargeInCents` / `charge_in_cents` | `long?` | **CENTS** |
| Proration credit | `SubscriptionMigrationPreview.CreditAppliedInCents` / `credit_applied_in_cents` | `long?` | **CENTS** (there is no `credit_in_cents`) |
| Proration net due | `SubscriptionMigrationPreview.PaymentDueInCents` / `payment_due_in_cents` | `long?` | **CENTS** |
| Proration adjustment | `SubscriptionMigrationPreview.ProratedAdjustmentInCents` / `prorated_adjustment_in_cents` | `long?` | **CENTS** |
| Subscription balance | `Subscription.BalanceInCents` / `balance_in_cents` | `long?` | **CENTS** |
| Subscription lifetime revenue | `Subscription.TotalRevenueInCents` / `total_revenue_in_cents` | `long?` | **CENTS** |
| Subscription product price | `Subscription.ProductPriceInCents` / `product_price_in_cents` | `long?` | **CENTS** |
| Next billing amount | `Subscription.CurrentBillingAmountInCents` / `current_billing_amount_in_cents` | `long?` | **CENTS** |
| Credit / prepayment balance | `Subscription.CreditBalanceInCents`, `Subscription.PrepaymentBalanceInCents` | `long?` | **CENTS** |

**Rule:** every `*InCents` field is an integer count of minor units → convert with `value / 100m` (decimal,
never `double`). The only dollar-denominated fields in scope are the component `UnitPrice` **strings** — parse
with `decimal.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture)`, and prefer the `*_in_cents` sibling
whenever it is non-null.

### 2.10 PAGINATION — exact parameter names

| Operation | C# params | Wire | Defaults |
|---|---|---|---|
| `ProductFamilies.ListProductsForProductFamily` | `page`, `perPage` | `page`, `per_page` | 1 / 20 |
| `Products.ListProducts` | `page`, `perPage` | `page`, `per_page` | 1 / 20 |
| `Components.ListComponents` | `page`, `perPage` | `page`, `per_page` | 1 / 20 |
| `Components.ListComponentsForProductFamily` | `page`, `perPage` | `page`, `per_page` | 1 / 20 |
| `Customers.ListCustomers` | `page`, `perPage` | `page`, `per_page` | 1 / **50** |
| `Subscriptions.ListSubscriptions` | `page`, `perPage` | `page`, `per_page` | 1 / 20 |
| `SubscriptionComponents.ListUsages` | `page`, `perPage` | `page`, `per_page` | 1 / 20 |
| `SubscriptionComponents.ListAllocations` | `page` **only** (no `perPage`) | `page` | 1 |
| `ProductFamilies.ListProductFamilies` | **none** | — | — |
| `Customers.ListCustomerSubscriptions` | **none** | — | — |
| `SubscriptionComponents.ListSubscriptionComponents` | **none** | — | — |

There is **no** auto-paginating (`IAsyncEnumerable`) variant on any of these — loop `page` until a returned
list has fewer than `perPage` items.

---

## 3. Trap notes (attach to the step named)

1. **Step 1 (client):** the `HttpClient` must be long-lived (`IHttpClientFactory`), never one per request.
   Note the generated DI extension `AddMaxioAdvancedBillingClient` registers the client as a **singleton** and
   invokes your `configure` callback **once at registration time** — configuration values are captured then,
   so `IOptionsMonitor`-style hot reload does **not** apply.
2. **Step 1 (auth):** Basic — `Username` = API key, `Password` = the literal `"x"`. Both are `required` on
   `BasicAuthCredentials`. Load the key from configuration/user-secrets, never a literal.
3. **Step 1 (base URL):** overriding `Server.Production.Us.BaseUrl` only affects the **US** environment; set
   the matching `.Eu.BaseUrl` too, or the EU selection silently keeps the default host. Setting `Site` alone is
   the correct path for a normal sandbox subdomain; set `BaseUrl` only for a mock/dev host.
4. **All steps (calling):** these list/search signatures have **many nullable params with no C# default** —
   call every operation with **named arguments** (`ct: ct`, `page: 1`, `perPage: 50`, `include: null`) or a
   positional call will mis-bind. The token parameter is literally named `ct`.
5. **All steps (envelopes):** every response wraps its payload one level down — `ProductResponse.Product`,
   `ProductFamilyResponse.ProductFamily`, `ComponentResponse.Component`, `CustomerResponse.Customer`,
   `SubscriptionResponse.Subscription`, `UsageResponse.Usage`, `SubscriptionComponentResponse.Component`,
   `SubscriptionMigrationPreviewResponse.Migration`. Note which are `!req` (non-null) vs nullable — this sheet
   marks each.
6. **Steps 2/4/7 (enums):** `ComponentKind`, `SubscriptionState`, `IntervalUnit`, `PricingScheme`,
   `CollectionMethod`, `ResumptionCharge`, `ServerEnvironment` are `StringEnum<T>` **records**, not C# enums —
   no `switch` over enum members, no `Enum.Parse`. Use the static constants (`ComponentKind.MeteredComponent`),
   `FromValue(wire)` for runtime values, `.Value` to read the wire string back, and `IsKnownValue()` /
   `TryGetKnownValue()` before trusting an unfamiliar value.
7. **Step 6 (unions):** `SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`, `AllocatedQuantity2`,
   `Resume` are unions — build with the static factories (**never `new`**, never object-initializer) and read
   with `TryGet…`; both int and string variants must be handled on read.
8. **Steps 3–8 (models):** records are immutable with `init` setters; `required` members
   (`CreateCustomer.FirstName/LastName/Email`, every `…Request.{Payload}`) must be set in the object initializer
   or it won't compile. Unmodeled JSON fields are dropped on deserialize — if you need a field not on this
   sheet, it isn't reachable through the SDK.
9. **Steps 4/6/7/8 (resilience):** retries apply to idempotent verbs only (`GET/HEAD/PUT/OPTIONS`) — so
   `CreateSubscription`, `CreateUsage`, `MigrateSubscriptionProduct`, `PauseSubscription`,
   `InitiateDelayedCancellation` (all `POST`) and `CancelSubscription`/`CancelDelayedCancellation` (`DELETE`)
   are **not** retried automatically. `RetryOptions.Timeout` is **per attempt**, not total. Any retry you add
   around a POST must be idempotency-safe (usage events would double-post).
10. **Step 3 (idempotency):** `reference` must be unique per customer — Maxio allows only one customer per
    reference value; a duplicate create is a 422 on `CreateCustomer`, so always do the lookup-first flow.
11. **Steps 2/6 (handles):** cache the resolved product-family id and component id per process; do **not**
    persist numeric ids in your database — persist the handles and re-resolve.
12. **Logging:** the SDK has no logging hook; attach a `DelegatingHandler` on the `HttpClient` if you need
    request/response tracing. Never log the Basic auth header.

---

## 4. Assumptions & Blockers

**Assumptions**

1. Only the **Production** server group is used. The **Ebb** group (`events.chargify.com`) is required solely
   by `SubscriptionComponents.RecordEvent` / `BulkRecordEvents` (event-based-billing metrics), which are **not**
   part of this plan — usage is recorded with `CreateUsage` on the Production host, which is the correct
   operation for a **metered** component.
2. `api-call` is a **metered** component (`ComponentKind.MeteredComponent`), so `CreateUsage` (not
   `AllocateComponent`) is the right write path, and `unit_balance` is the period-to-date counter.
3. The customer `reference` used for idempotency is the eShopOnWeb user's email.
4. Payment-profile / card collection is out of scope for this sheet (subscriptions are created against an
   existing customer without card attributes). If `Product.RequireCreditCard == true`, subscription creation
   will 422 without payment info — surface that, or add `PaymentProfileId` / Chargify.js collection as a
   follow-up. Raw card data must never be sent from this app.
5. "Region select" means `MaxioAdvancedBillingClientOptions.Environment` = `ServerEnvironment.Us` / `.Eu`;
   the arbitrary-base-URL override is a separate, additional configuration switch.

**Blockers** — none.

**Explicitly unverifiable without live traffic** (each already carries a defensive-coding directive above):

1. `UNVERIFIED` — the exact status/body `Customers.ReadCustomerByReference` returns for an unknown reference
   (§2.3). Directive: treat 404 (and an unusable customer) as absent; rethrow anything else.
2. `UNVERIFIED` — whether the live payload populates `SubscriptionComponent.UnitBalance` for this metered
   component (§2.5). Directive: read `UnitBalance`, else page `ListUsages` and sum, else show "unavailable".
3. `UNVERIFIED` — whether a held subscription reports `on_hold` or `paused` (§2.7); the SDK declares **both**
   constants while `SubscriptionStateFilter` declares only `OnHold`, which is map-visible evidence that the two
   generated definitions disagree. Directive: treat both (plus non-null `OnHoldAt`) as paused.
4. `UNVERIFIED` — see also §5.7 for the UC0-seeding unverified items.
5. `UNVERIFIED` — the concrete 422 payload the site actually returns for `CreateCustomer`; the generated
   `CustomerErrorResponse1` → `Errors { per_page, price_point }` shape is visibly wrong for a customer error
   (§2.8). Directive: extract best-effort, then fall back to `TryGetRawError → ReadAsString()`, then to a
   fixed generic message. (`ErrorListResponse1.Errors` is a plain non-null `IReadOnlyList<string>` and is
   safe to join directly.)

---

## 5. UC0 — sandbox seeding (operator-only console tool)

Write operations that provision the catalog when a sandbox is empty. Same rules as §2: signatures are
verbatim generated code, the token parameter is `ct`, every request body is `nullable, no default → must be
passed explicitly`, and every response wraps its payload one level down.

### 5.1 CREATE PRODUCT FAMILY

| | |
|---|---|
| Operation | `client.ProductFamilies.CreateProductFamily` — `POST /product_families.json` (Production) |
| Signature | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly |
| Request envelope | `CreateProductFamilyRequest { ProductFamily (product_family): CreateProductFamily !req }` (`Models/CreateProductFamilyRequest.cs`) |
| Inner model | `CreateProductFamily` (`Models/CreateProductFamily.cs`) — **exactly three fields**: `Name (name): string !req`, `Handle (handle): string?`, `Description (description): string?` |
| Response envelope → inner | `ProductFamilyResponse { ProductFamily (product_family): ProductFamily? }` — the inner is **nullable**; null-check before reading `.Id` |
| Error | **Case A** — `SdkException<CreateProductFamilyError>`; accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| Map page | `operations/ProductFamilies.md`, `records-1-Ac-Cr.md`, `records-3-Of-Su.md` |

```csharp
var famResp = await client.ProductFamilies.CreateProductFamily(
    new CreateProductFamilyRequest
    {
        ProductFamily = new CreateProductFamily
        {
            Name        = "eShop Subscribe",
            Handle      = "eshop-subscribe",
            Description = "eShopOnWeb subscription plans",
        }
    },
    ct: ct);
int familyId = famResp.ProductFamily?.Id
    ?? throw new InvalidOperationException("Maxio returned a product family without an id.");
```

`Name` is the only `required` member — set `Handle` explicitly anyway, since every lookup in §2 resolves by
handle.

### 5.2 CREATE PRODUCT (recurring) inside a product family

| | |
|---|---|
| Operation | `client.Products.CreateProduct` — `POST /product_families/{product_family_id}/products.json` |
| Signature | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — **`productFamilyId` is `string`, not `int`** (asymmetric with `ReadProductFamily(int id)` and `ArchiveComponent(int productFamilyId, …)`); pass `familyId.ToString(CultureInfo.InvariantCulture)`. `body` must be passed explicitly. |
| Request envelope | `CreateOrUpdateProductRequest { Product (product): CreateOrUpdateProduct !req }` (`Models/CreateOrUpdateProductRequest.cs`) |
| Response envelope → inner | `ProductResponse { Product (product): Product !req }` — inner is **required/non-null** |
| Error | **Case A** — `SdkException<CreateProductError>`; accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| Map page | `operations/Products.md`, `records-1-Ac-Cr.md` |

`CreateOrUpdateProduct` (`Models/CreateOrUpdateProduct.cs`) — **the complete field list; there are no others**:

| Field (C# / wire) | Type | Required? | Notes |
|---|---|---|---|
| `Name (name)` | `string` | **`!req`** | |
| `Handle (handle)` | `string?` | optional | set it — `"eshop-pro"` / `"basic-plan"` |
| `Description (description)` | `string` | **`!req`** | **required on create** (unlike the read-side `Product.Description`, which is nullable) — pass a non-empty string |
| `AccountingCode (accounting_code)` | `string?` | optional | |
| `RequireCreditCard (require_credit_card)` | `bool?` | optional | **the "payment method required" toggle → set `false`** |
| `PriceInCents (price_in_cents)` | `long` | **`!req`** | **CENTS integer** — `29900` and `2900` go in verbatim |
| `Interval (interval)` | `int` | **`!req`** | `1` |
| `IntervalUnit (interval_unit)` | `IntervalUnit` | **`!req`** | type `MaxioAdvancedBilling.Models.Enums.IntervalUnit`; **constant for month = `IntervalUnit.Month` (wire `month`)**; only other member is `IntervalUnit.Day (day)` |
| `TrialPriceInCents (trial_price_in_cents)` | `long?` | optional | **no trial → leave all three trial fields null (omit them entirely); do not send `0`** |
| `TrialInterval (trial_interval)` | `int?` | optional | leave null |
| `TrialIntervalUnit (trial_interval_unit)` | `IntervalUnit?` | optional | leave null |
| `TrialType (trial_type)` | `TrialType?` | optional | `MaxioAdvancedBilling.Models.Enums.TrialType` = `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)`; leave null when there is no trial |
| `ExpirationInterval (expiration_interval)` | `int?` | optional | **"expires never" → leave BOTH expiration fields null** (see below) |
| `ExpirationIntervalUnit (expiration_interval_unit)` | `ExpirationIntervalUnit?` | optional | `MaxioAdvancedBilling.Models.Enums.ExpirationIntervalUnit` = `Day (day)`, `Month (month)`, **`Never (never)`** |
| `AutoCreateSignupPage (auto_create_signup_page)` | `bool?` | optional | `false` for an API-only catalog |
| `TaxCode (tax_code)` | `string?` | optional | |

**"Expires never":** the `ExpirationIntervalUnit.Never` constant **does** exist, so both encodings are
expressible — omit both fields (no expiry configured), or set
`ExpirationIntervalUnit = ExpirationIntervalUnit.Never` explicitly (leaving `ExpirationInterval` null, since
"never" needs no count). Prefer **omitting both** for a never-expiring recurring product; use the explicit
`Never` only if a seeded product comes back with a non-null `Product.ExpirationInterval`.

> **GAP — `taxable = false` is NOT settable on product create/update.** `CreateOrUpdateProduct` has **no**
> `Taxable` field (the list above is complete); the read-side `Product.Taxable (taxable): bool?` exists but is
> response-only. The nearest lever is `TaxCode (tax_code): string?`. Report this as a gap: a seeded product's
> taxability must be set in the Maxio UI or left at the site default. (`MeteredComponent` **does** have
> `Taxable` — see §5.3 — so this gap is product-only.)
>
> **GAP — `request_credit_card` is NOT settable either.** Only `RequireCreditCard (require_credit_card)` is on
> the create model; `Product.RequestCreditCard (request_credit_card): bool?` is response-only. Setting
> `RequireCreditCard = false` is the whole of the "payment method required OFF" toggle available through this
> SDK.

```csharp
var prodResp = await client.Products.CreateProduct(
    familyId.ToString(CultureInfo.InvariantCulture),
    new CreateOrUpdateProductRequest
    {
        Product = new CreateOrUpdateProduct
        {
            Name              = "eShop Pro",
            Handle            = "eshop-pro",
            Description       = "eShop Pro monthly plan",   // REQUIRED
            PriceInCents      = 29_900,                     // cents
            Interval          = 1,
            IntervalUnit      = IntervalUnit.Month,
            RequireCreditCard = false,
            // no trial: TrialPriceInCents / TrialInterval / TrialIntervalUnit / TrialType all omitted
            // expires never: ExpirationInterval / ExpirationIntervalUnit omitted
            AutoCreateSignupPage = false,
        }
    },
    ct: ct);
var product = prodResp.Product;      // ProductResponse.Product — non-null
```

Repeat with `Handle = "basic-plan"`, `PriceInCents = 2_900`.

> **"No setup fee":** there is no setup-fee/initial-charge field on `CreateOrUpdateProduct` at all — the
> read-side `Product.InitialChargeInCents` is response-only — so a product created through this SDK simply has
> no initial charge. Nothing to set; nothing missing for your case.

### 5.3 CREATE METERED COMPONENT on a product family

| | |
|---|---|
| Operation | `client.Components.CreateMeteredComponent` — `POST /product_families/{product_family_id}/metered_components.json` |
| Signature | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — **`productFamilyId` is `string`**; `body` must be passed explicitly. **Note the parameter type `CreateMeteredComponent` is the request envelope record, which shares its name with the operation.** |
| Request envelope | `CreateMeteredComponent { MeteredComponent (metered_component): MeteredComponent !req }` (`Models/CreateMeteredComponent.cs`) |
| Inner model | `MeteredComponent` (`Models/MeteredComponent.cs`) — see table below |
| Response envelope → inner | `ComponentResponse { Component (component): Component !req }` — inner is **required/non-null** |
| Error | **Case A** — `SdkException<CreateMeteredComponentError>`; accessors: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] — **three branches, in that order** |
| Map page | `operations/Components.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `unions.md` |

`MeteredComponent` — complete field list:

| Field (C# / wire) | Type | Required? | Notes |
|---|---|---|---|
| `Name (name)` | `string` | **`!req`** | `"API Call"` |
| `UnitName (unit_name)` | `string` | **`!req`** | `"call"` |
| `PricingScheme (pricing_scheme)` | `PricingScheme` | **`!req`** | `MaxioAdvancedBilling.Models.Enums.PricingScheme`; **per-unit constant = `PricingScheme.PerUnit` (wire `per_unit`)**; others: `Stairstep (stairstep)`, `Volume (volume)`, `Tiered (tiered)` |
| `Handle (handle)` | `string?` | optional | `"api-call"` — set it |
| `Description (description)` | `string?` | optional | |
| `Taxable (taxable)` | `bool?` | optional | **settable here** (unlike products) |
| `UnitPrice (unit_price)` | `UnitPrice1?` (union) | optional | **DOLLARS**, union of `string` \| `double` — see below |
| `Prices (prices)` | `IReadOnlyList<Price>?` | optional | tier list — see below |
| `PricePoints (price_points)` | `IReadOnlyList<ComponentPricePointItem>?` | optional | `ComponentPricePointItem { Name?, Handle?, PricingScheme?, Interval?, IntervalUnit?, Prices: IReadOnlyList<Price>? }` — not needed for a single default price |
| `TaxCode (tax_code)` | `string?` | optional | |
| `HideDateRangeOnInvoice (hide_date_range_on_invoice)` | `bool?` | optional | |
| `DisplayOnHostedPage (display_on_hosted_page)` | `bool?` | optional | |
| `AllowFractionalQuantities (allow_fractional_quantities)` | `bool?` | optional | |
| `PublicSignupPageIds (public_signup_page_ids)` | `IReadOnlyList<int>?` | optional | |
| `Interval (interval)` | `int?` | optional | |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | optional | |

**`unit_price` vs `prices` — the answer for $0.01 per unit:**

- Both are **optional** in the generated model, so the SDK does not force a tier list. For
  `PricingScheme.PerUnit`, set the scalar **`UnitPrice`**; a `Prices` tier list is required only for the
  tiered/volume/stairstep schemes.
- **`UnitPrice` is DOLLARS, not cents.** It is the union
  `MaxioAdvancedBilling.Models.AnyOf.UnitPrice1` — variants `string`, `double`; factories
  `UnitPrice1.String(string)`, `UnitPrice1.Double(double)`; readers `TryGetString`, `TryGetDouble`. There is
  no `*_in_cents` variant anywhere on this model, and the read-side sibling `Component.UnitPrice` is a
  dollars decimal string (§2.2). **Use the string variant** — `UnitPrice1.String("0.01")` — so the value
  never round-trips through binary floating point. `UnitPrice1.Double(0.01)` is legal but risks `0.01`
  serializing as `0.010000000000000000208…`.
- If you ever *do* need the tier list, `Price` (`Models/Price.cs`) is:
  `StartingQuantity (starting_quantity): StartingQuantity !req` (union `int`\|`string`),
  `EndingQuantity (ending_quantity): EndingQuantity?` (union `int`\|`string`, null = "and above"),
  `UnitPrice (unit_price): UnitPrice !req` (union `double`\|`string`) — note this is the union type
  **`UnitPrice`**, distinct from `UnitPrice1` on the component itself. Factories:
  `StartingQuantity.Int(int)`, `EndingQuantity.Int(int)`, `UnitPrice.String(string)` / `UnitPrice.Double(double)`.
  A single per-unit tier is `new Price { StartingQuantity = StartingQuantity.Int(1), UnitPrice = UnitPrice.String("0.01") }`.

```csharp
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;   // PricingScheme
using MaxioAdvancedBilling.Models.AnyOf;   // UnitPrice1, Price's unions

var compResp = await client.Components.CreateMeteredComponent(
    familyId.ToString(CultureInfo.InvariantCulture),
    new CreateMeteredComponent
    {
        MeteredComponent = new MeteredComponent
        {
            Name          = "API Call",
            Handle        = "api-call",
            UnitName      = "call",
            PricingScheme = PricingScheme.PerUnit,
            UnitPrice     = UnitPrice1.String("0.01"),   // DOLLARS
            Taxable       = false,
        }
    },
    ct: ct);
var component = compResp.Component;    // ComponentResponse.Component — non-null; Kind == ComponentKind.MeteredComponent
```

`UNVERIFIED (live-traffic only): whether the API accepts `unit_price` alone for a `per_unit` metered
component, or insists on a one-row `prices` list.` The generated model marks **both** optional, so it cannot
settle the server's either/or rule. **Defensive directive:** send `UnitPrice` first; if the call returns 422,
retry once with the equivalent single tier
(`Prices = [ new Price { StartingQuantity = StartingQuantity.Int(1), UnitPrice = UnitPrice.String("0.01") } ]`,
`UnitPrice` left null) and log which form the sandbox accepted. Do not send both at once.

### 5.4 ARCHIVE (correcting a mis-created entity)

| Entity | Operation & signature | Returns | Error case |
|---|---|---|---|
| **Product** | `client.Products.ArchiveProduct(int productId, CancellationToken ct = default)` — `DELETE /products/{product_id}.json` | `ProductResponse` → `.Product` (**required/non-null**) | **A** — `SdkException<ArchiveProductError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Component** | `client.Components.ArchiveComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `DELETE /product_families/{product_family_id}/components/{component_id}.json` | **`Component` — the BARE model, NOT `ComponentResponse`.** This is the one operation in this plan that returns an unwrapped payload; do **not** write `.Component` on the result. | **A** — `SdkException<ArchiveComponentError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Product family** | — | — | **DOES NOT EXIST** (see below) |

Note the parameter-type asymmetry: `ArchiveComponent` takes `int productFamilyId` (while
`CreateMeteredComponent` / `CreateProduct` take `string productFamilyId`) and `string componentId` (which
accepts the `handle:api-call` form, per the `ReadComponent`/`UpdateComponent` notes on the same path).

> **GAP — there is NO archive/delete operation for a product family.** `client.ProductFamilies` exposes
> **exactly four** operations (`operations/ProductFamilies.md`): `CreateProductFamily`, `ListProductFamilies`,
> `ListProductsForProductFamily`, `ReadProductFamily`. No `ArchiveProductFamily`, no `DeleteProductFamily`, no
> `UpdateProductFamily`. Report this plainly as a gap: a mis-created product family cannot be removed or
> renamed through this SDK — it must be handled in the Maxio UI/support, or the seeder must create the family
> under a corrected handle and leave the bad one in place. **Do not work around it** by deleting products and
> reusing the family, and do not reach for `PurgeSubscription` (that purges subscriptions in test-mode sites,
> not catalog entities).

Archiving is also **not** how you undo a wrong price: archived products/components remain on existing
subscriptions (both operations' notes say current subscribers continue to be charged). For a seeder, treat
archive as "hide the mistake from new signups", then create the corrected entity under a new handle.

### 5.5 Seeding sequence and idempotency

```
1. ListProductFamilies(null, null, null, null, null, ct: ct)  → find Handle == "eshop-subscribe"
   └─ absent → CreateProductFamily  → familyId
2. ListProductsForProductFamily(familyId.ToString(), null, null, null, null, null, null, null, null,
                                page: 1, perPage: 200, ct: ct)
   └─ for each of "eshop-pro" (29900) / "basic-plan" (2900) missing → CreateProduct
      (or probe with Products.ReadProductByHandle and catch 404 — Case B, SdkException<RawError>)
3. Components.FindComponent("api-call", ct: ct)  → catch SdkException<RawError> 404
   └─ absent → CreateMeteredComponent
```

**None of these creates is idempotent** — re-running the seeder without the existence probes will create
duplicate products/components (a duplicate *handle* is rejected by the server with a 422, which is your
accidental safety net, but do not rely on it). Every step must probe first. All four creates are `POST`, so
**they are not auto-retried** by `RetryOptions` (idempotent verbs only) — a transient failure surfaces
immediately and the operator re-runs the tool, which is the correct behaviour here.

### 5.6 Error handling for UC0 — all four creates are Case A

```csharp
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;

try { /* CreateMeteredComponent */ }
catch (SdkException<CreateMeteredComponentError> ex)
{
    if (ex.Error.TryGetNoContent(out RawError notFound))                 // 404 — family id wrong
        Console.Error.WriteLine($"product family not found (HTTP {(int)notFound.StatusCode})");
    else if (ex.Error.TryGetErrorListResponse1(out var validation))      // 422
        Console.Error.WriteLine(string.Join("; ", validation.Errors));   // Errors is non-null IReadOnlyList<string>
    else if (ex.Error.TryGetRawError(out RawError raw))                  // ALWAYS LAST
        Console.Error.WriteLine($"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}");
}
```

Summary of §5 error cases (all **Case A** — none of the seeding writes is Case B):

| Operation | Exception type | Accessors, in branch order |
|---|---|---|
| `ProductFamilies.CreateProductFamily` | `SdkException<CreateProductFamilyError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `Products.CreateProduct` | `SdkException<CreateProductError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `Components.CreateMeteredComponent` | `SdkException<CreateMeteredComponentError>` | `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `Products.ArchiveProduct` | `SdkException<ArchiveProductError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `Components.ArchiveComponent` | `SdkException<ArchiveComponentError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| *(archive product family)* | — | **operation does not exist** |

The probe operations used by the seeder are the Case-B reads already documented in §2.8
(`ListProductFamilies`, `ListProductsForProductFamily` is Case **A** with `TryGetString` [404],
`ReadProductByHandle`, `FindComponent`).

### 5.7 UC0 gaps & unverified items

**Gaps (operations/fields that do not exist in this SDK — report, do not work around):**

1. **No archive/delete/update operation for a product family** — `client.ProductFamilies` has only
   `CreateProductFamily`, `ListProductFamilies`, `ListProductsForProductFamily`, `ReadProductFamily`.
2. **`taxable` is not settable on product create/update** — `CreateOrUpdateProduct` has no `Taxable` field
   (only `TaxCode`); `Product.Taxable` is response-only. (Components *can* set `Taxable`.)
3. **`request_credit_card` is not settable** — only `RequireCreditCard` is on `CreateOrUpdateProduct`;
   `Product.RequestCreditCard` is response-only.
4. **No setup-fee / initial-charge field on product create** — `Product.InitialChargeInCents` is
   response-only. (Not a blocker for UC0, which wants no setup fee.)
5. **No by-handle archive for products** — `ArchiveProduct` takes `int productId`; resolve the id first with
   `Products.ReadProductByHandle(handle, ct: ct)` → `.Product.Id`.

**Unverified (live-traffic only), each with its defensive directive:**

1. `UNVERIFIED` — whether a `per_unit` metered component is accepted with `unit_price` alone or requires a
   one-row `prices` tier list (§5.3). Directive: send `UnitPrice` first; on 422 retry once with the single
   `Price` tier and log which form worked; never send both.
2. `UNVERIFIED` — whether omitting both expiration fields, or sending
   `ExpirationIntervalUnit.Never`, is what this site records as "never expires" (§5.2). Directive: omit both,
   then read the created product back and, if `Product.ExpirationInterval` is non-null, re-create/report
   rather than assume.
3. `UNVERIFIED` — whether a duplicate `handle` is rejected with 422 (the assumed safety net in §5.5).
   Directive: never rely on it — always run the existence probe before each create.
