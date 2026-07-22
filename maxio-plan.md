# Maxio Advanced Billing → eShopOnWeb integration plan (`MaxioBillingClient` : `IBillingClient`)

Everything below is grounded in the bundled SDK map (`sdk-map.md`, `map/operations/*.md`,
`map/models/*.md`) for SDK `AsadAli.AdvancedBilling.Sdk`, source commit `15db14b` / tag `v1.0.2`.
No fact here comes from memory. Rows that need SDK source are flagged
`→ maxio-debug resolves from source if it surfaces`; rows only live traffic can confirm are labelled
`UNVERIFIED` with a defensive-coding directive.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | NuGet + options plumbing: `MaxioOptions` (ApiKey, Subdomain, BaseUrlOverride, Region) bound from config/user-secrets | — |
| 2 | Client construction in Infrastructure DI: `new MaxioAdvancedBillingClient(httpClient, options)` via a typed `AddHttpClient<>`; set `BasicAuth`, `Environment`, `Server.Production.Us.Site` or `.BaseUrl` | client ctor + `MaxioAdvancedBillingClientOptions` |
| 3 | `IBillingClient` catalog reads (UC0/UC1 step 1): families, products, components | `ProductFamilies.ListProductFamilies` / `ReadProductFamily` / `ListProductsForProductFamily`, `Products.ReadProductByHandle` / `ReadProduct` / `ListProducts`, `Components.ListComponentsForProductFamily` / `ReadComponent` / `FindComponent` |
| 4 | Customer idempotency (UC1 step 3): lookup-by-reference → create-if-missing | `Customers.ReadCustomerByReference`, `Customers.ListCustomers` (q=email), `Customers.CreateCustomer` |
| 5 | Subscribe (UC1): create subscription on `customer_id` + `product_handle` | `Subscriptions.CreateSubscription` |
| 6 | Subscription reads (UC1/UC4): per-customer list + read by id | `Customers.ListCustomerSubscriptions`, `Subscriptions.ReadSubscription` |
| 7 | Lifecycle (UC4): pause / resume / cancel now / cancel at period end / undo delayed cancel / reactivate | `SubscriptionStatus.*` |
| 8 | Metered usage (UC2): record usage + read period-to-date total | `SubscriptionComponents.CreateUsage`, `.ListUsages`, `.ReadSubscriptionComponent` |
| 9 | Plan change (UC3): preview proration → commit now (prorated) or at renewal (no proration) | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `.MigrateSubscriptionProduct`, `Subscriptions.UpdateSubscription` |
| 10 | Single error-translation boundary in `MaxioBillingClient` → `BillingProviderException` | per-operation catches (§4) |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it**
> (e.g. `MaxioAdvancedBilling.Models.Enums.SubscriptionState`,
> `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference`,
> `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`, and the **client-config types**:
> `MaxioAdvancedBilling.Servers.ServerEnvironment`,
> `MaxioAdvancedBilling.Core.Configuration.RetryOptions`,
> `MaxioAdvancedBilling.Core.Configuration.ServerOptions`). The map carries these namespaces — do not drop
> them to the root or `.Models`, or the implementer guesses the wrong `using` and the build breaks.

Namespace table (map `sdk-map.md` §Namespaces):

| Contents | Namespace |
|---|---|
| Client `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` |
| Controllers (`client.Products`, …) | `MaxioAdvancedBilling.Api` |
| Records (`Product`, `Subscription`, `CreateCustomerRequest`, …) | `MaxioAdvancedBilling.Models` |
| Enums (`SubscriptionState`, `ComponentKind`, `IntervalUnit`, …) | `MaxioAdvancedBilling.Models.Enums` |
| Unions (`SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`, …) | `MaxioAdvancedBilling.Models.AnyOf` (OneOf types: `…Models.OneOf`) |
| Typed error classes (`CreateCustomerError`, …) | `MaxioAdvancedBilling.Errors` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |

---

### A. Client setup

**A1 — construction (map: `sdk-map.md` §Getting a client, §Servers & auth).**
The **only** constructor is
`MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`
— so a custom `HttpClient` is supplied positionally, first. `MaxioAdvancedBillingClientOptions` has exactly
four properties:

| Property | Type | Use here |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Us` (default) or `.Eu` |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default() with { … }`; all members `required` |
| `Server` | `ServerOptions` (see A1b) | subdomain **or** absolute base-URL override |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `new BasicAuthCredentials { Username = apiKey, Password = "x" }` |

Auth is Basic-only: **`Username` = the Maxio API key, `Password` = the literal string `"x"`.**

**A1b — base URL / subdomain. This is the exact mechanism, and it answers your "explicit override wins"
requirement directly.** There is no single "BaseUrl" property on the options; the override is nested
**per server group AND per environment** (map `sdk-map.md` §Servers & auth):

| Server group | US template | EU template | Override points |
|---|---|---|---|
| Production (all operations in this integration) | `https://{site}.chargify.com` | `https://{site}.ebilling.maxio.com` | `options.Server.Production.Us.Site`, `options.Server.Production.Us.BaseUrl` (and `.Eu.Site` / `.Eu.BaseUrl`) |
| Ebb (events) — only `SubscriptionComponents.RecordEvent` / `BulkRecordEvents`, **not used here** | `https://events.chargify.com/{site}` | same | `options.Server.Ebb.Us.BaseUrl` / `.Us.Site` |

So: **an arbitrary absolute URL IS supported** — assign it to
`options.Server.Production.Us.BaseUrl = "http://localhost:8080";` (a literal URL with no `{…}` placeholder
is used as-is). Only the options of the environment selected by `options.Environment` are read, so set the
override on the same environment you select. Implementation shape for "explicit override wins, else derive
from subdomain":

```
options.Environment = ServerEnvironment.Us;                    // US per brief
if (!string.IsNullOrWhiteSpace(cfg.BaseUrlOverride))
    options.Server.Production.Us.BaseUrl = cfg.BaseUrlOverride; // wins outright
else
    options.Server.Production.Us.Site = cfg.Subdomain;          // {site} template -> https://{site}.chargify.com
```

(`{site}` defaults to the literal `subdomain` if never set — always set it.)
Note you never need a `using` for `ServerOptions`/`ProductionOptions`: you only touch them through the
`options.Server.…` property chain. **The exact declared namespace of `ServerOptions` / `ProductionOptions`
(root `MaxioAdvancedBilling` vs `MaxioAdvancedBilling.Servers` vs `…Core.Configuration`) is a source-level
detail the map names only by file path (`ServerOptions.cs`, `Servers/ProductionOptions.cs`)
→ maxio-debug resolves from source if it surfaces** (it only surfaces if you declare one of these types
explicitly instead of using the property chain).

**A2 — DI (map: `sdk-map.md`, source `ServiceCollectionExtensions.cs`).** The generated extension is
`services.AddMaxioAdvancedBillingClient(o => { … })`, taking a configure-options callback; it registers the
client and resolves the **default, unnamed** `IHttpClientFactory` client (companion skill
`dotnet-client-initialization`). **Whether an overload accepting an `HttpClient` (or returning
`IHttpClientBuilder`) exists is not carried by the map → maxio-debug resolves from source if it surfaces.**
Because the brief requires a *custom* `HttpClient`, do **not** depend on that: register the SDK client
yourself with a typed/named `HttpClient`, which is unambiguous and uses the documented single constructor:

```
services.AddHttpClient<MaxioBillingClient>(...)          // your class owns the HttpClient config
// inside MaxioBillingClient ctor: _client = new MaxioAdvancedBillingClient(httpClient, BuildOptions(cfg));
```
The `HttpClient`/handler pipeline must be long-lived (factory-managed); the SDK client itself is also meant
to be long-lived — build it once per `MaxioBillingClient` instance, never per call.

---

### B. Catalog reads

