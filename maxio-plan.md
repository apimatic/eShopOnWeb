# Maxio Advanced Billing — contract sheet for eShopOnWeb recurring subscriptions

Scope: additive subscription billing on `src/PublicApi`, parallel to the existing one-time flow.
Everything below is grounded in the bundled SDK map (pages cited per row) or, where the map does not
carry a body, in the SDK source at the commit the map was generated from (tag `v1.0.2`, source files
cited by name). Metered component `api-call` usage reporting is **out of scope** and appears nowhere here.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Package + client registration (options, Basic auth, site/base-URL selection, DI) | — (`MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `AddMaxioAdvancedBillingClient`) |
| 2 | Resolve the product family `eshop-subscribe` (handle → id) once per request or cached by the app | `ProductFamilies.ListProductFamilies` |
| 3 | `GET /api/subscription-plans` — list sellable plans of that family | `ProductFamilies.ListProductsForProductFamily` (+ `Sites.ReadSite` for display currency; `Products.ReadProductByHandle` for a single plan) |
| 4 | `POST /api/subscriptions` step 1 — ensure a Maxio customer exists for the caller (lookup by reference, else create) | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` step 2 — idempotency guard: does this customer already have a subscription to this plan handle? | `Customers.ListCustomerSubscriptions` |
| 6 | `POST /api/subscriptions` step 3 — enroll by **product handle** + existing **customer id** | `Subscriptions.CreateSubscription` |
| 7 | `GET /api/my-subscriptions` — the caller's subscriptions with plan name, price, state, next billing date | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |

Nothing in this plan uses a numeric product id or a numeric product-family id that is not resolved at
runtime from a handle. The only numeric id the integration holds across calls is the Maxio **customer id**,
which it re-derives from the caller's reference on every request (step 4 / step 7).

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

| Controller (property · type) | Method signature (verbatim, params in order) | Request model + fields | Response envelope + fields read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` · `MaxioAdvancedBilling.Api.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters are nullable **with no C# default → must be passed explicitly**; call as `ListProductFamilies(null, null, null, null, null, ct: ct)` | none (GET) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each element has exactly one member `ProductFamily (product_family): ProductFamily?` (**nullable — may be null on a drifted element**). Read `ProductFamily.Handle`, `.Id` | **Case B** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `ex.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | none | `operations/ProductFamilies.md`, `models/records-3-Of-Su.md` |
| `client.ProductFamilies` · `MaxioAdvancedBilling.Api.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable **with no C# default → must be passed explicitly**; call as `ListProductsForProductFamily(familyId.ToString(), null, null, null, null, null, null, false, null, page: p, perPage: 100, ct: ct)`. **`productFamilyId` is a `string`** even though it carries a numeric id | none (GET). Query wire names: `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each element has exactly one member `Product (product): Product` (**`required` → non-null**). Read `Product.Handle/.Name/.Description/.PriceInCents/.Interval/.IntervalUnit/.ArchivedAt/.RequireCreditCard/.ProductFamily` | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [all other statuses]. ⚠ On 404 the SDK deserializes the body **as a JSON string** (`Errors/ListProductsForProductFamilyError.cs`), so a 404 whose body is a JSON *object* throws `JsonException` instead of the `SdkException` | manual `page` + `perPage` (defaults 1 / 20). Loop while `result.Count == perPage` | `operations/ProductFamilies.md`, `models/records-3-Of-Su.md` |
| `client.Products` · `MaxioAdvancedBilling.Api.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — `GET /products/handle/{api_handle}.json` | none (GET) | `MaxioAdvancedBilling.Models.ProductResponse` → `.Product` (`required`, non-null) | **Case B** — `SdkException<RawError>`; not-found surfaces as `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Products.md` |
| `client.Products` · `MaxioAdvancedBilling.Api.Products` *(alternative to the family-scoped list — site-wide)* | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **note the parameter order differs from `ListProductsForProductFamily`** (`endDate`/`endDatetime` come *before* `startDate`/`startDatetime`) | none (GET) | `IReadOnlyList<ProductResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (1 / 20) | `operations/Products.md` |
| `client.Sites` · `MaxioAdvancedBilling.Api.Sites` | `ReadSite(CancellationToken ct = default)` | none (GET) | `MaxioAdvancedBilling.Models.SiteResponse` → `Site (site): Site` (`required`); read `Site.Currency (currency): string?`, `Site.Subdomain`, `Site.Test (test): bool?` | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`, `models/records-3-Of-Su.md` |
| `client.Customers` · `MaxioAdvancedBilling.Api.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json?reference=…` | none (GET); query wire name `reference` | `MaxioAdvancedBilling.Models.CustomerResponse` → `Customer (customer): Customer` (**`required` → non-null**); read `Customer.Id (id): int?`, `.Reference`, `.Email` | **Case B** — `SdkException<RawError>`. **On 404 (customer not found) this is the exception you get**: `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound`. It does **not** return `null` and there is no typed 404 accessor. Confirmed in `Api/Customers.cs` (the operation is wired to the raw error deserializer) | none | `operations/Customers.md` |
| `client.Customers` · `MaxioAdvancedBilling.Api.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable **with no default → must be passed explicitly** | `MaxioAdvancedBilling.Models.CreateCustomerRequest` → `Customer (customer): CreateCustomer` **required**. See §2.2 for `CreateCustomer` members | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`required`); read `.Id` | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [all other statuses]. ⚠ See §2.6 — on 422 the typed payload cannot carry the message and `TryGetRawError` returns **false** | none | `operations/Customers.md`, `models/records-1-Ac-Cr.md`, `models/records-2-Cr-Ne.md` |
| `client.Customers` · `MaxioAdvancedBilling.Api.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `GET /customers/{customer_id}/subscriptions.json` | none (GET) | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each element has exactly one member `Subscription (subscription): Subscription?` (**nullable**). Read `Subscription.Id/.State/.CurrentPeriodEndsAt/.NextAssessmentAt/.ProductPriceInCents/.Currency/.CreatedAt/.Product?.Name/.Product?.Handle` | **Case B** — `SdkException<RawError>` | **none** — the operation exposes no `page`/`perPage` parameters at all | `operations/Customers.md`, `models/records-4-Su-We.md` |
| `client.Subscriptions` · `MaxioAdvancedBilling.Api.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable **with no default → must be passed explicitly** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription` **required**. See §2.3 for the members that matter | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `Subscription (subscription): Subscription?` (**nullable — guard before dereferencing**) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [all other statuses]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **required** → the 422 messages array is `string.Join("; ", e.Errors)`. ⚠ `TryGetRawError` returns **false** on a 422 (the raw slot is only filled for non-422) | none | `operations/Subscriptions.md`, `models/records-2-Cr-Ne.md` |
| `client.Subscriptions` · `MaxioAdvancedBilling.Api.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — `GET /subscriptions/lookup.json?reference=…`; `reference` is nullable **with no default → must be passed explicitly** | none (GET) | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription` (nullable) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [other]. ⚠ On 404 use **`TryGetNoContent`** — `TryGetRawError` returns **false** for that status (`Errors/FindSubscriptionError.cs`) | none | `operations/Subscriptions.md` |

### 2.2 `CreateCustomer` (inner model of `CreateCustomerRequest`) — namespace `MaxioAdvancedBilling.Models`

| Member (wire name) | Type | Required? |
|---|---|---|
| `FirstName (first_name)` | `string` | **`required`** — object initializer will not compile without it |
| `LastName (last_name)` | `string` | **`required`** |
| `Email (email)` | `string` | **`required`** |
| `Reference (reference)` | `string?` | optional in C#, but **this is the idempotency key** — the operation's Notes state you may create only one customer per `reference` value and that it is how a customer is retrieved by your own app's id |
| `Organization`, `CcEmails`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt (bool?)`, `TaxExemptReason`, `ParentId (int?)`, `SalesforceId` | as listed | optional — **deliberately left unset by this plan**; the operation's Notes attach ISO-3166 formatting rules to `country`/`state`, which only matter if you start sending an address |

