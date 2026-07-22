# Maxio Advanced Billing → `MaxioBillingClient` (Infrastructure) — implementation plan + contract sheet

Target: one Infrastructure class `MaxioBillingClient : IBillingClient` (interface in ApplicationCore),
backed by NuGet package `AsadAli.AdvancedBilling.Sdk` (root namespace `MaxioAdvancedBilling`), SDK
`v1.0.2` / commit `15db14b`.

Everything below is grounded in the bundled SDK map (`sdk-map.md`, `map/operations/*.md`,
`map/models/*.md`); each row cites its page. Nothing here is from memory.

---

## 1. Scope & sequence

| # | Step | SDK surface used |
|---|---|---|
| 1 | Options + DI: `MaxioBillingOptions` (ApiKey, Subdomain, BaseUrlOverride, Region), register a **named** `HttpClient` via `IHttpClientFactory`, construct `MaxioAdvancedBillingClient` once (singleton) from that `HttpClient` + `MaxioAdvancedBillingClientOptions`. | `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `BasicAuthCredentials`, `ServerEnvironment`, `options.Server.Production.Us.{BaseUrl,Site}` |
| 2 | Error boundary: one private helper per error case that converts `SdkException<…>` → your typed domain exception (status + message), plus a `HttpRequestException`/`TaskCanceledException` branch. Never put options/credentials into the exception message. | `SdkException<T>`, `RawError`, `{Operation}Error` |
| 3 | Plans (UC1): list products of a family, read product by handle, read product by id. | `ProductFamilies.ListProductsForProductFamily`, `Products.ReadProductByHandle`, `Products.ReadProduct`, `Products.ListProducts` |
| 4 | Product families: list / find by handle. | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ReadProductFamily` |
| 5 | Customers: lookup by reference, create, read by id. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ReadCustomer` |
| 6 | Subscriptions: create (product handle + customer id, or `customer_attributes`), list for customer, read by id. | `Subscriptions.CreateSubscription`, `Customers.ListCustomerSubscriptions`, `Subscriptions.ReadSubscription` |
| 7 | Metered usage (UC2): list/find components, record usage, read period-to-date total. | `Components.ListComponentsForProductFamily`, `Components.FindComponent`, `Components.ReadComponent`, `SubscriptionComponents.CreateUsage`, `SubscriptionComponents.ListUsages`, `SubscriptionComponents.ReadSubscriptionComponent` |
| 8 | Plan change (UC3): proration preview, apply-now migration, delayed change at renewal. | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `SubscriptionProducts.MigrateSubscriptionProduct`, `Subscriptions.UpdateSubscription` |
| 9 | Lifecycle (UC4): pause, resume, cancel now, cancel at period end, undo delayed cancel, reactivate. | `SubscriptionStatus.*` |
| 10 | Map SDK models → ApplicationCore DTOs (never leak SDK types out of Infrastructure). | — |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** (e.g.
> `MaxioAdvancedBilling.Models.Enums.SubscriptionState`,
> `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference`,
> `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`, and the **client-config
> types**: `MaxioAdvancedBilling.Servers.ServerEnvironment`,
> `MaxioAdvancedBilling.Core.Configuration.RetryOptions`). The map carries these namespaces — do not drop
> them to the root or `.Models`, or the implementer guesses the wrong `using` and the build breaks.

### 2.0 Namespaces (`using` directives) — `sdk-map.md`

| Contents | Namespace |
|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `AddMaxioAdvancedBillingClient` | `MaxioAdvancedBilling` |
| Controllers (`client.Products`, `client.Subscriptions`, …) | `MaxioAdvancedBilling.Api` |
| Records (`Product`, `Subscription`, `CreateCustomerRequest`, …) | `MaxioAdvancedBilling.Models` |
| Enums (`SubscriptionState`, `ComponentKind`, …) | `MaxioAdvancedBilling.Models.Enums` |
| Unions (`SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`, …) | `MaxioAdvancedBilling.Models.AnyOf` (OneOf unions: `MaxioAdvancedBilling.Models.OneOf`) |
| Typed error classes (`CreateCustomerError`, …) | `MaxioAdvancedBilling.Errors` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `ApiError`, `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |

C# does **not** import child namespaces transitively — each of the above needs its own `using`.

### 2.1 Client construction, auth, base URL — `sdk-map.md` ("Getting a client", "Servers & auth")

| Fact | Value |
|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` |
| **Only** constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` |
| Auth (confirmed) | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<maxio api key>", Password = "x" }` — **`Username` = API key, `Password` = literal `"x"`**. Basic is the only scheme. |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default, wire `US`) or `.Eu` (wire `EU`) |
| Production base-URL templates | US `https://{site}.chargify.com` · EU `https://{site}.ebilling.maxio.com` |
| Subdomain override point | `options.Server.Production.Us.Site = "your-subdomain";` (EU: `options.Server.Production.Eu.Site`). `{site}` defaults to the literal `subdomain`. |
| **Explicit base-URL override** | `options.Server.Production.Us.BaseUrl = "http://localhost:8080";` — a literal URL with no `{placeholders}` is used as-is (`dotnet-configuration-resilience`). Override wins because it replaces the template that `Site` feeds. |
| Second server group | `Ebb` (events ingest), template `https://events.chargify.com/{site}`, override `options.Server.Ebb.Us.BaseUrl` / `.Site`. **Only** `SubscriptionComponents.RecordEvent` / `BulkRecordEvents` use it — not needed for UC2 metered usage below, but if you point Production at a mock you must also point `Ebb` there if you ever call those. |
| DI extension | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; });` (source `ServiceCollectionExtensions.cs`). It resolves the **default, unnamed** `IHttpClientFactory` client. |
| Retry defaults | `RetryOptions.Default()`: retries `408,429,500,502,503,504`; methods `GET,HEAD,PUT,OPTIONS` only; `MaxRetries=3`; `Delay=1s`; `BackOffFactor=2`; exponential; `MaxJitter=500ms`; `Timeout=100s` **per attempt**. All `RetryOptions` members are `required` — customize via `RetryOptions.Default() with { … }`, never `new RetryOptions { … }` partially. |

**Recommended wiring for this project** (named `HttpClient` + explicit construction, because
`AddMaxioAdvancedBillingClient` binds only the default unnamed factory client):

```csharp
services.AddHttpClient("maxio");                    // long-lived pipeline, factory-managed
services.AddSingleton(sp =>
{
    var cfg  = sp.GetRequiredService<IOptions<MaxioBillingOptions>>().Value;
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("maxio");
    var o = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
    {
        BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
                    { Username = cfg.ApiKey, Password = "x" },
        Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
    };
    if (!string.IsNullOrWhiteSpace(cfg.BaseUrlOverride)) o.Server.Production.Us.BaseUrl = cfg.BaseUrlOverride;
    else if (!string.IsNullOrWhiteSpace(cfg.Subdomain)) o.Server.Production.Us.Site     = cfg.Subdomain;
    return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(http, o);
});
services.AddScoped<IBillingClient, MaxioBillingClient>();   // thin wrapper, cheap
```

`Server`/`ProductionOptions` are only ever reached **through the existing `options.Server` instance**
(`options.Server.Production.Us.BaseUrl = …`), so no `using` for those types is needed — that is why their
namespace is deliberately not asserted here (see Assumptions/Blockers).

### 2.2 Operations — one row per operation

Legend: **Case A** = `SdkException<{Op}Error>` with typed `TryGet…` accessors; **Case B** =
`SdkException<RawError>` (read `StatusCode` / `ReadAsString()` straight off `ex.Error`, no accessors).
"must pass" = nullable parameter with **no** C# default → must be supplied (pass `null` to skip).

#### Products / plans — `operations/Products.md`, `operations/ProductFamilies.md`

| Op | Signature (verbatim) | Request model | Returns | Error | Paging |
|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 must-pass (`dateField`…`include`) | — | `IReadOnlyList<ProductResponse>` | **A** `ListProductsForProductFamilyError`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | `page` + `perPage` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` | **B** `RawError` | none |
| `client.Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` | **B** `RawError` | none |
| `client.Products.ListProducts` (site-wide, only if family filter not wanted) | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 must-pass | — | `IReadOnlyList<ProductResponse>` | **B** `RawError` | `page` + `perPage` |
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must-pass | — | `IReadOnlyList<ProductFamilyResponse>` | **B** `RawError` | **none** (no page/perPage at all) |
| `client.ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse` | **B** `RawError` | none |

