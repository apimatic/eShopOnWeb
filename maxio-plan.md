# Maxio Advanced Billing — integration plan for eShopOnWeb (`src/PublicApi`)

Scope: additive recurring-subscription billing. Three JWT-protected endpoints
(`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`).
Maxio is the system of record; the existing Catalog→Basket→Order flow is untouched.

Everything below is grounded in the bundled SDK map (pages named in each **source** cell) and,
where the map does not carry the fact, in the named generated source file of SDK `v1.0.2`
(commit `15db14b`) — the version this sheet describes.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Bind `Maxio:*` settings; register the SDK client (subdomain-derived **or** explicit `Maxio:BaseUrl`) | — (client construction, §2.6) |
| 2 | Resolve the configured product family **by handle** → numeric family id | `ProductFamilies.ListProductFamilies` (+ client-side match on `ProductFamily.Handle`) |
| 3 | `GET /api/subscription-plans` — list products of that family, page until short page, drop archived | `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — resolve/create the Maxio customer for the caller **by `reference`** | `Customers.ReadCustomerByReference` → on 404 `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` — idempotency check: does the customer already hold a live subscription to that product handle? | `Customers.ListCustomerSubscriptions` (+ client-side match on `Subscription.Product.Handle` and `Subscription.State`) |
| 5b | Determine the site's billing architecture once, to pick the collection method (§2.2a) | `Sites.ReadSite` → `Site.RelationshipInvoicingEnabled` |
| 6 | `POST /api/subscriptions` — enroll (attach the **existing** customer by id, product by handle, **and set `PaymentCollectionMethod`** — §2.2a) | `Subscriptions.CreateSubscription` |
| 7 | `GET /api/my-subscriptions` — read back plan / price / state / next-billing-date | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 8 | Error boundary translating `SdkException<…>` (and `JsonException`, see §4) to HTTP responses | — |

Step 2 exists because the family-read operation cannot take a handle: `ReadProductFamily(int id, …)`
declares an **`int`** path parameter, so the `handle:eshop-subscribe` form that operation's own Notes
describe **cannot be passed through this SDK**. Resolve the family by listing families and matching
`ProductFamily.Handle` client-side, then pass the numeric id (as a string) to
`ListProductsForProductFamily`. No numeric id is ever hard-coded — it is resolved from the handle.

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
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

**All operation methods are async-only and carry NO `Async` suffix** — they return `Task`/`Task<T>`
and there are **no synchronous overloads anywhere in `Api/`** (verified across the whole `Api/`
directory of source `v1.0.2`). Write `await client.Customers.CreateCustomer(body, ct: token);`.

### 2.1 Operations

| Controller property | Method signature (verbatim, params in order) | Request model + fields | Response envelope → payload | Error case + accessors | Pagination | source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) | `Task<IReadOnlyList<ProductFamilyResponse>> ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable params have **no default → must be passed explicitly** (`null` to skip) | none (query only) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each → `.ProductFamily` (`MaxioAdvancedBilling.Models.ProductFamily?` — **nullable, null-check**) | **Case B** — `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()` | none — one call returns the whole list | `operations/ProductFamilies.md` |
| `client.ProductFamilies` | `Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` have **no default → must be passed explicitly** | `productFamilyId` = numeric family id **as a string** (step 2); `includeArchived: false`; `filter: null`; `include: null` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each → `.Product` (`MaxioAdvancedBilling.Models.Product`, **`required`** — see §4 hazard 1) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` (defaults 1 / 20); wire `page`, `per_page` | `operations/ProductFamilies.md` |
| `client.Products` (`MaxioAdvancedBilling.Api.Products`) | `Task<ProductResponse> ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — `GET /products/handle/{api_handle}.json` | `apiHandle` = e.g. `"eshop-pro"` | `MaxioAdvancedBilling.Models.ProductResponse` → `.Product` (**`required`**) | **Case B** — `SdkException<RawError>`; `ex.Error.StatusCode` etc. | none | `operations/Products.md` |
| `client.Products` | `Task<IReadOnlyList<ProductResponse>> ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — site-wide, **not** family-scoped; note the date params sit in a **different order** than in the family-scoped op | — | `IReadOnlyList<ProductResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Products.md` |
| `client.Customers` (`MaxioAdvancedBilling.Api.Customers`) | `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json?reference=…` | `reference` = the caller's stable external id | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`MaxioAdvancedBilling.Models.Customer`, **`required`**) | **Case B** — `SdkException<RawError>`; treat `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound` as "create" (§2.7) | none | `operations/Customers.md` |
| `client.Customers` | `Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must be passed explicitly** | `MaxioAdvancedBilling.Models.CreateCustomerRequest` → `Customer (customer): CreateCustomer` **required** | `CustomerResponse` → `.Customer` (**`required`**) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| `client.Customers` | `Task<IReadOnlyList<CustomerResponse>> ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — the 7 params `direction`…`q` have **no default → must be passed explicitly**. Note the date params are **`string?`** here, not `DateTimeOffset?` | `q` (wire `q`) is a **fuzzy search**; the op's own Notes say to use the *lookup* endpoint for an exact reference match | `IReadOnlyList<CustomerResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (defaults 1 / 50) | `operations/Customers.md` |
| `client.Customers` | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` = `Customer.Id` | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each → `.Subscription` (`MaxioAdvancedBilling.Models.Subscription?` — **nullable, null-check**) | **Case B** — `SdkException<RawError>` | **none exposed** — no `page`/`per_page`/state params at all (see Blocker B2) | `operations/Customers.md` |
| `client.Subscriptions` (`MaxioAdvancedBilling.Api.Subscriptions`) | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must be passed explicitly** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription` **required**; §2.2 says which optional members are load-bearing | `SubscriptionResponse` → `.Subscription` (**nullable, null-check**) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions` | `Task<IReadOnlyList<SubscriptionResponse>> ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` have **no default → must be passed explicitly** | **has NO customer filter** (no `customer_id`/`customer_reference` param) and its `product` filter is an **`int?` product id, not a handle** — unusable here, since ids are re-assigned on re-seed | `IReadOnlyList<SubscriptionResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Subscriptions.md` |
| `client.Sites` (`MaxioAdvancedBilling.Api.Sites`) | `Task<SiteResponse> ReadSite(CancellationToken ct = default)` — `GET /site.json` | none | `MaxioAdvancedBilling.Models.SiteResponse` → `.Site` (`MaxioAdvancedBilling.Models.Site`, **`required`**) → `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`, `DefaultPaymentCollectionMethod (default_payment_collection_method): string?`, `Subdomain`, `Currency`, `NetTerms (net_terms): NetTerms?`, `Test (test): bool?` | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`, `records-3-Of-Su.md` |
| `client.Subscriptions` | `Task<SubscriptionResponse> FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **no default → must be passed explicitly**. Looks up by the *subscription's* `reference` (`CreateSubscription.Reference`), not the customer's | — | `SubscriptionResponse` → `.Subscription` | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

**Customer-scoped listing is the only handle-safe path.** `ListSubscriptions` cannot be filtered by
customer, and `ListCustomerSubscriptions` cannot be filtered by state or product — so steps 5 and 7 fetch
the customer's subscriptions and filter **client-side** on `Subscription.Product.Handle` and
`Subscription.State`.

### 2.2 `CreateSubscription` — which members to set (nothing is `required`, so `required?` selects nothing)

`MaxioAdvancedBilling.Models.CreateSubscription` marks **no** member `required`. The operation's Notes name
the fields that decide whether the call is *accepted*:

> "Specify the product with `product_id` or `product_handle`. … Identify an existing customer with
> `customer_id` or `customer_reference`. … To create a new customer, pass customer_attributes."
> "Payment information may be required to create a subscription, depending on the options for the Product
> being subscribed."

| Member (C# → wire) | Type | Set it? | Why |
|---|---|---|---|
| `ProductHandle` (`product_handle`) | `string?` | **YES** — e.g. `"eshop-pro"` | Notes-named product selector; the handle form survives a re-seed |
| `CustomerId` (`customer_id`) | `int?` | **YES** — `Customer.Id` from step 4 | Notes-named "attach an existing customer" path — the path this brief wants |
| `CustomerReference` (`customer_reference`) | `string?` | alternative to `CustomerId`; do **not** set both | Notes-named second existing-customer path |
| `CustomerAttributes` (`customer_attributes`) | `MaxioAdvancedBilling.Models.CustomerAttributes?` | **NO** | Notes-named *create-a-new-customer* path; setting it defeats step 4's idempotent lookup |
| `ProductPricePointHandle` / `ProductPricePointId` | `string?` / `int?` | **NO** | Notes-named, only for a non-default price point; the sandbox products use their default |
| `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` | see model | **NO** | Notes tie payment info to products that require it; the sandbox products are payment-method-not-required |
| `Reference` (`reference`) | `string?` | optional — **recommended** (see Blocker B1) | Your own id for the subscription; the only key `FindSubscription(reference)` can look up |
| **`PaymentCollectionMethod`** (`payment_collection_method`) | **`MaxioAdvancedBilling.Models.Enums.CollectionMethod?`** | **YES — CORRECTED, see §2.2a** | **This row previously said "NO"; live sandbox traffic contradicted it (HTTP 422 "No payment method was on file for the $…  balance").** This is the member that makes Maxio *invoice* the signup balance instead of trying to charge a card |
| `NetTerms` (`net_terms`) | **`string?`** — note: `string`, not `int` (the *response* `Subscription.NetTerms` is `int?` — the two models disagree) | optional, only with invoice/remittance collection | Generated doc: "(Optional) Default: null The number of days after renewal (**on invoice billing**) that a subscription is due. A value between 0 (due immediately) and 180." |
| `NextBillingAt` (`next_billing_at`) | `DateTimeOffset?` | alternative lever — see §2.2a | Generated doc: a **future** value means "no trial or initial charges will be applied … **In fact, no payment will be captured at all**" |
| `InitialBillingAt` (`initial_billing_at`) | `DateTimeOffset?` | alternative lever — see §2.2a | Generated doc: a **future** value creates the subscription in the **Awaiting Signup** state; when the date hits, "if the payment is due at the `initial_billing_at` and it fails the subscription will be immediately canceled" |
| `DeferSignup` (`defer_signup`) | `bool?` **= false** | leave unset for the hero flow — see §2.2a | Generated doc: `true` creates the subscription in the **Awaiting Signup Date** state for an *unknown* first billing date; you later update it with `initial_billing_at`. Generated default is `false` |
| `CalendarBilling` (`calendar_billing`) | `MaxioAdvancedBilling.Models.CalendarBilling?` — fields `SnapDay (snap_day): MaxioAdvancedBilling.Models.AnyOf.SnapDay?` (**union**: `SnapDay.Int(int)` / `SnapDay.String(string)`, read via `TryGetInt` / `TryGetString`) and `CalendarBillingFirstCharge (calendar_billing_first_charge): MaxioAdvancedBilling.Models.Enums.FirstChargeType?` (`Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)`) | **NO** | Record summary: "(Optional). **Cannot be used when also specifying `next_billing_at`**". Aligns billing to a calendar day; it does not remove the signup balance |
| `CouponCode`, `CouponCodes`, `Components`, `Group`, `OfferId`, `ExpiresAt`, `Metafields`, `Currency`, `AgreementAcceptance`, `SkipBillingManifestTaxes` | — | **NO** | Not required by the Notes for this flow; deliberately omitted. (`AgreementAcceptance` summary: "Required when creating a subscription with **Maxio Payments**" — not this flow) |
| `DunningCommunicationDelayEnabled` | `bool?` **= false** | leave unset | Generated default is `false` |

Deliberately-omitted Notes-named fields: `product_id`, `product_price_point_handle`,
`product_price_point_id`, `customer_attributes`, and every payment-profile field (reasons above).
Source: `operations/Subscriptions.md` (Notes) + `records-2-Cr-Ne.md` (`CreateSubscription`);
doc-comment quotations from SDK source `Models/CreateSubscription.cs`.

### 2.2a Subscribing with NO payment method on file — the corrected contract

**What the SDK settles.** The signup balance still falls due even when the product does not *require* a
payment profile; "payment method not required" governs whether a profile must be **entered**, not whether a
due balance can be **charged**. The member that switches Maxio from "charge a card" to "issue an invoice" is:

| Fact | Exact detail | source |
|---|---|---|
| Property | `MaxioAdvancedBilling.Models.CreateSubscription.PaymentCollectionMethod`, wire `payment_collection_method` | `records-2-Cr-Ne.md` |
| Declared type | **`MaxioAdvancedBilling.Models.Enums.CollectionMethod?`** — the *same* enum type on request and response (`Subscription.PaymentCollectionMethod` is also `CollectionMethod?`); there is no separate request-side enum | `records-2-Cr-Ne.md`, `records-3-Of-Su.md` |
| `using` | `using MaxioAdvancedBilling.Models.Enums;` | `models/enums.md` |
| Full member list → wire value | `CollectionMethod.Automatic` → `automatic` · `CollectionMethod.Remittance` → `remittance` · `CollectionMethod.Prepaid` → `prepaid` · `CollectionMethod.Invoice` → `invoice` | `models/enums.md` |
| Which member means "bill me, do not charge a card" | **Architecture-dependent.** The enum's own generated summary: *"The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are — `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are — `remittance`, `automatic`, `prepaid`."* So: **`CollectionMethod.Remittance` on a Relationship-Invoicing site; `CollectionMethod.Invoice` on a legacy Statements site.** `Automatic` is the card-charging default that produced the 422 | `models/enums.md`; source `Models/CreateSubscription.cs` (same text on the property) |

