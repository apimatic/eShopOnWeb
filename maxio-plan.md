# Maxio Advanced Billing .NET SDK — Integration Plan (eShopOnWeb)

Target: eShopOnWeb (.NET 8, Clean Architecture — ApplicationCore / Infrastructure / Web / PublicApi).
Maxio sandbox site `apimatic-hackathon`. Product family `eshop-subscribe` (id `3008866`); products
`eshop-pro` (id `7111477`), `basic-plan` (id `7111478`); metered component `api-call` (id `3033795`).

SDK: `AsadAli.AdvancedBilling.Sdk` (namespace `MaxioAdvancedBilling`), client `MaxioAdvancedBillingClient`,
options `MaxioAdvancedBillingClientOptions`. Grounded from `sdk-map.md` and `map/operations/*.md` +
`map/models/*.md` in the `maxio-getting-started` skill (source commit `15db14b`, tag `v1.0.2`), and from
the session's SDK source clone (see Session artifacts) for facts the map doesn't carry verbatim.

## 1. Scope & sequence

1. **Client & DI wiring** (ApplicationCore defines an `IMaxioClient`-shaped abstraction if desired;
   Infrastructure registers the real SDK client). Uses: `MaxioAdvancedBillingClientOptions`,
   `AddMaxioAdvancedBillingClient`, `BasicAuthCredentials`, `options.Server.Production.{Us|Eu}.BaseUrl`.
2. **Browse plans / startup validation** — `client.ProductFamilies.ReadProductFamily` or
   `ListProductsForProductFamily`, `client.Products.ReadProductByHandle`/`ReadProduct`,
   `client.Components.FindComponent`/`ReadComponent` to confirm `api-call` resolves and
   `Kind == ComponentKind.MeteredComponent`.
3. **Find-or-create customer** — `client.Customers.ReadCustomerByReference` (404 → not found) then
   `client.Customers.CreateCustomer`.
4. **Create/idempotent-subscribe** — `client.Customers.ListCustomerSubscriptions` (check for an existing
   active subscription to the target product) then `client.Subscriptions.CreateSubscription`.
5. **List "my subscriptions"** — `client.Customers.ListCustomerSubscriptions`.
6. **Usage recording + read-back** — `client.SubscriptionComponents.CreateUsage`,
   `client.SubscriptionComponents.ReadSubscriptionComponent` (and/or `ListUsages`).
7. **Plan-change preview/commit** — `client.SubscriptionProducts.PreviewSubscriptionProductMigration`,
   `client.SubscriptionProducts.MigrateSubscriptionProduct`.
8. **Lifecycle** — `client.Subscriptions.ReadSubscription` (state check) +
   `client.SubscriptionStatus.{PauseSubscription, ResumeSubscription, CancelSubscription,
   ReactivateSubscription}`.
9. **Error handling** — throw-only `SdkException<TError>` model, Case A/B per operation (see sheet).
10. **Auth/base-URL** — Basic auth + explicit `Server.*.BaseUrl` override (wins over subdomain `Site`).

---

## 2. CONTRACT SHEET

### Item 1 — Product family / products / components (browse + startup validation)

| Op (`client.X.Y`) | Signature | Request model | Response envelope (read this field) | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse.ProductFamily` (`ProductFamily?`) | Case B `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass all nullable filter params explicitly (named args) | — | `IReadOnlyList<ProductResponse>` — each `.Product` (`Product !req`) | Case A `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] → `TryGetRawError` | manual `page`+`perPage` | `operations/ProductFamilies.md` |
| `Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse.Product` | Case B `SdkException<RawError>` | none | `operations/Products.md` |
| `Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse.Product` | Case B | none | `operations/Products.md` |
| `Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` — **use for `api-call` by handle** | — | `ComponentResponse.Component` (`Component !req`) | Case B `SdkException<RawError>` | none | `operations/Components.md` |
| `Components.ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | — | `ComponentResponse.Component` | Case B | none | `operations/Components.md` |