**Envelopes** (`records-3-Of-Su.md`):
`ProductResponse` = exactly one field `Product (product): Product !req`.
`ProductFamilyResponse` = one field `ProductFamily (product_family): ProductFamily?` (**nullable** — null-check it).

**`Product` fields you asked for** (`records-3-Of-Su.md`, `Models/Product.cs`, all `MaxioAdvancedBilling.Models`):

| C# property (wire) | Type | Note |
|---|---|---|
| `Id (id)` | `int?` | |
| `Name (name)` | `string?` | |
| `Handle (handle)` | `string?` | |
| `Description (description)` | `string?` | |
| **`PriceInCents (price_in_cents)`** | `long?` | **CENTS.** There is **no** `Price`/`price` field on `Product` at all — the map lists only `*_in_cents` money fields. Divide by 100m for display. |
| `Interval (interval)` | `int?` | |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | string-enum: `Day (day)`, `Month (month)` |
| `ProductFamily (product_family)` | `ProductFamily?` | nested |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | null ⇒ not archived |
| `RequestCreditCard (request_credit_card)` | `bool?` | |
| `RequireCreditCard (require_credit_card)` | `bool?` | both exist, distinct fields |
| also useful | `InitialChargeInCents: long?`, `TrialPriceInCents: long?`, `TrialInterval: int?`, `TrialIntervalUnit: IntervalUnit?`, `ExpirationInterval: int?`, `ExpirationIntervalUnit: ExpirationIntervalUnit?`, `AccountingCode: string?`, `Taxable: bool?`, `ProductPricePointId: int?`, `ProductPricePointHandle: string?`, `ProductPricePointName: string?`, `DefaultProductPricePointId: int?`, `VersionNumber: int?`, `CreatedAt/UpdatedAt: DateTimeOffset?` | | |

**`ProductFamily` fields**: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`,
`AccountingCode (accounting_code): string?`, `Description (description): string?`,
`CreatedAt/UpdatedAt/ArchivedAt: DateTimeOffset?`.

**Find product family by handle — the trap.** `ReadProductFamily` takes `int id`, so the documented
`handle:my-family` form is **not expressible** through it. Two supported routes:
1. `ListProductFamilies(null, null, null, null, null, ct: ct)` then match `ProductFamily.Handle` client-side; or
2. skip the family id entirely — `ListProductsForProductFamily` takes `string productFamilyId`, so
   `productFamilyId: "handle:my-family"` **is** expressible there (map notes on `ReadProductFamily`
   confirm the `handle:` convention for the family identifier).

#### Customers — `operations/Customers.md`

| Op | Signature | Request model | Returns | Error | Paging |
|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query `reference`) | — | `CustomerResponse` | **B** `RawError` | none |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` | `CustomerResponse` | **A** `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| `client.Customers.ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` | **B** `RawError` | none |
| `client.Customers.ListCustomers` (search fallback) | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 must-pass | — | `IReadOnlyList<CustomerResponse>` | **B** `RawError` | `page` + `perPage` (default 50) |

`CustomerResponse` = one field `Customer (customer): Customer !req`.

`CreateCustomer` (`records-1-Ac-Cr.md`) — **required**: `FirstName (first_name): string !req`,
`LastName (last_name): string !req`, `Email (email): string !req`. Optional and relevant:
`Reference (reference): string?`, `Organization`, `CcEmails`, `Address`, `Address2`, `City`, `State`,
`Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt: bool?`, `TaxExemptReason`, `ParentId: int?`,
`SalesforceId`.

`Customer` (read model, `records-2-Cr-Ne.md`): `Id (id): int?`, `FirstName`, `LastName`, `Email`,
`Reference (reference): string?`, `Organization`, `CreatedAt/UpdatedAt: DateTimeOffset?`, `Address*`,
`City`, `State`, `Zip`, `Country`, `Phone`, `Verified: bool?`, `TaxExempt: bool?`, `VatNumber`,
`ParentId: int?`, `Locale`, `Maxioid: string?`.

**Reference not found:** `ReadCustomerByReference` is **Case B**, so the not-found path arrives as
`SdkException<RawError>` and you read `ex.Error.StatusCode`. The *exact* status the live wire returns for a
missing reference is **not** in the map — `UNVERIFIED`. **Directive:** treat `HttpStatusCode.NotFound`
**and** any 4xx-with-empty/`null` customer as "not found" and return `null` from
`IBillingClient.FindCustomerByReferenceAsync`; also null-check `response.Customer` even on 200. Do **not**
branch on a hard-coded 404 only.

#### Subscriptions — `operations/Subscriptions.md`, `operations/Customers.md`

| Op | Signature | Request model | Returns | Error | Paging |
|---|---|---|---|---|---|
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` | `SubscriptionResponse` | **A** `CreateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` | **B** `RawError` | **none** — this endpoint has no page/perPage |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must pass (`null` to skip) | — | `SubscriptionResponse` | **B** `RawError` | none |
| `client.Subscriptions.ListSubscriptions` (site-wide filter alternative) | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 must-pass | — | `IReadOnlyList<SubscriptionResponse>` | **B** `RawError` | `page` + `perPage` |
| `client.Subscriptions.FindSubscription` (by your own reference) | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must pass | — | `SubscriptionResponse` | **A** `FindSubscriptionError`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none |

