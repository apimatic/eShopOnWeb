# Maxio Advanced Billing — Contract Sheet for eShopOnWeb Subscribe

Grounded entirely in the bundled SDK map (`maxio-getting-started` skill: `sdk-map.md` + `map/operations/*.md`
+ `map/models/*.md`). SDK: `AsadAli.AdvancedBilling.Sdk` `1.0.0` (release tag `v1.0.2`, commit `15db14b`),
root namespace `MaxioAdvancedBilling`, client class `MaxioAdvancedBillingClient`. This sheet does not
re-derive eShopOnWeb architecture — see the companion `plan.md` (§1–§8) for that; this file is the
Maxio-side ground truth the `MaxioBillingClient` implementation should be written from.

No SDK source clone was needed this session — every fact below resolved from the map. See
`## Session artifacts` at the end (empty).

---

## 1. Scope & sequence

Maps onto `plan.md` §6 phases:

1. **Phase 1 (provider seam + first read)** — client construction/auth/base-URL (§7 below), then
   `ProductFamilies.ReadProductFamily`, `Products.ListProductsForProductFamily` / `ReadProductByHandle`,
   `Components.FindComponent` for the UC2 startup metered-kind check.
2. **Phase 2 (UC1 Subscribe)** — `Customers.ReadCustomerByReference` → `Customers.CreateCustomer`
   (idempotent-on-reference find-or-create), `Customers.ListCustomerSubscriptions` (duplicate-enrollment
   guard), `Subscriptions.CreateSubscription`.
3. **Phase 3 (UC2 Usage)** — `Components.FindComponent` (startup + first-call kind validation),
   `SubscriptionComponents.CreateUsage`, `SubscriptionComponents.ReadSubscriptionComponent` (period-to-date
   `UnitBalance` read-back).
4. **Phase 4 (UC3 Plan change + UC4 Lifecycle)** —
   `SubscriptionProducts.PreviewSubscriptionProductMigration` / `MigrateSubscriptionProduct` (immediate,
   prorated), `Subscriptions.UpdateSubscription` with `ProductChangeDelayed=true` (delayed, no proration);
   `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` / `CancelSubscription` /
   `InitiateDelayedCancellation` / `CancelDelayedCancellation` / `ReactivateSubscription`;
   `Subscriptions.ReadSubscription` for current-state read-back.

All 7 requested capabilities **are** exposed by the SDK. No capability-level gaps — see
**Assumptions & Blockers** for the narrower caveats found while grounding.

---

## 2. Client construction, auth, base URL (capability 7)

**Source:** `sdk-map.md` "Getting a client" / "Servers & auth".

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;                    // ServerEnvironment, ServerOptions, ProductionOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;  // BasicAuthCredentials

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth   = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // literal "x"
    Environment = ServerEnvironment.Us,   // or .Eu — this is DATA-CENTER REGION, not deployment target
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

Constructor is the **only** one: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
DI alternative: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; });`.

**CRITICAL CORRECTION to `plan.md` §4.3's suggested `AddHttpClient` snippet.** Per the map's "Servers & auth"
section, the SDK resolves its outbound host from **`options.Server.Production.{Us|Eu}.BaseUrl` /
`.Site`** — *not* from the injected `HttpClient.BaseAddress`. The client builds request URIs from these
`ServerOptions` templates (`https://{site}.chargify.com` / `https://{site}.ebilling.maxio.com`), so setting
`http.BaseAddress` on the typed `HttpClient` registered via `services.AddHttpClient<IBillingClient,
MaxioBillingClient>(...)` **does not drive Maxio SDK routing** — it would be silently inert as far as the
SDK's own requests are concerned (an `HttpClient` with no matching `BaseAddress` still works because the
SDK issues absolute request URIs). `MaxioBillingClient` must instead build the resolution into the
`MaxioAdvancedBillingClientOptions` it constructs:

```csharp
// Resolution order the client MUST honor (plan.md §2.3): explicit Maxio:BaseUrl wins, else Subdomain-derived.
var prodOpts = new ProductionOptions();
if (!string.IsNullOrEmpty(settings.BaseUrl))
    prodOpts.Us.BaseUrl = settings.BaseUrl;      // explicit override — verbatim, wins
else
    prodOpts.Us.Site = settings.Subdomain;       // derived host: https://{Subdomain}.chargify.com

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth   = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
    Environment = settings.Environment == "EU" ? ServerEnvironment.Eu : ServerEnvironment.Us,
    Server      = new ServerOptions { Production = prodOpts },
};
```

`MaxioBillingClient` may still be registered as a typed client (`AddHttpClient<IBillingClient,
MaxioBillingClient>`) purely to get a pooled/long-lived `HttpClient` instance from `IHttpClientFactory` —
that part of the eShopOnWeb DI pattern is fine — but the `BaseAddress` assignment in that registration
callback should be dropped (or left harmless/unset); the actual base-URL override belongs on
`MaxioAdvancedBillingClientOptions.Server`, built inside `MaxioBillingClient`'s constructor from
`IOptions<MaxioSettings>`. Construct the `MaxioAdvancedBillingClient` once (store it as a field) and reuse
it for the instance's lifetime — do not rebuild it per call.

**Two server groups exist** — `Production` (everything in this contract sheet) and `Ebb` (event-ingest
endpoints on `SubscriptionComponents`, e.g. `RecordEvent`/`BulkRecordEvents` — out of scope for all 7
requested capabilities; metered usage here uses `CreateUsage`, which is a Production endpoint).

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**

### Capability 1 — Product Family / Products / Metered Components (read)

