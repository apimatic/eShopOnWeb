# Maxio Advanced Billing .NET SDK — eShopOnWeb "Subscriptions" module

Site subdomain: `apimatic-hackathon` · Product family: `eshop-subscribe` (id `3008866`) ·
Plans: `eshop-pro` (id `7111477`, $299/mo), `basic-plan` (id `7111478`, $29/mo) ·
Metered component: `api-call` (id `3033795`, kind `Metered`, $0.01/unit)

Grounded entirely from `maxio-getting-started` skill's `sdk-map.md` + `map/operations/*.md` +
`map/models/*.md` (SDK version `1.0.0`, tag `v1.0.2`, commit `15db14b`). No web-search or
training-data facts were used; every row below cites the map page it came from.

**Source-lookup follow-up (this session, resolved)**: a debugging-capable agent with Bash/git access
cloned the SDK source (`git clone --depth 1 --branch v1.0.2 https://github.com/asadali214/advanced-billing-sample-sdk`,
resolved commit `15db14b2e663ebe9e957e061bd67634630429035` — matches this file's header stamp exactly) into
`C:\claude-runs\exp1-plugin-exp-sonnet5high-002\tmp\maxio-sdk-src\20260712-210114` and opened the exact
files named for each of the 4 blockers below. All 4 are now resolved from source and folded into §2.2,
§2.4, §2.5, §2.6, and §4 below. The clone remains in place at that path for later reuse this session.

---

## 1. Scope & sequence

Build one `MaxioBillingClient : IBillingClient` (Infrastructure layer) wrapping a single
long-lived `MaxioAdvancedBillingClient`. Implementation order:

1. **Client construction & config** — `MaxioAdvancedBillingClientOptions` with Basic auth,
   `ServerEnvironment` (US/EU), and a base-URL-overrides-subdomain rule. (`dotnet-client-initialization`,
   `dotnet-authentication`, `dotnet-configuration-resilience`)
2. **Catalog resolution** — `ProductFamilies.ReadProductFamily`, `ProductFamilies.ListProductsForProductFamily`
   (or `Products.ReadProduct`/`ReadProductByHandle`), `Components.ReadComponent`/`FindComponent` to
   resolve/validate the two plans and the `api-call` metered component at startup.
3. **Customer lookup-or-create** — `Customers.ReadCustomerByReference` (keyed on the eShopOnWeb
   user id/email) falling back to `Customers.CreateCustomer` on a 404.
4. **Subscription enrollment** — `Customers.ListCustomerSubscriptions` (double-enrollment guard) then
   `Subscriptions.CreateSubscription`.
5. **"My subscriptions" read** — `Customers.ListCustomerSubscriptions` / `Subscriptions.ReadSubscription`.
6. **Usage metering** — `SubscriptionComponents.CreateUsage` to record usage, `SubscriptionComponents.ReadSubscriptionComponent`
   (period `UnitBalance`) or `SubscriptionComponents.ListUsages` (raw ledger, client-summed) to read back totals.
7. **Plan change** — `SubscriptionProducts.PreviewSubscriptionProductMigration` then
   `SubscriptionProducts.MigrateSubscriptionProduct` (immediate path); `Subscriptions.UpdateSubscription`
   with `ProductChangeDelayed=true` (next-renewal path).
8. **Lifecycle transitions** — `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` /
   `CancelSubscription` / `InitiateDelayedCancellation` / `CancelDelayedCancellation` / `ReactivateSubscription`,
   gated client-side on `Subscription.State`.
9. **Error normalization layer** — one `try/catch` shape per Case A/B operation type (see sheet), mapped to
   `IBillingClient`'s own exception/result types.

---

## 2. CONTRACT SHEET

### 2.1 Catalog (product family, products/plans, components)

| Operation | Signature (params in order) | Request model | Response envelope → fields read | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse` → `ProductFamily (product_family): ProductFamily?` → `Id, Name, Handle, ArchivedAt` | B: `SdkException<RawError>` (`StatusCode`,`ReadAsString()`,`ReadAsJson<T>()`,`ReadAsBytes()`) | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable filters **must be passed explicitly** (`null` to skip) | — | `IReadOnlyList<ProductResponse>` → each `Product (product): Product !req` → `Id, Name, Handle, PriceInCents, IntervalUnit, RequireCreditCard, ArchivedAt` | A: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError` [fallback] | manual `page`+`perPage` | `operations/ProductFamilies.md` |
| `client.Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` → `Product (product): Product !req` (same fields as above) | B: `RawError` | none | `operations/Products.md` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` | B: `RawError` | none | `operations/Products.md` |
| `client.Components.ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — **note: `componentId` is `string` here** (pass `"3033795"`) | — | `ComponentResponse` → `Component (component): Component !req` → `Kind (kind): ComponentKind?`, `UnitPrice`/`PricePerUnitInCents`, `Archived` | B: `RawError` | none | `operations/Components.md` |
| `client.Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | — | `ComponentResponse` | B: `RawError` | none | `operations/Components.md` |

**Validate metered kind**: `Component.Kind` (`ComponentKind` enum) must equal `metered_component` — see enum table below.

### 2.2 Customer lookup-or-create

| Operation | Signature | Request model | Response envelope → fields | Error case + accessors | Map page |
|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — | `CustomerResponse` → `Customer (customer): Customer !req` → `Id, Reference, Email, FirstName, LastName` | B: `RawError` — **a not-found reference throws `SdkException<RawError>` with `StatusCode==404`**; treat this specific status as "no existing customer", rethrow anything else | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest{ Customer (customer): CreateCustomer !req }`; `CreateCustomer{ FirstName !req, LastName !req, Email !req, Reference (reference): string?, Organization?, Address?, ... }` — **`Reference` is the idempotency key**: set it to the eShopOnWeb user id/email | `CustomerResponse` → `Customer.Id` (int, use as `customer_id` everywhere else) | A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | `operations/Customers.md`, `records-1-Ac-Cr.md` (`CreateCustomer`), `records-2-Cr-Pa.md` (`CustomerResponse`, `Customer`, `CustomerErrorResponse1`) |

`CustomerErrorResponse1{ Errors (errors): Errors? }`; `Errors{ PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }`.

**CONFIRMED from source** (`Errors/CreateCustomerError.cs`, `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs`): this
*is* genuinely the only typed 422 shape the SDK wires to `CreateCustomer` — `CreateCustomerError.Create` switches
`422 => FromJson<CustomerErrorResponse1>(...)` with no other case, and `CustomerErrorResponse1.Errors` is exactly
`{ PerPage: IReadOnlyList<string>?, PricePoint: IReadOnlyList<string>? }` (source-confirmed, not a map transcription
error). There is no alternate/better error model wired to this operation — in particular, `ErrorListResponse1{ Errors:
IReadOnlyList<string> }` (the "plain array of validation-message strings" shape, confirmed from `Models/ErrorListResponse1.cs`)
*is* used elsewhere in this SDK (e.g. `CreateSubscription`, `CreateUsage`, `MigrateSubscriptionProduct` 422s) but is
**not** wired to `CreateCustomerError` — only `CustomerErrorResponse1` is. The `per_page`/`price_point` field names read
as mismatched for customer-validation content (duplicate reference/email) because they are — this looks like a
generation artifact in this sample SDK (fields belonging to a different resource's error schema reused here) rather
than something this integration can rely on to surface "reference already in use" text. **Recipe**: call
`ex.Error.TryGetCustomerErrorResponse1(out var e422)` first per the map/source contract, but treat its `PerPage`/`PricePoint`
lists as unreliable/likely-empty for real duplicate-reference/email errors, and always also keep the `TryGetRawError`
fallback path live and parse the raw JSON body defensively for the actual message text the UI needs (see the trap note
in §3, now confirmed rather than provisional).

### 2.3 Subscription creation & listing

| Operation | Signature | Request model | Response envelope → fields | Error case + accessors | Map page |
|---|---|---|---|---|---|
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → each `Subscription (subscription): Subscription?` → `Id, State, Product, ProductPricePointId, CurrentPeriodEndsAt` | B: `RawError` | `operations/Customers.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest{ Subscription (subscription): CreateSubscription !req }`; key `CreateSubscription` fields for this use case: `ProductId (product_id): int?` (or `ProductHandle`), `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?` (or `CustomerReference (customer_reference): string?`), `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `PaymentProfileAttributes`/`CreditCardAttributes`/`BankAccountAttributes`/`PaymentProfileId` — **all optional; omit them entirely for "no card capture"** (see trap note) | `SubscriptionResponse` → `Subscription.Id, State` | A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/Subscriptions.md`, `records-1-Ac-Cr.md` (`CreateSubscription`, `CreateSubscriptionRequest`) |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed explicitly (`null` ok) | — | `SubscriptionResponse` | B: `RawError` | `operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed explicitly | — | `SubscriptionResponse` | A: `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | `operations/Subscriptions.md` |

**No-card-capture note**: the SDK exposes no per-subscription "skip card capture" flag on `CreateSubscription`. Card capture is governed by the **product/plan's own** `RequireCreditCard` setting (`Product.RequireCreditCard: bool?`, set at plan level via `CreateOrUpdateProduct.RequireCreditCard`). Confirm at startup (step 2 of scope) that `eshop-pro`/`basic-plan` have `RequireCreditCard == false`, then simply build `CreateSubscription` **without** any `PaymentProfileAttributes`/`CreditCardAttributes`/`BankAccountAttributes`/`PaymentProfileId`.

### 2.4 Usage metering (metered component `api-call`)

| Operation | Signature | Request model | Response envelope → fields | Error case + accessors | Map page |
|---|---|---|---|---|---|
| `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly. `SubscriptionIdOrReference`/`ComponentIdModel` are `AnyOf<int,string>` unions — build with `SubscriptionIdOrReference.Int(id)` / `ComponentIdModel.Int(3033795)` (or `.String(...)`), **not `new`** | `CreateUsageRequest{ Usage (usage): CreateUsage !req }`; `CreateUsage{ Quantity (quantity): double?, Memo (memo): string?, PricePointId (price_point_id): string?, BillingSchedule?, CustomPrice? }` | `UsageResponse` → `Usage (usage): Usage !req` → `Id, Quantity (Quantity1 AnyOf<int,string>), Memo, CreatedAt` | A: `SdkException<CreateUsageError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/SubscriptionComponents.md`, `records-1-Ac-Cr.md` (`CreateUsageRequest`,`CreateUsage`), `records-4-Su-We.md` (`Usage`,`UsageResponse`), `unions.md` (`SubscriptionIdOrReference`,`ComponentIdModel`,`Quantity1`) |
| `client.SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — **note: `componentId` is `int` here** (differs from `Components.ReadComponent`'s `string`) | — | `SubscriptionComponentResponse` → `Component (component): SubscriptionComponent?` → **`UnitBalance (unit_balance): int?`** — see resolved semantics below | A: `SdkException<ReadSubscriptionComponentError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | `operations/SubscriptionComponents.md`, `records-3-Pa-Su.md` (`SubscriptionComponent`, `SubscriptionComponentResponse`) |
| `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 nullable filters must be passed explicitly | — | `IReadOnlyList<UsageResponse>` (raw usage ledger — safe, source-independent alternative: sum `Usage.Quantity` client-side over the current billing period using `Subscription.CurrentPeriodStartedAt`/`CurrentPeriodEndsAt` as the date bounds via `sinceDate`/`untilDate`, instead of relying on `UnitBalance`) | B: `RawError` | manual `page`+`perPage` | `operations/SubscriptionComponents.md` |

**`UnitBalance` semantics — resolved from source** (`Api/SubscriptionComponents.cs`, `Models/SubscriptionComponent.cs`):
neither file states in one explicit sentence "resets every billing cycle", but the source supports **period-to-date**
as the correct reading, not lifetime: `CreateUsage`'s XML remarks say *"The `quantity` from usage for each component
is accumulated to the `unit_balance` ... for the subscription"* and *"The `unit_balance` has a floor of `0`; negative
unit balances are never allowed"* — i.e. it is a running total that can be driven down by posting a negative
`quantity` ("Deducting Usage"), not an immutable lifetime counter. Corroborating cross-file evidence (from
`Api/SubscriptionStatus.cs`'s `PreviewRenewal` remarks, read incidentally while resolving item 2): the renewal
preview explicitly lists *"Current metered usage `unit_balance` for metered components"* as one of the **current
billing period's** inputs to computing the **next** renewal charge — which only makes sense if `unit_balance` is
scoped to the period being renewed (a lifetime counter would double-count on every renewal). **Recipe**: treat
`SubscriptionComponent.UnitBalance` as the current period's accumulated usage for that metered component; it is
safe to read directly via `ReadSubscriptionComponent` for the "usage so far this period" figure. The plan's
existing `ListUsages`-sum-over-period fallback remains a valid belt-and-braces alternative but is no longer
required as the primary mechanism.

### 2.5 Plan change (preview + commit, two timing paths)

| Operation | Signature | Request model | Response envelope → fields | Error case + accessors | Map page |
|---|---|---|---|---|---|
| `client.SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `SubscriptionMigrationPreviewRequest{ Migration (migration): SubscriptionMigrationPreviewOptions !req }`; `SubscriptionMigrationPreviewOptions{ ProductId?, ProductHandle?, ProductPricePointId?, ProductPricePointHandle?, IncludeTrial?, IncludeInitialCharge?, IncludeCoupons?, PreservePeriod (preserve_period): bool?, Proration (proration): Proration?, ProrationDate (proration_date): DateTimeOffset? }`; `Proration{ PreservePeriod (preserve_period): bool? }` | `SubscriptionMigrationPreviewResponse` → `Migration (migration): SubscriptionMigrationPreview !req` → `ProratedAdjustmentInCents, ChargeInCents, PaymentDueInCents, CreditAppliedInCents` | A: `SdkException<PreviewSubscriptionProductMigrationError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/SubscriptionProducts.md`, `records-4-Su-We.md` |
| `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `SubscriptionProductMigrationRequest{ Migration (migration): SubscriptionProductMigration !req }`; `SubscriptionProductMigration{ ProductId?, ProductHandle?, ProductPricePointId?, ProductPricePointHandle?, IncludeTrial?, IncludeInitialCharge?, IncludeCoupons?, PreservePeriod (preserve_period): bool?, Proration (proration): Proration? }` — **this operation applies immediately (its HTTP verb is a one-shot `POST .../migrations.json`, no schedule param)** | `SubscriptionResponse` → `Subscription.Id, State, ProductPricePointId` | A: `SdkException<MigrateSubscriptionProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/SubscriptionProducts.md`, `records-4-Su-We.md` |
| `client.Subscriptions.UpdateSubscription` (next-renewal / delayed path) | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `UpdateSubscriptionRequest{ Subscription (subscription): UpdateSubscription !req }`; relevant fields: `ProductId (product_id): int?`, `ProductHandle?`, `ProductChangeDelayed (product_change_delayed): bool?` — **set `true` to defer the switch to next renewal**, `NextProductId (next_product_id): string?`, `NextProductPricePointId (next_product_price_point_id): string?` | `SubscriptionResponse` | A: `SdkException<UpdateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/Subscriptions.md`, `records-4-Su-We.md` (`UpdateSubscription`) |

**Proration/timing recipe — resolved from source** (`Api/SubscriptionProducts.cs`, `Api/Subscriptions.cs`,
`Models/Proration.cs`, `Models/SubscriptionProductMigration.cs`, `Models/UpdateSubscription.cs`):

- **(a) Apply now with proration** → `SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, body)`
  — this endpoint is a one-shot `POST /subscriptions/{id}/migrations.json`, always immediate. Build
  `SubscriptionProductMigrationRequest{ Migration = new SubscriptionProductMigration{ ProductId (or ProductHandle),
  ProductPricePointId (optional), PreservePeriod = true } }`. `SubscriptionProductMigration.PreservePeriod`'s doc-comment
  is explicit: *"If `false` is sent, the subscription's billing period will be reset to today and the full price of the
  new product will be charged. If `true` is sent, the billing period will not change and a prorated charge will be
  issued for the new product."* — so `PreservePeriod = true` is exactly "prorated, mid-period, now". (The nested
  `SubscriptionProductMigration.Proration.PreservePeriod` field, per `Models/Proration.cs`'s doc-comment — *"The
  alternative to sending preserve_period as a direct attribute to migration"* — is a wire-format alternative for the
  same flag; setting the direct `PreservePeriod` field is sufficient, no need to also populate `Proration`.)
- **(b) Apply at next renewal without proration** → `Subscriptions.UpdateSubscription(subscriptionId, body)`, **not**
  `MigrateSubscriptionProduct` (which cannot defer). Build `UpdateSubscriptionRequest{ Subscription = new
  UpdateSubscription{ ProductHandle (or ProductId), ProductChangeDelayed = true, ProductPricePointId/
  ProductPricePointHandle (optional) } }`. The operation's XML remarks are explicit: *"To perform a delayed product
  change, set the `product_handle` attribute as you would in a regular product change, but also set the
  `product_change_delayed` attribute to `true`. **No proration applies in this case.**"* — confirming (b) is
  proration-free by design, and confirming the target product is set via `ProductId`/`ProductHandle` (**not**
  `NextProductId`/`NextProductPricePointId`). **Correction to an earlier assumption**: `UpdateSubscription.NextProductId`
  is *not* the way to set the delayed target — per its own doc-comment (*"Set to an empty string to cancel a delayed
  product change"*) it is a **cancel-only** field (send `NextProductId = ""` to cancel an already-scheduled delayed
  change); it does not accept a new target product id.

### 2.6 Lifecycle transitions

| Operation | Signature | Request model | Response envelope → fields | Error case + accessors | Map page |
|---|---|---|---|---|---|
| `client.SubscriptionStatus.PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly (`null` ok) | `PauseRequest{ Hold (hold): AutoResume? }`; `AutoResume{ AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset? }` | `SubscriptionResponse` → `Subscription.State` | A: `SdkException<PauseSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — must be passed explicitly (`null` ok) | — (enum param only) | `SubscriptionResponse` | A: `SdkException<ResumeSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CancellationRequest{ Subscription (subscription): CancellationOptions !req }`; `CancellationOptions{ CancellationMessage?, ReasonCode?, CancelAtEndOfPeriod (cancel_at_end_of_period): bool? — **immediate=false/omit, end-of-period=true**, ScheduledCancellationAt?, RefundPrepaymentAccountBalance? }` | `SubscriptionResponse` → `Subscription.State` (expect `canceled`) | A: `SdkException<CancelSubscriptionApiError>` — `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] (union: `TryGetErrorListResponse1`/`TryGetSingleErrorResponse1`) · `TryGetRawError` [fallback] | `operations/SubscriptionStatus.md`, `unions.md` (`CancelSubscriptionErrorResponse`) |
| `client.SubscriptionStatus.InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CancellationRequest` (same as above) | `DelayedCancellationResponse{ Message (message): string? }` | A: `SdkException<InitiateDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | — | `DelayedCancellationResponse` | A: `SdkException<CancelDelayedCancellationError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `ReactivateSubscriptionRequest{ CalendarBilling: ReactivationBilling?, IncludeTrial?, PreserveBalance?, CouponCode?, UseCreditsAndPrepayments?, Resume (resume): Resume? }` — `Resume` is `AnyOf<bool, ResumeOptions>`, build via `Resume.Bool(true)` or `Resume.ResumeOptions(...)` | `SubscriptionResponse` → `Subscription.State` | A: `SdkException<ReactivateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | `operations/SubscriptionStatus.md`, `records-3-Pa-Su.md`, `unions.md` (`Resume`) |

**Client-side legal-transition gate**: read `Subscription.State` (`SubscriptionState` enum, values below) from the last known `SubscriptionResponse` before calling any transition.

**Pause state — resolved from source** (`Api/SubscriptionStatus.cs`, `Models/Enums/SubscriptionState.cs`):
`PauseSubscription` (`POST /subscriptions/{id}/hold.json`, XML remarks: *"Places the subscription on hold, preventing
it from renewing."*) results in `SubscriptionState.OnHold` (`"on_hold"`), **not** the separate `paused` value. Confirmed
by `SubscriptionState`'s own enum doc-comment: `on_hold` — *"Indicates that a subscription's billing has been
temporarily stopped. While it is expected that the subscription will resume and return to active status, this is
still treated as an 'End of Life' state..."* — this is exactly the hold/resume lifecycle `PauseSubscription`/
`ResumeSubscription` describe. By contrast `paused` is documented as an unrelated **internal/billing-arrears** state
— *"An internal state that indicates that your account with Advanced Billing is in arrears"* — not something
`PauseSubscription` ever produces or that `ResumeSubscription` is meant to reverse. **State-machine guard recipe**:
`Resume` is legal only when `Subscription.State == SubscriptionState.OnHold`; `SubscriptionState.Paused` is not a
resumable state reachable via this SDK's pause/resume pair and should not be treated as one.

### 2.7 Enum value lists actually used (from `map/models/enums.md`)

| Enum | Values |
|---|---|
| `ComponentKind` | `metered_component`, `quantity_based_component`, `on_off_component`, `prepaid_usage_component`, `event_based_component` |
| `SubscriptionState` | `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup` |
| `SubscriptionStateFilter` (for `ListSubscriptions`, not `ListCustomerSubscriptions`) | `active`, `canceled`, `expired`, `expired_cards`, `on_hold`, `past_due`, `pending_cancellation`, `pending_renewal`, `suspended`, `trial_ended`, `trialing`, `unpaid` |
| `CollectionMethod` | `automatic`, `remittance`, `prepaid`, `invoice` |
| `ResumptionCharge` | `prorated`, `immediate`, `delayed` |
| `ReactivationCharge` | `prorated`, `immediate`, `delayed` |

### 2.8 Client construction / auth / server-node facts (`sdk-map.md` §"Getting a client" / §"Servers & auth")

- Constructor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — only constructor; `httpClient` is caller-owned (reuse one long-lived instance, don't build per-request — `dotnet-client-initialization`).
- Auth: `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` (literal `"x"` password, `Username` = the API key) — `dotnet-authentication`.
- Region: `options.Environment = ServerEnvironment.Us` (default) or `ServerEnvironment.Eu`.
- Subdomain: `options.Server.Production.Us.Site = "apimatic-hackathon"` (and `.Eu.Site` if EU).
- Base-URL override: `options.Server.Production.Us.BaseUrl = "<explicit-base-url>"` — set **only** `BaseUrl` when config supplies one; set **only** `Site` (subdomain) otherwise, so the configured `BaseUrl` (when present) wins without relying on undocumented precedence between the two.
- Templates: US → `https://{site}.chargify.com`; EU → `https://{site}.ebilling.maxio.com`.
- DI: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; })` (`ServiceCollectionExtensions.cs`).
- Retries: `options.Retry` (`RetryOptions`, Polly-backed) — all members are `required`; build from `RetryOptions.Default()` and override, or construct fully — `dotnet-configuration-resilience`.

---

## 3. Trap notes

- **Step 1 (client/DI)**: construct one `HttpClient`+`MaxioAdvancedBillingClient` per process lifetime (DI singleton), never per request — `dotnet-client-initialization`.
- **Step 1 (auth)**: set `BasicAuth` before/at construction (or inside the `AddMaxioAdvancedBillingClient` callback), load the API key from configuration, never hardcode — `dotnet-authentication`.
- **Step 2/3/4/6 (list/search calls)**: every list/find operation above (`ListProductsForProductFamily`, `ListCustomers`-style, `ListUsages`, etc.) has 4–14 nullable filter params with **no C# default** — call with **named arguments**, passing `null` for every filter you don't use, or they mis-bind positionally — `dotnet-calling-endpoints`.
- **Step 3/6 (unions)**: `SubscriptionIdOrReference`, `ComponentIdModel`, `Resume` are `AnyOf<...>` unions — build with the static factory (`ComponentIdModel.Int(3033795)`) or implicit conversion, never `new`; read back via `TryGet…` — `dotnet-models`.
- **Step 2/5/6 (enums)**: `ComponentKind`, `SubscriptionState`, `CollectionMethod`, `ResumptionCharge` are `StringEnum<T>`, not C# enums — compare via `== ComponentKind.MeteredComponent` static member or `Type.FromValue("metered_component")`, not a `switch` on a C# enum — `dotnet-models`.
- **Step 3 (envelope pattern)**: every write wraps its payload one level deep — `CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`, `CreateUsageRequest.Usage`, `CancellationRequest.Subscription`, etc. — build the inner object first, then wrap — `dotnet-models`.
- **Step 8 (error handling)**: 163/247 SDK operations are Case A (typed `{Operation}Error` with per-status `TryGet…`), 84/247 are Case B (`RawError` only, no typed accessors — mostly reads/lists/deletes, confirmed per-op above). A single normalization layer must branch on which case each call site throws; `TryGetRawError` is the fallback **only** on Case A's typed error, never a substitute for catching `SdkException<RawError>` directly on a Case B call — `dotnet-error-handling`.
- **Step 8 (no no-throw variant)**: this SDK has **no** `...Result`/non-throwing call style — every call is `try/catch`-only; don't design the error layer around a result object the SDK doesn't return — `dotnet-error-handling`.
- **Step 5/6 (component id type asymmetry)**: `Components.ReadComponent`'s `componentId` is `string`; `SubscriptionComponents.ReadSubscriptionComponent`'s `componentId` is `int` — don't assume one signature from the other.
- **Step 9 (base URL vs subdomain)**: `options.Server.Production.Us.BaseUrl` and `.Site` are two independent override points on the same object — set exactly one per environment resolution to keep "explicit `BaseUrl` wins" deterministic without depending on unconfirmed precedence rules — `dotnet-configuration-resilience`.
- **Step 8 (defensive 422 parsing, now confirmed not just precautionary)**: parse `CreateCustomerError`'s 422 body defensively — call `TryGetCustomerErrorResponse1`, but also keep a `TryGetRawError`/raw-JSON fallback path live, because the typed shape (`Errors{PerPage, PricePoint}`) is source-confirmed to carry unrelated field names and will not reliably surface duplicate-reference/duplicate-email validation messages (see §2.2).

---

## 4. Assumptions & Blockers

- **Assumption**: "requires payment method off" is achieved by (a) confirming the target plan's `Product.RequireCreditCard == false` via `ReadProduct`/`ReadProductByHandle`, and (b) omitting all payment-profile fields from `CreateSubscription` — the SDK has no separate per-subscription flag for this, only a product-level one.
- **Assumption**: the eShopOnWeb user's stable key (email or username, to be decided by the caller) is stored verbatim in `Customer.Reference`/`CreateCustomer.Reference` and used as the lookup key for `ReadCustomerByReference`.
- **Assumption**: a 404 from `Customers.ReadCustomerByReference` (a Case B `RawError`) is the "customer does not exist yet" signal that should route to `CreateCustomer`; any other status should propagate as a real error.

**All 4 items below are now resolved** — a follow-up debugging pass this session cloned the SDK source at
tag `v1.0.2` (commit `15db14b2e663ebe9e957e061bd67634630429035`, matching this file's header stamp) into
`C:\claude-runs\exp1-plugin-exp-sonnet5high-002\tmp\maxio-sdk-src\20260712-210114`, opened each file named
below, and folded the confirmed contract back into §2.2/§2.4/§2.5/§2.6 above. Summary:

1. **Proration/timing semantics — RESOLVED** (§2.5). (a) "Apply now with proration":
   `SubscriptionProducts.MigrateSubscriptionProduct` with `SubscriptionProductMigration.PreservePeriod = true`
   (its doc-comment: `true` → "the billing period will not change and a prorated charge will be issued for the
   new product"). (b) "Apply at next renewal without proration": `Subscriptions.UpdateSubscription` with
   `ProductId`/`ProductHandle` set to the target product **plus** `ProductChangeDelayed = true` (its doc-comment:
   "No proration applies in this case."). Correction: `NextProductId`/`NextProductPricePointId` are **not** how
   you set the delayed target — `NextProductId`'s own doc-comment marks it cancel-only ("Set to an empty string
   to cancel a delayed product change").
2. **Pause state — RESOLVED** (§2.6). `PauseSubscription` (`/hold.json`) produces `SubscriptionState.OnHold`
   (`"on_hold"`), confirmed by both the operation's remarks ("Places the subscription on hold...") and the
   `on_hold` enum doc-comment ("billing has been temporarily stopped... expected that the subscription will
   resume"). `paused` is a distinct, unrelated internal/billing-arrears state and is not reachable via
   `PauseSubscription`/`ResumeSubscription`. Resume-legal-from gate: `State == SubscriptionState.OnHold` only.
3. **CreateCustomer 422 error shape — RESOLVED** (§2.2). `CustomerErrorResponse1.Errors{ PerPage, PricePoint }`
   genuinely is the only typed 422 shape wired to `CreateCustomerError` (confirmed in `CreateCustomerError.Create`'s
   status switch) — the map was accurate, not stale. The odd field names are a real mismatch in this sample SDK's
   generated model (not something to route around by finding a "better" model — none is wired to this operation),
   so the 422 handler must not rely on `PerPage`/`PricePoint` to carry duplicate-reference/duplicate-email text;
   always keep the `TryGetRawError` fallback live and parse raw JSON defensively for the actual message.
4. **UnitBalance semantics — RESOLVED (best-supported reading, not a single explicit sentence)** (§2.4).
   Neither named file states outright "resets every cycle", but `CreateUsage`'s remarks ("accumulated to the
   `unit_balance`", "floor of `0`", supports negative-quantity deductions) plus `PreviewRenewal`'s remarks
   (`Api/SubscriptionStatus.cs`, read incidentally) describing `unit_balance` as the **current** period's metered
   usage feeding the **next** renewal's charge together support period-to-date, not lifetime. Treat
   `SubscriptionComponent.UnitBalance` as safe to read directly via `ReadSubscriptionComponent` for "usage so far
   this period"; the `ListUsages`-sum-over-period fallback from §2.4 remains valid as a belt-and-braces check but
   is no longer required as the primary mechanism.
