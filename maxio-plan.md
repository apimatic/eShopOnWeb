# Maxio Advanced Billing .NET SDK — eShopOnWeb subscription module

Sandbox facts (given): site `apimatic-hackathon`, US region → `ServerEnvironment.Us`.
Seeded: ProductFamily `eshop-subscribe` (id `3008866`); Products `eshop-pro` (id `7111477`,
$299/mo), `basic-plan` (id `7111478`, $29/mo); metered Component `api-call` (id `3033795`,
`ComponentKind.MeteredComponent`, Per Unit, $0.01/unit).

## 1. Scope & sequence

1. **Client construction & DI** (Infrastructure only) — `MaxioBillingClient` builds one
   long-lived `MaxioAdvancedBillingClient` from an injected `HttpClient`
   (`IHttpClientFactory` typed client) + `MaxioAdvancedBillingClientOptions`; Basic auth;
   base URL resolved from config (`Maxio:BaseUrl` explicit override, else
   `Subdomain` + `Environment` region).
2. **Startup validation** — `ProductFamilies.ReadProductFamily`, `Products.ReadProduct` ×2
   (or `ReadProductByHandle`), `Components.FindComponent("api-call")` and assert
   `Kind == ComponentKind.MeteredComponent`.
3. **Plans page** — `Products.ListProductsForProductFamily` (or `ListProducts`) to surface
   name/price/interval.
4. **Customer provisioning** (idempotent-ish on `reference` = eShopOnWeb user id) —
   `Customers.ReadCustomerByReference`, fall back to `Customers.CreateCustomer`.
5. **Subscribe** — `Subscriptions.CreateSubscription` (product by id/handle, customer by
   id/reference, no payment profile when the product doesn't require one).
6. **"Already subscribed" / "my subscriptions"** — `Customers.ListCustomerSubscriptions`.
7. **Record usage** — `SubscriptionComponents.CreateUsage` against the metered component.
8. **Period-to-date usage / balance** — `SubscriptionComponents.ReadSubscriptionComponent`
   (`UnitBalance`) and/or `SubscriptionComponents.ListUsages` for history.
9. **Plan change** — proration-now via `SubscriptionProducts.PreviewSubscriptionProductMigration`
   + `MigrateSubscriptionProduct`; no-proration-at-renewal via
   `Subscriptions.UpdateSubscription` with `product_change_delayed: true`.
10. **Lifecycle** — `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` /
    `CancelSubscription` / `InitiateDelayedCancellation` / `CancelDelayedCancellation` /
    `ReactivateSubscription`.
11. **Error surfacing** — `SdkException<TError>` throughout; Case A/B per operation below.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C#
> identifier. The cancellation-token parameter really is named `ct`: in named arguments
> write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it**
> (e.g. `MaxioAdvancedBilling.Models.Enums.SubscriptionState`,
> `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference`,
> `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`). The map
> carries these namespaces — do not drop them to the root or `.Models`, or the
> implementer guesses the wrong `using` and the build breaks.

### 2.1 Client construction, auth, server override

Source: `sdk-map.md` ("Getting a client", "Servers & auth").

- Only constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`
  — **the SDK does not own an HttpClient; you must supply one.** This is exactly what an
  `IHttpClientFactory` typed client (`services.AddHttpClient<MaxioBillingClient>(...)` or
  the generated `AddMaxioAdvancedBillingClient` extension, which itself wires the
  **default, unnamed** factory client) gives you — inject the `HttpClient` into
  `MaxioBillingClient`'s constructor and pass it straight through.
- `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`) properties:
  `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`,
  `Retry: RetryOptions`, `Server: ServerOptions`,
  `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`.
- Auth: `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }`
  — Username = the Maxio/Chargify API key, Password is the **literal string `"x"`**.
- Environment: `ServerEnvironment.Us` (default; `apimatic-hackathon` is US) or `.Eu`.
- **Base URL / subdomain override** (satisfies "explicit `Maxio:BaseUrl` wins, else derive
  from Subdomain+Environment"):
  - Subdomain: `options.Server.Production.Us.Site = "<subdomain>"` (or `.Eu.Site` for EU).
  - Explicit base URL override (e.g. mock/dev host, or a literal `Maxio:BaseUrl` config
    value): `options.Server.Production.Us.BaseUrl = "<explicit url>"` (or `.Eu.BaseUrl`).
  - Templates if neither override is set: US `https://{site}.chargify.com`, EU
    `https://{site}.ebilling.maxio.com`.
  - `MaxioBillingClient`'s construction logic: read `Environment` config to pick
    `.Us`/`.Eu`; set `.Site` from `Subdomain` config; if `Maxio:BaseUrl` is present in
    config, additionally set `.BaseUrl` on that same environment branch — do not set
    `BaseUrl` unconditionally or it silently overrides the derived template even when no
    override was intended.
  - A second, separate server group (`Ebb`, events ingestion) exists for
    `SubscriptionComponents.RecordEvent`/`BulkRecordEvents` only — out of scope here (this
    module uses metered `CreateUsage`, not events-based billing).