| # | Operation (`client.X.Y`) | Signature (verbatim) | Request model | Response envelope → unwrap | Error case | Pagination | Map page |
|---|---|---|---|---|---|---|---|
| B3a | `ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must be passed explicitly (`null` to skip) | — | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily?`) | **B** `SdkException<RawError>` | none (no page params) | `operations/ProductFamilies.md` |
| B3b | `ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse` → `.ProductFamily` (`ProductFamily?`, nullable — null-check) | **B** `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| B4 | `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` must be passed explicitly | `ListProductsFilter { Ids (ids): IReadOnlyList<int>?, PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?, UseSiteExchangeRate (use_site_exchange_rate): bool? }` | `IReadOnlyList<ProductResponse>` → `.Product` (`Product`, **required**, non-null) | **A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` | `operations/ProductFamilies.md` |
| B5a | `Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` (required) | **B** `SdkException<RawError>` | none | `operations/Products.md` |
| B5b | `Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **B** `SdkException<RawError>` | none | `operations/Products.md` |
| B5c | `Products.ListProducts` (site-wide) | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — note the **date param order differs** from B4 | as above | `IReadOnlyList<ProductResponse>` | **B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Products.md` |
| B6a | `Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params `includeArchived`…`startDatetime` explicit; **dates are `string?` here, not `DateTimeOffset?`** | `ListComponentsFilter { Ids (ids): IReadOnlyList<int>?, UseSiteExchangeRate (use_site_exchange_rate): bool? }` | `IReadOnlyList<ComponentResponse>` → `.Component` (`Component`, **required**) | **B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Components.md` |
| B6b | `Components.ReadComponent` (by id **or** handle) | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` is the numeric id as a string **or** `"handle:my-component"` (prefix required) | — | `ComponentResponse` → `.Component` | **B** `SdkException<RawError>` | none | `operations/Components.md` |
| B6c | `Components.FindComponent` (site-wide, by handle, no family id) | `FindComponent(string handle, CancellationToken ct = default)` — bare handle, **no** `handle:` prefix (query param `handle`) | — | `ComponentResponse` → `.Component` | **B** `SdkException<RawError>` | none | `operations/Components.md` |

**Money / interval fields (map `models/records-3-Of-Su.md` `Product`, `models/records-1-Ac-Cr.md`
`Component` / `ComponentPrice`):**

`Product` (unwrap `ProductResponse.Product`) — fields you need for plan rendering:
`Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`,
**`PriceInCents (price_in_cents): long?`** ← *cents, integer*, `Interval (interval): int?`,
**`IntervalUnit (interval_unit): IntervalUnit?`** (enum: `Day (day)`, `Month (month)` — **only those two**),
`InitialChargeInCents (initial_charge_in_cents): long?`, `TrialPriceInCents (trial_price_in_cents): long?`,
`TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`,
`ExpirationInterval (expiration_interval): int?`,
`ExpirationIntervalUnit (expiration_interval_unit): ExpirationIntervalUnit?`,
`ArchivedAt (archived_at): DateTimeOffset?`, `Taxable (taxable): bool?`,
`RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`,
`ProductFamily (product_family): ProductFamily?`,
`DefaultProductPricePointId (default_product_price_point_id): int?`,
`ProductPricePointId/Handle/Name`, `VersionNumber (version_number): int?`.
→ **Billing interval = `Interval` (count) + `IntervalUnit` (day|month). Price = `PriceInCents` (long, CENTS)
— divide by 100m for display. There is no dollars/decimal price field on `Product`.**

`ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`,
`AccountingCode (accounting_code): string?`, `Description (description): string?`, `CreatedAt`, `UpdatedAt`,
`ArchivedAt` — all nullable.

`Component` (unwrap `ComponentResponse.Component`) — kind + pricing:
`Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`,
**`Kind (kind): ComponentKind?`** ← the component-KIND field,
`UnitName (unit_name): string?`, **`UnitPrice (unit_price): string?`** ← *string, DOLLARS/major units*,
**`PricePerUnitInCents (price_per_unit_in_cents): long?`** ← *cents*,
**`PricingScheme (pricing_scheme): PricingScheme?`**, `Prices (prices): IReadOnlyList<ComponentPrice?>?`,
`OveragePrices (overage_prices): IReadOnlyList<ComponentPrice?>?`,
`ProductFamilyId/Name/Handle`, `DefaultPricePointId (default_price_point_id): int?`,
`DefaultPricePointName`, `Recurring (recurring): bool?`, `Archived (archived): bool?`,
`Taxable`, `TaxCode`, `ItemCategory (item_category): ItemCategory?`,
`AllowFractionalQuantities (allow_fractional_quantities): bool?`,
`EventBasedBillingMetricId (event_based_billing_metric_id): int?`,
`Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`.

`ComponentPrice` (tier row inside `Prices`/`OveragePrices`): `Id (id): int?`,
`ComponentId (component_id): int?`, `StartingQuantity (starting_quantity): int?`,
`EndingQuantity (ending_quantity): int?`, **`UnitPrice (unit_price): string?`** (*string, major units*),
`PricePointId (price_point_id): int?`, `FormattedUnitPrice (formatted_unit_price): string?`,
`SegmentId (segment_id): int?`.
→ **Money magnitude rule for this SDK: any field whose name ends `_in_cents` is `long` CENTS; any field named
`unit_price` is a `string` in major units (parse with `decimal.Parse(..., CultureInfo.InvariantCulture)`).**

**Enums needed for §B** (map `models/enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`; these are
`StringEnum<T>` records, **not** C# enums):

| Enum | Members (`CSharpName (wire)`) |
|---|---|
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ItemCategory` | `BusinessSoftware (Business Software)`, `ConsumerSoftware (Consumer Software)`, `DigitalServices (Digital Services)`, `PhysicalGoods (Physical Goods)`, `Other (Other)` |
| `BasicDateField` (list filters) | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |

---

### C. Customers

| # | Operation | Signature | Request model | Response → unwrap | Error case | Map page |
|---|---|---|---|---|---|---|
| C7 | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable with no default → **must pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` (envelope) | `CustomerResponse` → `.Customer` (`Customer`, **required**) | **A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/Customers.md` |
| C8a | `Customers.ReadCustomerByReference` (exact match on your stable reference) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json?reference=…` | — | `CustomerResponse` → `.Customer` | **B** `SdkException<RawError>` — **no TryGet accessors** | `operations/Customers.md` |
| C8b | `Customers.ListCustomers` (search incl. by email) | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params `direction`…`q` explicit; **dates are `string?`**; the email goes in **`q`** | — | `IReadOnlyList<CustomerResponse>` → each `.Customer` | **B** `SdkException<RawError>` | `operations/Customers.md` |
| C8c | `Customers.ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` → `.Customer` | **B** `SdkException<RawError>` | `operations/Customers.md` |
| — | `Customers.UpdateCustomer` (if you ever sync name/email) | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` | `UpdateCustomerRequest` | `CustomerResponse` | **A** `SdkException<UpdateCustomerError>` — `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/Customers.md` |

**`CreateCustomer` (the inner body record, `MaxioAdvancedBilling.Models`, map `models/records-1-Ac-Cr.md`):**
`FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`,
`CcEmails (cc_emails): string?`, `Organization (organization): string?`,
**`Reference (reference): string?`** ← the stable external id for idempotency (Maxio enforces uniqueness:
one customer per reference value), `Address (address): string?`, `Address2 (address_2): string?`,
`City`, `State`, `Zip`, `Country` (ISO-3166-1 alpha-2), `Phone`, `Locale`, `VatNumber (vat_number): string?`,
`TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`,
`SalesforceId (salesforce_id): string?`.
Only `FirstName`, `LastName`, `Email` are `required` → set them in the object initializer or it won't compile.

**`Customer` (response record, map `models/records-2-Cr-Ne.md`)** — all fields nullable:
`Id (id): int?`, `FirstName`, `LastName`, `Email`, `Reference (reference): string?`, `Organization`,
`CreatedAt`/`UpdatedAt: DateTimeOffset?`, `Address`, `Address2`, `City`, `State`, `StateName`, `Zip`,
`Country`, `CountryName`, `Phone`, `Verified (verified): bool?`, `TaxExempt`, `VatNumber`, `ParentId`,
`Locale`, `SalesforceId`, `Maxioid (maxioid): string?`, `DefaultSubscriptionGroupUid`,
`DefaultAutoRenewalProfileId`, portal timestamps.

**Not-found behaviour (C8a/C8b) — idempotency directive.**
`ReadCustomerByReference` is Case B: on a miss the SDK throws `SdkException<RawError>` and you read
`ex.Error.StatusCode`. **`UNVERIFIED`: whether a missing reference returns HTTP 404 (throw) or 200 with an
empty/`null` customer (no throw) is only confirmable against live traffic — the map documents the operation
as "returns a single match" and does not model a 404 body.** Defensive directive for
`TryFindCustomerByReferenceAsync`:
1. call it inside `try`; treat a non-null `resp.Customer` as found;
2. treat a **`null` `resp.Customer`** as not-found (do not dereference);
3. `catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)` → return `null` (not-found),
   and **also** treat `400`/`422` from this lookup as not-found only if you first log `ex.Error.ReadAsString()`;
4. any other status → rethrow as `BillingProviderException`.
`ListCustomers(q: email)` is a **search, not an exact match**: it returns `200` with an **empty list** when
nothing matches (no throw), and may return near-matches — so re-verify
`string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase)` client-side before treating a hit as the
customer. **Prefer reference-lookup as the idempotency key; use email only as a secondary probe.**

---

### D. Subscriptions

