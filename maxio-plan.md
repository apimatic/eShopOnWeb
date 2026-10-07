# maxio-plan.md — Maxio Advanced Billing subscriptions for eShopOnWeb (`src/PublicApi`)

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · target `netstandard2.0` (runs on .NET 8).

## 1. Scope & sequence

All work is additive to the existing one-time commerce flow. Endpoints live in `src/PublicApi`, JWT-authenticated, under `/api/`.

| # | Step | SDK operations used |
|---|---|---|
| 1 | Bind `Maxio:` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl`) into the SDK client options; register the client in DI | (client construction — CONTRACT row C1) |
| 2 | `GET /api/subscription-plans` — enumerate products, keep those whose product family handle equals the configured handle | `Products.ListProducts` (C2) |
| 3 | Idempotent customer ensure for the authenticated user: look up by `reference`, create on 404 | `Customers.ReadCustomerByReference` (C3), `Customers.CreateCustomer` (C4) |
| 4 | `POST /api/subscriptions` — subscribe the user to a plan (by product handle + default price point), no payment method, response carries plan/price/state/next-billing-date | `Subscriptions.CreateSubscription` (C5) |
| 5 | `GET /api/my-subscriptions` — resolve the user's stored subscription ids against Maxio, return state + next billing date | `Subscriptions.ReadSubscription` (C6a) and/or `Customers.ListCustomerSubscriptions` (C6b), `Subscriptions.FindSubscription` (C6c) |
| 6 | Error boundary for all of the above (Case A/B ladder + `JsonException` handling) | (C7) |
| 7 | Future use (rows included now, no code in hero flow): metered component `api-call` discovery and attach-at-signup shape | `Components.FindComponent` (C8), `Components.ListComponents` (C9), `CreateSubscriptionComponent` (C10) |

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

Namespaces used below: root = `MaxioAdvancedBilling` · `MaxioAdvancedBilling.Api` (controllers) · `MaxioAdvancedBilling.Models` (records) · `MaxioAdvancedBilling.Models.Enums` (enums) · `MaxioAdvancedBilling.Models.AnyOf` (unions) · `MaxioAdvancedBilling.Errors` (typed errors) · `MaxioAdvancedBilling.Core` (`SdkException<T>`) · `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`) · `MaxioAdvancedBilling.Core.Authentication.Basic` (`BasicAuthCredentials`) · `MaxioAdvancedBilling.Core.Configuration` (`RetryOptions`) · `MaxioAdvancedBilling.Servers` (`ServerEnvironment`).

### C1 — Client construction, auth, sandbox / base-URL override — source: `sdk-map.md` (Getting a client, Servers & auth)

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // Username = API key, Password = literal "x"
    Environment = ServerEnvironment.Us,
};
// sandbox subdomain → https://{site}.chargify.com :
options.Server.Production.Us.Site = subdomain;
// Maxio:BaseUrl override — used verbatim as the API base address:
options.Server.Production.Us.BaseUrl = baseUrl; // e.g. "https://<subdomain>.chargify.com" or a mock host
var client = new MaxioAdvancedBillingClient(httpClient, options); // httpClient: System.Net.Http.HttpClient
```

- `MaxioAdvancedBillingClientOptions` (root ns) properties: `Environment: ServerEnvironment`, `Retry: RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`; all members `required` — start from `RetryOptions.Default()`), `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?`.
- `ServerEnvironment.Us` is the default; `.Eu` only for EU-hosted accounts.
- When `Maxio:BaseUrl` is set, assign it to `options.Server.Production.Us.BaseUrl` verbatim (do **not** append paths or `.json`); when absent, set `options.Server.Production.Us.Site = Maxio:Subdomain`.
- DI alternative: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })` (from `ServiceCollectionExtensions.cs`).
- Config binding keys (exact): `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). Values from user-secrets/env; POCO shape and registration are yours: `YOUR CALL — not in the map`.
- Package/version (row 7 of the brief): NuGet id `AsadAli.AdvancedBilling.Sdk`, **version `1.0.2`** — the tag the map's spec stamp records (source commit `15db14b`). Source: `sdk-map.md` (gen:stamp).