| Op | Signature | Request | Response (unwrap) | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse.ProductFamily: ProductFamily?` (`Id, Name, Handle, ...`) | `SdkException<RawError>` (B) | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, ct)` — 5 leading nullable params **must be passed** (`null` to skip) | — | `IReadOnlyList<ProductFamilyResponse>` | `SdkException<RawError>` (B) | none | `operations/ProductFamilies.md` |
| `client.Products.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, ct)` | — | `IReadOnlyList<ProductResponse>`, each `.Product: Product` (`Id, Name, Handle, PriceInCents, Interval, IntervalUnit, ...`) | `SdkException<ListProductsForProductFamilyError>` (A): `TryGetString(out string)`[404] → `TryGetRawError`[fallback] | manual `page`+`perPage` | `operations/Products.md` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, ct)` | — | `ProductResponse.Product: Product` | `SdkException<RawError>` (B) | none | `operations/Products.md` |
| `client.Products.ReadProduct` | `ReadProduct(int productId, ct)` | — | `ProductResponse.Product: Product` | `SdkException<RawError>` (B) | none | `operations/Products.md` |
| `client.Components.FindComponent` | `FindComponent(string handle, ct)` — site-wide handle lookup, no family scoping needed | — | `ComponentResponse.Component: Component` (`Id, Name, Handle, Kind: ComponentKind?, PricingScheme, UnitPrice, ProductFamilyId, ...`) | `SdkException<RawError>` (B) | none | `operations/Components.md` |
| `client.Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, ct)` | — | `IReadOnlyList<ComponentResponse>` | `SdkException<RawError>` (B) | manual | `operations/Components.md` |
| `client.Components.ReadComponent` | `ReadComponent(int productFamilyId, string componentId, ct)` — `componentId` accepts either the numeric id as a string or `"handle:api-call"` | — | `ComponentResponse.Component: Component` | `SdkException<RawError>` (B) | none | `operations/Components.md` |

**UC2 startup/first-call validation:** call `FindComponent("api-call")`, then check
`response.Component.Kind == ComponentKind.MeteredComponent` (wire `metered_component`) before ever calling
`CreateUsage`. `ComponentKind` values: `MeteredComponent (metered_component)`, `QuantityBasedComponent
(quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent
(prepaid_usage_component)`, `EventBasedComponent (event_based_component)` — `map/models/enums.md`.

**Sandbox config values to pass in:** family id `3008866`, product handles `eshop-pro` / `basic-plan`
(ids `7111477` / `7111478`), component handle `api-call` (id `3033795`).

**Gotcha (flagged, not a blocker):** `ReadProductFamily`'s C# signature is `int id` only — the operation's
own doc text says the family "can be specified either with the id number, or with the `handle:my-family`
format", but the generated method has no string overload, so handle-based family lookup is **not
reachable through this SDK method** (only `ReadComponent`'s `componentId` parameter is `string` and
genuinely supports the `handle:` prefix). Not a blocker here because config already carries the numeric
family id (`3008866`); flag it if a future need arises to resolve a family by handle alone (workaround:
`ListProductFamilies` + filter client-side by `.Handle`).

**UC0 create-side operations — FULL field-level detail (sandbox re-seed, 2026-07-14 revision).**
The sandbox (`apimatic-hackathon`) was verified empty (`[]` on `/product_families.json`,
`/components.json`, `/customers.json`, `/subscriptions.json`), so UC0 must actually run. Every field
below is the map's complete, verbatim field list for the model — nothing added or guessed.

**1. `client.ProductFamilies.CreateProductFamily(CreateProductFamilyRequest? body, ct)`**
→ `ProductFamilyResponse.ProductFamily: ProductFamily?`. Case A (`CreateProductFamilyError`:
`TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback]).
Body: `CreateProductFamilyRequest{ ProductFamily: CreateProductFamily !req }`.
`CreateProductFamily` (`Models/CreateProductFamily.cs`, `records-1-Ac-Cr.md`):

| C# property | Wire name | Type | Required? |
|---|---|---|---|
| `Name` | `name` | `string` | **required** |
| `Handle` | `handle` | `string?` | optional |
| `Description` | `description` | `string?` | optional |

