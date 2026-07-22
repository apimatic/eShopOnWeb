# Maxio Advanced Billing → eShopOnWeb integration plan (`MaxioBillingClient` : `IBillingClient`)

Grounded entirely in the bundled SDK map (`sdk-map.md`, `map/operations/*.md`, `map/models/*.md`) for SDK
`AsadAli.AdvancedBilling.Sdk` @ tag `v1.0.2` (source commit `15db14b`). Every row cites the map page it came
from. Nothing here is from memory.

---

## 1. Scope & sequence

| Step | Work | Operations used |
|---|---|---|
| S0 | Add NuGet `AsadAli.AdvancedBilling.Sdk`; add `using MaxioAdvancedBilling;` etc. (§3.1) | — |
| S1 | Infrastructure: `MaxioOptions` (ApiKey, Subdomain, Region, BaseUrl?), `MaxioBillingClient` ctor takes `MaxioAdvancedBillingClient` (SDK client) injected; DI registration via `AddMaxioAdvancedBillingClient` + `AddHttpClient(Options.DefaultName)` so tests can swap the primary handler | — |
| S2 | Error boundary: one private helper per error case → `BillingProviderException` (§6) | — |
| S3 | UC0 seed console: product family create/find | `ProductFamilies.CreateProductFamily`, `ProductFamilies.ListProductFamilies` |
| S4 | UC0: product create/find/read | `Products.CreateProduct`, `Products.ReadProductByHandle`, `ProductFamilies.ListProductsForProductFamily`, `Products.ArchiveProduct` |
| S5 | UC0: metered component create/find/validate-kind | `Components.CreateMeteredComponent`, `Components.FindComponent`, `Components.ReadComponent`, `Components.ListComponentsForProductFamily`, `Components.ArchiveComponent` |
| S6 | UC1: find-or-create customer by reference | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| S7 | UC1: subscribe (idempotent) | `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription`, `Subscriptions.ReadSubscription` |
| S8 | UC2: record usage + read period-to-date total | `SubscriptionComponents.CreateUsage`, `SubscriptionComponents.ReadSubscriptionComponent`, `SubscriptionComponents.ListUsages` |
| S9 | UC3: preview then commit plan change | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `SubscriptionProducts.MigrateSubscriptionProduct` (immediate + prorated), `Subscriptions.UpdateSubscription` (deferred, no proration) |
| S10 | UC4: lifecycle | `SubscriptionStatus.PauseSubscription`, `.ResumeSubscription`, `.CancelSubscription`, `.InitiateDelayedCancellation`, `.CancelDelayedCancellation`, `.ReactivateSubscription` |
| S11 | Tests: stub `HttpMessageHandler` → `new HttpClient(stub)` → `new MaxioAdvancedBillingClient(httpClient, options)` | — |

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
> `MaxioAdvancedBilling.Servers.ServerEnvironment`,
> `MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `ServerOptions` — see §3.3 for the one namespace
> the map does not pin). The map carries these namespaces — do not drop them to the root or `.Models`, or
> the implementer guesses the wrong `using` and the build breaks.

### 3.1 Package, namespaces, client construction

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (install by this; **`using` a different name**) | `sdk-map.md` |
| Version to add | tag/version the map was generated from: **`v1.0.2`** — pin `Version="1.0.2"` | `sdk-map.md` (stamp) |
| Root namespace | `MaxioAdvancedBilling` — client, options, `AddMaxioAdvancedBillingClient` | `sdk-map.md` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` (props: `Environment`, `Retry`, `Server`, `BasicAuth`) | `sdk-map.md` |
| Only constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — **this `HttpClient` argument is the unit-test seam** | `sdk-map.md`; `dotnet-testing` |
| DI extension | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`); it resolves the **default, unnamed** `IHttpClientFactory` client | `sdk-map.md`; `dotnet-client-initialization` |
| Controllers | `client.ProductFamilies`, `client.Products`, `client.Components`, `client.Customers`, `client.Subscriptions`, `client.SubscriptionStatus`, `client.SubscriptionProducts`, `client.SubscriptionComponents` — namespace of the controller classes: `MaxioAdvancedBilling.Api` | `sdk-map.md` (namespaces table) |

`using` directives you will need (C# does **not** import child namespaces transitively):

```csharp
using MaxioAdvancedBilling;                            // client, options, AddMaxioAdvancedBillingClient
using MaxioAdvancedBilling.Models;                     // all request/response records
using MaxioAdvancedBilling.Models.Enums;               // IntervalUnit, PricingScheme, ComponentKind, SubscriptionState…
using MaxioAdvancedBilling.Models.AnyOf;               // SubscriptionIdOrReference, ComponentIdModel, UnitPrice1, Quantity1…
using MaxioAdvancedBilling.Errors;                     // Case-A {Operation}Error types
using MaxioAdvancedBilling.Core.Exceptions;            // SdkException<TError>
using MaxioAdvancedBilling.Core.ErrorResponse;         // RawError, ApiError
using MaxioAdvancedBilling.Core.Authentication.Basic;  // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                    // ServerEnvironment
using MaxioAdvancedBilling.Core.Configuration;         // RetryOptions
```
Source: `sdk-map.md` (namespaces table + error-core table).

### 3.2 Auth (confirmed)

`options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }`
— HTTP Basic, **`Username` = the API key, `Password` = the literal `"x"`**; sends
`Authorization: Basic base64(key:x)`. It is the **only** auth property on the options class.
Source: `sdk-map.md` (Servers & auth); `dotnet-authentication`.

### 3.3 Base URL / environment / HttpClient seam (hard requirement)

The base URL is **not** taken from `HttpClient.BaseAddress` — the SDK composes it from `options.Environment`
+ `options.Server`. Configure it there.

| Knob | Exact expression | Effect | Source |
|---|---|---|---|
| Environment | `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu` | Selects which nested server options are read: `Us` → `https://{site}.chargify.com`; `Eu` → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Subdomain (derive path) | `options.Server.Production.Us.Site = "cp-exp-4";` (EU: `options.Server.Production.Eu.Site = …`) | Substitutes `{site}` into the template | `sdk-map.md` (override points) |
| Explicit override (wins) | `options.Server.Production.Us.BaseUrl = cfg["Maxio:BaseUrl"];` (set on the environment you selected) | A literal URL with no `{placeholders}` is used **as-is** — this is the mock/stub-host redirect point | `sdk-map.md`; `dotnet-configuration-resilience` |
| Events/Ebb group (not in scope) | `options.Server.Ebb.Us.BaseUrl` / `.Site` — only `SubscriptionComponents.RecordEvent` / `BulkRecordEvents` use it; **none of our operations do** | — | `sdk-map.md`; `operations/SubscriptionComponents.md` |
| HttpClient injection | `new MaxioAdvancedBillingClient(myHttpClient, options)` — pass your own `HttpClient` (stubbed `HttpMessageHandler` in tests). Under DI, register the stub on the **default unnamed** factory client: `services.AddHttpClient(Options.DefaultName).ConfigurePrimaryHttpMessageHandler(() => stub);` | — | `dotnet-testing`; `dotnet-client-initialization` |

