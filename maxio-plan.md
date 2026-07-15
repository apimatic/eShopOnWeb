# Maxio Advanced Billing integration plan — eShopOnWeb

Target: Maxio sandbox site `apimatic-hackathon`, region **US**. Product family `eshop-subscribe`
(id `3008866`); products `eshop-pro` (id `7111477`, $299/mo) and `basic-plan` (id `7111478`, $29/mo),
both requires-payment-method OFF, no trial, no setup fee, expires never, non-taxable. Metered
component `api-call` (id `3033795`, Metered kind, Per Unit, $0.01/unit) on that family.

Architecture: ApplicationCore defines a provider-agnostic `IBillingClient`; Infrastructure's single
`MaxioBillingClient` implements it via `AsadAli.AdvancedBilling.Sdk` (`using MaxioAdvancedBilling;`).

All facts below are grounded in the bundled SDK map (`sdk-map.md`, `map/operations/*.md`,
`map/models/*.md`) plus, where the map didn't carry a fact, the SDK source at commit `15db14b`
(tag `v1.0.2`) — cited inline where used.

---

## 1. Scope & sequence

1. **Client & DI setup** — construct/register `MaxioAdvancedBillingClient` (Basic auth, `ServerEnvironment.Us`,
   site = `apimatic-hackathon`). No operations.
2. **Startup validation** — `ProductFamilies.ReadProductFamily`, `ProductFamilies.ListProductsForProductFamily`,
   `Components.ListComponentsForProductFamily` (or `Components.ReadComponent`) to confirm the configured
   handles/ids resolve and `api-call` is `ComponentKind.MeteredComponent`.
3. **Browse-plans page** — `ProductFamilies.ListProductsForProductFamily` → name/price/interval per product.
4. **Ensure-customer (idempotent)** — `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` fallback.
5. **Create subscription** — `Subscriptions.CreateSubscription` against the resolved customer + product.
6. **My-subscriptions page** — `Customers.ListCustomerSubscriptions` (list) / `Subscriptions.ReadSubscription` (detail).
7. **Usage recording + read-back** — `SubscriptionComponents.CreateUsage` then
   `SubscriptionComponents.ReadSubscriptionComponent` for the running `UnitBalance`.
8. **Plan-change (migration)** — `SubscriptionProducts.PreviewSubscriptionProductMigration` →
   `SubscriptionProducts.MigrateSubscriptionProduct` for "now, with proration"; `Subscriptions.UpdateSubscription`
   (`ProductChangeDelayed = true`) for "at next renewal, no proration" (see Blockers — this path has no preview).
9. **Lifecycle transitions** — `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` /
   `CancelSubscription` (immediate) / `InitiateDelayedCancellation` + `CancelDelayedCancellation` /
   `ReactivateSubscription`.
10. **Error wrapping** — one `try/catch` shape per Case A/Case B operation, converted to the provider-agnostic
    exception at the `MaxioBillingClient` boundary (see §Trap notes and §Error model).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**