Source: `models/records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`), `operations/Customers.md` (Notes).

Consequence for step 4: `FirstName`/`LastName`/`Email` are C# `required`, so the integration must supply
three non-null strings for every customer it creates. Where those values come from in the application's
identity store is **YOUR CALL — not in the map**; if only an email is available, a placeholder must be
chosen deliberately rather than left to the compiler.

Read-back model `MaxioAdvancedBilling.Models.Customer` — members this integration reads:
`Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName`/`LastName: string?` ·
`CreatedAt (created_at): DateTimeOffset?`. Note `Id` is `int?` — the customer id you pass to
`ListCustomerSubscriptions(int customerId, …)` must be null-checked first. Source: `models/records-2-Cr-Ne.md`.

### 2.3 `CreateSubscription` (inner model of `CreateSubscriptionRequest`) — namespace `MaxioAdvancedBilling.Models`

The record marks **nothing** as `required`, so `required?` selects nothing for you. These are the members
the operation's own Notes tie to whether the call is accepted:

| Member (wire name) | Type | Use here |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` | **Set this** — the Notes: "Specify the product with `product_id` or `product_handle`". This is the handle-driven path the brief requires (`eshop-pro`, `basic-plan`) |
| `ProductId (product_id)` | `int?` | leave null (numeric ids are not stable in this sandbox) |
| `CustomerId (customer_id)` | `int?` | **Set this** — the Notes: "Identify an existing customer with `customer_id` or `customer_reference`". Use the id from step 4 |
| `CustomerReference (customer_reference)` | `string?` | alternative to `CustomerId`; set **one**, not both |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | leave null — used only to create a new customer inline instead of attaching an existing one |
| `ProductPricePointHandle (product_price_point_handle)` / `ProductPricePointId (product_price_point_id)` | `string?` / `int?` | leave null → the product's default price point is used (Notes: "To set a specific price point, use …") |
| `Reference (reference)` | `string?` | optional; set it to a deterministic per-(user, plan) key if you want `FindSubscription(reference)` to work later — see §2.7 |
| `PaymentProfileId (payment_profile_id)`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` | — | **left null** — the Notes say payment information "may be required … depending on the options for the Product being subscribed"; the brief states these plans do not require a payment method |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | left null → the site default applies. Values if you ever set it: see §2.5 |
| `CouponCode`/`CouponCodes`, `Components`, `CalendarBilling`, `OfferId` (union), `NextBillingAt`, `InitialBillingAt`, `PreviousBillingAt`, `ExpiresAt`, `Currency`, `Metafields`, `Group`, `NetTerms`, `ReceivesInvoiceEmails`, `DeferSignup (bool? = false)`, `SkipBillingManifestTaxes`, `ImportMrr`, `ActivatedAt`, `CanceledAt`, `AgreementAcceptance`, `AchAgreement`, `PrepaidConfiguration`, `SalesRepId`, `StoredCredentialTransactionId`, `ProductChangeDelayed`, `CustomPrice`, `Ref`, `CancellationMessage`, `CancellationMethod`, `ReasonCode`, `DunningCommunicationDelayEnabled (bool? = false)`, `DunningCommunicationDelayTimeZone`, `ExpirationTracksNextBillingChange`, `AgreementTerms`, `AuthorizerFirstName`, `AuthorizerLastName`, `CalendarBillingFirstCharge` | all `?` | **deliberately omitted.** None is named by the Notes as a condition of acceptance for a no-trial, no-setup-fee, no-payment-method plan |

Minimal accepted body shape (all four names are literal):

```csharp
new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
{
    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
    {
        ProductHandle = planHandle,   // "eshop-pro" | "basic-plan"
        CustomerId    = maxioCustomerId,
        Reference     = subscriptionReference, // optional; see §2.7
    }
};
```

Source: `models/records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`), `operations/Subscriptions.md` (`CreateSubscription` Notes).

### 2.4 Read models — exact member names and types

`MaxioAdvancedBilling.Models.Product` (source: `models/records-3-Of-Su.md`) — members this integration displays:

| Member (wire name) | Type | Note |
|---|---|---|
| `Handle (handle)` | `string?` | the plan key the API contract is built on |
| `Name (name)` | `string?` | |
| `Description (description)` | `string?` | |
| `PriceInCents (price_in_cents)` | `long?` | **`long?`, not `int?`/`decimal?`** — divide by 100m for display |
| `Interval (interval)` | `int?` | e.g. 1 |
| `IntervalUnit (interval_unit)` | `MaxioAdvancedBilling.Models.Enums.IntervalUnit?` | see §2.5 |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | non-null ⇒ archived; filter these out of the sellable list |
| `RequireCreditCard (require_credit_card)` | `bool?` | **yes, this member exists**; there is also a separate `RequestCreditCard (request_credit_card): bool?` |
| `ProductFamily (product_family)` | `MaxioAdvancedBilling.Models.ProductFamily?` | nested; `ProductFamily.Handle/.Id/.Name` |
| `Id (id)`, `TrialPriceInCents (long?)`, `TrialInterval (int?)`, `TrialIntervalUnit (IntervalUnit?)`, `InitialChargeInCents (long?)`, `Taxable (bool?)`, `ExpirationInterval (int?)`, `ExpirationIntervalUnit (ExpirationIntervalUnit?)`, `DefaultProductPricePointId (int?)`, `ProductPricePointHandle (string?)`, `CreatedAt/UpdatedAt (DateTimeOffset?)` | | available if needed |

**There is no `currency` member on `Product`.** Currency for the plans endpoint must come from
`client.Sites.ReadSite(ct)` → `SiteResponse.Site.Currency (currency): string?`, or from your own
configuration. (`models/records-3-Of-Su.md` — `Product` field list; `Site` field list.)

`MaxioAdvancedBilling.Models.Subscription` (source: `models/records-3-Of-Su.md`) — members this integration reads:

| Member (wire name) | Type |
|---|---|
| `Id (id)` | `int?` |
| `State (state)` | `MaxioAdvancedBilling.Models.Enums.SubscriptionState?` — see §2.5 |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` |
| `Product (product)` | `MaxioAdvancedBilling.Models.Product?` — nested; `Product?.Name`, `Product?.Handle` give the plan name/handle without a second call |
| `ProductPriceInCents (product_price_in_cents)` | `long?` |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` |
| `BalanceInCents (balance_in_cents)` | `long?` |
| `TotalRevenueInCents (total_revenue_in_cents)` | `long?` |
| `CreditBalanceInCents` / `PrepaymentBalanceInCents` | `long?` |
| `Currency (currency)` | `string?` |
| `CreatedAt (created_at)` / `UpdatedAt (updated_at)` | `DateTimeOffset?` |
| `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `DelayedCancelAt`, `ScheduledCancellationAt`, `OnHoldAt`, `AutomaticallyResumeAt` | `DateTimeOffset?` |
| `Reference (reference)` | `string?` |
| `Customer (customer)` | `MaxioAdvancedBilling.Models.Customer?` |
| `PreviousState (previous_state)` | `SubscriptionState?` |
| `CancellationMethod (cancellation_method)` | `MaxioAdvancedBilling.Models.Enums.CancellationMethod?` |
| `PaymentCollectionMethod (payment_collection_method)` | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` |

**Every date member on `Subscription` is `DateTimeOffset?` — never `DateTime?`.**

**Which field is the "next billing date":** use **`CurrentPeriodEndsAt`** (`current_period_ends_at`,
`DateTimeOffset?`). Map-visible evidence: the `UpdateSubscription` Notes state that when you send
`next_billing_at`, "The server response will not return data under the key/value pair of `next_billing_at`.
View the key/value pair of `current_period_ends_at` to verify that the `next_billing_at` date has been
changed successfully" (`operations/Subscriptions.md`). `NextAssessmentAt` (`next_assessment_at`,
`DateTimeOffset?`) is the assessment timestamp and is also present on the model; whether the two hold the
same instant for a given live subscription is **UNVERIFIED** (only live traffic can confirm). Defensive
directive: project `CurrentPeriodEndsAt ?? NextAssessmentAt` into your response's next-billing-date field
and allow it to be null — both are nullable and a canceled/expired subscription may carry neither.

### 2.5 Enum value tables (namespace `MaxioAdvancedBilling.Models.Enums`)

These are **not** C# enums. They are `StringEnum<T>` records deriving from
`MaxioAdvancedBilling.Core.Enum.StringEnum<T>` / `TypedEnum<TValue,TEnum>`: compare with the static members
(record equality), read the wire text with `.Value` (`string`), and note `ToString()` returns that same
wire value. `FromValue("wire")` accepts unknown values, and `IsKnownValue()` tells you whether the value is
one of the generated constants. (Sources: `models/enums.md`; `Core/Enum/StringEnum.cs`, `Core/Enum/TypedEnum.cs`.)

`IntervalUnit` — **only two members**: `IntervalUnit.Day (day)` · `IntervalUnit.Month (month)`.
(`models/enums.md`.) There is no week/year member, so a UI that switches on interval units must have a
fallback branch for an unknown wire value.

`SubscriptionState` — `Pending (pending)` · `FailedToCreate (failed_to_create)` · `Trialing (trialing)` ·
`Assessing (assessing)` · `Active (active)` · `SoftFailure (soft_failure)` · `PastDue (past_due)` ·
`Suspended (suspended)` · `Canceled (canceled)` · `Expired (expired)` · `Paused (paused)` · `Unpaid (unpaid)` ·
`TrialEnded (trial_ended)` · `OnHold (on_hold)` · `AwaitingSignup (awaiting_signup)`. (`models/enums.md`.)
The enum's own doc classifies `active`/`assessing`/`pending`/`trialing`/`paused` as *live*,
`past_due`/`soft_failure`/`unpaid` as *problem*, and `canceled`/`expired`/`failed_to_create`/`on_hold`/
`suspended`/`trial_ended` as *end-of-life*; it explicitly warns not to base access decisions on
`assessing` or `pending` because they may not always be exposed.

`ExpirationIntervalUnit` — `Day (day)` · `Month (month)` · `Never (never)`.

`CollectionMethod` — `Automatic (automatic)` · `Remittance (remittance)` · `Prepaid (prepaid)` · `Invoice (invoice)`.