### C2 — List products (enumerate plans) — source: `operations/Products.md`, `records-3-Of-Su.md` (`Product`, `ProductResponse`, `ProductFamily`, `ListProductsFilter`)

```csharp
IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse> client.Products.ListProducts(
    MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField,
    MaxioAdvancedBilling.Models.ListProductsFilter? filter,
    DateTimeOffset? endDate, DateTimeOffset? endDatetime,
    DateTimeOffset? startDate, DateTimeOffset? startDatetime,
    bool? includeArchived,
    MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include,
    int? page = 1, int? perPage = 20, CancellationToken ct = default)
// 8 nullable params (dateField … include) have NO default → must pass explicitly (pass null)
// Returns: IReadOnlyList<ProductResponse> · Error: SdkException<RawError> — Case B · Pagination: manual page+perPage
```

- **There is no product-family query parameter** on `ListProducts`. Filter client-side: keep responses where `resp.Product.ProductFamily.Handle == Maxio:ProductFamilyHandle` (`ProductFamily.Handle (handle): string?`, also `ProductFamily.Id (id): int?`).
- `ProductResponse` envelope: single field `Product (product): Product` (`!req`).
- `Product` fields the integration reads (all nullable):
  - `Handle (handle): string?` — plan handle (`eshop-pro`, `basic-plan`)
  - `Name (name): string?`
  - `PriceInCents (price_in_cents): long?` — **cents** (`eshop-pro` ⇒ 29900; `basic-plan` ⇒ 2900)
  - `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`
  - `ProductPricePointId (product_price_point_id): int?` — **the price point id to pass to `CreateSubscription`**
  - `ProductPricePointHandle (product_price_point_handle): string?`
  - `DefaultProductPricePointId (default_product_price_point_id): int?`
  - `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?` — sandbox plans report payment method not required here
  - `ProductFamily (product_family): ProductFamily?`
- Single-plan variant: `ProductResponse ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — Case B (`RawError`).

### C3 — Find customer by reference (idempotency lookup) — source: `operations/Customers.md`, `records-2-Cr-Ne.md` (`Customer`, `CustomerResponse`)

```csharp
MaxioAdvancedBilling.Models.CustomerResponse client.Customers.ReadCustomerByReference(
    string reference, CancellationToken ct = default)
// Returns: CustomerResponse · Error: SdkException<RawError> — Case B (no typed 404 shape)
```

- 404 detection: `catch (SdkException<RawError> ex)` → `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound`. Any other status (401, 5xx) must **not** be treated as "no customer".
- `CustomerResponse` envelope: single field `Customer (customer): Customer` (`!req`).
- `Customer` accessors: `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`.
- Reference convention: stable value keyed to the eShopOnWeb user id — `YOUR CALL — not in the map` (exact string format). The Maxio side of the guarantee is documented: only one customer may exist per `reference` value (`CreateCustomer` Notes, `operations/Customers.md`).

### C4 — Create customer — source: `operations/Customers.md`, `records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`)

```csharp
MaxioAdvancedBilling.Models.CustomerResponse client.Customers.CreateCustomer(
    MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)
// body: nullable, no default → must pass explicitly
// Error: SdkException<CreateCustomerError> — Case A (typed)
//   TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1) [422]
//   TryGetRawError(out RawError) [fallback]
```

- Request: `CreateCustomerRequest { Customer (customer): CreateCustomer — !req }`.
- `CreateCustomer` fields: `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` · plus optional `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`.
- Address fields left out of scope (no shipping/billing address is captured for the subscription hero flow): say so if the UI later collects them — no compiler catches an omission.
- Country must be ISO-3166 2-char and state ISO-3166-2 if ever sent (`CreateCustomer` Notes).

### C5 — Create subscription — source: `operations/Subscriptions.md`, `records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`), `records-3-Of-Su.md` (`Subscription`), `records-4-Su-We.md` (`SubscriptionResponse`)

```csharp
MaxioAdvancedBilling.Models.SubscriptionResponse client.Subscriptions.CreateSubscription(
    MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)