| # | Operation | Signature | Request model | Response → unwrap | Error case | Pagination | Map page |
|---|---|---|---|---|---|---|---|
| D9 | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` (envelope) | `SubscriptionResponse` → `.Subscription` (`Subscription?` — **nullable, null-check**) | **A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md` |
| D10a | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | **B** `SdkException<RawError>` | none | `operations/Customers.md` |
| D10b | `Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed explicitly (pass `null`) | — | `SubscriptionResponse` → `.Subscription` | **B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| D10c | `Subscriptions.ListSubscriptions` (site-wide) | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **14 params must be passed explicitly** | — | `IReadOnlyList<SubscriptionResponse>` | **B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Subscriptions.md` |
| D10d | `Subscriptions.FindSubscription` (by *your* reference) | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed explicitly | — | `SubscriptionResponse` | **A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md` |

> **GAP (report, don't invent): `ListSubscriptions` has NO customer filter parameter** — there is no
> `customerId` / `customerReference` / metadata-customer query param in the generated signature (map
> `operations/Subscriptions.md` query-param list). **To list a customer's subscriptions you must use
> `Customers.ListCustomerSubscriptions(customerId, ct: ct)`** — which in turn has **no paging, no state
> filter, and no `include`** parameters. If you need per-customer + state filtering, filter the returned list
> client-side on `Subscription.State`.

**`CreateSubscription` (inner body record, map `models/records-2-Cr-Ne.md`) — the fields this integration
sets; everything is optional (`?`), nothing is `required`:**
`CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`;
`ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`;
`ProductPricePointHandle (product_price_point_handle): string?` / `ProductPricePointId (…): int?`;
`Reference (ref): string?` — **careful: there are two distinct fields, `Reference (reference)` and
`Ref (ref)`**; `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`;
`PaymentCollectionMethod (payment_collection_method): CollectionMethod?`;
`NetTerms (net_terms): string?`; `NextBillingAt (next_billing_at): DateTimeOffset?`;
`InitialBillingAt (initial_billing_at): DateTimeOffset?`; `DeferSignup (defer_signup): bool? = false`;
`CustomerAttributes (customer_attributes): CustomerAttributes?` (only if creating the customer inline);
`Components (components): IReadOnlyList<CreateSubscriptionComponent>?`;
`Metafields (metafields): IReadOnlyDictionary<string,string>?`;
`Currency (currency): string?`; `ProductChangeDelayed (product_change_delayed): bool?`;
`PaymentProfileId (payment_profile_id): int?` / `CreditCardAttributes` / `BankAccountAttributes` — **leave all
payment fields unset for the no-payment-method flow**; `OfferId (offer_id): OfferId?` (union);
`PrepaidConfiguration`, `Group (group): GroupSettings?`, `SalesRepId`, `ReasonCode`, `ExpiresAt`,
`SkipBillingManifestTaxes (skip_billing_manifest_taxes): bool?`.
→ UC1 body: `new CreateSubscriptionRequest { Subscription = new CreateSubscription { CustomerId = id,
ProductHandle = handle } }`. Whether the site actually permits a card-less signup depends on the product's
`RequestCreditCard`/`RequireCreditCard` flags (readable on `Product`) — a refusal surfaces as the 422
`ErrorListResponse1`.

**`Subscription` (response record, map `models/records-3-Of-Su.md`) — fields this integration reads:**
`Id (id): int?`, **`State (state): SubscriptionState?`**,
**`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`** ← the "next billing / current period
ends" field (Maxio does **not** return `next_billing_at` on reads; the update docs say to verify via
`current_period_ends_at`), **`NextAssessmentAt (next_assessment_at): DateTimeOffset?`** (next charge
assessment), `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`,
**`CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`** ← *pending-cancellation flag*,
**`DelayedCancelAt (delayed_cancel_at): DateTimeOffset?`**,
`ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`,
`CanceledAt (canceled_at): DateTimeOffset?`, `CancellationMessage (cancellation_message): string?`,
`CancellationMethod (cancellation_method): CancellationMethod?`,
`OnHoldAt (on_hold_at): DateTimeOffset?`, `AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?`,
`PreviousState (previous_state): SubscriptionState?`, `ActivatedAt`, `ExpiresAt`, `TrialStartedAt`,
`TrialEndedAt`, `CreatedAt`, `UpdatedAt`,
`BalanceInCents (balance_in_cents): long?`, `TotalRevenueInCents: long?`,
**`ProductPriceInCents (product_price_in_cents): long?`**,
`CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`,
`CreditBalanceInCents: long?`, `PrepaymentBalanceInCents: long?`,
**`Customer (customer): Customer?`** (nested full customer),
**`Product (product): Product?`** (nested full product — name/handle/price_in_cents/interval, so a single
`ReadSubscription` gives you the plan info without a second call),
`ProductPricePointId (product_price_point_id): int?`,
`ProductPricePointType (product_price_point_type): PricePointType?`,
**`NextProductId (next_product_id): int?`, `NextProductHandle (next_product_handle): string?`,
`NextProductPricePointId: int?`** ← non-null ⇒ a **delayed product change is scheduled**,
`PaymentCollectionMethod (payment_collection_method): CollectionMethod?`,
`Reference (reference): string?`, `Currency (currency): string?`, `Locale`, `CouponCode`, `CouponCodes`,
`Coupons`, `NetTerms (net_terms): int?`, `SnapDay (snap_day): string?`,
`SelfServicePageToken (self_service_page_token): string?` (only with `include`),
`PrepaidConfiguration`, `Group`, `CreditCard`, `BankAccount`, `OfferId (offer_id): int?`.

**Enums for §D** (`MaxioAdvancedBilling.Models.Enums`):

| Enum | Members |
|---|---|
| `SubscriptionState` (on `Subscription.State` / `.PreviousState`) | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` (the **list filter** — a *different* type with a different member set) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `SubscriptionInclude` (`ReadSubscription`) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` (`ListSubscriptions`) | `SelfServicePageToken (self_service_page_token)` |

> **Paused vs on-hold:** the pause endpoint is `POST /hold.json`; the state that comes back is
> `on_hold` (`SubscriptionState.OnHold`) — `Paused (paused)` also exists in the enum. Treat **both**
> `OnHold` and `Paused` as "paused" in your mapping rather than assuming one.

---

### D11. Lifecycle operations (all on `client.SubscriptionStatus`, map `operations/SubscriptionStatus.md`)

| Intent | Operation & signature | Request model | Response → unwrap | Error case (all Case A) |
|---|---|---|---|---|
| **Pause (hold)** | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` explicit (`null` allowed for an open-ended hold) | `PauseRequest { Hold (hold): AutoResume? }`; `AutoResume { AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset? }` | `SubscriptionResponse` → `.Subscription` | `SdkException<PauseSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` |
| **Change auto-resume date** | `UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` | same `PauseRequest` | `SubscriptionResponse` | `SdkException<UpdateAutomaticSubscriptionResumptionError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| **Resume** | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — **no body**; the single param is a query enum and must be passed explicitly (pass `null` for non-calendar-billing) | — (enum `ResumptionCharge`: `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)`) | `SubscriptionResponse` | `SdkException<ResumeSubscriptionError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| **Cancel immediately** | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — HTTP `DELETE /subscriptions/{id}.json`. **Immediate = omit all schedule params**: pass `body: null`, or a `CancellationRequest` whose `CancelAtEndOfPeriod`/`ScheduledCancellationAt` are left null | `CancellationRequest { Subscription (subscription): CancellationOptions !req }`; `CancellationOptions { CancellationMessage (cancellation_message): string?, ReasonCode (reason_code): string?, CancelAtEndOfPeriod (cancel_at_end_of_period): bool?, ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?, RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool? }` | `SubscriptionResponse` → `.Subscription` (state becomes `canceled`) | `SdkException<CancelSubscriptionApiError>` — **note the type name ends `ApiError`, not `Error`** — `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError(out RawError)` |
| **Cancel at end of period (delayed)** | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — HTTP `POST /subscriptions/{id}/delayed_cancel.json`. **This is the preferred expression of "cancel at period end"** (the docs note you cannot set `cancel_at_end_of_period` at creation, nor while past due) | same `CancellationRequest`/`CancellationOptions` (set `CancellationMessage`/`ReasonCode` as needed) | **`DelayedCancellationResponse { Message (message): string? }`** — **NOT a subscription**; re-read the subscription if you need the new flags | `SdkException<InitiateDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` |
| **Undo delayed cancel** | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` — `DELETE /delayed_cancel.json`; idempotent (no-op if not scheduled) | — | `DelayedCancellationResponse` → `.Message` | `SdkException<CancelDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` |
| **Reactivate** | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `PUT /reactivate.json`; works for `canceled` / `unpaid` / `trial_ended` | `ReactivateSubscriptionRequest { CalendarBilling (calendar_billing): ReactivationBilling?, IncludeTrial (include_trial): bool?, PreserveBalance (preserve_balance): bool?, CouponCode (coupon_code): string?, UseCreditsAndPrepayments (use_credits_and_prepayments): bool?, Resume (resume): Resume? (union) }` — **no envelope wrapper here**; `Resume` union: `Resume.Bool(bool)` / `Resume.ResumeOptions(ResumeOptions{ RequireResume (require_resume): bool?, ForgiveBalance (forgive_balance): bool? })`, read via `TryGetBool` / `TryGetResumeOptions` | `SubscriptionResponse` → `.Subscription` | `SdkException<ReactivateSubscriptionError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` |

**Reading "is pending cancellation" (no dedicated operation exists).** Read it off
`Subscriptions.ReadSubscription(...)` → `.Subscription`:
`CancelAtEndOfPeriod == true` (primary flag) — corroborate with `DelayedCancelAt != null` /
`ScheduledCancellationAt != null`. Expose as
`IsPendingCancellation = sub.CancelAtEndOfPeriod == true || sub.DelayedCancelAt is not null`.

---

### E. Usage / metered component (map `operations/SubscriptionComponents.md`)

| # | Operation | Signature | Request model | Response → unwrap | Error case | Pagination |
|---|---|---|---|---|---|---|
| E12 | `SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — **first two params are union types, not int/string**; `body` must be passed explicitly | `CreateUsageRequest { Usage (usage): CreateUsage !req }` (envelope); `CreateUsage { Quantity (quantity): double?, PricePointId (price_point_id): string?, Memo (memo): string?, BillingSchedule (billing_schedule): BillingSchedule?, CustomPrice (custom_price): ComponentCustomPrice? }` — **`Quantity` is `double?`, and negative values deduct** | `UsageResponse` → `.Usage` (`Usage`, **required**) | **A** `SdkException<CreateUsageError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none |
| E13a | `SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `sinceId`…`untilDate` must be passed explicitly | — | `IReadOnlyList<UsageResponse>` → each `.Usage` | **B** `SdkException<RawError>` | manual `page`+`perPage` |
| E13b | `SubscriptionComponents.ReadSubscriptionComponent` ← **running total lives here** | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — plain `int`s, no unions | — | `SubscriptionComponentResponse` → `.Component` (`SubscriptionComponent?` — **nullable**) | **A** `SdkException<ReadSubscriptionComponentError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none |
| E13c | `SubscriptionComponents.ListSubscriptionComponents` (all components on a sub; optional historic usage) | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — **12 params must be passed explicitly** | — | `IReadOnlyList<SubscriptionComponentResponse>` | **B** `SdkException<RawError>` | none |

**Unions for E (namespace `MaxioAdvancedBilling.Models.AnyOf`, map `models/unions.md`) — build with
factories, never `new`:**

| Union | Variants | Factories | Read back |
|---|---|---|---|
| `SubscriptionIdOrReference` | int, string | `SubscriptionIdOrReference.Int(subscriptionId)`, `SubscriptionIdOrReference.String(reference)` — implicit conversions from `int`/`string` also exist | `TryGetInt(out int)`, `TryGetString(out string)` |
| `ComponentIdModel` | int, string | `ComponentIdModel.Int(componentId)`, `ComponentIdModel.String("handle:my-component")` — the handle form **requires the `handle:` prefix** per the endpoint docs | `TryGetInt`, `TryGetString` |
| `Quantity1` (on `Usage.Quantity`) | int, string | `Quantity1.Int(…)`, `Quantity1.String(…)` | `TryGetInt(out int)`, `TryGetString(out string)` |
| `AllocatedQuantity2` (on `SubscriptionComponent.AllocatedQuantity`) | int, string | `AllocatedQuantity2.Int`, `AllocatedQuantity2.String` | `TryGetInt`, `TryGetString` |

**`Usage` record (map `models/records-4-Su-We.md`):** `Id (id): long?`, `Memo (memo): string?`,
`CreatedAt (created_at): DateTimeOffset?`, `PricePointId (price_point_id): int?`,
**`Quantity (quantity): Quantity1?` (union int|string)**, `OverageQuantity (overage_quantity): int?`,
`ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`,
`SubscriptionId (subscription_id): int?`.
→ **Asymmetry to code around: you WRITE `CreateUsage.Quantity` as `double?`, but READ `Usage.Quantity` as the
`Quantity1` int|string union.** Read it with
`if (u.Quantity is { } q) { if (q.TryGetInt(out var i)) … else if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) … }`.

**`SubscriptionComponent` record (map `models/records-3-Of-Su.md`) — the period-to-date total:**
`Id (id): int?`, `Name (name): string?`, **`Kind (kind): ComponentKind?`**, `UnitName (unit_name): string?`,
`Enabled (enabled): bool?`, **`UnitBalance (unit_balance): int?`** ← *the running period-to-date total for a
metered component; the `CreateUsage` docs state each usage `quantity` accumulates into `unit_balance`, with a
floor of 0*, `AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (union — the quantity-based/
on-off allocation, **not** metered usage), `PricingScheme (pricing_scheme): PricingScheme?`,
`ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`,
`SubscriptionId (subscription_id): int?`, `Currency (currency): string?`, `Recurring (recurring): bool?`,
`PricePointId/Handle/Name`, `PricePointType (price_point_type): PricePointType?`,
`ProductFamilyId/Handle`, `ArchivedAt`, `CreatedAt`, `UpdatedAt`, `Description`,
`AllowFractionalQuantities (allow_fractional_quantities): bool?`,
**`HistoricUsages (historic_usages): IReadOnlyList<HistoricUsage>?`** (only populated when
`include=historic_usages` is requested via E13c — `HistoricUsage { TotalUsageQuantity (total_usage_quantity):
double?, BillingPeriodStartsAt (billing_period_starts_at): DateTimeOffset?, BillingPeriodEndsAt
(billing_period_ends_at): DateTimeOffset? }` — last ten billing periods, i.e. **closed** periods),
`Subscription (subscription): SubscriptionComponentSubscription?`, `Interval`, `IntervalUnit`.
`ListSubscriptionComponentsInclude`: `Subscription (subscription)`, `HistoricUsages (historic_usages)`.

