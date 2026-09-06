# Maxio Integration Plan: eShopOnWeb Recurring Subscriptions

## Scope & sequence

1. **Client initialization & DI registration** — register `MaxioAdvancedBillingClient` in DI, configure auth (API key, password="x"), set base URL and environment (us-hosting by default, EU via `ServerEnvironment.Eu`).
2. **Fetch/ensure Maxio customer** — check if customer exists by reference (user ID or email) via `ReadCustomerByReference`; if 404, create via `CreateCustomer` with user's first/last name and email. Store Maxio customer ID in app.
3. **List subscription plans** — fetch available plans via `ReadProductByHandle` for each plan handle (eshop-pro, basic-plan) or `ListProducts` with product-family filter. Return to user with price and description.
4. **Create subscription** — on user selection, call `CreateSubscription` with customer ID/reference, product handle, and optional product-price-point-handle or custom pricing. Return subscription details (state, next-billing-date, plan info).
5. **Fetch user subscriptions** — for user's account page, call `ListCustomerSubscriptions` to show all active/past subscriptions.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal
C# identifier. The cancellation-token parameter really is named `ct`: in named
arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take
each one from that type's own map row, never from where a neighbouring type sits. A members
table names the namespace outright; otherwise the row's source path implies it
(`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
namespace). Enums, unions, auth, server and client-config types are spread across different
child namespaces, and two types configured side by side in the same options object routinely
live in different ones. Dropping a type to the root or to `.Models` makes the implementer
guess the wrong `using`, and the build breaks.

### 1. Ensure Maxio customer exists (idempotent lookup → create if missing)

| Operation | Controller | Signature | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| **ReadCustomerByReference** (GET) | `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param: `reference` (C# → wire: `reference` ← `reference`). Lookup by app's user ID or email. | `CustomerResponse` wraps single `Customer` field: `Id`, `FirstName`, `LastName`, `Email`, `Reference`, `CreatedAt`, `UpdatedAt`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Verified`, `TaxExempt`, `VatNumber`, etc. | **Case B**: `SdkException<RawError>` — 404 on not-found, 400+ on bad reference. Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | none | `operations/Customers.md` |
| **CreateCustomer** (POST) | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly. | `CreateCustomerRequest` wraps single `Customer` field containing `CreateCustomer` record. Required fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional: `Reference (reference): string?` (set to app's user ID for idempotency), `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`, `CcEmails`. | `CustomerResponse` wraps `Customer` field. Returned customer has SDK-assigned `Id` (store this). | **Case A**: `SdkException<CreateCustomerError>` — 422 on validation (duplicate reference, missing email). Accessor: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` → `Errors` dict; fallback `TryGetRawError(out RawError)`. | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |

### 2. List subscription plans by product family

| Operation | Controller | Signature | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| **ReadProductByHandle** (GET) | `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | Path param: `apiHandle` (e.g., "eshop-pro", "basic-plan"). Call once per plan. | `ProductResponse` wraps single `Product` field: `Id`, `Handle`, `Name`, `Description`, `PriceInCents` (monthly base price), `Interval` (1), `IntervalUnit` (Month), `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `TrialType`, `InitialChargeInCents`, `ProductFamily`, `PublicSignupPages`, `ProductPricePointName`, `ProductPricePointId`, `CreatedAt`, `UpdatedAt`, etc. | **Case B**: `SdkException<RawError>` — 404 if handle not found. Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | none | `operations/Products.md`, `records-3-Of-Su.md` |
| **ListProducts** (GET, alternative) | `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 optional params before pagination; all must be passed explicitly (pass `null` to skip). Defaults: `page`=1, `perPage`=20. | Query params (wire ← C#): `filter` ← `filter` (use to match product-family handle), others as named. Filter params: construct `ListProductsFilter` with `StartDate`, `EndDate`, etc. (see records page for full shape). | `IReadOnlyList<ProductResponse>` — list of products. Each wraps `Product` field as above. | **Case B**: `SdkException<RawError>`. | Manual `page`+`perPage`; defaults 1/20. | `operations/Products.md`, `records-3-Of-Su.md` |

### 3. Create subscription (enroll user in plan)

| Operation | Controller | Signature | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| **CreateSubscription** (POST) | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly. | `CreateSubscriptionRequest` wraps single `Subscription` field containing `CreateSubscription` record. **Identify customer**: either `CustomerId (customer_id): int?` (Maxio ID from step 1) OR `CustomerReference (customer_reference): string?` (app user ID, if set during customer create). **Identify product**: either `ProductHandle (product_handle): string?` (e.g., "eshop-pro") OR `ProductId (product_id): int?`. Optional: `ProductPricePointHandle`, `ProductPricePointId` (for price-point override), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum: `Automatic`, `Remittance`, `Prepaid`, `Invoice`; note wire names differ), `PaymentProfileId`, `CouponCode`, `CouponCodes`, `Reference` (internal ID for this subscription), `NextBillingAt (next_billing_at): DateTimeOffset?`, `CustomPrice` (advanced: custom pricing), `Components` (metered components, if any), `ReceivesInvoiceEmails`, `NetTerms`, `DeferSignup`, `StoredCredentialTransactionId`, `CalendarBilling`, `Metafields`, etc. | `SubscriptionResponse` wraps single `Subscription` field: `Id`, `State` (enum `SubscriptionState`: `Pending`, `Trialing`, `Active`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup`, etc.), `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt` (billing cycle end), `NextAssessmentAt` (next bill date), `ActivatedAt`, `CreatedAt`, `UpdatedAt`, `CanceledAt`, `Product` (nested: `Id`, `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`), `Customer` (nested customer object), `CreditCard` (payment profile if stored), `PaymentType`, `PaymentCollectionMethod`, `CouponCodes`, `CouponUseCount`, `CouponUsesAllowed`, `Coupons` (applied coupon details), `PrepaidConfiguration`, etc. | **Case A**: `SdkException<CreateSubscriptionError>` — 422 on validation (product not found, customer not found, payment required but missing, 3DS auth required). Accessor: `TryGetErrorListResponse1(out ErrorListResponse1)` → `Errors` list of strings; fallback `TryGetRawError(out RawError)`. Notes: if payment fails and 3DS is required, response includes `action_link` for post-auth flow (see provider docs). | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |

### 4. Fetch customer's subscriptions

| Operation | Controller | Signature | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| **ListCustomerSubscriptions** (GET) | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path param: `customerId` (Maxio customer ID from step 1). | `IReadOnlyList<SubscriptionResponse>` — list of all subscriptions for that customer (active, canceled, expired, etc.). Each wraps `Subscription` field as in step 3 (full Subscription model). | **Case B**: `SdkException<RawError>` — 404 if customer not found, 400+ on bad ID. | none | `operations/Customers.md`, `records-4-Su-We.md` |

---

## Enums & wire names

**All enums are `StringEnum<T>` / `IntEnum<T>` (NOT C# enums).** Construct via static members or `Type.FromValue(wireValue)`. Wire names are shown in parentheses; C# member names are what you write in code.

### CollectionMethod — payment collection type
| C# member | Wire value |
|---|---|
| `Automatic` | `automatic` |
| `Remittance` | `remittance` |
| `Prepaid` | `prepaid` |
| `Invoice` | `invoice` |

**Notes**: The provider default is `Automatic` for most subscriptions. Choose based on your billing model (e.g., `Automatic` for credit-card recurring, `Invoice` for net-terms).

### SubscriptionState — subscription lifecycle
| C# member | Wire value | Meaning |
|---|---|---|
| `Pending` | `pending` | Awaiting first charge (if `DeferSignup` was set). |
| `AwaitingSignup` | `awaiting_signup` | Created but activation deferred. |
| `Trialing` | `trialing` | In trial period (if product has trial). |
| `Active` | `active` | Normal, active, paid-to-date. |
| `PastDue` | `past_due` | Payment due. |
| `Suspended` | `suspended` | Dunning paused or manual hold. |
| `Canceled` | `canceled` | Canceled by user or provider. |
| `Expired` | `expired` | Subscription end date passed. |
| `OnHold` | `on_hold` | Paused. |
| `Paused` | `paused` | Paused. |
| `TrialEnded` | `trial_ended` | Trial ended, no ongoing subscription (no-obligation trial). |
| `Unpaid` | `unpaid` | Unpaid invoices outstanding. |

**Notes**: For display to the user, map `Active` to "current", `Canceled`/`Expired` to "inactive", `Trialing` to "trial in progress". See provider docs for full state machine.

### IntervalUnit — billing period unit
| C# member | Wire value |
|---|---|
| `Day` | `day` |
| `Month` | `month` |

**Notes**: Most plans use `Month`; daily is less common but supported.

---

## Client construction & configuration

**DI registration** (preferred):
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials 
    { 
        Username = config["Maxio:ApiKey"],  // API key from config
        Password = "x"                       // literal "x"
    };
    o.Environment = ServerEnvironment.Us;   // or ServerEnvironment.Eu if EU hosting
    // Optional: override base URL
    // o.Server.Production.Us.BaseUrl = "http://localhost:8080";
});
```

**Manual instantiation** (if DI unavailable):
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = ServerEnvironment.Us,
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Namespaces** (add `using` directives):
```csharp
using MaxioAdvancedBilling;                           // client, options
using MaxioAdvancedBilling.Api;                       // controller accessors (e.g., client.Customers)
using MaxioAdvancedBilling.Core.Authentication.Basic; // BasicAuthCredentials
using MaxioAdvancedBilling.Models;                    // request/response records
using MaxioAdvancedBilling.Models.Enums;              // CollectionMethod, SubscriptionState, IntervalUnit
using MaxioAdvancedBilling.Errors;                    // CreateCustomerError, CreateSubscriptionError
```

---

## Trap notes

⚠ **Step 1 (customer lookup/create)** — the `Reference` field makes customer lookup idempotent: set it to your app's user ID at create time, then use `ReadCustomerByReference(reference)` to check before creating. If the call throws 404, the customer doesn't exist; if it throws 400+, the reference is malformed or the site is down. Do **not** catch `SdkException<RawError>` to detect "exists"; use HTTP status inspection or store the Maxio ID in your app. **MUST load `dotnet-error-handling`** to distinguish error cases.

⚠ **Step 2 (list plans)** — `ReadProductByHandle` returns the product at its default price point. To use an alternate price point, store the `product_price_point_id` returned and pass it (or the handle) at subscription creation. If you need plan details (description, trial terms), query via `ReadProductByHandle` and extract `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `InitialChargeInCents` from the `Product` field. **MUST load `dotnet-models`** to work with the `Product` and `Subscription` record shapes and understand union fields.

⚠ **Step 3 (create subscription)** — if customer was identified by `CustomerReference`, you **must** have set the `Reference` field when creating that customer (step 1); if omitted, the subscription will be created under a different customer or fail. The `CreateSubscription` notes in the map say payment info may be required depending on product options; if missing and required, the call throws 422 with a nested error in the `Errors` list (or, for 3DS, includes an `action_link`). **MUST load `dotnet-models`** to construct the complex `CreateSubscription`/`CreateSubscriptionRequest` payloads (required fields, optional fields, unions like `ComponentId` or `OfferId`).

⚠ **Step 3 (error boundary)** — `System.Text.Json.JsonException` surfaces from **two directions**: (a) a drifted/malformed 2xx response body (missing required `Subscription` field in `SubscriptionResponse`) deserializes as `JsonException`, **not** `SdkException` — an exception-only boundary that catches only `SdkException` lets it escape; (b) a **non-2xx** body that doesn't match the generated `CreateSubscriptionError` shape throws `JsonException` **during error object construction**, destroying the HTTP status — map every `JsonException` to a 5xx internally then report a deterministic rejection to the caller, and do **not** retry 5xx naively (the same malformed body will fail again). **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ **Step 5 (list subscriptions)** — the returned list includes all subscription states (active, canceled, expired, paused, etc.); filter by `State` in memory if you only want active ones. The `Subscription` model is large (50+ fields); extract only what you display. **MUST load `dotnet-models`** to understand the `Subscription` shape and which nested fields (e.g., `Product`, `Customer`, `CreditCard`) are optional.

⚠ **Configuration & resilience** — the SDK's `Retry` options gate **status codes** via `StatusCodesToRetry` and **verbs** via `HttpMethodsToRetry`, but a **transport failure** (`HttpRequestException`) retries on **every** verb, including `POST` — so a non-idempotent write (subscription creation) can execute twice if the network fails between send and receive. `Timeout` bounds each **attempt**, not the whole operation. No built-in logging hook exists. **MUST load `dotnet-configuration-resilience`** before configuring retries or timeouts.

⚠ **Authentication & configuration** — Basic auth requires username = API key (from `Maxio:ApiKey` config binding), password = literal `"x"` (no substitution). Environment selection (`ServerEnvironment.Us` vs `.Eu`) determines the base URL; if your sandbox is on EU hosting, pass `.Eu`. To redirect to a local mock or dev server, override `options.Server.Production.Us.BaseUrl` or `.Eu.BaseUrl` before constructing the client. **MUST load `dotnet-authentication`** to confirm credential wiring and per-environment setup.

---

## REQUIRED READING

Load these skills **before implementation starts**. They are **not** summarized here; the sheet deliberately carries only operation signatures, model shapes, and error accessors. Each skill covers defaults, worked examples, and integration patterns you must wire yourself.

| Skill | Step(s) | Purpose |
|---|---|---|
| `dotnet-client-initialization` | 1 | Client construction, DI registration, `HttpClient` pipeline (long-lived, reuse via factory). |
| `dotnet-authentication` | 1 | Basic auth setup (username/password), credential binding from config, rotating keys. |
| `dotnet-calling-endpoints` | 2–5 | Calling operations, required vs optional parameters, named arguments, cancellation. |
| `dotnet-models` | 2–5 | Request/response record shapes, required fields, unions, enums (not C# enums), wire-name mapping. |
| `dotnet-error-handling` | 1, 3, 5 | Error cases (typed vs raw), `TryGet…` accessors, `SdkException<T>` boundary, `JsonException` hazards (see trap notes above). |
| `dotnet-configuration-resilience` | 1, 3 | Retries, timeouts (per-attempt, not total), `HttpMethodsToRetry` semantics (status-code-only, not transport failures), `Timeout` semantics, base-URL override. |
| `dotnet-testing` | — (post-MVP) | Mocking the `HttpClient`, test patterns. |

---

## Assumptions & Blockers

- **Assumption**: User is already authenticated in the app and has a unique ID (email or integer) that can be used as the Maxio `Reference` field. This assumes your app owns customer identity and Maxio is a billing engine, not the source of truth.
- **Assumption**: Plans are pre-configured in the Maxio sandbox (eshop-pro, basic-plan handles exist) with pricing, trial terms, and intervals already set. The app reads them via `ReadProductByHandle` and does not create them.
- **Assumption**: No in-memory database loss risk for Maxio customer IDs: the app stores the Maxio customer `Id` in persistent storage (SQL, blob, config, etc.) on first create, and re-uses it on future logins. Losing this mapping forces a re-create under the same reference (which fails with duplicate error) or a lookup by reference (which finds the orphaned customer). Plan to store it.
- **Assumption**: Payment method is either collected out-of-band (user enters card on a Maxio-hosted form or Chargify.js) and stored via payment-profile ID **before** subscription creation, or the product's `RequestCreditCard` flag is `false` (net terms / manual pay). The plan assumes no full-card PCI compliance in eShopOnWeb; if card capture is in-scope, **MUST load** `dotnet-models` for `PaymentProfileAttributes` and understand vault/gateway options.
- **Blocker**: None identified. The Maxio API surface is sufficient for the hero flow. Live traffic can only confirm whether wire-payload encoding (e.g., `ProductHandle` as string, `PaymentCollectionMethod` enum wire name) matches what the actual API ingests — the map and source are authoritative, but real sandbox testing will catch any model-generation drift.