**Model shapes needed:**
- `ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, … (`records/records-3-Pa-Su.md:33`).
- `Product`: `Id (id): int?`, `Handle (handle): string?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `PriceInCents (price_in_cents): long?`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointId (product_price_point_id): int?`, plus many more (`records-3-Pa-Su.md:32`). **`RequireCreditCard`/`RequestCreditCard` are the fields to check for "requires-payment-method off" at startup** (item 3 depends on this).
- `Component`: `Id (id): int?`, `Handle (handle): string?`, `Kind (kind): ComponentKind?`, `ProductFamilyId (product_family_id): int?`, `Archived (archived): bool?`, … (`records-1-Ac-Cr.md:75`).
- Enum `ComponentKind` (StringEnum) values: `metered_component`, `quantity_based_component`, `on_off_component`, `prepaid_usage_component`, `event_based_component` (`models/enums.md:22`). **Startup validation asserts `Component.Kind == ComponentKind.MeteredComponent`** (exact C# constant name source-confirmed — see §5 Q6 below).

### Item 2 — Find-or-create Customer (idempotent, keyed by reference = email/username)

| Op | Signature | Request model | Response | Error | Map page |
|---|---|---|---|---|---|
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — | `CustomerResponse.Customer` (`Customer !req`) | Case B `SdkException<RawError>` — a **404** (no such reference) is a normal "not found," not a real transport error: check `ex.Error.StatusCode == HttpStatusCode.NotFound` in the catch, else rethrow | `operations/Customers.md` |
| `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — body must be passed explicitly | `CreateCustomerRequest.Customer` (`CreateCustomer !req`) — fields: `FirstName !req`, `LastName !req`, `Email !req`, `Reference (reference): string?` (**set this to the eShopOnWeb user's email/username**), `Organization`, `Address*`, `Phone`, `Locale`, `VatNumber`, `TaxExempt(Reason)`, `ParentId`, `SalesforceId` (all optional) | `CustomerResponse.Customer` | Case A `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] → `TryGetRawError` | `operations/Customers.md` |

**Idempotent pattern:** try `ReadCustomerByReference(reference: email)`; on 404 (`RawError.StatusCode == 404`), `CreateCustomer` with `Customer.Reference = email`; on any other status, rethrow.

**CONFIRMED from source (`Models/Errors.cs`, `Models/CustomerErrorResponse1.cs` — was flagged `SOURCE-LOOKUP NEEDED`, now resolved):** `CustomerErrorResponse1.Errors` (`Errors?`) genuinely only exposes `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` — **it carries no customer-specific keys at all** (no email/reference message fields). This is confirmed as-is in source, not a map omission. **Do not rely on `TryGetCustomerErrorResponse1` for a human-readable duplicate-reference/invalid-email message** — on 422, prefer falling through to `TryGetRawError(out raw)` and `raw.ReadAsString()`/`raw.ReadAsJson<JsonElement>()` for the actual validation message, or treat `TryGetCustomerErrorResponse1` as effectively unused for this integration's error UI.

### Item 3 — Create Subscription (no card capture), idempotent-subscribe

| Op | Signature | Request model | Response | Error | Map page |
|---|---|---|---|---|---|
| `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription` (`CreateSubscription !req`) — relevant fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?` (supply **one** of these), `CustomerId (customer_id): int?` (or `CustomerReference (customer_reference): string?`), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Reference (reference): string?`. **No dedicated "requires-payment-method" flag on the request** — omit `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` entirely; this only succeeds without a card if the target product itself is configured with `RequireCreditCard = false` (verify via item 1's `ReadProduct`/`ReadProductByHandle` at startup). | `SubscriptionResponse.Subscription` (`Subscription?`) | Case A `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`.Errors: IReadOnlyList<string> !req`) → `TryGetRawError` | `operations/Subscriptions.md` |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — **use to detect an already-active subscription before creating** | — | `IReadOnlyList<SubscriptionResponse>` | Case B `SdkException<RawError>` | `operations/Customers.md` |

**Idempotent-subscribe pattern:** `ListCustomerSubscriptions(customerId)`, filter for an entry whose `Subscription.Product?.Id`/`Handle` matches the target product **and** `Subscription.State` is in an "already effectively subscribed" set (uses `active`, `trialing`, `assessing`, `past_due`, `soft_failure`, `unpaid`, `on_hold` as "already active/consider don't-duplicate"; treat `canceled`/`expired`/`suspended` as re-subscribable). If found, return it; else `CreateSubscription`.

**Enum `SubscriptionState`** (StringEnum, `models/enums.md:96`): `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`. C# constant names source-confirmed in §5 Q6.

**Enum `CollectionMethod`** (`models/enums.md:21`): `automatic`, `remittance`, `prepaid`, `invoice`. C# constants: `.Automatic`/`.Remittance`/`.Prepaid`/`.Invoice` (confirmed, §5 Q6).

### Item 4 — List a customer's subscriptions

Same op as above table: `Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` → `IReadOnlyList<SubscriptionResponse>`, each `.Subscription` carrying `State`, `Product`, `CurrentPeriodEndsAt`, `BalanceInCents`, etc. Full `Subscription` field list is in §5 Q1 below (source-confirmed this batch). Case B `SdkException<RawError>`. No pagination (returns full list).

### Item 5 — Record usage + read back period-to-date total

| Op | Signature | Request model | Response | Error | Map page |
|---|---|---|---|---|---|
| `SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` | `CreateUsageRequest.Usage` (`CreateUsage !req`) — `Quantity (quantity): double?`, `Memo (memo): string?` (optional memo), `PricePointId`, `BillingSchedule`, `CustomPrice` (all optional) | `UsageResponse.Usage` (`Usage !req`) — `Id`, `Memo`, `CreatedAt`, `Quantity (quantity): Quantity1?`, `ComponentId`, `SubscriptionId` | Case A `SdkException<CreateUsageError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `TryGetRawError` | `operations/SubscriptionComponents.md` |
| `SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — **read-back for period-to-date total** | — | `SubscriptionComponentResponse.Component` (`SubscriptionComponent?`) — `UnitBalance (unit_balance): int?` | Case A `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent(out RawError)` [404] → `TryGetRawError` | `operations/SubscriptionComponents.md` |
| `SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — fallback if you need raw usage events rather than the balance field | — | `IReadOnlyList<UsageResponse>` | Case B `SdkException<RawError>` | manual `page`+`perPage` | `operations/SubscriptionComponents.md` |

**Union construction — source-confirmed (§5 Q8):** `SubscriptionIdOrReference.Int(subscriptionId)` / `ComponentIdModel.Int(componentId)`, or rely on the implicit `int`→union conversion operator (`SubscriptionIdOrReference subRef = subscriptionId;`). `ReadSubscriptionComponent`/`ListAllocations` take plain `int` params instead (no union) — don't mix the two signatures up.

**RESOLVED (was `SOURCE-LOOKUP NEEDED`) — `Models/SubscriptionComponent.cs` confirmed:** `UnitBalance` (`public int? UnitBalance { get; init; }`) carries **no XML doc-comment at all** in source — its exact semantics (period-to-date usage vs. remaining allowance) are genuinely undocumented at the source level, not just missing from the map. **Recommendation stands and is now confirmed necessary:** don't rely on `UnitBalance`'s name alone for "period-to-date usage" — cross-check by summing `ListUsages` entries within the current period window (`Subscription.CurrentPeriodStartedAt`..`CurrentPeriodEndsAt`) if the UI needs to be authoritative, or verify `UnitBalance`'s behavior empirically against the sandbox before shipping the number as-is.

### Item 6 — Preview + commit plan change (proration)

| Op | Signature | Request model | Response | Error | Map page |
|---|---|---|---|---|---|
| `SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` | `SubscriptionMigrationPreviewRequest.Migration` (`SubscriptionMigrationPreviewOptions !req`) — `ProductId (product_id): int?` / `ProductHandle (product_handle): string?` (target product — supply one), `ProductPricePointId`/`ProductPricePointHandle` (optional), `IncludeTrial`, `IncludeInitialCharge`, `IncludeCoupons`, `PreservePeriod (preserve_period): bool?`, `Proration (proration): Proration?`, `ProrationDate (proration_date): DateTimeOffset?` | `SubscriptionMigrationPreviewResponse.Migration` (`SubscriptionMigrationPreview !req`) — **`ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents`** — this is the proration-amount payload the UI reads | Case A `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] → `TryGetRawError` | `operations/SubscriptionProducts.md` |
| `SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — **commits the change** | `SubscriptionProductMigrationRequest.Migration` (`SubscriptionProductMigration !req`) — `ProductId`/`ProductHandle`, `ProductPricePointId`/`ProductPricePointHandle`, `IncludeTrial` (default `false`), `IncludeInitialCharge` (default `false`), `IncludeCoupons` (default `true`), **`PreservePeriod (preserve_period): bool?` (default `false`)**, `Proration (proration): Proration?` (`Proration.PreservePeriod: bool?` — doc-commented as *"the alternative to sending preserve_period as a direct attribute to migration"*, i.e. it's the **same setting**, sent nested instead of top-level — set exactly one of the two, not both) | `SubscriptionResponse.Subscription` | Case A `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] → `TryGetRawError` | `operations/SubscriptionProducts.md` |