- DI: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; o.Environment = ...; })`
  registers the client transient and wires an `IHttpClientFactory`-backed `HttpClient`
  (default/unnamed). **`→ maxio-debug resolves from source if it surfaces`: the map gives
  only this usage snippet, not the extension method's full signature** (e.g. whether it
  accepts a named-client string, an `Action<IHttpClientBuilder>` for attaching handlers,
  or only the options callback) — confirm the overload from `ServiceCollectionExtensions.cs`
  before relying on any parameter beyond the options callback shown.
- **`→ maxio-debug resolves from source if it surfaces`**: the exact namespace of
  `ServerOptions`/`ProductionOptions`/`EbbOptions`/`Server` is not spelled out in the map
  (only `ServerEnvironment`'s namespace, `MaxioAdvancedBilling.Servers`, is confirmed by
  the sample `using`). If `options.Server.Production.Us...` fails to resolve with only
  `using MaxioAdvancedBilling;` + `using MaxioAdvancedBilling.Servers;` in scope, that's
  the signal to open `Servers/ServerOptions.cs` / `Servers/ProductionOptions.cs`.
- `RetryOptions` — all members `required`; use `RetryOptions.Default()` unless tuning.
  Retries cover only idempotent HTTP methods (`GET/HEAD/PUT/OPTIONS`) by default — `POST`
  (customer/subscription/usage creation, cancel/pause/resume/migrate) is **not** retried.

### 2.2 Products / Product Families / Components (read-only validation & Plans page)

Source: `map/operations/ProductFamilies.md`, `Products.md`, `Components.md`.

| Operation | Signature | Response | Error |
|---|---|---|---|
| `client.ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | `ProductFamilyResponse` → `ProductFamily (product_family): ProductFamily?` | `SdkException<RawError>` (Case B) |
| `client.Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | `ProductResponse` → `Product (product): Product !req` | Case B |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `ProductResponse` | Case B |
| `client.Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<ProductResponse>` | Case B |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<ProductResponse>` | `SdkException<ListProductsForProductFamilyError>` (Case A) — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| `client.Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | `ComponentResponse` → `Component (component): Component !req` | Case B |

`Product` fields you need (namespace `MaxioAdvancedBilling.Models`): `Id (id): int?`,
`Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`,
`Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`,
`RequireCreditCard (require_credit_card): bool?` / `RequestCreditCard (request_credit_card): bool?`
(product-level "requires payment method" toggle — read this to decide whether a
subscription can be created without a payment profile), `ProductFamily (product_family): ProductFamily?`.

`Component` fields: `Id (id): int?`, `Name`, `Handle`, `Kind (kind): ComponentKind?`,
`ProductFamilyId (product_family_id): int?`, `PricePerUnitInCents (price_per_unit_in_cents): long?`,
`UnitName (unit_name): string?`. Startup validation asserts
`component.Kind == ComponentKind.MeteredComponent` (`MaxioAdvancedBilling.Models.Enums.ComponentKind`,
wire value `metered_component`).

Map page: `records-1-Ac-Cr.md` (`Component`), `records-3-Of-Su.md` (`Product`,
`ProductFamily`, `ProductResponse`, `ProductFamilyResponse`).

### 2.3 Customers

Source: `map/operations/Customers.md`.

| Operation | Signature | Response | Error |
|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `CustomerResponse` → `Customer (customer): Customer !req` | Case B — a not-found lookup surfaces as `RawError.StatusCode == HttpStatusCode.NotFound` (no typed 404) |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CustomerResponse` | `SdkException<CreateCustomerError>` (Case A) — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | Case B |
| `client.Customers.ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | `CustomerResponse` | Case B |

**Idempotent-ish provisioning pattern**: call `ReadCustomerByReference(reference: eShopUserId, ct)`
first; on `SdkException<RawError>` with `StatusCode == NotFound`, call `CreateCustomer`
with `reference` set to the same value. The map's own note on `CreateCustomer` states:
*"you may only create one customer for a given reference value... the `reference` value
must be unique"* — so a second `CreateCustomer` for the same reference is expected to 422
via `CustomerErrorResponse1`, not silently succeed; treat a 422 there as a race (another
request created it first) and re-`ReadCustomerByReference` rather than surfacing an error.

`CreateCustomerRequest` (`MaxioAdvancedBilling.Models`): `Customer (customer): CreateCustomer !req`.
`CreateCustomer` required fields: `FirstName (first_name): string !req`,
`LastName (last_name): string !req`, `Email (email): string !req`. Relevant optional:
`Reference (reference): string?` (the idempotency key), `Organization`, `Phone`,
`Address`/`Address2`/`City`/`State`/`Zip`/`Country` (ISO 3166-1 alpha-2 country,
ISO 3166-2 state — 2 chars US, 2–3 outside), `Locale`.

`Customer` (response) fields you need: `Id (id): int?`, `Reference (reference): string?`,
`FirstName`, `LastName`, `Email`.

**Trust judgment / flagged discrepancy**: `CustomerErrorResponse1.Errors` is typed
`Errors?` where the `Errors` record (`records-2-Cr-Ne.md`) declares only
`PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?`
— fields that look unrelated to a customer-creation validation error (e.g. a duplicate
`reference` or missing `email`). This is a **map-visible inconsistency**, not a live-wire
guess: two generated shapes (the operation's stated purpose vs. its typed error payload)
don't line up. **Defensive-coding directive (`UNVERIFIED` — only live traffic confirms
the real 422 body shape)**: attempt `ex.Error.TryGetCustomerErrorResponse1(out var e422)`
and best-effort read `e422.Errors`, but always **also** fall back to
`ex.Error.TryGetRawError(out var raw)` → `raw.ReadAsString()` for the message you actually
surface to the user, since the typed shape may not carry the real validation text.

Map pages: `map/operations/Customers.md`, `map/models/records-1-Ac-Cr.md` (`CreateCustomer`,
`CreateCustomerRequest`), `records-2-Cr-Ne.md` (`Customer`, `CustomerResponse`,
`CustomerErrorResponse1`, `Errors`).

### 2.4 Subscriptions — create, list, preview

Source: `map/operations/Subscriptions.md`.

| Operation | Signature | Response | Error |
|---|---|---|---|
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` → `Subscription (subscription): Subscription?` | `SdkException<CreateSubscriptionError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| `client.Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | Case B |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | `SubscriptionResponse` | Case B |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | `SubscriptionResponse` | `SdkException<FindSubscriptionError>` (Case A) — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] |
| `client.Subscriptions.PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionPreviewResponse` | Case B |