**2. `client.Products.CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, ct)`**
→ `ProductResponse.Product: Product?`. Case A (`CreateProductError`: `TryGetErrorListResponse1`[422] →
`TryGetRawError`[fallback]). `productFamilyId` is `string` — pass the numeric family id as a string
(`"3008866"` or the newly created family's id, `.ToString()`).
Body: `CreateOrUpdateProductRequest{ Product: CreateOrUpdateProduct !req }`.
`CreateOrUpdateProduct` (`Models/CreateOrUpdateProduct.cs`, `records-1-Ac-Cr.md` line 150 — this is the
**complete** field list, nothing else exists on this model):

| C# property | Wire name | Type | Required? | How to express this seed's spec |
|---|---|---|---|---|
| `Name` | `name` | `string` | **required** | e.g. `"eShop Pro"` / `"eShop Basic"` |
| `Handle` | `handle` | `string?` | optional | e.g. `"eshop-pro"` / `"basic-plan"` |
| `Description` | `description` | `string` | **required** (not nullable — unlike `Handle`) | any non-empty text; the spec didn't give one — pick something descriptive |
| `AccountingCode` | `accounting_code` | `string?` | optional | omit (`null`) |
| `RequireCreditCard` | `require_credit_card` | `bool?` | optional | **this is the "requires payment method" field** — set `false` for requires-payment-method = off |
| `PriceInCents` | `price_in_cents` | `long` | **required** | `29900` for $299.00, `2900` for $29.00 — cents, not dollars |
| `Interval` | `interval` | `int` | **required** | `1` |
| `IntervalUnit` | `interval_unit` | `IntervalUnit` (StringEnum: `Day`, `Month`) | **required** | `IntervalUnit.Month` |
| `TrialPriceInCents` | `trial_price_in_cents` | `long?` | optional | omit (`null`) — **"no trial" = leave all four trial fields null; there is no explicit "TrialInterval=0" convention, absence is the signal** |
| `TrialInterval` | `trial_interval` | `int?` | optional | omit (`null`) |
| `TrialIntervalUnit` | `trial_interval_unit` | `IntervalUnit?` | optional | omit (`null`) |
| `TrialType` | `trial_type` | `TrialType?` (StringEnum: `NoObligation`, `PaymentExpected`) | optional | omit (`null`) |
| `ExpirationInterval` | `expiration_interval` | `int?` | optional | omit (`null`) — **"expires never" = leave both expiration fields null.** Note: `ExpirationIntervalUnit` does carry a literal `Never` member (`ExpirationIntervalUnit` StringEnum: `Day`, `Month`, `Never`), but this create model gives no paired-without-interval convention for using it standalone — omission of both fields is the simpler, unambiguous way to express "never expires" here |
| `ExpirationIntervalUnit` | `expiration_interval_unit` | `ExpirationIntervalUnit?` | optional | omit (`null`) (see above) |
| `AutoCreateSignupPage` | `auto_create_signup_page` | `bool?` | optional | omit (`null`) |
| `TaxCode` | `tax_code` | `string?` | optional | **this is the only taxability-related field on this model — there is no plain boolean `Taxable` property on `CreateOrUpdateProduct`** (contrast with `MeteredComponent` below, which does have one). Leave `null` to express "not taxable" |

**Flagged model gap (not guessed — literally absent from the map's field list, confirm on source
if it matters downstream):** `CreateOrUpdateProduct` has **no setup-fee / initial-charge field at
all** (`InitialChargeInCents` exists only on the separate `CreateProductPricePoint` model used for
price-point endpoints, not on the product-create body). "No setup fee" therefore isn't something you
set — there is nothing to set; a product created via this call cannot carry an initial charge through
this endpoint.

**3. `client.Components.CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, ct)`**
→ `ComponentResponse.Component: Component?`. Case A (`CreateMeteredComponentError`:
`TryGetNoContent(out RawError)`[404] → `TryGetErrorListResponse1(out ErrorListResponse1)`[422] →
`TryGetRawError`[fallback]). `productFamilyId` is `string` (same as above).
Body wrapper `CreateMeteredComponent` (`Models/CreateMeteredComponent.cs`, `records-1-Ac-Cr.md`):
`{ MeteredComponent: MeteredComponent !req }` — **note the request-wrapper class and the operation
share the literal name `CreateMeteredComponent`; the inner payload type is `MeteredComponent`.**
`MeteredComponent` (`Models/MeteredComponent.cs`, `records-2-Cr-Ne.md` line 157 — complete field list):

| C# property | Wire name | Type | Required? | How to express this seed's spec |
|---|---|---|---|---|
| `Name` | `name` | `string` | **required** | e.g. `"API Calls"` |
| `UnitName` | `unit_name` | `string` | **required — no value was given in the spec; pick one (e.g. `"call"` / `"calls"`) before writing the seed code** | — |
| `Description` | `description` | `string?` | optional | omit or set |
| `Handle` | `handle` | `string?` | optional | e.g. `"api-call"` (matches the sandbox config value already in this sheet) |
| `Taxable` | `taxable` | `bool?` | optional | **this model, unlike `CreateOrUpdateProduct`, does have a plain boolean taxability field** — set `false` for not taxable |
| `PricingScheme` | `pricing_scheme` | `PricingScheme` (StringEnum: `Stairstep`, `Volume`, `PerUnit`, `Tiered`) | **required** | `PricingScheme.PerUnit` |
| `Prices` | `prices` | `IReadOnlyList<Price>?` | optional | **leave `null`/omit for `PerUnit` — `Prices` is for the tiered/volume/stairstep schemes, not needed here** |
| `PricePoints` | `price_points` | `IReadOnlyList<ComponentPricePointItem>?` | optional | omit |
| `UnitPrice` | `unit_price` | `UnitPrice1?` (union, `MaxioAdvancedBilling.Models.AnyOf`: `string`\|`double`) | optional | **this is the field for a flat per-unit price** — build via `UnitPrice1.Double(0.01)` (or `.String("0.01")`); read back via `TryGetDouble`/`TryGetString`, never `new` |
| `TaxCode` | `tax_code` | `string?` | optional | omit |
| `HideDateRangeOnInvoice` | `hide_date_range_on_invoice` | `bool?` | optional | omit |
| `DisplayOnHostedPage` | `display_on_hosted_page` | `bool?` | optional | omit |
| `AllowFractionalQuantities` | `allow_fractional_quantities` | `bool?` | optional | omit |
| `PublicSignupPageIds` | `public_signup_page_ids` | `IReadOnlyList<int>?` | optional | omit |
| `Interval` | `interval` | `int?` | optional | omit (metered components bill on the subscription's own period by default) |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` | optional | omit |

**"Is `Kind` implicit or an explicit field?" — implicit.** `MeteredComponent` (the create payload) carries
**no `Kind` field at all.** The component's kind (`metered_component`) is determined entirely by calling
the `CreateMeteredComponent` operation specifically (as opposed to `CreateOnOffComponent`,
`CreateQuantityBasedComponent`, etc. — sibling operations on the same `Components` controller, each with
its own payload type and its own implicit kind). `ComponentKind` (the enum with `MeteredComponent`,
`QuantityBasedComponent`, etc.) only appears on the **read-side** `Component` model (as already noted
above for `FindComponent`/`ReadComponent`), never as a settable field on any of the create bodies.

Map pages cited: `operations/ProductFamilies.md`, `operations/Products.md`, `operations/Components.md`,
`records-1-Ac-Cr.md` (`CreateProductFamily`, `CreateProductFamilyRequest`, `CreateOrUpdateProduct`,
`CreateOrUpdateProductRequest`, `CreateMeteredComponent`), `records-2-Cr-Ne.md` (`MeteredComponent`),
`models/enums.md` (`PricingScheme`, `IntervalUnit`, `ExpirationIntervalUnit`, `TrialType`),
`models/unions.md` (`UnitPrice1`).

### Capability 2 — Customer, idempotent on stable reference (email/username)

| Op | Signature | Request | Response | Error | Map page |
|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, ct)` — query `reference` ← `reference` | — | `CustomerResponse.Customer: Customer` (`Id, FirstName, LastName, Email, Reference, ...`) | `SdkException<RawError>` (B) — a miss is a normal 404, read `ex.Error.StatusCode` | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, ct)` | `CreateCustomerRequest.Customer: CreateCustomer{ FirstName !req, LastName !req, Email !req, Reference?, Organization?, Address?, City?, State?, Zip?, Country?, Phone?, Locale?, ... }` | `CustomerResponse.Customer: Customer` | `SdkException<CreateCustomerError>` (A): `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)`[422] → `TryGetRawError`[fallback] | `operations/Customers.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, ct)` | — | `IReadOnlyList<SubscriptionResponse>` | `SdkException<RawError>` (B) | `operations/Customers.md` |

**Idempotent find-or-create pattern (no atomic upsert endpoint exists):**
1. `ReadCustomerByReference(reference: userEmailOrUsername)`. On success → use `Customer.Id`.
2. On `SdkException<RawError>` with `StatusCode == NotFound` → `CreateCustomer` with
   `Customer.Reference = reference` (the doc text on `CreateCustomer` states reference values must be
   unique — a race where two requests create the same reference concurrently will surface as a `422` on
   the loser).
3. **Defensive directive:** in the `422` branch (`ex.Error.TryGetCustomerErrorResponse1(out var e)`), fall
   back to `ReadCustomerByReference` again rather than surfacing a raw error — this makes the whole
   find-or-create idempotent under the double-click race described in `plan.md` UC1's failure scenarios.

Use `User.Identity.Name` (email/username, per `plan.md` §4.4) as `reference`.

### Capability 3 — Create + find subscription (enroll, detect existing)

| Op | Signature | Request | Response | Error | Map page |
|---|---|---|---|---|---|
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, ct)` | `CreateSubscriptionRequest.Subscription: CreateSubscription{ ProductHandle?, ProductId?, ProductPricePointHandle?, ProductPricePointId?, CustomerId?, CustomerReference?, CustomerAttributes?: CustomerAttributes, PaymentProfileId?, Reference?, CouponCode?, ... }` (many more optional fields — see `records-2-Cr-Ne.md` for the full list) | `SubscriptionResponse.Subscription: Subscription?` (`Id, State: SubscriptionState?, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt, Product: Product?, Customer: Customer?, ...`) | `SdkException<CreateSubscriptionError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/Subscriptions.md` |
| `client.Customers.ListCustomerSubscriptions` | (see Capability 2) | — | `IReadOnlyList<SubscriptionResponse>` — filter client-side for a subscription whose `State` is `Active`/`Trialing` before enrolling | `SdkException<RawError>` (B) | `operations/Customers.md` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, ct)` — `include` must be passed explicitly (`null` to skip) | — | `SubscriptionResponse.Subscription: Subscription?` | `SdkException<RawError>` (B) | `operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, ct)` — this is the **subscription's own** `Reference` field (set at creation), *not* the customer reference | — | `SubscriptionResponse` | `SdkException<FindSubscriptionError>` (A): `TryGetNoContent(out RawError)`[404] → `TryGetRawError` | `operations/Subscriptions.md` |