**GAP CONFIRMED (was `SOURCE-LOOKUP NEEDED`, now resolved from `Models/SubscriptionProductMigration.cs`'s XML doc-comment) — "commit at next renewal without proration" is genuinely NOT supported by this endpoint.** The doc-comment on `PreservePeriod` reads verbatim: *"If `false` is sent, the subscription's billing period will be reset to today and the full price of the new product will be charged. If `true` is sent, the billing period will not change and a prorated charge will be issued for the new product."* Both branches **charge immediately** — there is no third option that defers the charge/change to the next renewal date. Concretely:
- **"Immediate with proration"** → `PreservePeriod = true` (period unchanged, prorated charge issued now).
- **"Immediate without proration" / full-price reset** → `PreservePeriod = false` (the default) — this resets the period to today and charges full price now; it is **not** "no charge until next renewal," it's "full price now."
- **"At next renewal"** → **not available** via `MigrateSubscriptionProduct`/`SubscriptionProductMigration` on this SDK surface — confirmed absent, not merely undocumented. If the brief's "next renewal, no proration" requirement is a hard requirement, it needs either (a) a different mechanism entirely (e.g., customer-scheduled self-service change, or a scheduled job that calls `MigrateSubscriptionProduct` with `PreservePeriod=false` exactly at the next renewal timestamp, reading `Subscription.CurrentPeriodEndsAt` to know when), or (b) re-confirming with Maxio support/docs outside this SDK's surface — flag this to the coordinator as a product-requirement gap, not an implementation bug.

### Item 7 — Lifecycle: pause / resume / cancel / reactivate + state re-read

| Op | Signature | Request model | Response | Error | Map page |
|---|---|---|---|---|---|
| `Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — pass `include: null` if unused. **Use before every lifecycle call to validate the legal transition, and again after a caught provider error to resync state.** | — | `SubscriptionResponse.Subscription` — read `.State` (`SubscriptionState?`) | Case B `SdkException<RawError>` | `operations/Subscriptions.md` |
| `SubscriptionStatus.PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` | `PauseRequest.Hold` (`AutoResume?`) — `AutoResume.AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` (null = indefinite pause) | `SubscriptionResponse.Subscription` | Case A `SdkException<PauseSubscriptionError>`: `TryGetErrorListResponse1` [422] → `TryGetRawError` | `operations/SubscriptionStatus.md` |
| `SubscriptionStatus.ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` | scalar enum param, no body record | `SubscriptionResponse.Subscription` | Case A `SdkException<ResumeSubscriptionError>`: `TryGetErrorListResponse1` [422] → `TryGetRawError` | `operations/SubscriptionStatus.md` |
| `SubscriptionStatus.CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` | `CancellationRequest.Subscription` (`CancellationOptions !req`) — `CancellationMessage`, `ReasonCode`, **`CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`** (true = end-of-period, false/omit = immediate), `ScheduledCancellationAt`, `RefundPrepaymentAccountBalance` | `SubscriptionResponse.Subscription` | Case A `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent(out RawError)` [404] → `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] → `TryGetRawError` | `operations/SubscriptionStatus.md` |
| `SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` | `CalendarBilling`, `IncludeTrial`, `PreserveBalance`, `CouponCode`, `UseCreditsAndPrepayments`, `Resume (resume): Resume?` — `Resume` is an `AnyOf` union over `bool`/`ResumeOptions`; build via `Resume.Bool(true)` or `Resume.ResumeOptions(new ResumeOptions{...})` (factories source-confirmed, §5 Q7/Q8) | `SubscriptionResponse.Subscription` | Case A `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] → `TryGetRawError` | `operations/SubscriptionStatus.md` |

**Enum `ResumptionCharge`** (`models/enums.md:83`): `prorated`, `immediate`, `delayed`. C# constants confirmed: `.Prorated`/`.Immediate`/`.Delayed`.
**Enum `CancellationMethod`** (`models/enums.md:17`, appears on `Subscription.CancellationMethod` for read-back): `merchant_ui`, `merchant_api`, `dunning`, `billing_portal`, `unknown`, `imported`. C# constants confirmed: `.MerchantUi`/`.MerchantApi`/`.Dunning`/`.BillingPortal`/`.Unknown`/`.Imported`.

**Legal-transition validation:** the map/source doc-comments read this batch don't include a from/to transition matrix for Pause/Resume/Cancel/Reactivate — still `SOURCE-LOOKUP NEEDED: Api/SubscriptionStatus.cs` (its per-method XML doc-comments, not yet opened) if a hard transition table is required; otherwise rely on a `ReadSubscription` + provider-error-driven approach (attempt the call, catch, re-read).

### Item 8 — Error-handling shape

- Every operation is **throw-only**; no `…Result` no-throw sibling exists anywhere in this SDK.
- `SdkException<TError>` — source-confirmed (`Core/Exceptions/SdkException.cs`, namespace `MaxioAdvancedBilling.Core.Exceptions`): `public sealed class SdkException<TError> : Exception { public required TError Error { get; init; } }` — exactly one property, no other members.
- **Case A** (typed): `TError` is a generated `{Operation}Error : ApiError` (namespace `MaxioAdvancedBilling.Errors`, confirmed via `Errors/CreateCustomerError.cs`) with per-status `TryGet…(out …)` accessors **plus** inherited `TryGetRawError(out RawError)`.
- **Case B** (raw): `TError` is `RawError` directly — source-confirmed (`Core/ErrorResponse/RawError.cs`, namespace `MaxioAdvancedBilling.Core.ErrorResponse`): `public sealed class RawError { public HttpStatusCode StatusCode { get; } public ReadOnlyMemory<byte> ReadAsBytes(); public string ReadAsString(); public T? ReadAsJson<T>(); }` — no `TryGet*`, exactly these 4 members.
- Of the 247 total SDK operations, 163 are Case A / 84 are Case B — per-operation case is fixed and listed in every row above.

### Item 9 — Auth & base-URL / server override

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;   // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },  // literal "x"
    Environment = ServerEnvironment.Us,  // or .Eu
};
options.Server.Production.Us.Site = "apimatic-hackathon";
// options.Server.Production.Us.BaseUrl = explicitOverride;  // wins if set — see §5 Q5, source-confirmed
```