`BasicDateField` — `UpdatedAt (updated_at)` · `CreatedAt (created_at)` (only needed if you start passing `dateField`).
`ListProductsInclude` — `PrepaidProductPricePoint (prepaid_product_price_point)` (single member).
`SortingDirection` — `Asc (asc)` · `Desc (desc)`.

### 2.6 Error handling — types, statuses, bodies

| Fact | Detail | Source |
|---|---|---|
| Base exception | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` — `public sealed class SdkException<TError> : Exception`, whose **only** public member is `Error` (type `TError`) | `Core/Exceptions/SdkException.cs` |
| There is **no** non-generic `SdkException` base | Each closed generic (`SdkException<CreateSubscriptionError>`, `SdkException<RawError>`, …) is an unrelated type as far as `catch` is concerned; they share only `System.Exception` | `Core/Exceptions/SdkException.cs` |
| The exception carries **no status code and no useful message** | `SdkException` sets no `Message`, so `ex.Message` is the framework's default "Exception of type … was thrown." text and `ex.ToString()` contains no API detail. Status lives **only** on `RawError.StatusCode` | `Core/Exceptions/SdkException.cs` |
| Reading a status | Case B: `ex.Error.StatusCode` (`System.Net.HttpStatusCode`). Case A: `if (ex.Error.TryGetRawError(out var raw)) status = raw.StatusCode;` — **and that accessor returns `false` for any status the typed error models** (see rows below) | `sdk-map.md`, `Core/ErrorResponse/ApiError.cs` |
| Reading a body | `RawError`: `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`, `StatusCode` | `sdk-map.md` |
| Every operation is **throw-only** | No `…Result` / no-throw variants exist anywhere in this SDK; each of the 9 calls above must be wrapped | `sdk-map.md` |
| 404 on customer-lookup-by-reference | `SdkException<RawError>` with `ex.Error.StatusCode == HttpStatusCode.NotFound` — this is the exception you catch to mean "no Maxio customer yet" | `operations/Customers.md`; `Api/Customers.cs` |
| `CreateSubscription` 422 | `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)`; `ErrorListResponse1.Errors` is `IReadOnlyList<string>` (`required`) — that is the messages array. `TryGetRawError` is **false** for 422 | `operations/Subscriptions.md`; `Errors/CreateSubscriptionError.cs` |
| `CreateCustomer` 422 — **trust warning** | `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)`. The payload `CustomerErrorResponse1` has exactly one member `Errors (errors): MaxioAdvancedBilling.Models.Errors?`, and the generated `Errors` record declares **only** `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` — a paging/price-point shape that cannot carry a customer validation message. On 422 `TryGetRawError` returns **false** (the raw slot is only filled for non-422 statuses), so **the SDK gives you no way to read the 422 text for this operation.** Two generated definitions that disagree — a customer-error accessor whose payload is a shared paging-error model — is map-visible evidence, not a guess | `models/records-2-Cr-Ne.md` (`CustomerErrorResponse1`, `Errors`); `Errors/CreateCustomerError.cs` |
| `ListProductsForProductFamily` 404 | `TryGetString(out string)`; the SDK parses the 404 body **as a JSON string** — a JSON-object body throws `JsonException` in place of the `SdkException` | `Errors/ListProductsForProductFamilyError.cs` |
| `FindSubscription` 404 | Use `TryGetNoContent(out RawError)`; `TryGetRawError` is **false** for 404 | `Errors/FindSubscriptionError.cs` |
| Typed errors are built by deserializing the error body | `ApiError` subclasses call `FromJson<TPayload>` inside `Create(...)`, i.e. **while the exception's `Error` object is being constructed** | `Core/ErrorResponse/ApiError.cs`, `Errors/*.cs` |

Defensive-coding directive for the `CreateCustomer` 422 (`UNVERIFIED` — only live traffic can confirm what
the sandbox actually puts in that body): treat a 422 from `CreateCustomer` as "this reference already
exists / the payload was rejected", **re-run `ReadCustomerByReference` and use the customer it returns**;
extract a message best-effort from `TryGetCustomerErrorResponse1`'s payload and fall back to a generic
message when it is empty. Do not build user-facing text out of that accessor's contents.

### 2.7 Idempotency — what the SDK actually gives you

| Lever | Fact | Verdict |
|---|---|---|
| Customer | `CreateCustomer` Notes: "you may only create one customer for a given reference value. If provided, the `reference` value must be unique." So set `CreateCustomer.Reference` to the app's stable user key; lookup-then-create, and on 422 re-look-up | map-backed (`operations/Customers.md`) |
| Subscription — primary guard | `ListCustomerSubscriptions(customerId, ct)` returns every subscription for that customer, each with `Subscription?.Product?.Handle` and `Subscription?.State`. Check for an existing subscription to the same handle in a live state **before** calling `CreateSubscription`, and return that one instead of creating a second | map-backed (`operations/Customers.md`, `models/records-3-Of-Su.md`) |
| Subscription — secondary lever | `CreateSubscription.Reference` + `FindSubscription(reference)` gives a direct lookup. Whether Maxio **enforces** uniqueness of a subscription `reference` is stated nowhere in the map (unlike the customer reference, where the Notes say it outright) | **UNVERIFIED** — do not rely on it as the only guard; use it as a fast path and keep the list-based check |
| Whether two truly simultaneous POSTs can both pass the guard | Nothing in the SDK serializes them | **YOUR CALL — not in the map**: the request-deduplication/locking rule for `POST /api/subscriptions` is an application decision |

### 2.8 Client construction, auth, server node, DI

**Package.** Package id `AsadAli.AdvancedBilling.Sdk` (root namespace for `using` is `MaxioAdvancedBilling` —
they differ). Install with `dotnet add package AsadAli.AdvancedBilling.Sdk`; do **not** add a project
reference to the SDK sources. Transitive runtime deps: `Polly`, `Microsoft.Extensions.Http`,
`System.Net.Http.Json`, `System.Net.ServerSentEvents`. Target framework `netstandard2.0` — consumable from
.NET 8. **Version:** the map is stamped at tag `v1.0.2` (commit `15db14b`), while the `.csproj` at that tag
declares `<Version>1.0.0</Version>`, so the exact published NuGet version string cannot be settled from the
map or the source — `UNVERIFIED`. Directive: restore once, then pin the resolved version explicitly in the
`.csproj` rather than leaving it floating. (`sdk-map.md`; `MaxioAdvancedBilling.csproj`.)

**Namespaces to import** (each is a separate `using` — C# does not import child namespaces transitively):

| Types you touch | Namespace |
|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` | `MaxioAdvancedBilling` |
| Controller types `Customers`, `Products`, `ProductFamilies`, `Subscriptions`, `Sites` | `MaxioAdvancedBilling.Api` |
| Records (`Product`, `Customer`, `Subscription`, `CreateCustomerRequest`, `CreateSubscriptionRequest`, `ProductResponse`, `SubscriptionResponse`, `CustomerResponse`, `SiteResponse`, `ErrorListResponse1`, `CustomerErrorResponse1`, `Errors`) | `MaxioAdvancedBilling.Models` |
| Enums (`IntervalUnit`, `SubscriptionState`, `CollectionMethod`, `BasicDateField`, `ListProductsInclude`, `SortingDirection`) | `MaxioAdvancedBilling.Models.Enums` |
| `StringEnum<T>` / `TypedEnum<,>` base | `MaxioAdvancedBilling.Core.Enum` |
| Typed error classes (`CreateSubscriptionError`, `CreateCustomerError`, `FindSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment`, `ProductionOptions` (+ nested `ProductionOptions.UsOptions` / `.EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` |
| `RetryOptions`, `RetryAttempt` | `MaxioAdvancedBilling.Core.Configuration` |

⚠ Collision hazard: the controller types are named `Customers`, `Products`, `Subscriptions`, `Sites`, and
`MaxioAdvancedBilling.Models` declares plain names such as `Customer`, `Product`, `Subscription`, `Site`,
`Address`, `Errors`. If a file that imports these also imports an application namespace declaring the same
simple name, the build fails with **CS0104 (ambiguous reference)**. Prefer reaching controllers through
`client.Products` (no `using MaxioAdvancedBilling.Api;` needed unless you name the type), and alias the
model types you map from, e.g. `using MaxioProduct = MaxioAdvancedBilling.Models.Product;`.

**Options object** — `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`, a mutable class with
settable properties and defaults (source: `MaxioAdvancedBillingClientOptions.cs`):

| Property | Type | Default |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` — the map documents `ServerEnvironment.Us` as the default; the two members are `ServerEnvironment.Us` (`US`) and `ServerEnvironment.Eu` (`EU`) |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` (all members are `required` — start from `Default()` if you customise) |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new()` — already instantiated, so `options.Server.Production.Us.…` is safe to assign without constructing anything |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` |

**Auth.** `BasicAuthCredentials` is `sealed` with two `required`, `init`-only `string` members — exactly
`Username` and `Password`. Maxio convention: **`Username` = the API key, `Password` = the literal `"x"`**
(the SDK's own doc-comment on the property says so). Because the members are `init`-only and the options are
captured once at client construction, **rotating the API key requires building a new client** — you cannot
mutate the credentials in place. (`sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs`.)

**Server node / base URL.** `ServerOptions` (namespace `MaxioAdvancedBilling`) has
`Production: ProductionOptions` and `Ebb: EbbOptions`, both default-constructed.
`ProductionOptions` (namespace `MaxioAdvancedBilling.Servers`) has `Us: ProductionOptions.UsOptions` and
`Eu: ProductionOptions.EuOptions`, both default-constructed; each nested class has two settable `string`
members:

| Member | Default (Us) | Default (Eu) |
|---|---|---|
| `BaseUrl` | `"https://{site}.chargify.com"` | `"https://{site}.ebilling.maxio.com"` |
| `Site` | `"subdomain"` | `"subdomain"` |

The URL is built by a literal `String.Replace("{site}", value)` over `BaseUrl`, then
`baseUrl.TrimEnd('/') + "/" + path.TrimStart('/')` (`Core/TemplateParamsFactory.cs`,
`Servers/ProductionOptions.cs`). Three consequences, all source-verified:

1. **Subdomain-derived case** (`Maxio:BaseUrl` not set): set `options.Server.Production.Us.Site = <Maxio:Subdomain>`
   (e.g. `cp-exp-2`) and leave `BaseUrl` alone → `https://cp-exp-2.chargify.com`. **If you never set `Site`,
   there is no error at construction — every call silently goes to the literal host
   `https://subdomain.chargify.com`.** Validate the configured subdomain is non-empty yourself.
2. **Explicit override case** (`Maxio:BaseUrl` set): assign
   `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`. A value containing no `{site}` placeholder is
   used **verbatim** (the `Replace` is a no-op; one trailing `/` is trimmed). **An explicit base-URL override
   is supported — this is not a blocker.** Set it on the leg matching `Environment` (`.Us` for
   `ServerEnvironment.Us`); assigning the `.Us` leg while `Environment` is `Eu` has no effect.
3. All operations in this plan are on the **Production** server group; `options.Server.Ebb` is irrelevant here.

Because both cases mutate the same already-instantiated object graph, one registration can handle both:
set `Site` always, and additionally overwrite `BaseUrl` when the optional setting is present.

**Configuration keys** (binding keys, as given in the brief — never read a raw environment variable name
into the code): `Maxio:ApiKey` → `BasicAuthCredentials.Username`; `Maxio:Subdomain` →
`options.Server.Production.Us.Site`; `Maxio:BaseUrl` (optional) → `options.Server.Production.Us.BaseUrl`;
`Maxio:ProductFamilyHandle` → the handle passed to step 2 (`eshop-subscribe`). The SDK documents no default
for any of these beyond the `BaseUrl`/`Site` defaults in the table above; `Maxio:ApiKey` has no default and
must be supplied by every deployment. Where these are bound from (user-secrets, appsettings, key vault) is
**YOUR CALL — not in the map**.

**Construction and DI** (source: `MaxioAdvancedBillingClient.cs`, `ServiceCollectionExtensions.cs`):

- The **only** constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
  The class is `sealed`; every API group is a get-only property (`client.Customers`, `client.Products`,
  `client.ProductFamilies`, `client.Subscriptions`, `client.Sites`), all constructed in the constructor.
- **Thread-safety / lifetime:** the client and its controllers hold only readonly collaborators and keep no
  mutable per-call state, so a single instance is safe to use concurrently across requests. Its shared
  mutable dependency is the injected `HttpClient`.
- `services.AddMaxioAdvancedBillingClient(o => { … })` registers `MaxioAdvancedBillingClient` as a
  **singleton**, calls `services.AddHttpClient()`, and at first resolution creates one default client from
  `IHttpClientFactory` and holds it for the process lifetime. **The configure delegate runs once, eagerly, at
  registration time** — options are captured then, so configuration must be readable at registration and
  nothing per-request/per-scope can influence them.
- The extension is declared inside a C# 14 `extension(IServiceCollection services)` block. If it does not
  bind from the application's project (older `LangVersion`), register manually against the public
  constructor instead — the constructor above is public and is the same thing the extension calls.

---

## 3. Trap notes

- ⚠ **Step 1 (client registration)** — `AddMaxioAdvancedBillingClient` registers a **singleton** client that
  captures one `IHttpClientFactory`-created `HttpClient` forever, and its options delegate runs once at
  registration. What that costs you in handler lifetime/DNS behaviour, and what the correct ownership shape
  is for an ASP.NET Core app, is not visible in that signature. **MUST load `dotnet-client-initialization`**
  before wiring the client.
- ⚠ **Step 1 (resilience)** — `options.Retry` (`RetryOptions`, all members `required`) and its `Timeout`
  member do not mean what their names suggest: whether a failed **`POST /subscriptions`** can be re-sent (and
  therefore whether a double subscription can be created by the transport layer, not by the user), and what
  `Timeout` actually bounds, decide whether your idempotency guard in §2.7 is sufficient. Do not choose retry
  settings from the names alone. **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ **Step 1/2 (auth + base URL)** — credentials are `init`-only and captured at construction, and the base
  URL is a template expanded once per request; when and where to set credentials, and how to keep the key out
  of source, is the skill's subject. **MUST load `dotnet-authentication`** before setting `BasicAuth`.
- ⚠ **Steps 2–7 (every call)** — most list operations have long runs of nullable parameters with **no C#
  default**, so a positional call mis-binds silently (e.g. `ListProducts` orders `endDate` before `startDate`
  while `ListProductsForProductFamily` does the opposite). **MUST load `dotnet-calling-endpoints`** before
  writing the first call.
- ⚠ **Steps 3–7 (models)** — `IntervalUnit`/`SubscriptionState` are `StringEnum<T>` records, not C# enums, and
  response envelopes differ in nullability (`ProductResponse.Product` is `required`; `SubscriptionResponse.Subscription`
  and `ProductFamilyResponse.ProductFamily` are nullable). What happens to JSON fields the model does not
  declare, and how required members interact with deserialization, changes what your mapping code can assume.
  **MUST load `dotnet-models`** before constructing request payloads or mapping to your own DTOs.
- ⚠ **Steps 4–6 (error boundary)** — the four operations in the hero flow span both error cases, and on
  several statuses the accessor you would reach for first returns `false` (see §2.6). Which exception types
  actually reach your catch blocks, and how to read a status without parsing exception text, is the skill's
  subject. **MUST load `dotnet-error-handling`** before writing any `try`/`catch`.
- ⚠ **Tests** — the `HttpClient` constructor argument is the seam; how to fake it without coupling tests to
  SDK internals is not inferable from the signature. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **all** of these **before implementation starts**. This sheet deliberately does not carry their
contents — it names the contract, not the usage rules.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `MaxioAdvancedBillingClient`, `HttpClient` ownership and lifetime |
| `dotnet-authentication` | Step 1 — supplying `BasicAuthCredentials` from `Maxio:ApiKey`, key rotation |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, timeouts, base-URL/server selection, pagination loops |
| `dotnet-calling-endpoints` | Steps 2–7 — every `client.X.Operation(...)` call, named arguments, cancellation |
| `dotnet-models` | Steps 3–7 — building `CreateCustomerRequest`/`CreateSubscriptionRequest`, reading enums and envelopes |
| `dotnet-error-handling` | Steps 4–7 — the exception boundary for all three endpoints |
| `dotnet-testing` | Tests for the integration layer |

**Two hazard rows that must shape the error boundary from the start** —
`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Concrete instances of both in this integration, verified in the SDK source: `ProductResponse.Product`,
`CustomerResponse.Customer`, `SiteResponse.Site` and `ErrorListResponse1.Errors` are `required` members
(direction 1); `CreateCustomerError` parses a 422 into `CustomerErrorResponse1` and
`ListProductsForProductFamilyError` parses a 404 **as a bare JSON string** (direction 2).

---

## 5. Assumptions & Blockers

**Assumptions (about intent — correct me if wrong):**

1. `Maxio:Subdomain` will be the sandbox site `cp-exp-2`, and `Maxio:ProductFamilyHandle` will be
   `eshop-subscribe`; the plan resolves that handle to a numeric family id at runtime via
   `ListProductFamilies` rather than hard-coding an id.
2. The environment stays `ServerEnvironment.Us` (US hosting). If the sandbox is EU-hosted, the
   `Site`/`BaseUrl` assignments must move to `options.Server.Production.Eu.*` — assigning the `Us` leg would
   then have no effect.
3. `GET /api/subscription-plans` lists only non-archived products of that one family (`ArchivedAt == null`,
   `includeArchived: false`). Display currency comes from `Sites.ReadSite` or from configuration, because
   `Product` carries no currency member (§2.4).
4. The stable eShopOnWeb user key used as `CreateCustomer.Reference` — and the first/last name/email values
   fed into the three `required` members — come from the application's own identity path. Which values those
   are is **YOUR CALL — not in the map**; the SDK will not compile a `CreateCustomer` without all three.
5. No payment profile, card capture or 3-DS handling is implemented, per the brief's statement that these
   plans do not require a payment method. The `CreateSubscription` Notes still describe a 422 + `action_link`
   3-DS flow for products that *do* require payment, so the 422 branch is written but not exercised.
6. The exact NuGet version string to pin is resolved at first restore (§2.8) — the source at the mapped tag
   declares `1.0.0` while the tag is `v1.0.2`.

**Unverified (labelled in place, cannot be settled from map or source — only live traffic can):**

- Whether `current_period_ends_at` and `next_assessment_at` hold the same instant for an active
  subscription (§2.4) — handled by the `??` fallback directive.
- Whether Maxio enforces uniqueness on a *subscription* `reference` (§2.7) — handled by keeping the
  `ListCustomerSubscriptions` guard as primary.
- What the sandbox actually returns in a `CreateCustomer` 422 body (§2.6) — handled by the re-lookup
  directive plus a generic fallback message.
- Whether `ListProductsForProductFamily` accepts the `"handle:eshop-subscribe"` form in its `string
  productFamilyId` parameter. The map documents that format for the `{product_family_id}` path segment in
  `ReadProductFamily`'s Notes, but `ReadProductFamily` itself takes an `int` and so cannot use it. Directive:
  implement the deterministic `ListProductFamilies` → match `Handle` → `Id` path (fully map-backed); treat the
  `handle:` prefix only as an optional optimization proven against the sandbox first.

**Blockers:**

1. **Signup collection — RESOLVED BY §6, verify live.** The sandbox rejects the §2.3 minimal body with
   `422 ["No payment method was on file for the $299.00 balance"]`: `RequireCreditCard = false` stops the
   *product* from demanding a card, but the site still attempts **automatic** collection of the signup
   balance. §6 gives the documented lever (`CreateSubscription.PaymentCollectionMethod`) and how to choose
   its value from the site's own architecture flag. Until a live call succeeds, the fix itself is
   `UNVERIFIED`.

The explicit base-URL override the brief asked about **is** supported by the generated client (§2.8,
item 2) — it is a plain settable `string` on `options.Server.Production.Us.BaseUrl`, used verbatim when it
contains no `{site}` placeholder. That was never a blocker.

---

## 6. Revision — creating a subscription that carries a balance with NO payment profile

Triggered by the live `422 ["No payment method was on file for the $299.00 balance"]` from
`CreateSubscription` with `{ ProductHandle = "eshop-pro", CustomerId = … }` on site `cp-exp-2`.

### 6.1 The documented lever

`CreateSubscription.PaymentCollectionMethod` is the **only** member on the request model that the SDK
documents as governing how payment is collected. Its verbatim doc (identical on the field and on the enum
type itself):

> The type of payment collection to be used in the subscription. For legacy Statements Architecture valid
> options are — `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are —
> `remittance`, `automatic`, `prepaid`.

Source: `models/records-2-Cr-Ne.md` (`CreateSubscription.PaymentCollectionMethod (payment_collection_method): CollectionMethod?`),
`models/enums.md` (`CollectionMethod` summary); field doc-comment in `Models/CreateSubscription.cs`,
type doc-comment in `Models/Enums/CollectionMethod.cs`.

**What distinguishes `Invoice` from `Remittance`: site architecture, not behaviour.** The doc splits the four
members into two per-architecture sets — `invoice`/`automatic` for legacy Statements, and
`remittance`/`automatic`/`prepaid` for current Relationship Invoicing. `invoice` and `remittance` are the
legacy and current names for the non-automatic, bill-the-customer mode; the SDK documents no behavioural
difference between them and never lists them as valid on the same site. `automatic` is the mode that
attempts collection (the mode the live 422 above is coming from), and `prepaid` draws down a prepayment
balance (`Subscription.PrepaymentBalanceInCents`).

**Which one applies to `cp-exp-2` is readable at runtime, not guessable:** `client.Sites.ReadSite(ct)` →
`SiteResponse.Site` carries `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` and
`DefaultPaymentCollectionMethod (default_payment_collection_method): string?`. `true` ⇒ send
`CollectionMethod.Remittance`; `false` ⇒ send `CollectionMethod.Invoice`. The `DefaultPaymentCollectionMethod`
string is also what the site is applying today when you send nothing (`models/records-3-Of-Su.md`, `Site`).

**Is remittance documented as "no payment attempted"?** The closest map-visible statement is in
`operations/Invoices.md` (`IssueInvoice` Notes): *"For Remittance subscriptions, the invoice will go into
'open' status and payment won't be attempted."* That is the SDK's own prose linking the remittance
collection method to no collection attempt. Note honestly: the `CreateSubscription` Notes themselves do
**not** name a collection method — they only say payment information "may be required to create a
subscription, depending on the options for the Product being subscribed". So `PaymentCollectionMethod` is the
documented lever, and remittance/invoice are the documented non-automatic modes, but that this specific 422
disappears on this specific site is `UNVERIFIED` until the live call succeeds.

Optional pre-flight that costs nothing: `client.Subscriptions.PreviewSubscription(body, ct)` takes the
**same** `CreateSubscriptionRequest`, returns `SubscriptionPreviewResponse`, is **Case B**
(`SdkException<RawError>`), and its Notes state "A subscription will not be created by utilizing this
endpoint; it is meant to serve as a prediction" — use it to inspect the signup balance without creating
anything (`operations/Subscriptions.md`).

### 6.2 The other members — what the Notes actually claim, and whether they are the answer

| Member | What the SDK doc says, verbatim in substance | Verdict |
|---|---|---|
| `NextBillingAt (next_billing_at): DateTimeOffset?` | "Set this attribute to a future date/time to sync **imported** subscriptions to your existing renewal schedule… If you provide a `next_billing_at` timestamp that is in the future, no trial or initial charges will be applied when you create the subscription. **In fact, no payment will be captured at all.** … If you do not provide a value…, any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created." | **Side effect, not the answer.** It suppresses signup collection only as part of the subscription-*import* feature, and it silently moves the customer's first payment. Do not use it to dodge a collection error on a normal signup |
| `InitialBillingAt (initial_billing_at): DateTimeOffset?` | "Set this attribute to a future date/time to create a subscription in the **Awaiting Signup** state, rather than Active or Trialing… When the `initial_billing_at` date hits, the subscription will transition to the expected state… **If the payment is due at the `initial_billing_at` and it fails the subscription will be immediately canceled.**" | **Defers the failure, does not remove it.** Not the answer |
| `DeferSignup (defer_signup): bool? = false` | "Set this attribute to true to create the subscription in the **Awaiting Signup Date** state. Use this when you want to create a subscription that has an unknown first billing date. When the first billing date is known, update a subscription and set the `initial_billing_at` date." | **Different use case** (unknown first billing date), not a collection setting. Not the answer |
| `CalendarBilling (calendar_billing): CalendarBilling?` | "(Optional). **Cannot be used when also specifying `next_billing_at`**" | Unrelated to collection. Not the answer; note the mutual exclusion if you ever set it |
| `NetTerms (net_terms): string?` | "(Optional) Default: null The number of days after renewal (**on invoice billing**) that a subscription is due. A value between 0 (due immediately) and 180." | **Companion setting, still optional.** It only has meaning once you are on invoice/remittance billing; it is not required and does not by itself change collection. Note the C# type is `string?`, not `int?` |
| `ImportMrr (import_mrr): bool?` | "Setting this attribute to true will cause the subscription's MRR to be added to your MRR analytics immediately. **For this value to be honored, a `next_billing_at` must be present and set to a future date.** This key/value will not be returned in the subscription response body." | Analytics only, and dependent on the import path above. Not the answer |

Sources: `models/records-2-Cr-Ne.md` for names/types/wire names; the per-field doc-comments in
`Models/CreateSubscription.cs` for the claims quoted above (the map carries field lists, not per-field prose).

### 6.3 Exact names to write

```csharp
using MaxioAdvancedBilling.Models;        // CreateSubscription, CreateSubscriptionRequest
using MaxioAdvancedBilling.Models.Enums;  // CollectionMethod

new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
{
    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
    {
        ProductHandle           = "eshop-pro",
        CustomerId              = maxioCustomerId,
        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
        // ...Invoice instead, when Site.RelationshipInvoicingEnabled is false
    }
};
```

- Property: `PaymentCollectionMethod` (wire `payment_collection_method`), declared type **`CollectionMethod?`** — confirmed.
- Enum type: `MaxioAdvancedBilling.Models.Enums.CollectionMethod` — a `sealed record CollectionMethod : StringEnum<CollectionMethod>`, **not** a C# enum. Assign the static member directly (`CollectionMethod.Remittance`); the constructor is private, so `new CollectionMethod("remittance")` will not compile. `CollectionMethod.FromValue("remittance")` exists if you must build it from a configured string, and `.Value` reads the wire text back.
- Members, literal C# identifier + wire value: `CollectionMethod.Automatic ("automatic")` · `CollectionMethod.Remittance ("remittance")` · `CollectionMethod.Prepaid ("prepaid")` · `CollectionMethod.Invoice ("invoice")` (`models/enums.md`).
- The same enum type is on the read model: `Subscription.PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — assert on it in the response to confirm the site honoured the request.

### 6.4 Effect on error handling and on required members

- **No change to the error surface.** `CreateSubscription` remains **Case A**:
  `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` with exactly
  `TryGetErrorListResponse1(out ErrorListResponse1)` [422] and `TryGetRawError(out RawError)` [everything
  else]. The accessor set is fixed by the generated type's `Create(...)` status switch (422 → typed, `_` →
  raw) and does not vary with the request body — so the 422 you are reading today is read exactly the same
  way afterwards, and `TryGetRawError` still returns **false** on a 422 (`operations/Subscriptions.md`;
  `Errors/CreateSubscriptionError.cs`).
- **No member becomes required.** `CreateSubscription` declares no C# `required` members at all; setting
  `PaymentCollectionMethod` does not oblige you to send `NetTerms` (optional, default null) or anything else.
  `CreateSubscriptionRequest.Subscription` remains the only `required` member in the payload
  (`models/records-2-Cr-Ne.md`).

### 6.5 Resulting state and next-billing-date — what is documented, what is not

- The SDK documents **no** landing state for a remittance/invoice subscription. The only state claims in the
  Notes are conditional on the *other* members: `initial_billing_at` or `defer_signup` create the
  subscription in **Awaiting Signup** (`SubscriptionState.AwaitingSignup`, wire `awaiting_signup`) "rather
  than Active or Trialing"; with neither set, the `next_billing_at` doc says trial/initial charges are
  "assessed and charged at the time of subscription creation". Since §6.1 changes only the collection method
  and sets none of those members, **which state you land in is `UNVERIFIED`** — only the live response can
  confirm it.
- Defensive directive for the `GET /api/my-subscriptions` and `POST /api/subscriptions` responses (both of
  which report state): read `Subscription.State?.Value` (the wire string) and render it as-is with a
  fallback for null and for values outside the generated member list — `SubscriptionState` is a
  `StringEnum`, so an unknown wire value deserializes successfully rather than throwing, and
  `State.IsKnownValue()` tells you whether it is one of the 15 generated constants. Do **not** hard-code an
  expectation of `active`; the enum's own doc warns that `assessing` and `pending` "may not always be
  exposed" and that access decisions must not be based on them.
- **`CurrentPeriodEndsAt` may be null in this scenario** and nothing in the map promises otherwise: every
  date member on `Subscription` is `DateTimeOffset?`, and a subscription created in Awaiting Signup has, by
  the `initial_billing_at` doc's own description, not yet started a period. Keep the §2.4 directive —
  project `CurrentPeriodEndsAt ?? NextAssessmentAt`, allow null, and render "not scheduled" rather than
  defaulting to `DateTimeOffset.MinValue` or throwing.
- If you do end up creating Awaiting Signup subscriptions, the transition operation is
  `client.Subscriptions.ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)`
  — **Case A**, `SdkException<ActivateSubscriptionError>` with `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [**400**] ·
  `TryGetRawError` [fallback]; its Notes state it is "only available on the Relationship Invoicing
  architecture" (`operations/Subscriptions.md`). That is out of the hero flow's scope unless §6.1 lands you
  in `awaiting_signup`.