**Recommended read path for "period-to-date usage total" (E13):**
1. **Primary:** `ReadSubscriptionComponent(subscriptionId, componentId, ct: ct)` →
   `resp.Component?.UnitBalance` — a single call, server-computed running balance.
   **`UNVERIFIED`: that `unit_balance` is reset to 0 at period rollover for metered components (the wire
   semantics) is only confirmable against live traffic.** Defensive directive: surface it as
   `CurrentUnitBalance` (not "this period's usage"), and where the UI must show a period total, cross-check
   against option 2.
2. **Exact period-to-date sum:** `ListUsages(SubscriptionIdOrReference.Int(id), ComponentIdModel.Int(cid),
   sinceId: null, maxId: null, sinceDate: currentPeriodStartedAt, untilDate: null, page: 1, perPage: 200,
   ct: ct)` and sum `u.Usage.Quantity` yourself (union read as above), paging until a short page.
   `current_period_started_at` comes from `Subscription.CurrentPeriodStartedAt`.
3. `HistoricUsages` gives **previous** periods only — do not use it for period-to-date.

> **GAP: no single operation returns "period-to-date metered usage total" as one number.** `unit_balance` is
> the closest server-side value; a guaranteed-exact figure requires summing `ListUsages` client-side.

---

### F. Plan change with proration (map `operations/SubscriptionProducts.md`, `operations/Subscriptions.md`)