- **Auth**: `MaxioAdvancedBillingClientOptions.BasicAuth: BasicAuthCredentials?` — `Username` = API key, `Password` = literal `"x"`. `BasicAuthCredentials` is source-confirmed sealed with `required string Username`/`required string Password` and an internal `Encode()` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`).
- **Subdomain/environment**: `options.Environment` (`ServerEnvironment.Us` default / `.Eu`, namespace `MaxioAdvancedBilling.Servers`); `options.Server.Production.{Us|Eu}.Site` sets `{site}` in the base-URL template.
- **Explicit base-URL override — confirmed to win over `Site`, and now source-confirmed exactly how:** `ProductionOptions.Resolve(...)` builds the request URL as `new UrlTemplate(Us.BaseUrl, path, [TemplateParam.ForServer("site", Us.Site)])` — `Site` is substituted **into** the `{site}` placeholder *inside* `BaseUrl`. Set `BaseUrl` to a literal host with no `{site}` token (e.g. `"https://localhost:8080"`) and the `Site` substitution has nothing to replace — `BaseUrl` wins outright. No intermediate `new()` calls are needed: `Server`/`Production`/`Us`/`Eu`/`Ebb` are all auto-instantiated with real defaults (see §5 Q5).
- DI form: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Server.Production.Us.BaseUrl = …; })` — exact mechanics source-confirmed in §5 Q4.