Config precedence to implement: `Maxio:BaseUrl` present → set `…BaseUrl` (skip `Site`); else set `Site` from
subdomain and pick `ServerEnvironment.Us`/`.Eu` from region. **Set the knobs on the same environment you set
in `options.Environment`** — only that environment's nested options are read
(`dotnet-configuration-resilience`).

`ServerOptions` namespace: the map's options table names the type but its row gives the source path
`ServerOptions.cs` (repo root, not `Core/Configuration/`), so the namespace is most likely
`MaxioAdvancedBilling`. You never need to *name* the type (you only walk `options.Server.…`), so do not add a
`using` for it. **→ maxio-debug resolves from source if it surfaces** (only if you must declare a
`ServerOptions` variable). `RetryOptions` *is* pinned: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`
(`sdk-map.md`).

### 3.4 Operations — one row per in-scope operation

Legend: **A** = typed `SdkException<{Op}Error>` (accessors listed) · **B** = `SdkException<RawError>`.
"must-pass" = nullable parameter with **no** C# default → must be passed explicitly (pass `null` to skip).

#### UC0 — product families, products, components

| # | Call | Exact signature (verbatim) | Request model → fields | Response envelope → payload | Error case | Map page |
|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies.CreateProductFamily` | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` must-pass | `CreateProductFamilyRequest { ProductFamily (product_family): CreateProductFamily !req }`; `CreateProductFamily { Name (name): string !req, Handle (handle): string?, Description (description): string? }` | `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` **(nullable — null-check)** | **A** `CreateProductFamilyError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/ProductFamilies.md`; `records-1-Ac-Cr.md`; `records-3-Of-Su.md` |
| 2 | `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must-pass | — (query only; **no `page`/`perPage` at all**) | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` each | **B** `RawError` | `operations/ProductFamilies.md` |
| 2b | find family by handle | **No read-by-handle exists.** `ReadProductFamily(int id, …)` takes `int` only. Use #2 and match `ProductFamily.Handle` client-side | — | `ProductFamily { Id: int?, Name: string?, Handle: string?, AccountingCode, Description, CreatedAt, UpdatedAt, ArchivedAt (all nullable) }` | **B** (for `ReadProductFamily`) | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 3 | `client.Products.CreateProduct` | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `productFamilyId` is **string** (id or `"handle:my-family"`); `body` must-pass | `CreateOrUpdateProductRequest { Product (product): CreateOrUpdateProduct !req }`; `CreateOrUpdateProduct { Name (name): string !req, Handle (handle): string?, Description (description): string !req, AccountingCode: string?, RequireCreditCard (require_credit_card): bool?, PriceInCents (price_in_cents): long !req, Interval (interval): int !req, IntervalUnit (interval_unit): IntervalUnit !req, TrialPriceInCents: long?, TrialInterval: int?, TrialIntervalUnit: IntervalUnit?, TrialType: TrialType?, ExpirationInterval: int?, ExpirationIntervalUnit: ExpirationIntervalUnit?, AutoCreateSignupPage: bool?, TaxCode: string? }` | `ProductResponse.Product (product): Product !req` | **A** `CreateProductError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/Products.md`; `records-1-Ac-Cr.md`; `records-3-Of-Su.md` |
| 4 | `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse.Product` | **B** `RawError` (404 → `ex.Error.StatusCode == HttpStatusCode.NotFound`) | `operations/Products.md` |
| 5 | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 must-pass (`dateField`…`include`) | — | `IReadOnlyList<ProductResponse>` | **A** `ListProductsForProductFamilyError`: `TryGetString(out string)` [404] · `TryGetRawError` | `operations/ProductFamilies.md` |
| 5b | read product price/interval | `Product { Id: int?, Name: string?, Handle: string?, PriceInCents (price_in_cents): long?, Interval (interval): int?, IntervalUnit (interval_unit): IntervalUnit?, RequireCreditCard: bool?, Taxable: bool?, ExpirationInterval: int?, ExpirationIntervalUnit: ExpirationIntervalUnit?, TrialPriceInCents: long?, TrialInterval: int?, InitialChargeInCents: long?, ArchivedAt: DateTimeOffset?, ProductFamily: ProductFamily?, ProductPricePointId: int?, ProductPricePointHandle: string? }` | — | — | — | `records-3-Of-Su.md` |
| 6 | `client.Products.ArchiveProduct` | `ArchiveProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse.Product` | **A** `ArchiveProductError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/Products.md` |
| 7 | `client.Components.CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — `body` must-pass | `CreateMeteredComponent { MeteredComponent (metered_component): MeteredComponent !req }`; `MeteredComponent { Name (name): string !req, UnitName (unit_name): string !req, Description: string?, Handle (handle): string?, Taxable (taxable): bool?, PricingScheme (pricing_scheme): PricingScheme !req, Prices: IReadOnlyList<Price>?, PricePoints: IReadOnlyList<ComponentPricePointItem>?, UnitPrice (unit_price): UnitPrice1? (union string\|double), TaxCode: string?, HideDateRangeOnInvoice: bool?, DisplayOnHostedPage: bool?, AllowFractionalQuantities: bool?, PublicSignupPageIds: IReadOnlyList<int>?, Interval: int?, IntervalUnit: IntervalUnit? }` | `ComponentResponse.Component (component): Component !req` | **A** `CreateMeteredComponentError`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/Components.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| 8 | `client.Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` (`GET /components.lookup`, query `handle`) | — | `ComponentResponse.Component` | **B** `RawError` | `operations/Components.md` |
| 9 | `client.Components.ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` is **string**: numeric id **or** `"handle:my-handle"` | — | `ComponentResponse.Component` | **B** `RawError` | `operations/Components.md` |
| 10 | `client.Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 must-pass (`includeArchived`…`startDatetime`); note the date params here are **`string?`**, not `DateTimeOffset?` | — | `IReadOnlyList<ComponentResponse>` | **B** `RawError` | `operations/Components.md` |
| 10b | read component kind / price | `Component { Id: int?, Name: string?, Handle: string?, Kind (kind): ComponentKind?, PricingScheme (pricing_scheme): PricingScheme?, UnitName: string?, UnitPrice (unit_price): string?, PricePerUnitInCents (price_per_unit_in_cents): long?, ProductFamilyId: int?, ProductFamilyHandle: string?, Archived: bool?, Taxable: bool?, Recurring: bool?, DefaultPricePointId: int?, Prices: IReadOnlyList<ComponentPrice?>?, Interval: int?, IntervalUnit: IntervalUnit?, ArchivedAt: DateTimeOffset? }` — **validate metered with `component.Kind == ComponentKind.MeteredComponent`** | — | — | — | `records-1-Ac-Cr.md` |
| 11 | `client.Components.ArchiveComponent` | `ArchiveComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | — | **`Component` directly — NOT `ComponentResponse`** (asymmetric with every other component op) | **A** `ArchiveComponentError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/Components.md` |