| # | Intent | Operation & signature | Request model | Response → unwrap | Error case |
|---|---|---|---|---|---|
| F14 | **Preview proration (dry run, no side effects)** | `SubscriptionProducts.PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `POST /subscriptions/{id}/migrations/preview.json`; `body` explicit | `SubscriptionMigrationPreviewRequest { Migration (migration): SubscriptionMigrationPreviewOptions !req }` (envelope); `SubscriptionMigrationPreviewOptions { ProductId (product_id): int?, ProductPricePointId (product_price_point_id): int?, IncludeTrial (include_trial): bool? = false, IncludeInitialCharge (include_initial_charge): bool? = false, IncludeCoupons (include_coupons): bool? = true, PreservePeriod (preserve_period): bool? = false, ProductHandle (product_handle): string?, ProductPricePointHandle (product_price_point_handle): string?, Proration (proration): Proration?, ProrationDate (proration_date): DateTimeOffset? }`; `Proration { PreservePeriod (preserve_period): bool? }` | `SubscriptionMigrationPreviewResponse { Migration (migration): SubscriptionMigrationPreview !req }` → `.Migration` | **A** `SdkException<PreviewSubscriptionProductMigrationError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` |
| F15a | **Commit now, with proration** | `SubscriptionProducts.MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `POST /subscriptions/{id}/migrations.json` | `SubscriptionProductMigrationRequest { Migration (migration): SubscriptionProductMigration !req }` (envelope); `SubscriptionProductMigration { ProductId (product_id): int?, ProductPricePointId (product_price_point_id): int?, IncludeTrial (include_trial): bool? = false, IncludeInitialCharge (include_initial_charge): bool? = false, IncludeCoupons (include_coupons): bool? = true, PreservePeriod (preserve_period): bool? = false, ProductHandle (product_handle): string?, ProductPricePointHandle (product_price_point_handle): string?, Proration (proration): Proration? }` — **note: no `ProrationDate` on the commit model, unlike the preview** | `SubscriptionResponse` → `.Subscription` (the updated subscription) | **A** `SdkException<MigrateSubscriptionProductError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| F15b | **Apply at next renewal, no proration (delayed product change)** | `Subscriptions.UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `PUT /subscriptions/{id}.json` | `UpdateSubscriptionRequest { Subscription (subscription): UpdateSubscription !req }` (envelope); set **`ProductHandle (product_handle)` (or `ProductId`) + `ProductChangeDelayed (product_change_delayed): bool? = true`**; optionally `ProductPricePointId/Handle`. Cancel a scheduled change by setting **`NextProductId (next_product_id): string?` to `""`** (yes — declared `string?`, not `int?`) | `SubscriptionResponse` → `.Subscription`; verify via `NextProductId`/`NextProductHandle` | **A** `SdkException<UpdateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` |

