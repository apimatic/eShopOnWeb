# Maxio Advanced Billing — integration plan & CONTRACT SHEET (eShopOnWeb / src/PublicApi)

Scope: recurring-subscription billing, additive and parallel to Catalog→Basket→Order.
SDK: NuGet package **`AsadAli.AdvancedBilling.Sdk`**, pin **`1.0.2`** (published versions are `1.0.0`,
`1.0.1`, `1.0.2`; `1.0.2` is the version the bundled SDK map was generated from — source tag `v1.0.2`,
commit `15db14b`). Root namespace for `using` directives is **`MaxioAdvancedBilling`**, which is *not* the
package id.

```
dotnet add src/PublicApi/PublicApi.csproj package AsadAli.AdvancedBilling.Sdk --version 1.0.2
```

---

## 1. Scope & sequence

| # | Step | SDK operations / types used |
|---|---|---|
| 1 | Add package + bind options from configuration section `Maxio` (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`). | — |
| 2 | Register the client in DI (`AddMaxioAdvancedBillingClient`), set Basic auth, set the server site/base URL. | `MaxioAdvancedBillingClientOptions`, `BasicAuthCredentials`, `ServerEnvironment`, `ServerOptions` |
| 3 | Plan catalog service: resolve the configured product-family **handle** → family id, then page products. | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | `GET /api/subscription-plans` endpoint: map `Product` → your plan DTO. | `ProductResponse.Product` |
| 5 | Enrollment service (idempotent): resolve/create the Maxio customer by **reference**, then look for an existing subscription before creating one. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 6 | `POST /api/subscriptions` endpoint: run step 5, return the existing subscription unchanged when one is found. | `SubscriptionResponse.Subscription` |
| 7 | `GET /api/my-subscriptions`: resolve customer by reference → list that customer's subscriptions → project state/plan/price/period end. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 8 | Error boundary: translate SDK exceptions to your HTTP responses (see §3 + REQUIRED READING). | `SdkException<T>`, `RawError`, typed `…Error` classes |
| 9 | Tests: stub `HttpMessageHandler` behind the `HttpClient` ctor argument; wrap the SDK behind your own interface. | `MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` |

### Step 3 detail — family lookup by handle (why two calls)

`ProductFamilies.ReadProductFamily` takes **`int id`** in C#, even though the provider's own prose on that
operation says a family "can be specified either with the id number, or with the `handle:my-family` format".
The generated signature cannot carry `handle:my-family`, so **there is no by-handle read** for product
families. Resolve the handle this way:

1. `ListProductFamilies(dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct)`
2. Match `ProductFamilyResponse.ProductFamily?.Handle` against `Maxio:ProductFamilyHandle`.
3. Use the matched `ProductFamily.Id` (an `int?` — guard the null) formatted with
   `CultureInfo.InvariantCulture` as the `productFamilyId` string argument of `ListProductsForProductFamily`.

Do **not** pass `"handle:my-family"` into `ListProductsForProductFamily`'s `productFamilyId`: path template
values are escaped with `Uri.EscapeDataString` before substitution (`Core/TemplateParamsFactory.cs`), so the
colon is sent as `%3A`. Whether the provider accepts the escaped form is not something the SDK can settle —
`UNVERIFIED`, and the list-and-match route above avoids the question entirely.

### Step 5 detail — the idempotency sequence

1. `ReadCustomerByReference(reference, ct)` → found ⇒ use `CustomerResponse.Customer.Id`.
   Not found ⇒ see §3 for how "not found" surfaces.
2. If absent: `CreateCustomer(new CreateCustomerRequest { Customer = new CreateCustomer { … } }, ct)`.
   The provider's Notes on `CreateCustomer` state: *"you may only create one customer for a given
   reference value. If provided, the `reference` value must be unique."* So on a `422` from a racing
   double-click, re-run step 1 and use the customer it returns instead of failing the request.
3. Before creating a subscription, look for an existing one:
   `ListCustomerSubscriptions(customerId, ct)` and match on `Subscription.Product?.Handle` == requested
   plan handle **and** a live `Subscription.State` (see the state table in §2.4). If found, return it.
4. Only then `CreateSubscription`. Set `CreateSubscription.Reference` to a value your app derives
   deterministically from (caller identity + product handle) so a duplicate is detectable afterwards via
   `Subscriptions.FindSubscription(reference, ct)`; whether the provider itself *rejects* a duplicate
   subscription reference is **UNVERIFIED** (the SDK's Notes make that guarantee for **customer**
   `reference` only, not for subscription `reference`) — so treat step 3 as the real duplicate guard and
   do not rely on the provider to reject the second write.
5. Serialize concurrent enrollment for one caller in your own code — see §6, `YOUR CALL` row.

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

### 2.1 Operations

| Controller property | Method signature (verbatim, params in order) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters are nullable **with no default ⇒ must be passed explicitly** (pass `null`) | none (GET) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; read `.ProductFamily` (`product_family`, **nullable**) → `.Handle`, `.Id` | **Case B** — `SdkException<RawError>`; `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none (returns the whole list) | `operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` have **no default ⇒ pass explicitly** (`null`); `page`/`perPage` default to `1`/`20` | none (GET). Query wire names: `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; read `.Product` (`product`, **required**) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` [all other statuses] | manual `page` + `perPage`; plain array response (no total/next envelope) → request `page = 1,2,…` until a page returns **fewer than `perPage`** items | `operations/ProductFamilies.md` |
| `client.Products` (`MaxioAdvancedBilling.Api.Products`) | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — use only if you must validate a single plan handle | none (GET) | `ProductResponse` → `.Product` | **Case B** — `SdkException<RawError>` | none | `operations/Products.md` |
| `client.Customers` (`MaxioAdvancedBilling.Api.Customers`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json`, query `reference` | none (GET) | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`customer`, **required**) → `.Id (id): int?`, `.Email`, `.Reference` | **Case B** — `SdkException<RawError>`; detect not-found via `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — the 7 params `direction`…`q` have **no default ⇒ pass explicitly**; note the date params here are **`string?`**, not `DateTimeOffset?` | none (GET). `q` is the free-text search (Notes: search by email, Advanced Billing id, organization, reference, first/last name) | `IReadOnlyList<CustomerResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (default `50`); array response, same stop rule as above | `operations/Customers.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable with **no default ⇒ pass explicitly** | `MaxioAdvancedBilling.Models.CreateCustomerRequest` → `Customer (customer): CreateCustomer` **required**. `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string` **required**, `LastName (last_name): string` **required**, `Email (email): string` **required**, `Reference (reference): string?`, `Organization (organization): string?`, `CcEmails (cc_emails): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse` → `.Customer.Id` | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422 only]** · `TryGetRawError(out RawError)` [every other status, incl. 401/404/5xx] | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (GET) | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; read `.Subscription` (`subscription`, **nullable — null-check it**) | **Case B** — `SdkException<RawError>` | **none — this operation has no `page`/`per_page` parameters at all.** You get whatever the provider returns in one response; there is no SDK-side way to page it, and `ListSubscriptions` has **no customer filter** to fall back on. | `operations/Customers.md` |
| `client.Subscriptions` (`MaxioAdvancedBilling.Api.Subscriptions`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **no default ⇒ pass explicitly** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription` **required**. Fields used (full carry list in §2.2): `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `Reference (reference): string?` | `SubscriptionResponse` → `.Subscription` (**nullable**) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` **[422 only]** · `TryGetRawError(out RawError)` [everything else] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **no default ⇒ pass explicitly** | none (GET), query `reference` | `SubscriptionResponse` → `.Subscription` | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError(out RawError)` [fallback] — note **both** accessors hand you a `RawError`, so read the status off whichever returns `true` | none | `operations/Subscriptions.md` |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, **no default ⇒ pass explicitly** (`null`) | none (GET) | `SubscriptionResponse` → `.Subscription` | **Case B** — `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` **no default ⇒ pass explicitly**. **`product` is the numeric product id — there is no product-handle filter and no customer filter here.** | none (GET) | `IReadOnlyList<SubscriptionResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Subscriptions.md` |

### 2.2 `CreateSubscription` — which optional fields to carry, and which were deliberately left out

⚠ **`CreateSubscription` marks *nothing* required** — every field is nullable, so `required?` selects
nothing for you and the compiler will happily let you send an empty subscription object. The provider's
Notes on `CreateSubscription` decide what is accepted:

> *"Specify the product with `product_id` or `product_handle`. To set a specific product price point, use
> `product_price_point_handle` or `product_price_point_id`. Identify an existing customer with
> `customer_id` or `customer_reference`. Optionally, include an existing payment profile using
> `payment_profile_id`. To create a new customer, pass customer_attributes."*

**Carry (from those Notes):**

| C# field | Wire name | Type | Use here |
|---|---|---|---|
| `ProductHandle` | `product_handle` | `string?` | the plan handle from the request — this is the handle-based product reference the brief asks for |
| `CustomerId` | `customer_id` | `int?` | the id from step 5.1/5.2 — **this is the "use existing customer" field** |
| `CustomerReference` | `customer_reference` | `string?` | alternative to `CustomerId` for an existing customer; set **one**, not both |
| `CustomerAttributes` | `customer_attributes` | `CustomerAttributes?` | **only** if you choose to create customer+subscription in a single call instead of steps 5.1–5.2 |
| `Reference` | `reference` | `string?` | your deterministic subscription reference (enables `FindSubscription`) |

`MaxioAdvancedBilling.Models.CustomerAttributes` (all optional): `FirstName (first_name)`, `LastName
(last_name)`, `Email (email)`, `CcEmails (cc_emails)`, `Organization (organization)`, `Reference
(reference)`, `Address (address)`, `Address2 (address_2)`, `City (city)`, `State (state)`, `Zip (zip)`,
`Country (country)`, `Phone (phone)`, `Verified (verified): bool?`, `TaxExempt (tax_exempt): bool?`,
`VatNumber (vat_number)`, `Metafields (metafields): IReadOnlyDictionary<string,string>?`, `ParentId
(parent_id): int?`, `SalesforceId (salesforce_id)`, `DefaultAutoRenewalProfileId
(default_auto_renewal_profile_id): int?`. Source: `records-2-Cr-Ne.md`.

**Deliberately left out** (present on `CreateSubscription`, not sent by this plan, and why): the
Notes-named payment fields `PaymentProfileId (payment_profile_id)` and the card/bank payloads
`CreditCardAttributes (credit_card_attributes)`, `PaymentProfileAttributes (payment_profile_attributes)`,
`BankAccountAttributes (bank_account_attributes)` — the brief states both plans are
payment-method-not-required with no trial and no setup fee, so no card capture and no 3DS flow;
`ProductPricePointHandle (product_price_point_handle)` / `ProductPricePointId (product_price_point_id)` —
omitted so the product's default price point is used; and the out-of-scope `CouponCode`/`CouponCodes`,
`Components`, `CalendarBilling`, `Metafields`, `Group`, `OfferId`, `PrepaidConfiguration`,
`NextBillingAt`/`InitialBillingAt`/`PreviousBillingAt`, `Currency`, `NetTerms`, `PaymentCollectionMethod`,
`AgreementAcceptance`, `AchAgreement`, `DeferSignup`. Source: `records-2-Cr-Ne.md` + the
`CreateSubscription` Notes in `operations/Subscriptions.md`.

⚠ The Notes also say a 3DS-required payment returns **422 with an `action_link`**. With
payment-method-not-required plans that path should not be reached; if a 422 ever carries an action link
your boundary must not present it as a validation message — see the `UNVERIFIED` row in §6.

### 2.3 Response models — the fields the integration reads

All records below live in **`MaxioAdvancedBilling.Models`**. Format: `CSharpName (wire_name): Type`.

**`ProductFamilyResponse`** — `ProductFamily (product_family): ProductFamily?` (**nullable**). Source: `records-3-Of-Su.md`.

**`ProductFamily`** — `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `AccountingCode (accounting_code): string?`, `Description (description): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `ArchivedAt (archived_at): DateTimeOffset?`. Source: `records-3-Of-Su.md`.

**`ProductResponse`** — `Product (product): Product` **required** (a 2xx body without `product` fails to deserialize; see REQUIRED READING). Source: `records-3-Of-Su.md`.

**`Product`** — the fields the plans endpoint needs: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`, `InitialChargeInCents (initial_charge_in_cents): long?`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointName (product_price_point_name): string?`, `DefaultProductPricePointId (default_product_price_point_id): int?`, `Taxable (taxable): bool?`, `VersionNumber (version_number): int?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`. Source: `records-3-Of-Su.md`.

⚠ **"payment method required" is `RequireCreditCard`**, but the model carries a *second*, similarly named
`RequestCreditCard` and the generated code documents neither. Surface `RequireCreditCard`, treat `null` as
"unknown" rather than "not required", and label the distinction `UNVERIFIED` (§6). Also filter out plans
whose `ArchivedAt` is non-null in addition to passing `includeArchived: false`.

**`SubscriptionResponse`** — `Subscription (subscription): Subscription?` (**nullable — unlike `ProductResponse`/`CustomerResponse`, whose payload field is `required`**). Source: `records-4-Su-We.md`.

**`Subscription`** — fields the my-subscriptions endpoint reads: `Id (id): int?`, `State (state): SubscriptionState?`, `PreviousState (previous_state): SubscriptionState?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `DelayedCancelAt (delayed_cancel_at): DateTimeOffset?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `CancellationMethod (cancellation_method): CancellationMethod?`, `ExpiresAt (expires_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `BalanceInCents (balance_in_cents): long?`, `Currency (currency): string?`, `Reference (reference): string?`, `Product (product): Product?`, `Customer (customer): Customer?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointType (product_price_point_type): PricePointType?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`. Source: `records-3-Of-Su.md`.

⚠ **There is no `next_billing_at` on the `Subscription` response model.** `next_billing_at` exists only on
the *request* side (`CreateSubscription.NextBillingAt (next_billing_at)`). For "next billing / current
period end" read **`CurrentPeriodEndsAt`**, with `NextAssessmentAt` as the secondary field. The
`UpdateSubscription` Notes corroborate this: *"The server response will not return data under the key/value
pair of `next_billing_at`. View the key/value pair of `current_period_ends_at`…"*
(`operations/Subscriptions.md`). Plan name/handle/price for a subscription come from the nested
`Subscription.Product?.Name` / `.Handle` / `.PriceInCents`, with `Subscription.ProductPriceInCents` as the
subscription-level price.

**`CustomerResponse`** — `Customer (customer): Customer` **required**. Source: `records-2-Cr-Ne.md`.

**`Customer`** — `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, plus `Address (address)`, `Address2 (address_2)`, `City (city)`, `State (state)`, `StateName (state_name)`, `Zip (zip)`, `Country (country)`, `CountryName (country_name)`, `Phone (phone)`, `Verified (verified): bool?`, `TaxExempt (tax_exempt): bool?`, `VatNumber (vat_number)`, `ParentId (parent_id): int?`, `Locale (locale)`, `SalesforceId (salesforce_id)`, `Maxioid (maxioid)`. Source: `records-2-Cr-Ne.md`.

**Error payload records** — `MaxioAdvancedBilling.Models.ErrorListResponse1`: `Errors (errors): IReadOnlyList<string>` **required** (used by `CreateSubscriptionError` @422). `MaxioAdvancedBilling.Models.CustomerErrorResponse1`: `Errors (errors): Errors?`, where `MaxioAdvancedBilling.Models.Errors` is `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` (used by `CreateCustomerError` @422). Source: `records-2-Cr-Ne.md`.

⚠ **Trust judgment, from map/source-visible evidence only:** the two generated 422 payloads disagree — one
models `errors` as an array of strings, the other as an object whose only fields are `per_page` and
`price_point`, which are paging/price-point concepts rather than customer-validation concepts, and `Errors`
is a *shared* model reused across operations. Treat `TryGetCustomerErrorResponse1` as **low-trust**:
extract messages best-effort and fall back to a generic message plus the raw body/status (`UNVERIFIED`,
§6). This is exactly the shape that produces the second hazard row in REQUIRED READING.

### 2.4 Enums

All in **`MaxioAdvancedBilling.Models.Enums`**. These are **not C# enums** — each is a
`sealed record X : MaxioAdvancedBilling.Core.Enum.StringEnum<X>` exposing a public `string Value`, an
`IsKnownValue()` check, `ToString()` returning the wire value, an implicit conversion to `string`, and a
static `FromValue(string)`. Compare with `==` against the static members (record value equality on
`Value`); a wire value the SDK doesn't know deserializes into an instance carrying that raw string rather
than throwing, so `IsKnownValue()` is how you detect a state Maxio added after this SDK build.

| Enum | Members (`CSharpName (wire_value)`) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `SubscriptionStateFilter` (only for `ListSubscriptions(state:)`) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` — **a different type from `SubscriptionState` with a different member set** (no `pending`/`assessing`/`failed_to_create`/`awaiting_signup`/`paused`; adds `expired_cards`, `pending_cancellation`, `pending_renewal`) | `models/enums.md` |
| `IntervalUnit` (`Product.IntervalUnit`) | `Day (day)`, `Month (month)` | `models/enums.md` |
| `ExpirationIntervalUnit` (`Product.ExpirationIntervalUnit`) | `Day (day)`, `Month (month)`, `Never (never)` | `models/enums.md` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` | `models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `models/enums.md` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `models/enums.md` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `models/enums.md` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` | `models/enums.md` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` | `models/enums.md` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` | `models/enums.md` |
| `SubscriptionInclude` (`ReadSubscription(include:)`) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `models/enums.md` |
| `SubscriptionListInclude` (`ListSubscriptions(include:)`) | `SelfServicePageToken (self_service_page_token)` | `models/enums.md` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `models/enums.md` |

**"Active" for rendering/filtering:** the map gives the value list but does **not** define which states your
product treats as entitling. `SubscriptionState.Active` is the unambiguous one; `Trialing`, `PastDue`,
`Assessing`, `SoftFailure`, `OnHold`, `Suspended` are judgement calls — see the `YOUR CALL` row in §6. The
`SubscriptionState` doc-summary in `models/enums.md` explicitly warns that `assessing` and `pending` are
transient internal states and that access decisions should not be based on them.

### 2.5 Client construction, auth, server/base URL

| Item | Exact contract | Source |
|---|---|---|
| Client type | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **`sealed`**; the only constructor is `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` ("Getting a client"), `MaxioAdvancedBillingClient.cs` |
| Controller properties | `client.Customers` → `MaxioAdvancedBilling.Api.Customers`; `client.Subscriptions` → `…Api.Subscriptions`; `client.Products` → `…Api.Products`; `client.ProductFamilies` → `…Api.ProductFamilies`. Each controller class is **`public sealed` with an `internal` constructor** — they cannot be subclassed or mocked. | `sdk-map.md`, `MaxioAdvancedBillingClient.cs`, `Api/ProductFamilies.cs` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment` · `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions` · `Server: MaxioAdvancedBilling.ServerOptions` · `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. All four are settable `{ get; set; }` and pre-initialised, so you assign only what you need. | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` — HTTP Basic, **username = the API key, password = the literal `"x"`**. `BasicAuthCredentials` is `public sealed class` with `public required string Username { get; init; }` and `public required string Password { get; init; }` — both must be set in the object initializer. | `sdk-map.md` ("Servers & auth"), `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `MaxioAdvancedBilling.Servers.ServerEnvironment` exposes exactly two: `ServerEnvironment.Us` (wire `US`, **the default**) → `https://{site}.chargify.com`, and `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com`. **There is no separate "sandbox"/"test" environment** — a Maxio sandbox site is an ordinary site on the same host, selected by its subdomain, so sandbox vs production is `Maxio:Subdomain` (plus a sandbox API key), not `Environment`. Use `ServerEnvironment.Us` unless the account is EU-hosted. | `sdk-map.md` ("Servers & auth") |
| Subdomain (site) | `options.Server.Production.Us.Site = <Maxio:Subdomain>` — `MaxioAdvancedBilling.ServerOptions.Production` is `MaxioAdvancedBilling.Servers.ProductionOptions`, whose `Us` is the nested `ProductionOptions.UsOptions` with `public string BaseUrl { get; set; }` and `public string Site { get; set; }` (and a parallel `Eu` of type `ProductionOptions.EuOptions`). **`Site` defaults to the literal string `"subdomain"`** — leave it unset and every call goes to `https://subdomain.chargify.com`, which is a silent misconfiguration rather than a startup error. Set `.Eu.Site` too if you ever select `ServerEnvironment.Eu`; each environment reads its own options object. | `sdk-map.md`, `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| Verbatim base-URL override | **Supported — this is not a gap.** `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`. The default value is the template `"https://{site}.chargify.com"`; the SDK builds the final URL by string-replacing `{site}` inside `BaseUrl` and then joining `baseUrl.TrimEnd('/') + "/" + path.TrimStart('/')`. A verbatim URL containing no `{site}` placeholder therefore passes through **unchanged** (a base URL with a path prefix is preserved too), and `Site` becomes irrelevant for that group. Bind it as: if `Maxio:BaseUrl` is non-empty set `BaseUrl` (and skip `Site`), otherwise set `Site` from `Maxio:Subdomain` and leave `BaseUrl` at its default template. The Ebb/events group (`options.Server.Ebb.*`) is used by no operation in this plan. | `sdk-map.md` ("Servers & auth"), `Servers/ProductionOptions.cs`, `Core/TemplateParamsFactory.cs` |
| DI registration | `MaxioAdvancedBilling.ServiceCollectionExtensions` provides `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` (extension on `IServiceCollection`, returns `IServiceCollection`). It calls `services.AddHttpClient()` and registers **`MaxioAdvancedBillingClient` as a singleton**, resolving one `HttpClient` from `IHttpClientFactory.CreateClient()` at first resolution and holding it for the process lifetime. The `configure` callback runs **once, at registration time** — it cannot read scoped services, and options captured there are frozen for the app's lifetime (an `IOptionsMonitor` reload will not reach the client). | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Retry/timeout knobs | `options.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (a `record`) with members `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>` · `HttpMethodsToRetry: IReadOnlyList<HttpMethod>` · `MaxRetries: int` · `Delay: TimeSpan` · `Timeout: TimeSpan?` · `BackOffFactor: int` · `UseExponentialBackoff: bool` · `MaxJitter: TimeSpan` · `OnRetry: Action<RetryAttempt>?`. **Every member is `required`**, so you cannot construct a partial instance — start from the static `RetryOptions.Default()` and use a `with` expression to change individual members. What each knob actually governs: see the trap note in §3.2. | `sdk-map.md` (RetryOptions table), `Core/Configuration/RetryOptions.cs` |
| Config binding keys | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). No SDK-side default exists for any of them; the only SDK defaults in play are `Site = "subdomain"` and `BaseUrl = "https://{site}.chargify.com"` above. Bind them through the options pattern on section `"Maxio"` and fail fast at startup when `ApiKey`, `Subdomain` (unless `BaseUrl` is set) or `ProductFamilyHandle` is missing. | `YOUR CALL — not in the map` (key names come from the brief) |
| Language/TFM requirement | The SDK ships `netstandard2.0` but its public types use `required` members (`BasicAuthCredentials`, `RetryOptions`, the request records). Object-initializer code that sets them must compile with **C# 11 or later**. | `Core/Authentication/Basic/BasicAuthCredentials.cs`, `Core/Configuration/RetryOptions.cs` |

### 2.6 `using` directives this integration needs

```csharp
using MaxioAdvancedBilling;                            // client, options, ServerOptions, AddMaxioAdvancedBillingClient
using MaxioAdvancedBilling.Servers;                    // ServerEnvironment, ProductionOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;  // BasicAuthCredentials
using MaxioAdvancedBilling.Core.Configuration;         // RetryOptions, RetryAttempt
using MaxioAdvancedBilling.Core.Exceptions;            // SdkException<T>
using MaxioAdvancedBilling.Core.ErrorResponse;         // RawError, ApiError
using MaxioAdvancedBilling.Errors;                     // CreateCustomerError, CreateSubscriptionError, FindSubscriptionError, ListProductsForProductFamilyError
using MaxioAdvancedBilling.Models;                     // records: Product, Subscription, CreateCustomerRequest, …
using MaxioAdvancedBilling.Models.Enums;               // SubscriptionState, IntervalUnit, …
```

C# does not import child namespaces transitively — `using MaxioAdvancedBilling.Models;` alone leaves every
enum and error type unresolved (`CS0246`). Source: `sdk-map.md` ("Namespaces").

---

## 3. Error handling, resilience, pagination, testing

### 3.1 Exception facts (contract — resolved here)

- Every operation is **throw-only**: this SDK generates **no** `…Result` / `ApiResult` no-throw variants,
  so every call must be wrapped. (`sdk-map.md`, error-handling model.)
- The thrown type is `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`, declared
  `public sealed class SdkException<TError> : Exception` with a single member
  `public required TError Error { get; init; }`. Consequences you must design around:
  - **There is no non-generic `SdkException` base, and no status code on the exception itself.** You cannot
    write one `catch (SdkException ex)` that covers the SDK; each closed generic
    (`SdkException<RawError>`, `SdkException<CreateCustomerError>`, `SdkException<CreateSubscriptionError>`,
    `SdkException<FindSubscriptionError>`, `SdkException<ListProductsForProductFamilyError>`) is an
    unrelated type and needs its own `catch`. Catch them at the call site, translate to one billing
    exception of your own, and let the endpoint boundary handle that single type.
  - `ex.Message` carries no API information (the SDK never sets one) — never surface it or parse it. The
    status code comes **only** from a `RawError`.
- **Reading status + body.** Case B: `ex.Error` *is* a `RawError` → `ex.Error.StatusCode`
  (`System.Net.HttpStatusCode`), `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. Case A: call the
  operation's status-specific `TryGet…` first, then `TryGetRawError(out var raw)` and read
  `raw.StatusCode`. `RawError` buffers the body, so `ReadAsString()` is safe after the response is disposed
  and safe to call repeatedly. (`sdk-map.md` error-core table, `Core/ErrorResponse/RawError.cs`.)
- **Which status reaches which accessor** (read off the generated error classes, not assumed):
  `CreateCustomerError` maps **only 422** to `TryGetCustomerErrorResponse1`; `CreateSubscriptionError` maps
  **only 422** to `TryGetErrorListResponse1`; `FindSubscriptionError` maps **404** to `TryGetNoContent`;
  `ListProductsForProductFamilyError` maps **404** to `TryGetString`. **401, 403 and 5xx always fall to
  `TryGetRawError`** on every one of them — auth failures never have a typed shape anywhere in this
  integration. (`operations/*.md`, `Errors/CreateCustomerError.cs`.)
- **Mapping onto your API's responses** — the SDK supplies the status; the mapping is a product decision,
  and this is the shape the facts support: 404 from `ReadCustomerByReference` / `FindSubscription` ⇒ "not
  enrolled" (a normal branch, not an error); 422 ⇒ 400/422 to your caller with messages extracted
  best-effort; 401/403 from Maxio ⇒ **never** propagate as 401 to your JWT caller (it means your API key or
  subdomain is wrong, not that the caller is unauthenticated) — log and return 502/503; 429 and 5xx ⇒ 503.

⚠ **Step 8 (error boundary)** — which exception types actually arrive, what a catch ladder silently
misses, and why `TryGetRawError` is not a catch-all: **MUST load `dotnet-error-handling`** before writing
any `try`/`catch` around an SDK call.

### 3.2 Trap notes

⚠ **Step 2 (client registration)** — the SDK's retry/timeout options do **not** bound a whole call and are
**not** the timeout on the `HttpClient` you register; which of `MaxRetries`, `Timeout`,
`StatusCodesToRetry`, `HttpMethodsToRetry` may legally be set to what, and what `Timeout` actually
measures, are settled there. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 (client registration)** — `AddMaxioAdvancedBillingClient` registers a **singleton** holding one
`IHttpClientFactory`-created `HttpClient` for the process lifetime; whether that ownership is right for
your app, and whether the SDK wrapper should instead be transient over a factory-managed handler pipeline,
is the client-lifetime question. **MUST load `dotnet-client-initialization`** before choosing between the
DI extension and constructing the client yourself.

⚠ **Step 5/6 (POST /api/subscriptions)** — whether a `CreateCustomer`/`CreateSubscription` POST that fails
on the wire can be **re-sent by the SDK's resilience layer**, and therefore whether one call from your
endpoint can produce two Maxio records without your code ever looping, is decided by the retry
configuration — not by your idempotency check. This is the single biggest risk to the "a double-click must
never create two customers or two subscriptions" requirement. **MUST load
`dotnet-configuration-resilience`** before you consider the idempotency work done.

⚠ **Steps 3–7 (every call)** — many optional parameters on these list/find operations have **no C#
default** and mis-bind in a positional call; the correct calling convention (named arguments, cancellation
flow, async usage) lives there. **MUST load `dotnet-calling-endpoints`** before the first
`client.{Group}.{Operation}(…)` call.

⚠ **Steps 4/6/7 (mapping models)** — these enums are `StringEnum<T>` records rather than C# enums, payloads
sit one level inside an envelope, and JSON fields the models don't declare are not preserved; how to build
request records and read enum/union values safely is there. **MUST load `dotnet-models`** before
constructing request payloads or projecting `Product`/`Subscription` onto your DTOs.

⚠ **Step 9 (tests)** — the `HttpClient` constructor argument is the only seam (the client and all four
controllers are `sealed` with `internal` constructors, so nothing below `MaxioAdvancedBillingClient` can be
mocked); what to fake and what to assert so the tests don't encode SDK internals is there. **MUST load
`dotnet-testing`** before writing the service-layer tests.

⚠ **Step 2 (credentials)** — where credentials must be set relative to client construction, and how to
source the key without hardcoding it, is the auth step's concern. **MUST load `dotnet-authentication`**
before setting `BasicAuth`.

### 3.3 Pagination shape (all list operations in scope)

Manual `page` + `perPage` only. Every list operation returns a **bare `IReadOnlyList<…>`** — there is no
envelope carrying a total count, a next-page cursor or a "has more" flag, and the SDK exposes no auto-paging
enumerable for these operations. Loop `page = 1, 2, …` with a fixed `perPage`, stop when a page returns
fewer than `perPage` items, and cap the loop so a provider that ignores `page` cannot spin forever.
`ListCustomerSubscriptions` has **no paging parameters at all** (§2.1).

### 3.4 Testing seam

Construct `new MaxioAdvancedBillingClient(new HttpClient(fakeHandler), options)` in tests, with
`options.Server.Production.Us.BaseUrl` pointed at a literal test host and a fake `HttpMessageHandler`
returning canned JSON. Because the controllers are sealed with internal constructors, your service layer
should depend on **your own** interface (one method per use case) and keep the SDK types behind it, so unit
tests of endpoint logic need no HTTP at all and only the thin adapter is exercised through the handler seam.

---

## 4. REQUIRED READING — load before implementation starts

These companion skills must be loaded **before implementation starts**. This sheet deliberately does not
carry their contents: it names the hazard and the step it bites at; the skill carries the defaults, the
worked examples, and the parts you must still wire yourself.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, DI registration, `HttpClient` ownership and lifetime |
| `dotnet-authentication` | Step 2 — supplying the Basic credentials and sourcing the API key |
| `dotnet-configuration-resilience` | Step 2 + steps 5/6 — retries, timeouts, base-URL selection, pagination, and whether a write can be re-sent |
| `dotnet-calling-endpoints` | Steps 3–7 — calling operations, optional-parameter binding, request/response envelopes |
| `dotnet-models` | Steps 4, 6, 7 — building request records, `StringEnum<T>` values, mapping onto your DTOs |
| `dotnet-error-handling` | Step 8 — the exception boundary (always required; every integration writes one) |
| `dotnet-testing` | Step 9 — what to fake and what to assert |

**Two hazard rows that must shape the boundary from its first version** — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Both directions are live in *this* integration, not hypothetical: `ProductResponse.Product` and
`CustomerResponse.Customer` are `required`, so a 2xx body missing that key throws on deserialize; and the
422 path of `CreateCustomerError` parses the body into `CustomerErrorResponse1` with **no** guard around
the parse (`Core/ErrorResponse/ApiError.cs` → `FromJson<TBody>`), so a 422 whose body doesn't match that
odd `per_page`/`price_point` shape loses the 422 status entirely.

---

## 5. Assumptions & Blockers

**Assumptions**

1. The caller's Maxio `reference` is derived from the eShopOnWeb identity in the JWT and is stable across
   logins; the plan uses it for `ReadCustomerByReference` and for the deterministic subscription
   `reference`. Which claim supplies it is `YOUR CALL` (§6).
2. `Maxio:ProductFamilyHandle` names exactly one product family on the configured site, and every
   subscribable plan is a **product** in that family (not a price point of a product). If plans are modelled
   as price points, step 3 lists the wrong things and the plan needs revising.
3. The site is US-hosted (`ServerEnvironment.Us`). An EU-hosted account needs `Environment` **and**
   `Server.Production.Eu.Site`/`.BaseUrl` set instead.
4. Sandbox is expressed as a sandbox subdomain + sandbox API key, not as a distinct SDK environment (the
   SDK exposes only `Us` and `Eu`).
5. Both plans really are payment-method-not-required, so no payment-profile fields are sent (§2.2). If a
   plan's `RequireCreditCard` is `true`, `CreateSubscription` will need card capture and this plan does not
   cover it.
6. Cancellation is out of scope: only the `SubscriptionState`/`CancellationMethod` enums are surfaced. No
   cancel operation is planned (`SubscriptionStatus` is a separate controller with 10 operations, not
   grounded here).

**Blockers**

None. Nothing in scope requires a capability the map lacks: the missing by-handle product-family read is
worked around with `ListProductFamilies` + handle match (§1, step 3 detail), and the verbatim base-URL
override the brief asked about **is** supported (§2.5) — that is not a gap to report.

---

## 6. Rows the map cannot settle

| Item | Resolution | Source |
|---|---|---|
| Which JWT claim identifies the caller, and the exact `reference` string built from it | resolve from the app's own identity path; must be stable and unique per user | `YOUR CALL — not in the map` |
| Which `SubscriptionState` values count as "active/entitling" for rendering and for the duplicate check in step 5.3 | product decision; `Active` is unambiguous, `Trialing`/`PastDue`/`OnHold`/`Suspended` are yours to classify | `YOUR CALL — not in the map` |
| Serialising two concurrent `POST /api/subscriptions` for the same caller (the double-click window between the "does one exist" read and the create) | application concurrency/persistence decision — the SDK offers no idempotency key and the read-then-create sequence is not atomic | `YOUR CALL — not in the map` |
| Whether to persist the Maxio `customer_id`/`subscription_id` locally instead of re-looking-up by reference on every request | application persistence decision; the SDK supports both | `YOUR CALL — not in the map` |
| The request contract of your own three endpoints (route shapes, DTO field names, status codes) | yours; §3.1 only supplies the Maxio status you map from | `YOUR CALL — not in the map` |
| Whether a lookup miss on `ReadCustomerByReference` really arrives as **404** (vs a 200 with an empty body) | Code defensively: treat `RawError.StatusCode == HttpStatusCode.NotFound` as "no customer" and treat **no other status** as not-found; because `CustomerResponse.Customer` is `required`, a 200 whose body lacks `customer` throws `JsonException` — catch it separately, log operation + raw status, and surface the generic failure rather than silently reporting "not enrolled". | `UNVERIFIED` |
| Whether the provider rejects a duplicate **subscription** `reference` (the customer-reference uniqueness guarantee in the Notes does not extend to subscriptions) | Do not depend on it: keep the step-5.3 existing-subscription check as the real guard, and treat a successful create against a reference you already used as possible duplication to reconcile. | `UNVERIFIED` |
| Whether `Product.RequireCreditCard` (vs `RequestCreditCard`) is the field the live payload sets for "payment method required" | Extract `RequireCreditCard` best-effort; treat `null` as unknown and render "unknown" rather than "not required"; if both are non-null and disagree, prefer `RequireCreditCard` and log the disagreement once. | `UNVERIFIED` |
| Whether a 422 from `CreateCustomer` actually carries the generated `CustomerErrorResponse1` shape (`per_page`/`price_point`) | Extract best-effort: call `TryGetCustomerErrorResponse1` inside its own guard, and on anything unexpected fall back to `TryGetRawError` → `StatusCode` + `ReadAsString()` and a generic message. Never let the typed-extraction path be the only way a 422 produces a response. | `UNVERIFIED` |
| Whether the provider accepts `handle:my-family` percent-escaped (`handle%3Amy-family`) in the `product_family_id` path segment | Avoided by design — resolve the numeric id via `ListProductFamilies` and never send the handle form. | `UNVERIFIED` |
| Whether a 422 on `CreateSubscription` can carry a 3DS `action_link` for a payment-method-not-required plan | Extract `ErrorListResponse1.Errors` best-effort; if the raw body contains an action link, do not present it as a validation message — log it and return a generic failure, since this integration captures no card. | `UNVERIFIED` |