**Determine the architecture from the SDK — do not guess between the two members.**
`await client.Sites.ReadSite(ct: token)` → `MaxioAdvancedBilling.Models.SiteResponse` → `.Site`
(`MaxioAdvancedBilling.Models.Site`, **`required`**) carries
**`RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`** and
**`DefaultPaymentCollectionMethod (default_payment_collection_method): string?`**. `true` ⇒ send
`Remittance`; `false`/absent ⇒ send `Invoice`. Read it once at startup (or per-request with caching) rather
than hard-coding a member. Source: `operations/Sites.md`, `records-3-Of-Su.md`.

**Confirm what actually took**: the response `Subscription.PaymentCollectionMethod` (`CollectionMethod?`)
echoes the method the subscription was created with, and `Subscription.State` shows the resulting state.

Secondary, independent levers on the same model (each with a different side effect — do not stack them
blindly):

- `NextBillingAt` set to a **future** `DateTimeOffset` — generated doc: "no trial or initial charges will be
  applied … **In fact, no payment will be captured at all**. The first payment will be captured … near the
  time specified by `next_billing_at`." This defers the balance rather than invoicing it, so it only moves
  the 422 to the first renewal unless a payment method exists by then.
- `InitialBillingAt` set to a **future** `DateTimeOffset` — creates the subscription **Awaiting Signup**;
  "if the payment is due at the `initial_billing_at` and it fails the subscription will be immediately
  canceled".