**Enroll:** set `CreateSubscription.ProductHandle = "eshop-pro"` (or `"basic-plan"`) and
`CreateSubscription.CustomerId = customer.Id` (from Capability 2) — do not use `CustomerAttributes`
inline once the customer already exists, to avoid creating a second customer record.

**No-card-on-file enrollment (2026-07-14 revision — grounded in full `CreateSubscription.cs` field list,
`records-2-Cr-Ne.md` line 17).** `RequireCreditCard=false` on the *product* only controls whether the
Billing Portal/signup UI demands a card for that product — it does not change what `CreateSubscription`
itself does when no `PaymentProfileId`/`PaymentProfileAttributes`/`CreditCardAttributes` is supplied. The
422 `"No payment method was on file for the $299.00 balance"` is the API's own default assessment
behavior: with no `PaymentCollectionMethod` set, the subscription defaults to trying to **automatically**
charge a card at creation, and there is none. The field that controls this is on `CreateSubscription`
itself:

- **`PaymentCollectionMethod`** (wire `payment_collection_method`): `CollectionMethod?` — a `StringEnum`
  with four members (`Models/Enums/CollectionMethod.cs`, `map/models/enums.md`): `Automatic (automatic)`,
  `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. Its own doc text: *"The type of
  payment collection to be used in the subscription. For legacy Statements Architecture valid options are
  `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are `remittance`,
  `automatic`, `prepaid`."* Setting anything other than `Automatic` (the effective default when the field
  is left `null`) is what tells the subscription not to attempt an automatic card charge at creation —
  i.e. it makes a card on file unnecessary — while the recurring price ($299/mo) still accrues normally as
  a balance to be settled by invoice/remittance instead of auto-charge.