`SubscriptionResponse` = one field `Subscription (subscription): Subscription?` — **nullable**, null-check.

**`CreateSubscription` request fields you need** (`records-2-Cr-Ne.md`, 50 fields total; relevant subset):
`ProductHandle (product_handle): string?`, `ProductId (product_id): int?`,
`ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId: int?`,
`CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`,
`CustomerAttributes (customer_attributes): CustomerAttributes?`,
`PaymentProfileId (payment_profile_id): int?`,
`PaymentCollectionMethod (payment_collection_method): CollectionMethod?`,
`Reference (reference): string?`, `Ref (ref): string?`,
`CouponCode`/`CouponCodes`, `NextBillingAt`/`InitialBillingAt`/`PreviousBillingAt: DateTimeOffset?`,
`Components (components): IReadOnlyList<CreateSubscriptionComponent>?`,
`Metafields: IReadOnlyDictionary<string,string>?`,
`ProductChangeDelayed (product_change_delayed): bool?`, `Currency: string?`,
`DeferSignup: bool? = false`. No field is `required` on `CreateSubscription` — the envelope
`CreateSubscriptionRequest.Subscription` **is** `!req`.

`CustomerAttributes` (for create-customer-inline): `FirstName`, `LastName`, `Email`, `CcEmails`,
`Organization`, `Reference`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`,
`Verified: bool?`, `TaxExempt: bool?`, `VatNumber`, `Metafields: IReadOnlyDictionary<string,string>?`,
`ParentId: int?`, `SalesforceId`, `DefaultAutoRenewalProfileId: int?` — **all nullable**, none required.

**`Subscription` fields you asked for** (`records-3-Of-Su.md`, `Models/Subscription.cs`):

| C# property (wire) | Type |
|---|---|
| `Id (id)` | `int?` |
| `State (state)` | `SubscriptionState?` (string-enum — see 2.4) |
| `PreviousState (previous_state)` | `SubscriptionState?` |
| `Product (product)` | `Product?` (full nested product) |
| `Customer (customer)` | `Customer?` (full nested customer) |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` |
| **`BalanceInCents (balance_in_cents)`** | `long?` — **cents** |
| `TotalRevenueInCents`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `CreditBalanceInCents`, `PrepaymentBalanceInCents` | `long?` — all cents |
| `CancelAtEndOfPeriod (cancel_at_end_of_period)` | `bool?` |
| `CanceledAt`, `DelayedCancelAt`, `ScheduledCancellationAt`, `OnHoldAt`, `AutomaticallyResumeAt`, `ActivatedAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `CreatedAt`, `UpdatedAt` | `DateTimeOffset?` |
| `CancellationMessage (cancellation_message)` | `string?` |
| `CancellationMethod (cancellation_method)` | `CancellationMethod?` (enum) |
| `ReasonCode (reason_code)` | `string?` |
| `NextProductId (next_product_id)` | `int?` · `NextProductHandle (next_product_handle): string?` · `NextProductPricePointId: int?` (these show a **pending delayed product change**) |
| `Reference (reference)` | `string?` |
| `ProductPricePointId: int?`, `ProductPricePointType: PricePointType?`, `PaymentCollectionMethod: CollectionMethod?`, `Currency: string?`, `NetTerms: int?`, `Coupons: IReadOnlyList<SubscriptionIncludedCoupon>?`, `SelfServicePageToken: string?` | |

#### Components & metered usage (UC2) — `operations/Components.md`, `operations/SubscriptionComponents.md`

| Op | Signature | Request model | Returns | Error | Paging |
|---|---|---|---|---|---|
| `client.Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 must-pass. Note `productFamilyId` is **`int`** here (no `handle:` form) and the date params are **`string?`**, not `DateTimeOffset?` | — | `IReadOnlyList<ComponentResponse>` | **B** `RawError` | `page` + `perPage` |
| `client.Components.FindComponent` (by handle, site-wide) | `FindComponent(string handle, CancellationToken ct = default)` (query `handle`) | — | `ComponentResponse` | **B** `RawError` | none |
| `client.Components.ReadComponent` (by id **or** `handle:` within a family) | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` is `string`, so pass `"handle:my-component"` or the numeric id as a string | — | `ComponentResponse` | **B** `RawError` | none |
| `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` must pass | `CreateUsageRequest { Usage (usage): CreateUsage !req }` | `UsageResponse` | **A** `CreateUsageError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 must-pass (`sinceId`…`untilDate`) | — | `IReadOnlyList<UsageResponse>` | **B** `RawError` | `page` + `perPage` |
| `client.SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | — | `SubscriptionComponentResponse` | **A** `ReadSubscriptionComponentError`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none |
| `client.SubscriptionComponents.ListSubscriptionComponents` (all components on a sub) | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 must-pass | — | `IReadOnlyList<SubscriptionComponentResponse>` | **B** `RawError` | **none** |

**Union construction** (`unions.md`, namespace `MaxioAdvancedBilling.Models.AnyOf`; factories only, no `new`):
- `SubscriptionIdOrReference.Int(int)` / `SubscriptionIdOrReference.String(string)`; readers `TryGetInt`/`TryGetString`; implicit from `int`/`string`.
- `ComponentIdModel.Int(int)` / `ComponentIdModel.String(string)` — pass `ComponentIdModel.String("handle:sms-messages")` to address the component by handle (the `ListUsages` map note documents the `handle:` prefix).
- `Quantity1.Int(int)` / `Quantity1.String(string)`; readers `TryGetInt(out int)` / `TryGetString(out string)`.

**Envelopes**: `ComponentResponse { Component (component): Component !req }` ·
`UsageResponse { Usage (usage): Usage !req }` ·
`SubscriptionComponentResponse { Component (component): SubscriptionComponent? }` (**nullable**, and note
the property is named `Component`, not `SubscriptionComponent`).

**`CreateUsage` request record** (`records-2-Cr-Ne.md`): `Quantity (quantity): double?`,
`PricePointId (price_point_id): string?`, `Memo (memo): string?`,
`BillingSchedule (billing_schedule): BillingSchedule?`, `CustomPrice (custom_price): ComponentCustomPrice?`.
`Quantity` is **`double?`** on the write side (negative values deduct, per the map note).

**`Usage` read record** (`records-4-Su-We.md`): `Id (id): long?`, `Memo (memo): string?`,
`CreatedAt (created_at): DateTimeOffset?`, `PricePointId (price_point_id): int?`,
**`Quantity (quantity): Quantity1?` (union int|string)**, `OverageQuantity (overage_quantity): int?`,
`ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`,
`SubscriptionId (subscription_id): int?`. Note the asymmetry: you **write** `double`, you **read** a
`int|string` union — summing requires `TryGetInt` first, then `TryGetString` + `decimal.TryParse` with
`CultureInfo.InvariantCulture`.

**`Component` fields (kind / unit price / pricing scheme)** (`records-1-Ac-Cr.md`):
`Id: int?`, `Name: string?`, `Handle: string?`, `Description: string?`,
**`Kind (kind): ComponentKind?`**, **`PricingScheme (pricing_scheme): PricingScheme?`**,
`UnitName (unit_name): string?`, **`UnitPrice (unit_price): string?`** (a **string**, not a number),
**`PricePerUnitInCents (price_per_unit_in_cents): long?`** (cents, numeric),
`Prices (prices): IReadOnlyList<ComponentPrice?>?`, `OveragePrices: IReadOnlyList<ComponentPrice?>?`,
`ProductFamilyId: int?`, `ProductFamilyHandle: string?`, `DefaultPricePointId: int?`,
`DefaultPricePointName: string?`, `PricePointCount: int?`, `Recurring: bool?`, `Taxable: bool?`,
`Archived: bool?`, `ArchivedAt: DateTimeOffset?`, `AllowFractionalQuantities: bool?`,
`Interval: int?`, `IntervalUnit: IntervalUnit?`, `EventBasedBillingMetricId: int?`,
`UpgradeCharge/DowngradeCredit: CreditType?`.
`ComponentPrice`: `Id: int?`, `ComponentId: int?`, `StartingQuantity: int?`, `EndingQuantity: int?`,
**`UnitPrice (unit_price): string?`**, `FormattedUnitPrice (formatted_unit_price): string?`,
`PricePointId: int?`, `SegmentId: int?`.
⇒ For a **tiered/volume/stairstep** scheme the single `Component.UnitPrice` is not the whole story —
read `Prices` (per-tier `StartingQuantity`/`EndingQuantity`/`UnitPrice`). For `per_unit`,
`PricePerUnitInCents` is the safe numeric field.

**`SubscriptionComponent`** (`records-3-Of-Su.md`) — what UC2's "period-to-date total" reads:
`Id: int?`, `Name: string?`, **`Kind (kind): ComponentKind?`**, `UnitName: string?`, `Enabled: bool?`,
**`UnitBalance (unit_balance): int?`**, `Currency: string?`,
`AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (union int|string —
`AllocatedQuantity2.Int/String`, `TryGetInt/TryGetString`), `PricingScheme: PricingScheme?`,
`ComponentId: int?`, `ComponentHandle: string?`, `SubscriptionId: int?`, `PricePointId/Handle/Name/Type`,
`ProductFamilyId: int?`, `ProductFamilyHandle: string?`,
`HistoricUsages (historic_usages): IReadOnlyList<HistoricUsage>?`, `ArchivedAt`, `CreatedAt`, `UpdatedAt`,
`Interval: int?`, `IntervalUnit: IntervalUnit?`.
`HistoricUsage`: `TotalUsageQuantity (total_usage_quantity): double?`,
`BillingPeriodStartsAt`, `BillingPeriodEndsAt: DateTimeOffset?` — but the map's summary says it is
**"Optional for Event Based Components. If the `include=historic_usages` query param is provided"**, and
`ReadSubscriptionComponent` has **no** `include` parameter, so you cannot request it there.