**"Already subscribed" / "my subscriptions"** — prefer
`client.Customers.ListCustomerSubscriptions(customerId, ct)` (§2.3) over
`Subscriptions.ListSubscriptions`, since it's scoped by customer directly with no filter
params to mis-bind.

`CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`.
`CreateSubscription` fields relevant here: `ProductHandle (product_handle): string?` /
`ProductId (product_id): int?` (pick one), `ProductPricePointHandle`/`ProductPricePointId`
(optional — default price point used if omitted), `CustomerId (customer_id): int?` /
`CustomerReference (customer_reference): string?` (pick one — use the existing customer
found/created in §2.3, do **not** send `CustomerAttributes` to create-inline once a
customer already exists), `PaymentProfileId (payment_profile_id): int?` (omit entirely
when the product does not require a payment method), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`,
`CouponCode`/`CouponCodes`, `Reference (reference): string?` (subscription's own
reference, distinct from the customer's), `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`
(to pre-allocate the metered component at signup — optional, since metered components
default to zero usage and don't need allocation before first `CreateUsage`).

**"requires payment method = off" interaction (grounded from the operation's own doc
note on `CreateSubscription`)**: *"Payment information may be required to create a
subscription, depending on the options for the Product being subscribed."* This is
product-level (`Product.RequireCreditCard`/`RequestCreditCard`, §2.2) — no separate
subscription-level flag exists in `CreateSubscription` to force a no-payment-method
signup; the map does not carry the server-side validation rule beyond that note.
**`→ maxio-debug resolves from source if it surfaces`** if create-without-profile 422s
against a `require_credit_card: false` product — the exact validation logic isn't spelled
out in the map beyond the doc-comment paragraph already quoted.

`Subscription` (response, `MaxioAdvancedBilling.Models`) fields you need: `Id (id): int?`,
`State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`,
`Customer (customer): Customer?`, `Product (product): Product?`,
`CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`,
`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`,
`BalanceInCents (balance_in_cents): long?`, `Reference (reference): string?`,
`CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`,
`DelayedCancelAt (delayed_cancel_at): DateTimeOffset?`,
`NextProductId (next_product_id): int?` / `NextProductHandle (next_product_handle): string?`
(set when a delayed product change is pending).

`SubscriptionState` (`MaxioAdvancedBilling.Models.Enums`, `StringEnum`) — full member
list with exact wire values: `Pending (pending)`, `FailedToCreate (failed_to_create)`,
`Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`,
`SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`,
`Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`,
`TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.
Compare with `==` (value equality) or read `.Value` for the raw wire string; build from a
wire string with `SubscriptionState.FromValue(...)`, not a C# `switch` assuming a real enum.