// body: nullable, no default → must pass explicitly
// Error: SdkException<CreateSubscriptionError> — Case A (typed)
//   TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1) [422]
//   TryGetRawError(out RawError) [fallback]
```

- Request: `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription — !req }`.
- `CreateSubscription` — fields this integration writes (all optional on the model, selected per the operation's Notes):
  - **Product identification — the API accepts either form** (`CreateSubscription` Notes): `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`.
  - **Price point — either form**: `ProductPricePointId (product_price_point_id): int?` **or** `ProductPricePointHandle (product_price_point_handle): string?`. Use `Product.ProductPricePointId` from C2 (or `DefaultProductPricePointId`); omit both to get the product's default price point.
  - **Customer identification — either form** (`CreateSubscription` Notes): `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`. Recommended here: `CustomerId` from the C3 lookup. (`CustomerAttributes (customer_attributes): CustomerAttributes?` creates a customer inline — **do not use**; it defeats the idempotent ensure in C3/C4.)
  - `Reference (reference): string?` — store a stable idempotency key on the subscription; `YOUR CALL — not in the map` (exact format, e.g. keyed to user + plan).
  - `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — **set it for the no-card signup flow** (see next bullet).
- **No payment method at signup:** leave `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` unset **and** set `PaymentCollectionMethod` explicitly. Live sandbox evidence: omitting both produced `422` with `ErrorListResponse1` `"No payment method was on file for the $299.00 balance"` — when no collection method is sent, the site default (`Site.DefaultPaymentCollectionMethod (default_payment_collection_method): string?`, records-3-Of-Su.md) applies; an automatic collection charges the balance and needs a payment profile. Set `CreateSubscription.PaymentCollectionMethod = CollectionMethod.Remittance` (wire `remittance`) when `Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` is `true`, otherwise `CollectionMethod.Invoice` (wire `invoice`) on legacy Statements Architecture — per the `CollectionMethod` enum summary (`map/models/enums.md`): RI-valid options are `remittance`, `automatic`, `prepaid`; legacy-valid are `invoice`, `automatic`. Read the site first: `SiteResponse client.Sites.ReadSite(CancellationToken ct = default)` → `SiteResponse { Site (site): Site !req }` (records-3-Of-Su.md), **Case B** (`SdkException<RawError>`, `operations/Sites.md`). Whether the sandbox accepts a remittance/invoice signup with no payment profile can only be confirmed by live traffic — `UNVERIFIED`; if it still 422s the same way, the requirement is product/site configuration and must be changed on the Maxio sandbox side, not in the payload (`CreateSubscription` Notes: "Payment information may be required to create a subscription, depending on the options for the Product being subscribed"). The model also carries `DeferSignup (defer_signup): bool? = false`, but the map row documents no semantics for it — do not rely on it. The resulting subscription `State` in the response is whatever Maxio assigns (read it, don't assume) — `UNVERIFIED`.
- Response: `SubscriptionResponse` envelope: single field `Subscription (subscription): Subscription?` — **read one level down**.
- `Subscription` accessors this integration returns to the caller:
  - `Id (id): int?` · `State (state): SubscriptionState?` · `Reference (reference): string?`
  - `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — **the next billing date** · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`
  - `ProductPriceInCents (product_price_in_cents): long?` (cents) · `BalanceInCents (balance_in_cents): long?`
  - `Product (product): Product?` → nested `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductPricePointId`
  - `Customer (customer): Customer?` → nested `Id`, `Reference`, `Email`. (The `Subscription` record has no flat `customer_id`/`product_id` int fields — go through the nested objects.)
- 422 payload: `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` — human-readable validation messages.

### C6 — Read / list subscriptions — source: `operations/Subscriptions.md`, `operations/Customers.md`, `records-4-Su-We.md` (`SubscriptionResponse`)

**C6a — Read by id** (primary path when the app stores subscription ids — storage is `YOUR CALL — not in the map`):

```csharp
MaxioAdvancedBilling.Models.SubscriptionResponse client.Subscriptions.ReadSubscription(
    int subscriptionId,
    IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionInclude>? include,
    CancellationToken ct = default)
// include: nullable, no default → pass explicitly (null is fine; enum members: SubscriptionInclude.Coupons,
//          SubscriptionInclude.SelfServicePageToken)
// Returns: SubscriptionResponse · Error: SdkException<RawError> — Case B (404/401 via ex.Error.StatusCode)
```

**C6b — List by customer** (alternative when only the Maxio customer id is known):

```csharp
IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse> client.Customers.ListCustomerSubscriptions(
    int customerId, CancellationToken ct = default)
// Returns: IReadOnlyList<SubscriptionResponse> · Error: SdkException<RawError> — Case B · no pagination params
```

**C6c — Find by subscription reference** (idempotency re-check on POST):

```csharp
MaxioAdvancedBilling.Models.SubscriptionResponse client.Subscriptions.FindSubscription(
    string? reference, CancellationToken ct = default)
// reference: nullable, no default → pass explicitly
// Error: SdkException<FindSubscriptionError> — Case A (typed)
//   TryGetNoContent(out RawError) [404] · TryGetRawError(out RawError) [fallback]
```

**C6d — Site-wide list with filters** (only if you choose server-side filtering instead of C6b):

```csharp
IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse> client.Subscriptions.ListSubscriptions(
    MaxioAdvancedBilling.Models.Enums.SubscriptionStateFilter? state,
    int? product, int? productPricePointId, int? coupon, string? couponCode,
    MaxioAdvancedBilling.Models.Enums.SubscriptionDateField? dateField,
    DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime,
    IReadOnlyDictionary<string, string>? metadata,
    MaxioAdvancedBilling.Models.Enums.SortingDirection? direction,
    MaxioAdvancedBilling.Models.Enums.SubscriptionSort? sort,
    IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionListInclude>? include,
    int? page = 1, int? perPage = 20, CancellationToken ct = default)
// 14 nullable params (state … include) must pass explicitly (pass null) · Case B · manual page+perPage
// NOTE: there is NO customer filter on this endpoint — use C6b to scope to a customer.
```

All three list/read rows return the same `SubscriptionResponse` → `Subscription` accessors as C5 (state, next billing date, product handle, price via the nested `Product`).

### C7 — Error handling (applies to every call above) — source: `sdk-map.md` (Error-handling model)

- Every operation is **throw-only**; there are no `…Result`/`ApiResult` no-throw variants in this SDK.
- `SdkException<TError>` (`MaxioAdvancedBilling.Core`, source `Core/Exceptions/SdkException.cs`) exposes `.Error: TError`.
- **Case B (raw)** — `SdkException<RawError>`: `ex.Error.StatusCode: System.Net.HttpStatusCode` (404 vs 401 vs 422 discrimination) · `ex.Error.ReadAsString(): string` · `ex.Error.ReadAsJson<T>(): T?` · `ex.Error.ReadAsBytes()`. Operations in scope that are Case B: `ReadCustomerByReference`, `ListProducts`, `ReadProductByHandle`, `ReadSubscription`, `ListCustomerSubscriptions`, `ListSubscriptions`, `ListComponents`, `FindComponent`, `DeleteCustomer`.
- **Case A (typed)** — `SdkException<{Operation}Error>` (`MaxioAdvancedBilling.Errors`): status-specific `TryGet…(out …)` per the row (C4, C5, C6c) plus inherited `TryGetRawError(out RawError)` fallback for every other status. Typed accessors are try-patterns — a miss means "not that shape", fall through.
- **Dual-direction `System.Text.Json.JsonException` hazards — both must reach the boundary (write these into the boundary now):**
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
- 401 never means "not found": check auth wiring (C1) before translating any status into a domain outcome.

### C8/C9/C10 — Metered component `api-call` (future use, no hero-flow code) — source: `operations/Components.md`, `records-1-Ac-Cr.md` (`Component`, `ComponentResponse`), `records-2-Cr-Ne.md` (`CreateSubscriptionComponent`), `unions.md`

**C8 — Find component by handle:**

```csharp
MaxioAdvancedBilling.Models.ComponentResponse client.Components.FindComponent(
    string handle, CancellationToken ct = default)
// Returns: ComponentResponse · Error: SdkException<RawError> — Case B
// ComponentResponse envelope: Component (component): Component !req
```

- `Component` accessors for `api-call` ($0.01/unit): `Handle (handle): string?` · `Id (id): int?` · `Name (name): string?` · `Kind (kind): ComponentKind?` · `UnitName (unit_name): string?` · `PricePerUnitInCents (price_per_unit_in_cents): long?` (⇒ 1) · `DefaultPricePointId (default_price_point_id): int?` · `ProductFamilyHandle (product_family_handle): string?` · `ProductFamilyId (product_family_id): int?`.

**C9 — List components (site-wide):**

```csharp
IReadOnlyList<MaxioAdvancedBilling.Models.ComponentResponse> client.Components.ListComponents(
    MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField,
    string? startDate, string? endDate, string? startDatetime, string? endDatetime,
    bool? includeArchived,
    MaxioAdvancedBilling.Models.ListComponentsFilter? filter,
    int? page = 1, int? perPage = 20, CancellationToken ct = default)
// 7 nullable params must pass explicitly (pass null) · Case B · manual page+perPage
// (family-scoped variant: ListComponentsForProductFamily(int productFamilyId, …) — Case B, manual page+perPage)
```

**C10 — Attach at signup** (via `CreateSubscription.Components`, C5): `CreateSubscriptionComponent` fields: `ComponentId (component_id): ComponentId1?` (union), `Enabled (enabled): bool?`, `UnitBalance (unit_balance): int?`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity3?` (union), `Quantity (quantity): int?`, `PricePointId (price_point_id): PricePointId2?` (union), `CustomPrice (custom_price): ComponentCustomPrice?`.
Union construction (no `new` on a variant object — static factories, from `unions.md`, namespace `MaxioAdvancedBilling.Models.AnyOf`): `ComponentId1.Int(int)` / `ComponentId1.String(string)` · `PricePointId2.Int(int)` / `PricePointId2.String(string)` · `AllocatedQuantity3.Int(int)` / `AllocatedQuantity3.String(string)`; read back with `TryGetInt(out …)` / `TryGetString(out …)`.

### Enum value tables needed — source: `map/models/enums.md` (namespace `MaxioAdvancedBilling.Models.Enums`; these are `StringEnum<T>` records, NOT C# enums — build with the static members shown or `Type.FromValue(wire)`)

| Enum | Members (`CSharp (wire)`) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards (expired_cards)`, `OnHold`, `PastDue`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `ComponentKind` (future) | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |

## 3. Trap notes (hazard + consequence only — the skill carries the resolution)

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline the SDK client wraps must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request; a per-request client can silently break connection reuse and auth-credential timing. **MUST load `dotnet-client-initialization`** before wiring the client into DI.
- ⚠ Step 1 (credentials) — credentials must be set before/at client construction and loaded from configuration, never hard-coded; getting the Basic pair wrong (Username ≠ API key, Password ≠ `"x"`) surfaces later as 401s with no clue at the call site. **MUST load `dotnet-authentication`**.
- ⚠ Step 2/4/5 (first calls) — every list/search operation has nullable parameters with **no C# default** that mis-bind in a positional call; call them with named arguments. **MUST load `dotnet-calling-endpoints`** before the first `client.{Controller}.{Operation}(...)` call.
- ⚠ Step 4/5 (request/response models) — enums are `StringEnum<T>` not C# enums, unions are built via factories and read via `TryGet…`, and JSON wire names differ from C# property names; constructing `CreateSubscription`/`CreateCustomer` the naive way compiles and still sends the wrong thing. **MUST load `dotnet-models`** before building any payload.
- ⚠ Step 6 (error boundary) — read/list ops are Case B (`RawError`) while create/find are Case A (typed), and `TryGetRawError` is not a catch-all on typed errors; a ladder written against the wrong case misclassifies 404 vs 401 vs 422. **MUST load `dotnet-error-handling`** before writing any `try/catch` around an SDK call.
- ⚠ Steps 1–5 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; and a transport failure can be retried on **every** verb including the non-idempotent `CreateSubscription`/`CreateCustomer` POSTs, so decide what happens to the caller when a write's outcome is unknown. **MUST load `dotnet-configuration-resilience`** before tuning or accepting retry defaults.
- ⚠ Step 6/7 (tests) — the `HttpClient` constructor argument is the test seam; stubbing the client class instead leaves the error paths (C7) untested. **MUST load `dotnet-testing`** before writing tests for the integration layer.

## 4. REQUIRED READING (load before implementation starts — the sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, `HttpClient` ownership/lifetime, options shape. |
| `dotnet-authentication` | Step 1 — Basic credentials (API key + `"x"`), per-environment config, rotation. |
| `dotnet-calling-endpoints` | Steps 2–5 — controller selection, named arguments for must-pass-explicitly params, async/`ct`. |
| `dotnet-models` | Steps 2–5, C10 — required members, `StringEnum<T>`, union factories/`TryGet…`, wire names. |
| `dotnet-error-handling` | Step 6 — Case A/B ladder, `RawError` accessors, both `JsonException` directions. |
| `dotnet-configuration-resilience` | Steps 1–5 — retry/timeout semantics, base-URL/server override, pagination mechanics, non-idempotent-write retry risk. |
| `dotnet-testing` | Step 7 — which seam to fake, covering error and edge paths. |

`dotnet-error-handling` appears unconditionally: every integration here writes an error boundary.

## 5. Assumptions & Blockers

- **Assumption — family filtering is client-side:** `ListProducts` has no product-family query parameter (map row C2); the plan assumes filtering by `Product.ProductFamily.Handle` in the app. If the plan catalog grows beyond one page, pagination must be walked (manual `page`+`perPage`).
- **Assumption — subscription-id persistence:** which of C6a/C6b/C6c backs `GET /api/my-subscriptions` depends on what the app stores per user (Maxio subscription ids vs Maxio customer id vs subscription reference). The map cannot decide this — `YOUR CALL — not in the map`.
- **Assumption — reference formats:** the exact `reference` strings for the Maxio customer (keyed to the eShopOnWeb user id) and the subscription idempotency key are application decisions — `YOUR CALL — not in the map`. The SDK/API side of the guarantee (one customer per `reference`; `FindSubscription` by reference) is documented in C3/C4/C6c.
- **UNVERIFIED — no-card signup acceptance and state:** live sandbox traffic returned `422` `"No payment method was on file for the $299.00 balance"` for a signup that sent no payment fields and no collection method (see C5). C5 now directs setting `PaymentCollectionMethod` (`Remittance` on RI sites, `Invoice` on legacy) as the payload-side lever; whether Maxio's sandbox accepts that signup without a payment profile, and the `Subscription.State` it then returns, can only be confirmed by live traffic — read `Subscription.State` from the response (C5) rather than assuming a value, and treat a missing/unexpected field per the C7 boundary rules. If the 422 persists with a collection method set, the requirement lives in the sandbox's product/site configuration — fix there, not in the payload.
- **No Blockers.** Every operation the feature needs exists in the map; nothing in scope requires a capability the SDK lacks.