#### UC1 — customer + subscription

| # | Call | Exact signature | Request model → fields | Response → payload | Error case | Map page |
|---|---|---|---|---|---|---|
| 12 | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` (`GET /customers/lookup.json?reference=`) | — | `CustomerResponse.Customer (customer): Customer !req` | **B** `RawError` — **"not found" = `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound`; any other status ⇒ real failure ⇒ rethrow as `BillingProviderException`** | `operations/Customers.md` |
| 13 | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must-pass | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer { FirstName (first_name): string !req, LastName (last_name): string !req, Email (email): string !req, Reference (reference): string?, Organization: string?, CcEmails: string?, Address/Address2/City/State/Zip/Country/Phone/Locale/VatNumber: string?, TaxExempt: bool?, ParentId: int? }` | `CustomerResponse.Customer` | **A** `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` — see §6 trap on `CustomerErrorResponse1` | `operations/Customers.md`; `records-1-Ac-Cr.md` |
| 13b | customer read-back | `Customer { Id (id): int?, FirstName, LastName, Email, Reference (reference): string?, Organization, CreatedAt/UpdatedAt: DateTimeOffset?, … }` (all nullable) | — | — | — | `records-2-Cr-Ne.md` |
| 14 | `client.Customers.ListCustomers` (optional search) | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 must-pass (`direction`…`q`); `q` is fuzzy search, **not** an exact reference lookup | — | `IReadOnlyList<CustomerResponse>` | **B** `RawError` | `operations/Customers.md` |
| 15 | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must-pass | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`; `CreateSubscription` fields we set: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `Reference (ref/reference)` → **note two distinct fields: `Reference (reference): string?` and `Ref (ref): string?`**, `CouponCode: string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `NextBillingAt: DateTimeOffset?`, `Components: IReadOnlyList<CreateSubscriptionComponent>?`, `ProductChangeDelayed: bool?`. **All fields optional** — identify product by `ProductHandle` **or** `ProductId`, customer by `CustomerId` **or** `CustomerReference` | `SubscriptionResponse.Subscription (subscription): Subscription?` **(nullable — null-check before use)** | **A** `CreateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md` |
| 16 | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — no paging params | — | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` each; filter `State == SubscriptionState.Active` (or `.Trialing`) to detect an existing subscription | **B** `RawError` | `operations/Customers.md` |
| 17 | `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must-pass** (pass `null`) | — | `SubscriptionResponse.Subscription` | **B** `RawError` | `operations/Subscriptions.md` |
| 18 | subscription read fields | `Subscription { Id (id): int?, State (state): SubscriptionState?, PreviousState: SubscriptionState?, CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?, CurrentPeriodStartedAt: DateTimeOffset?, NextAssessmentAt (next_assessment_at): DateTimeOffset?, ActivatedAt/CanceledAt/ExpiresAt/DelayedCancelAt/OnHoldAt/AutomaticallyResumeAt/ScheduledCancellationAt: DateTimeOffset?, CancelAtEndOfPeriod (cancel_at_end_of_period): bool?, CancellationMessage: string?, CancellationMethod: CancellationMethod?, ProductPriceInCents (product_price_in_cents): long?, CurrentBillingAmountInCents: long?, BalanceInCents: long?, Customer (customer): Customer?, Product (product): Product?, NextProductId: int?, NextProductHandle: string?, Reference: string?, ProductPricePointId: int?, Currency: string? }`. **Customer id → `sub.Customer?.Id`** (there is no flat `customer_id`); **product handle/name/price/interval → `sub.Product?.Handle / .Name / .PriceInCents / .Interval / .IntervalUnit`**; **period end → `CurrentPeriodEndsAt`; next billing/assessment → `NextAssessmentAt`** | — | — | — | `records-3-Of-Su.md` |