---

## 3. Trap notes (attached to steps above)

- **Step 1 (client/DI):** the `HttpClient` and `MaxioAdvancedBillingClient` are both meant to be long-lived. Source-confirmed: `AddMaxioAdvancedBillingClient` registers the client as a **singleton** (`services.AddSingleton(sp => new MaxioAdvancedBillingClient(...))`) and internally creates its own `HttpClient` via `IHttpClientFactory.CreateClient()` (the default/unnamed client, after calling `services.AddHttpClient()`) — **you do not construct or pass an `HttpClient` yourself when using this DI method.** [`dotnet-client-initialization` + source, §5 Q4]
- **Step 1 (auth):** set `BasicAuth` before constructing the client, or inside the `Add…Client(o => ...)` DI callback (the `configure` delegate runs once, synchronously, at registration, before the options are captured into the singleton factory closure); load the API key from configuration/secrets, never hardcode. [`dotnet-authentication` + source]
- **Steps 2–8 (all list/filter calls):** every operation with several nullable-no-default params (`ListProductsForProductFamily`, `ListUsages`, etc.) must be called with **named arguments**. [`dotnet-calling-endpoints`]
- **Step 2 (component kind check):** compare with `Kind == ComponentKind.MeteredComponent` — this exact constant name is now source-confirmed (§5 Q6), not a guess; also guard for `Kind` being `null`. [`dotnet-models` + source]
- **Step 3 (create subscription / write envelope):** every write body wraps the "real" payload one level down — populate the **inner** record, not the outer wrapper. [`dotnet-calling-endpoints`]
- **Step 5 (usage union args):** `.Int(...)`/`.String(...)` factories are source-confirmed, plus an **implicit conversion operator** from both `int` and `string` on both `SubscriptionIdOrReference` and `ComponentIdModel` — `SubscriptionIdOrReference subRef = subscriptionId;` compiles and is equivalent to `.Int(subscriptionId)`. [`dotnet-models` + source, §5 Q8]
- **Steps 3, 6, 7 (all Case A catches):** enumerate **every** `TryGet…` accessor listed in the sheet per operation, with `TryGetRawError` **last**. Note the `CreateCustomerError` 422 typed accessor is now confirmed low-value (see item 2) — plan to fall through to `TryGetRawError` in practice for that one. [`dotnet-error-handling` + source]
- **Step 7 (re-read after error):** on any caught `SdkException` from a lifecycle call, re-`ReadSubscription` before deciding the next action. [`dotnet-error-handling`]
- **Step 9 (retries):** `POST`/`DELETE` are **not** retried by default — a transient 5xx on `CreateSubscription`/`CreateUsage`/`CancelSubscription`/`MigrateSubscriptionProduct`/`PauseSubscription`/`ResumeSubscription` surfaces immediately. [`dotnet-configuration-resilience`]
- **Step 9 (verify on the wire):** attach a logging `DelegatingHandler` for the first real call against the sandbox and confirm verb/path/site substitution. [`dotnet-configuration-resilience`]

---

## 4. Assumptions & Blockers