- **No other field on `CreateSubscription` exists for "no card required."** The full field list (54
  fields, `records-2-Cr-Ne.md` line 17) has nothing else that skips or defers the initial charge on its
  own — no `SkipInitialCharge`/`RequirePaymentMethod` flag anywhere on this model. `DeferSignup: bool?`
  (default `false`) only delays the *signup date* itself, not the payment-method requirement, and is not a
  substitute here.
- **Which value to pass — unverified without a live call, so treat as a defensive directive, not a
  guess:** the map cannot tell you whether this sandbox site runs the legacy Statements Architecture
  (where `Invoice` is valid) or the current Relationship Invoicing Architecture (where `Remittance` is the
  analogous non-card option and `Invoice` would itself be rejected as invalid for the site). **Directive:**
  try `CollectionMethod.Remittance` first (correct for any current/new Maxio site, which an
  `apimatic-hackathon`-style sandbox almost certainly is), and fall back to `CollectionMethod.Invoice` only
  if `Remittance` itself 422s as invalid for this site's architecture — log whichever one the sandbox
  actually accepts so this sheet can be tightened to a single verified value afterward. Do **not** hardcode
  a guess without a fallback path, since this is exactly the kind of live-wire fact this sheet cannot
  settle from the map or source alone.

This is not a capability gap — `CreateSubscription` fully exposes the field UC1 needs
(`PaymentCollectionMethod`); the SDK is not missing anything here. `MaxioBillingClient.CreateSubscriptionAsync`
should be updated to set `Subscription.PaymentCollectionMethod = CollectionMethod.Remittance` (with the
`Invoice` fallback above) alongside the existing `ProductHandle`/`CustomerId`.

**Duplicate-enrollment guard (UC1):** call `ListCustomerSubscriptions(customerId)` first; if any entry's
`Subscription.State` is `SubscriptionState.Active` or `.Trialing`, return that subscription instead of
calling `CreateSubscription` again. This is app-side idempotency — the SDK has no built-in
idempotency key for subscription creation, so a true double-click race is only mitigated, not eliminated
(matches `plan.md` UC1 failure-scenario language, which accepts best-effort detection).

### Capability 4 — Record usage + read back period-to-date total

| Op | Signature | Request | Response | Error | Map page |
|---|---|---|---|---|---|
| `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, ct)` | `CreateUsageRequest.Usage: CreateUsage{ Quantity: double?, Memo?: string, PricePointId?: string, ... }` | `UsageResponse.Usage: Usage{ Id, Quantity: Quantity1 (union int\|string), ComponentId, SubscriptionId, ... }` | `SdkException<CreateUsageError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionComponents.md` |
| `client.SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, ct)` | — | `SubscriptionComponentResponse.Component: SubscriptionComponent?` — **`UnitBalance: int?` is the period-to-date running total** | `SdkException<ReadSubscriptionComponentError>` (A): `TryGetNoContent(out RawError)`[404] → `TryGetRawError`[fallback] | `operations/SubscriptionComponents.md` |
| `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, ct)` | — | `IReadOnlyList<UsageResponse>` — individual usage records, not a running total | `SdkException<RawError>` (B) | `operations/SubscriptionComponents.md` |

**Union construction:**
`SubscriptionIdOrReference.Int(subscriptionId)` (or `.String(reference)`); `ComponentIdModel.Int(3033795)`
(or `.String("handle:api-call")`). Both are `AnyOf` unions (`int, string`), namespace
`MaxioAdvancedBilling.Models.AnyOf` — built via the static factory, never `new`. Source: `map/models/unions.md`.

**Read-back of running total (UC2 step 3):** prefer `ReadSubscriptionComponent(subscriptionId,
componentId).Component.UnitBalance` — this is the accumulated unit balance for the period, exactly what
"period-to-date total" means per Maxio's docs (quantity from each `CreateUsage` call accumulates into
`unit_balance` on the subscription's component line item). `ListUsages` gives the raw history if an audit
trail is needed instead, but summing it yourself duplicates what `UnitBalance` already gives you.

**Deducting/reversing usage:** a negative `Quantity` on `CreateUsage` decrements the balance (floored at 0)
— useful if UC2's failure-scenario "don't blindly resend after an ambiguous response" needs a correction
path; read `UnitBalance` first to confirm the actual state before deciding to adjust.

### Capability 5 — Plan change: preview + commit, immediate (prorated) vs delayed (next renewal)

| Op | Signature | Request | Response | Error | Map page |
|---|---|---|---|---|---|
| `client.SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, ct)` | `SubscriptionMigrationPreviewRequest.Migration: SubscriptionMigrationPreviewOptions{ ProductId?, ProductHandle?, ProductPricePointId?, ProductPricePointHandle?, IncludeTrial? = false, IncludeInitialCharge? = false, IncludeCoupons? = true, PreservePeriod? = false, Proration?: Proration{ PreservePeriod: bool? }, ProrationDate?: DateTimeOffset }` | `SubscriptionMigrationPreviewResponse.Migration: SubscriptionMigrationPreview{ ProratedAdjustmentInCents, ChargeInCents, PaymentDueInCents, CreditAppliedInCents }` | `SdkException<PreviewSubscriptionProductMigrationError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionProducts.md` |
| `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, ct)` — **this is the immediate, prorated commit** | `SubscriptionProductMigrationRequest.Migration: SubscriptionProductMigration{ ProductId?, ProductHandle?, ProductPricePointId?, ProductPricePointHandle?, IncludeTrial?, IncludeInitialCharge?, IncludeCoupons?, PreservePeriod?, Proration?: Proration }` | `SubscriptionResponse.Subscription: Subscription?` | `SdkException<MigrateSubscriptionProductError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionProducts.md` |
| `client.Subscriptions.UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, ct)` — **delayed product change: no proration** | `UpdateSubscriptionRequest.Subscription: UpdateSubscription{ ProductHandle?, ProductId?, ProductChangeDelayed?: bool, ProductPricePointId?, ProductPricePointHandle?, ... }` — set `ProductChangeDelayed = true` to schedule the change for the subscription's next renewal | `SubscriptionResponse.Subscription: Subscription?` | `SdkException<UpdateSubscriptionError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/Subscriptions.md` |