`ErrorListResponse1`: `Errors (errors): IReadOnlyList<string> !req` — a flat validation
message list, straightforward to surface verbatim.

Map pages: `map/operations/Subscriptions.md`, `map/operations/Customers.md`,
`map/models/records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`,
`ErrorListResponse1`), `records-4-Su-We.md` (`Subscription`, `SubscriptionResponse`),
`enums.md` (`SubscriptionState`, `ComponentKind`, `CollectionMethod`).

### 2.5 Metered usage — record & read back

Source: `map/operations/SubscriptionComponents.md`.

| Operation | Signature | Response | Error |
|---|---|---|---|
| `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` | `UsageResponse` → `Usage (usage): Usage !req` | `SdkException<CreateUsageError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<UsageResponse>` | Case B |
| `client.SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | `SubscriptionComponentResponse` → `Component (component): SubscriptionComponent?` | `SdkException<ReadSubscriptionComponentError>` (Case A) — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] |

`subscriptionIdOrReference`/`componentId` are **`AnyOf` unions, not plain `int`** —
construct with the factory, not a literal:
`MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference.Int(subscriptionId)` (or
`.String(subscriptionReference)`); `MaxioAdvancedBilling.Models.AnyOf.ComponentIdModel.Int(componentId)`
(or `.String("handle:api-call")` — component-id-by-handle convention seen elsewhere in
this controller is the literal `handle:` prefix on the string variant;
**`→ maxio-debug resolves from source if it surfaces`** if a bare handle without the
prefix 404s, since the map's union row doesn't itself spell out the prefix convention for
`ComponentIdModel` specifically).

`CreateUsageRequest`: `Usage (usage): CreateUsage !req`. `CreateUsage` fields:
`Quantity (quantity): double?` — **nullable in the generated type despite being the one
field that must always be sent**; `Memo (memo): string?`; `PricePointId (price_point_id): string?`
(omit to use the component's default price point — for `api-call` that's the seeded Per
Unit $0.01/unit price point). To deduct/reverse usage, send a negative `Quantity` (per
the operation's own doc note); `unit_balance` floors at 0 server-side.

`UsageResponse` → `Usage`: `Id (id): long?`, `Memo (memo): string?`,
`CreatedAt (created_at): DateTimeOffset?`, `ComponentId (component_id): int?`,
`ComponentHandle (component_handle): string?`, `SubscriptionId (subscription_id): int?`,
`Quantity (quantity): Quantity1?` (`AnyOf`: `int`/`string` — read via
`TryGetInt(out var q)` else `TryGetString(out var qs)`), `OverageQuantity (overage_quantity): int?`.

**Period-to-date usage / component balance (item 8)** — read
`SubscriptionComponentResponse.Component.UnitBalance (unit_balance): int?` on
`SubscriptionComponent` (this **is** the metered component's period-to-date accumulated
balance the map documents: *"The `quantity` from usage for each component is accumulated
to the `unit_balance` on the Component Line Item for the subscription"* — this quote is
from `CreateUsage`'s own doc note). Also available on the same record:
`AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (`AnyOf` int/string — not
meaningful for a metered component, only quantity/on-off), `PricePointId`, `Kind (kind): ComponentKind?`.
This capability **is exposed** — no gap to report for item 8.