**What the SDK actually offers for "period-to-date total for a component on a subscription" — pick one:**
1. **`ReadSubscriptionComponent(subscriptionId, componentId, ct: ct)` → `.Component.UnitBalance` (`int?`).**
   Cheapest: one call, a single accumulated number. The map's `CreateUsage` note states verbatim: *"The
   `quantity` from usage for each component is accumulated to the `unit_balance` on the Component Line Item
   for the subscription"*, and *"`unit_balance` has a floor of `0`"*. Requires the **numeric** component id
   (`int`) — no handle form on this operation.
2. **`ListUsages(...)` + sum client-side.** Only route that gives per-record detail (memo/created_at) and
   the only route that accepts a component **handle**. Use `sinceDate`/`untilDate` to bound to the current
   period (read `Subscription.CurrentPeriodStartedAt` / `CurrentPeriodEndsAt` first), and **page** —
   `perPage` default is 20, so loop pages until a short page comes back. Map note: `ListUsages` *"is not
   compatible with quantity-based components"*.
   **Recommendation:** use (1) for the balance, (2) when the UI needs the itemised list.
3. Not applicable: `ListAllocations` is for Quantity/On-Off/Prepaid allocations, not metered usage.

#### Plan change (UC3) — `operations/SubscriptionProducts.md`, `operations/Subscriptions.md`

| Op | Signature | Request model | Returns | Error |
|---|---|---|---|---|
| **Preview proration** `client.SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` must pass | `SubscriptionMigrationPreviewRequest { Migration (migration): SubscriptionMigrationPreviewOptions !req }` | `SubscriptionMigrationPreviewResponse { Migration (migration): SubscriptionMigrationPreview !req }` | **A** `PreviewSubscriptionProductMigrationError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Apply now, with proration** `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` must pass | `SubscriptionProductMigrationRequest { Migration (migration): SubscriptionProductMigration !req }` | `SubscriptionResponse` | **A** `MigrateSubscriptionProductError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **At next renewal, no proration** `client.Subscriptions.UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | `UpdateSubscriptionRequest { Subscription (subscription): UpdateSubscription !req }` | `SubscriptionResponse` | **A** `UpdateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |

`SubscriptionMigrationPreviewOptions` (`records-4-Su-We.md`): `ProductId (product_id): int?`,
`ProductHandle (product_handle): string?`, `ProductPricePointId: int?`, `ProductPricePointHandle: string?`,
`IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge (include_initial_charge): bool? = false`,
`IncludeCoupons (include_coupons): bool? = true`, `PreservePeriod (preserve_period): bool? = false`,
`Proration (proration): Proration?`, **`ProrationDate (proration_date): DateTimeOffset?`** (preview-only field).

`SubscriptionProductMigration` (`records-4-Su-We.md`): same fields **minus** `ProrationDate` —
`ProductId`, `ProductHandle`, `ProductPricePointId`, `ProductPricePointHandle`,
`IncludeTrial: bool? = false`, `IncludeInitialCharge: bool? = false`, `IncludeCoupons: bool? = true`,
`PreservePeriod: bool? = false`, `Proration (proration): Proration?`.

`Proration` record (`records-3-Of-Su.md`, `Models/Proration.cs`): its **only** field is
`PreservePeriod (preserve_period): bool?`. There is no on/off proration flag on the migration body — the
migration endpoint prorates by definition (it is the "prorated upgrade/downgrade" path the
`UpdateSubscription` docs point you to), so *apply-now-with-proration* = call
`MigrateSubscriptionProduct`; you do not set a "proration: true" flag.

**Preview result — field names and units** (`SubscriptionMigrationPreview`, `records-4-Su-We.md`):

| C# property (wire) | Type | Unit |
|---|---|---|
| `ProratedAdjustmentInCents (prorated_adjustment_in_cents)` | `long?` | **cents** |
| `ChargeInCents (charge_in_cents)` | `long?` | **cents** |
| `PaymentDueInCents (payment_due_in_cents)` | `long?` | **cents** |
| `CreditAppliedInCents (credit_applied_in_cents)` | `long?` | **cents** |

That is the whole record — four `long?` cent amounts, no line items, no currency field.