**Two distinct mechanisms — do not conflate them:**
- **"Apply now with proration"** (`plan.md` UC3 step 1, "now" branch) → preview via
  `PreviewSubscriptionProductMigration`, commit via `MigrateSubscriptionProduct`. Both take the *same*
  shape of migration options (`ProductId`/`ProductHandle` + optional `Proration.PreservePeriod`), so the
  preview call and the commit call should be built from identical parameters to keep "preview matches
  commit" true (`plan.md` UC3 failure scenario: "never silently apply a different amount than the one
  shown").
- **"At next renewal without proration"** (`plan.md` UC3 step 1, "at renewal" branch) →
  `UpdateSubscription` with `ProductChangeDelayed = true`. Per the operation's own doc text, **no proration
  applies** to this path, and setting `NextProductId = ""` (empty string) cancels a pending delayed change.

**Gap — no server-side preview endpoint for the delayed path.** `PreviewSubscriptionProductMigration` only
previews the *immediate, prorated* migration; there is no analogous "preview a delayed/renewal-time product
change" operation in the SDK. For `plan.md` UC3 step 2's "the new plan price effective from the next
period" preview on the delayed branch, compose it client-side from the target product's already-known price
(`Products.ReadProductByHandle(targetHandle).Product.PriceInCents`) rather than expecting a provider call —
there is nothing to call. This is not a capability gap (the delayed change itself works), just an absent
preview endpoint for that one branch; flagged so the implementer doesn't search for a nonexistent
"delayed migration preview" method.

**Staleness-of-preview guard (UC3 failure scenario: "reject the commit and require a fresh preview"):**
neither `PreviewSubscriptionProductMigration` nor `MigrateSubscriptionProduct` returns or accepts a
preview token/ETag — implement the staleness check application-side (e.g., re-run the preview immediately
before commit and compare `ProratedAdjustmentInCents`/`ChargeInCents` to what was shown to the customer;
reject the commit if they differ).

### Capability 6 — Lifecycle: pause / resume / cancel (immediate + delayed) / reactivate / read state

| Op | Signature | Request | Response | Error | Map page |
|---|---|---|---|---|---|
| `client.SubscriptionStatus.PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, ct)` — `POST .../hold.json`; **may not pause if `next_billing_at` is within 24h** | `PauseRequest{ Hold?: AutoResume{ AutomaticallyResumeAt?: DateTimeOffset } }` (pass `null` body for a plain, indefinite pause) | `SubscriptionResponse.Subscription: Subscription?` | `SdkException<PauseSubscriptionError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, ct)` — `POST .../resume.json`; query `calendar_billing['resumption_charge']` ← param (calendar-billing subscriptions only — pass `null` otherwise) | — | `SubscriptionResponse.Subscription: Subscription?` | `SdkException<ResumeSubscriptionError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, ct)` — **`DELETE`**; omit schedule params in `body` for immediate cancel | `CancellationRequest.Subscription: CancellationOptions{ CancellationMessage?, ReasonCode?, CancelAtEndOfPeriod?: bool, ScheduledCancellationAt?: DateTimeOffset, RefundPrepaymentAccountBalance?: bool }` | `SubscriptionResponse.Subscription: Subscription?` | `SdkException<CancelSubscriptionApiError>` (A): `TryGetNoContent(out RawError)`[404] → `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, ct)` — **end-of-period cancel**, `POST .../delayed_cancel.json`; cannot be used at creation or while past-due | `CancellationRequest` (same shape as above) | `DelayedCancellationResponse{ Message?: string }` | `SdkException<InitiateDelayedCancellationError>` (A): `TryGetNoContent(out RawError)`[404] → `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, ct)` — **`DELETE .../delayed_cancel.json`**; idempotent, resets `cancel_at_end_of_period` to `false` | — | `DelayedCancellationResponse{ Message?: string }` | `SdkException<CancelDelayedCancellationError>` (A): `TryGetNoContent(out RawError)`[404] → `TryGetRawError`[fallback] | `operations/SubscriptionStatus.md` |
| `client.SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, ct)` — **`PUT .../reactivate.json`** | `ReactivateSubscriptionRequest{ CalendarBilling?: ReactivationBilling, IncludeTrial?: bool, PreserveBalance?: bool, CouponCode?: string, UseCreditsAndPrepayments?: bool, Resume?: Resume (union bool\|ResumeOptions) }` (pass `null` body for the plain case) | `SubscriptionResponse.Subscription: Subscription?` | `SdkException<ReactivateSubscriptionError>` (A): `TryGetErrorListResponse1(out ErrorListResponse1)`[422] → `TryGetRawError`[fallback] | `operations/SubscriptionStatus.md` |
| `client.Subscriptions.ReadSubscription` | (see Capability 3) — **current-state read-back** | — | `SubscriptionResponse.Subscription.State: SubscriptionState?` | `SdkException<RawError>` (B) | `operations/Subscriptions.md` |

