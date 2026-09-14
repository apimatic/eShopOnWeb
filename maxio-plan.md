# Maxio Advanced Billing — "Subscribe" feature plan (PublicApi)

## 1. Scope & sequence

1. **Client & DI registration** — register `MaxioAdvancedBillingClient` for the PublicApi host, bound from
   config keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:BaseUrl` (optional). No operations used yet.
2. **`GET /api/subscription-plans`** — resolve the product family id from `Maxio:ProductFamilyHandle` via
   `ProductFamilies.ListProductFamilies`, then list its products via `Products.ListProductsForProductFamily`.
3. **`POST /api/subscriptions`** (hero flow) — idempotent find-or-create on `Customers.ReadCustomerByReference`
   / `Customers.CreateCustomer`, then `Subscriptions.CreateSubscription` with `ProductHandle` + resolved
   `CustomerId`, no payment profile.
4. **`GET /api/my-subscriptions`** — resolve customer via `Customers.ReadCustomerByReference`, then
   `Customers.ListCustomerSubscriptions`.
5. **Error boundary** — a single translation layer wrapping every SDK call (see Trap notes + REQUIRED READING).

A capability the map lacks would be a Blocker (§5) — there is none: every operation needed above exists and is
covered in the CONTRACT SHEET below.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones.

### Namespaces used below

| Contents | Namespace |
|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment`, server option types | `MaxioAdvancedBilling.Servers` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |
| Operation controllers (`client.Customers`, `client.Subscriptions`, …) | `MaxioAdvancedBilling.Api` |
| Records (`CreateCustomerRequest`, `CustomerResponse`, `Product`, `Subscription`, …) | `MaxioAdvancedBilling.Models` |
| Enums (`SubscriptionState`, `IntervalUnit`, `CollectionMethod`, …) | `MaxioAdvancedBilling.Models.Enums` |
| Error classes (`CreateCustomerError`, `CreateSubscriptionError`, …) | `MaxioAdvancedBilling.Errors` |
| Core error types (`RawError`, `SdkException<T>`, `ApiError`) | `MaxioAdvancedBilling.Core.ErrorResponse` / `MaxioAdvancedBilling.Core.Exceptions` |

### Client construction & config mapping

| Config key | Maps to |
|---|---|
| `Maxio:ApiKey` | `options.BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }` (literal `"x"`) |
| `Maxio:Subdomain` | `options.Server.Production.Us.Site = <Subdomain>` — templates into `https://{site}.chargify.com` |
| `Maxio:BaseUrl` (optional) | when non-empty, `options.Server.Production.Us.BaseUrl = <BaseUrl>` verbatim (overrides the templated URL — `Site` becomes irrelevant when this is set) |
| *(none — no config key defined for it)* | `options.Environment` stays default `ServerEnvironment.Us` — no EU key exists in the constrained config surface, so EU hosting is not reachable by this integration |

Constructor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.

**DI extension — exact signature and lifetime** (confirmed from `ServiceCollectionExtensions.cs`, root
namespace `MaxioAdvancedBilling`; implemented as a C# extension member block on `IServiceCollection`, not an
ordinary static `this IServiceCollection` method):
```csharp
public IServiceCollection AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)
```
Called as `services.AddMaxioAdvancedBillingClient(o => { ... })`. Internally it: builds one
`MaxioAdvancedBillingClientOptions` and invokes `configure` on it once; calls `services.AddHttpClient()`
(registers the default/unnamed `IHttpClientFactory` client — no named client, no per-typed-client handler
config); then registers the `MaxioAdvancedBillingClient` itself via `services.AddSingleton(sp => new
MaxioAdvancedBillingClient(httpClientFactory.CreateClient(), options))`. **The client is registered
Singleton**, and the `HttpClient` it wraps is created exactly once (at first singleton resolution) from the
default `IHttpClientFactory.CreateClient()` and then held for the app's lifetime inside that singleton — it
is not re-created or rotated per the usual `IHttpClientFactory` handler-lifetime pattern. Whether this
default wiring's handler lifetime is acceptable as-is or needs a `PooledConnectionLifetime`/named-client
override is exactly the `dotnet-client-initialization` question — this fact (singleton + one
factory-created `HttpClient` captured for life) is what that skill's guidance should be applied to.