#### UC2 — metered usage

| # | Call | Exact signature | Request/params | Response → payload | Error case | Map page |
|---|---|---|---|---|---|---|
| 19 | `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` must-pass. **Both path params are AnyOf unions (`int` or `string`) with implicit conversions**: `SubscriptionIdOrReference.Int(123)` / `ComponentIdModel.Int(456)` / `ComponentIdModel.String("handle:api-calls")` — so the component may be addressed by **numeric id or `handle:` string** | `CreateUsageRequest { Usage (usage): CreateUsage !req }`; `CreateUsage { Quantity (quantity): double?, PricePointId (price_point_id): string?, Memo (memo): string?, BillingSchedule: BillingSchedule?, CustomPrice: ComponentCustomPrice? }` — **`Quantity` is `double?`**; negative values deduct | `UsageResponse.Usage (usage): Usage !req`; `Usage { Id: long?, Memo: string?, CreatedAt: DateTimeOffset?, PricePointId: int?, Quantity (quantity): Quantity1? (union int\|string), OverageQuantity: int?, ComponentId: int?, ComponentHandle: string?, SubscriptionId: int? }` | **A** `CreateUsageError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionComponents.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md`; `unions.md` |
| 20 | `client.SubscriptionComponents.ReadSubscriptionComponent` — **period-to-date total** | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — **`componentId` is `int` here (no handle form)** | — | `SubscriptionComponentResponse.Component (component): SubscriptionComponent?` **(nullable)**; `SubscriptionComponent { Id: int?, Name: string?, Kind (kind): ComponentKind?, UnitName: string?, Enabled: bool?, **UnitBalance (unit_balance): int?** ← running metered total for the current period (raw units, NOT money), AllocatedQuantity (allocated_quantity): AllocatedQuantity2? (union int\|string) ← quantity-based only, PricingScheme: PricingScheme?, ComponentId: int?, ComponentHandle: string?, SubscriptionId: int?, PricePointId/Handle/Name, ProductFamilyId: int?, HistoricUsages: IReadOnlyList<HistoricUsage>?, Interval: int?, IntervalUnit: IntervalUnit? }` | **A** `ReadSubscriptionComponentError`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | `operations/SubscriptionComponents.md`; `records-3-Of-Su.md` |
| 21 | `client.SubscriptionComponents.ListUsages` (fallback total = sum) | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 must-pass (`sinceId`…`untilDate`) | — | `IReadOnlyList<UsageResponse>` → `.Usage.Quantity` (union: `TryGetInt` / `TryGetString`) | **B** `RawError` | `operations/SubscriptionComponents.md` |

#### UC3 — plan change: preview then commit

| # | Call | Exact signature | Request model → fields | Response → payload | Error case | Map page |
|---|---|---|---|---|---|---|
| 22 | `client.SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` must-pass | `SubscriptionMigrationPreviewRequest { Migration (migration): SubscriptionMigrationPreviewOptions !req }`; `SubscriptionMigrationPreviewOptions { ProductId (product_id): int?, ProductHandle (product_handle): string?, ProductPricePointId: int?, ProductPricePointHandle: string?, IncludeTrial (include_trial): bool? = false, IncludeInitialCharge (include_initial_charge): bool? = false, IncludeCoupons (include_coupons): bool? = true, PreservePeriod (preserve_period): bool? = false, Proration (proration): Proration?, ProrationDate (proration_date): DateTimeOffset? }`; `Proration` is a **record** (`Models/Proration.cs`), not an enum: `{ PreservePeriod (preserve_period): bool? }` | `SubscriptionMigrationPreviewResponse.Migration (migration): SubscriptionMigrationPreview !req`; `SubscriptionMigrationPreview { ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?, ChargeInCents (charge_in_cents): long?, PaymentDueInCents (payment_due_in_cents): long?, CreditAppliedInCents (credit_applied_in_cents): long? }` — **all four are CENTS (`long`)** | **A** `PreviewSubscriptionProductMigrationError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | `operations/SubscriptionProducts.md`; `records-4-Su-We.md`; `records-3-Of-Su.md` |
| 23 | **(a) commit immediately, prorated** — `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` must-pass | `SubscriptionProductMigrationRequest { Migration (migration): SubscriptionProductMigration !req }`; `SubscriptionProductMigration { ProductId: int?, ProductHandle (product_handle): string?, ProductPricePointId: int?, ProductPricePointHandle: string?, IncludeTrial: bool? = false, IncludeInitialCharge: bool? = false, IncludeCoupons: bool? = true, PreservePeriod (preserve_period): bool? = false, Proration (proration): Proration? }` — **use the same field values you previewed with in #22 so the preview and the commit agree** | `SubscriptionResponse.Subscription` (nullable) | **A** `MigrateSubscriptionProductError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionProducts.md`; `records-4-Su-We.md` |
| 24 | **(b) deferred to next renewal, no proration** — `client.Subscriptions.UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must-pass | `UpdateSubscriptionRequest { Subscription (subscription): UpdateSubscription !req }`; set **`ProductHandle (product_handle): string?`** (or `ProductId: int?`) **and `ProductChangeDelayed (product_change_delayed): bool? = true`** → change happens at the next renewal, **no proration** (operation Notes, `operations/Subscriptions.md`). To **cancel** a scheduled delayed change: set `NextProductId (next_product_id): string?` to `""` (empty string — the field is `string?`, not `int?`) | `SubscriptionResponse.Subscription`; read back `NextProductId (int?)` / `NextProductHandle (string?)` on `Subscription` | **A** `UpdateSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/Subscriptions.md`; `records-4-Su-We.md`; `records-3-Of-Su.md` |

#### UC4 — lifecycle