**How the two timings are expressed (verbatim from the map's operation notes):**
- **Now, prorated** → the **migrations** endpoint (F15a). It computes prorated adjustment/charge/credit;
  `PreservePeriod = true` (or `Proration { PreservePeriod = true }`) keeps the current billing period instead
  of starting a new one.
- **At next renewal, no proration** → `UpdateSubscription` with `ProductChangeDelayed = true`. The map's note
  states explicitly: *"This method schedules the product change to happen automatically at the subscription's
  next renewal date… **No proration applies in this case.**"*
- (A plain `UpdateSubscription` with `ProductHandle` and **without** `ProductChangeDelayed` is the third,
  unused option: product changes immediately but the new amount is charged at the normal start of the next
  period — **not** a prorated upgrade. Do not use it for UC3.)

**`SubscriptionMigrationPreview` — the proration amounts (map `models/records-4-Su-We.md`), ALL `long?` CENTS:**

| Field (wire) | Type | Meaning |
|---|---|---|
| `ProratedAdjustmentInCents (prorated_adjustment_in_cents)` | `long?` | prorated credit/adjustment for the unused portion of the old product (cents) |
| `ChargeInCents (charge_in_cents)` | `long?` | prorated charge for the new product (cents) |
| `PaymentDueInCents (payment_due_in_cents)` | `long?` | **net amount actually due now** (cents) — this is the number to show the customer |
| `CreditAppliedInCents (credit_applied_in_cents)` | `long?` | existing credit applied (cents) |

Divide by `100m` for display; there is no dollars/decimal variant.

---

### H. UC0 — sandbox seeding (operator tooling)

Operator console app; same client construction as §A (point `options.Server.Production.Us.Site` at the
**new** sandbox subdomain, or `…Us.BaseUrl` at an explicit override). All seeding calls are `POST`/`DELETE`
⇒ **never auto-retried** (trap note 3) — make the console idempotent by reading before creating
(`ProductFamilies.ListProductFamilies`, `Products.ReadProductByHandle`, `Components.FindComponent`).

| # | Operation (`client.X.Y`) | Signature (verbatim) | Request model (envelope → inner) | Response → unwrap | Error case + accessors | Map page |
|---|---|---|---|---|---|---|
| H1 | `ProductFamilies.CreateProductFamily` | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateProductFamilyRequest { ProductFamily (product_family): CreateProductFamily !req }` → `CreateProductFamily { Name (name): string !req, Handle (handle): string?, Description (description): string? }` | `ProductFamilyResponse` → `.ProductFamily` (`ProductFamily?` — **nullable**, null-check; read `.Id` for the next step) | **A** `SdkException<CreateProductFamilyError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/ProductFamilies.md`, `models/records-1-Ac-Cr.md` |
| H2 | `Products.CreateProduct` | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — **`productFamilyId` is `string`** (pass `familyId.ToString(CultureInfo.InvariantCulture)`, or the `"handle:my-family"` form); `body` must be passed explicitly | `CreateOrUpdateProductRequest { Product (product): CreateOrUpdateProduct !req }` → `CreateOrUpdateProduct` (full member list below) | `ProductResponse` → `.Product` (`Product`, **required**, non-null) | **A** `SdkException<CreateProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/Products.md`, `models/records-1-Ac-Cr.md` |
| H3 | `Components.CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — **`productFamilyId` is `string`**; `body` must be passed explicitly. **Note the collision: the body TYPE is also named `CreateMeteredComponent`** (`MaxioAdvancedBilling.Models.CreateMeteredComponent`) — same identifier as the method, so write `body: new MaxioAdvancedBilling.Models.CreateMeteredComponent { … }` if the compiler complains | `CreateMeteredComponent { MeteredComponent (metered_component): MeteredComponent !req }` → `MeteredComponent` (full member list below) | `ComponentResponse` → `.Component` (`Component`, **required**) | **A** `SdkException<CreateMeteredComponentError>` — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/Components.md`, `models/records-1-Ac-Cr.md`, `models/records-2-Cr-Ne.md` |
| H4a | `Products.ArchiveProduct` | `ArchiveProduct(int productId, CancellationToken ct = default)` — **`int`**, unlike create's `string` family id | — | `ProductResponse` → `.Product` (check `.ArchivedAt`) | **A** `SdkException<ArchiveProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/Products.md` |
| H4b | `Components.ArchiveComponent` | `ArchiveComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — **`productFamilyId` is `int` here** (create takes `string`); `componentId` = numeric id as string **or** `"handle:my-component"` | — | **`Component`** — a **bare record, NOT `ComponentResponse`**: do **not** write `.Component` on this one | **A** `SdkException<ArchiveComponentError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/Components.md` |
| H4c | archive/delete a **product family** | **DOES NOT EXIST** | — | — | — | `operations/ProductFamilies.md` |

**Path/param type answer (asked explicitly).** `CreateProduct` and `CreateMeteredComponent` both take the
product-family id as **`string productFamilyId`** (so `"handle:my-family"` also works). The archive/list/read
siblings take **`int`**: `ArchiveComponent(int productFamilyId, …)`,
`ListComponentsForProductFamily(int productFamilyId, …)`, `ReadComponent(int productFamilyId, …)`,
`ReadProductFamily(int id, …)`. `ListProductsForProductFamily(string productFamilyId, …)` is `string`.
**This int/string split is inconsistent inside the same controller — keep the family id as `int` in your
seeder state and `.ToString(CultureInfo.InvariantCulture)` at the two create call sites.**

**H2 — `CreateOrUpdateProduct` (inner body record, `MaxioAdvancedBilling.Models`, map
`models/records-1-Ac-Cr.md`). This is the COMPLETE member list — the create model is much smaller than the
`Product` response record:**

| Member (wire) | Type | Required? | Seeding value |
|---|---|---|---|
| `Name (name)` | `string` | **`!req`** | `"Pro Plan"` / `"Starter Plan"` |
| `Handle (handle)` | `string?` | optional | `"pro-plan"` / `"starter-plan"` |
| `Description (description)` | `string` | **`!req`** ← **easiest 422 in the whole sheet: `Description` is `required`, non-nullable, even though the response record has it nullable** | any non-empty string |
| `AccountingCode (accounting_code)` | `string?` | optional | omit |
| **`RequireCreditCard (require_credit_card)`** | `bool?` | optional | **`false`** — this is the **only** payment-method flag on the create model (see gap H-G1) |
| **`PriceInCents (price_in_cents)`** | **`long`** | **`!req`** | **CENTS, integer: `$299.00 → 29900L`, `$29.00 → 2900L`**. There is no dollars/decimal/string price field on the create model |
| `Interval (interval)` | `int` | **`!req`** | `1` |
| `IntervalUnit (interval_unit)` | `IntervalUnit` | **`!req`** | `IntervalUnit.Month` (enum has **only** `Day (day)`, `Month (month)`) |
| `TrialPriceInCents (trial_price_in_cents)` | `long?` | optional | **omit all three trial members ⇒ no trial** |
| `TrialInterval (trial_interval)` | `int?` | optional | omit |
| `TrialIntervalUnit (trial_interval_unit)` | `IntervalUnit?` | optional | omit |
| `TrialType (trial_type)` | `TrialType?` | optional | omit (`NoObligation (no_obligation)`, `PaymentExpected (payment_expected)`) |
| `ExpirationInterval (expiration_interval)` | `int?` | optional | omit |
| **`ExpirationIntervalUnit (expiration_interval_unit)`** | `ExpirationIntervalUnit?` | optional | **"expires never" = omit, or set `ExpirationIntervalUnit.Never`** — enum: `Day (day)`, `Month (month)`, **`Never (never)`** |
| `AutoCreateSignupPage (auto_create_signup_page)` | `bool?` | optional | `false` |
| `TaxCode (tax_code)` | `string?` | optional | omit |

So `$299.00/mo` =
`new CreateOrUpdateProduct { Name = "Pro Plan", Handle = "pro-plan", Description = "…", PriceInCents = 29900,
Interval = 1, IntervalUnit = IntervalUnit.Month, RequireCreditCard = false }`, wrapped in
`new CreateOrUpdateProductRequest { Product = … }`.

**H3 — `MeteredComponent` (inner body record, map `models/records-2-Cr-Ne.md`):**

| Member (wire) | Type | Required? | Seeding value |
|---|---|---|---|
| `Name (name)` | `string` | **`!req`** | `"API Calls"` |
| **`UnitName (unit_name)`** | `string` | **`!req`** ← **second-easiest 422: required, and easy to forget** | `"call"` |
| **`PricingScheme (pricing_scheme)`** | `PricingScheme` | **`!req`** | **`PricingScheme.PerUnit`** (wire `per_unit`); full enum: `Stairstep (stairstep)`, `Volume (volume)`, **`PerUnit (per_unit)`**, `Tiered (tiered)` |
| **`UnitPrice (unit_price)`** | `UnitPrice1?` **(union)** | optional | **`UnitPrice1.String("0.01")`** — union variants `string` and `double` (`UnitPrice1.String(string)` / `UnitPrice1.Double(double)`, read back `TryGetString` / `TryGetDouble`). **Build with the factory, never `new`** |
| `Prices (prices): IReadOnlyList<Price>?` | list | optional | omit for `per_unit`; see the tier shape below for the fallback |
| `PricePoints (price_points): IReadOnlyList<ComponentPricePointItem>?` | list | optional | omit |
| `Description (description)` | `string?` | optional | optional |
| `Handle (handle)` | `string?` | optional | `"api-calls"` — set it, so the app can use `ComponentIdModel.String("handle:api-calls")` |
| `Taxable (taxable)` | `bool?` | optional | `false` |
| `TaxCode (tax_code)` | `string?` | optional | omit |
| `HideDateRangeOnInvoice (hide_date_range_on_invoice)` | `bool?` | optional | omit |
| `DisplayOnHostedPage (display_on_hosted_page)` | `bool?` | optional | omit |
| `AllowFractionalQuantities (allow_fractional_quantities)` | `bool?` | optional | `false` |
| `PublicSignupPageIds (public_signup_page_ids)` | `IReadOnlyList<int>?` | optional | omit |
| `Interval (interval)` | `int?` | optional | omit (inherits the product's billing period) |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | optional | omit |

**Answer to "one generic create-component op, or per-kind ops?" — per-kind, five separate operations, and
there is NO generic create-component operation and NO `kind` parameter on any create model**
(`operations/Components.md`): `CreateMeteredComponent` (`POST /product_families/{id}/metered_components.json`),
`CreateQuantityBasedComponent`, `CreateOnOffComponent`, `CreatePrepaidUsageComponent`,
`CreateEventBasedComponent`. **The kind is implied by which operation you call**; `ComponentKind` is a
**read-only** enum that comes back on `Component.Kind` / `SubscriptionComponent.Kind`
(`MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`,
`OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`,
`EventBasedComponent (event_based_component)`). For metered, use **`Components.CreateMeteredComponent`** and
assert `resp.Component.Kind == ComponentKind.MeteredComponent` after seeding.

**Answer to "per-unit scheme: enum or tier list?" — the enum.** `PricingScheme` is a **required scalar**
(`PricingScheme.PerUnit`) and `Prices` is **optional**, so the per-unit price is expressed as the scalar
`UnitPrice` union, not a tier list. **Fallback tier shape** if the site rejects a bare `unit_price`
(`Price`, map `models/records-3-Of-Su.md`, all three members are unions):

```
Prices = new[] { new Price {
    StartingQuantity = StartingQuantity.Int(1),     // !req  (union int|string)
    EndingQuantity   = null,                        // optional (union int|string) — null = open-ended
    UnitPrice        = UnitPrice.String("0.01")     // !req  (union double|string) — NOTE: type `UnitPrice`,
} };                                                //        a DIFFERENT union from MeteredComponent's `UnitPrice1`
```
`UnitPrice` union: `UnitPrice.Double(double)` / `UnitPrice.String(string)`, read via `TryGetDouble` /
`TryGetString`. `StartingQuantity`/`EndingQuantity` unions: `.Int(int)` / `.String(string)`.

**Money units on the create models (magnitude rule restated for seeding).** The generated definitions
themselves split the two conventions: `CreateOrUpdateProduct.PriceInCents` is `long` **cents**
(`$299.00 → 29900`), while every `unit_price` member — `MeteredComponent.UnitPrice` (`UnitPrice1`),
`Price.UnitPrice` (`UnitPrice`) — is a string/double in **major units** (`$0.01 → "0.01"`), matching the
response side where `Component.UnitPrice: string` sits next to `Component.PricePerUnitInCents: long`.
**`UNVERIFIED`: that the live API interprets the create-model `unit_price` as dollars (not cents) is only
confirmable against live traffic.** Defensive directive for the seeder: after `CreateMeteredComponent`,
read back the returned `Component` and **assert `PricePerUnitInCents == 1`** (and/or
`decimal.Parse(UnitPrice, CultureInfo.InvariantCulture) == 0.01m`); if it comes back as `100`, the value was
read as cents — fail the seeding run loudly with the observed value rather than silently mispricing.
Always send the union as a **string with `CultureInfo.InvariantCulture` formatting** (`"0.01"`, never
`0,01`).

**H5 — required members that 422 if omitted (the whole list for these three creates).** These are C#
`required` on the record, so **the compiler catches them** if you use an object initializer — the 422 risk is
only if you build the model dynamically:

| Model | Required members |
|---|---|
| `CreateProductFamilyRequest` | `ProductFamily` (the envelope member itself) |
| `CreateProductFamily` | **`Name`** only (`Handle`, `Description` optional) |
| `CreateOrUpdateProductRequest` | `Product` (envelope) |
| `CreateOrUpdateProduct` | **`Name`**, **`Description`** ← *not obvious*, **`PriceInCents`**, **`Interval`**, **`IntervalUnit`** |
| `CreateMeteredComponent` (envelope) | `MeteredComponent` |
| `MeteredComponent` | **`Name`**, **`UnitName`** ← *not obvious*, **`PricingScheme`** |
| `Price` (only if you use the tier fallback) | **`StartingQuantity`**, **`UnitPrice`** (`EndingQuantity` optional) |

Plus the operation-level rule: every `body` parameter on H1–H3 is `T?` **with no default** ⇒ pass it
explicitly, and pass `ct:` by name.

**Gaps in the seeding surface (report, don't invent):**
- **H-G1 — no `request_credit_card` on the create model.** `CreateOrUpdateProduct` exposes only
  **`RequireCreditCard (require_credit_card): bool?`**; the response record `Product` carries **both**
  `RequireCreditCard` *and* `RequestCreditCard (request_credit_card): bool?`. **`request_credit_card` cannot
  be set through `CreateProduct`/`UpdateProduct`** (both take the same `CreateOrUpdateProductRequest`).
  Directive: set `RequireCreditCard = false`, then **read back `Product.RequestCreditCard` and log it**; if it
  comes back `true`, the card prompt must be turned off in the Maxio UI — the SDK cannot do it.
- **H-G2 — `taxable` cannot be set at create.** The create model has only `TaxCode`; `Product.Taxable` is
  read-only from the SDK's perspective. "Taxable = no" is therefore the site/product default — verify by
  reading back `Product.Taxable`, and set it in the UI if it is not `false`.
- **H-G3 — no setup/initial-charge field at create.** `Product.InitialChargeInCents` and
  `InitialChargeAfterTrial` exist on the **response** record only; `CreateOrUpdateProduct` has no equivalent,
  so "no setup fee" is simply "cannot be set" (which is the outcome you want).
- **H-G4 — there is NO archive/delete operation for a product family.** `client.ProductFamilies` has exactly
  four operations — `CreateProductFamily`, `ListProductFamilies`, `ListProductsForProductFamily`,
  `ReadProductFamily`. A mis-created product family **cannot be removed or archived through this SDK**;
  correct it in the Maxio UI, or create a correctly-named family and leave the stray one unused.
- **H-G5 — `ArchiveComponent` returns a bare `Component`, not `ComponentResponse`** (the only op in this
  sheet whose success payload is not enveloped) — writing `.Component` on it is a compile error.
- **H-G6 — no update-price-point / no `UpdateProduct`-only price change:** the map notes `UpdateProduct`
  *"will create a new price point and set it as the default"*. If a seeded price is wrong, prefer archiving
  the product (H4a) and creating a fresh one over updating it.
- **H-G7 — creates are `POST` ⇒ never auto-retried** and none of these three ops is idempotent server-side
  (a repeat `CreateProductFamily` with the same handle 422s). Read-before-create, and treat a 422 whose
  message mentions the handle as "already exists" rather than a hard failure.

---

## 3. Error handling — exact type per operation (§G16)

Pattern (companion `dotnet-error-handling`), with three usings for Case A
(`MaxioAdvancedBilling.Core.Exceptions`, `MaxioAdvancedBilling.Core.ErrorResponse`,
`MaxioAdvancedBilling.Errors`) and two for Case B (drop `.Errors`).
**Every operation in this SDK is throw-only — there are no `…Result`/`ApiResult` no-throw variants
(map `sdk-map.md`: "No-throw variant: absent" on all 247 operations). Never write `…Result(` calls.**

| Operation | Thrown type | Accessors (in this order) | Message source |
|---|---|---|---|
| `Products.ReadProduct` / `ReadProductByHandle` / `ListProducts` | **Case B** `SdkException<RawError>` | **none** — `ex.Error.StatusCode`, `ex.Error.ReadAsString()` | raw body string |
| `ProductFamilies.ListProductFamilies` / `ReadProductFamily` | **Case B** `SdkException<RawError>` | **none** | raw body |
| `ProductFamilies.ListProductsForProductFamily` | Case A `SdkException<ListProductsForProductFamilyError>` | `TryGetString(out string)` [404] → `TryGetRawError(out RawError)` | the 404 `string` payload, else raw |
| `Components.ListComponentsForProductFamily` / `ListComponents` / `ReadComponent` / `FindComponent` | **Case B** `SdkException<RawError>` | **none** | raw body |
| `Customers.CreateCustomer` | Case A `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] → `TryGetRawError` | `CustomerErrorResponse1.Errors (errors): Errors?` → `Errors { PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }` — **see the warning below** |
| `Customers.ReadCustomerByReference` / `ReadCustomer` / `ListCustomers` / `ListCustomerSubscriptions` | **Case B** `SdkException<RawError>` | **none** | raw body |
| `Customers.UpdateCustomer` | Case A `SdkException<UpdateCustomerError>` | `TryGetNoContent(out RawError)` [404] → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] → `TryGetRawError` | as above |
| `Subscriptions.CreateSubscription` | Case A `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `TryGetRawError` | `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` → `string.Join("; ", e.Errors)` |
| `Subscriptions.ReadSubscription` / `ListSubscriptions` | **Case B** `SdkException<RawError>` | **none** | raw body |
| `Subscriptions.FindSubscription` | Case A `SdkException<FindSubscriptionError>` | `TryGetNoContent(out RawError)` [404] → `TryGetRawError` | status only (404 body is raw/empty) |
| `Subscriptions.UpdateSubscription` | Case A `SdkException<UpdateSubscriptionError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` list |
| `SubscriptionStatus.PauseSubscription` | Case A `SdkException<PauseSubscriptionError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| `SubscriptionStatus.UpdateAutomaticSubscriptionResumption` | Case A `SdkException<UpdateAutomaticSubscriptionResumptionError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| `SubscriptionStatus.ResumeSubscription` | Case A `SdkException<ResumeSubscriptionError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| `SubscriptionStatus.CancelSubscription` | Case A **`SdkException<CancelSubscriptionApiError>`** (name ends `ApiError`) | `TryGetNoContent(out RawError)` [404] → `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] → `TryGetRawError` | `CancelSubscriptionErrorResponse` is an **AnyOf union** (`Models/AnyOf/`): `TryGetErrorListResponse1(out ErrorListResponse1)` → join `.Errors`; `TryGetSingleErrorResponse1(out SingleErrorResponse1)` → `.Error (error): string !req` |
| `SubscriptionStatus.InitiateDelayedCancellation` | Case A `SdkException<InitiateDelayedCancellationError>` | `TryGetNoContent(out RawError)` [404] → `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| `SubscriptionStatus.CancelDelayedCancellation` | Case A `SdkException<CancelDelayedCancellationError>` | `TryGetNoContent(out RawError)` [404] → `TryGetRawError` | status only |
| `SubscriptionStatus.ReactivateSubscription` | Case A `SdkException<ReactivateSubscriptionError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| `SubscriptionComponents.CreateUsage` | Case A `SdkException<CreateUsageError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| `SubscriptionComponents.ListUsages` / `ListSubscriptionComponents` | **Case B** `SdkException<RawError>` | **none** | raw body |
| `SubscriptionComponents.ReadSubscriptionComponent` | Case A `SdkException<ReadSubscriptionComponentError>` | `TryGetNoContent(out RawError)` [404] → `TryGetRawError` | status only |
| `SubscriptionProducts.PreviewSubscriptionProductMigration` | Case A `SdkException<PreviewSubscriptionProductMigrationError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| `SubscriptionProducts.MigrateSubscriptionProduct` | Case A `SdkException<MigrateSubscriptionProductError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| **§H** `ProductFamilies.CreateProductFamily` | Case A `SdkException<CreateProductFamilyError>` | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `TryGetRawError` | joined `Errors` |
| **§H** `Products.CreateProduct` | Case A `SdkException<CreateProductError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| **§H** `Components.CreateMeteredComponent` | Case A `SdkException<CreateMeteredComponentError>` | `TryGetNoContent(out RawError)` [404] → `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors`; 404 = unknown product family |
| **§H** `Products.ArchiveProduct` | Case A `SdkException<ArchiveProductError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |
| **§H** `Components.ArchiveComponent` | Case A `SdkException<ArchiveComponentError>` | `TryGetErrorListResponse1` [422] → `TryGetRawError` | joined `Errors` |

**Case B (RawError, no `TryGet` accessors at all) — the full list in this integration:**
`Products.ReadProduct`, `Products.ReadProductByHandle`, `Products.ListProducts`,
`ProductFamilies.ListProductFamilies`, `ProductFamilies.ReadProductFamily`,
`Components.ListComponentsForProductFamily`, `Components.ListComponents`, `Components.ReadComponent`,
`Components.FindComponent`, `Customers.ReadCustomer`, `Customers.ReadCustomerByReference`,
`Customers.ListCustomers`, `Customers.ListCustomerSubscriptions`, `Subscriptions.ReadSubscription`,
`Subscriptions.ListSubscriptions`, `SubscriptionComponents.ListUsages`,
`SubscriptionComponents.ListSubscriptionComponents`.
For these: `ex.Error` **is** the `RawError` — read `(int)ex.Error.StatusCode` and `ex.Error.ReadAsString()`.
Do **not** call `ReadAsJson<T>()` on them (it throws `JsonException` on a non-JSON body).

**Warning — `CustomerErrorResponse1` looks wrong for customer validation.** The generated payload for
`CreateCustomer`'s 422 is `CustomerErrorResponse1 { Errors (errors): Errors? }` where
`Errors { PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }`
— i.e. the shared `Errors` record carries only `per_page`/`price_point` members, which have nothing to do
with customer fields (map `models/records-2-Cr-Ne.md`). This is visible **in the generated definitions
themselves**: a suspicious shared model reused across unrelated operations. **`UNVERIFIED` — what the live
422 body actually contains cannot be confirmed from the map.** Defensive directive for
`CreateCustomerAsync`: in the 422 branch, extract best-effort —
`e422.Errors?.PerPage`, `e422.Errors?.PricePoint` (join whatever is non-empty) — and when both are
null/empty **fall back to `ex.Message` plus the HTTP status**, never to `ex.Error.ToString()` (which yields
only the type name). Because `TryGetRawError` returns `false` for a status that has a typed accessor, you
cannot recover the raw 422 body through it — so log `ex.Message` in that branch.

**Boundary rules for `MaxioBillingClient` (companion `dotnet-error-handling`):**
- Read typed accessors **inside** the per-operation `catch` — never in a shared `Describe(ApiError)` helper
  (the base `ApiError` exposes only `TryGetRawError`, so a shared helper silently loses every typed body).
- `TryGetRawError` is **always the last branch** — it is not a catch-all; a status with a typed or
  status-specific accessor leaves it `false`.
- Add a second catch for connection failures at the same boundary:
  `catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) → BillingProviderException("billing provider unreachable", ex)`.
  A `catch (SdkException<…>)` will **not** match these.
- Guard **every** call site, including the read-only catalog calls on page load.
- `AuthSchemeException` is not a concern here (single Basic scheme).

---

## 4. Trap notes (attach to the step named)

1. **Step 2 (client):** the only ctor is `(HttpClient, MaxioAdvancedBillingClientOptions)` — the `HttpClient`
   must be long-lived/factory-managed and the SDK client built **once**, not per call.
2. **Step 2:** `RetryOptions` members are all `required` — never `new RetryOptions { MaxRetries = 5 }`;
   write `RetryOptions.Default() with { MaxRetries = 5, Timeout = TimeSpan.FromSeconds(30) }`.
3. **Step 2:** retries cover **`GET/HEAD/PUT/OPTIONS` only** by default (statuses 408/429/500/502/503/504,
   3 retries, 1s delay, ×2 backoff, ≤500ms jitter). **`POST`/`PATCH`/`DELETE` are never retried** — so
   `CreateSubscription`, `CreateCustomer`, `CreateUsage`, `PauseSubscription`,
   `InitiateDelayedCancellation`, `MigrateSubscriptionProduct` and both cancels get **no** automatic retry.
   Any retry of those is yours to write, and must be idempotency-guarded (customer `reference`,
   subscription lookup-before-create).
4. **Step 2:** `RetryOptions.Timeout` (default 100s) is **per attempt, not total** — cap a whole call with a
   `CancellationToken`, passed as `ct:`.
5. **Steps 3/6/8 (every list/search call):** **use named arguments.** `ListSubscriptions` has 14
   no-default params, `ListSubscriptionComponents` 12, `ListProductsForProductFamily`/`ListProducts` 8,
   `ListCustomers` 7 — a positional call mis-binds silently. And the token parameter is `ct:`, never
   `cancellationToken:`.
6. **Steps 4/5/8/9 (writes):** every write body is an **envelope with exactly one required member** —
   `CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`, `CreateUsageRequest.Usage`,
   `CancellationRequest.Subscription`, `PauseRequest.Hold`, `UpdateSubscriptionRequest.Subscription`,
   `SubscriptionProductMigrationRequest.Migration`, `SubscriptionMigrationPreviewRequest.Migration`.
   Forgetting the wrapper is the most common 422. **Exception:** `ReactivateSubscriptionRequest` has **no**
   wrapper — its fields sit at the top level.
7. **All steps:** `body` parameters are declared `T?` **with no default** ⇒ you must pass them explicitly,
   even when `null` (e.g. `CancelSubscription(id, body: null, ct: ct)` for an immediate cancel).
8. **Steps 3/6/7:** enums are `StringEnum<T>` **records**, not C# enums. Write
   `SubscriptionState.Active`, compare with `==` (value equality), read the wire value with `.Value`, build
   unknown values with `FromValue("…")`, and guard incoming values with `IsKnownValue()` /
   `TryGetKnownValue(...)` — a future Maxio state must not throw in your mapper.
9. **Step 8/9:** unions are built with **static factories** (`SubscriptionIdOrReference.Int(id)`,
   `ComponentIdModel.String("handle:x")`) and read with `TryGet…(out …)` — **never `new`**, never a cast.
10. **All steps:** unmodeled JSON fields are **dropped** on deserialization — anything not in the record
    tables above is unreachable through the SDK.
11. **Steps 3/6:** response envelopes differ in nullability — `ProductResponse.Product`,
    `ComponentResponse.Component`, `CustomerResponse.Customer`, `UsageResponse.Usage` are **required**
    (non-null), while `SubscriptionResponse.Subscription`, `ProductFamilyResponse.ProductFamily` and
    `SubscriptionComponentResponse.Component` are **nullable** — null-check those three.
12. **Step 7:** `InitiateDelayedCancellation` and `CancelDelayedCancellation` return
    `DelayedCancellationResponse { Message }`, **not** a subscription — re-read the subscription if the
    caller needs updated state/flags.
13. **Step 2 (US/EU):** only the options of the environment selected by `options.Environment` are read —
    setting `Server.Production.Eu.BaseUrl` while `Environment = Us` silently does nothing.
14. **Testing:** the `HttpClient` constructor argument is the test seam (companion `dotnet-testing`) — stub
    with a custom `HttpMessageHandler`; there are no SDK mocking helpers. `IBillingClient` keeps the domain
    tests SDK-free.

---

## 5. Gaps the SDK does NOT expose (report, don't invent)

1. **No customer filter on `ListSubscriptions`** — must use `Customers.ListCustomerSubscriptions(customerId)`,
   which itself has **no paging, no state filter, no `include`**. (`operations/Subscriptions.md`,
   `operations/Customers.md`)
2. **`ProductFamilies.ReadProductFamily(int id)` takes an `int` only** — although the endpoint documentation
   mentions a `handle:my-family` form, the generated C# parameter is `int`, so **reading a product family by
   handle is not possible through this method**. Work around it with `ListProductFamilies(...)` +
   client-side match on `ProductFamily.Handle`, or `ListProductsForProductFamily("handle:my-family", …)`
   (that one takes `string productFamilyId`). (`operations/ProductFamilies.md`)
3. **No single "period-to-date usage total" operation** — `SubscriptionComponent.UnitBalance` is the closest
   server-side number; an exact figure requires summing `ListUsages` client-side (§E13).
4. **No dedicated "is pending cancellation" endpoint** — derive from
   `Subscription.CancelAtEndOfPeriod` / `DelayedCancelAt` / `ScheduledCancellationAt`.
5. **No no-throw (`ApiResult`) variants anywhere** — so HTTP status/headers on *success* (rate-limit headers,
   `Link` pagination headers) are **not reachable**. Add a `DelegatingHandler` on your `HttpClient` if you
   need them; there is also **no built-in logging hook** in the SDK.
6. **No decimal/dollar money fields** — everything is `*_in_cents` (`long`) or a `unit_price` string; the
   integration owns cents→decimal conversion and rounding.
7. **`Customers.ReadCustomerByReference` returns no typed 404 model** (Case B) — not-found must be detected
   from `RawError.StatusCode`, see the §C directive.
8. **Auto-pagination:** every list op in scope is manual `page`/`perPage` (or, for
   `Customers.ListCustomerSubscriptions` / `ListSubscriptionComponents` / `ListProductFamilies`, has **no**
   paging at all) — there is no `IAsyncEnumerable` auto-pager on these rows.

---

## 6. Assumptions & Blockers

**Assumptions**
1. "US/EU environment" is selected via `options.Environment` = `ServerEnvironment.Us` per the brief; the EU
   path is wired but unused.
2. The metered component (UC2) is a **`ComponentKind.MeteredComponent`** — `CreateUsage` is valid for
   metered and prepaid components only, and `ListUsages` is explicitly **not compatible with quantity-based
   components**.
3. UC3's "plan" = a Maxio **product** (not a price point) — migration bodies use `ProductHandle`/`ProductId`;
   price-point handling is out of scope.
4. Customer idempotency key = your app's stable id written to `CreateCustomer.Reference`; Maxio enforces
   uniqueness on it, so lookup-then-create is a safe pattern.
5. Subscriptions are created against an existing `CustomerId` (not `CustomerAttributes` inline creation).
6. The sandbox site permits card-less signup for the products in scope (product-level
   `RequestCreditCard`/`RequireCreditCard` decide this; a refusal shows up as the 422 `ErrorListResponse1`).
7. The `HttpClient` is registered by you (typed `AddHttpClient<MaxioBillingClient>`), not by the SDK's
   `AddMaxioAdvancedBillingClient` — because the brief requires a custom `HttpClient` and the map does not
   confirm an `HttpClient`-accepting overload on that extension.

**Blockers** — none for implementation. Two rows are flagged for source resolution if they surface
(`ServiceCollectionExtensions.AddMaxioAdvancedBillingClient`'s full signature/overloads; the declared
namespace of `ServerOptions`/`ProductionOptions`) and two facts are labelled `UNVERIFIED` with defensive
directives already written into the sheet (`ReadCustomerByReference` not-found status; `unit_balance` period
semantics; plus the suspicious `CustomerErrorResponse1`/`Errors` 422 payload).