- **Assumption:** the eShopOnWeb user's stable external reference is the user's **email** — plan uses `Customer.Reference = email`. Either email or username works as a plain string, no SDK change needed.
- **Assumption:** "requires-payment-method off" is a **product-level** configuration (`Product.RequireCreditCard`/`RequestCreditCard` = false on `eshop-pro`/`basic-plan` in the sandbox), not a per-subscription request flag — confirmed no such flag exists on `CreateSubscriptionRequest`/`CreateSubscription`. Startup validation should assert this via `ReadProductByHandle` for both products.
- **RESOLVED — item 6 (plan-change timing):** "commit at next renewal without proration" is **confirmed unsupported** by `MigrateSubscriptionProduct`/`SubscriptionProductMigration` — both `PreservePeriod` branches charge immediately (source doc-comment read this batch, see Item 6 above). This is now a **product-requirement gap to raise with the coordinator**, not an open source-lookup.
- **RESOLVED — item 5 (usage read-back):** `SubscriptionComponent.UnitBalance` is confirmed to carry **no doc-comment** in source (genuinely undocumented, not a map gap) — recommend cross-checking against summed `ListUsages` for period-to-date usage before trusting it in the UI.
- **RESOLVED — item 2 (customer-creation error message):** `CustomerErrorResponse1.Errors` is confirmed to carry only `PerPage`/`PricePoint` keys, unrelated to customer validation — use `TryGetRawError`'s raw body for a real 422 message on `CreateCustomer`.
- **Blocker (still open, item 3/idempotent-subscribe):** the map/source pass this session didn't confirm `CreateCustomer`'s behavior on a duplicate `Reference` (new customer vs. 422) — plan's find-then-create pattern side-steps this for the normal case; a race between two requests still has an unconfirmed exact error shape.
- **Blocker (still open, item 7):** no state-transition matrix for Pause/Resume/Cancel/Reactivate — `Api/SubscriptionStatus.cs`'s per-method XML doc-comments haven't been opened this session. `SOURCE-LOOKUP NEEDED: Api/SubscriptionStatus.cs` if a hard transition table is required before implementation; otherwise the plan's read-then-attempt-then-reread pattern covers it without one.
- No blocker on items 1, 4, 8, 9 — fully grounded (map + source this batch) with no open questions.

---

## 5. Follow-up facts (batch 2 — source-confirmed against the session clone)

Answering the Infrastructure-layer implementation questions batched by the coordinator. All facts below were
confirmed by opening the exact file in the session's recorded SDK source clone (path in **Session artifacts**)
— none guessed. Namespaces are the literal `namespace` line at the top of each file.

**Q1 — `Subscription` record (full field list, confirmed source `Models/Subscription.cs` shape as previously
mapped from `records-4-Su-We.md:126`; the model itself wasn't reopened this batch since the map's field list is
verbatim — restated here as the answer):**
`Id (id): int?`, `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`,
`TotalRevenueInCents`, `ProductPriceInCents`, `ProductVersionNumber`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`,
`NextAssessmentAt (next_assessment_at): DateTimeOffset?` (**no separate `NextBillingAt` field exists on
`Subscription`** — `next_billing_at` only appears as an input on `CreateSubscription`, not on the read model),
`TrialStartedAt`, `TrialEndedAt`, `ActivatedAt`, `ExpiresAt`, `CreatedAt`, `UpdatedAt`, `CancellationMessage`,
`CancellationMethod (cancellation_method): CancellationMethod?`, `CancelAtEndOfPeriod`, `CanceledAt`,
`CurrentPeriodStartedAt`, `PreviousState (previous_state): SubscriptionState?`, `SignupPaymentId`, `SignupRevenue`,
`DelayedCancelAt`, `CouponCode`, `SnapDay`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`,
**`Customer (customer): Customer?`** — the **full** `Customer` record (not a slim `Customer1`/summary type), so it
carries `Reference` and everything else in Q2 below, **`Product (product): Product?`** — the **full** `Product`
record, carrying `Id`, `Handle`, `Name`, `PriceInCents` and everything else from Item 1's model shape, `CreditCard`,
`Group`, `BankAccount`, `PaymentType`, `ReferralCode`, `NextProductId`, `NextProductHandle`, `CouponUseCount`,
`CouponUsesAllowed`, `ReasonCode`, `AutomaticallyResumeAt`, `CouponCodes`, `OfferId`, `PayerId`,
`CurrentBillingAmountInCents`, `ProductPricePointId`, `ProductPricePointType`, `NextProductPricePointId`,
`NetTerms`, `StoredCredentialTransactionId`, `Reference`, `OnHoldAt`, `PrepaidDunning`, `Coupons`,
`DunningCommunicationDelayEnabled`, `DunningCommunicationDelayTimeZone`, `ReceivesInvoiceEmails`, `Locale`,
`Currency`, `ScheduledCancellationAt`, `CreditBalanceInCents`, `PrepaymentBalanceInCents`, `PrepaidConfiguration`,
`SelfServicePageToken`. Map page: `records-4-Su-We.md`.

**Q2 — `Customer` record:** `Id (id): int?` (confirmed `int?`), `Reference (reference): string?`,
`Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` — all confirmed
matching the asked types, plus `CcEmails`, `Organization`, `CreatedAt`, `UpdatedAt`, `Address*`, `Phone`,
`Verified`, portal-invite timestamps, `TaxExempt`/`VatNumber`, `ParentId`, `Locale`,
`DefaultSubscriptionGroupUid`, `SalesforceId`, `Maxioid`. Map page: `records-2-Cr-Pa.md:19`.