Map pages: `map/operations/SubscriptionComponents.md`, `map/models/records-2-Cr-Ne.md`
(`CreateUsage`, `CreateUsageRequest`), `records-4-Su-We.md` (`Usage`, `UsageResponse`,
`SubscriptionComponent`, `SubscriptionComponentResponse`), `unions.md`
(`SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`, `AllocatedQuantity2`).

### 2.6 Plan change — preview + commit, both timing options

Source: `map/operations/SubscriptionProducts.md` (proration-now path),
`map/operations/Subscriptions.md` (`UpdateSubscription`, no-proration-at-renewal path).

**Apply now with proration:**

| Operation | Signature | Response | Error |
|---|---|---|---|
| `client.SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` | `SubscriptionMigrationPreviewResponse` → `Migration (migration): SubscriptionMigrationPreview !req` | `SdkException<PreviewSubscriptionProductMigrationError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | `SdkException<MigrateSubscriptionProductError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |

`SubscriptionMigrationPreviewRequest`: `Migration (migration): SubscriptionMigrationPreviewOptions !req`.
`SubscriptionMigrationPreviewOptions` fields: `ProductId (product_id): int?` /
`ProductHandle (product_handle): string?`, `ProductPricePointId`/`ProductPricePointHandle`,
`IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge (include_initial_charge): bool? = false`,
`IncludeCoupons (include_coupons): bool? = true`, `PreservePeriod (preserve_period): bool? = false`,
`Proration (proration): Proration?` (nested record, itself just
`PreservePeriod (preserve_period): bool?` — **the map shows this as a distinct nested
record from the top-level `PreservePeriod` field on the same request; which one actually
governs proration/period-preservation isn't spelled out beyond the field list**;
`ProrationDate (proration_date): DateTimeOffset?` (preview a future date within the
current billing period, per the operation's own doc note).

`SubscriptionMigrationPreviewResponse` → `SubscriptionMigrationPreview` fields (the
proration amounts you need): `ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?`,
`ChargeInCents (charge_in_cents): long?`, `PaymentDueInCents (payment_due_in_cents): long?`,
`CreditAppliedInCents (credit_applied_in_cents): long?`. **No explicit "effective date"
field is present on `SubscriptionMigrationPreview`** — the migration is computed as of
now (or `ProrationDate` if the preview request set it); if the UI needs to display an
effective date, use the request's own `ProrationDate` (echo it back) rather than
expecting the response to carry one.

`SubscriptionProductMigrationRequest`: `Migration (migration): SubscriptionProductMigration !req`.
`SubscriptionProductMigration` fields mirror the preview options minus `ProrationDate`:
`ProductId`/`ProductHandle`, `ProductPricePointId`/`ProductPricePointHandle`,
`IncludeTrial`, `IncludeInitialCharge`, `IncludeCoupons`, `PreservePeriod`, `Proration`.
**Valid subscription states for migration**: `active` or `trialing` only (per the
operation's own doc note; `trial_ended` works but is documented as not recommended).

**At next renewal, without proration** (grounded from `UpdateSubscription`'s own doc
note: *"This method schedules the product change to happen automatically at the
subscription's next renewal date... No proration applies in this case"*):

| Operation | Signature | Response | Error |
|---|---|---|---|
| `client.Subscriptions.UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | `SdkException<UpdateSubscriptionError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |

`UpdateSubscriptionRequest`: `Subscription (subscription): UpdateSubscription !req`.
Fields to set for the delayed change: `ProductHandle (product_handle): string?` /
`ProductId (product_id): int?` (the target product), `ProductChangeDelayed (product_change_delayed): bool?`
→ set `true`, optionally `ProductPricePointId (product_price_point_id): int?` /
`ProductPricePointHandle (product_price_point_handle): string?`. To **cancel** a pending
delayed change, per the operation's own doc note, set `NextProductId (next_product_id): string?`
to an empty string (note: on `UpdateSubscription` this field is typed `string?`, not
`int?`, unlike the read-back `Subscription.NextProductId: int?` — don't conflate the two
records). Effective date/state of a pending delayed change is read back from
`Subscription.NextProductId`/`NextProductHandle` (non-null = pending) on the next
`ReadSubscription`/`ListCustomerSubscriptions` call — there is no separate "effective at"
timestamp field on `Subscription` for this; the map does not carry one.

**Both timing options ARE exposed by the SDK — no gap to report for item 9.**

Map pages: `map/operations/SubscriptionProducts.md`, `map/operations/Subscriptions.md`,
`map/models/records-4-Su-We.md` (`SubscriptionMigrationPreview(Request/Response)`,
`SubscriptionProductMigration(Request)`, `UpdateSubscription(Request)`),
`records-3-Of-Su.md` (`Proration`).

### 2.7 Lifecycle — pause, resume, cancel (immediate/delayed), reactivate

Source: `map/operations/SubscriptionStatus.md`.

| Action | Operation | Signature | Response | Error |
|---|---|---|---|---|
| Pause | `client.SubscriptionStatus.PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | `SdkException<PauseSubscriptionError>` (A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] |
| Resume | `client.SubscriptionStatus.ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` | `SubscriptionResponse` | `SdkException<ResumeSubscriptionError>` (A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] |
| Cancel (immediate or scheduled) | `client.SubscriptionStatus.CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | `SdkException<CancelSubscriptionApiError>` (A) — `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError` [fallback] |
| Cancel at end-of-period (dedicated endpoint) | `client.SubscriptionStatus.InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` | `DelayedCancellationResponse` → `Message (message): string?` | `SdkException<InitiateDelayedCancellationError>` (A) — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] |
| Remove a pending delayed cancellation | `client.SubscriptionStatus.CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | `DelayedCancellationResponse` | `SdkException<CancelDelayedCancellationError>` (A) — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] |
| Reactivate | `client.SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | `SdkException<ReactivateSubscriptionError>` (A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] |

**Immediate vs. delayed/end-of-period cancel — both are exposed, two ways:**
1. `CancelSubscription`'s own `CancellationRequest → CancellationOptions` carries
   `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?` and
   `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?` directly — per
   the operation's own doc note, *"To cancel the subscription immediately, omit any
   schedule parameters... To use the schedule options, the Schedule Subscription
   Cancellation feature must be enabled on your site."* (site-feature-gated —
   **`UNVERIFIED`**: whether that site feature is enabled on `apimatic-hackathon` can only
   be confirmed by calling it; fall back to path 2 below if a 422/feature-off response is
   seen).
2. The dedicated `InitiateDelayedCancellation` endpoint (no site-feature gate mentioned in
   its doc note) — sets `cancel_at_end_of_period` and returns only a `Message` string, not
   the subscription; re-`ReadSubscription` to confirm the new
   `Subscription.CancelAtEndOfPeriod`/`DelayedCancelAt` values. `CancelDelayedCancellation`
   reverses it (idempotent per its own doc note).

`CancellationRequest`: `Subscription (subscription): CancellationOptions !req`.
`CancellationOptions` fields: `CancellationMessage (cancellation_message): string?`,
`ReasonCode (reason_code): string?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`,
`ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`,
`RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool?`.

`CancelSubscriptionErrorResponse` (`AnyOf`): variants `ErrorListResponse1`,
`SingleErrorResponse1` — read via `TryGetErrorListResponse1(out ErrorListResponse1)` /
`TryGetSingleErrorResponse1(out SingleErrorResponse1)`. `SingleErrorResponse1`:
`Error (error): string !req`.

`PauseRequest`: `Hold (hold): AutoResume?`. `AutoResume`:
`AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` (omit for an
indefinite pause; set to schedule an automatic resume date).

`ResumeSubscription`'s `calendarBillingResumptionCharge` param is
`MaxioAdvancedBilling.Models.Enums.ResumptionCharge?` (only meaningful for calendar-
billing subscriptions — pass `null` otherwise): `Prorated (prorated)`,
`Immediate (immediate)`, `Delayed (delayed)`.

`ReactivateSubscriptionRequest` fields: `IncludeTrial (include_trial): bool?`,
`PreserveBalance (preserve_balance): bool?`, `CouponCode (coupon_code): string?`,
`UseCreditsAndPrepayments (use_credits_and_prepayments): bool?`,
`Resume (resume): Resume?` (`AnyOf`: `bool` or `ResumeOptions` — construct
`Resume.Bool(true)` for a plain resume-if-possible, or `Resume.ResumeOptions(new ResumeOptions{...})`
for the `require_resume` variant the operation's own doc note describes),
`CalendarBilling (calendar_billing): ReactivationBilling?` (calendar-billing subscriptions
only — `ReactivationBilling.ReactivationCharge (reactivation_charge): ReactivationCharge? = ReactivationCharge.Prorated`,
values `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)`).

**Exact wire values for subscription state** (repeated from §2.4 for convenience,
`MaxioAdvancedBilling.Models.Enums.SubscriptionState`): `pending`, `failed_to_create`,
`trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`,
`expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`. After
`PauseSubscription` expect `on_hold`(`OnHold`); after `ResumeSubscription` expect
`active`(`Active`)/`trialing`(`Trialing`); after `CancelSubscription`/expired delayed
cancel expect `canceled`(`Canceled`); after `ReactivateSubscription` expect
`active`/`trialing` (map's own doc note on the operation).

Map pages: `map/operations/SubscriptionStatus.md`, `map/models/records-1-Ac-Cr.md`
(`CancellationRequest`, `CancellationOptions`, `AutoResume`), `records-2-Cr-Ne.md`
(`DelayedCancellationResponse`), `records-3-Of-Su.md` (`PauseRequest`,
`ReactivateSubscriptionRequest`, `ReactivationBilling`, `SingleErrorResponse1`),
`unions.md` (`CancelSubscriptionErrorResponse`, `Resume`), `enums.md`
(`SubscriptionState`, `ResumptionCharge`, `ReactivationCharge`).

### 2.8 Error handling — exception types & message extraction (item 11)

Source: `sdk-map.md` ("Error-handling model"), `dotnet-error-handling`.

- Every operation is **throw-only** — no `{Operation}Result` no-throw variant exists
  anywhere in this SDK (confirmed by the map's own summary: 247/247 operations
  throw-only).
- `SdkException<TError>` (`MaxioAdvancedBilling.Core.Exceptions`) is the single thrown
  type; `TError` is either:
  - **Case A** — a per-operation `{Operation}Error : ApiError`
    (`MaxioAdvancedBilling.Errors`) with named `TryGet…(out …)` accessors (one per
    documented status/body — see each operation's row above) plus the inherited
    `TryGetRawError(out RawError)` **fallback-only** accessor (it does **not** fire for
    statuses that already have a more specific accessor — enumerate every named
    `TryGet…` first, `TryGetRawError` last).
  - **Case B** — `TError` **is** `RawError` directly (`MaxioAdvancedBilling.Core.ErrorResponse`):
    `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` (throws
    `JsonException` on non-JSON bodies — prefer `ReadAsString()` for surfacing to a user),
    `ReadAsBytes(): ReadOnlyMemory<byte>`.
- `ApiError` (`MaxioAdvancedBilling.Core.ErrorResponse`) exposes only `TryGetRawError`.
- Per-operation Case A/Case B and the exact accessors are already listed against each
  operation in §2.2–§2.7 above — implement each `catch` from that table, not from memory.
- **Connection failures** (`HttpRequestException`, `TaskCanceledException`) are not
  `SdkException<T>` and need a separate `catch` at the `MaxioBillingClient` boundary if
  `IBillingClient` is to surface one uniform failure type to ApplicationCore/PublicApi.
- **Defensive-coding directive for user-facing messages**: prefer the typed accessor's
  structured message list (`ErrorListResponse1.Errors`, `SingleErrorResponse1.Error`,
  etc.) when present, but always keep a `TryGetRawError`/`RawError.ReadAsString()` path as
  the final fallback for every catch block — several typed error payloads in this SDK
  (see §2.3's `CustomerErrorResponse1.Errors` finding) may not carry the actual validation
  text in the fields the map shows.

---

## 3. Trap notes (attached to the steps above)

- **§1 client construction**: construct `MaxioAdvancedBillingClient` once and reuse it —
  it (and the `HttpClient` it wraps) is meant to be long-lived; don't build a new client
  per request. If using the raw constructor instead of `AddMaxioAdvancedBillingClient`,
  still source the `HttpClient` from `IHttpClientFactory` (a typed client), not `new HttpClient()` per call.
- **§1 auth**: set `BasicAuth` before/at construction (in the object initializer or the DI
  options callback) — there is no later "set credentials" call.
- **All list/search operations** (§2.2, §2.4 `ListSubscriptions`, §2.5 `ListUsages`):
  many leading nullable params have **no C# default**, so a positional call must supply
  every one of them (even as `null`) up to the first defaulted one (`page`) — call these
  **only** with named arguments.
- **§2.4/2.5/2.6/2.7 request bodies**: every `body` parameter is nullable with no C#
  default → must be passed explicitly (never omitted) even though it's typed `?`.
- **§2.5 usage**: `subscriptionIdOrReference`/`componentId` are `AnyOf` unions — build
  with `.Int(...)`/`.String(...)` factories, never assign an `int` directly (no implicit
  conversion is confirmed for `SubscriptionIdOrReference`/`ComponentIdModel` in the map —
  use the factory to be safe).
- **§2.4 `Subscription.State` / §2.7 all lifecycle responses**: `SubscriptionState` is a
  `StringEnum`, not a C# enum — compare with `==` or `.Value`, don't `switch` on it as a
  closed set without a default arm, and use `FromValue(...)` if you ever need to round-
  trip an unrecognized future state without throwing.
- **§2.8 error handling**: `TryGetRawError` is a fallback, not a catch-all — for every
  Case A operation, branch on every named `TryGet…` accessor from that operation's table
  row first, `TryGetRawError` last, or a typed 422/404 body gets silently missed.
- **§1 retries**: `POST` (customer create, subscription create, usage record,
  pause/resume/cancel/migrate, reactivate) is not covered by the default retry policy —
  a transient 5xx on any of these surfaces immediately as `SdkException<TError>`; the
  `MaxioBillingClient` wrapper is where retry/backoff for these writes would need to be
  added if desired (not modeled by the SDK's built-in `RetryOptions`).

---

## 4. Assumptions & Blockers

- Assumed the eShopOnWeb "user id" (whatever `IBillingClient` receives as the caller's
  identity — e.g. ASP.NET Identity's user id string) is the value written to
  `CreateCustomer.Reference` / read back via `ReadCustomerByReference`. The brief says
  "idempotent-ish on a 'reference' field" but doesn't name the exact eShopOnWeb identity
  value to use — this is an ApplicationCore/Infrastructure wiring decision, not a Maxio
  contract fact, so it isn't fixed here.
  Not a Maxio contract fact — no map lookup applies.
- Assumed "requires payment method = off" in the brief refers to a Product-level
  `RequireCreditCard`/`RequestCreditCard` toggle read at subscribe time (§2.4), since no
  subscription-level "skip payment method" flag exists on `CreateSubscription` per the
  map — flagged in §2.4 as `→ maxio-debug resolves from source if it surfaces` if this
  assumption doesn't hold up against a live 422.
- No blockers: all eleven requested capabilities are exposed by the SDK per the map,
  including the two the brief said to watch for a gap on (proration preview:
  `PreviewSubscriptionProductMigration`, §2.6; delayed/end-of-period cancel:
  `InitiateDelayedCancellation`/`CancelSubscription`'s own schedule fields, §2.7) and the
  third (period-to-date usage read: `SubscriptionComponent.UnitBalance`/`ListUsages`,
  §2.5). Nothing needs to be reported back as an unsupported-feature gap.
- Two source-level facts are flagged (not blockers) for `maxio-debug` to resolve if they
  surface during implementation: the full `AddMaxioAdvancedBillingClient` overload
  (§2.1), and the exact namespace of `ServerOptions`/`ProductionOptions`/`EbbOptions`
  (§2.1).
- One live-traffic-only uncertainty is flagged `UNVERIFIED` with a defensive-coding
  fallback already written into the sheet: the real shape of `CustomerErrorResponse1`'s
  422 body (§2.3), and whether the Schedule Subscription Cancellation site feature is
  enabled on `apimatic-hackathon` (§2.7).