**Union construction for reactivate's `Resume` field:** `Resume.Bool(true)` or
`Resume.ResumeOptions(new ResumeOptions{...})`; read back via `TryGetBool`/`TryGetResumeOptions`. Namespace
`MaxioAdvancedBilling.Models.AnyOf`. Source: `map/models/unions.md`.

**`SubscriptionState` values** (StringEnum, `map/models/enums.md`): `Pending`, `FailedToCreate`,
`Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`,
`Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup`.

**Flagged ambiguity (defensive-coding directive, not resolvable from the map or source alone):** the pause
endpoint is literally `hold.json` (`PauseSubscription`), yet `SubscriptionState` carries **two** distinct
constants that could plausibly represent "paused" — `OnHold (on_hold)` and `Paused (paused)` — and the map
does not state which one this endpoint's resulting subscription actually reports (nor does the SDK source
carry that runtime fact; it is only observable from a live call). **Directive:** when checking whether a
subscription is in the paused state after `PauseSubscription` (or before allowing `ResumeSubscription`),
compare against **both** `SubscriptionState.OnHold` and `SubscriptionState.Paused` rather than assuming a
single constant, and log the actual `.Value` seen the first time this runs against the sandbox so the
assumption can be tightened later. This is exactly the kind of live-wire fact this sheet labels unverified
rather than guessing.

---

## 3. Error-handling model (applies to every call above)

Every operation **throws**; there is **no non-throwing `…Result` variant anywhere in this SDK** (247/247
operations are throw-only per `sdk-map.md`). Two cases, both already named per-operation in the tables
above:

- **Case A (typed):** `catch (SdkException<{Operation}Error> ex)` — enumerate every `TryGet…` accessor
  listed in this sheet's Error column for that op, in order, with `TryGetRawError` **last** (it is not a
  catch-all — it only fires for statuses with no more specific accessor).
- **Case B (raw):** `catch (SdkException<RawError> ex)` — read `ex.Error.StatusCode` /
  `ex.Error.ReadAsString()` directly; `RawError` has no `TryGet…` accessors at all.

Namespaces needed for a full catch block: `MaxioAdvancedBilling.Core.Exceptions` (`SdkException<T>`),
`MaxioAdvancedBilling.Core.ErrorResponse` (`ApiError`, `RawError`), `MaxioAdvancedBilling.Errors` (the
per-operation `{Operation}Error` types, Case A only).

