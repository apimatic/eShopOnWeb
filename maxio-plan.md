# Maxio Advanced Billing — integration plan for eShopOnWeb (net8.0, Clean Architecture)

Every fact below is grounded in the SDK map that ships with the SDK source (`sdk-map.md`,
`map/operations/*.md`, `map/models/*.md`) or, where the map could not settle it, in the exact source file the
map names. Map/source citations are in the right-hand column of each table.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | **ApplicationCore**: define `IBillingClient` + provider-agnostic DTOs (`PlanSummary`, `SubscriptionSummary`, `UsageRecord`, `PlanChangePreview`) and a `BillingException` (status code + human message). No SDK types cross this boundary. | — |
| 2 | **Infrastructure**: `MaxioOptions` (`Maxio:ApiKey`, `Maxio:SiteSubdomain`, `Maxio:Region` = `US`\|`EU`, `Maxio:BaseUrl`, `Maxio:ProductFamilyHandle`, `Maxio:MeteredComponentHandle`), bound from configuration. | — |
| 3 | **Infrastructure**: `MaxioBillingClient : IBillingClient` — registered as a **typed client** (`services.AddHttpClient<MaxioBillingClient>()`); it constructs `MaxioAdvancedBillingClient(httpClient, options)` in its ctor from the injected `HttpClient`. See §2.5 for auth + base-URL wiring. | — |
| 4 | **Startup validation** (hosted service or `IStartupFilter`): product family exists → product handle resolves to an id → metered component exists and `Kind == metered_component`. | `ListProductFamilies`, `ReadProductFamily`, `ReadProductByHandle`, `ReadComponent`/`FindComponent` |
| 5 | **Plans page**: list products of the family, project name/handle/price/interval. | `ListProductsForProductFamily` |
| 6 | **Subscribe flow**: look up customer by reference (eShop email) → create if 404 → check existing active subscription → create subscription. | `ReadCustomerByReference`, `CreateCustomer`, `ListCustomerSubscriptions`, `CreateSubscription` |
| 7 | **Subscription detail**: read one subscription (state, period, product, customer). | `ReadSubscription` |
| 8 | **Metered usage**: record usage; show period-to-date total. | `CreateUsage`, `ListUsages` |
| 9 | **Plan change**: preview proration → commit now, or schedule at renewal → cancel a pending delayed change. | `PreviewSubscriptionProductMigration`, `MigrateSubscriptionProduct`, `UpdateSubscription` |
| 10 | **Lifecycle**: pause / resume / cancel now / cancel at period end / clear pending cancel / reactivate. | `PauseSubscription`, `ResumeSubscription`, `CancelSubscription`, `InitiateDelayedCancellation`, `CancelDelayedCancellation`, `ReactivateSubscription` |
| 11 | **Tests**: `StubHandler : HttpMessageHandler` → `new HttpClient(stub)` → `new MaxioBillingClient(httpClient, options)`. Assert outgoing method/path/query/body and both error shapes. | — |

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
> `MaxioAdvancedBilling.ServerOptions`). Do not drop them to the root or `.Models`, or the implementer
> guesses the wrong `using` and the build breaks.
>
> **Namespace correction (source-verified):** `ServerOptions` and `Server` live in the **root** namespace
> `MaxioAdvancedBilling` — *not* `MaxioAdvancedBilling.Core.Configuration` (source: `ServerOptions.cs` line 3,
> `Server.cs` line 4). `ProductionOptions` (and its nested `UsOptions`/`EuOptions`) live in
> `MaxioAdvancedBilling.Servers` (source: `Servers/ProductionOptions.cs` line 3). `RetryOptions` **is** in
> `MaxioAdvancedBilling.Core.Configuration` (sdk-map.md).

### 2.0 Namespaces to `using`