Only the `Production` server group is used by this feature's operations (none touch `Ebb`/events).

**Server option nesting — confirmed from source** (`ServerOptions.cs`, `Servers/ProductionOptions.cs`):
`options.Server` is `MaxioAdvancedBilling.ServerOptions` → `.Production` is
`MaxioAdvancedBilling.Servers.ProductionOptions` → `.Us` (literal property name, type nested
`ProductionOptions.UsOptions`) → `.Site: string` (default `"subdomain"`) and `.BaseUrl: string` (default
`"https://{site}.chargify.com"`). `options.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions`
(per `sdk-map.md`'s client-options table). Both nestings in the plan's config-mapping table above are
correct as written.

**Package id & version to pin** — NuGet package id is confirmed as `AsadAli.AdvancedBilling.Sdk` (map +
`PackageId` element in `MaxioAdvancedBilling.csproj`). The **exact version string is `UNVERIFIED`**: the
map/clone is pinned to git tag `v1.0.2` (commit `15db14b`), but that same commit's `.csproj` embeds
`<Version>1.0.0</Version>` — the git tag name and the packed `Version` property disagree at the very commit
the map was generated from. Neither the map nor the source can settle which string (`1.0.0`, `1.0.2`, or a
third value set only at CI publish time) is what's actually listed on nuget.org for
`AsadAli.AdvancedBilling.Sdk` — only checking the live NuGet feed can confirm that. Resolve this by running
`dotnet add package AsadAli.AdvancedBilling.Sdk` once (or checking nuget.org directly) and pinning whatever
version string that resolves to in `Directory.Packages.props`; do not assume `1.0.0` or `1.0.2` without that
check.

### Operations

| Controller property · method | Signature (params in order) | Request model + fields | Response envelope + fields read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `Task<IReadOnlyList<ProductFamilyResponse>> ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filter params must be passed explicitly (pass `null`) | none (query-filter op) | `IReadOnlyList<ProductFamilyResponse>`, each `.ProductFamily` (`ProductFamily?`): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?` | Case B `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none (signature has no page/perPage — returns full list) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable filter params must be passed explicitly (`null` to skip) | none | `IReadOnlyList<ProductResponse>`, each `.Product` (`Product !req`): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?` | Case A `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1/20) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |

**Correction (was wrong in the original sheet):** `ListProductsForProductFamily` is a member of the
**`ProductFamilies`** controller (`client.ProductFamilies.ListProductsForProductFamily(...)`), not
`Products` — confirmed against both `map/operations/ProductFamilies.md` (where this operation was always
listed) and the installed package's `Api/ProductFamilies.cs` source (`Api/Products.cs` only declares
`ArchiveProduct`, `CreateProduct`, `ListProducts`, `ReadProduct`, `ReadProductByHandle`, `UpdateProduct` —
six operations, none of them this one). The earlier CONTRACT SHEET row mistakenly wrote the accessor as
`client.Products`; every call site should use `client.ProductFamilies` instead.
| `client.Customers.ReadCustomerByReference` | `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` ← `reference` | none | `CustomerResponse.Customer` (`Customer !req`): `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | Case B `SdkException<RawError>` — 404 when no customer has that reference (check `StatusCode`) | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| `client.Customers.CreateCustomer` | `Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest.Customer` (`CreateCustomer !req`): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` ← **set this to the idempotency key** | `CustomerResponse.Customer` (`Customer !req`): `Id (id): int?`, `Reference (reference): string?` | Case A `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] — **see Trap note on the 422 shape below** | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| `client.Subscriptions.CreateSubscription` | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest.Subscription` (`CreateSubscription !req`): `ProductHandle (product_handle): string?` ← plan handle (`eshop-pro`/`basic-plan`), `CustomerId (customer_id): int?` ← resolved customer id. All other fields (payment profile / credit-card / bank-account attributes, price point, coupons) are optional (`?`, none `!req`) — **leave them unset**, matching "payment method not required" on these plans | `SubscriptionResponse.Subscription` (`Subscription?`): `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ← **next billing date**, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (companion/assessment timing field — see note below), `Product (product): Product?`, `Customer (customer): Customer?` | Case A `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors (errors): IReadOnlyList<string> !req` — plain message list) · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| `client.Customers.ListCustomerSubscriptions` | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>`, each `.Subscription` (`Subscription?`): same fields as above (`Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `Product`) | Case B `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none (signature has no page/perPage) | `operations/Customers.md`, `records-4-Su-We.md` |

**Which fields to carry, and which optional fields the Notes tie to acceptance:**
- `CreateSubscription`'s Notes (operations/Subscriptions.md) name `product_id`/`product_handle`,
  `product_price_point_handle`/`product_price_point_id`, `customer_id`/`customer_reference`, and
  `payment_profile_id` as the load-bearing optional fields for this exact call shape. This plan uses
  `ProductHandle` (handle-based, per the "resolve by handle" constraint) + `CustomerId` (from the idempotent
  find-or-create), and deliberately omits `product_price_point_handle`/`Id` (the seeded plans' default price
  point is what we want) and all payment-profile/credit-card/bank-account fields (the seeded plans do not
  require a payment method). No other optional field on `CreateSubscription` is needed for this flow.
- `CreateCustomer`'s Notes name `reference` as the sole uniqueness constraint ("you may only create one
  customer for a given reference value") — this plan sets it explicitly; no other optional field is required.

### Addendum — reconciling live `RequireCreditCard` config (diagnostic + fix operations)

Added in response to the live 422 (§5 Blockers). Two operations, both on `client.Products`
(`operations/Products.md`):

| Controller property · method | Signature (params in order) | Request model + fields | Response envelope + fields read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Products.ReadProductByHandle` | `Task<ProductResponse> ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none | `ProductResponse.Product` (`Product !req`): `Id (id): int?` ← needed for the follow-up `UpdateProduct` call, `RequireCreditCard (require_credit_card): bool?` ← the flag to inspect, `Name (name): string?`, `Description (description): string?` ← nullable on read, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `Handle (handle): string?` | Case B `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/Products.md`, `records-3-Of-Su.md` |
| `client.Products.UpdateProduct` | `Task<ProductResponse> UpdateProduct(int productId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — takes the numeric **`productId`**, not a handle; `body` must be passed explicitly | `CreateOrUpdateProductRequest.Product` (`CreateOrUpdateProduct !req`): `Name (name): string !req`, `Description (description): string !req`, `PriceInCents (price_in_cents): long !req`, `Interval (interval): int !req`, `IntervalUnit (interval_unit): IntervalUnit !req`, `RequireCreditCard (require_credit_card): bool?` ← **writable — set `false` here**, `Handle (handle): string?`, `AccountingCode`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `TrialType`, `ExpirationInterval`, `ExpirationIntervalUnit`, `AutoCreateSignupPage`, `TaxCode` (all remaining fields `?`, none `!req`) | `ProductResponse.Product` (updated product) | Case A `SdkException<UpdateProductError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Products.md`, `records-1-Ac-Cr.md` |

**Direct answer: yes, `CreateOrUpdateProduct` exposes a writable `RequireCreditCard (require_credit_card):
bool?` field** — this is not a dead end requiring the Maxio admin UI. Confirmed straight from the map's
records page (`records-1-Ac-Cr.md`), no source clone needed for this fact (both operations' signatures and
this field were already fully resolved by the map).

**Two load-bearing gotchas before you write this code (both grounded, not guesses):**

1. **`UpdateProduct` needs the numeric id, `ReadProductByHandle` does not give you one for free to skip** —
   you must call `ReadProductByHandle("eshop-pro")` first to get `.Product.Id`, then pass that `int` into
   `UpdateProduct`. There is no `UpdateProductByHandle` operation on this controller (`Products.md` lists
   exactly 6 operations: `ArchiveProduct`, `CreateProduct`, `ListProducts`, `ReadProduct`,
   `ReadProductByHandle`, `UpdateProduct` — none handle-addressed for writes).
2. **`CreateOrUpdateProduct` is a shared create/update model, and five of its fields are `!req`**
   (`Name`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`) with no optional/partial-update path —
   to flip only `RequireCreditCard`, you must re-supply all five current values (harvested from the
   `ReadProductByHandle` call) in the same `UpdateProduct` request, since the C# `required` modifier won't
   let you omit them. **`Description` — confirmed from source** (`Models/Product.cs`: `public string?
   Description { get; init; }`, wire name `description`; `Models/CreateOrUpdateProduct.cs`: `public required
   string Description { get; init; }`) — **is present on both the response and write models**, but the
   response side is nullable (`string?`) while the write side is non-nullable `required string`: if the live
   read ever returns a null `Description`, assign `product.Description ?? string.Empty` when building the
   `UpdateProduct` request (a plain `required string`, no min-length/non-empty constraint is declared on
   either model, so an empty string is a valid value per the SDK's own shape — whether Maxio's live
   validation additionally rejects an empty description is `UNVERIFIED`, but moot here since the seeded
   products already have a real description to read back).
3. **`UpdateProduct`'s own Notes are explicit that this is not a side-effect-free toggle**
   (`operations/Products.md`): *"Updating a product using this endpoint will create a new price point and
   set it as the default price point for this product."* Resubmitting `PriceInCents`/`Interval`/`IntervalUnit`
   (forced by gotcha #2) will **create a new price point** on `eshop-pro`/`basic-plan` and make it the
   product's new default, even though the price value itself is unchanged. For a feature with no live
   subscribers yet this is low-risk, but it is a real, documented side effect of using this write path — not
   a "safe read-modify-write" the way toggling one field usually implies. `UNVERIFIED`: whether Advanced
   Billing's live behavior treats an unchanged price/interval as a no-op internally despite still being
   flagged in the Notes as "creates a new price point" — the Notes state it unconditionally, so treat it as
   happening regardless of whether the values changed.

Given #2 and #3, if you'd rather not touch price points at all, reconfiguring `require_credit_card` via the
Maxio admin UI (out of this SDK's reach, as you noted) avoids both gotchas; using `UpdateProduct` is a real,
grounded SDK path but is **not free of side effects** — this is a decision about which trade-off to accept,
`YOUR CALL — not in the map`.

**Update (live-falsified): `RequireCreditCard` is already `false` on both `eshop-pro` (id `7130993`) and
`basic-plan` (id `7130994`)** — confirmed live via `ReadProductByHandle`. The `UpdateProduct` path above is
no longer the fix; the 422 has a different cause. See the new addendum below.

### Addendum 2 — diagnosing the 422 now that `RequireCreditCard` is ruled out

Re-grounded from source (`Api/Subscriptions.cs`'s full XML `<remarks>` on `CreateSubscription`,
`Models/Enums/CollectionMethod.cs`, and a newly-relevant operation, `client.Sites.ReadSite`):

- **Neither the map nor the source states that `PaymentCollectionMethod = CollectionMethod.Invoice` (or any
  non-`Automatic` value) avoids an immediate auto-charge attempt.** `CreateSubscription`'s full source doc
  comment ties "payment information may be required" only to "the options for the Product being subscribed"
  — it never mentions `payment_collection_method` at all. The `CollectionMethod` enum source file has no
  per-value doc comments beyond the one class-level summary already captured in `enums.md`. This operational
  question remains `UNVERIFIED` by both map and source — re-checking source added nothing new here.
- **But the same class-level doc comment reveals a real, source-grounded risk with reaching for `Invoice`
  specifically**: its four values are not universally valid together — they're split by site architecture:
  *legacy Statements Architecture* → `invoice`, `automatic` only; *current Relationship Invoicing
  Architecture* → `remittance`, `automatic`, `prepaid` only. **`invoice` is a legacy-only value.** If this
  sandbox site is on the current (Relationship Invoicing) architecture — the modern default for newly
  provisioned sites — setting `Invoice` would likely be rejected outright as an invalid value for this site,
  a different failure masking whatever the real fix is. Test the wrong family member and you get noise, not
  signal.
- **Grounded way to settle both "which architecture" and "what's the site's actual default" without
  guessing**: `client.Sites.ReadSite(CancellationToken ct = default)` (`operations/Sites.md`, Case B
  `SdkException<RawError>`, no params) → `SiteResponse.Site` (`records-3-Of-Su.md`):
  - `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` — tells you definitively which
    architecture applies, and therefore whether `Remittance`/`Prepaid` (if `true`) or `Invoice` (if
    `false`/legacy) is even a legal value to try as `CreateSubscription.PaymentCollectionMethod`.
  - `DefaultPaymentCollectionMethod (default_payment_collection_method): string?` — **this is exactly the
    "site-level default payment collection setting" you asked about** — the site's own configured default.
    Note its modeled type is plain `string?`, not the `CollectionMethod` enum type (no doc comment explains
    why; treat its value as one of the same wire strings — `"automatic"`, `"remittance"`, `"prepaid"`,
    `"invoice"` — by convention, not by an enforced type). If this reads `"automatic"`, that is a plausible,
    source-consistent explanation for why an unset `PaymentCollectionMethod` on `CreateSubscription` results
    in an auto-charge attempt that then 422s for lack of a card — though neither map nor source explicitly
    states that an omitted `PaymentCollectionMethod` inherits the site default; that inheritance behavior is
    `UNVERIFIED` and is the standard implication of it being an optional field, not a documented fact.

**Recommended grounded experiment order**: call `ReadSite` first, read both fields, then set
`CreateSubscription.PaymentCollectionMethod` to the correct **non-`Automatic`** member of whichever
architecture's valid set `RelationshipInvoicingEnabled` indicates (`CollectionMethod.Remittance` if `true`,
`CollectionMethod.Invoice` only if `false`) as your live test — rather than reaching for `Invoice`
unconditionally. This still does not guarantee the 422 resolves (that operational fact is genuinely
undocumented), but it avoids spending a test cycle on a value the site's own architecture may reject outright
for an unrelated reason.

### Enum value tables (only the ones this scope touches)

| Enum | Values (C# member (wire value)) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `models/enums.md` (not set by this plan — omitted from `CreateSubscription`; listed for completeness since it appears on the request/response models touched above) |

### "Next billing date" — resolved

`Subscription.CurrentPeriodEndsAt` (`current_period_ends_at`) is the field to surface as "next billing
date". This is stated directly in `UpdateSubscription`'s Notes (`operations/Subscriptions.md`): "The server
response will not return data under the key/value pair of `next_billing_at`. View the key/value pair of
`current_period_ends_at` to verify that the `next_billing_at` date has been changed successfully." —
i.e. `current_period_ends_at` is the response-side stand-in for "when this subscription bills next".
`NextAssessmentAt` (`next_assessment_at`) also exists on `Subscription` and denotes when Advanced Billing
will next *attempt an assessment* (relevant in dunning/past-due states); it is a distinct, secondary concept
— not the primary "next billing date" for the confirmation payload, but safe to expose alongside it if useful.

### Idempotent find-or-create customer — resolved flow

1. Call `ReadCustomerByReference(reference)`. If it returns 200 → use `Customer.Id`.
2. If it throws `SdkException<RawError>` with `StatusCode == HttpStatusCode.NotFound` → call `CreateCustomer`
   with `Reference = reference` (plus required `FirstName`/`LastName`/`Email`). Use the returned `Customer.Id`.
3. If `CreateCustomer` throws `SdkException<CreateCustomerError>` (the 422 case) → **do not attempt to parse
   the error payload for "duplicate reference"** (see Trap note below) — instead, re-call
   `ReadCustomerByReference(reference)` once more. `CreateCustomer`'s own Notes state the *only* validation
   restriction on this endpoint is reference-uniqueness, so a 422 immediately following a failed lookup is
   the expected shape of "someone else (e.g. a double-click) created this reference in the race window" —
   the re-lookup recovers the now-existing customer. If the re-lookup also fails, surface the original
   `CreateCustomer` error (a genuine validation failure, e.g. a malformed field).
4. Any other status from step 1 or 3 (not 404/422) is not a recognized idempotency case — surface it.

This flow does not require parsing any error body for a specific message, sidestepping the shape concern
below entirely.

---

## 3. Trap notes

⚠ Step 1 (client & DI setup) — the `HttpClient` passed into `MaxioAdvancedBillingClient` must be long-lived
and reused via `IHttpClientFactory`, not rebuilt per request; the SDK client wrapper's own DI lifetime is a
separate question from the `HttpClient`'s. **MUST load `dotnet-client-initialization`.**

⚠ Step 1 (auth wiring) — credentials must be set before/at construction (via the DI options callback or
before `new MaxioAdvancedBillingClient(...)`), and must come from configuration (`Maxio:ApiKey`), never
hardcoded. **MUST load `dotnet-authentication`.**

⚠ Steps 2–4 (every call site) — several operations here take 5–14 nullable parameters with **no C# default**
(`ListProductFamilies`, `ListProductsForProductFamily`) — a positional call silently mis-binds them.
**MUST load `dotnet-calling-endpoints`.**

⚠ Steps 2–4 (models) — `SubscriptionState`/`IntervalUnit`/`CollectionMethod` are `StringEnum<T>`, not C#
`enum` — comparing/switching on them the C# way will not compile or will not match as expected; wire names
(`current_period_ends_at`, `product_handle`, …) differ from the C# property names used above; any JSON field
not modeled on a record (there is no full guarantee every wire field present here has a modeled counterpart)
is silently dropped on deserialize rather than erroring. **MUST load `dotnet-models`.**

⚠ Step 3 (`CreateCustomer`'s 422 shape) — `CustomerErrorResponse1.Errors` is declared as type `Errors`
(`records-2-Cr-Ne.md` / `records-1-Ac-Cr.md`), and that `Errors` record's own two fields are `PerPage
(per_page)` and `PricePoint (price_point)` (`Models/Errors.cs`) — names that do not correspond to any
customer field (no `reference`, `email`, etc.). Two generated definitions disagree with what the operation's
own Notes describe as its only failure mode (a duplicate `reference`), so this typed accessor cannot be
trusted to carry a reference-duplicate message in a named field. This is why the resolved find-or-create flow
above never parses `TryGetCustomerErrorResponse1`'s fields — it treats any 422 as a signal to re-look-up by
reference instead. `UNVERIFIED`: whether the live 422 body for a duplicate reference matches this typed shape
at all; the defensive flow above does not depend on that being true.

⚠ Step 2 (`ListProductsForProductFamily`'s `productFamilyId` string param) — resolve the family id via
`ListProductFamilies` + client-side filter on `Handle` (in the CONTRACT SHEET above), not by guessing that
this string parameter accepts a `handle:eshop-subscribe`-style value. `ReadProductFamily`'s own Notes
(`operations/ProductFamilies.md`) claim a family can be addressed by `handle:my-family` format, but
`ReadProductFamily`'s own signature takes `int id` — a signature/prose mismatch on that neighbouring
operation. `UNVERIFIED`: whether `ListProductsForProductFamily`'s `string productFamilyId` also accepts that
convention; the resolve-then-call-by-id path in the sheet does not depend on it.

⚠ Step 5 (error boundary) — **many, not all**, operations here are Case B (`ListProductFamilies`,
`ListProductsForProductFamily`'s underlying list call is Case A but its sibling reads are Case B,
`ReadCustomerByReference`, `ListCustomerSubscriptions`) while `CreateCustomer` and `CreateSubscription` are
Case A — confirm each call's case from the CONTRACT SHEET before writing its catch block; `TryGetRawError` is
not a catch-all inside a Case A catch (it is one of several `TryGet…` branches). This SDK has no
`{Operation}Result` no-throw variant anywhere — every call above must be wrapped in `try/catch`.
**MUST load `dotnet-error-handling`.**

⚠ Step 5 / Steps 3–4 (resilience vs. idempotency) — `CreateCustomer` and `CreateSubscription` are both POST
(non-idempotent at the transport level). The SDK's `RetryOptions.HttpMethodsToRetry` gates only the
**status-code** retry trigger, but a bare transport failure (`HttpRequestException`) is retried on **every**
verb including POST, and no setting disables that (`MaxRetries` floor is 1). This means the SDK itself can
cause a `CreateCustomer`/`CreateSubscription` POST to be sent more than once independent of anything this
plan does — which is exactly why the find-or-create flow (§2) and a request to re-verify subscription state
after a suspicious create failure are load-bearing, not optional hardening. **MUST load
`dotnet-configuration-resilience`** before registering/tuning the client.

⚠ Step 2 (pagination) — `ListProductsForProductFamily` defaults to `page=1, perPage=20`; today's seed (2
plans) fits in one page, but the endpoint does not auto-page. `YOUR CALL` whether `GET
/api/subscription-plans` loops pages defensively for future growth — not required by today's seed data.

---

## 4. REQUIRED READING

Load all of the following **before implementation starts** — this sheet deliberately does not carry their
contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic-auth credential wiring and timing |
| `dotnet-calling-endpoints` | Steps 2–4 — named-argument calling convention for multi-nullable-param operations |
| `dotnet-models` | Steps 2–4 — `StringEnum<T>` usage, wire-name vs. C#-name mapping, unmodeled-field drop behavior |
| `dotnet-error-handling` | Step 5 — Case A vs. Case B per operation, `TryGet…` accessor mechanics |
| `dotnet-configuration-resilience` | Step 1 / Steps 3–4 — retry/timeout semantics, especially the non-idempotent-POST retry hazard tied directly to this feature's idempotency requirement |
| `dotnet-testing` | Testing the integration layer (the seam to fake is the `HttpClient` ctor argument) |

Always include, verbatim — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
`System.Text.Json.JsonException` from deserialization, **not** as an `SdkException`, so an
SDK-exception-only catch ladder lets it escape the integration boundary; and a **non-2xx** body that does not
match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is
being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed
with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an
outage, and a caller that retries 5xx retries something that can never succeed. **MUST load
`dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions:**
- Idempotency key (`reference` on the Maxio customer): the brief says "user id / email" without picking one.
  `YOUR CALL — not in the map`: this is an application-identity decision, not an SDK fact. (A stable
  identifier — e.g. the JWT's user id — is generally safer than email, which can change, but the final choice
  belongs to the implementer.)
- `CreateCustomer`'s required `FirstName`/`LastName`/`Email` must be sourced from the authenticated user's
  JWT claims (or a documented fallback when a claim is absent). `YOUR CALL — not in the map`: which claims
  eShopOnWeb's PublicApi JWTs actually carry is outside what this agent has read.
- No `Maxio:Environment`/EU config key exists in the given config surface, so `ServerEnvironment.Us` is used
  unconditionally (see CONTRACT SHEET). If the sandbox site is EU-hosted, this would need a new config key —
  flag to the user if sandbox behavior suggests otherwise; not assumed here.
- `GET /api/subscription-plans` does not loop pagination (today's 2-plan seed fits page 1/perPage 20) — see
  Trap notes.

**Blockers:**
- **Live-confirmed (2026-09-05): `CreateSubscription` on `eshop-pro` returns 422 "No payment method was on
  file for the $299.00 balance" against the real sandbox, contradicting the task's premise that these
  seeded plans don't require a payment method.** Grounded root cause: `Product.RequireCreditCard`
  (`require_credit_card`) is documented in source (`Models/Product.cs`) as "Boolean that controls whether a
  payment profile is required to be entered for customers wishing to sign up on this product" — i.e.
  whether a payment profile is mandatory at signup is a **per-product configuration flag on the Maxio side**,
  not something any field on `CreateSubscriptionRequest`/`CreateCustomerRequest` can override per-call. There
  is no request-body field in either model that waives this. `CreateSubscription`'s own Notes
  (`operations/Subscriptions.md`) independently corroborate this: "Payment information may be required to
  create a subscription, depending on the options for the Product being subscribed."
  - `CollectionMethod`/`PaymentCollectionMethod` (`Automatic`/`Remittance`/`Prepaid`/`Invoice`) is **not** a
    documented workaround: its doc comment (identical in `map/models/enums.md` and the source XML comment on
    `CreateSubscription.PaymentCollectionMethod`) only describes *how an allowed balance is collected*
    (auto-charge vs. invoice/remittance/prepaid) — neither the map nor the source states any relationship
    between this field and whether a payment profile is required to exist at all. Setting it to `Invoice`
    would be an unverified guess, not a grounded fix — `UNVERIFIED`.
  - **Diagnostic step (grounded, no guessing):** call the already-in-scope
    `client.Products.ReadProductByHandle("eshop-pro")` (Case B, `operations/Products.md`) and read
    `.Product.RequireCreditCard` directly — this settles whether the live sandbox's `eshop-pro` actually has
    `require_credit_card = false` as the task assumed, or whether the live product config drifted from the
    intended seed.
  - **Resolution is outside the SDK/request-payload's control**: if `RequireCreditCard` is `true` live, the
    fix is changing that product's configuration in Maxio (admin UI or a `Products.UpdateProduct` call — an
    application/ops decision, `YOUR CALL — not in the map`, not a code change to this integration), or
    accepting that a payment profile must be collected for this plan and revisiting the "no card required"
    requirement in the task itself. This blocks the "no payment profile" hero-flow assumption until one of
    those is resolved — it is not a defect in `MaxioSubscriptionBillingService.cs`.