**Delayed change (no proration)** — `UpdateSubscription` body fields (`records-4-Su-We.md`,
`Models/UpdateSubscription.cs`): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`,
**`ProductChangeDelayed (product_change_delayed): bool?`**, `ProductPricePointId (product_price_point_id): int?`,
`ProductPricePointHandle: string?`, `NextProductId (next_product_id): **string?**`,
`NextProductPricePointId: **string?**`, `NextBillingAt: DateTimeOffset?`,
`SnapDay (snap_day): SnapDay1?` (union string|int), `NetTerms (net_terms): NetTerms1?` (union string|int),
`Reference: string?`, `Components: IReadOnlyList<UpdateSubscriptionComponent>?`, `CustomPrice`, etc.

- **Now, no proration, at next period start:** `UpdateSubscription` with `ProductHandle` set and
  `ProductChangeDelayed` **unset/false** — map note: *"The new payment amount is calculated and charged at
  the normal start of the next period."*
- **Scheduled at next renewal:** `UpdateSubscription` with `ProductHandle` set **and
  `ProductChangeDelayed = true`** — map note: *"No proration applies in this case."* Verify afterwards by
  reading `Subscription.NextProductId` / `NextProductHandle`.
- **Cancel a pending delayed change:** set `NextProductId = ""` (empty string — hence its `string?` type).
- **Immediate, prorated:** `MigrateSubscriptionProduct` (subscription must be `active` or `trialing`;
  migrating to the *current* product is the most common failure, per the map note).

#### Lifecycle (UC4) — `operations/SubscriptionStatus.md`

| Intent | Op | Signature | Request model | Returns | Error | Resulting state |
|---|---|---|---|---|---|---|
| Pause (hold) | `client.SubscriptionStatus.PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` must pass (`null` allowed) | `PauseRequest { Hold (hold): AutoResume? }`; `AutoResume { AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset? }` | `SubscriptionResponse` | **A** `PauseSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `SubscriptionState.OnHold` (`on_hold`); `Subscription.OnHoldAt` set. Map limitation: **cannot** pause if `next_billing_at` is within 24 h → 422. |
| Change/remove auto-resume date | `client.SubscriptionStatus.UpdateAutomaticSubscriptionResumption` | `UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` | `PauseRequest` (set `Hold.AutomaticallyResumeAt = null` to clear) | `SubscriptionResponse` | **A** same accessors as pause | stays `on_hold` |
| Resume a paused sub | `client.SubscriptionStatus.ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — **no body**; the 2nd param is a *query* param (`calendar_billing['resumption_charge']`) and must pass (`null` for non-calendar-billing) | — | `SubscriptionResponse` | **A** `ResumeSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | back to `active` (or reactivation-like if the renewal date has passed) |
| Cancel immediately | `client.SubscriptionStatus.CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` must pass; **pass `null` (or omit schedule fields) for an immediate cancel** | `CancellationRequest { Subscription (subscription): CancellationOptions !req }`; `CancellationOptions { CancellationMessage (cancellation_message): string?, ReasonCode (reason_code): string?, CancelAtEndOfPeriod (cancel_at_end_of_period): bool?, ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?, RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool? }` | `SubscriptionResponse` | **A** `CancelSubscriptionApiError` (note the **`ApiError` suffix** — not `CancelSubscriptionError`): `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError` [fallback] | `SubscriptionState.Canceled`; `CanceledAt`, `CancellationMessage`, `CancellationMethod = merchant_api` |
| Cancel at end of period | `client.SubscriptionStatus.InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` must pass | `CancellationRequest` (as above; `CancellationMessage`/`ReasonCode` carry the reason) | **`DelayedCancellationResponse { Message (message): string? }`** — ⚠ **not** a `SubscriptionResponse`; you get only a message string, so re-`ReadSubscription` if you need the new state | **A** `InitiateDelayedCancellationError`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | subscription stays `active` with `CancelAtEndOfPeriod = true` and `DelayedCancelAt` set; fails if past due |
| Undo a delayed cancel | `client.SubscriptionStatus.CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | — | `DelayedCancellationResponse` | **A** `CancelDelayedCancellationError`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | `CancelAtEndOfPeriod` reset to `false`; **idempotent** |
| Reactivate a cancelled sub | `client.SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass (`null` = plain reactivate, new billing period) | `ReactivateSubscriptionRequest { CalendarBilling (calendar_billing): ReactivationBilling?, IncludeTrial (include_trial): bool?, PreserveBalance (preserve_balance): bool?, CouponCode (coupon_code): string?, UseCreditsAndPrepayments (use_credits_and_prepayments): bool?, Resume (resume): Resume? (union) }`; `Resume.Bool(bool)` or `Resume.ResumeOptions(new ResumeOptions { RequireResume (require_resume): bool?, ForgiveBalance (forgive_balance): bool? })` | `SubscriptionResponse` | **A** `ReactivateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `active` (or `trialing` when `IncludeTrial = true` / resuming a trial). Works for `canceled`, `unpaid`, `trial_ended` only. `Resume = Resume.Bool(true)` keeps the original billing date when within the cancelled period. |

Not lifecycle but adjacent: `client.Subscriptions.ActivateSubscription` (awaiting-signup/trialing →
active, Relationship Invoicing only) and `client.SubscriptionStatus.RetrySubscription` (past-due retry).

### 2.3 Error handling (item 9)

**Model** (`sdk-map.md` "Error-handling model"): every operation is **throw-only** — this SDK generates
**no** `{Operation}Result`/`ApiResult` no-throw variants. Non-2xx throws
`MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` with a single property
`public required TError Error { get; init; }`.

- **Case B** (`TError = MaxioAdvancedBilling.Core.ErrorResponse.RawError`): `RawError` is **not** an
  `ApiError` and has **no** `TryGet…` at all. Read `ex.Error.StatusCode` (`System.Net.HttpStatusCode`),
  `ex.Error.ReadAsString()` (`string`), `ex.Error.ReadAsBytes()` (`ReadOnlyMemory<byte>`),
  `ex.Error.ReadAsJson<T>()` (`T?` — **throws `JsonException`** on a non-JSON body, so prefer
  `ReadAsString()`).
- **Case A** (`TError = MaxioAdvancedBilling.Errors.{Op}Error : ApiError`): status code is **not** a
  property on the typed error — the **only** way to get a numeric status is through a
  `TryGet…(out RawError)` accessor (`TryGetNoContent`, or the inherited
  `TryGetRawError(out RawError)`), then `raw.StatusCode`. For a status that has a *typed* accessor (e.g.
  422 → `ErrorListResponse1`), **`TryGetRawError` returns `false`** — it is not a catch-all. So: write one
  `if/else if` per accessor listed in the row above, `TryGetRawError` **last**, and infer the status from
  which branch fired (422 for the typed-payload branches, 404 for `TryGetNoContent`).

**Case-A/Case-B tally for this integration** (from the rows above):

| Case B (`SdkException<RawError>`) | Case A (typed) |
|---|---|
| `Products.ReadProduct`, `Products.ReadProductByHandle`, `Products.ListProducts`, `ProductFamilies.ListProductFamilies`, `ProductFamilies.ReadProductFamily`, `Customers.ReadCustomer`, `Customers.ReadCustomerByReference`, `Customers.ListCustomers`, `Customers.ListCustomerSubscriptions`, `Subscriptions.ReadSubscription`, `Subscriptions.ListSubscriptions`, `Components.FindComponent`, `Components.ReadComponent`, `Components.ListComponents(ForProductFamily)`, `SubscriptionComponents.ListUsages`, `SubscriptionComponents.ListSubscriptionComponents` | `ProductFamilies.ListProductsForProductFamily`, `Customers.CreateCustomer`, `Customers.UpdateCustomer`, `Subscriptions.CreateSubscription`, `Subscriptions.UpdateSubscription`, `Subscriptions.FindSubscription`, `SubscriptionComponents.CreateUsage`, `SubscriptionComponents.ReadSubscriptionComponent`, `SubscriptionProducts.MigrateSubscriptionProduct`, `SubscriptionProducts.PreviewSubscriptionProductMigration`, `SubscriptionStatus.CancelSubscription` (`CancelSubscriptionApiError`), `SubscriptionStatus.InitiateDelayedCancellation`, `SubscriptionStatus.CancelDelayedCancellation`, `SubscriptionStatus.PauseSubscription`, `SubscriptionStatus.ResumeSubscription`, `SubscriptionStatus.ReactivateSubscription`, `SubscriptionStatus.UpdateAutomaticSubscriptionResumption` |

**Error payload shapes** (records pages):

| Payload | Fields | Message extraction |
|---|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | `string.Join("; ", e.Errors)` — the good case |
| `CustomerErrorResponse1` | `Errors (errors): Errors?` where record `Errors` = `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` | ⚠ see trust note T-1 below — **cannot** carry customer validation text |
| `CancelSubscriptionErrorResponse` (union, `unions.md`) | variants `ErrorListResponse1`, `SingleErrorResponse1`; readers `TryGetErrorListResponse1(out …)`, `TryGetSingleErrorResponse1(out …)` | try both readers in that order |
| `RawError` | `StatusCode`, `ReadAsString()` | use the string body |

**Domain-exception boundary — required shape** (`dotnet-error-handling`):

```csharp
catch (SdkException<CreateCustomerError> ex)   // Case A: one branch per accessor, TryGetRawError LAST
{ … throw new BillingProviderException(status, message, ex); }
catch (SdkException<RawError> ex)              // Case B
{ throw new BillingProviderException((int)ex.Error.StatusCode, ex.Error.ReadAsString(), ex); }
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{ throw new BillingProviderException(503, "billing provider unreachable", ex); }
```

- Guard **every** call site, including the read-only ones — a connection failure on a read is not an
  `SdkException` and will escape a `catch (SdkException<…>)`.
- **Never** build a shared `string Describe(ApiError e)` helper: the typed `TryGet…` accessors live on the
  concrete `{Op}Error`, not on `ApiError`, so such a helper can only reach `TryGetRawError` and silently
  degrades to `e.ToString()` (a bare type name). Read the accessors inside each per-operation `catch`.
- **API-key safety:** the key only ever lives in `options.BasicAuth.Username`. Never log the
  `MaxioAdvancedBillingClientOptions`/`BasicAuthCredentials` object, never put `ex.ToString()` of an
  `HttpRequestException` chain into a user-facing message, and never echo request headers. `RawError`
  carries the **response** body only, which is safe to surface (still: log it, don't return it verbatim to
  an end user).
- `AuthSchemeException` cannot occur here — single (Basic) scheme (`dotnet-error-handling` note).

### 2.4 Enums needed (`models/enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`)

These are **`StringEnum<T>` records, not C# enums.** Construct with the static member
(`SubscriptionState.Active`) or `SubscriptionState.FromValue("active")` for a runtime value; read back
with `.Value` (raw wire string); `==` compares by value; guard unknowns with
`X.TryGetKnownValue(v, out var known)` / `instance.IsKnownValue()`. They convert implicitly to their
underlying string.

**`SubscriptionState`** — `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`,
`Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`,
`Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`,
`TrialEnded (trial_ended)`, **`OnHold (on_hold)`**, `AwaitingSignup (awaiting_signup)`.
⚠ Both `Paused (paused)` **and** `OnHold (on_hold)` exist. `PauseSubscription` hits the **hold** endpoint
(`/hold.json`) and the docs describe the "on hold" state — map your domain `Paused` to **`on_hold`** and
treat `paused` as a synonym on read.

**`SubscriptionStateFilter`** (for `ListSubscriptions(state:)` only — a *different* type from
`SubscriptionState`) — `Active (active)`, `Canceled (canceled)`, `Expired (expired)`,
`ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`,
`PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`,
`TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`.

**`ComponentKind`** — `MeteredComponent (metered_component)`,
`QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`,
`PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)`.

**`PricingScheme`** — `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)`.

**`IntervalUnit`** — `Day (day)`, `Month (month)`.
**`ExpirationIntervalUnit`** — `Day (day)`, `Month (month)`, `Never (never)`.
**`ResumptionCharge`** — `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)`.
**`SortingDirection`** — `Asc (asc)`, `Desc (desc)`.
**`BasicDateField`** — `UpdatedAt (updated_at)`, `CreatedAt (created_at)`.
**`SubscriptionInclude`** (`ReadSubscription`) — `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)`.
**`SubscriptionListInclude`** (`ListSubscriptions`) — `SelfServicePageToken (self_service_page_token)`.
**`ListProductsInclude`** — `PrepaidProductPricePoint (prepaid_product_price_point)`.
**`CollectionMethod`** — `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`.
**`CancellationMethod`** — `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)`.
**`PricePointType`** — `Catalog (catalog)`, `Default (default)`, `Custom (custom)`.
**`CreditType`** — `Full (full)`, `Prorated (prorated)`, `None (none)`.
**`ItemCategory`** — `BusinessSoftware (Business Software)`, `ConsumerSoftware (Consumer Software)`, `DigitalServices (Digital Services)`, `PhysicalGoods (Physical Goods)`, `Other (Other)` (note: wire values contain **spaces and capitals**).

### 2.5 Cancellation tokens & paging (item 10)

- Every operation's last parameter is `CancellationToken ct = default`. **In named calls write `ct: ct`.**
  There are no sync overloads; all methods are `Task`-returning.
- `RetryOptions.Timeout` (default 100 s) is **per attempt**, not total — to bound a whole call (retries
  included) pass a `CancellationToken` from a `CancellationTokenSource(TimeSpan)`.
- Paging is **manual** everywhere in this scope — this SDK's list operations here return
  `IReadOnlyList<…>`, **not** `IAsyncEnumerable<…>`, so there is no auto-pagination to `await foreach`.
  Loop `page: n` from 1 until a page returns fewer than `perPage` items.

| Operation | Paging params | Defaults |
|---|---|---|
| `ProductFamilies.ListProductsForProductFamily` | `page`, `perPage` | 1 / 20 |
| `Products.ListProducts` | `page`, `perPage` | 1 / 20 |
| `Customers.ListCustomers` | `page`, `perPage` | 1 / **50** |
| `Subscriptions.ListSubscriptions` | `page`, `perPage` | 1 / 20 |
| `Components.ListComponents`, `Components.ListComponentsForProductFamily` | `page`, `perPage` | 1 / 20 |
| `SubscriptionComponents.ListUsages` | `page`, `perPage` | 1 / 20 |
| `SubscriptionComponents.ListAllocations` | **`page` only** (no `perPage`) | 1 |
| `ProductFamilies.ListProductFamilies`, `Customers.ListCustomerSubscriptions`, `SubscriptionComponents.ListSubscriptionComponents` | **none** | — |

---

## 3. Trap notes (attach to the step named)

- **T-1 (step 5, `CreateCustomer` 422) — trust judgment, map-visible.** `CreateCustomerError`'s 422
  accessor yields `CustomerErrorResponse1`, whose only field is `Errors: Errors?`, and the `Errors` record
  declares **only** `PerPage (per_page)` and `PricePoint (price_point)` string lists. Two generated
  definitions disagree with each other's purpose: a *customer* validation body cannot plausibly be
  `{per_page, price_point}` — this is a shared/mis-bound generated model (the same `Errors` record is
  reachable from unrelated operations). Consequence, since 422 has a typed accessor:
  **`TryGetRawError` will return `false` for a 422** and you will be left with an empty
  `CustomerErrorResponse1`. **Directive:** in the `CreateCustomer` catch, try
  `e.Errors?.PerPage`/`e.Errors?.PricePoint` best-effort, and when both are null/empty throw your domain
  exception with status `422` and a **generic** message ("customer could not be created — validation
  failed"), not `ex.Error.ToString()`. Whether the live wire body actually matches this model is
  `UNVERIFIED` (only live traffic can confirm).
- **T-2 (steps 3–9) — named arguments are mandatory.** Every list/search op above has 4–14 leading
  nullable params with **no C# default**, so a positional call mis-binds or fails to compile. Always call
  named, e.g.
  `client.ProductFamilies.ListProductsForProductFamily(productFamilyId: famId, dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null, page: 1, perPage: 100, ct: ct)`.
- **T-3 (all write steps) — envelope pattern.** Every write body is a one-field envelope
  (`CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`, `CreateUsageRequest.Usage`,
  `UpdateSubscriptionRequest.Subscription`, `CancellationRequest.Subscription`,
  `SubscriptionProductMigrationRequest.Migration`, `SubscriptionMigrationPreviewRequest.Migration`,
  `PauseRequest.Hold`) and that field is `required` (except `PauseRequest.Hold`, which is nullable).
  Forgetting the envelope is a compile error; **passing the envelope with a null inner object is not**.
- **T-4 (steps 6–9) — response envelopes are sometimes nullable.**
  `SubscriptionResponse.Subscription`, `ProductFamilyResponse.ProductFamily`,
  `SubscriptionComponentResponse.Component`, `AllocationResponse.Allocation` are `T?`;
  `ProductResponse.Product`, `CustomerResponse.Customer`, `ComponentResponse.Component`,
  `UsageResponse.Usage` are `!req`. Null-check the first group before mapping.
- **T-5 (everywhere) — enums are `StringEnum<T>`, not C# enums.** `switch` on `state.Value` (the wire
  string) or compare against the constants; a `switch` expression over the record type won't behave like a
  C# enum switch. Unknown/future wire values do **not** throw — they round-trip, so guard with
  `IsKnownValue()` before assuming.
- **T-6 (step 7) — usage quantity type asymmetry.** Write `CreateUsage.Quantity` as `double?`; read
  `Usage.Quantity` as the `Quantity1` int|string union. Sum with `TryGetInt` then
  `TryGetString` + `decimal.TryParse(..., CultureInfo.InvariantCulture, ...)`.
- **T-7 (steps 1–2) — HttpClient/client lifetime.** The SDK does **not** own the `HttpClient`; keep both
  the `HttpClient` (via `IHttpClientFactory`) and the `MaxioAdvancedBillingClient` long-lived (singleton).
  Never construct either per request. `MaxioBillingClient` itself may be scoped/transient.
- **T-8 (step 2) — retries skip your writes.** Default `HttpMethodsToRetry` is `GET/HEAD/PUT/OPTIONS`
  only, so `CreateSubscription`/`CreateUsage`/`CancelSubscription` (`POST`/`DELETE`) surface errors with
  **no** retry. Do not add `POST` to the retry list — `CreateUsage` and `CreateSubscription` are not
  idempotent. There is **no** built-in logging hook: add a `DelegatingHandler` on the named `HttpClient`
  if you want request/response logging (and redact the `Authorization` header there).
- **T-9 (step 8) — units.** Every money field in scope ends in `_in_cents` and is `long?`. There is **no**
  dollar-denominated field anywhere in `Product`, `Subscription`, or `SubscriptionMigrationPreview`.
  `Component.UnitPrice` / `ComponentPrice.UnitPrice` are the exception: they are **`string?`** (and
  `FormattedUnitPrice` is a display string) — parse with `InvariantCulture`, or prefer
  `Component.PricePerUnitInCents`.
- **T-10 (step 9) — `InitiateDelayedCancellation` returns `DelayedCancellationResponse` (a bare
  `Message: string?`)**, not the subscription. If `IBillingClient` promises the post-cancel state, follow
  with `ReadSubscription`.
- **T-11 (step 1) — mock host.** Pointing `options.Server.Production.Us.BaseUrl` at
  `http://localhost:8080` covers every operation in this plan (all are in the **Production** group). The
  two Ebb event-ingest ops are out of scope; if they ever come into scope, `options.Server.Ebb.Us.BaseUrl`
  must be set separately.