- `DeferSignup = true` — creates the subscription in the **Awaiting Signup Date** state for an *unknown*
  first billing date. Resulting state member: `MaxioAdvancedBilling.Models.Enums.SubscriptionState.AwaitingSignup`
  → wire `awaiting_signup`. Such a subscription is **not** `Active`, so the hero flow's "see it reflected in
  their account" would show an awaiting state and no next-billing date; it can later be moved on with
  `client.Subscriptions.ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)`.
- `NetTerms` (`string?`) — days after renewal that an **invoice-billed** subscription is due, "between 0 and
  180". Only meaningful once collection is invoice/remittance.

**Fallback if the site refuses every collection method**: store a payment method first via
`client.PaymentProfiles.CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)`
→ `MaxioAdvancedBilling.Models.CreatePaymentProfileRequest` → `PaymentProfile (payment_profile): CreatePaymentProfile` **required**
(members include `CustomerId (customer_id): int?`, `CurrentVault (current_vault): MaxioAdvancedBilling.Models.Enums.AllVaults?`
— the sandbox member is `AllVaults.Bogus` → wire `bogus`, `FullNumber`, `ExpirationMonth`/`ExpirationYear`
(unions), `ChargifyToken`, `VaultToken`), then pass the resulting profile id as
`CreateSubscription.PaymentProfileId (payment_profile_id): int?`. Its Notes warn that a newly created profile
is **not** automatically current for a subscription. This reintroduces card capture, which the brief excludes —
treat it as a last resort. Source: `operations/PaymentProfiles.md`, `records-1-Ac-Cr.md`, `models/enums.md`.

### 2.3 Models — exact field names (`CSharpName (wire_name): Type`)

| Model | Fields this integration touches | source |
|---|---|---|
| `MaxioAdvancedBilling.Models.CreateCustomerRequest` | `Customer (customer): CreateCustomer` **required** | `records-1-Ac-Cr.md` |
| `MaxioAdvancedBilling.Models.CreateCustomer` | `FirstName (first_name): string` **required**, `LastName (last_name): string` **required**, `Email (email): string` **required**, `Reference (reference): string?` ← **the external-id field**; optional: `Organization`, `CcEmails`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | `records-1-Ac-Cr.md` |
| `MaxioAdvancedBilling.Models.CustomerResponse` | `Customer (customer): Customer` **required** | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.Customer` | `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` — every field nullable | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.ProductResponse` | `Product (product): Product` **required** | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.Product` | `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, **`PriceInCents (price_in_cents): long?`** (cents — divide by 100 to display), `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` ← **the only archived signal on the model**, `ProductFamily (product_family): ProductFamily?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `Taxable (taxable): bool?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`, `InitialChargeInCents (initial_charge_in_cents): long?`, `ExpirationInterval (expiration_interval): int?`, `ExpirationIntervalUnit (expiration_interval_unit): MaxioAdvancedBilling.Models.Enums.ExpirationIntervalUnit?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointName`, `DefaultProductPricePointId`, `PublicSignupPages (public_signup_pages): IReadOnlyList<PublicSignupPage>?`, `VersionNumber (version_number): int?` | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` (**nullable**) | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.ProductFamily` | `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `AccountingCode`, `CreatedAt`, `UpdatedAt`, `ArchivedAt (archived_at): DateTimeOffset?` | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` | `Subscription (subscription): CreateSubscription` **required** | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | `Subscription (subscription): Subscription?` (**nullable**) | `records-4-Su-We.md` |
| `MaxioAdvancedBilling.Models.Subscription` | `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, **`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`** ← the next-billing date (§2.4), `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `Product (product): Product?` (→ `.Handle`, `.Name`, `.PriceInCents`, `.Interval`, `.IntervalUnit`), `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `Customer (customer): Customer?` (→ `.Reference`, `.Id`), `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `PreviousState (previous_state): SubscriptionState?`, `Currency (currency): string?`, `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointType (product_price_point_type): MaxioAdvancedBilling.Models.Enums.PricePointType?` — **there is NO `NextBillingAt` on this model**; `next_billing_at` exists only on the *request* model `CreateSubscription` | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.ErrorListResponse1` | `Errors (errors): IReadOnlyList<string>` **required** — the 422 payload of `CreateSubscription` | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.CustomerErrorResponse1` | `Errors (errors): MaxioAdvancedBilling.Models.Errors?` — the 422 payload of `CreateCustomer` | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.Errors` | `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` — **that is the entire record**; it carries no per-field validation messages (see §5 trust judgment) | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.ListProductsFilter` | `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — **no handle filter**, so pass `filter: null` | `records-2-Cr-Ne.md` |

