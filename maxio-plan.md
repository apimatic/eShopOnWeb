# Maxio Advanced Billing — `MaxioBillingClient` implementation plan + CONTRACT SHEET

Target: one Infrastructure class `MaxioBillingClient : IBillingClient` (ApplicationCore), eShopOnWeb .NET 8.

Every fact below was read this session from the SDK map that ships with the SDK source
(`sdk-map.md`, `map/operations/*.md`, `map/models/*.md`) or, where the map was ambiguous, from the exact
source file the map names. Nothing here is from memory.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | NuGet + options/DI wiring (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Region`, `Maxio:BaseUrl`) | `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient(...)` |
| 2 | Catalog read (plans page) | `Products.ListProducts`, `ProductFamilies.ListProductsForProductFamily`, `Products.ReadProductByHandle`, `ProductFamilies.ReadProductFamily`, `Components.ListComponentsForProductFamily` |
| 3 | Customer upsert keyed on eShop email as `reference` | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ReadCustomer` |
| 4 | Subscribe (UC1) | `Subscriptions.CreateSubscription`, `Subscriptions.ReadSubscription` |
| 5 | Subscription queries | `Subscriptions.ListSubscriptions`, `Customers.ListCustomerSubscriptions` |
| 6 | Metered usage (UC2) | `SubscriptionComponents.CreateUsage`, `SubscriptionComponents.ListUsages`, `SubscriptionComponents.ReadSubscriptionComponent` |
| 7 | Plan change (UC3): preview then commit | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `SubscriptionProducts.MigrateSubscriptionProduct`, `Subscriptions.UpdateSubscription` (delayed path) |
| 8 | Lifecycle (UC4) | `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` / `CancelSubscription` / `InitiateDelayedCancellation` / `CancelDelayedCancellation` / `ReactivateSubscription` |
| 9 | Seeding (UC0) | `ProductFamilies.CreateProductFamily`, `Products.CreateProduct`, `Components.CreateMeteredComponent`, `Products.ArchiveProduct`, `Components.ArchiveComponent` |
| 10 | Unit tests against stubbed JSON | `HttpMessageHandler` seam |

**NuGet package id:** `AsadAli.AdvancedBilling.Sdk` — **reference version `1.0.2`**.
Root namespace to `using` is `MaxioAdvancedBilling` (differs from the package id).
Transitive deps: `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents`.
Target framework of the SDK: `netstandard2.0` (fine for .NET 8).
*Version caveat — see Assumptions & Blockers, item B1.*

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it**
> (e.g. `MaxioAdvancedBilling.Models.Enums.SubscriptionState`,
> `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference`,
> `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`, and the client-config types:
> `MaxioAdvancedBilling.Servers.ServerEnvironment`,
> `MaxioAdvancedBilling.Core.Configuration.RetryOptions`, and — **verified from source, this one is NOT
> under `Core.Configuration`** — `MaxioAdvancedBilling.ServerOptions` (root namespace; see 2.1).
> Do not drop namespaces or the implementer guesses the wrong `using` and the build breaks.

### 2.1 Client construction, auth, server/base-URL (capability A) — `sdk-map.md`; source `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/ServerEnvironment.cs`, `MaxioAdvancedBillingClientOptions.cs`, `ServiceCollectionExtensions.cs`, `ServiceCollectionExtensions` verified

| Fact | Value |
|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` |
| **Only** constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` (plain settable properties, not a builder) |
| `options.Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` — default `ServerEnvironment.Default()` == `ServerEnvironment.Us` |
| `options.Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (default `RetryOptions.Default()`) |
| `options.Server` | `MaxioAdvancedBilling.ServerOptions` — **root namespace**, source `ServerOptions.cs` (`namespace MaxioAdvancedBilling;` read verbatim) |
| `options.BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` |
| `BasicAuthCredentials` shape | `public required string Username { get; init; }`, `public required string Password { get; init; }` — **both `required`** |
| **Auth model — CONFIRMED** | HTTP Basic only. `Username` = the Maxio/Chargify **API key**; `Password` = the literal `"x"`. This is stated in the map *and* in the XML doc on `MaxioAdvancedBillingClientOptions.BasicAuth`. There is no other scheme on the options class. |

**Environment enum values (exact):** `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (wire `"US"`) and
`.Eu` (wire `"EU"`); `ServerEnvironment.Default()` returns `Us`. It is a `StringEnum<ServerEnvironment>`
record, not a C# enum — compare with `==`, build unknowns with `FromValue("US")`.

**Server / base URL — the SDK DOES expose a raw base-URL override.** `ServerOptions` has two server groups,
each with per-environment nested options carrying **both** `BaseUrl` and `Site`:

```
options.Server.Production.Us.BaseUrl  // default "https://{site}.chargify.com"
options.Server.Production.Us.Site     // default "subdomain"
options.Server.Production.Eu.BaseUrl  // default "https://{site}.ebilling.maxio.com"
options.Server.Production.Eu.Site
options.Server.Ebb.Us.BaseUrl / .Site // default "https://events.chargify.com/{site}" (events ingest only)
options.Server.Ebb.Eu.BaseUrl / .Site
```

Nested types (verified in `Servers/ProductionOptions.cs`):
`MaxioAdvancedBilling.Servers.ProductionOptions` with public nested classes `UsOptions` / `EuOptions`,
both `public string BaseUrl { get; set; }` and `public string Site { get; set; }`. `ServerOptions` exposes
`Production` (`ProductionOptions`) and `Ebb` (`MaxioAdvancedBilling.Servers.EbbOptions`).

So both configurations you asked for are supported on the **same build**:

* **(a) subdomain-derived sandbox host, US:**
  `options.Environment = ServerEnvironment.Us; options.Server.Production.Us.Site = "apimatic-hackathon";`
  → resolves to `https://apimatic-hackathon.chargify.com`.
* **(b) arbitrary explicit base URL, honored verbatim:**
  `options.Server.Production.Us.BaseUrl = "http://localhost:8080";`
  A literal URL with no `{site}` placeholder is used as-is (the URL template substitutes `{site}` only if
  the template contains it — `ProductionOptions.Resolve` builds `new UrlTemplate(Us.BaseUrl, path, [site])`).
  **Directive:** when `Maxio:BaseUrl` is present in config, set `BaseUrl` verbatim **and still set `Site`**
  (harmless if unused) so a template-bearing custom URL also works. All non-events operations in this plan
  are in the **Production** group, so overriding `Server.Production.<env>.BaseUrl` is sufficient.

**DI registration (exists).** `MaxioAdvancedBilling.ServiceCollectionExtensions` — a C# 14 `extension(IServiceCollection services)` member:

```csharp
public IServiceCollection AddMaxioAdvancedBillingClient(
    Action<MaxioAdvancedBillingClientOptions>? configure = null)
```

Source-verified behaviour (this differs from the generic companion-skill wording, which says "transient"):
it calls `services.AddHttpClient()`, then **`services.AddSingleton(sp => new MaxioAdvancedBillingClient(
sp.GetRequiredService<IHttpClientFactory>().CreateClient(), options))`** — i.e. the client is a
**singleton** over the **default (unnamed)** `IHttpClientFactory` client, and `options` is captured **once at
registration time** (later `IOptions` changes are not observed).

### 2.2 Operations table

Controller property is on the client (`client.Subscriptions` etc.). Return types nest their payload; the
"unwrap" column is what `MaxioBillingClient` reads. Error case A = typed `SdkException<{Op}Error>`;
case B = `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` (no `TryGet…`).

| # | Controller · method (signature verbatim, params in order) | Request model + fields | Response envelope → unwrap | Error case | Map page |
|---|---|---|---|---|---|
| 4 | `client.Products.ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — first 8 params are nullable **with no default → must pass explicitly** (`null` to skip) | — | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product (product): Product !req` | **B** `RawError` | `operations/Products.md` |
| 4 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` is a **string**; accepts the id or `"handle:eshop-subscribe"` | — | `IReadOnlyList<ProductResponse>` → `.Product` | **A** `ListProductsForProductFamilyError`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | `operations/ProductFamilies.md` |
| 5 | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **B** | `operations/Products.md` |
| 5 | `client.Products.ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **B** | `operations/Products.md` |
| 6 | `client.ProductFamilies.ReadProductFamily(int id, CancellationToken ct = default)` — **`int` only**; the endpoint doc mentions `handle:my-family` but the C# param is `int`, so handle lookup is NOT reachable here (use `ListProductFamilies` + match `Handle`) | — | `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` — **nullable** | **B** | `operations/ProductFamilies.md` |
| 6 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must be passed explicitly; **no paging params** | — | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` | **B** | `operations/ProductFamilies.md` |
| 7 | `client.Components.ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — note `productFamilyId` is **`int`** here (unlike the Products variant) and the date params are **`string?`** | `ListComponentsFilter`: `Ids (ids): IReadOnlyList<int>?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` | `IReadOnlyList<ComponentResponse>`; `ComponentResponse.Component (component): Component !req` | **B** | `operations/Components.md` |
| 7 | `client.Components.FindComponent(string handle, CancellationToken ct = default)` — lookup by handle across the site (`api-call`) | — | `ComponentResponse` → `.Component` | **B** | `operations/Components.md` |
| 7 | `client.Components.ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` may be `"handle:api-call"` (prefix required) | — | `ComponentResponse` → `.Component` | **B** | `operations/Components.md` |
| 8 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, plus optional `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason`, `ParentId (parent_id): int?`, `SalesforceId` | `CustomerResponse.Customer (customer): Customer !req` | **A** `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)`. `CustomerErrorResponse1`: `Errors (errors): Errors?` (union) | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 9 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference`; **`reference` is non-nullable `string`** | — | `CustomerResponse` → `.Customer !req` | **B** `RawError` — **not found does NOT return null; it throws.** Catch `SdkException<RawError>` and branch on `ex.Error.StatusCode` (see §2.6 / B2) | `operations/Customers.md` |
| 10 | `client.Customers.ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` → `.Customer` | **B** | `operations/Customers.md` |
| 11 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` — **all fields optional**; the ones you need: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `Reference (reference): string?`, `CouponCode`, `CouponCodes`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `NextBillingAt/InitialBillingAt: DateTimeOffset?`, `ProductChangeDelayed (product_change_delayed): bool?`, `OfferId (offer_id): OfferId?` **(union)**. `CustomerAttributes`: `FirstName/LastName/Email/Reference/…` all `string?` | `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable, unlike CustomerResponse** | **A** `CreateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| 12 | `client.Subscriptions.ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **14 leading params are nullable with NO default → all must be supplied positionally unless you use named args.** There is **no customer filter param** on this op | — | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` | **B** | `operations/Subscriptions.md` |
| 12 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — **this is the by-customer filter**; no paging | — | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` | **B** | `operations/Customers.md` |
| 13 | `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed (`null` to skip) | — | `SubscriptionResponse` → `.Subscription?` | **B** — unknown id ⇒ throws `SdkException<RawError>`; read `ex.Error.StatusCode` | `operations/Subscriptions.md` |
| 13 | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — lookup by subscription `reference` | — | `SubscriptionResponse` | **A** `FindSubscriptionError`: `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError(out RawError)` | `operations/Subscriptions.md` |
| 15 | `client.SubscriptionComponents.CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — **handle addressing IS supported**: `componentId` is a union; pass `ComponentIdModel.String("handle:api-call")` (the `handle:` prefix is required by the endpoint) or `ComponentIdModel.Int(id)`. Subscription may be `SubscriptionIdOrReference.Int(id)` or `.String(reference)` | `CreateUsageRequest`: `Usage (usage): CreateUsage !req`. `CreateUsage`: `Quantity (quantity): double?` (**`double?`**, negative allowed to deduct), `PricePointId (price_point_id): string?`, `Memo (memo): string?`, `BillingSchedule (billing_schedule): BillingSchedule?`, `CustomPrice (custom_price): ComponentCustomPrice?` | `UsageResponse.Usage (usage): Usage !req`. `Usage`: `Id (id): long?`, `Quantity (quantity): Quantity1?` **(union int\|string)**, `Memo`, `ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`, `SubscriptionId`, `PricePointId`, `OverageQuantity`, `CreatedAt` | **A** `CreateUsageError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/SubscriptionComponents.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md`, `unions.md` |
| 16 | `client.SubscriptionComponents.ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 middle params must be passed explicitly | — | `IReadOnlyList<UsageResponse>` → `.Usage` (sum `Quantity` yourself; it is a union) | **B** | `operations/SubscriptionComponents.md` |
| 16 | `client.SubscriptionComponents.ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — **`componentId` is `int` here, no handle addressing** | — | `SubscriptionComponentResponse.Component (component): SubscriptionComponent?` (**note: the wire key is `component`, not `subscription_component`**) | **A** `ReadSubscriptionComponentError`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | `operations/SubscriptionComponents.md`, `records-3-Of-Su.md` |
| 16 | `client.SubscriptionComponents.ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 nullable params, no defaults; pass `include: [ListSubscriptionComponentsInclude.HistoricUsages]` to get prior periods | — | `IReadOnlyList<SubscriptionComponentResponse>` → `.Component` | **B** | `operations/SubscriptionComponents.md` |
| 17 | `client.SubscriptionProducts.PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — **the proration preview; no side effects** | `SubscriptionMigrationPreviewRequest`: `Migration (migration): SubscriptionMigrationPreviewOptions !req`. `SubscriptionMigrationPreviewOptions`: `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge (include_initial_charge): bool? = false`, `IncludeCoupons (include_coupons): bool? = true`, `PreservePeriod (preserve_period): bool? = false`, `Proration (proration): Proration?`, `ProrationDate (proration_date): DateTimeOffset?` | `SubscriptionMigrationPreviewResponse.Migration (migration): SubscriptionMigrationPreview !req`. **Money fields, all `long?` and all IN CENTS (name-suffixed `_in_cents`):** `ProratedAdjustmentInCents (prorated_adjustment_in_cents)`, `ChargeInCents (charge_in_cents)`, `PaymentDueInCents (payment_due_in_cents)`, `CreditAppliedInCents (credit_applied_in_cents)` | **A** `PreviewSubscriptionProductMigrationError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/SubscriptionProducts.md`, `records-4-Su-We.md` |
| 18a | **Apply now with proration:** `client.SubscriptionProducts.MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` | `SubscriptionProductMigrationRequest`: `Migration (migration): SubscriptionProductMigration !req`. `SubscriptionProductMigration`: same fields as the preview options **minus `ProrationDate`** — `ProductId`, `ProductHandle`, `ProductPricePointId`, `ProductPricePointHandle`, `IncludeTrial = false`, `IncludeInitialCharge = false`, `IncludeCoupons = true`, `PreservePeriod (preserve_period): bool? = false`, `Proration (proration): Proration?`. `Proration`: `PreservePeriod (preserve_period): bool?` | `SubscriptionResponse` → `.Subscription?` | **A** `MigrateSubscriptionProductError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionProducts.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` |
| 18b | **At next renewal, no proration:** `client.Subscriptions.UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` with `ProductChangeDelayed = true` | `UpdateSubscriptionRequest`: `Subscription (subscription): UpdateSubscription !req`. `UpdateSubscription` relevant fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, **`ProductChangeDelayed (product_change_delayed): bool?`**, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `NextProductId (next_product_id): string?` (**`string`** — per the endpoint doc, set it to `""` to **cancel** a scheduled delayed product change), `NextProductPricePointId (next_product_price_point_id): string?`, `NextBillingAt: DateTimeOffset?`, `Reference: string?` | `SubscriptionResponse` → `.Subscription?` | **A** `UpdateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/Subscriptions.md`, `records-4-Su-We.md` |
| 19 | `client.SubscriptionStatus.PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `POST /subscriptions/{id}/hold.json`; pass `body: null` for an indefinite hold | `PauseRequest`: `Hold (hold): AutoResume?`. `AutoResume`: `AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` | `SubscriptionResponse` → `.Subscription?` (state becomes `on_hold`) | **A** `PauseSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`, `records-3-Of-Su.md`, `records-1-Ac-Cr.md` |
| 19b | `client.SubscriptionStatus.UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — change/clear the auto-resume date | same `PauseRequest` | `SubscriptionResponse` | **A** same accessors | `operations/SubscriptionStatus.md` |
| 20 | `client.SubscriptionStatus.ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — **no request body; the 2nd arg is a query param and must be passed explicitly (`null` for non-calendar-billing)** | — (enum `ResumptionCharge`: `Prorated`/`Immediate`/`Delayed`) | `SubscriptionResponse` | **A** `ResumeSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md` |
| 21 | **Cancel immediately:** `client.SubscriptionStatus.CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `DELETE /subscriptions/{id}.json`; omit schedule fields for an immediate cancel | `CancellationRequest`: `Subscription (subscription): CancellationOptions !req`. `CancellationOptions`: `CancellationMessage (cancellation_message): string?`, `ReasonCode (reason_code): string?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool?` | `SubscriptionResponse` → `.Subscription?` | **A** `CancelSubscriptionApiError` (**note the `Api` infix in the type name**): `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError(out RawError)` | `operations/SubscriptionStatus.md`, `records-1-Ac-Cr.md` |
| 22 | **Cancel at end of period:** `client.SubscriptionStatus.InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `POST …/delayed_cancel.json` | same `CancellationRequest` / `CancellationOptions` (use `CancellationMessage`, `ReasonCode`) | **`DelayedCancellationResponse`: `Message (message): string?` — that is the WHOLE model; it does NOT return the subscription.** Re-read via `ReadSubscription` to see `CancelAtEndOfPeriod`/`DelayedCancelAt` | **A** `InitiateDelayedCancellationError`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`, `records-2-Cr-Ne.md` |
| 22b | **Cancel the delayed cancellation:** `client.SubscriptionStatus.CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` — `DELETE …/delayed_cancel.json`; idempotent per the endpoint doc | — | `DelayedCancellationResponse` (`Message` only) | **A** `CancelDelayedCancellationError`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | `operations/SubscriptionStatus.md` |
| 23 | `client.SubscriptionStatus.ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` | `ReactivateSubscriptionRequest` (**flat, no envelope**): `CalendarBilling (calendar_billing): ReactivationBilling?`, `IncludeTrial (include_trial): bool?`, `PreserveBalance (preserve_balance): bool?`, `CouponCode (coupon_code): string?`, `UseCreditsAndPrepayments (use_credits_and_prepayments): bool?`, `Resume (resume): Resume?` **(union: `Resume.Bool(bool)` / `Resume.ResumeOptions(ResumeOptions)`)** | `SubscriptionResponse` → `.Subscription?` | **A** `ReactivateSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`, `records-3-Of-Su.md`, `unions.md` |
| 24 | `client.ProductFamilies.CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` | `CreateProductFamilyRequest`: `ProductFamily (product_family): CreateProductFamily !req`. `CreateProductFamily`: `Name (name): string !req`, `Handle (handle): string?`, `Description (description): string?` | `ProductFamilyResponse.ProductFamily?` | **A** `CreateProductFamilyError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/ProductFamilies.md`, `records-1-Ac-Cr.md` |
| 25 | `client.Products.CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `productFamilyId` is a **string** (id or `"handle:eshop-subscribe"`) | `CreateOrUpdateProductRequest`: `Product (product): CreateOrUpdateProduct !req`. `CreateOrUpdateProduct` — **required**: `Name (name): string !req`, `Description (description): string !req`, **`PriceInCents (price_in_cents): long !req`**, `Interval (interval): int !req`, `IntervalUnit (interval_unit): IntervalUnit !req`. Optional: `Handle (handle): string?`, `AccountingCode`, **`RequireCreditCard (require_credit_card): bool?`**, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval`, `TrialIntervalUnit`, `TrialType`, `ExpirationInterval`, `ExpirationIntervalUnit`, `AutoCreateSignupPage`, `TaxCode`. **THERE IS NO `taxable` FIELD — see Blocker B3.** | `ProductResponse.Product !req` | **A** `CreateProductError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/Products.md`, `records-1-Ac-Cr.md` |
| 26 | `client.Components.CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — **the body type is `CreateMeteredComponent` (the envelope), which is a different type from the inner `MeteredComponent`** | `CreateMeteredComponent`: `MeteredComponent (metered_component): MeteredComponent !req`. `MeteredComponent` — **required**: `Name (name): string !req`, `UnitName (unit_name): string !req`, `PricingScheme (pricing_scheme): PricingScheme !req`. Optional: `Handle (handle): string?`, `Description`, `Taxable (taxable): bool?`, `UnitPrice (unit_price): UnitPrice1?` **(union `string`\|`double` → `UnitPrice1.Double(0.01)` or `UnitPrice1.String("0.01")`)**, `Prices (prices): IReadOnlyList<Price>?`, `PricePoints`, `TaxCode`, `HideDateRangeOnInvoice`, `DisplayOnHostedPage`, `AllowFractionalQuantities`, `PublicSignupPageIds`, `Interval`, `IntervalUnit`. `Price`: `StartingQuantity (starting_quantity): StartingQuantity !req` (union int\|string), `EndingQuantity (ending_quantity): EndingQuantity?` (union), `UnitPrice (unit_price): UnitPrice !req` (union `double`\|`string`) | `ComponentResponse.Component !req` | **A** `CreateMeteredComponentError`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/Components.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `unions.md` |
| 27 | `client.Products.ArchiveProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **A** `ArchiveProductError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/Products.md` |
| 27 | `client.Components.ArchiveComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` may be `"handle:api-call"` | — | **`Component` — returned BARE, not `ComponentResponse`.** Do **not** write `.Component` on this one. See trap T8 | **A** `ArchiveComponentError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/Components.md` |

### 2.3 Fields to read off the response models (capabilities 4, 14, 16)

**`Product`** (`records-3-Of-Su.md`, `Models/Product.cs`) — the catalog fields you asked for:
`Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `Description (description): string?` ·
**`PriceInCents (price_in_cents): long?` — CENTS** (so `eshop-pro` $299/mo ⇒ `29900`; `basic-plan` $29 ⇒ `2900`) ·
`Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` ·
`ProductFamily (product_family): ProductFamily?` · **`ArchivedAt (archived_at): DateTimeOffset?`** ·
also `Taxable (taxable): bool?`, `RequireCreditCard`, `RequestCreditCard`, `TrialPriceInCents (long?)`,
`TrialInterval`, `TrialIntervalUnit`, `InitialChargeInCents (long?)`, `DefaultProductPricePointId`,
`ProductPricePointHandle`, `VersionNumber`, `CreatedAt`, `UpdatedAt`.

**`ProductFamily`**: `Id`, `Name`, `Handle`, `Description`, `AccountingCode`, `CreatedAt`, `UpdatedAt`, `ArchivedAt` — all nullable.

**`Component`** (`records-1-Ac-Cr.md`) — for validating `api-call` is metered:
`Id (id): int?` · `Name` · `Handle (handle): string?` · **`Kind (kind): ComponentKind?`** ·
`PricingScheme (pricing_scheme): PricingScheme?` · `UnitName (unit_name): string?` ·
**`UnitPrice (unit_price): string?`** and **`PricePerUnitInCents (price_per_unit_in_cents): long?`** ·
`Prices (prices): IReadOnlyList<ComponentPrice?>?` (`ComponentPrice.UnitPrice: string?`,
`FormattedUnitPrice: string?`, `StartingQuantity/EndingQuantity: int?`) ·
`ProductFamilyId/Name/Handle` · `Archived (archived): bool?` · `ArchivedAt` · `Taxable` · `Recurring` ·
`DefaultPricePointId` · `AllowFractionalQuantities`.
**Magnitude:** the model carries *both* a bare `unit_price` (`string`) and an explicit
`price_per_unit_in_cents` (`long`) for the same concept — read money from `PricePerUnitInCents` (cents,
$0.01 ⇒ `1`) and treat `UnitPrice` as the decimal-currency string (`"0.01"`). Prefer the `_in_cents` field
for arithmetic; the `unit_price` string's exact formatting is `UNVERIFIED` (only live traffic can confirm) —
**parse it defensively with `decimal.TryParse(..., CultureInfo.InvariantCulture, out _)` and fall back to
`PricePerUnitInCents / 100m`.**

**`Subscription`** (`records-3-Of-Su.md`) — the fields you listed, all present:
`Id (id): int?` · **`State (state): SubscriptionState?`** · `Product (product): Product?` (⇒ nested
`Product.Id/Handle/Name/PriceInCents`) · `Customer (customer): Customer?` ·
`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ·
`NextAssessmentAt (next_assessment_at): DateTimeOffset?` ·
`ActivatedAt (activated_at): DateTimeOffset?` · `CanceledAt (canceled_at): DateTimeOffset?` ·
**`DelayedCancelAt (delayed_cancel_at): DateTimeOffset?`** · `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?` ·
`CancellationMessage`, `CancellationMethod (CancellationMethod enum)`, `ScheduledCancellationAt` ·
`CurrentPeriodStartedAt` · `PreviousState (SubscriptionState?)` · `OnHoldAt (on_hold_at): DateTimeOffset?` ·
`AutomaticallyResumeAt` · `Reference (reference): string?` · `ProductPriceInCents (long?)` ·
`CurrentBillingAmountInCents (long?)` · `BalanceInCents (long?)` · `NextProductId (int?)` ·
`NextProductHandle (string?)` · `NextProductPricePointId` · `TrialStartedAt/TrialEndedAt` · `ExpiresAt` ·
`CreatedAt/UpdatedAt` · `Currency` · `SelfServicePageToken`.

**`Customer`**: `Id (id): int?`, `FirstName`, `LastName`, `Email`, `Reference (reference): string?`,
`Organization`, `CreatedAt`, `UpdatedAt`, `Locale`, `TaxExempt`, `ParentId`, `Maxioid`.

**`SubscriptionComponent`** (period-to-date usage, capability 16):
`Id (id): int?` · `ComponentId (component_id): int?` · `ComponentHandle (component_handle): string?` ·
`Name` · **`Kind (kind): ComponentKind?`** · `UnitName` · **`UnitBalance (unit_balance): int?` — this is
the running period-to-date metered total, a UNIT COUNT (not money)** · `PricingScheme` ·
`AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (union int\|string) · `Enabled` ·
`SubscriptionId` · `PricePointId/Handle/Name/Type` · `ProductFamilyId/Handle` · `Currency` ·
`HistoricUsages (historic_usages): IReadOnlyList<HistoricUsage>?` · `ArchivedAt` · `Interval/IntervalUnit`.
`HistoricUsage`: `TotalUsageQuantity (total_usage_quantity): double?`,
`BillingPeriodStartsAt (billing_period_starts_at): DateTimeOffset?`, `BillingPeriodEndsAt`.
The `CreateUsage` endpoint doc states plainly that "the `quantity` from usage for each component is
accumulated to the `unit_balance`", and that `unit_balance` has a floor of `0`.
**Directive:** read period-to-date total from `ReadSubscriptionComponent(...).Component?.UnitBalance`;
if it is null, fall back to summing `ListUsages` (`Usage.Quantity` is a union — use
`q.TryGetInt(out var i)` then `q.TryGetString(out var s)` + `int.TryParse(s, InvariantCulture, …)`).

### 2.4 Enums actually needed (`map/models/enums.md`) — all `StringEnum<T>`, namespace `MaxioAdvancedBilling.Models.Enums`

| Enum | C# member (wire value) |
|---|---|
| `SubscriptionState` (read off `Subscription.State`) | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| **`SubscriptionStateFilter`** (the `state:` param of `ListSubscriptions` — a **different, smaller** type) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `ComponentKind` | **`MeteredComponent (metered_component)`**, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, **`PerUnit (per_unit)`**, `Tiered (tiered)` |
| `IntervalUnit` | `Day (day)`, **`Month (month)`** — *(only these two; there is no `year`)* |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `SubscriptionSort` | `SignupDate`, `PeriodStart`, `PeriodEnd`, `NextAssessment`, `UpdatedAt`, `CreatedAt`, `TotalPayments`, `Id`, `OpenBalance`, `ExpiresAt` |
| `SubscriptionDateField` (list filter) | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SubscriptionInclude` (ReadSubscription) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` (ListSubscriptions) | `SelfServicePageToken (self_service_page_token)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ListSubscriptionComponentsSort` | `Id (id)`, `UpdatedAt (updated_at)` |
| `SubscriptionListDateField` | `UpdatedAt (updated_at)` *(only value)* |
| `IncludeNotNull` | `NotNull (not_null)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ResumptionCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `CreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `ExpirationIntervalUnit`, `TrialType`, `CollectionMethod`, `PricePointType`, `CancellationMethod` | referenced by fields above; pull values from `enums.md` only if you set/branch on them |

**"Paused" vs "on hold":** the SDK has *both* `SubscriptionState.Paused (paused)` and
`SubscriptionState.OnHold (on_hold)`, but the pause operation is `PauseSubscription` →
`POST /subscriptions/{id}/hold.json` and the filter enum only offers `OnHold`. Map your domain "paused"
to **`on_hold`** and treat `paused` as a defensive extra case.

### 2.5 Unions used (`map/models/unions.md`, namespace `MaxioAdvancedBilling.Models.AnyOf`)

| Union | Variants | Factories | Readers |
|---|---|---|---|
| `SubscriptionIdOrReference` | `int`, `string` | `SubscriptionIdOrReference.Int(int)`, `.String(string)` | `TryGetInt(out …)`, `TryGetString(out …)` |
| `ComponentIdModel` | `int`, `string` | `ComponentIdModel.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` |
| `Quantity1` (on `Usage.Quantity`) | `int`, `string` | `Quantity1.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` |
| `AllocatedQuantity2` | `int`, `string` | `.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` |
| `UnitPrice1` (on `MeteredComponent.UnitPrice`) | `string`, `double` | `UnitPrice1.String(string)`, `.Double(double)` | `TryGetString`, `TryGetDouble` |
| `UnitPrice` (on `Price.UnitPrice`) | `double`, `string` | `UnitPrice.Double(double)`, `.String(string)` | `TryGetDouble`, `TryGetString` |
| `StartingQuantity` / `EndingQuantity` | `int`, `string` | `.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` |
| `Resume` (on `ReactivateSubscriptionRequest.Resume`) | `bool`, `ResumeOptions` | `Resume.Bool(bool)`, `Resume.ResumeOptions(ResumeOptions)` | `TryGetBool`, `TryGetResumeOptions` |

Unions have **no** object-initializer and **no** `new` — always the static factory; AnyOf-over-primitives
also supports implicit conversion.

### 2.6 Errors (capabilities 28–29)

Types and namespaces (all three usings are needed for a Case-A catch):
`MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` (`public required TError Error { get; init; }`) ·
`MaxioAdvancedBilling.Core.ErrorResponse.ApiError` / `.RawError` ·
`MaxioAdvancedBilling.Errors.{Operation}Error`.

`RawError` public members: `StatusCode: System.Net.HttpStatusCode`, `ReadAsString(): string`,
`ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
`ApiError` (base of every typed error) exposes only `TryGetRawError(out RawError): bool`.

**No `{Operation}Result` / `ApiResult` no-throw variants exist anywhere in this SDK** — every operation is
throw-only. Ignore the result-style sections of the companion skills.

**Which of your operations is which — the exact split (from each operation row):**

| Case **B** — catch `SdkException<RawError>` (NO `TryGet…`; read `ex.Error.StatusCode` / `ReadAsString()`) | Case **A** — catch `SdkException<{Op}Error>` |
|---|---|
| `Products.ListProducts`, `Products.ReadProduct`, `Products.ReadProductByHandle`, `ProductFamilies.ListProductFamilies`, `ProductFamilies.ReadProductFamily`, `Components.ListComponents`, `Components.ListComponentsForProductFamily`, `Components.ReadComponent`, `Components.FindComponent`, `Customers.ReadCustomer`, `Customers.ReadCustomerByReference`, `Customers.ListCustomers`, `Customers.ListCustomerSubscriptions`, `Customers.DeleteCustomer`, `Subscriptions.ListSubscriptions`, `Subscriptions.ReadSubscription`, `Subscriptions.PreviewSubscription`, `SubscriptionComponents.ListUsages`, `SubscriptionComponents.ListSubscriptionComponents` | `Customers.CreateCustomer` (`CreateCustomerError`), `Customers.UpdateCustomer` (`UpdateCustomerError`), `Subscriptions.CreateSubscription` (`CreateSubscriptionError`), `Subscriptions.UpdateSubscription` (`UpdateSubscriptionError`), `Subscriptions.FindSubscription` (`FindSubscriptionError`), `ProductFamilies.ListProductsForProductFamily` (`ListProductsForProductFamilyError`), `ProductFamilies.CreateProductFamily`, `Products.CreateProduct`, `Products.UpdateProduct`, `Products.ArchiveProduct`, `Components.CreateMeteredComponent`, `Components.ArchiveComponent`, `Components.UpdateComponent`, `SubscriptionComponents.CreateUsage` (`CreateUsageError`), `SubscriptionComponents.ReadSubscriptionComponent` (`ReadSubscriptionComponentError`), `SubscriptionProducts.MigrateSubscriptionProduct`, `SubscriptionProducts.PreviewSubscriptionProductMigration`, `SubscriptionStatus.*` (all 10: `PauseSubscriptionError`, `ResumeSubscriptionError`, `CancelSubscriptionApiError`, `InitiateDelayedCancellationError`, `CancelDelayedCancellationError`, `ReactivateSubscriptionError`, …) |

Case-A accessor lists are in the operations table above; **write one `if`/`else if` per accessor and put
`TryGetRawError` LAST** — it is not a catch-all (a status with a typed accessor leaves `TryGetRawError`
false).

**Reading the HTTP status:**
* Case B: `ex.Error.StatusCode`.
* Case A: only via a `RawError` obtained from an accessor —
  `if (ex.Error.TryGetRawError(out var raw)) status = raw.StatusCode;` (and the status-specific
  `TryGetNoContent(out RawError)` accessors likewise yield a `RawError` with a `StatusCode`). **There is no
  status property on `SdkException<T>` itself.**

**Reading the provider's message.** The 422 payload type is almost always
`MaxioAdvancedBilling.Models.ErrorListResponse1` (`TryGetErrorListResponse1`); customer ops use
`CustomerErrorResponse1` (`Errors (errors): Errors?` — a union). **Directive (defensive):** in each Case-A
catch, try the typed accessor first and extract the message list best-effort; on any failure fall through to
`TryGetRawError(out var raw)` and use `raw.ReadAsString()`; if even that is empty, use a generic
"billing provider rejected the request" message. Do **not** call `raw.ReadAsJson<T>()` blindly — it throws
`JsonException` on a non-JSON body.

**`ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req`** — a plain required string array, so
`{"errors": ["..."]}` is the correct shape for every op that uses it.
**`CustomerErrorResponse1.Errors (errors): Errors?` is a DIFFERENT shape** — `MaxioAdvancedBilling.Models.Errors`
is a **record (not a union)** with exactly `PerPage (per_page): IReadOnlyList<string>?` and
`PricePoint (price_point): IReadOnlyList<string>?`, i.e. a JSON **object**. An `errors` **array** does not
deserialise into it. See B10 — the typed-accessor-then-`TryGetRawError` chain is **not** sufficient on its
own for customer ops.

**Capability 29 — how 404 manifests:**

| Scenario | Manifestation |
|---|---|
| Unknown **subscription id** (`ReadSubscription`) | Case **B**: throws `SdkException<RawError>`; branch on `ex.Error.StatusCode == HttpStatusCode.NotFound`. The SDK models no 404 shape here. |
| Unknown **subscription reference** (`FindSubscription`) | Case **A** `FindSubscriptionError` — the SDK explicitly models 404: `TryGetNoContent(out RawError)` returns true, and `TryGetRawError` is then **false**. |
| Unknown **customer reference** (`ReadCustomerByReference`) | Case **B**: **throws, does not return null**. Catch `SdkException<RawError>` and map `StatusCode == NotFound` → your `null`/`Option` "no customer". The SDK models no statuses for this op, so the exact code Maxio returns for a miss is `UNVERIFIED` — **directive: treat `NotFound` as "absent" and, defensively, also treat a `2xx`-with-null `CustomerResponse.Customer` and any `4xx` other than 401/403 as "absent" only after logging `ex.Error.ReadAsString()`; never swallow 401/403/5xx.** |
| Unknown **product handle** (`ReadProductByHandle`) | Case **B**: throws `SdkException<RawError>`; check `StatusCode`. |
| Unknown **product family** in `ListProductsForProductFamily` | Case **A**: `TryGetString(out string)` at **[404]** — the 404 body is a bare string. |
| Unknown component on a subscription (`ReadSubscriptionComponent`) | Case **A**: `TryGetNoContent(out RawError)` [404]. |

Also guard transport failures at the `MaxioBillingClient` boundary — `HttpRequestException` /
`TaskCanceledException` are **not** `SdkException<T>` and will escape an SDK-only catch. Convert both plus
`SdkException<…>` into one provider exception type on `IBillingClient`.

### 2.7 Testing seam + exact stub JSON envelopes (capability 30)

**Seam:** the `HttpClient` constructor argument. No SDK mocking helpers exist.

```csharp
var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
{ Content = new StringContent(json, Encoding.UTF8, "application/json") });
var client = new MaxioAdvancedBillingClient(
    new HttpClient(handler),
    new MaxioAdvancedBillingClientOptions()); // auth not needed for stubs
```
(`StubHandler` = your own `HttpMessageHandler` subclass overriding `SendAsync`, capturing `LastRequest`.)

**Under DI:** `AddMaxioAdvancedBillingClient` resolves the **default (unnamed)** factory client, so stub it
with `services.AddHttpClient(Options.DefaultName).ConfigurePrimaryHttpMessageHandler(() => stubHandler);`
then `sp.GetRequiredService<MaxioAdvancedBillingClient>()`.

**Envelope shapes your stub payloads must produce** (derived from each response record's single member and
its wire name — these are the literal `[JsonPropertyName]` values):

| Operation | Stub JSON |
|---|---|
| `CreateSubscription`, `ReadSubscription`, `UpdateSubscription`, `Migrate…`, `Pause/Resume/Cancel/Reactivate` | `{"subscription": { "id": 1, "state": "active", "current_period_ends_at": "2026-08-01T00:00:00Z", "product": { "id": 10, "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }, "customer": { "id": 5, "reference": "buyer@example.com" } }}` |
| `ListSubscriptions`, `ListCustomerSubscriptions` | `[ {"subscription": { … }}, … ]` — an **array of envelopes**, not `{"subscriptions": […]}` |
| `CreateCustomer`, `ReadCustomer`, `ReadCustomerByReference` | `{"customer": { "id": 5, "first_name": "A", "last_name": "B", "email": "buyer@example.com", "reference": "buyer@example.com" }}` |
| `ListCustomers` | `[ {"customer": { … }}, … ]` |
| `ReadProduct`, `ReadProductByHandle`, `CreateProduct`, `ArchiveProduct` | `{"product": { "id": 10, "name": "Pro", "handle": "eshop-pro", "description": "…", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null, "product_family": { "id": 2, "handle": "eshop-subscribe" } }}` |
| `ListProducts`, `ListProductsForProductFamily` | `[ {"product": { … }}, … ]` |
| `CreateProductFamily`, `ReadProductFamily` | `{"product_family": { "id": 2, "name": "eShop Subscribe", "handle": "eshop-subscribe" }}` |
| `ListProductFamilies` | `[ {"product_family": { … }}, … ]` |
| `CreateMeteredComponent`, `ReadComponent`, `FindComponent` | `{"component": { "id": 77, "name": "API Calls", "handle": "api-call", "kind": "metered_component", "pricing_scheme": "per_unit", "unit_name": "call", "unit_price": "0.01", "price_per_unit_in_cents": 1, "product_family_id": 2 }}` |
| `ListComponents…` | `[ {"component": { … }}, … ]` |
| **`ArchiveComponent`** | **bare `{ "id": 77, "handle": "api-call", "archived": true, … }` — NO `component` wrapper**, because the generated return type is `Component`. See T8/B4. |
| `CreateUsage` | `{"usage": { "id": 900, "quantity": 25, "memo": "…", "component_id": 77, "component_handle": "api-call", "subscription_id": 1 }}` |
| `ListUsages` | `[ {"usage": { … }}, … ]` |
| `ReadSubscriptionComponent`, `ListSubscriptionComponents` | `{"component": { "id": 88, "component_id": 77, "component_handle": "api-call", "kind": "metered_component", "unit_balance": 25, "pricing_scheme": "per_unit", "subscription_id": 1 }}` (list = array of these) — **key is `component`, not `subscription_component`** |
| `PreviewSubscriptionProductMigration` | `{"migration": { "prorated_adjustment_in_cents": -1500, "charge_in_cents": 29900, "payment_due_in_cents": 28400, "credit_applied_in_cents": 1500 }}` |
| `InitiateDelayedCancellation`, `CancelDelayedCancellation` | `{"message": "…"}` |
| Error stubs, Case A 422 | `{"errors": ["Product handle: is invalid."]}` with status 422 → assert `Assert.ThrowsAsync<SdkException<CreateSubscriptionError>>` and `ex.Error.TryGetErrorListResponse1(out var e)` |
| Error stubs, Case B 404 | any body, status 404 → assert `SdkException<RawError>` and `ex.Error.StatusCode` |

Assert the **outgoing** request too (`handler.LastRequest`): verb, path, and that query params appear in
snake_case (`per_page=20`, `include_archived=true`). **Note:** a stubbed `503` on a `GET/PUT` will be
retried 3× before surfacing — either allow for it or set `options.Retry = RetryOptions.Default() with { MaxRetries = 0 }`
in tests.

---

## 3. Trap notes (attach to the step named)

- **T1 (step 1 — auth):** `BasicAuthCredentials.Username`/`.Password` are both `required` — you must set
  both in the object initializer or it won't compile. Username = API key, Password = `"x"`. Load the key
  from configuration/user-secrets, never hardcode.
- **T2 (step 1 — HttpClient lifetime):** the SDK does **not** own the `HttpClient`. Register it once via
  `IHttpClientFactory` / `AddMaxioAdvancedBillingClient` and reuse; never `new HttpClient()` per request.
  `MaxioAdvancedBillingClient` is itself long-lived — the DI extension registers it **singleton**, and it
  captures your configured `options` **once at registration**, so `Maxio:BaseUrl` changes at runtime are
  not picked up.
- **T3 (step 1 — retries):** default `RetryOptions.HttpMethodsToRetry` = `GET, HEAD, PUT, OPTIONS` only.
  `CreateSubscription`/`CreateUsage`/`CreateCustomer` (POST) and `CancelSubscription` (DELETE) are **not**
  retried. `RetryOptions.Timeout` (default 100s) is **per attempt**, not total — bound a whole call with a
  `CancellationToken`. All `RetryOptions` members are `required`: start from `RetryOptions.Default() with { … }`.
- **T4 (steps 2, 5, 6 — list ops):** `ListSubscriptions` (14), `ListProducts` (8),
  `ListComponentsForProductFamily` (7), `ListSubscriptionComponents` (12), `ListUsages` (4) all carry many
  nullable params **with no C# default**, in a fixed order. **Call every list/search op with named
  arguments** (`state:`, `include:`, `page:`, `perPage:`, `ct:`) — a positional call silently mis-binds or
  fails to compile. Copy names from the signature, not from wire names.
- **T5 (all steps — `ct`):** the token parameter is literally `ct`. `cancellationToken: ct` will not compile.
- **T6 (steps 3, 4, 9 — envelopes on writes):** every write body nests its payload one level:
  `new CreateSubscriptionRequest { Subscription = new CreateSubscription { … } }`. The exceptions are
  `ReactivateSubscriptionRequest` and `PauseRequest`-style bodies, which are flat/differently shaped —
  check the table. Also every `body` parameter is declared `Type? body` **with no default**, so pass it
  explicitly (even `body: null`).
- **T7 (all — enums):** `SubscriptionState` etc. are `StringEnum<T>` **records**, not C# enums. Compare with
  `==` (value equality), read the wire string with `.Value`, build unknown values with `FromValue("…")`,
  and guard server-sent values with `TryGetKnownValue`/`IsKnownValue` so a new Maxio state doesn't crash you.
  Don't `switch` on them as if they were C# enums with a compile-time-exhaustive set.
- **T8 (step 9 — archive):** `ArchiveComponent` returns a **bare `Component`**, while every other component
  op returns `ComponentResponse`. Writing `result.Component` there is a compile error; writing a
  `{"component":{…}}` stub for it will deserialize to an all-null `Component`. `ArchiveProduct`, by
  contrast, *does* return `ProductResponse`.
- **T9 (steps 4, 7, 8 — nullable payload):** `SubscriptionResponse.Subscription` and
  `ProductFamilyResponse.ProductFamily` are **nullable** (`?`), while `CustomerResponse.Customer`,
  `ProductResponse.Product`, `ComponentResponse.Component`, `UsageResponse.Usage` are `!req`. Null-check the
  first two before dereferencing.
- **T10 (step 8 — delayed cancel):** `InitiateDelayedCancellation` / `CancelDelayedCancellation` return only
  `DelayedCancellationResponse { Message }`. To report the resulting state you must re-`ReadSubscription`
  and read `CancelAtEndOfPeriod` / `DelayedCancelAt`.
- **T11 (step 6 — handles):** handle-based addressing needs the literal `handle:` prefix in the
  string (`"handle:api-call"`, `"handle:eshop-subscribe"`) for `CreateUsage`/`ListUsages`/`ReadComponent`/
  `ArchiveComponent`/`CreateProduct`/`ListProductsForProductFamily`. `Components.FindComponent(handle)` is
  the one that takes the **bare** handle (it's a query param).
- **T12 (step 1 — first run):** path/template params are substituted via `ToString()` and are not
  type-checked against the route; a successful call surfaces neither URL nor status. Attach a
  `DelegatingHandler` that logs `request.Method`/`RequestUri` on the first run of each new call and confirm
  the verb, that no `{site}`/`{…}` placeholder survives, and that your query params appear. Gate it behind a
  debug flag afterwards.
- **T13 (all — unions):** never `new` a union. `ComponentIdModel.String("handle:api-call")`,
  `SubscriptionIdOrReference.Int(id)`, `UnitPrice1.Double(0.01d)`; read back with `TryGet…`.
- **T14 (step 10 — namespaces):** C# does not import child namespaces transitively.
  `using MaxioAdvancedBilling.Models;` alone will **not** give you enums, unions, or error types. You will
  typically need: `MaxioAdvancedBilling`, `.Models`, `.Models.Enums`, `.Models.AnyOf`, `.Servers`,
  `.Core.Authentication.Basic`, `.Core.Configuration`, `.Core.Exceptions`, `.Core.ErrorResponse`, `.Errors`.

---

## 4. Assumptions & Blockers

**Assumptions (about your intent — correct me and I'll revise):**

- **A1.** Idempotency key = the eShopOnWeb user's email, written to `Customer.reference`; the "find or
  create" flow is `ReadCustomerByReference` → on `NotFound` → `CreateCustomer`. (The SDK also lets you skip
  the lookup entirely by passing `CustomerReference` or `CustomerAttributes` directly on
  `CreateSubscription`; I planned the explicit two-step because you asked for a lookup.)
- **A2.** Products are addressed by **handle** (`eshop-pro`, `basic-plan`) rather than numeric id
  throughout, since handles are what you configured.
- **A3.** "Plan change applied now with proration" ⇒ `MigrateSubscriptionProduct` (the `/migrations`
  endpoint, which is what `PreviewSubscriptionProductMigration` previews); "at next renewal without
  proration" ⇒ `UpdateSubscription` with `ProductChangeDelayed = true`, which the endpoint doc states
  explicitly applies "no proration". These are **two different controllers** — the preview only pairs with
  the migrate path.
- **A4.** Prices are held in cents in your domain (`price_in_cents` is `long` everywhere on products).
- **A5.** US region, so only `options.Server.Production.Us.*` is configured; if `Maxio:Region` says EU you
  must set both `options.Environment = ServerEnvironment.Eu` **and** `options.Server.Production.Eu.*`.
- **A6.** No payment-profile / credit-card capture is in scope (the SDK requires PCI handling or
  Chargify.js for that); sandbox products are assumed `require_credit_card = false`.

**Blockers / gaps — report these, do not work around them:**

- **B1 — package version discrepancy (source-visible).** The map's stamp says the source commit is tagged
  **`v1.0.2`**, but `MaxioAdvancedBilling.csproj` at that same ref declares `<Version>1.0.0</Version>`.
  Two generated artifacts in one clone disagree. Reference **`AsadAli.AdvancedBilling.Sdk` `1.0.2`** (the
  tag the map stamps and the ref this contract sheet was read from); if NuGet has no `1.0.2`, take the
  highest published version and re-ask me to re-ground — a different version means a different contract.
- **B2 — "not found" semantics are not modelled for the lookup you rely on.** `ReadCustomerByReference` is
  Case B: the SDK declares **no** 404 shape, so "customer does not exist" is indistinguishable at the type
  level from any other error. Which status Maxio returns for a reference miss is `UNVERIFIED` (only live
  traffic can confirm). Implement per the directive in §2.6 and log the raw body the first time you see it.
- **B3 — `taxable` is NOT settable when creating a product.** `CreateOrUpdateProduct` has **no** `taxable`
  field (it has `TaxCode` only), even though the read model `Product` exposes `Taxable`. Your UC0 seeding
  requirement "taxable=false" **cannot be expressed through this SDK**. Options: rely on the Maxio site
  default, or set it in the Maxio UI after seeding. There is no workaround in the generated surface.
  (`require_credit_card=false` **is** settable: `CreateOrUpdateProduct.RequireCreditCard`.)
- **B4 — generated inconsistency, trust it cautiously.** `Components.ArchiveComponent` is declared to return
  a bare `Component` while all 11 sibling component operations return `ComponentResponse`. Two generated
  definitions of the same resource disagree about the envelope, so one of them does not match the wire. If
  archiving ever deserializes to an all-null `Component`, that is this bug — fall back to re-reading via
  `ReadComponent` rather than trusting the archive return value. Which side is wrong is `UNVERIFIED`.
- **B5 — `ReadProductFamily` takes `int` only.** The endpoint documentation says the family may be given as
  `handle:my-family`, but the generated parameter is `int id`, so handle lookup is unreachable through this
  method. Resolve `eshop-subscribe` → id via `ListProductFamilies` and match on `Handle`, and cache it.
  (`ListProductsForProductFamily` and `CreateProduct` *do* take a `string productFamilyId` and accept
  `handle:` form.)
- **B6 — `ListSubscriptions` has no customer filter.** Its 14 filter params cover state, product, coupon,
  dates, metadata and sorting — **not** customer. Filter by customer with
  `Customers.ListCustomerSubscriptions(customerId, ct)`, which has **no paging parameters at all** (so a
  customer with very many subscriptions may be truncated server-side — unbounded page size is `UNVERIFIED`).
- **B7 — `ListProductFamilies` has no paging** either (5 date params only).
- **B8 — component unit-price magnitude.** `MeteredComponent.UnitPrice` (write side) is a
  `string|double` union with no `_in_cents` suffix, while the read model exposes both `unit_price` (string)
  and `price_per_unit_in_cents` (long). Seed `$0.01/unit` as `UnitPrice1.Double(0.01d)` with
  `PricingScheme = PricingScheme.PerUnit`, then **verify by reading the component back and asserting
  `PricePerUnitInCents == 1`** before trusting the seed. The write-side magnitude is `UNVERIFIED` by the
  SDK surface alone.
- **B10 — an undeserialisable error body escapes as a raw `JsonException`, NOT as `SdkException<TError>`.**
  Source-verified: `Core/ErrorResponse/ApiError.cs`'s `From<TBody,TSelf>` is
  `wrap(await parser.Map(response, ct))` — **no try/catch**. If the 4xx/5xx body doesn't match the declared
  payload type, `System.Text.Json.JsonException` propagates out of the error-construction path and the
  `SdkException<TError>` is **never constructed**, so `catch (SdkException<CreateCustomerError>)` does not
  match. There is **no SDK-level option** to make it fall back to `RawError`:
  `MaxioAdvancedBillingClientOptions` has exactly four properties (`Environment`, `Retry`, `Server`,
  `BasicAuth`) — no serializer or error-parsing knob. **Mitigation: every `MaxioBillingClient` call site must
  also catch `System.Text.Json.JsonException`** (alongside `SdkException<…>`, `HttpRequestException`,
  `TaskCanceledException`) and convert it to the provider exception — the HTTP status is unrecoverable at
  that point, so log "billing provider returned an unparseable error body". This risk is highest on
  `Customers.CreateCustomer`/`UpdateCustomer`, whose 422 payload `CustomerErrorResponse1.Errors` is the
  narrow `Errors` record (`per_page`/`price_point` only) while every sibling 422 model in the SDK is either
  `ErrorListResponse1` (string array) or a string/object map — two generated definitions of "the 422 body"
  that disagree. Whether the live Maxio wire actually sends the `per_page`/`price_point` object for a
  customer 422 is `UNVERIFIED`; if it sends an array, this operation's typed error is unusable in production
  and the `JsonException` catch is the only thing standing between you and an unhandled exception.
- **B9 — no `year` interval.** `IntervalUnit` has only `Day` and `Month`. Annual plans must be expressed as
  `Interval = 12, IntervalUnit = IntervalUnit.Month`. (Your $299/mo and $29/mo plans are `Interval = 1,
  IntervalUnit = IntervalUnit.Month`.)