- **T-12 (unmodeled fields)** — the SDK drops JSON fields it doesn't model on deserialize. Don't expect to
  read anything not listed in the record rows above.
- **T-13 (testing)** — the `HttpClient` constructor argument is the only test seam; there are no SDK
  mocking helpers. Stub with a custom `HttpMessageHandler` and assert
  `SdkException<{the exact error type in the row}>` on error paths.

---

## 4. Assumptions & Blockers

**Assumptions**

1. `IBillingClient` is provider-agnostic, so **no** `MaxioAdvancedBilling.*` type crosses the
   ApplicationCore boundary — all SDK records/enums are mapped to your own DTOs inside
   `MaxioBillingClient`. The sheet gives SDK-side names only.
2. US hosting (`ServerEnvironment.Us`) is the default; EU is a config switch. Both override points are
   listed.
3. "List plans" = products of **one** product family. `Products.ListProducts` (site-wide) is included as
   an alternative but the family-scoped call is what the sheet sequences.
4. Payment profiles / credit-card capture are **out of scope** — subscriptions are created without
   `credit_card_attributes`, which the map warns may fail on products where `RequireCreditCard = true`.
5. UC3 "apply now with proration" = `MigrateSubscriptionProduct`; "at next renewal without proration" =
   `UpdateSubscription` + `ProductChangeDelayed = true`. Both readings are backed by the map's operation
   notes, but the choice of which one your domain calls "upgrade now" is a product decision.