| Contents | Namespace | Source |
|---|---|---|
| Client, options, `ServerOptions` | `MaxioAdvancedBilling` | sdk-map.md · `ServerOptions.cs` |
| Controllers (`client.Products`, …) | `MaxioAdvancedBilling.Api` | sdk-map.md |
| Records (all request/response models) | `MaxioAdvancedBilling.Models` | sdk-map.md, records pages |
| Enums (`SubscriptionState`, `ComponentKind`, …) | `MaxioAdvancedBilling.Models.Enums` | `models/enums.md` |
| Unions (`SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`) | `MaxioAdvancedBilling.Models.AnyOf` | `models/unions.md` |
| Typed error classes (`CreateCustomerError`, …) | `MaxioAdvancedBilling.Errors` | sdk-map.md |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `ApiError`, `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `Core/ErrorResponse/*.cs` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Servers/ServerEnvironment.cs` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | sdk-map.md |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | sdk-map.md |

### 2.1 Operations

Response envelopes are single-field wrappers: always unwrap (`ProductResponse.Product`,
`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `UsageResponse.Usage`,
`ComponentResponse.Component`, `ProductFamilyResponse.ProductFamily`,
`SubscriptionMigrationPreviewResponse.Migration`).

| # | Controller · signature (verbatim) | Request model construction | Response → fields read | Error case + accessors | Map page |
|---|---|---|---|---|---|
| 1 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` are nullable with **no default → must pass explicitly** (`null` to skip) | `productFamilyId` accepts an id **or** `"handle:my-family"` (string param). Pass `filter: null, includeArchived: false, include: null` etc. **by name.** | `IReadOnlyList<ProductResponse>` → `.Product`: `Name (name): string?`, `Handle (handle): string?`, `Id (id): int?`, `PriceInCents (price_in_cents): long?` (**cents**), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (`day`\|`month`), `ArchivedAt (archived_at): DateTimeOffset?`, `ProductPricePointId (product_price_point_id): int?` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 2a | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must be passed explicitly | all `null` | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description`, `CreatedAt/UpdatedAt/ArchivedAt: DateTimeOffset?` | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 2b | `client.ProductFamilies.ReadProductFamily(int id, CancellationToken ct = default)` | **`int` only** — see GAP G1: handle lookup is not expressible here | `ProductFamilyResponse.ProductFamily` (fields as 2a) | **Case B** `SdkException<RawError>` (404 when absent) | `operations/ProductFamilies.md` |
| 3 | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — `GET /products/handle/{api_handle}.json` | plain handle, **no** `handle:` prefix | `ProductResponse.Product` → `.Id` is the live numeric id | **Case B** `SdkException<RawError>` — unresolved handle ⇒ `ex.Error.StatusCode == HttpStatusCode.NotFound`; fail startup with a clear message | `operations/Products.md` |
| 3b | `client.Products.ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse.Product` | **Case B** `SdkException<RawError>` | `operations/Products.md` |
| 4a | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **pass explicitly** | `new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = …, LastName = …, Email = …, Reference = <eShop user email/username> } }`. `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, plus optional `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `CcEmails`, `ParentId`, … **`FirstName`/`LastName`/`Email` are C# `required`** — the object initializer will not compile without them. | `CustomerResponse.Customer` → `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName`, `CreatedAt (created_at): DateTimeOffset?` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. **Duplicate `reference` ⇒ 422** (map note: "you may only create one customer for a given reference value") ⇒ lands in `TryGetCustomerErrorResponse1`, and `TryGetRawError` is then **false** (see T5). | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 4b | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json?reference=` | — | `CustomerResponse.Customer` (single exact match) | **Case B** `SdkException<RawError>` — not found ⇒ `StatusCode == NotFound`; treat as "create it" | `operations/Customers.md` |
| 4c | `client.Customers.ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse.Customer` | **Case B** `SdkException<RawError>` | `operations/Customers.md` |
| 5 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **pass explicitly** | `new CreateSubscriptionRequest { Subscription = new CreateSubscription { CustomerId = 123, ProductHandle = "pro-plan" } }`. Identity choices on `CreateSubscription`: `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`; `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`; price point via `ProductPricePointHandle`/`ProductPricePointId`. Also available: `Reference (reference): string?` (your own idempotency ref), `CouponCode`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `NextBillingAt: DateTimeOffset?`, `Components: IReadOnlyList<CreateSubscriptionComponent>?`. **No payment-profile fields needed** when the product has "requires payment method" off — leave `CreditCardAttributes`/`PaymentProfileId` unset. | `SubscriptionResponse.Subscription` (fields per row 7) | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` ⇒ `string.Join("; ", …)` is your human message. | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| 6a | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → filter client-side on `.Subscription.State` | **Case B** `SdkException<RawError>` | `operations/Customers.md` |
| 6b | `client.Subscriptions.ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` **must be passed explicitly** | **Named arguments are mandatory** here (14 same-shaped nullable params — a positional call mis-binds silently). E.g. `ListSubscriptions(state: SubscriptionStateFilter.Active, product: productId, productPricePointId: null, coupon: null, couponCode: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, metadata: null, direction: null, sort: null, include: null, page: 1, perPage: 200, ct: ct)`. **No customer filter exists** — see GAP G2. | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | `operations/Subscriptions.md` |
| 7 | `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **pass explicitly** (`null`, or `[SubscriptionInclude.Coupons]`) | — | `SubscriptionResponse.Subscription`: `Id (id): int?`, `State (state): SubscriptionState?`, `PreviousState`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt`, `CanceledAt`, `DelayedCancelAt (delayed_cancel_at): DateTimeOffset?`, `ScheduledCancellationAt`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CancellationMessage`, `CancellationMethod (cancellation_method): CancellationMethod?`, `OnHoldAt (on_hold_at): DateTimeOffset?`, `AutomaticallyResumeAt`, `ProductPriceInCents (product_price_in_cents): long?` (**cents**), `CurrentBillingAmountInCents: long?` (**cents**), `BalanceInCents: long?`, `TotalRevenueInCents: long?`, `ProductPricePointId: int?`, `NextProductId (next_product_id): int?`, `NextProductHandle (next_product_handle): string?`, `NextProductPricePointId: int?`, `Reference (reference): string?`, `Currency: string?`, `Product (product): Product?` (nested → `Id`, `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`), `Customer (customer): Customer?` (nested → `Id`, `Email`, `FirstName`, `LastName`, `Reference`) | **Case B** `SdkException<RawError>` | `operations/Subscriptions.md`, `records-3-Of-Su.md` |
| 8 | `client.SubscriptionComponents.CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `POST /subscriptions/{subscription_id_or_reference}/components/{component_id}/usages.json`; `body` **pass explicitly** | Both path params are **AnyOf unions with implicit conversions** — `SubscriptionIdOrReference` (int \| string; factories `.Int(int)`, `.String(string)`), `ComponentIdModel` (int \| string; `.Int(int)`, `.String(string)`). Pass the numeric subscription id (`subscriptionId` implicitly converts) and either the numeric component id **or** the string `"handle:api-calls"` (the `handle:` prefix is required when addressing by handle). Body: `new CreateUsageRequest { Usage = new CreateUsage { Quantity = 5, Memo = "…" } }`; `CreateUsage`: `Quantity (quantity): double?`, `Memo (memo): string?`, `PricePointId (price_point_id): string?`, `BillingSchedule`, `CustomPrice`. Negative `Quantity` deducts (balance floors at 0). | `UsageResponse.Usage` **!req** → `Id (id): long?`, `Quantity (quantity): Quantity1?` **(union int\|string)**, `Memo (memo): string?`, `CreatedAt (created_at): DateTimeOffset?`, `ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`, `SubscriptionId (subscription_id): int?`, `PricePointId: int?`, `OverageQuantity: int?` | **Case A** `SdkException<CreateUsageError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/SubscriptionComponents.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md`, `unions.md` |
| 9 | `client.SubscriptionComponents.ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `sinceId`…`untilDate` **must be passed explicitly** | Call by name: `ListUsages(subscriptionId, "handle:api-calls", sinceId: null, maxId: null, sinceDate: periodStart, untilDate: null, page: 1, perPage: 200, ct: ct)`. `since_date`/`until_date` default to **midnight** of the given date. Metered components only (map note: not compatible with quantity-based). | `IReadOnlyList<UsageResponse>` → sum `.Usage.Quantity`: it is the union `Quantity1` — `if (q.TryGetInt(out var i)) total += i; else if (q.TryGetString(out var s)) total += decimal.Parse(s, CultureInfo.InvariantCulture);` **Paginate** (`perPage` max page size) until a short page. | **Case B** `SdkException<RawError>` | `operations/SubscriptionComponents.md`, `unions.md` |
| 10a | `client.Components.ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params `includeArchived`…`startDatetime` **explicit** | `productFamilyId` is **`int`** here (needs the resolved numeric family id) | `IReadOnlyList<ComponentResponse>` → `.Component` | **Case B** `SdkException<RawError>` | `operations/Components.md` |
| 10b | `client.Components.ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | `componentId` = numeric id **or** `"handle:api-calls"` (prefix required) | `ComponentResponse.Component` **!req** → `Id (id): int?`, `Name`, `Handle (handle): string?`, **`Kind (kind): ComponentKind?`**, `UnitName (unit_name): string?`, `UnitPrice (unit_price): string?` (**decimal string, currency units — not cents**), `PricePerUnitInCents (price_per_unit_in_cents): long?` (**cents**), `PricingScheme (pricing_scheme): PricingScheme?`, `ProductFamilyId: int?`, `Archived (archived): bool?`, `Recurring: bool?`, `DefaultPricePointId: int?`, `Prices: IReadOnlyList<ComponentPrice?>?` | **Case B** `SdkException<RawError>` | `operations/Components.md`, `records-1-Ac-Cr.md` |
| 10c | `client.Components.FindComponent(string handle, CancellationToken ct = default)` — `GET /components/lookup.json?handle=` | site-wide handle lookup, **no** `handle:` prefix | `ComponentResponse.Component` | **Case B** `SdkException<RawError>` | `operations/Components.md` |
| 11 | `client.SubscriptionProducts.PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **pass explicitly** | `new SubscriptionMigrationPreviewRequest { Migration = new SubscriptionMigrationPreviewOptions { ProductHandle = "pro-plan" } }`. `SubscriptionMigrationPreviewOptions`: `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `ProductPricePointId: int?`, `ProductPricePointHandle: string?`, `IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge (include_initial_charge): bool? = false`, `IncludeCoupons (include_coupons): bool? = true`, `PreservePeriod (preserve_period): bool? = false`, `Proration (proration): Proration?` (`Proration.PreservePeriod: bool?`), `ProrationDate (proration_date): DateTimeOffset?` (future date inside the current period) | `SubscriptionMigrationPreviewResponse.Migration` **!req** → `ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?`, `ChargeInCents (charge_in_cents): long?`, `PaymentDueInCents (payment_due_in_cents): long?`, `CreditAppliedInCents (credit_applied_in_cents): long?` — **all `long?` cents** | **Case A** `SdkException<PreviewSubscriptionProductMigrationError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/SubscriptionProducts.md`, `records-4-Su-We.md` |
| 12a | **Apply now, with proration** — `client.SubscriptionProducts.MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` | `new SubscriptionProductMigrationRequest { Migration = new SubscriptionProductMigration { ProductHandle = "pro-plan", IncludeInitialCharge = false, PreservePeriod = false } }`. `SubscriptionProductMigration` = same fields as the preview options **minus `ProrationDate`**. Legal source states: `active` or `trialing` (`trial_ended` tolerated but discouraged). Migrating to the **current** product is the most common failure. | `SubscriptionResponse.Subscription` | **Case A** `SdkException<MigrateSubscriptionProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/SubscriptionProducts.md`, `records-4-Su-We.md` |
| 12b | **At next renewal, no proration (delayed product change)** — `client.Subscriptions.UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` | `new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = "pro-plan", ProductChangeDelayed = true } }`. `UpdateSubscription` relevant fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, **`ProductChangeDelayed (product_change_delayed): bool?`**, `NextProductId (next_product_id): **string?**`, `NextProductPricePointId (next_product_price_point_id): string?`, `ProductPricePointId: int?`, `ProductPricePointHandle: string?`, `NextBillingAt: DateTimeOffset?`, `Reference`, `SnapDay (union)`, `NetTerms (union)`. | `SubscriptionResponse.Subscription` → pending change visible as `NextProductId (next_product_id): int?` / `NextProductHandle (next_product_handle): string?` | **Case A** `SdkException<UpdateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/Subscriptions.md`, `records-4-Su-We.md` |
| 12c | **Cancel a pending delayed product change** — same `UpdateSubscription` op | `new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextProductId = "" } }` — the map's operation note is explicit: *"To cancel a delayed product change, set `next_product_id` to an empty string."* The request-side `NextProductId` is typed **`string?`** precisely to allow `""` (the response-side `Subscription.NextProductId` is `int?`). | `SubscriptionResponse.Subscription` → `NextProductId`/`NextProductHandle` cleared | as 12b | `operations/Subscriptions.md`, `records-4-Su-We.md` |
| 13a | **Pause / hold** — `client.SubscriptionStatus.PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `POST /subscriptions/{id}/hold.json`; `body` **pass explicitly** (`null` for indefinite hold) | `new PauseRequest { Hold = new AutoResume { AutomaticallyResumeAt = when } }`. `PauseRequest.Hold (hold): AutoResume?`; `AutoResume.AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?`. **Limitation (map note): may not pause if `next_billing_at` is within 24 hours.** Legal source state: `active` (result state `on_hold`). | `SubscriptionResponse.Subscription` → `State == SubscriptionState.OnHold`, `OnHoldAt`, `AutomaticallyResumeAt` | **Case A** `SdkException<PauseSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/SubscriptionStatus.md`, `records-3-Of-Su.md`, `records-1-Ac-Cr.md` |
| 13a′ | **Change/remove the auto-resume date** — `client.SubscriptionStatus.UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `PUT …/hold.json` | same `PauseRequest`; `AutomaticallyResumeAt = null` removes it | `SubscriptionResponse.Subscription` | **Case A** `SdkException<UpdateAutomaticSubscriptionResumptionError>` — `TryGetErrorListResponse1` [422] · `TryGetRawError` | `operations/SubscriptionStatus.md` |
| 13b | **Resume / unhold** — `client.SubscriptionStatus.ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — `POST …/resume.json`; the param **has no default → pass `calendarBillingResumptionCharge: null` explicitly** | Query param `calendar_billing['resumption_charge']`; only meaningful for calendar-billing subscriptions. Enum `ResumptionCharge`: `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)`. Legal source state: `on_hold`. | `SubscriptionResponse.Subscription` → `State` back to `Active` (or reactivation-like behaviour if the renewal date has passed) | **Case A** `SdkException<ResumeSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/SubscriptionStatus.md`, `enums.md` |
| 13c | **Cancel immediately** — `client.SubscriptionStatus.CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `DELETE /subscriptions/{id}.json`; `body` **pass explicitly** | **Omit schedule parameters to cancel immediately** — pass `body: null`, or `new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "…", ReasonCode = "…" } }`. `CancellationOptions`: `CancellationMessage (cancellation_message): string?`, `ReasonCode (reason_code): string?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `RefundPrepaymentAccountBalance: bool?`. | `SubscriptionResponse.Subscription` → `State == SubscriptionState.Canceled`, `CanceledAt`, `CancellationMethod == CancellationMethod.MerchantApi` | **Case A** `SdkException<CancelSubscriptionApiError>` *(note the `Api` in the type name)* — `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError(out RawError)` [fallback]. `CancelSubscriptionErrorResponse` is itself an **AnyOf union**: `TryGetErrorListResponse1(out …)` / `TryGetSingleErrorResponse1(out …)` (`SingleErrorResponse1.Error (error): string !req`). | `operations/SubscriptionStatus.md`, `records-1-Ac-Cr.md`, `unions.md` |
| 13d | **Cancel at end of period (delayed)** — `client.SubscriptionStatus.InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `POST …/delayed_cancel.json` | `body: null` is sufficient; optional `CancellationRequest` with message/reason. **Not allowed at subscription creation, nor while `past_due`.** | `DelayedCancellationResponse` → **`Message (message): string?` only** — it does **not** return the subscription. To confirm the pending cancel, re-read the subscription: `CancelAtEndOfPeriod == true`, `DelayedCancelAt`/`ScheduledCancellationAt` set. | **Case A** `SdkException<InitiateDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/SubscriptionStatus.md`, `records-2-Cr-Ne.md` |
| 13e | **Clear the pending cancellation** — `client.SubscriptionStatus.CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` — `DELETE …/delayed_cancel.json` | no body; **idempotent** (no-op success if nothing pending) | `DelayedCancellationResponse.Message` | **Case A** `SdkException<CancelDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | `operations/SubscriptionStatus.md` |
| 13f | **Reactivate a cancelled subscription** — `client.SubscriptionStatus.ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `PUT …/reactivate.json`; `body` **pass explicitly** | `new ReactivateSubscriptionRequest { Resume = true }` (resume the same billing period if still inside it) or `body: null`. `ReactivateSubscriptionRequest`: `Resume (resume): Resume?` **(union `bool` \| `ResumeOptions`; implicit from `bool`; `ResumeOptions { RequireResume, ForgiveBalance }`)**, `IncludeTrial (include_trial): bool?`, `PreserveBalance (preserve_balance): bool?`, `CouponCode (coupon_code): string?`, `UseCreditsAndPrepayments: bool?`, `CalendarBilling (calendar_billing): ReactivationBilling?` (`ReactivationCharge` = `prorated`\|`immediate`\|`delayed`). **Legal source states (map note): `canceled`, `unpaid`, `trial_ended`** — it will not work from other states. Result state: `active` (or `trialing` with `IncludeTrial`). | `SubscriptionResponse.Subscription` → `State`, `CanceledAt`/`CancellationMessage` cleared | **Case A** `SdkException<ReactivateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/SubscriptionStatus.md`, `records-3-Of-Su.md`, `unions.md` |

### 2.2 Enums needed (all `StringEnum<T>` records in `MaxioAdvancedBilling.Models.Enums` — **not** C# enums; build via the static member or `T.FromValue("wire")`)

**`SubscriptionState`** (the `Subscription.State` you read back):

| Member | wire | | Member | wire |
|---|---|---|---|---|
| `SubscriptionState.Pending` | `pending` | | `SubscriptionState.Canceled` | `canceled` |
| `SubscriptionState.FailedToCreate` | `failed_to_create` | | `SubscriptionState.Expired` | `expired` |
| `SubscriptionState.Trialing` | `trialing` | | `SubscriptionState.Paused` | `paused` |
| `SubscriptionState.Assessing` | `assessing` | | `SubscriptionState.Unpaid` | `unpaid` |
| `SubscriptionState.Active` | `active` | | `SubscriptionState.TrialEnded` | `trial_ended` |
| `SubscriptionState.SoftFailure` | `soft_failure` | | `SubscriptionState.OnHold` | `on_hold` |
| `SubscriptionState.PastDue` | `past_due` | | `SubscriptionState.AwaitingSignup` | `awaiting_signup` |
| `SubscriptionState.Suspended` | `suspended` | | | |

> **`paused` vs `on_hold`:** `PauseSubscription` hits `/hold.json`; the state you must test for after a pause is
> **`OnHold`**. `Paused` is a separate member of the same enum — do not conflate them.

**`SubscriptionStateFilter`** (the `state:` argument of `ListSubscriptions` — a *different* enum, fewer members):
`Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`,
`PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`,
`Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`.

**`ComponentKind`** (startup validation of the metered component — `Component.Kind`):
`MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`,
`OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`,
`EventBasedComponent (event_based_component)`. Refuse startup unless
`component.Kind == ComponentKind.MeteredComponent`.

**`IntervalUnit`**: `Day (day)`, `Month (month)` — the only two. (Product billing interval = `Interval` (int) × `IntervalUnit`.)

**`CancellationMethod`**: `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`,
`BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)`.

**`ResumptionCharge`** / **`ReactivationCharge`**: both `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)`.

**`CollectionMethod`**: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`.

**`PricingScheme`**: `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)`.

**`SubscriptionInclude`**: `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)`.
**`SortingDirection`**: `Asc (asc)`, `Desc (desc)`. **`BasicDateField`**: `UpdatedAt (updated_at)`, `CreatedAt (created_at)`.

*(source: `models/enums.md`)*

### 2.3 Money / unit conventions (assert these in tests)

| Field | C# type | Unit | Source |
|---|---|---|---|
| `Product.PriceInCents`, `Product.TrialPriceInCents`, `Product.InitialChargeInCents` | `long?` | **integer cents** | `records-3-Of-Su.md` |
| `Subscription.ProductPriceInCents`, `.CurrentBillingAmountInCents`, `.BalanceInCents`, `.TotalRevenueInCents`, `.CreditBalanceInCents`, `.PrepaymentBalanceInCents` | `long?` | **integer cents** | `records-3-Of-Su.md` |
| `SubscriptionMigrationPreview.ProratedAdjustmentInCents / ChargeInCents / PaymentDueInCents / CreditAppliedInCents` | `long?` | **integer cents** | `records-4-Su-We.md` |
| `Component.PricePerUnitInCents` | `long?` | **integer cents** | `records-1-Ac-Cr.md` |
| `Component.UnitPrice`, `ComponentPrice.UnitPrice`, `SegmentPrice.UnitPrice` | `string?` | **decimal string in currency units (dollars), not cents** — parse with `decimal.Parse(s, CultureInfo.InvariantCulture)` | `records-1-Ac-Cr.md` |
| `Usage.Quantity` | `Quantity1` **union** (`int` \| `string`) | unitless count; may arrive as either JSON number or string — read with `TryGetInt` then `TryGetString` | `records-4-Su-We.md`, `unions.md` |
| `CreateUsage.Quantity` (request) | `double?` | unitless count; negative = deduction | `records-2-Cr-Ne.md` |
| `Invoice`/`ProformaInvoice` `TotalAmount`, `DueAmount`, `PaidAmount`, … | `string?` | decimal strings in currency units | `records-2-Cr-Ne.md`, `records-3-Of-Su.md` |
| `Subscription.Currency`, `Product`-level currency | `string?` | ISO code | `records-3-Of-Su.md` |

**Rule of thumb, verified across the model pages: anything whose wire name ends `_in_cents` is `long?` integer
cents; anything named `unit_price`/`*_amount` is a decimal `string` in currency units.** Convert once, at the
`MaxioBillingClient` boundary, into a single `decimal` money type in ApplicationCore.

### 2.4 Date/time types

All of these are **`System.DateTimeOffset?`** (nullable) — no `DateTime`, no strings:
`Subscription.CurrentPeriodStartedAt`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt`, `.ActivatedAt`,
`.CanceledAt`, `.DelayedCancelAt`, `.ScheduledCancellationAt`, `.OnHoldAt`, `.AutomaticallyResumeAt`,
`.TrialStartedAt`, `.TrialEndedAt`, `.ExpiresAt`, `.CreatedAt`, `.UpdatedAt`; `Usage.CreatedAt`;
`Customer.CreatedAt/.UpdatedAt`; `Product.CreatedAt/.UpdatedAt/.ArchivedAt`;
`ProductFamily.CreatedAt/.UpdatedAt/.ArchivedAt`; `Component.CreatedAt/.UpdatedAt/.ArchivedAt`;
`HistoricUsage.BillingPeriodStartsAt/.BillingPeriodEndsAt`; `AutoResume.AutomaticallyResumeAt`;
`CancellationOptions.ScheduledCancellationAt`; `SubscriptionMigrationPreviewOptions.ProrationDate`.
Query-parameter date arguments are `DateTimeOffset?` on `ListSubscriptions`, `ListUsages`, `ListProducts*`,
but **`string?`** on `ListCustomers` and `ListComponents*` — check the signature per operation.
*(sources: the records pages + the operation pages cited above.)*

### 2.5 Client construction, auth, servers, base URL

```csharp
// Infrastructure/Billing/MaxioBillingClient.cs
using MaxioAdvancedBilling;                                   // client, options, ServerOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;         // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                           // ServerEnvironment

public sealed class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _maxio;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)  // typed client
    {
        var o = options.Value;
        var sdkOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth   = new BasicAuthCredentials { Username = o.ApiKey, Password = "x" },
            Environment = o.Region == "EU" ? ServerEnvironment.Eu : ServerEnvironment.Us,
        };

        if (!string.IsNullOrWhiteSpace(o.BaseUrl))            // explicit override wins
        {
            sdkOptions.Server.Production.Us.BaseUrl = o.BaseUrl;
            sdkOptions.Server.Production.Eu.BaseUrl = o.BaseUrl;
        }
        else                                                   // derive from subdomain + region
        {
            sdkOptions.Server.Production.Us.Site = o.SiteSubdomain;   // "apimatic-hackathon"
            sdkOptions.Server.Production.Eu.Site = o.SiteSubdomain;
        }

        _maxio = new MaxioAdvancedBillingClient(httpClient, sdkOptions);
    }
}
```

| Fact | Value | Source |
|---|---|---|
| Only constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `MaxioAdvancedBillingClient.cs` line 39 |
| Options properties | `Environment: ServerEnvironment` (default `ServerEnvironment.Us`), `Retry: RetryOptions` (default `RetryOptions.Default()`), `Server: ServerOptions` (default `new()`), `BasicAuth: BasicAuthCredentials?` (default `null`) | `MaxioAdvancedBillingClientOptions.cs` |
| Auth | **HTTP Basic**: `Username` = **your Maxio API key**, `Password` = the literal `"x"`. Set it on the options **before** constructing the client. There is no other scheme. | sdk-map.md · `MaxioAdvancedBillingClientOptions.cs` (XML doc: *"The `username` is a Maxio Chargify API key. The `password` is `x`."*) |
| Site subdomain | **Not** a top-level option — it is the `{site}` template variable: `options.Server.Production.Us.Site` (default the literal string `"subdomain"`). Set it to `"apimatic-hackathon"`. | `Servers/ProductionOptions.cs` lines 16-26 |
| **Arbitrary base URL — YES, supported** | `options.Server.Production.Us.BaseUrl` (default `"https://{site}.chargify.com"`) and `.Eu.BaseUrl` (default `"https://{site}.ebilling.maxio.com"`) are plain settable `string` properties on `ProductionOptions.UsOptions`/`EuOptions`. Assign any absolute URL — with or without the `{site}` placeholder. The map states this explicitly for mock/dev hosts (`options.Server.Production.Us.BaseUrl = "http://localhost:8080"`). | sdk-map.md *Servers & auth* · `Servers/ProductionOptions.cs` |
| Interaction with the Environment enum | `ProductionOptions.Resolve` **branches on `options.Environment`**: `Us` ⇒ `Us.BaseUrl`+`Us.Site`, `Eu` ⇒ `Eu.BaseUrl`+`Eu.Site`. An override written only to `.Us` is **ignored** when `Environment = Eu`. Set both sides (as above) or set the side matching the region. `ServerEnvironment` is a `StringEnum` record with `Us`("US") / `Eu`("EU") and `Default() => Us`. | `Servers/ProductionOptions.cs` lines 10-14 · `Servers/ServerEnvironment.cs` |
| `HttpClient.BaseAddress` | **Not the mechanism.** The SDK resolves each request URL from `options.Server` into a `UrlTemplate(BaseUrl, Path, Variables)` and hands absolute URLs to the `HttpClient`; the injected client's `BaseAddress` plays no part in routing. Configure the base URL through `options.Server`. | `Server.cs`, `Servers/ProductionOptions.cs`, `Core/Models/UrlTemplate.cs`, `MaxioAdvancedBillingClient.cs` |
| Second server group | `Ebb` (`https://events.chargify.com/{site}`) — used **only** by the events-ingest ops `RecordEvent`/`BulkRecordEvents`, which are **out of scope**. Ignore `options.Server.Ebb`. | sdk-map.md · `operations/SubscriptionComponents.md` |
| Target for this task | `Maxio:SiteSubdomain = "apimatic-hackathon"`, `Maxio:Region = "US"` ⇒ effective base `https://apimatic-hackathon.chargify.com` | derived from `Servers/ProductionOptions.cs` default template |
| **Test seam — confirmed** | The `HttpClient` constructor argument *is* the seam. `new MaxioBillingClient(new HttpClient(stubHandler), options)` needs no DI and no SDK mocking helpers; the SDK ships none. Assert the outgoing request off `StubHandler.LastRequest` (method, `RequestUri.AbsolutePath`, snake_case query, serialized JSON body). | `MaxioAdvancedBillingClient.cs` (ctor) + `dotnet-testing` |
| NuGet | `dotnet add package AsadAli.AdvancedBilling.Sdk` — **version: see GAP G4** | sdk-map.md · `MaxioAdvancedBilling.csproj` |
| Root namespace ≠ package id | package `AsadAli.AdvancedBilling.Sdk`, `using MaxioAdvancedBilling;` | sdk-map.md |
| TFM | SDK targets `netstandard2.0` — consumable from net8.0. Transitive deps: `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents`. | `MaxioAdvancedBilling.csproj` |

### 2.6 Error handling — the one catch pattern

**Structural facts (source-verified, and they constrain the design):**

- `public sealed class SdkException<TError> : Exception` — **the only member is `public required TError Error`.**
  There is **no** non-generic `SdkException` base type and **no** `StatusCode` property on the exception, so
  **a single `catch (SdkException) `across all operations is impossible**; the only common base is
  `System.Exception`. (source: `Core/Exceptions/SdkException.cs`, 8 lines, read in full)
- `SdkException` never sets `Message` ⇒ `ex.Message` is the useless framework default
  (*"Exception of type 'MaxioAdvancedBilling.Core.Exceptions.SdkException`1[…]' was thrown."*).
  **Never surface `ex.Message` to a user or a log as the API error.** (same source)
- `abstract class ApiError` exposes exactly one public member: `bool TryGetRawError(out RawError error)`.
  (source: `Core/ErrorResponse/ApiError.cs` line 19)
- **`TryGetRawError` is not a catch-all.** In every generated Case-A error, the typed branch is constructed
  with `fallback: default`, so when the typed accessor matches (e.g. 422) `TryGetRawError` returns **false** —
  and there is then **no status code anywhere on the exception**. Take the status from the map row
  (the accessor's documented status) in that branch. (source read in full: `Errors/CreateCustomerError.cs`
  lines 20-34 — `AsCustomerErrorResponse1` passes `default` as the fallback)
- `RawError` members: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`,
  `ReadAsBytes(): ReadOnlyMemory<byte>`. `ReadAsJson<T>()` **throws `JsonException`** on a non-JSON body —
  prefer `ReadAsString()`. (sdk-map.md core-error table)
- **No `{Operation}Result` / `ApiResult` no-throw variants exist anywhere in this SDK** — every call throws.
  (sdk-map.md: *"No-throw variants: absent across this SDK"*, repeated per operation row)

**Recommended shape — one private helper per error case, called from each operation's own `catch`:**

```csharp
using MaxioAdvancedBilling.Core.Exceptions;      // SdkException<TError>
using MaxioAdvancedBilling.Core.ErrorResponse;   // ApiError, RawError
using MaxioAdvancedBilling.Errors;               // CreateCustomerError, CreateSubscriptionError, …

// Case B (RawError) — shared, safe to factor out: RawError carries its own status.
private static BillingException FromRaw(RawError raw, string op) =>
    new(op, (int)raw.StatusCode, Safe(() => raw.ReadAsString()) ?? "(no body)");

// Case A — MUST live inside the per-operation catch: the typed TryGet* accessors are declared on the
// concrete {Operation}Error, not on ApiError. A helper typed as ApiError can only reach TryGetRawError.
try
{
    var resp = await _maxio.Customers.CreateCustomer(body, ct);
    return Map(resp.Customer);
}
catch (SdkException<CreateCustomerError> ex)
{
    if (ex.Error.TryGetCustomerErrorResponse1(out var e422))   // 422 — status is 422 by construction
        throw new BillingException("CreateCustomer", 422, DescribeCustomerError(e422));
    if (ex.Error.TryGetRawError(out var raw))                  // ALWAYS LAST: other statuses only
        throw FromRaw(raw, "CreateCustomer");
    throw new BillingException("CreateCustomer", 0, "Maxio returned an unrecognised error.");
}
catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "CreateCustomer"); }   // defensive
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    throw new BillingException("CreateCustomer", 0, "Maxio is unreachable.", ex);  // transport, NOT SdkException
}
```

**Per-operation catch types — the compiler enforces these, so copy them exactly:**

| Operation | catch | Accessors (status) | Human message from |
|---|---|---|---|
| `ListProductsForProductFamily` | `SdkException<ListProductsForProductFamilyError>` | `TryGetString(out string)` [404] · `TryGetRawError` | the `string` itself |
| `ListProductFamilies`, `ReadProductFamily`, `ReadProduct`, `ReadProductByHandle`, `ReadCustomer`, `ReadCustomerByReference`, `ListCustomers`, `ListCustomerSubscriptions`, `ListSubscriptions`, `ReadSubscription`, `ListUsages`, `ListComponents*`, `ReadComponent`, `FindComponent` | `SdkException<RawError>` | — (**no accessors**) | `ex.Error.ReadAsString()` |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1` [422] · `TryGetRawError` | `CustomerErrorResponse1.Errors` — **see T6** |
| `UpdateCustomer` | `SdkException<UpdateCustomerError>` | `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1` [422] · `TryGetRawError` | as above |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `UpdateSubscription` | `SdkException<UpdateSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `CreateUsage` | `SdkException<CreateUsageError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `PreviewSubscriptionProductMigration` | `SdkException<PreviewSubscriptionProductMigrationError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `MigrateSubscriptionProduct` | `SdkException<MigrateSubscriptionProductError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `PauseSubscription` | `SdkException<PauseSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `UpdateAutomaticSubscriptionResumption` | `SdkException<UpdateAutomaticSubscriptionResumptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `ResumeSubscription` | `SdkException<ResumeSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `CancelSubscription` | `SdkException<CancelSubscriptionApiError>` | `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse` [422] · `TryGetRawError` | union: `TryGetErrorListResponse1` → join, else `TryGetSingleErrorResponse1` → `.Error` |
| `InitiateDelayedCancellation` | `SdkException<InitiateDelayedCancellationError>` | `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |
| `CancelDelayedCancellation` | `SdkException<CancelDelayedCancellationError>` | `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | status only |
| `ReactivateSubscription` | `SdkException<ReactivateSubscriptionError>` | `TryGetErrorListResponse1` [422] · `TryGetRawError` | `string.Join("; ", e.Errors)` |

**Status-code extraction, both cases:**
Case B → `(int)ex.Error.StatusCode`. Case A → the branch that fired tells you the status (the table above gives
it); only the `TryGetRawError` branch carries a live `RawError.StatusCode`. When no accessor fires, record
status `0` and a generic message — never `ex.Message`.

---

## 3. Trap notes (attached to the step where they bite)

- **T1 (steps 5, 6, 9, 10) — named arguments are mandatory on the list ops.** `ListSubscriptions` (14),
  `ListProductsForProductFamily` (8), `ListComponentsForProductFamily` (7), `ListUsages` (4),
  `ListProductFamilies` (5) all have runs of same-shaped nullable parameters with **no C# defaults**; a
  positional call compiles and mis-binds. Always write `state:`, `filter:`, `sinceDate:`, `perPage:`, `ct:`.
- **T2 (every call) — the token parameter is literally `ct`.** `ct: cancellationToken`, never
  `cancellationToken:`.
- **T3 (steps 4, 6, 8, 9, 12, 13) — every write takes a single-field envelope.** `CreateCustomerRequest{Customer}`,
  `CreateSubscriptionRequest{Subscription}`, `UpdateSubscriptionRequest{Subscription}`,
  `CreateUsageRequest{Usage}`, `CancellationRequest{Subscription}`, `SubscriptionProductMigrationRequest{Migration}`,
  `SubscriptionMigrationPreviewRequest{Migration}`, `PauseRequest{Hold}`. Responses unwrap symmetrically.
  Forgetting the envelope compiles nowhere — but forgetting to *unwrap* is the common review miss.
- **T4 (steps 4, 6, 7) — enums are `StringEnum<T>` records, not C# enums.** `switch` on them with `==`
  comparisons against the static members (`state == SubscriptionState.Active`) or `.ToString()`/`FromValue`;
  they will not work in a C# `switch` over enum constants and have no `[Flags]`/numeric behaviour. An unknown
  wire value round-trips rather than throwing, so treat "not one of the members I know" as a real case.
- **T5 (all steps) — `TryGetRawError` last, and false on typed statuses.** See §2.6; do not open a catch block
  with `TryGetRawError`.
- **T6 (step 6) — `CustomerErrorResponse1` is a suspicious shared model, trust it only defensively.** The map
  gives `CustomerErrorResponse1.Errors (errors): Errors?` where the `Errors` record declares exactly
  `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`
  (`records-2-Cr-Ne.md`) — pagination/price-point fields that cannot plausibly carry a "reference has already
  been taken" message. The same page also carries an unused `CustomerError { Customer (customer): string? }`,
  i.e. two generated definitions that disagree about the customer error shape. **Directive:** in the 422 branch
  extract best-effort — concatenate `e422.Errors?.PerPage` and `e422.Errors?.PricePoint` if non-empty, else fall
  back to the generic message `"Maxio rejected the customer (HTTP 422) — a customer with this reference may
  already exist."`, and never dereference without null checks. Whether the live 422 body actually matches this
  generated model is **UNVERIFIED** — only live traffic can settle it. Corollary for the subscribe flow: do not
  *depend* on parsing the 422 to detect duplicates — **always `ReadCustomerByReference` first** and treat the
  422 purely as a race-condition fallback (on 422, re-read by reference and use the existing customer).
- **T7 (steps 4, 6, 8, 12, 13) — retries do not cover your writes.** `RetryOptions` retries idempotent methods
  only (`GET/HEAD/PUT/OPTIONS`) by default, so `POST`/`DELETE` (`CreateCustomer`, `CreateSubscription`,
  `CreateUsage`, `PauseSubscription`, `InitiateDelayedCancellation`, `CancelSubscription`,
  `MigrateSubscriptionProduct`) surface the first failure. Do not add `POST` to `HttpMethodsToRetry` for
  `CreateSubscription`/`CreateUsage` — they are not idempotent and a retry double-charges. `RetryOptions.Timeout`
  is **per attempt**, not total; all its members are `required`, so start from `RetryOptions.Default()`.
- **T8 (step 3) — one long-lived `HttpClient`.** Register the typed client
  (`services.AddHttpClient<MaxioBillingClient>()`); never `new HttpClient()` per request. The
  `MaxioAdvancedBillingClient` wrapper itself may be per-instance/transient.
- **T9 (steps 4-10) — guard reads as well as writes.** The startup-validation reads and the plans-page read run
  automatically; a `HttpRequestException`/`TaskCanceledException` there is not an `SdkException` and will escape
  a Maxio-only catch. Every call site gets the transport catch from §2.6.
- **T10 (step 8) — usage addressing.** When addressing a component by handle in a **path**
  (`CreateUsage`/`ListUsages`/`ReadComponent`/`UpdateComponent`) the value must be prefixed: `"handle:api-calls"`.
  When addressing it via a **query lookup** (`FindComponent(handle)`) or a product by handle
  (`ReadProductByHandle(apiHandle)`), pass the bare handle. Getting this backwards yields a 404.
- **T11 (step 9) — `ListUsages` paginates manually** (`page`/`perPage`, default 20). Sum across pages or you
  will silently under-report the period total. There is no auto-pagination helper on this operation.
- **T12 (step 6) — unmodeled JSON is dropped on deserialize.** Anything Maxio returns that the generated record
  does not declare is silently discarded; do not plan on reading a field that is not in the tables above.

---

## 4. GAPS — call these out, do not invent behaviour

- **G1 — `ReadProductFamily` cannot take a handle.** The endpoint doc says a family may be addressed as
  `handle:my-family`, but the generated signature is `ReadProductFamily(int id, …)` — a `string` handle is not
  expressible (`operations/ProductFamilies.md`). **Workaround:** `ListProductFamilies(null, null, null, null, null, ct: ct)`
  and match `.ProductFamily.Handle` client-side to obtain the numeric id (needed anyway for
  `ListComponentsForProductFamily(int productFamilyId, …)`), then `ReadProductFamily(id, ct)` to verify.
  Alternatively `ListProductsForProductFamily("handle:my-family", …)` accepts the handle form (its param is
  `string`) and a 404 there proves the family is missing.
- **G2 — `ListSubscriptions` has no customer filter.** Its parameters are state/product/pricepoint/coupon/date/
  metadata/sort/include only — there is **no `customer_id` / `customer_reference`** (`operations/Subscriptions.md`).
  **Use `ListCustomerSubscriptions(customerId, ct)`** (which in turn has no state filter — filter
  `.Subscription.State` in memory). Combining "by customer **and** by state" server-side is not exposed.
- **G3 — no "period-to-date usage total" endpoint.** `ListUsages` returns individual usage records only; the
  running total must be summed client-side (bounded by `sinceDate: currentPeriodStartedAt`). The nearest
  server-side aggregates are `SubscriptionComponent.UnitBalance (unit_balance): int?` from
  `ReadSubscriptionComponent(int subscriptionId, int componentId, ct)` — **note it takes `int componentId`, no
  handle form** — and `SubscriptionComponent.HistoricUsages: IReadOnlyList<HistoricUsage>?`
  (`TotalUsageQuantity: double?`, `BillingPeriodStartsAt/EndsAt: DateTimeOffset?`), returned by
  `ListSubscriptionComponents` only when `include: [ListSubscriptionComponentsInclude.HistoricUsages]` is passed.
- **G4 — package version is ambiguous.** The map's source stamp says commit `15db14b`, **tagged `v1.0.2`**
  (sdk-map.md), but `MaxioAdvancedBilling.csproj` at that same ref declares `<Version>1.0.0</Version>`. The two
  disagree and neither is the NuGet feed. **Reference `AsadAli.AdvancedBilling.Sdk` version `1.0.2`** (matching
  the tag the map was generated from) and, if restore fails, fall back to `1.0.0`; pin explicitly either way.
  Which version the feed actually serves is **UNVERIFIED** from map or source.
- **Not a gap, but note:** there is **no** dedicated "apply plan change with proration at a chosen date and
  commit" op beyond `MigrateSubscriptionProduct`; `ProrationDate` exists only on the **preview** options
  (`SubscriptionMigrationPreviewOptions`), not on `SubscriptionProductMigration`. A future-dated commit is not
  exposed.

---

## 5. Assumptions & Blockers

**Assumptions**

1. The eShopOnWeb identity used as the Maxio customer `reference` is stable and unique per user (email or
   username). `CreateCustomer.Reference` is optional in the model — the uniqueness constraint is server-side, so
   the plan makes `ReadCustomerByReference`-then-create the primary idempotency mechanism (see T6).
2. `CreateCustomer` requires `FirstName`, `LastName`, `Email` as C# `required` members; eShopOnWeb users may only
   have an email. The plan assumes the implementer supplies a deterministic placeholder (e.g. split the email
   local part, or `"eShop"` / `"Customer"`) rather than leaving them unset — the initializer will not compile
   otherwise.
3. "Plans page price" is taken from `Product.PriceInCents` (the product's default price point). If the site uses
   non-default price points per plan, `ProductPricePoints` operations (out of scope here) would be needed.
4. Region → environment mapping is `US → ServerEnvironment.Us`, `EU → ServerEnvironment.Eu`; an unrecognised
   `Maxio:Region` value falls back to `Us` (the SDK's own `ServerEnvironment.Default()`).
5. The metered component is addressed by handle from configuration; startup validation resolves it via
   `FindComponent`/`ReadComponent` and refuses to start unless `Kind == ComponentKind.MeteredComponent`.
6. Detecting "already has an active subscription" means `State` in {`Active`, `Trialing`} on
   `ListCustomerSubscriptions`; the exact set is a product decision, not an SDK fact.

**Blockers**

- **UC0 (live sandbox):** the seeded products on site `cp-exp-1` / family `eshop-subscribe` have "requires payment
  method" **ON**, so `CreateSubscription` with no payment fields returns 422 *"No payment method was on file for
  the $299.00 balance"*. This is a seed/spec mismatch, not an SDK limitation — remediation contract in §6.
- One fact is unverifiable from map or source and is labelled in place: the live wire shape of
  the `CreateCustomer`/`UpdateCustomer` 422 body versus the generated `CustomerErrorResponse1`/`Errors` model
  (**UNVERIFIED** — see T6; handled by the defensive extract-else-generic directive). The published NuGet version
  (G4) is likewise unverifiable from the clone.

---

## 6. Addendum — product "requires payment method" (UC0 remediation) & subscribing without a card

| # | Controller · signature (verbatim) | Request model construction | Response → fields read | Error case + accessors | Map page |
|---|---|---|---|---|---|
| 14a | **Update in place** — `client.Products.UpdateProduct(int productId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `PUT /products/{product_id}.json`; `body` nullable, no default → **pass explicitly**. `productId` is `int` only (resolve the handle with `ReadProductByHandle` first). | `new CreateOrUpdateProductRequest { Product = new CreateOrUpdateProduct { Name = …, Description = …, PriceInCents = …, Interval = …, IntervalUnit = …, RequireCreditCard = false } }`. `CreateOrUpdateProduct`: `Name (name): string !req`, `Handle (handle): string?`, `Description (description): string !req`, `AccountingCode: string?`, **`RequireCreditCard (require_credit_card): bool?`** ← *this is the "requires payment method" flag; no declared default*, `PriceInCents (price_in_cents): long !req` (**cents**), `Interval (interval): int !req`, `IntervalUnit (interval_unit): IntervalUnit !req`, `TrialPriceInCents: long?`, `TrialInterval: int?`, `TrialIntervalUnit: IntervalUnit?`, `TrialType: TrialType?`, `ExpirationInterval: int?`, `ExpirationIntervalUnit: ExpirationIntervalUnit?`, **`AutoCreateSignupPage (auto_create_signup_page): bool?`** (unrelated to payment; creates a public signup page), `TaxCode: string?`. **Five fields are C# `required`** ⇒ every update is a full replace: `ReadProductByHandle` first and echo the current `Name`/`Description`/`PriceInCents`/`Interval`/`IntervalUnit` back. | `ProductResponse.Product` → verify `RequireCreditCard (require_credit_card): bool?` is now `false` | **Case A** `SdkException<UpdateProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/Products.md`, `records-1-Ac-Cr.md`, `records-3-Of-Su.md` |
| 14b | **Create a card-free variant** — `client.Products.CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `POST /product_families/{product_family_id}/products.json`; `body` **pass explicitly** | same `CreateOrUpdateProductRequest` as 14a. `productFamilyId` is a **`string`** ⇒ `"handle:eshop-subscribe"` is accepted (unlike `ReadProductFamily`, see G1). | `ProductResponse.Product` → `.Id`, `.Handle` | **Case A** `SdkException<CreateProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/Products.md`, `records-1-Ac-Cr.md` |

- **T13 — `UpdateProduct` mints a new default price point.** Map operation note, verbatim: *"Updating a product
  using this endpoint will create a new price point and set it as the default price point for this product. If
  you should like to update an existing product price point, that must be done separately."* Combined with the
  five `required` fields, a careless call silently reprices the plan. Existing subscribers keep their old price
  point; new subscriptions get the new one. If that side effect is unacceptable, use 14b to create
  e.g. `eshop-pro-nocard` and repoint `Maxio:ProductHandle`.
- **T14 — `request_credit_card` is response-only.** The response record `Product` carries **both**
  `RequestCreditCard (request_credit_card): bool?` and `RequireCreditCard (require_credit_card): bool?`, but the
  request record `CreateOrUpdateProduct` declares **only** `require_credit_card`. `request_credit_card` cannot be
  set through this SDK — assert the seed fix on `RequireCreditCard`.
- **Subscribing without a card — what is and is not grounded.** `CreateSubscription.PaymentCollectionMethod`
  (wire `payment_collection_method`) is `CollectionMethod?`; members `CollectionMethod.Automatic (automatic)`,
  `.Remittance (remittance)`, `.Prepaid (prepaid)`, `.Invoice (invoice)`. The only documentation attached to it
  (both `enums.md` and the property's XML doc in `Models/CreateSubscription.cs`) is an architecture-compatibility
  note — *"For legacy Statements Architecture valid options are `invoice`, `automatic`. For current Relationship
  Invoicing Architecture valid options are `remittance`, `automatic`, `prepaid`."* **Neither the map nor the
  source states that a non-`automatic` collection method permits creation without a payment profile, or that the
  balance becomes an open invoice — that behaviour is UNVERIFIED**; only live traffic can confirm it. What the
  SDK *does* say (`CreateSubscription` operation note) is that *"Payment information may be required to create a
  subscription, **depending on the options for the Product being subscribed**"* — i.e. the product flag in 14a is
  the grounded lever. Two other request fields bear on this, per their XML docs: `DeferSignup (defer_signup):
  bool? = false` — *"Set this attribute to true to create the subscription in the Awaiting Signup Date state…
  You can omit the `initial_billing_at` date to activate the subscription immediately"* (defers billing; the
  subscription lands in `SubscriptionState.AwaitingSignup`), and `CustomPrice (custom_price):
  SubscriptionCustomPrice?` — a subscription-unique price point (`PriceInCents !req` union, `Interval !req`
  union, `IntervalUnit !req`), where `PriceInCents = 0` removes the balance itself, though the server's reaction
  is likewise UNVERIFIED. There is **no** `skip_payment_method`/`require_credit_card` override and no test-gateway
  switch on `CreateSubscription`.