### 2.4 Next-billing-date: which field is authoritative

Settled from the generated doc-comments in `Models/Subscription.cs` (SDK source `v1.0.2`):

| Property | Type | Generated doc summary | Use |
|---|---|---|---|
| `CurrentPeriodEndsAt` | `DateTimeOffset?` | "Timestamp relating to the end of the current (recurring) period (i.e., **when the next regularly scheduled attempted charge will occur**)" | **This is the "next billing date" to show the user.** |
| `NextAssessmentAt` | `DateTimeOffset?` | "Timestamp that indicates when capture of payment will be tried **or retried**. This value will usually track the `current_period_ends_at`, but **will diverge if a renewal payment fails** and must be retried" | Do **not** surface as "next billing date" — after a failed renewal it is a retry time, not the billing date. |

Both are nullable: render "—" when `CurrentPeriodEndsAt` is null rather than silently substituting
`NextAssessmentAt`. Source: `records-3-Of-Su.md` (field list) + SDK source `Models/Subscription.cs` (doc comments).

### 2.5 Enums — the values this integration needs

Enums are **not** C# enums: they are `record`s deriving from
`MaxioAdvancedBilling.Core.Enum.StringEnum<T>` (base `MaxioAdvancedBilling.Core.Enum.TypedEnum<string, T>`);
the concrete types live in **`MaxioAdvancedBilling.Models.Enums`**. Read the wire string via the inherited
**`.Value`** property (`string`), compare with `==` (record equality), and test membership of the documented
set with **`IsKnownValue()`** — the deserializer accepts *any* string, so an unrecognised state value
round-trips into an instance equal to none of the members (source `Core/Enum/TypedEnum.cs`,
`Core/Enum/StringEnum.cs`). Construct with the static members or `SubscriptionState.FromValue("active")`.

| Enum | Members (`CSharpMember (wire)`) | source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionStateFilter` (only for `ListSubscriptions`) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` — note it is a **different type** from `SubscriptionState` with a **different value set** | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — generated summary: **"For legacy Statements Architecture valid options are — `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are — `remittance`, `automatic`, `prepaid`."** See §2.2a | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.FirstChargeType` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.AllVaults` (only if a payment profile is ever stored) | 34 members; the sandbox one is `Bogus (bogus)` — summary: "Use `bogus` for testing" | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SortingDirection` | `Asc (asc)`, `Desc (desc)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `models/enums.md` |

Which states count as "already subscribed" for the step-5 idempotency check is an application decision —
the SDK only supplies the value list above.

`| Live-subscription state set for idempotency | choose from the SubscriptionState members above | YOUR CALL — not in the map |`

### 2.6 Client construction, auth, and the two base-URL modes

Package **`AsadAli.AdvancedBilling.Sdk`**, root namespace **`MaxioAdvancedBilling`** (they differ — install
by package id, `using` the namespace). Install via NuGet only; never add a project reference to SDK sources.
This sheet describes **v1.0.2** (map stamp: commit `15db14b`, tag `v1.0.2`); pin that version, because
another version's surface is not what is documented here.

`| Which versions are published on nuget.org | pin 1.0.2 so the app matches this sheet | YOUR CALL — not in the map |`