6. UC2's "period-to-date total" is satisfied by `SubscriptionComponent.UnitBalance`, with
   `ListUsages`-and-sum as the itemised fallback. Which one `IBillingClient` exposes is your call.

**Blockers / unresolved**

- **UNVERIFIED (live traffic only):** the HTTP status `ReadCustomerByReference` returns for an unknown
  reference (404 vs 200-with-empty-body). Handled by the defensive directive in §2.2 (treat 404 *and*
  4xx/empty as not-found, and null-check the payload on 200).
- **UNVERIFIED (live traffic only):** whether the real 422 body of `CreateCustomer` matches the generated
  `CustomerErrorResponse1`/`Errors` shape (trap T-1). Handled by the best-effort-then-generic directive.
- **UNVERIFIED (live traffic only):** whether a mock at `http://localhost:8080` needs the `/{site}` path
  segment. The map gives the *template*, not what your mock expects; the literal `BaseUrl` override is
  used as-is with no `{site}` substitution, so build mock routes to match the plain paths in the HTTP
  column of §2.2 (e.g. `GET /products/handle/{api_handle}.json`).
- **Exact overload signature of `AddMaxioAdvancedBillingClient`** (parameter type — `Action<…Options>` vs
  an `IHttpClientBuilder`-returning overload, and whether a named-client overload exists). The map shows
  only the usage form `services.AddMaxioAdvancedBillingClient(o => { … })` from
  `ServiceCollectionExtensions.cs`. **→ `maxio-debug` resolves from source if it surfaces.** Not on the
  critical path: the §2.1 wiring constructs the client explicitly from a named `IHttpClientFactory` client
  and never calls the extension.
- **Declared namespace of `ServerOptions` / `ProductionOptions` / `EbbOptions`.** `sdk-map.md` gives the
  source paths `ServerOptions.cs`, `Server.cs` (repo root ⇒ `MaxioAdvancedBilling`) and
  `Servers/ProductionOptions.cs`, `Servers/EbbOptions.cs` (⇒ `MaxioAdvancedBilling.Servers`), but does not
  state the namespaces outright and the root-level pair is ambiguous.
  **→ `maxio-debug` resolves from source if it surfaces.** Not on the critical path: the plan only ever
  touches these through `options.Server.Production.Us.…`, which needs no `using` and no type name.
- **`ReactivationBilling`, `BillingSchedule`, `ComponentCustomPrice`, `UpdateSubscriptionComponent`,
  `CreateSubscriptionComponent` field lists** were not pulled — none is required by the sequenced steps.
  If a later requirement needs them, they are one lookup away on the records pages
  (`records-1-Ac-Cr.md` / `records-2-Cr-Ne.md` / `records-3-Of-Su.md` / `records-4-Su-We.md`).