### 2.1 Client construction, auth, servers (map: `sdk-map.md`)

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }, // literal "x"
    Environment = ServerEnvironment.Us,                 // default; US region
};
options.Server.Production.Us.Site = "apimatic-hackathon"; // {site} in https://{site}.chargify.com
var client = new MaxioAdvancedBillingClient(httpClient, options); // httpClient: caller-owned, long-lived
```

- Only constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
  `MaxioBillingClient` (Infrastructure) should take the `MaxioAdvancedBillingClient` (or the `HttpClient` +
  options) via DI and hold it for the app lifetime — do not construct per request.
- `MaxioAdvancedBillingClientOptions` members: `Environment: ServerEnvironment`, `Retry: RetryOptions`,
  `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?`. No other auth scheme exists on this SDK
  (Basic-only) — do not build multi-scheme handling.
- **Explicit base-URL override** (instead of subdomain-derived): `options.Server.Production.Us.BaseUrl =
  "https://custom-host.example.com"` — nested per server group (`Production`/`Ebb`) **and** per environment
  (`Us`/`Eu`), never set directly on `ServerOptions`. Only `Production` is used by the operations in this
  plan (the `Ebb` group is events-ingest only, not used here).
- **DI extension — exact source** (`ServiceCollectionExtensions.cs`, namespace `MaxioAdvancedBilling`,
  read from source: the map's "DI alternative" line names the file but not the full body/lifetime, which
  is why this was pulled from the clone rather than the map):
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
              configure?.Invoke(options);              // invoked synchronously, right here — no IServiceProvider
              services.AddHttpClient();                 // registers IHttpClientFactory infra for you
              services.AddSingleton(sp =>
              {
                  var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                  var httpClient = httpClientFactory.CreateClient();   // default/unnamed client, created ONCE
                  return new MaxioAdvancedBillingClient(httpClient, options);
              });
              return services;
          }
      }
  }
  ```
  This uses C# 14 extension-member syntax (`extension(IServiceCollection services) { ... }`), but is called
  exactly like a classic extension method: `services.AddMaxioAdvancedBillingClient(o => { ... });`. **Only
  one overload exists** — `Action<MaxioAdvancedBillingClientOptions>? configure = null` — there is **no**
  `Action<IServiceProvider, MaxioAdvancedBillingClientOptions>` overload and no other overload of any kind.
  Facts that follow directly from this body:
  - **Lifetime:** `MaxioAdvancedBillingClient` is registered `AddSingleton` — one instance for the app's
    lifetime, directly injectable by concrete type (`public MaxioBillingClient(MaxioAdvancedBillingClient client)`
    works as-is; no interface/wrapper is registered for it).
  - **`HttpClient` handling:** the extension calls `services.AddHttpClient()` (the parameterless registration
    of `IHttpClientFactory` infrastructure) for you — **do not** also call `services.AddHttpClient()` yourself
    for this purpose (harmless if you do, since it's additive registration, but redundant). The `HttpClient`
    itself is obtained via `httpClientFactory.CreateClient()` (the **default, unnamed** client) **once**,
    inside the singleton factory delegate — so it is created a single time and reused for the app's lifetime
    as a field of the singleton `MaxioAdvancedBillingClient`, not re-created per logical request the way a
    named `IHttpClientFactory` client normally rotates handlers. You do not need to (and cannot cleanly)
    supply your own `HttpClient` alongside this extension.
  - **`MaxioAdvancedBillingClientOptions` is not itself registered in the container** — it only exists as a
    local variable captured by the singleton factory's closure. You cannot resolve
    `MaxioAdvancedBillingClientOptions` (or `IOptions<MaxioAdvancedBillingClientOptions>`) from DI elsewhere;
    it is not exposed beyond this one registration.
  - **`configure` has no `IServiceProvider` access and runs eagerly.** `configure?.Invoke(options)` executes
    **synchronously inside the `AddMaxioAdvancedBillingClient(...)` call itself** — i.e. at the point in
    `ConfigureCoreServices.cs`/`Program.cs` where you call it, before the container is even built. There is
    **no way** to resolve `IOptions<MaxioSettings>` (or anything else from DI) inside this delegate — plain
    `Action<T>`, no service-provider parameter, no lazy/deferred invocation to hook into. **If per-app-config
    resolution through your own bound `IOptions<MaxioSettings>` is required, do not use this extension** —
    read configuration directly and synchronously at the composition-root call site instead (e.g.
    `builder.Configuration.GetSection("Maxio").Get<MaxioSettings>()`, which is available before
    `BuildServiceProvider()`/`builder.Build()` runs, unlike `IOptions<T>`), and pass the resulting literal
    values into the `configure` action; or skip this extension entirely and hand-roll the same
    `AddHttpClient()` + `AddSingleton(sp => ...)` pair yourself, resolving
    `sp.GetRequiredService<IOptions<MaxioSettings>>().Value` inside your own factory delegate (this is exactly
    the fallback pattern described in the follow-up question, and it is the correct one for DI-driven config).
- Retry defaults (`RetryOptions.Default()`): retries `408/429/500/502/503/504` on `GET/HEAD/PUT/OPTIONS` only,
  3 retries, 1s exponential backoff. **`POST`/`DELETE`/`PATCH` are never auto-retried** — this covers nearly
  every write in this plan (CreateSubscription, CreateUsage, CreateCustomer, migrations, cancel, pause,
  resume, reactivate are all POST/DELETE) — do not assume idempotent-retry safety on these; if you want
  your own retry on a genuinely idempotent write, do it at the `MaxioBillingClient` layer explicitly.

### 2.2 Operations used, in call order

| # | Operation (`client.X.Y`) | Signature | Request fields | Response envelope → fields read | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|---|
| 2 | `ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | path `id` = `3008866` (int only — see Blockers, no handle form) | `ProductFamilyResponse.ProductFamily?: ProductFamily` → `Id, Name, Handle, AccountingCode, Description` | Case B `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| 2/3 | `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `productFamilyId` = `"3008866"` (**string** here, unlike `ReadProductFamily`'s `int`); pass `null` for the 8 leading nullable filters; `page`/`perPage` have defaults | `IReadOnlyList<ProductResponse>`, each `.Product: Product` → `Id, Name, Handle, PriceInCents, Interval, IntervalUnit, RequireCreditCard, RequestCreditCard, Taxable, TrialPriceInCents/TrialInterval (null ⇒ no trial), ExpirationInterval (null ⇒ expires never)` | Case B `SdkException<RawError>` | manual `page`+`perPage` | `operations/ProductFamilies.md` |
| 2 | `Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `productFamilyId` = `3008866` (**int** here) | `IReadOnlyList<ComponentResponse>`, each `.Component: Component` → `Id, Name, Handle, Kind (ComponentKind), PricingScheme, UnitPrice, PricePerUnitInCents, UnitName` | Case B `SdkException<RawError>` | manual `page`+`perPage` | `operations/Components.md` |
| 2 | `Components.ReadComponent` (alt., single lookup of `api-call`) | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | `productFamilyId=3008866`, `componentId` = `"3033795"` or `"handle:api-call"` (handle prefix supported — `componentId` is `string`) | `ComponentResponse.Component: Component` → assert `Kind == ComponentKind.MeteredComponent`, `PricingScheme == PricingScheme.PerUnit`, `UnitPrice == "0.01"` | Case B `SdkException<RawError>` | none | `operations/Components.md` |
| 4a | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` ← the eShopOnWeb user's stable id/email | `CustomerResponse.Customer: Customer` → `Id, FirstName, LastName, Email, Reference` | Case B `SdkException<RawError>` — a miss is a `404` in `RawError.StatusCode`, not a typed "not found" | none | `operations/Customers.md` |
| 4b | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `body.Customer: CreateCustomer` — `!req`: `FirstName, LastName, Email`; optional incl. `Reference (reference): string?` (set to the same stable id used for lookup) | `CustomerResponse.Customer: Customer !req` → `Id` (store as the Maxio customer id) | Case A `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| 5 | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `body.Subscription: CreateSubscription` — set `ProductId: int?` (or `ProductHandle`) + `CustomerId: int?` (from step 4); `PaymentProfileId`/card fields may be omitted since these products have `RequireCreditCard = false`; optional `Reference: string?` (subscription-level reference, useful for `SubscriptionIdOrReference.String(...)` later) | `SubscriptionResponse.Subscription?: Subscription` → `Id, State (SubscriptionState), Product, CurrentPeriodEndsAt, NextAssessmentAt` | Case A `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/Subscriptions.md` |
| 6a | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` from step 4 | `IReadOnlyList<SubscriptionResponse>`, each `.Subscription` → see field breakdown below; **no second per-subscription call is needed** — plan name/price/interval are inline | Case B `SdkException<RawError>` | none (no page params on this op) | `operations/Customers.md` |
| 6b | `Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | `include` — pass `null` unless you need `SelfServicePageToken` | `SubscriptionResponse.Subscription` (same fields as below, single) | Case B `SdkException<RawError>` | none | `operations/Subscriptions.md` |

**`Subscription` field breakdown for "my subscriptions" rendering** (`Models/Subscription.cs`, all under
`SubscriptionResponse.Subscription`, returned inline by `ListCustomerSubscriptions`/`ReadSubscription`/
`CreateSubscription`/etc. — confirmed from source, not just the map, since exact field-level doc comments
mattered here):

| What to show | Exact field(s) | Type | Source doc-comment (verbatim, abridged) |
|---|---|---|---|
| Plan name/handle | `Subscription.Product.Name`, `Subscription.Product.Handle` | `Product?` nested, `string?`/`string?` | `Product` is inline on the subscription (`[JsonPropertyName("product")] public Product? Product`); no separate `ReadProduct` call needed |
| Plan price (**prefer this over `Product.PriceInCents`**) | `Subscription.ProductPriceInCents` | `long?` | *"The recurring amount of the product (and version) currently subscribed. NOTE: this may differ from the current price of the product, if you've changed the price of the product but haven't moved this subscription to a newer version."* — i.e. this is what the subscription is actually charged; `Subscription.Product.PriceInCents` is the product's current catalog price and can disagree with it |
| Plan interval | `Subscription.Product.Interval`, `Subscription.Product.IntervalUnit` | `int?`, `IntervalUnit?` | inline on the nested `Product`, same object as above |
| Next billing date | `Subscription.CurrentPeriodEndsAt` | `DateTimeOffset?` | *"Timestamp relating to the end of the current (recurring) period (i.e., when the next regularly scheduled attempted charge will occur)"* — this is the field to show as "next billing date" |
| Next **payment-capture attempt** (usually equal to the above) | `Subscription.NextAssessmentAt` | `DateTimeOffset?` | *"...will usually track `current_period_ends_at`, but will diverge if a renewal payment fails and must be retried. In that case `current_period_ends_at` will advance to the end of the next period...but `next_assessment_at` will be scheduled for the auto-retry time."* Use `CurrentPeriodEndsAt` for "next billing date" UI; reserve `NextAssessmentAt` for dunning/retry-specific displays. **Neither field's doc comment references a scheduled delayed product change** — a pending delayed product change (step 8c) does not move either date; it only shows up via `NextProductId`/`NextProductHandle`/`NextProductPricePointId` below |
| Pending delayed **cancellation** (confirms the trap-note guess) | `Subscription.CancelAtEndOfPeriod: bool?`, `Subscription.DelayedCancelAt: DateTimeOffset?` | — | *"Whether or not the subscription will (or has) canceled at the end of the period"* / *"Timestamp for when the subscription is currently set to cancel"* — both confirmed verbatim, no correction needed. A **third**, distinct field `Subscription.ScheduledCancellationAt: DateTimeOffset?` also exists with no doc-comment summary in source — it mirrors the request-side `CancellationOptions.ScheduledCancellationAt` (the feature-gated "Schedule Subscription Cancellation" path via plain `CancelSubscription`, step 9c) and is separate from `DelayedCancelAt` (set by `InitiateDelayedCancellation`, step 9d); don't conflate the two when reading back which mechanism is active |
| Pending delayed **product change** | `Subscription.NextProductId: int?`, `Subscription.NextProductHandle: string?`, `Subscription.NextProductPricePointId: int?` | — | *"If a delayed product change is scheduled, the ID/handle of the product [price point] that the subscription will be changed to at the next renewal."* |
| 7a | `SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` | `subscriptionIdOrReference` = `SubscriptionIdOrReference.Int(id)` (or `.String(reference)`); `componentId` = `ComponentIdModel.Int(3033795)` (or `.String("handle:api-call")`); `body.Usage: CreateUsage` → `Quantity: double?` (set the call count; all fields nullable in the type but `Quantity` is de-facto required by the API), `Memo: string?` optional | `UsageResponse.Usage !req: Usage` → `Id, Quantity, ComponentId, CreatedAt` | Case A `SdkException<CreateUsageError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/SubscriptionComponents.md` |
| 7b | `SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | `componentId = 3033795` (int here, unlike the union used by `CreateUsage`) | `SubscriptionComponentResponse.Component?: SubscriptionComponent` → **`UnitBalance: int?`** is the period-to-date running total/balance for the metered component; also `Kind, PricingScheme, PricePointName` | Case A `SdkException<ReadSubscriptionComponentError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none | `operations/SubscriptionComponents.md` |
| 8a | `SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` | `body.Migration: SubscriptionMigrationPreviewOptions` → `ProductId` or `ProductHandle`; `PreservePeriod: bool? = false` (**`true`** = keep current period, prorated charge now — this is the "now, with proration" preview; `false`/default = period resets to today, full price, not prorated); `ProrationDate: DateTimeOffset?` optional (preview a future date within the current period) | `SubscriptionMigrationPreviewResponse.Migration !req: SubscriptionMigrationPreview` → `ProratedAdjustmentInCents, ChargeInCents, PaymentDueInCents, CreditAppliedInCents` | Case A `SdkException<PreviewSubscriptionProductMigrationError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/SubscriptionProducts.md` |
| 8b | `SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` | `body.Migration: SubscriptionProductMigration` → same shape as the preview options (`ProductId`/`ProductHandle`, `PreservePeriod: bool? = false`, `Proration: Proration?` — see Trap notes for the `PreservePeriod` vs `Proration.PreservePeriod` duplication) | `SubscriptionResponse.Subscription` (updated) | Case A `SdkException<MigrateSubscriptionProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/SubscriptionProducts.md` |
| 8c | `Subscriptions.UpdateSubscription` (delayed, no-proration change) | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` | `body.Subscription: UpdateSubscription` → `ProductId: int?` or `ProductHandle: string?` + **`ProductChangeDelayed: bool? = true`**; optionally `ProductPricePointId`/`ProductPricePointHandle`; to *cancel* a pending delayed change set `NextProductId: string?` = `""` (empty string — note the request field is `string?` specifically to allow this sentinel, even though the read-back `Subscription.NextProductId` is `int?`) | `SubscriptionResponse.Subscription` → `NextProductId, NextProductHandle` show the pending change until it locks in at renewal | Case A `SdkException<UpdateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/Subscriptions.md` |
| 9a | `SubscriptionStatus.PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` | `body.Hold: AutoResume?` → optional `AutomaticallyResumeAt: DateTimeOffset?`; pass `body = null` or `new PauseRequest()` for an indefinite hold. Not allowed if `next_billing_at` is within 24h. | `SubscriptionResponse.Subscription` → `State` becomes on-hold (see Trap notes: verify against `SubscriptionState.OnHold`, not `.Paused`) | Case A `SdkException<PauseSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/SubscriptionStatus.md` |
| 9b | `SubscriptionStatus.ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` | `calendarBillingResumptionCharge` — pass `null` (these are non-calendar-billing monthly products; the param only affects calendar-billing subscriptions) | `SubscriptionResponse.Subscription` → `State` returns to `Active` | Case A `SdkException<ResumeSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/SubscriptionStatus.md` |
| 9c | `SubscriptionStatus.CancelSubscription` (**immediate**) | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` | Pass `body = null` (or a `CancellationRequest` whose `CancellationOptions` has no `CancelAtEndOfPeriod`/`ScheduledCancellationAt`) to cancel **now** | `SubscriptionResponse.Subscription` → `State == Canceled`, `CanceledAt` set | Case A `SdkException<CancelSubscriptionApiError>` — `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] (an `AnyOf` of `ErrorListResponse1` / `SingleErrorResponse1` — try both `TryGetErrorListResponse1`/`TryGetSingleErrorResponse1` on it) · `TryGetRawError` [fallback] | none | `operations/SubscriptionStatus.md` |
| 9d | `SubscriptionStatus.InitiateDelayedCancellation` (**end-of-period**) | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` | `body.Subscription: CancellationOptions` → optional `CancellationMessage`, `ReasonCode`; sets the subscription to cancel at the end of the current period | `DelayedCancellationResponse.Message: string?` (thin — no subscription snapshot; re-`ReadSubscription` to confirm `CancelAtEndOfPeriod` / `DelayedCancelAt`) | Case A `SdkException<InitiateDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/SubscriptionStatus.md` |
| 9e | `SubscriptionStatus.CancelDelayedCancellation` (undo 9d) | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | none | `DelayedCancellationResponse.Message`; idempotent — safe even if no delayed cancel was pending | Case A `SdkException<CancelDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none | `operations/SubscriptionStatus.md` |
| 9f | `SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` | `body` optional: `IncludeTrial`, `PreserveBalance`, `CouponCode`, `Resume: Resume?` (`AnyOf<bool, ResumeOptions>` — build via `Resume.Bool(true)` or `Resume.ResumeOptions(new ResumeOptions{...})`, not `new Resume(...)`) | `SubscriptionResponse.Subscription` → `State` becomes `Active` or `Trialing` (not applicable here — no trial configured) | Case A `SdkException<ReactivateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | `operations/SubscriptionStatus.md` |

### 2.2a Where the enums/unions above come from

- `ComponentKind`, `PricingScheme`, `SubscriptionState`, `IntervalUnit`: `map/models/enums.md`.
- `SubscriptionIdOrReference`, `ComponentIdModel`, `Resume`: `map/models/unions.md` (both `AnyOf`, namespace
  `MaxioAdvancedBilling.Models.AnyOf`) — build with the static factories (`.Int(...)`/`.String(...)`), never
  `new`.

### 2.3 Enum value tables actually needed

| Enum | Members (C# name → wire value) | Used for |
|---|---|---|
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` | Assert `api-call` resolves to `MeteredComponent` at startup |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` | Assert `api-call` is `PerUnit` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | Render "$299/mo" from `Product.Interval=1, IntervalUnit=Month` |
| `SubscriptionState` | `Pending, FailedToCreate, Trialing, Assessing, Active, SoftFailure, PastDue, Suspended, Canceled, Expired, Paused, Unpaid, TrialEnded, OnHold, AwaitingSignup` | Render subscription state; note both `Paused` and `OnHold` exist as *distinct* members — see Trap notes |
| `CreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` | Not used directly in this scope (migration here uses `PreservePeriod`/`Proration`, not `CreditType`) — listed only so it isn't confused with `Proration` |

Namespace for all enums: `MaxioAdvancedBilling.Models.Enums`. Construct via the static members (e.g.
`ComponentKind.MeteredComponent`) or `ComponentKind.FromValue("metered_component")`; these are
`StringEnum<T>` records, not C# `enum` — compare with `==`, not `Enum.Equals`.

### 2.4 Error-handling model (applies to every call above)

Every operation throws `SdkException<TError>` (`MaxioAdvancedBilling.Core.Exceptions`) — never a no-throw
`…Result` sibling (this SDK generates none). `TError` is either:

- **Case A** — a per-operation `{Operation}Error : ApiError` (namespace `MaxioAdvancedBilling.Errors`) with
  named `TryGet…(out …)` accessors per possible status, plus the inherited
  `TryGetRawError(out RawError)` fallback (must be tried **last** — it is not a catch-all; a status with a
  more specific accessor never falls through to it).
- **Case B** — `TError` is `RawError` directly (`MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode:
  HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` (throws `JsonException` on non-JSON body —
  prefer `ReadAsString()` unless you know the shape), `ReadAsBytes()`.

Recommended single pattern for `MaxioBillingClient` (wrap each call, translate to the provider-agnostic
exception at this one boundary):

```csharp
try
{
    return await client.Subscriptions.CreateSubscription(body, ct: ct);
}
catch (SdkException<CreateSubscriptionError> ex)                 // Case A — enumerate every TryGet*
{
    if (ex.Error.TryGetErrorListResponse1(out var errs))
        throw new BillingException(string.Join("; ", errs.Errors), ex);
    if (ex.Error.TryGetRawError(out var raw))                     // always last
        throw new BillingException($"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}", ex);
    throw new BillingException("unknown Maxio error", ex);
}
catch (SdkException<RawError> ex)                                 // Case B ops (ReadX, ListX, etc.)
{
    throw new BillingException($"HTTP {(int)ex.Error.StatusCode}: {ex.Error.ReadAsString()}", ex);
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)  // connection failure
{
    throw new BillingException("Maxio unreachable", ex);
}
```

Every read (`ReadProductFamily`, `ListProductsForProductFamily`, `ListComponentsForProductFamily`,
`ReadComponent`, `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ReadSubscription`,
`ReadSubscriptionComponent`) is **Case B**; every write in this plan except `ResumeSubscription`… no —
`ResumeSubscription` is also Case A. In short: **only the 8 reads above are Case B; every other operation in
§2.2 is Case A** with its own distinct `{Operation}Error` type — you need one `catch (SdkException<…Error>)`
block per distinct operation (they do not share a base you can catch generically beyond `ApiError`, and a
shared `ApiError`-typed helper can only reach `TryGetRawError`, silently losing every typed 422 body — read
each operation's typed accessors inside its own `catch`).

---

## 3. Trap notes (attached to the steps above)

- **Step 1 (client construction):** the `HttpClient` and the `MaxioAdvancedBillingClient` are both meant to
  be long-lived singletons — build once (or via DI `AddMaxioAdvancedBillingClient`), never per request.
- **Step 2 (startup validation):** `ProductFamilies.ReadProductFamily` takes `int id` — despite its own XML
  doc saying the family "can be specified either with the id number, or with the `handle:my-family`
  format," the **generated C# signature only accepts `int`** (confirmed in `Api/ProductFamilies.cs`); there
  is no overload for the handle string. Use the numeric id `3008866` you already have — do not attempt to
  pass `"handle:eshop-subscribe"` here (it won't compile).
- **Step 2/3 (product-family-scoped ids):** the `productFamilyId` parameter type **differs by controller** —
  `ProductFamilies.ListProductsForProductFamily(string productFamilyId, …)` takes a **string**, while
  `Components.ListComponentsForProductFamily(int productFamilyId, …)`, `Components.ReadComponent`,
  `Components.ArchiveComponent`, `Components.UpdateProductFamilyComponent` take an **int**. Passing the
  wrong CLR type is a compile error, but don't assume consistency when adding more calls later.
- **Step 2 (component handle lookup):** `Components.ReadComponent`'s `componentId` is `string` and accepts
  either the bare numeric id (`"3033795"`) or a handle prefixed with `"handle:"` (`"handle:api-call"`).
- **Step 4 (ensure-customer idempotency — no native upsert):** `CreateCustomer`'s own doc states "you may
  only create one customer for a given reference value" — a duplicate `reference` is a **422 validation
  error** (`SdkException<CreateCustomerError>`), not an upsert. Additionally, `CustomerErrorResponse1.Errors`
  deserializes to a record (`Models/Errors.cs`) whose only fields are `PerPage`/`PricePoint` — a shape that
  does not model customer-domain validation messages at all (it looks like a generic/shared error-payload
  model mismatched to this operation). **Do not pattern-match on those fields to detect "duplicate
  reference."** Defensive pattern: (1) `ReadCustomerByReference` first; (2) on a `404` `RawError`, call
  `CreateCustomer`; (3) if `CreateCustomer` still throws `SdkException<CreateCustomerError>` with a 422,
  treat it as a race with a concurrent create and re-run `ReadCustomerByReference` — if that now succeeds,
  use the found customer; if it still 404s, wrap and rethrow with the raw body
  (`ex.Error.TryGetRawError(out var raw)` → `raw.ReadAsString()`) for diagnostics rather than trying to parse
  a specific "already taken" message out of the typed payload.
- **Steps 6/9 (authorization — there is no top-level `CustomerId` on `Subscription`):** `Models/Subscription.cs`
  line 214-215 declares only `[JsonPropertyName("customer")] public Customer? Customer { get; init; }` — a
  **nested** object, not a scalar id. There is **no** `Subscription.CustomerId` field anywhere in the record
  (confirmed by grep of the full source file for `customer_id`/`CustomerId` — the only hit is this nested
  `Customer` property). To verify a subscription belongs to the calling user before allowing
  pause/resume/cancel/reactivate/migrate, compare against **`Subscription.Customer.Id: int?`** (the nested
  `Customer` record's own `Id` field, `Models/Customer.cs` — same shape as `CustomerResponse.Customer.Id`
  from step 4). This nested `Customer` has no XML doc comment stating it's always populated (unlike
  `SelfServicePageToken`, which explicitly requires `include: SubscriptionInclude.SelfServicePageToken` to
  appear) — **fail closed**: if `Subscription.Customer` or `Subscription.Customer.Id` is `null` on a
  `ReadSubscription`/`ListCustomerSubscriptions` result, treat ownership as unverified and deny the action
  rather than defaulting to allow.
- **Step 4 (`FirstName`/`LastName` are compiler-required, blank-vs-omitted is unverified):**
  `Models/CreateCustomer.cs` declares `public required string FirstName { get; init; }` and
  `public required string LastName { get; init; }` (and `Email` likewise) with **no XML doc comment and no
  length/non-empty validation attribute** on any of the three. So: the C# type system forces you to supply
  *some* non-null string for both (omitting them is a compile error) — but nothing in the SDK model or the
  map states whether Maxio's server-side validation accepts an **empty string** `""` for a real name. If the
  eShopOnWeb caller only has a username/email, prefer a real fallback (e.g. the email's local-part, or the
  literal username) over `""`, since blank-string acceptance is unverified from source and would need a live
  sandbox check to confirm.
- **Step 5 (no payment method needed):** both configured products have `RequireCreditCard = false`, so
  `CreateSubscription`'s `PaymentProfileAttributes`/`CreditCardAttributes`/`BankAccountAttributes` can all be
  left `null`.
- **Step 7 (usage quantity is nullable but required):** `CreateUsage.Quantity` is typed `double?` (no `!req`
  marker in the generated model) but the API needs a quantity to record usage — always set it explicitly;
  don't rely on the C# type system to enforce this.
- **Step 7 (component id shapes differ across the two usage-related operations):**
  `SubscriptionComponents.CreateUsage`/`ListUsages` take the union `ComponentIdModel` (build via
  `ComponentIdModel.Int(3033795)` or `.String("handle:api-call")`), while
  `SubscriptionComponents.ReadSubscriptionComponent` takes a plain `int componentId` — no handle form there.
- **Step 8 (migration proration semantics — source-grounded, not map-inferred):** `SubscriptionProductMigration`
  / `SubscriptionMigrationPreviewOptions`'s `PreservePeriod` field doc (`Models/SubscriptionProductMigration.cs`):
  *"If `false` is sent, the subscription's billing period will be reset to today and the full price of the
  new product will be charged. If `true` is sent, the billing period will not change and a prorated charge
  will be issued for the new product."* So **Migration always applies immediately (today)** regardless of
  `PreservePeriod` — the flag chooses between "full price, new period starts now" (`false`, the default) and
  "prorated charge, current period preserved" (`true`, = the "apply now with proration" behavior asked for).
  There is **no** "prorate but also wait until renewal" combination via Migration.
- **Step 8 (redundant proration field):** `SubscriptionProductMigration.Proration: Proration?` is, per its
  own doc comment on `Models/Proration.cs`, *"the alternative to sending `preserve_period` as a direct
  attribute to migration"* — i.e. the same `preserve_period` boolean can be set either as the top-level
  `PreservePeriod` field or nested as `Proration.PreservePeriod`. Set **one**, not both, to avoid ambiguity
  (behavior when both are set to different values is undocumented).
- **Step 8 (delayed/at-renewal path has no preview — Blocker, see §4):** `PreviewSubscriptionProductMigration`
  only previews the Migration flow (immediate, proration-controlled by `PreservePeriod`). The delayed,
  no-proration path (`UpdateSubscription` with `ProductChangeDelayed = true`) is a structurally different
  endpoint family (`Subscriptions`, not `SubscriptionProducts`) and has no corresponding preview operation
  anywhere in the SDK's 247 operations.
- **Step 8 (canceling a pending delayed change):** per `UpdateSubscription`'s operation notes, set
  `NextProductId = ""` (empty string) to cancel a previously-scheduled delayed product change — this is why
  `UpdateSubscription.NextProductId` is typed `string?` even though `Subscription.NextProductId` (the
  read-back field) is `int?`.
- **Step 9 (pause → which `SubscriptionState`):** `PauseSubscription` calls `POST …/hold.json`, and
  `SubscriptionState` separately declares **both** `Paused (paused)` and `OnHold (on_hold)` as distinct
  members. The map/source doesn't state outright which one a paused subscription lands in — treat this as
  unverified until you read back the subscription with `ReadSubscription` after your first sandbox
  `PauseSubscription` call, and assert against whichever value you actually observe (most consistent with
  the `hold`/`resume`/`automatically_resume_at` naming is `OnHold`, but confirm on the wire — see the
  "verify on the wire" note in dotnet-configuration-resilience for attaching a logging `DelegatingHandler`
  on first run).
- **Step 9 (resume is calendar-billing-only param):** `ResumeSubscription`'s `calendarBillingResumptionCharge`
  query param (`ResumptionCharge?`) only affects calendar-billing subscriptions; for these monthly
  fixed-interval products, always pass `null`.
- **Step 9 (immediate vs delayed cancel share a request type but different endpoints):**
  `CancelSubscription` (DELETE, immediate) and `InitiateDelayedCancellation` (POST, end-of-period) both take
  `CancellationRequest{ Subscription: CancellationOptions }`; the *endpoint*, not a request field, decides
  which behavior you get — don't try to make `CancelSubscription` delayed by setting
  `CancelAtEndOfPeriod`/`ScheduledCancellationAt` unless your site has the "Schedule Subscription
  Cancellation" feature enabled (per the operation's own notes); use `InitiateDelayedCancellation` instead,
  which works unconditionally.
- **All Case-A operations:** enumerate every `TryGet…` accessor the map row lists (typed body first,
  status-specific `RawError` accessors next, `TryGetRawError` **last**) — don't write a single
  `if (TryGetRawError(...))` and stop, or every typed 422/404 body is silently dropped.
- **Retries:** default `RetryOptions` only auto-retries `GET/HEAD/PUT/OPTIONS`. Of the writes in §2.2, only
  `UpdateSubscription` (step 8c, `PUT`) and `ReactivateSubscription` (step 9f, `PUT`) qualify. Everything
  else — `CreateCustomer`, `CreateSubscription`, `CreateUsage`, `MigrateSubscriptionProduct` (`POST`),
  `PauseSubscription`/`ResumeSubscription`/`InitiateDelayedCancellation` (`POST`), `CancelSubscription`/
  `CancelDelayedCancellation` (`DELETE`) — is **not** retried by the SDK; a transient 503 surfaces
  immediately as an exception from these calls.

---

## 4. Assumptions & Blockers

**Assumptions:**
- The product family, both products, and the `api-call` component already exist in the
  `apimatic-hackathon` sandbox (per the ids given) — this plan's "startup validation" step only **reads**
  them (`ReadProductFamily`/`ListProductsForProductFamily`/`ListComponentsForProductFamily`/`ReadComponent`);
  no `CreateProduct`/`CreateMeteredComponent` calls are in scope.
- "Stable external reference" for `Customers.CreateCustomer`/`ReadCustomerByReference` is assumed to be the
  eShopOnWeb user's application id (or normalized email) — either works since `reference` is just a unique
  string on the Maxio side; pick one and use it consistently for both the lookup and the create.
- `Product.RequireCreditCard` (not `RequestCreditCard`) is treated as the authoritative "requires payment
  method" flag; both fields exist on the `Product` record with no XML-doc summary distinguishing them in the
  map, so this is an assumption, not a confirmed fact — if subscription creation unexpectedly demands a
  payment profile despite `RequireCreditCard == false`, re-check `RequestCreditCard` too. **Superseded in
  part by §5 below:** the *create-time* `CreateOrUpdateProduct.RequireCreditCard` field is documented in
  source as deprecated/legacy — see §5's seed-script Blocker before relying on it to actually enforce
  "no payment method required" for these two products.
- No trial/setup-fee/taxable configuration requires any special-case code in `MaxioBillingClient` — these
  are pure product-configuration facts on the Maxio side (`Product.TrialPriceInCents`/`TrialInterval` null,
  `Product.Taxable` false) that the client only needs to read back for validation, never write.

**Blockers (surface to the user, don't invent behavior):**
- **Delayed/at-renewal product change has no preview operation.** The SDK supports "apply at next renewal,
  no proration" via `Subscriptions.UpdateSubscription` (`ProductChangeDelayed = true`), but there is **no**
  SDK operation that previews this path's charges before committing — `PreviewSubscriptionProductMigration`
  only previews the immediate Migration flow. If a "preview the delayed change" UI requirement is firm, it
  cannot be satisfied by this SDK version; the closest approximation is to preview the *immediate* migration
  (`PreviewSubscriptionProductMigration` with `PreservePeriod = true`) as a proxy figure and label it as such
  in the UI, or omit the preview for the delayed path entirely.
- **Which `SubscriptionState` a paused subscription reports is unverified from the map/source alone** (see
  Trap notes, Step 9) — both `Paused` and `OnHold` exist as distinct enum members and neither the map nor the
  XML docs state which one `PauseSubscription` produces. Confirm against a real sandbox call before writing
  any code that branches on this specific enum value (e.g. "show a Resume button when state == X").

---

## 5. UC0 — Seed data (one-time script, not wired into the storefront)

**Why this exists:** live verification against the sandbox (`GET
https://apimatic-hackathon.chargify.com/product_families.json`) returned `[]` — the family/products/component
in this plan's target description do **not** yet exist at ids `3008866`/`7111477`/`7111478`/`3033795`. Those
ids must be **created**, not just read back. This section is additive to §§1-4 (which still govern the
storefront's own read/consume-only calls once seeding is done) — write this as a small standalone script
(e.g. a console project under `src/`, or a top-level `Program.cs` guarded by an env flag), not part of
`MaxioBillingClient`.

### 5.1 Seed operations, in required order

| # | Operation | Signature | Request fields (what to set) | Response → new id to capture | Error case | Map page |
|---|---|---|---|---|---|---|
| 0a | `ProductFamilies.CreateProductFamily` | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` | `body.ProductFamily: CreateProductFamily` → `Name = "eShopSubscribe" !req`, `Handle = "eshop-subscribe"` (optional string, no format constraint documented in source beyond being a plain nullable `string?`), `Description` optional | `ProductFamilyResponse.ProductFamily?.Id: int?` → save as the new family id (config value replacing `3008866`) | Case A `SdkException<CreateProductFamilyError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/ProductFamilies.md`, `Models/CreateProductFamily.cs` |
| 0b | `Products.CreateProduct` ×2 (Pro Plan, Basic Plan) | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` | `productFamilyId` = the family id from 0a **as a string** (this controller's `productFamilyId` param is `string`, unlike `Components`' `int` — see existing Trap notes); `body.Product: CreateOrUpdateProduct` → see §5.2 for the exact per-field values for each plan | `ProductResponse.Product !req.Id: int?` → save each as the new product id (replacing `7111477`/`7111478`) | Case A `SdkException<CreateProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/Products.md`, `Models/CreateOrUpdateProduct.cs` |
| 0c | `Components.CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` | `productFamilyId` = the family id from 0a **as a string** (this operation's own `productFamilyId` param is `string`, matching `Products.CreateProduct` — not the `int` used by `Components.ListComponentsForProductFamily`/`ReadComponent`); `body.MeteredComponent: MeteredComponent` → see §5.3 | `ComponentResponse.Component !req.Id: int?` → save as the new component id (replacing `3033795`) | Case A `SdkException<CreateMeteredComponentError>` — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/Components.md`, `Models/MeteredComponent.cs` |

**Order dependency:** 0a **must** run first — both 0b and 0c take the family id/handle as a required path
segment and will 404/422 without an existing family. Between 0b and 0c there is **no** dependency either
way: `CreateMeteredComponent`'s route is `POST /product_families/{product_family_id}/metered_components.json`
— scoped to the **family**, not to any product — so the component can be created before, after, or
interleaved with the two `CreateProduct` calls. (Nothing in the map or source ties a metered component's
creation to a product existing first; components are attached to *subscriptions* later via allocation/usage,
never to a product record itself.)

### 5.2 `CreateOrUpdateProduct` — exact field values for both plans (`Models/CreateOrUpdateProduct.cs`)

| Field | Type | Pro Plan value | Basic Plan value | Note |
|---|---|---|---|---|
| `Name` | `string !req` | `"Pro Plan"` | `"Basic Plan"` | — |
| `Handle` | `string?` | `"eshop-pro"` | `"basic-plan"` | optional but set explicitly to match the target handles |
| `Description` | `string !req` | any non-null string | any non-null string | **required** — cannot omit, unlike `Handle` |
| `PriceInCents` | `long !req` | `29900` | `2900` | **integer cents**, not a decimal string — doc comment: *"The product price, in integer cents"* (`$299.00` → `29900`, `$29.00` → `2900`) |
| `Interval` | `int !req` | `1` | `1` | paired with `IntervalUnit` |
| `IntervalUnit` | `IntervalUnit !req` | `IntervalUnit.Month` | `IntervalUnit.Month` | "monthly" = `Interval=1, IntervalUnit=Month` |
| `TrialPriceInCents`/`TrialInterval`/`TrialIntervalUnit`/`TrialType` | all `?`, none `!req` | leave **null/omitted** | leave **null/omitted** | "no trial" = omission, not zero values — all four are independently optional |
| *(setup/initial charge)* | — | **no field exists** | **no field exists** | `CreateOrUpdateProduct` has **no `InitialChargeInCents`/setup-fee field at all** (confirmed absent from `Models/CreateOrUpdateProduct.cs` — only the read-only `Product` response model has `InitialChargeInCents`). "No setup fee" is therefore not a choice you make here — this create operation cannot set one either way |
| `ExpirationInterval`/`ExpirationIntervalUnit` | `int?` / `ExpirationIntervalUnit?` | leave **null/omitted** | leave **null/omitted** | doc comment confirms `ExpirationIntervalUnit` has an explicit `Never` member ("either month, day or never"), but the source doesn't document whether setting `ExpirationIntervalUnit.Never` alone (without `ExpirationInterval`) is valid/necessary — omitting both is the simpler, safer way to express "expires never" and matches every other optional-pair field in this model |
| *(taxable flag)* | — | **no boolean field exists** | **no boolean field exists** | only `TaxCode: string?` exists on this model — there is **no** `Taxable: bool` create-time field (unlike the `Product`/`MeteredComponent` *response* models, which do have one). "Non-taxable" = leave `TaxCode` null/omitted; there's no separate switch to flip |
| `RequireCreditCard` | `bool? ` | `false` | `false` | **read the caveat below before relying on this** |

**Blocker — `RequireCreditCard`'s own doc comment calls it deprecated/legacy-only:**
`Models/CreateOrUpdateProduct.cs` line 34-39:
```csharp
/// <summary>
/// Deprecated value that can be ignored unless you have legacy hosted pages. For Public Signup Page
/// users, read this attribute from under the signup page.
/// </summary>
[JsonPropertyName("require_credit_card")]
public bool? RequireCreditCard { get; init; }
```
This is the **only** payment-method-requirement field this SDK's product-create/update model exposes — there
is no other lever here. But its own doc comment says it's meaningful only for legacy hosted pages, and that
for Public Signup Page (modern) setups the actual requirement lives elsewhere ("under the signup page"),
which this SDK has **no operation for** (no Public Signup Page controller appears in the 33-controller map).
**Practical guidance for the seed script:** set `RequireCreditCard = false` on both products anyway (it's the
only exposed knob, costs nothing to set, and may still apply on non-legacy sites despite the deprecation
note) — but do **not** treat its presence as a confirmed guarantee that `CreateSubscription` will never
prompt/require a payment profile for these products. Verify the actual behavior empirically in the sandbox
(create a subscription with no `PaymentProfileAttributes` and confirm it succeeds) rather than trusting this
field alone — this is exactly the kind of fact only live traffic can confirm, so treat it as unverified until
you've done that one sandbox call.

**CONFIRMED empirically (live sandbox, ids `3023074`/`7126957`/`7126958`/`3057195`):** `RequireCreditCard =
false` did **not** suppress the requirement — `Subscriptions.CreateSubscription` with only `CustomerId` +
`ProductHandle = "eshop-pro"` set (no `PaymentProfileAttributes`/`CreditCardAttributes`/
`BankAccountAttributes`) was rejected with `"No payment method was on file for the $299.00 balance"`
(`SdkException<CreateSubscriptionError>`). This is now a confirmed capability gap, not a suspicion.

**Follow-up, checked against `Models/CreateSubscription.cs` field-by-field (all 50 fields, doc comments read
in full) — is there ANY documented field that waives/defers the payment-method requirement?** No field is
documented as an explicit "skip payment method" / "no bill" / "collection-method = none" switch — the
closest two candidates, neither of which is documented to do exactly this:
- **`PaymentCollectionMethod: CollectionMethod?`** — doc comment only describes *which* collection method is
  used ("legacy Statements Architecture: `invoice`, `automatic`"; "Relationship Invoicing: `remittance`,
  `automatic`, `prepaid`") — it does **not** state that any value waives the payment-method-on-file check.
  `CollectionMethod`'s own enum members (`map/models/enums.md`) are only `Automatic`/`Remittance`/`Prepaid`/
  `Invoice` — there is no `None`/`Skip` member.
- **`NextBillingAt: DateTimeOffset?`** — this is the only field whose doc comment documents deferring a
  charge: *"If you provide a `next_billing_at` timestamp that is in the future, no trial or initial charges
  will be applied when you create the subscription. In fact, no payment will be captured at all. The first
  payment will be captured...near the time specified by `next_billing_at`."* This defers **charge capture**,
  but the doc comment does not say it waives the payment-method-on-file validation itself (the error message
  observed is about a missing payment method for the balance, not about a failed charge attempt) — whether
  setting a future `NextBillingAt` also avoids that specific validation is **unverified**; it would need its
  own live sandbox test, which is outside what source/map grounding can confirm.

**Conclusion — report as a definitive capability gap:** as modeled by this SDK (`CreateOrUpdateProduct` +
`CreateSubscription`, every field and doc comment checked), there is no confirmed, documented way to create a
subscription against these products without either supplying a payment profile
(`PaymentProfileAttributes`/`CreditCardAttributes`/`BankAccountAttributes`) or empirically testing the
undocumented `NextBillingAt` deferral as an unproven workaround. Do not invent a test card number or payment
profile shape not already in this sheet's `CreateSubscription` field list — none is grounded here.

### 5.3 `MeteredComponent` — exact field values for `api-call` (`Models/MeteredComponent.cs`)

| Field | Type | Value | Note |
|---|---|---|---|
| `Name` | `string !req` | `"API Calls"` | — |
| `UnitName` | `string !req` | e.g. `"call"` (singular — doc comment: *"should be singular since it will be automatically pluralized when necessary"*) | required, no default given by the coordinator's spec beyond the component name — pick a singular unit noun |
| `Handle` | `string?` | `"api-call"` | doc comment gives an explicit format constraint: *"Must start with a letter or number and may only contain lowercase letters, numbers, or the characters '.', ':', '-', or '_'."* — `"api-call"` satisfies this |
| `PricingScheme` | `PricingScheme !req` | `PricingScheme.PerUnit` | — |
| `UnitPrice` | `UnitPrice1?` (`AnyOf<string, double>`, namespace `MaxioAdvancedBilling.Models.AnyOf`) | `UnitPrice1.String("0.01")` or `UnitPrice1.Double(0.01)` | doc comment: *"The amount the customer will be charged per unit when the pricing scheme is 'per_unit'... can contain up to 8 decimal places"* — **this is the field to set for a flat per-unit price**, built via the union's static factory (`UnitPrice1.String(...)`/`.Double(...)`), never `new UnitPrice1(...)` |
| `Prices` | `IReadOnlyList<Price>?` | leave **null** | doc comment explicitly states: *"(Not required for 'per_unit' pricing schemes) One or more price brackets"* — confirms `Prices` (tiered/volume brackets, `Models/Price.cs`: `StartingQuantity !req`, `EndingQuantity?`, `UnitPrice !req` per bracket) is **not** needed for `PerUnit`; only the flat `UnitPrice` field above is required |
| `Taxable` | `bool?` | `false` (non-taxable, matches the family's products) | — |

**Naming trap for the seed script:** the operation method `Components.CreateMeteredComponent(...)` and its
own request body's wrapper type `Models.CreateMeteredComponent` (→ `MeteredComponent (metered_component):
MeteredComponent !req`, `map/models/records-1-Ac-Cr.md:141`) share the **identical name** `CreateMeteredComponent`
— one is a method on `client.Components`, the other is a `record` type under `MaxioAdvancedBilling.Models`.
They're distinguishable by context (`client.Components.CreateMeteredComponent(...)` vs `new
CreateMeteredComponent { MeteredComponent = new MeteredComponent { ... } }`) as long as
`using MaxioAdvancedBilling.Models;` is in scope, but don't be surprised by the apparent recursion when
reading the call site.

### 5.4 Capturing the new ids

| Seeded entity | Response path to the new id |
|---|---|
| Product family | `ProductFamilyResponse.ProductFamily.Id: int?` (from 0a) |
| Pro Plan product | `ProductResponse.Product.Id: int?` (from the 0b call with `Handle = "eshop-pro"`) |
| Basic Plan product | `ProductResponse.Product.Id: int?` (from the 0b call with `Handle = "basic-plan"`) |
| `api-call` component | `ComponentResponse.Component.Id: int?` (from 0c) |

Write these four ids into whatever config source `MaxioBillingClient`/`MaxioSettings` reads (§1-§4 assume
they're already correct there) — the seed script's whole purpose is to produce them once, by hand-checking
the script's console output or a small results file, not to wire itself into the app's config automatically.