`using` directives needed (C# does **not** import child namespaces transitively):

```csharp
using MaxioAdvancedBilling;                            // client, options, ServerOptions, ServiceCollectionExtensions
using MaxioAdvancedBilling.Api;                        // Customers, Products, ProductFamilies, Subscriptions
using MaxioAdvancedBilling.Servers;                    // ServerEnvironment, ProductionOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;  // BasicAuthCredentials
using MaxioAdvancedBilling.Core.Configuration;         // RetryOptions
using MaxioAdvancedBilling.Core.Exceptions;            // SdkException<T>
using MaxioAdvancedBilling.Core.ErrorResponse;         // RawError, ApiError
using MaxioAdvancedBilling.Errors;                     // CreateCustomerError, CreateSubscriptionError, ListProductsForProductFamilyError, FindSubscriptionError
using MaxioAdvancedBilling.Models;                     // records
using MaxioAdvancedBilling.Models.Enums;               // SubscriptionState, IntervalUnit, …
```

| Fact | Exact member(s) | source |
|---|---|---|
| Client type | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **`sealed`, no interface**; sole constructor `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md`; source `MaxioAdvancedBillingClient.cs` |
| Controller properties | `client.Customers`, `client.Products`, `client.ProductFamilies`, `client.Subscriptions` — types in `MaxioAdvancedBilling.Api`, each `sealed` with an **`internal` constructor** (not constructible or mockable from your code) | source `MaxioAdvancedBillingClient.cs`, `Api/Customers.cs` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — settable properties: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` = `Us`), `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions` (default `RetryOptions.Default()`), `Server: MaxioAdvancedBilling.ServerOptions` (default `new()`), `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` (default `null`) | `sdk-map.md`; source `MaxioAdvancedBillingClientOptions.cs` |
| **Auth — the exact pattern** | `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" };` — **`Username` = the API key; `Password` = the literal one-character string `"x"`**. Both members are `required` `init`-only `string`. The SDK base64-encodes `"{Username}:{Password}"` and sets the `Authorization: Basic …` header itself — do not build the header yourself | `sdk-map.md` (Servers & auth); source `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment enum | `MaxioAdvancedBilling.Servers.ServerEnvironment` — `ServerEnvironment.Us` (wire `"US"`, default) and `ServerEnvironment.Eu` (wire `"EU"`); `ServerEnvironment.Default()` returns `Us`. A `StringEnum` record, not a C# enum | `sdk-map.md`; source `Servers/ServerEnvironment.cs` |
| **Mode A — subdomain-derived** | `options.Server.Production.Us.Site = <Maxio:Subdomain>;` and leave `BaseUrl` at its default template `"https://{site}.chargify.com"` → requests go to `https://<subdomain>.chargify.com`. `Site`'s default is the literal string `"subdomain"`, which is why it must always be set. EU equivalent: `options.Server.Production.Eu.Site`, default template `"https://{site}.ebilling.maxio.com"` | `sdk-map.md` (Servers & auth); source `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| **Mode B — explicit base-URL override** | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>;` — the base URL is a template in which **only `{site}` is substituted**, so a value containing no `{site}` placeholder is used **verbatim** (one trailing `/` is trimmed before the path is appended). Set it on the branch matching `options.Environment` (`.Us` for the default `ServerEnvironment.Us`, `.Eu` for `ServerEnvironment.Eu`). Setting `Site` as well is harmless when the URL has no `{site}` | source `Servers/ProductionOptions.cs`, `Core/TemplateParamsFactory.cs`, `Core/UriFactory.cs` |
| Types on the override path | `options.Server` is `MaxioAdvancedBilling.ServerOptions` (**root namespace, NOT `.Servers`**); `.Production` is `MaxioAdvancedBilling.Servers.ProductionOptions`; `.Us` / `.Eu` are its nested classes `ProductionOptions.UsOptions` / `ProductionOptions.EuOptions`, each exposing `string BaseUrl` and `string Site`. A second group `options.Server.Ebb` (`MaxioAdvancedBilling.Servers.EbbOptions`) exists for event-ingest endpoints and is **not** touched by any operation in this plan | source `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| Config-key → member mapping | `Maxio:ApiKey` → `BasicAuthCredentials.Username` (with `Password = "x"`); `Maxio:Subdomain` → `options.Server.Production.Us.Site`; `Maxio:BaseUrl` (optional) → `options.Server.Production.Us.BaseUrl` **when non-empty**, used instead of the default template; `Maxio:ProductFamilyHandle` → **not an SDK setting** — it is the value matched against `ProductFamily.Handle` in step 2 | brief + the rows above |
| DI registration | `MaxioAdvancedBilling.ServiceCollectionExtensions` (root namespace) declares, as a C# 14 extension member on `IServiceCollection`: `IServiceCollection AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`. Its body calls `services.AddHttpClient()`, then registers `MaxioAdvancedBillingClient` as a **SINGLETON** built from `IHttpClientFactory.CreateClient()` (the default, unnamed client). **The `configure` callback runs once, at registration time**, and the options instance is captured in the closure — so `Maxio:*` values are read at startup and never re-read per request (no `IOptionsSnapshot`-style reload) | `sdk-map.md`; source `ServiceCollectionExtensions.cs` |
| Retry/timeout member names | `options.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; **every member is `required`**, so start from `RetryOptions.Default()` and mutate rather than `new`-ing one. Members: `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>`, `HttpMethodsToRetry: IReadOnlyList<HttpMethod>`, `MaxRetries: int`, `Delay: TimeSpan`, `Timeout: TimeSpan?`, `BackOffFactor: int`, `UseExponentialBackoff: bool`, `MaxJitter: TimeSpan`, `OnRetry: Action<RetryAttempt>?`. **What each actually governs — and what it does not — is in the companion skill (§3); do not infer it from the names.** | `sdk-map.md` (RetryOptions table) |

### 2.7 Errors — the exact shapes reaching your catch blocks

| Fact | Exact detail | source |
|---|---|---|
| Exception type | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`, declared `public sealed class SdkException<TError> : System.Exception`. **There is no non-generic `SdkException` base** — `SdkException<RawError>` and `SdkException<CreateCustomerError>` are unrelated closed generic types, so one `catch` clause cannot cover both. The ladder needs one clause per operation's error type plus a `System.Exception` backstop | source `Core/Exceptions/SdkException.cs` |
| Members on the exception | **`Error` only** (`public required TError Error { get; init; }`) plus what `System.Exception` provides. **The exception itself has no `StatusCode` and no body** — both come from the `RawError` inside | source `Core/Exceptions/SdkException.cs` |
| Status + raw body (Case B) | `ex.Error.StatusCode` → `System.Net.HttpStatusCode`; `ex.Error.ReadAsString()` → `string`; `ex.Error.ReadAsBytes()` → `ReadOnlyMemory<byte>`; `ex.Error.ReadAsJson<T>()` → `T?` | `sdk-map.md`; source `Core/ErrorResponse/RawError.cs` |
| Status + raw body (Case A) | Typed errors derive from `MaxioAdvancedBilling.Core.ErrorResponse.ApiError`, whose **only** public member is `TryGetRawError(out RawError error): bool`. The typed accessor and the raw fallback are **mutually exclusive**: when the status matched the typed shape, `TryGetRawError` returns `false`, so on a 422 you get the typed payload and **no `StatusCode` at all** — infer the status from *which* accessor succeeded | source `Core/ErrorResponse/ApiError.cs`, `Errors/CreateCustomerError.cs` |
| `CreateCustomer` 422 | `catch (SdkException<CreateCustomerError> ex)` → `ex.Error.TryGetCustomerErrorResponse1(out var e422)` → `e422.Errors` is `MaxioAdvancedBilling.Models.Errors?` with **only** `PerPage` and `PricePoint` (`IReadOnlyList<string>?`) — no general message list (see §5) | `records-2-Cr-Ne.md`; source `Errors/CreateCustomerError.cs` |
| `CreateSubscription` 422 | `catch (SdkException<CreateSubscriptionError> ex)` → `ex.Error.TryGetErrorListResponse1(out var e422)` → `e422.Errors` is `IReadOnlyList<string>` (**`required`**) — join the strings for display | `records-2-Cr-Ne.md` |
| `ListProductsForProductFamily` 404 | `catch (SdkException<ListProductsForProductFamilyError> ex)` → `ex.Error.TryGetString(out var message)` — a bare `string` payload, not a record | `operations/ProductFamilies.md` |
| **`ReadCustomerByReference` 404** | This op is **Case B**: any non-2xx throws `SdkException<RawError>` carrying the response status verbatim. Detect "no such customer" as `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound` and branch to create. There is **no typed error class** for it and **no union/`AnyOf` accessor** involved | `operations/Customers.md`; source `Api/Customers.cs` (mapped with `RawErrorResponse.Instance`) |
| Union accessors | **No operation in this plan returns or throws a union type.** The only union near this flow is `CreateSubscription.OfferId`, which this plan does not set — so no union `TryGet…` pattern is required anywhere | `models/unions.md`, `records-2-Cr-Ne.md` |

`| Whether the lookup endpoint really answers 404 (rather than a 2xx with no customer) for an unknown reference | code the not-found branch on status 404 AND treat a 2xx whose customer payload is absent as not-found — never let either shape fall through to "exists" | UNVERIFIED |`

### 2.8 Wire routing — HTTP verb + path template per operation (for an `HttpMessageHandler` stub)

Every template below is **verbatim as the SDK builds it**, including the `.json` suffix, the
`{placeholder}` names, and the leading `/`. All eight are on the **Production** server group.

| # | Operation | Verb | Path template (verbatim) | Query | source |
|---|---|---|---|---|---|
| 1 | `ProductFamilies.ListProductFamilies` | `GET` | `/product_families.json` | `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime` | `operations/ProductFamilies.md` |
| 2 | `ProductFamilies.ListProductsForProductFamily` | `GET` | `/product_families/{product_family_id}/products.json` — the `productFamilyId` argument is substituted for **`{product_family_id}`** | `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include` | `operations/ProductFamilies.md` |
| 3 | `Customers.CreateCustomer` | `POST` | `/customers.json` | none | `operations/Customers.md` |
| 4 | `Customers.ListCustomerSubscriptions` | `GET` | `/customers/{customer_id}/subscriptions.json` — the `customerId` argument is substituted for **`{customer_id}`** | none | `operations/Customers.md` |
| 5 | `Subscriptions.CreateSubscription` | `POST` | `/subscriptions.json` | none | `operations/Subscriptions.md` |
| 6 | `Customers.ReadCustomerByReference` | `GET` | `/customers/lookup.json` | `reference` (a **query** param — it is *not* in the path) | `operations/Customers.md` |
| 7 | `Sites.ReadSite` | `GET` | `/site.json` — singular `site`, no `s` | none | `operations/Sites.md` |
| 8 | `Products.ReadProductByHandle` | `GET` | `/products/handle/{api_handle}.json` — the `apiHandle` argument is substituted for **`{api_handle}`** | none | `operations/Products.md` |

**How the path joins the base URL.** The templates carry a leading `/`, but the SDK does not simply
concatenate: it expands the placeholders, then joins as `baseUrl.TrimEnd('/') + "/" + path.TrimStart('/')`.
So exactly one `/` separates them regardless of whether the configured base URL ends in a slash — a stub
must match on the resulting absolute URL's `AbsolutePath` (`/subscriptions.json`), not on string
concatenation of its own base. Placeholder values are `Uri.EscapeDataString`-escaped when substituted.
Source: `Core/TemplateParamsFactory.cs`, `Core/UriFactory.cs`.

### 2.9 Outgoing request body — exactly what `CreateSubscriptionRequest` serialises to

The body is `JsonRequest.Create(body)` → `System.Net.Http.Json.JsonContent.Create(model)` with **no custom
`JsonSerializerOptions`**, so the `[JsonPropertyName]` attributes on the records are the whole naming story
(Content-Type `application/json; charset=utf-8`). Source: `Api/Subscriptions.cs`, `Core/Request/JsonRequest.cs`.

| C# member | Wire name in the POST body | Nesting |
|---|---|---|
| `CreateSubscriptionRequest.Subscription` | **`subscription`** | top-level object |
| `CreateSubscription.ProductHandle` | **`product_handle`** | inside `subscription` |
| `CreateSubscription.CustomerId` | **`customer_id`** (JSON number) | inside `subscription` |
| `CreateSubscription.PaymentCollectionMethod` | **`payment_collection_method`** — serialised as the **bare wire string** (`"remittance"`) by `StringEnumConverter<CollectionMethod>`, not as an object | inside `subscription` |
| `CreateSubscription.Reference` | **`reference`** | inside `subscription` |

**Two members are always written, even when you never set them.** Every property on `CreateSubscription`
carries `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` — **except `defer_signup` and
`dunning_communication_delay_enabled`**, which have no ignore condition and default to `false`. So a
minimal subscribe body is:

```json
{"subscription":{"product_handle":"eshop-pro","payment_collection_method":"remittance","customer_id":123,
 "reference":"…","defer_signup":false,"dunning_communication_delay_enabled":false}}
```

Assert on **parsed JSON**, not on string equality: member order follows the record's declaration order, and
the two always-present booleans will fail a naive exact-string assertion. `CreateCustomerRequest` behaves the
same way — top-level `customer`, with `first_name`, `last_name`, `email` always written (they are `required`)
and every other member omitted when null. Source: `Models/CreateSubscription.cs`,
`Models/CreateSubscriptionRequest.cs`, `Models/CreateCustomer.cs`, `Models/Enums/CollectionMethod.cs`.

---

## 3. Trap notes

> ⚠ **Step 1 (client registration / DI).** `AddMaxioAdvancedBillingClient` registers a singleton over
> `IHttpClientFactory.CreateClient()` and snapshots the options at registration — but whether that lifetime
> is right for `src/PublicApi`, and who then owns the handler pipeline, is exactly what the signature cannot
> tell you. **MUST load `dotnet-client-initialization`** before wiring the client into the service container.

> ⚠ **Step 1 (credentials).** Where the API key is read from, and when credentials are attached relative to
> client construction, decides whether a key rotation can ever take effect — the options object is captured
> once. **MUST load `dotnet-authentication`** before writing the `BasicAuth` wiring.

> ⚠ **Step 1 (resilience & base URL).** The SDK's retry/timeout options do **not** bound a whole call and
> are **not** the timeout on the `HttpClient` you register; and which verbs and failure kinds get re-sent
> decides whether a failed `POST /subscriptions` can reach Maxio more than once — the same double-charge
> risk the idempotency requirement exists to prevent. **MUST load `dotnet-configuration-resilience`** before
> setting `options.Retry` or the base URL.

> ⚠ **Steps 3, 5, 7 (list calls).** Every list operation here has a long tail of nullable parameters with no
> C# default; a positional call mis-binds silently, and a wrong `page`/`perPage` loop silently truncates the
> plan list or the user's subscriptions. **MUST load `dotnet-calling-endpoints`** before the first
> `client.…` call.

> ⚠ **Steps 3–7 (models & enums).** Enums here are `StringEnum` records, response envelopes wrap their
> payload one level down, some envelope members are `required` while others are nullable, and JSON fields
> the models do not declare are discarded — all of which change how Maxio data maps onto eShopOnWeb DTOs.
> **MUST load `dotnet-models`** before constructing request payloads or mapping responses.

> ⚠ **Step 8 (error boundary).** `SdkException<T>` is sealed with no non-generic base, typed and raw
> accessors are mutually exclusive, and `JsonException` can arrive on both the success and the failure path
> (§4). What a correct ladder looks like, and which failures must never be retried, is not derivable from
> these shapes. **MUST load `dotnet-error-handling`** before writing the boundary.

> ⚠ **Tests.** The client is `sealed`, controllers are `sealed` with `internal` constructors, and no
> interfaces are generated — the only seam is the `HttpClient` constructor argument. **MUST load
> `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING — load **before implementation starts**

These carry what this sheet deliberately does **not** restate (defaults, semantics, worked examples, what
you must still wire yourself). Load each before writing the step it governs.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `MaxioAdvancedBillingClient`, `HttpClient` ownership, lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuth` credentials, key sourcing, rotation |
| `dotnet-configuration-resilience` | Step 1 — `options.Retry`, timeouts, base-URL/server selection, pagination |
| `dotnet-calling-endpoints` | Steps 2–7 — every `client.{Group}.{Operation}(…)` call, named arguments, cancellation |
| `dotnet-models` | Steps 3–7 — request models, `required` members, `StringEnum`, envelope unwrapping |
| `dotnet-error-handling` | Step 8 — the catch ladder, status/body extraction, what must not be retried |
| `dotnet-testing` | Tests — the `HttpClient` seam, error-path coverage |

**Two `System.Text.Json.JsonException` hazards reach the boundary from opposite directions and need
opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary. Live here, not hypothetical: `ProductResponse.Product` and
  `CustomerResponse.Customer` are both `required`, so a 200 whose body lacks `product` / `customer` throws
  instead of yielding a null payload;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed. Also live here: `CreateCustomerError.Create` runs
  `FromJson<CustomerErrorResponse1>` on **every** 422 before the exception is thrown (source
  `Errors/CreateCustomerError.cs`), and `ErrorListResponse1.Errors` is `required`.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

### Trust judgment on the 422 error contracts (evidence: the generated definitions themselves)

Two generated definitions of "the validation errors of a write" **disagree**: `CreateSubscription`'s 422
payload is `ErrorListResponse1.Errors : IReadOnlyList<string>` (**required**, an *array*), while
`CreateCustomer`'s 422 payload is `CustomerErrorResponse1.Errors : Errors`, an **object** whose entire
declared surface is `PerPage (per_page)` and `PricePoint (price_point)` — two fields with nothing to do with
customer validation. `Errors` is plainly a shared model bound to the wrong operation. Consequences to code
for, both **UNVERIFIED** (only live traffic can confirm which shape the site actually returns):

`| CreateCustomer 422 message text | extract best-effort from the typed payload's PerPage / PricePoint lists, and when both are null fall back to a generic "customer could not be created" message — never assume a message list is present | UNVERIFIED |`

`| A 422 whose errors member is an array (CreateCustomer) or an object (CreateSubscription) | wrap the awaited SDK call so System.Text.Json.JsonException is caught alongside SdkException<…> and mapped to a deterministic 4xx-style rejection, NOT a retryable 5xx — the status code is already gone by the time it reaches you | UNVERIFIED |`

### Assumptions

1. **Caller identity → Maxio `reference`.** The Maxio customer is keyed by `CreateCustomer.Reference`
   (wire `reference`), derived from the authenticated caller. Which token claim supplies that stable
   identifier is not an SDK fact.
   `| Caller identity → the value written to reference | resolve from the app's own identity path | YOUR CALL — not in the map |`
2. **Customer name/email.** `CreateCustomer.FirstName`, `.LastName` and `.Email` are all C# `required` —
   the request cannot be constructed without them. Where eShopOnWeb sources a first/last name for a user
   whose identity record may hold only an email is an application decision.
   `| Source of FirstName / LastName / Email | resolve from the app's own identity or profile store | YOUR CALL — not in the map |`
3. **Plan selection.** `POST /api/subscriptions` carries a product **handle**; when absent, `eshop-pro` is
   the default target per the brief. Validate the submitted handle against the step-3 family listing before
   calling `CreateSubscription`, so an arbitrary site-wide handle cannot be subscribed.
4. **Price display.** `Product.PriceInCents` and `Subscription.ProductPriceInCents` are `long?` **cents**.
   The `Product` model exposes **no currency field** (only `Subscription.Currency` does), so the currency
   label beside a plan price is not available from the plan listing.
   `| Currency label for a plan price | supply from configuration, or from the subscription's Currency | YOUR CALL — not in the map |`
5. **Telling the user up front that a plan needs a payment method.** Use
   **`Product.RequireCreditCard (require_credit_card): bool?`** — generated doc: *"Boolean that controls
   whether a payment profile is required to be entered for customers wishing to sign up on this product."*
   That is the correct field. **Do NOT use `Product.RequestCreditCard (request_credit_card): bool?`** —
   generated doc: *"Deprecated value that can be ignored unless you have legacy hosted pages. For Public
   Signup Page users, read this attribute from under the signup page."* Both are `bool?`, so a null means
   "not stated", not "false". **Caveat proven by live traffic:** `RequireCreditCard == false` does **not**
   imply the signup will succeed without a payment method — it governs whether a profile must be *entered*,
   not whether a balance due at signup can be *settled*. Treat it as a hint for the UI, never as a
   precondition check that replaces §2.2a. Source: `records-3-Of-Su.md`; SDK source `Models/Product.cs`.
6. **Archived / hidden plans.** Pass `includeArchived: false` **and** additionally drop products whose
   `ArchivedAt` is non-null. There is **no `hidden`/`visible` flag on `Product`** in the map — the nearest
   field, `PublicSignupPages`, is a different concept. Do not invent a hidden filter.
7. **The metered component `api-call` is out of scope** — none of the three endpoints reports usage, and no
   usage/allocation operation is planned (those live on `client.SubscriptionComponents`).
8. **Endpoint conventions.** Route shapes, DTOs, auth-policy names and registration style of
   `src/PublicApi` belong to that project; this sheet does not name them.
   `| PublicApi endpoint / DTO / auth-policy conventions | follow the project's existing endpoint pattern | YOUR CALL — not in the map |`

### Blockers

**B1 — The SDK offers no idempotency key, so "a double-click must never create two subscriptions" cannot be
satisfied by the SDK alone.** `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct)`
takes no idempotency-key parameter, `CreateSubscription` declares no such field
(`operations/Subscriptions.md`, `records-2-Cr-Ne.md`), and the client exposes no per-request header hook.
The step-5 check-then-create is therefore **not atomic**: two concurrent requests can both observe "no
subscription" and both POST. Someone must decide the application-side guard — a per-user serialization
point, a uniqueness constraint in eShopOnWeb's own store, or an explicit acceptance of the window —
**before this endpoint is written**; the plan is incomplete until that decision exists. Related, and equally
application-side: the same non-atomicity applies to step 4 (`ReadCustomerByReference` → `CreateCustomer`),
except that Maxio's documented rule that `reference` must be unique (`CreateCustomer` Notes: "you may only
create one customer for a given reference value") means the loser of that race gets a 422 rather than a
duplicate customer — so code the "reference already taken" 422 as *re-read the customer*, not as a failure.
Setting `CreateSubscription.Reference` to a deterministic per-user-per-product value makes a duplicate
detectable after the fact via `FindSubscription(reference)`, but does **not** make the create atomic.

**B2 — `ListCustomerSubscriptions` exposes no pagination and no filters.** Its whole signature is
`(int customerId, CancellationToken ct)` — no `page`, `per_page`, `state` or product parameter
(`operations/Customers.md`). It is nevertheless the only customer-scoped listing (`ListSubscriptions` has no
customer filter at all), so steps 5 and 7 depend on it returning the customer's *complete* set. Whether the
server caps that list cannot be determined from the SDK.
`| Whether ListCustomerSubscriptions returns every subscription for a customer | code steps 5 and 7 so that a missing entry yields "not subscribed" (a safe, visible outcome) rather than a silent wrong answer, and keep the client-side state/handle filter explicit | UNVERIFIED |`

**B4 — RESOLVED (verified live on site `cp-exp-2`).** With
`PaymentCollectionMethod = CollectionMethod.Remittance`, chosen from `client.Sites.ReadSite()` →
`Site.RelationshipInvoicingEnabled == true`, `CreateSubscription` returns **HTTP 201** with
`Subscription.State == "active"` and a populated `CurrentPeriodEndsAt` (one month out), for **both** seeded
plans, with **no payment profile involved**. Both UNVERIFIED rows below are therefore now **VERIFIED**: the
site accepts `Remittance`, and the resulting subscription is `Active` with a next-billing date — so the hero
flow can show plan / price / state / next-billing-date straight from the create response. The original
finding is kept below for the record.

<details><summary>Original B4 (resolved) — signup refused with 422 "No payment method was on file for the $… balance"</summary>

**NOT an SDK gap — the SDK exposes the lever; it was missing from the request.** The corrected
contract is §2.2a: send
`PaymentCollectionMethod = CollectionMethod.Remittance` (Relationship-Invoicing site) or
`CollectionMethod.Invoice` (legacy Statements site), choosing the member from
`client.Sites.ReadSite(...)` → `Site.RelationshipInvoicingEnabled`. The default when the member is omitted
is card collection, which is what produced the 422. This is therefore neither an SDK gap nor necessarily a
site misconfiguration — the request simply asked Maxio to charge a card for a balance with no card on file.

What the SDK could not settle, now closed by the live run above:

`| Whether site cp-exp-2 accepts the chosen CollectionMethod member and creates the subscription without a stored payment method | send the member selected from Site.RelationshipInvoicingEnabled; on a further 422, read the message list from ErrorListResponse1.Errors and surface it verbatim rather than retrying — a 422 here is deterministic and will never succeed on retry | VERIFIED live: Remittance accepted, HTTP 201, no payment profile |`

`| Whether the resulting subscription is Active (so the hero flow can show plan/price/state/next-billing-date) or lands in a non-active state | assert on the returned Subscription.State and CurrentPeriodEndsAt from the create response itself; render whatever state comes back | VERIFIED live: State == active, CurrentPeriodEndsAt populated one month out |`

The guidance stands for code that must keep working across sites: still *select* the member from
`Site.RelationshipInvoicingEnabled` rather than hard-coding `Remittance`, and still render the returned
state rather than assuming `Active`.

</details>

If invoice/remittance collection is *also* refused, the remaining causes are outside every SDK member: the
site's collection-method configuration, or the product's price-point/billing configuration. In that case the
only SDK-expressible path is storing a payment profile first (§2.2a fallback), which the brief excludes —
escalate as a **site/product configuration issue**, not an SDK gap.

**B3 — Page-boundary detection for the plan list is inference, not a documented contract.**
`ListProductsForProductFamily` returns a bare `IReadOnlyList<ProductResponse>` — **no total and no
next-page marker** (`operations/ProductFamilies.md`). The only available stop condition is "a page returned
fewer items than `perPage`". The maximum accepted `perPage` is not in the map.
`| Maximum accepted per_page value | loop pages with an explicit upper bound on iterations, stopping on a short or empty page; never assume one page holds the whole family | UNVERIFIED |`