**Connection failures are separate from API errors.** `HttpRequestException` / `TaskCanceledException` are
not caught by `SdkException<...>` catches — `MaxioBillingClient` should catch these at its boundary too and
translate them into whatever `BillingProviderException` (per `plan.md`'s ApplicationCore exception type)
the rest of the app expects, alongside the `SdkException<...>` translation, so `ISubscriptionService` only
ever sees one failure shape.

**Retry semantics (relevant to the "re-read state before retry" language in UC2–UC4 failure scenarios):**
retries (via `options.Retry`, Polly-backed) cover only idempotent HTTP verbs — `GET`/`HEAD`/`PUT`/`OPTIONS`
— by default. Of the write operations in this sheet: `UpdateSubscription` (PUT) and `ReactivateSubscription`
(PUT) are retried by default; `CreateSubscription`, `CreateUsage`, `PauseSubscription`, `ResumeSubscription`,
`MigrateSubscriptionProduct`, `InitiateDelayedCancellation` (all POST) and `CancelSubscription`,
`CancelDelayedCancellation` (DELETE) are **not** retried automatically — a transient failure on any of
these surfaces immediately as an exception, matching the plan's instruction to re-read subscription/usage
state before ever retrying them manually.

---

## 4. Trap notes (companion-skill gotchas, tied to specific steps)

- **Named arguments, `ct:` literally.** Every signature above that has ≥1 nullable-no-default leading
  parameter (`ListProductsForProductFamily`, `ListComponentsForProductFamily`, `ListCustomerSubscriptions`
  callers building `ListSubscriptions`-style calls, etc.) must be called with named arguments — a
  positional call silently mis-binds. The cancellation parameter is named `ct`, not `cancellationToken`, in
  every one of these methods.
- **`MaxioAdvancedBillingClient` and its `HttpClient` are both long-lived.** Construct the SDK client once
  inside `MaxioBillingClient` (e.g., in the constructor, from the injected `HttpClient` +
  `IOptions<MaxioSettings>`) and reuse it for every call — do not new one up per request. The `HttpClient`
  itself should come from `IHttpClientFactory` (typed client registration is fine for that purpose alone —
  see the base-URL correction in §2 above for what NOT to rely on that registration for).
- **Enums are `StringEnum<T>`, not C# enums.** `ComponentKind.MeteredComponent`,
  `SubscriptionState.Active`, `SubscriptionStateFilter.Active`, etc. — use the static members or
  `Type.FromValue("wire_value")`; compare with `==` (they're records) or read `.Value` for the raw wire
  string back out.
- **Unions are built via static factories, never `new`, and read via `TryGet…`.** All four unions touched
  in this scope — `SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1` (on `Usage`/`CreateUsage`
  reads), `Resume` (on reactivate) — follow this pattern; see each capability's row above for the exact
  factory/reader names.
- **Envelope pattern on every read/write.** Every response type in this sheet nests the real payload one
  level down (`XResponse.X`), except `DelayedCancellationResponse` (flat) and list operations that return
  `IReadOnlyList<XResponse>` directly (still one level of `.X` unwrap per item). Don't skip the unwrap.
- **Namespaces don't import transitively.** Add a separate `using` per kind of type touched:
  `MaxioAdvancedBilling` (client/options), `MaxioAdvancedBilling.Api` (implicit via `client.X`),
  `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Models.AnyOf`,
  `MaxioAdvancedBilling.Errors`, `MaxioAdvancedBilling.Core.Exceptions`,
  `MaxioAdvancedBilling.Core.ErrorResponse`, and `MaxioAdvancedBilling.Servers` (for `ServerOptions`/
  `ProductionOptions`/`ServerEnvironment` used in the base-URL resolution in §2). If a name from this sheet
  fails to compile, trust the compiler over this sheet and re-open the exact `.cs` file the map row names.
- **Auth is Basic, password is the literal `"x"`.** `BasicAuthCredentials{ Username = apiKey, Password =
  "x" }` — set on `options.BasicAuth` before constructing the client (or inside the `AddMaxioAdvancedBillingClient`
  DI callback). Load `apiKey` from `IOptions<MaxioSettings>`, never hardcode it (`plan.md` §2.3/§5 already
  requires user-secrets for this).
- **`Maxio:Environment` (US/EU) is a different axis from `Maxio:BaseUrl` (prod/dev/mock).** Map straight to
  `options.Environment = ServerEnvironment.Us|Eu`; keep it orthogonal to the `Server.Production.Us/Eu.BaseUrl`
  override described in §2 — don't conflate the two in `MaxioSettings.ResolveBaseUrl()`.

---

## 5. Assumptions & Blockers

**Blockers:** none. All 7 requested capabilities are exposed by the SDK.

**Assumptions / flagged caveats (all grounded above, not guessed):**

1. **`ReadProductFamily` cannot resolve by handle** (signature is `int id` only, despite its own doc text
   claiming handle support) — not a blocker since config carries the numeric family id `3008866`, but
   flagged in Capability 1 as a map/source inconsistency for anyone tempted to pass a handle string there.
2. **No delayed/renewal-time migration preview endpoint exists** — `PreviewSubscriptionProductMigration`
   only covers the immediate/prorated path. The "at renewal" preview in UC3 must be composed client-side
   from the target product's known price rather than calling a (nonexistent) provider preview for that
   branch. See Capability 5.
3. **No preview-staleness token from the provider** — the "reject a stale preview at commit" requirement
   (UC3) has no server-side support (no ETag/version on the preview response); implement the staleness
   check by re-previewing immediately before commit and comparing amounts client-side.
4. **`PauseSubscription`'s resulting `SubscriptionState` (`OnHold` vs `Paused`) is not determinable from the
   map or SDK source** — both constants exist and either could plausibly be what a held subscription
   reports; this is unverifiable without a live call. Directive: treat both as "paused" when checking state,
   and log the actual value observed against the sandbox to tighten this later (labeled explicitly as
   unverified, per this sheet's grounding rules — not asserted from memory).
5a. **UC0 must actually be performed, not assumed satisfied.** Live verification on 2026-07-14 found the
   `apimatic-hackathon` sandbox empty (`[]` from `/product_families.json`, `/components.json`,
   `/customers.json`, `/subscriptions.json`), contradicting this sheet's earlier "already seeded, family id
   `3008866`" assumption in Capability 1. That family id / product ids / component id are **placeholders
   only** until a real seed run records the actual ids the sandbox returns; update this sheet with the real
   ids once UC0's `CreateProductFamily` / `CreateProduct` (x2) / `CreateMeteredComponent` calls succeed.
5b. **Two model-shape gaps found while grounding UC0, not resolvable from the map beyond what's stated:**
   `CreateOrUpdateProduct` (product create) has no boolean `Taxable` field (only `TaxCode: string?`) and no
   setup-fee/initial-charge field at all — both differ from what an implementer might assume by analogy
   with `MeteredComponent`, which does have a plain `Taxable: bool?`. See the expanded UC0 tables above for
   the exact fields available on each model; nothing here is a capability gap, just a narrower field surface
   than assumed.
5c. **`MeteredComponent.UnitName` is required and the user's spec didn't supply a value** — pick one before
   writing the seed code (e.g. `"call"`); flagged so the seed script doesn't fail 422 on a missing required
   field.
5d. **`CreateSubscription.PaymentCollectionMethod` (`CollectionMethod?`) must be set explicitly for a
   no-card-on-file enrollment** — left `null` it defaults to attempting an automatic card charge at
   creation, which is the 422 (`"No payment method was on file..."`) observed live against the reseeded
   sandbox on 2026-07-14. Directive: set `CollectionMethod.Remittance` (current-architecture non-card
   option), fall back to `CollectionMethod.Invoice` if the site rejects `Remittance` (legacy-architecture
   sites only accept `invoice`/`automatic`) — see Capability 3 for the full grounding. Not a capability
   gap; the field exists and is fully documented on `CreateSubscription`.
5. **Customer/subscription idempotency is app-implemented, not provider-atomic.** Both the "find-or-create
   customer on reference" and "detect existing active subscription before enrolling" patterns are built
   from separate read-then-write calls (no atomic upsert or idempotency-key parameter exists on
   `CreateCustomer`/`CreateSubscription`); a genuine double-click race is mitigated (via the 422-then-reread
   pattern for customers, and the pre-check-list pattern for subscriptions) but not fully eliminated at the
   provider level. This matches `plan.md`'s own UC1 failure-scenario language, which already accepts
   best-effort duplicate detection.

## Session artifacts

- SDK clone: /tmp/maxio-sdk-src/20260714-205216 (ref v1.0.2, cloned 2026-07-14 20:52:16)