| # | Call | Exact signature | Request model → fields | Response → payload | Error case | Map page |
|---|---|---|---|---|---|---|
| 25 | Pause (hold) — `client.SubscriptionStatus.PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` must-pass (pass `null` for an indefinite hold) | `PauseRequest { Hold (hold): AutoResume? }`; `AutoResume { AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset? }` — **no reason/message field** | `SubscriptionResponse.Subscription` → expect `State == SubscriptionState.OnHold`, `OnHoldAt` set | **A** `PauseSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`; `records-3-Of-Su.md`; `records-1-Ac-Cr.md` |
| 26 | Resume — `client.SubscriptionStatus.ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — **no body**; the single query param `calendar_billing['resumption_charge']` is must-pass (pass `null`) | — | `SubscriptionResponse.Subscription` → `State == SubscriptionState.Active` (or `.Trialing`) | **A** `ResumeSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md` |
| 27 | Cancel immediately — `client.SubscriptionStatus.CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` (`DELETE /subscriptions/{id}.json`) — `body` must-pass; **omit all schedule params to cancel now** | `CancellationRequest { Subscription (subscription): CancellationOptions !req }`; `CancellationOptions { CancellationMessage (cancellation_message): string?, ReasonCode (reason_code): string?, CancelAtEndOfPeriod (cancel_at_end_of_period): bool?, ScheduledCancellationAt: DateTimeOffset?, RefundPrepaymentAccountBalance: bool? }` | `SubscriptionResponse.Subscription` → `State == SubscriptionState.Canceled`, `CanceledAt`, `CancellationMessage` | **A** `CancelSubscriptionApiError` **(note the `Api` infix — the type is NOT `CancelSubscriptionError`)**: `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError`. `CancelSubscriptionErrorResponse` is itself an **AnyOf union**: `TryGetErrorListResponse1(out …)` / `TryGetSingleErrorResponse1(out …)` | `operations/SubscriptionStatus.md`; `records-1-Ac-Cr.md`; `unions.md` |
| 28 | Cancel at end of period — `client.SubscriptionStatus.InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` (`POST …/delayed_cancel.json`) — `body` must-pass; same `CancellationRequest`/`CancellationOptions` as #27 | as #27 | **`DelayedCancellationResponse { Message (message): string? }` — that is the ENTIRE payload; no state, no dates.** To report state, follow with #17 `ReadSubscription` and read `CancelAtEndOfPeriod` / `DelayedCancelAt` | **A** `InitiateDelayedCancellationError`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`; `records-2-Cr-Ne.md` |
| 29 | Undo delayed cancel — `client.SubscriptionStatus.CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` (`DELETE …/delayed_cancel.json`) — no body; **idempotent** per the operation Notes | — | `DelayedCancellationResponse { Message: string? }` (again message-only) | **A** `CancelDelayedCancellationError`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | `operations/SubscriptionStatus.md` |
| 30 | Reactivate — `client.SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must-pass | `ReactivateSubscriptionRequest { CalendarBilling (calendar_billing): ReactivationBilling?, IncludeTrial (include_trial): bool?, PreserveBalance (preserve_balance): bool?, CouponCode (coupon_code): string?, UseCreditsAndPrepayments (use_credits_and_prepayments): bool?, Resume (resume): Resume? (union bool\|ResumeOptions → `Resume.Bool(true)`) }` — **no reason/message field** | `SubscriptionResponse.Subscription` → `State == SubscriptionState.Active` or `.Trialing`; `CanceledAt`/`CancellationMessage` cleared | **A** `ReactivateSubscriptionError`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md`; `records-3-Of-Su.md`; `unions.md` |

### 3.5 Enums needed (namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>`, **not** C# enums)

Construct with the static member (`IntervalUnit.Month`) or `IntervalUnit.FromValue("month")`; read the wire
value with `.Value`; `==` compares by value; guard unknowns with `TryGetKnownValue`/`IsKnownValue`.
Source: `map/models/enums.md` + `dotnet-models`.

| Enum | C# member (wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` — **paused/hold is `OnHold` (`on_hold`); a separate `Paused (paused)` member also exists** |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` — **only two members** |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `ResumptionCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `SubscriptionStateFilter` (only for `ListSubscriptions`) | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` |