**Q3 — `SubscriptionComponent` (from `SubscriptionComponentResponse.Component`) — confirmed via
`Models/SubscriptionComponent.cs` this batch:** `Kind` is a **flat field directly on `SubscriptionComponent`**
(`Kind (kind): ComponentKind?`) — there is **no nested `Component` sub-object**; the component identity is
carried as scalars `ComponentId (component_id): int?` / `ComponentHandle (component_handle): string?`, and
`SubscriptionId (subscription_id): int?` is also flat. There **is** a nested `Subscription (subscription):
SubscriptionComponentSubscription?` field — a **slim subscription-summary type**, not the full `Subscription`
model. Also present: `Id`, `Name`, `UnitName`, `Enabled`, `UnitBalance (unit_balance): int?` (undocumented in
source — see §4), `Currency`, `AllocatedQuantity`, `PricingScheme`, `Recurring`, `UpgradeCharge`,
`DowngradeCredit`, `ArchivedAt`, `PricePointId`/`PricePointHandle`/`PricePointType`/`PricePointName`,
`ProductFamilyId`/`ProductFamilyHandle`, `CreatedAt`, `UpdatedAt`, `UseSiteExchangeRate`, `Description`,
`AllowFractionalQuantities`, `HistoricUsages`, `DisplayOnHostedPage`, `Interval`, `IntervalUnit`.

**Q4 — `AddMaxioAdvancedBillingClient` exact mechanics (source: `ServiceCollectionExtensions.cs`, namespace
`MaxioAdvancedBilling`):**
```csharp
namespace MaxioAdvancedBilling;
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMaxioAdvancedBillingClient(
            Action<MaxioAdvancedBillingClientOptions>? configure = null)
        {
            var options = new MaxioAdvancedBillingClientOptions();
            configure?.Invoke(options);
            services.AddHttpClient();
            services.AddSingleton(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient();
                return new MaxioAdvancedBillingClient(httpClient, options);
            });
            return services;
        }
    }
}
```
It's written with C#'s new `extension(IServiceCollection services) { ... }` member syntax (not the classic
`this IServiceCollection services` form), but is called identically: `services.AddMaxioAdvancedBillingClient(o
=> {...})`. Key facts: **`configure` is optional** (`= null`, can be omitted); it calls **`services.AddHttpClient()`**
(registers the default `IHttpClientFactory`) then registers the `MaxioAdvancedBillingClient` as a
**singleton** via `AddSingleton`, resolving `httpClientFactory.CreateClient()` (the **default, unnamed**
client) inside the factory lambda. **You do not need to construct or register an `HttpClient` yourself** —
the DI extension owns that entirely. To attach a logging/auth `DelegatingHandler`, configure the default
factory client (`services.AddHttpClient(Options.DefaultName).AddHttpMessageHandler(...)`) *before or alongside*
this call, per `dotnet-configuration-resilience`.

**Q5 — `Server.Production.Us`/`.Eu` shape (source: `ServerOptions.cs` at repo root, namespace
`MaxioAdvancedBilling`; `Servers/ProductionOptions.cs`, namespace `MaxioAdvancedBilling.Servers`):**
```csharp
// ServerOptions.cs
public class ServerOptions
{
    public ProductionOptions Production { get; set; } = new();
    public EbbOptions Ebb { get; set; } = new();
}

// Servers/ProductionOptions.cs
public class ProductionOptions
{
    public UsOptions Us { get; set; } = new();
    public EuOptions Eu { get; set; } = new();

    public class UsOptions { public string BaseUrl { get; set; } = "https://{site}.chargify.com"; public string Site { get; set; } = "subdomain"; }
    public class EuOptions { public string BaseUrl { get; set; } = "https://{site}.ebilling.maxio.com"; public string Site { get; set; } = "subdomain"; }
}
```
`Production`/`Ebb` on `ServerOptions`, and `Us`/`Eu` on `ProductionOptions`, are **all auto-instantiated**
(`= new()`) with real default values — **never null**, no intermediate `new()` needed. `BaseUrl` and `Site`
are plain **non-nullable `string`** properties (not `string?`) — write `options.Server.Production.Us.BaseUrl
= "..."` / `.Site = "apimatic-hackathon"` directly. Bonus-confirmed: `ProductionOptions.Resolve(ServerEnvironment,
path)` builds the request URL via `new UrlTemplate(Us.BaseUrl, path, [TemplateParam.ForServer("site", Us.Site)])`
— `Site` is substituted into the `{site}` token *inside* `BaseUrl`; a `BaseUrl` with no `{site}` token makes
`Site` a no-op, confirming the override-wins requirement (Item 9).

**Q6 — Enum C# constant names (all confirmed via `Models/Enums/*.cs`, namespace
`MaxioAdvancedBilling.Models.Enums` for every one; naming convention confirmed as PascalCase of the snake_case
wire value, one `public static readonly {Enum} {Name} = new("wire_value")` field per constant, plus a
`public static {Enum} FromValue(string value)` on every enum):**
- `ComponentKind`: `.MeteredComponent` ("metered_component"), `.QuantityBasedComponent`, `.OnOffComponent`,
  `.PrepaidUsageComponent`, `.EventBasedComponent`.