**There is no "product-change proration timing" enum.** Timing is selected structurally:
migrate-now (#23) vs `ProductChangeDelayed = true` on update (#24); the `Proration` type is a *record*
(`{ PreservePeriod: bool? }`), not an enum. Source: `records-3-Of-Su.md`, `operations/Subscriptions.md`.

### 3.6 Unions used (namespace `MaxioAdvancedBilling.Models.AnyOf`) — factories, no `new`

| Union | Variants | Factories | Readers | Implicit from |
|---|---|---|---|---|
| `SubscriptionIdOrReference` | `int`, `string` | `.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` | `int`, `string` |
| `ComponentIdModel` | `int`, `string` | `.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` | `int`, `string` |
| `UnitPrice1` (on `MeteredComponent.UnitPrice`) | `string`, `double` | `.String(string)`, `.Double(double)` | `TryGetString`, `TryGetDouble` | `string`, `double` |
| `Quantity1` (on `Usage.Quantity`) | `int`, `string` | `.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` | `int`, `string` |
| `AllocatedQuantity2` (on `SubscriptionComponent.AllocatedQuantity`) | `int`, `string` | `.Int(int)`, `.String(string)` | `TryGetInt`, `TryGetString` | `int`, `string` |
| `Resume` (on `ReactivateSubscriptionRequest.Resume`) | `bool`, `ResumeOptions` | `.Bool(bool)`, `.ResumeOptions(ResumeOptions)` | `TryGetBool`, `TryGetResumeOptions` | `bool`, `ResumeOptions` |
| `CancelSubscriptionErrorResponse` (error payload) | `ErrorListResponse1`, `SingleErrorResponse1` | `.ErrorListResponse1(…)`, `.SingleErrorResponse1(…)` | `TryGetErrorListResponse1`, `TryGetSingleErrorResponse1` | both |

Source: `map/models/unions.md`.

### 3.7 Money / units convention (test-magnitude critical)

| Field | Type | Unit | Source |
|---|---|---|---|
| `CreateOrUpdateProduct.PriceInCents (price_in_cents)` | `long` **required** | **CENTS** — $19.99 ⇒ `1999` | `records-1-Ac-Cr.md` |
| `Product.PriceInCents`, `.TrialPriceInCents`, `.InitialChargeInCents` | `long?` | **CENTS** | `records-3-Of-Su.md` |
| `MeteredComponent.UnitPrice (unit_price)` (create) | `UnitPrice1?` union `string`\|`double` | **DOLLARS** (decimal money units, not cents) — $0.01 ⇒ `UnitPrice1.String("0.01")` (string form avoids binary-float drift) | `records-2-Cr-Ne.md`; `unions.md` |
| `Component.UnitPrice (unit_price)` (read-back) | `string?` | **DOLLARS as a decimal string** — parse with `decimal.Parse(…, CultureInfo.InvariantCulture)` | `records-1-Ac-Cr.md` |
| `Component.PricePerUnitInCents (price_per_unit_in_cents)` | `long?` | **CENTS** — the cents mirror of `UnitPrice`; assert magnitudes against this one | `records-1-Ac-Cr.md` |
| `ComponentPrice.UnitPrice` / `.FormattedUnitPrice` | `string?` | dollars string / display string | `records-1-Ac-Cr.md` |
| `SubscriptionMigrationPreview.*` (`ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents`) | `long?` | **CENTS** | `records-4-Su-We.md` |
| `Subscription.ProductPriceInCents`, `.CurrentBillingAmountInCents`, `.BalanceInCents`, `.CreditBalanceInCents`, `.PrepaymentBalanceInCents`, `.TotalRevenueInCents` | `long?` | **CENTS** | `records-3-Of-Su.md` |
| `CreateUsage.Quantity` | `double?` | **raw units** (not money) | `records-2-Cr-Ne.md` |
| `SubscriptionComponent.UnitBalance` | `int?` | **raw units** (not money) | `records-3-Of-Su.md` |
| `Subscription.SignupRevenue` | `string?` | dollars string | `records-3-Of-Su.md` |

**Rule of thumb from the map: a field whose wire name ends `_in_cents` is `long`/CENTS; a money field whose
wire name has no `_in_cents` suffix is a decimal-dollars `string` (or a string/double union).** Do the
cents↔decimal conversion once, in `MaxioBillingClient`, and expose only domain money types over
`IBillingClient`.

### 3.8 Error contract → `BillingProviderException`

Every operation is **throw-only** — this SDK generates **no** `{Operation}Result` / `ApiResult` no-throw
variants (`sdk-map.md`). Two shapes, and only two:

```csharp
// CASE B — RawError IS the error model. No TryGet* at all, no TryGetRawError.
catch (SdkException<RawError> ex)
{
    var status  = ex.Error.StatusCode;          // System.Net.HttpStatusCode
    var message = ex.Error.ReadAsString();      // prefer this; ReadAsJson<T>() THROWS JsonException on non-JSON bodies
    throw new BillingProviderException((int)status, message, ex);
}

// CASE A — typed {Operation}Error. One branch per accessor, TryGetRawError ALWAYS LAST.
catch (SdkException<CreateSubscriptionError> ex)
{
    if (ex.Error.TryGetErrorListResponse1(out var e422))      // 422: ErrorListResponse1 { Errors: IReadOnlyList<string> !req }
        throw new BillingProviderException(422, string.Join("; ", e422.Errors), ex);
    if (ex.Error.TryGetRawError(out var raw))                 // only statuses with NO more-specific accessor
        throw new BillingProviderException((int)raw.StatusCode, raw.ReadAsString(), ex);
    throw new BillingProviderException(0, "Maxio error with no readable payload", ex);
}
```

| Op group | Exception type to catch | Accessors, in order |
|---|---|---|
| #1 CreateProductFamily, #3 CreateProduct, #6 ArchiveProduct, #11 ArchiveComponent, #15 CreateSubscription, #19 CreateUsage, #22 PreviewMigration, #23 MigrateProduct, #24 UpdateSubscription, #25 Pause, #26 Resume, #30 Reactivate | `SdkException<{Op}Error>` (`{Op}Error` = `CreateProductFamilyError`, `CreateProductError`, `ArchiveProductError`, `ArchiveComponentError`, `CreateSubscriptionError`, `CreateUsageError`, `PreviewSubscriptionProductMigrationError`, `MigrateSubscriptionProductError`, `UpdateSubscriptionError`, `PauseSubscriptionError`, `ResumeSubscriptionError`, `ReactivateSubscriptionError`) | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `TryGetRawError(out RawError)` |
| #7 CreateMeteredComponent | `SdkException<CreateMeteredComponentError>` | `TryGetNoContent(out RawError)` [404] → `TryGetErrorListResponse1` [422] → `TryGetRawError` |
| #13 CreateCustomer | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] → `TryGetRawError` |
| #5 ListProductsForProductFamily | `SdkException<ListProductsForProductFamilyError>` | `TryGetString(out string)` **[404 — a bare string body]** → `TryGetRawError` |
| #20 ReadSubscriptionComponent | `SdkException<ReadSubscriptionComponentError>` | `TryGetNoContent(out RawError)` [404] → `TryGetRawError` |
| #27 CancelSubscription | `SdkException<CancelSubscriptionApiError>` | `TryGetNoContent(out RawError)` [404] → `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422, itself a union → `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1`] → `TryGetRawError` |
| #28 InitiateDelayedCancellation | `SdkException<InitiateDelayedCancellationError>` | `TryGetNoContent` [404] → `TryGetErrorListResponse1` [422] → `TryGetRawError` |
| #29 CancelDelayedCancellation | `SdkException<CancelDelayedCancellationError>` | `TryGetNoContent` [404] → `TryGetRawError` |
| #2 ListProductFamilies, #4 ReadProductByHandle, #8 FindComponent, #9 ReadComponent, #10 ListComponentsForProductFamily, #12 ReadCustomerByReference, #14 ListCustomers, #16 ListCustomerSubscriptions, #17 ReadSubscription, #21 ListUsages | **`SdkException<RawError>`** | none — read `ex.Error.StatusCode` / `ex.Error.ReadAsString()` directly |

Error payload shapes: `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` ·
`SingleErrorResponse1 { Error (error): string !req }` · `CustomerErrorResponse1 { Errors (errors): Errors? }`
where `Errors { PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }`.
Sources: `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `sdk-map.md` (error-core), `unions.md`.

Also catch connection failures at the same boundary (they are **not** `SdkException`):

```csharp
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{ throw new BillingProviderException(0, "Maxio unreachable", ex); }
```
(`dotnet-error-handling`.) Guard **every** call site including reads.

---

## 4. Trap notes (attached to the step where they bite)

1. **S1 / §3.3 — the base URL is an SDK option, not `HttpClient.BaseAddress`.** The SDK builds absolute URLs from `options.Environment` + `options.Server.Production.{Us|Eu}.{BaseUrl|Site}`; set the `Maxio:BaseUrl` override there. Do not depend on a `BaseAddress` you set on the typed `HttpClient` (`sdk-map.md` servers table; `dotnet-configuration-resilience`).
2. **S1 — one long-lived `HttpClient` / `IHttpClientFactory`; never one per request.** The SDK client itself is also meant to be long-lived (`dotnet-client-initialization`). With DI, `AddMaxioAdvancedBillingClient` resolves the **default, unnamed** factory client — that is the one to configure with handlers or the test stub.
3. **All steps — named arguments on every list/search call.** Many nullable params have **no** C# default, so a positional call mis-binds or fails to compile. And the token parameter is `ct:` (`dotnet-calling-endpoints`).
4. **S3–S10 — writes use the envelope pattern.** Every create/update body nests one required inner record: `CreateProductFamilyRequest.ProductFamily`, `CreateOrUpdateProductRequest.Product`, `CreateMeteredComponent.MeteredComponent`, `CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`, `CreateUsageRequest.Usage`, `CancellationRequest.Subscription`, `SubscriptionProductMigrationRequest.Migration`, `UpdateSubscriptionRequest.Subscription`. Reads unwrap symmetrically (`ProductResponse.Product`, …).
5. **S7/S10 — `SubscriptionResponse.Subscription` and `ProductFamilyResponse.ProductFamily` are NULLABLE (`?`)**, unlike `ProductResponse.Product` / `CustomerResponse.Customer` / `ComponentResponse.Component` / `UsageResponse.Usage` (`!req`). Null-check the two nullable ones before dereferencing.
6. **S5/S10 — enums are `StringEnum<T>`, not C# enums.** `component.Kind == ComponentKind.MeteredComponent` works (records compare by value); `switch` over them like C# enum members does not. Use `.Value` for the wire string and `FromValue(...)` for a server-provided value (`dotnet-models`).
7. **S3 — no read-family-by-handle.** `ReadProductFamily` takes `int id` even though its doc text mentions `handle:my-family`; list + match `Handle` client-side, or pass `"handle:my-family"` where a **string** family id is accepted (`CreateProduct`, `ListProductsForProductFamily`).
8. **S5 — `ArchiveComponent` returns `Component`, not `ComponentResponse`** (no `.Component` unwrap). `ArchiveProduct` *does* return `ProductResponse`.
9. **S8 — component addressing differs per operation.** `CreateUsage`/`ListUsages` take `ComponentIdModel` (int **or** `"handle:…"`); `ReadSubscriptionComponent` takes a plain `int componentId`. Resolve the handle to an id once (via `FindComponent`) and cache it.
10. **S8 — `UnitBalance` is units, not money;** it is the accumulated metered `quantity` for the current period (per the `CreateUsage` operation Notes: "The `quantity` from usage … is accumulated to the `unit_balance`"). Floor is 0.
11. **S9 — retries do not cover the mutating calls.** Default `HttpMethodsToRetry` is `GET/HEAD/PUT/OPTIONS`; `POST`/`DELETE` (create subscription, usage, migrate, cancel, pause) are **not** retried, and `RetryOptions.Timeout` is **per attempt**, not total. Bound a whole call with a `CancellationToken` (`dotnet-configuration-resilience`). Do **not** add `POST` to `HttpMethodsToRetry` for `CreateSubscription`/`CreateUsage` — they are not idempotent.
12. **S10 — delayed-cancel operations return only `{ message }`.** Anything the UI needs about state must come from a follow-up `ReadSubscription`.
13. **S2 — `TryGetRawError` is not a catch-all** and must be the **last** branch; a 422 with a typed accessor leaves it `false`. Do not factor error reading into a helper typed as `ApiError` — only `TryGetRawError` is visible there (`dotnet-error-handling`).
14. **S2 — `RawError.ReadAsJson<T>()` throws `JsonException`** on non-JSON bodies; prefer `ReadAsString()`.
15. **S11 — first-run wire check.** Path params are substituted via `value?.ToString()` with no route type-checking, and a successful response surfaces neither URL nor status. Attach a logging `DelegatingHandler` on the first execution of each new call and confirm verb, path (no leftover `{placeholder}`), path-segment values, and query params (`dotnet-configuration-resilience`). Query params are snake_case on the wire — assert e.g. `per_page=20` in tests.
16. **S11 — assert the right exception per operation** (Case A vs Case B, per §3.8); a Case-B test that asserts `SdkException<SomeError>` will not compile, and a Case-A test that asserts through `TryGetRawError` for a 422 will fail (`dotnet-testing`).
17. **S3–S5 — unmodeled JSON fields are dropped on deserialize.** If the seed console needs a field not listed in §3.4, it does not exist on the model; do not expect it to round-trip.

---

## 5. Contract-trust judgment (map/source evidence only)

| Row | Judgment | Basis |
|---|---|---|
| `CreateCustomerError.TryGetCustomerErrorResponse1` [422] | **Low trust as a message source — code defensively.** The generated payload is `CustomerErrorResponse1 { Errors: Errors? }` and `Errors` declares only `PerPage (per_page)` and `PricePoint (price_point)` — fields that have nothing to do with customer validation. `Errors` is a generic shared model reused here. **Directive:** extract best-effort (`e.Errors?.PerPage`, `e.Errors?.PricePoint`, both may be null), and when nothing readable comes out, fall back to `ex.Error.TryGetRawError(out var raw)` → `raw.ReadAsString()`, else a generic message. Whether the live 422 body actually matches this model is `UNVERIFIED`. | `records-2-Cr-Ne.md` (`CustomerErrorResponse1`, `Errors`) |
| `ReadProductFamily(int id)` vs its doc note about `handle:my-family` | **Definitions disagree** — the doc text allows a handle, the C# parameter is `int`. Trust the signature; use list-and-match (#2b). | `operations/ProductFamilies.md` |
| `SubscriptionState` has both `Paused (paused)` and `OnHold (on_hold)` | Two members that could each mean "paused". `PauseSubscription` posts to `…/hold.json`, so treat **`OnHold`** as the paused state and map `Paused` onto the same domain state defensively (accept either). | `enums.md`; `operations/SubscriptionStatus.md` |
| 404 on `ReadCustomerByReference` = "not found" | `UNVERIFIED` — only live traffic proves Maxio returns 404 (rather than 200 + empty) for an unknown reference. **Directive:** treat `HttpStatusCode.NotFound` as not-found, treat any *other* non-2xx as a real failure, and additionally guard against a 2xx with a missing/blank customer by null/empty-`Id` checking before using the result. | `operations/Customers.md` |
| `MigrateSubscriptionProduct` produces the proration previewed by `PreviewSubscriptionProductMigration` | `UNVERIFIED` — the map shows matching request options (`SubscriptionMigrationPreviewOptions` ⊃ `SubscriptionProductMigration` + `ProrationDate`) but only live traffic proves amounts agree. **Directive:** send identical `ProductHandle`/`PreservePeriod`/`IncludeCoupons`/`IncludeTrial`/`IncludeInitialCharge`/`Proration` values in preview and commit, re-read the subscription after commit, and never assert equality of preview vs charged amount in a unit test — assert it only against a stubbed response. | `operations/SubscriptionProducts.md`; `records-4-Su-We.md` |
| Exact declared type/namespace of `ServerOptions`, and the member names on `ProductionOptions` beyond `BaseUrl`/`Site` | Map names the override points (`options.Server.Production.Us.BaseUrl` / `.Site`) and the source files (`ServerOptions.cs`, `Servers/ProductionOptions.cs`) but not the full member list. **→ maxio-debug resolves from source if it surfaces** (only needed if you must declare one of these types by name). | `sdk-map.md` (servers table) |
| Full body of `Api/Subscriptions.cs` `UpdateSubscription` — i.e. exactly how `next_product_id: ""` is serialized to cancel a delayed change | Behaviour is documented in the operation Notes; the serialization detail is source-level. **→ maxio-debug resolves from source if it surfaces** (only if the empty-string call fails to clear `NextProductId`). | `operations/Subscriptions.md` |

---

## 6. Assumptions & Blockers

**Assumptions**

1. Seed product is priced in whole cents and created **without** a trial and **without** an expiration —
   implemented by omitting `TrialPriceInCents`/`TrialInterval`/`TrialIntervalUnit`/`TrialType` and
   `ExpirationInterval`/`ExpirationIntervalUnit` (all nullable on `CreateOrUpdateProduct`).
2. `CreateOrUpdateProduct` has **no `taxable` field and no setup/initial-charge field** (map lists only
   `TaxCode` and no `InitialChargeInCents`). I assume "taxable false / no setup fee" is satisfied by leaving
   them unset (the API default), and that `taxable` is read back off `Product.Taxable`. If taxable must be
   set explicitly it has to be done in the Maxio UI or via a different endpoint — not via `CreateProduct`.
3. `Description` is **required** on `CreateOrUpdateProduct` (`string !req`) — the seed tool must supply one
   even though the brief didn't mention it.
4. "Recurring monthly" = `Interval = 1`, `IntervalUnit = IntervalUnit.Month`.
5. `require_credit_card = false` ⇒ `RequireCreditCard = false` on `CreateOrUpdateProduct`; the read-back
   field is `Product.RequireCreditCard` (there is also a distinct `RequestCreditCard` on `Product` — do not
   confuse them).
6. The idempotency key for the customer is `CreateCustomer.Reference` set to the eShopOnWeb user
   email/username; look-up is `ReadCustomerByReference`.
7. Subscribing identifies product by `ProductHandle` and customer by `CustomerId` (from step 6) — both are
   optional fields on `CreateSubscription`, so nothing enforces this at compile time.
8. "Already subscribed" = `ListCustomerSubscriptions` contains a subscription whose `State` is
   `SubscriptionState.Active` or `.Trialing` for the target product handle.
9. Region/subdomain: `ServerEnvironment.Us` + `Site = "cp-exp-4"` unless `Maxio:BaseUrl` is set.
10. Package version pinned to `1.0.2` to match the map's stamp.

**Blockers**

None for planning. Two things are outside my tool scope by design: I cannot run `dotnet add package`,
build, or inspect the eShopOnWeb project layout (no Bash, and I do not read project code) — placement of
`IBillingClient`/`MaxioBillingClient` inside the solution is the main agent's call.