- `SubscriptionState` (all 15): `.Pending`, `.FailedToCreate`, `.Trialing`, `.Assessing`, `.Active`,
  `.SoftFailure`, `.PastDue`, `.Suspended`, `.Canceled`, `.Expired`, `.Paused`, `.Unpaid`, `.TrialEnded`,
  `.OnHold`, `.AwaitingSignup`.
- `CollectionMethod`: `.Automatic`, `.Remittance`, `.Prepaid`, `.Invoice`.
- `ResumptionCharge`: `.Prorated`, `.Immediate`, `.Delayed`.
- `CancellationMethod`: `.MerchantUi`, `.MerchantApi`, `.Dunning`, `.BillingPortal`, `.Unknown`, `.Imported`.
- Literal expressions to write: `ComponentKind.MeteredComponent`, `SubscriptionState.Active`,
  `SubscriptionState.Canceled`, `SubscriptionState.Paused`, `SubscriptionState.Trialing`,
  `SubscriptionState.Unpaid`, `SubscriptionState.OnHold`, `SubscriptionState.PastDue`,
  `SubscriptionState.Suspended`, `SubscriptionState.Expired`, `SubscriptionState.AwaitingSignup`.

**Q7 — Namespaces (grouped, all confirmed via the `namespace` line in the actual source file this batch,
except where noted):**
| Type(s) | Namespace | Confirmed how |
|---|---|---|
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | opened `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | opened `Servers/ServerEnvironment.cs` |
| `ServerOptions` | `MaxioAdvancedBilling` (root) | opened `ServerOptions.cs` — **not** `.Servers` despite `ProductionOptions`/`EbbOptions` being in `.Servers`; don't assume folder mirrors namespace uniformly on this SDK |
| `ProductionOptions` (incl. nested `UsOptions`/`EuOptions`) | `MaxioAdvancedBilling.Servers` | opened `Servers/ProductionOptions.cs` |
| `ComponentKind`, `SubscriptionState`, `CollectionMethod`, `ResumptionCharge`, `CancellationMethod` | `MaxioAdvancedBilling.Models.Enums` | opened all 5 `Models/Enums/*.cs` files |
| `SubscriptionIdOrReference`, `ComponentIdModel`, `Resume` | `MaxioAdvancedBilling.Models.AnyOf` | opened `Models/AnyOf/SubscriptionIdOrReference.cs`, `ComponentIdModel.cs`, `Resume.cs` |
| `Proration`, `AutoResume`, `ResumeOptions`, `Errors`, `CustomerErrorResponse1` | `MaxioAdvancedBilling.Models` | opened `Models/Proration.cs`, `AutoResume.cs`, `ResumeOptions.cs`, `Errors.cs` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | opened `Core/ErrorResponse/RawError.cs` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` | opened `Core/Exceptions/SdkException.cs` |
| `CreateCustomerError` (and every other `{Operation}Error`) | `MaxioAdvancedBilling.Errors` | opened `Errors/CreateCustomerError.cs` |
| `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `CreateCustomerError : ApiError` is declared in `Errors/CreateCustomerError.cs` under `namespace MaxioAdvancedBilling.Errors` but the base class isn't `using`-qualified there beyond the ambient `Core.ErrorResponse`/`Core.Models` usings at the top of that file — **not independently opened `ApiError.cs` this batch**; inferred with high confidence from the shared file usings, not a direct read. Treat as `SOURCE-LOOKUP NEEDED: Core/ErrorResponse/ApiError.cs` if you need it standalone (unlikely — you reach it only via a concrete `{Operation}Error`, never construct/reference it directly). |

**Q8 — Union factory/reader methods (confirmed identical on both, via `Models/AnyOf/SubscriptionIdOrReference.cs`
and `Models/AnyOf/ComponentIdModel.cs`):**
```csharp
public static SubscriptionIdOrReference Int(int value);
public static SubscriptionIdOrReference String(string value);
public bool TryGetInt(out int value);
public bool TryGetString(out string value);
public static implicit operator SubscriptionIdOrReference(int value);     // => Int(value)
public static implicit operator SubscriptionIdOrReference(string value); // => String(value)
```
(same 6 members, verbatim, on `ComponentIdModel`). So `SubscriptionIdOrReference subRef = subscriptionId;`
(passing a plain `int`) compiles and is exactly equivalent to `SubscriptionIdOrReference.Int(subscriptionId)` —
confirmed, not inferred.

## Session artifacts

- SDK source clone (read-only reference, tag `v1.0.2` / commit `15db14b`, matches `sdk-map.md`):
  `/tmp/maxio-sdk-src/20260713-211909` (Windows path: `C:\Users\moham\AppData\Local\Temp\maxio-sdk-src\20260713-211909`).
  Reuse this path for any later source lookup in this session — do not re-clone.
