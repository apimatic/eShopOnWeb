# Maxio Advanced Billing integration plan — eShopOnWeb PublicApi

## 1. Scope & sequence

Additive recurring-subscription billing on `src/PublicApi` (JWT-authenticated endpoints), Maxio as billing system of record. One-time cart/checkout untouched.

| # | Step | SDK operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` (version `1.0.2`) to `src/PublicApi` | — |
| 2 | Configuration: bind `Maxio:*` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) | — |
| 3 | Client construction + DI registration (options, Basic auth, server/site or BaseUrl override) | — |
| 4 | `GET /api/subscription-plans` — resolve family handle → id (cached), list products in family | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` — ensure customer (lookup-then-create by reference), check existing subscriptions, create subscription | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — resolve customer by reference, list its subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Error boundary: translate SDK exceptions to HTTP responses | all of the above |
| 8 | Tests for the integration layer | — |

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

### 2.0 Package, namespaces, client construction

| Fact | Value | Map page |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` version `1.0.2` (map stamp: source tag `v1.0.2`). Package id ≠ root namespace. | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options class | `MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| DI extension | `services.AddMaxioAdvancedBillingClient(o => { … })` (from `ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| Auth | HTTP Basic — `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <api key>, Password = "x" }` (password is the literal `"x"`) | `sdk-map.md` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `.Eu` → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Site (subdomain) | `options.Server.Production.Us.Site = "<subdomain>"` (e.g. `cp-exp-1`); `{site}` defaults to `subdomain` | `sdk-map.md` |
| Custom base URL override | `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` — used verbatim as the API base address; when set, prefer it over `Site` derivation | `sdk-map.md` |
| `ServerOptions` / `ProductionOptions` | `ServerOptions` at repo root ⇒ namespace `MaxioAdvancedBilling`; `Servers/ProductionOptions.cs` ⇒ `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| `RetryOptions` | namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required` — start from `RetryOptions.Default()` | `sdk-map.md` |

Using directives needed (C# does not import child namespaces transitively):

```csharp
using MaxioAdvancedBilling;                            // client, options, ServerOptions
using MaxioAdvancedBilling.Models;                     // all records (Product, Customer, Subscription, requests/responses)
using MaxioAdvancedBilling.Models.Enums;               // SubscriptionState, IntervalUnit, CollectionMethod, …
using MaxioAdvancedBilling.Errors;                     // CreateCustomerError, CreateSubscriptionError, ListProductsForProductFamilyError, …
using MaxioAdvancedBilling.Core.Exceptions;            // SdkException<T>  (Core/Exceptions/SdkException.cs)
using MaxioAdvancedBilling.Core.ErrorResponse;         // RawError         (Core/ErrorResponse/RawError.cs)
using MaxioAdvancedBilling.Core.Authentication.Basic;  // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                    // ServerEnvironment, ProductionOptions
```

Error-handling model (applies to every operation): operations are **throw-only** (no `…Result` variants exist in this SDK). On an error status the SDK throws `SdkException<TError>` with `.Error: TError`. Case A: `TError` is a typed `…Error : ApiError` with status-specific `TryGet…(out …)` accessors plus inherited `TryGetRawError(out RawError)`. Case B: `TError` is `RawError` — members `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. (`sdk-map.md`)

### 2.1 Step 4 — list plans: `ProductFamilies.ListProductFamilies` (map: `operations/ProductFamilies.md`)

| | |
|---|---|
| Controller property | `client.ProductFamilies` |
| Signature | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 params nullable, no default → **must pass explicitly** (pass `null`) |
| Returns | `IReadOnlyList<ProductFamilyResponse>` |
| Envelope | `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` (nullable) |
| Inner fields read | `ProductFamily.Id (id): int?` · `ProductFamily.Handle (handle): string?` · `ProductFamily.Name (name): string?` |
| Error | **Case B** — `SdkException<RawError>` (`StatusCode`, `ReadAsString()`) |
| Pagination | none |

Purpose in this integration: resolve configured `Maxio:ProductFamilyHandle` → family `Id` (match `ProductFamily.Handle`, ordinal-ignore-case; cache the result). 

### 2.2 Step 4 — list plans: `ProductFamilies.ListProductsForProductFamily` (map: `operations/ProductFamilies.md`)

| | |
|---|---|
| Controller property | `client.ProductFamilies` |
| Signature | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — params `dateField`…`include` (8) nullable, no default → **must pass explicitly** (pass `null`); pass `includeArchived: false` |
| Returns | `IReadOnlyList<ProductResponse>` |
| Envelope | `ProductResponse.Product (product): Product` (**required**, non-null) |
| Inner fields read (per plan) | `Product.Handle (handle): string?` · `Product.Name (name): string?` · `Product.Description (description): string?` · `Product.PriceInCents (price_in_cents): long?` — **integer cents, `long`; no Money model** · `Product.Interval (interval): int?` · `Product.IntervalUnit (interval_unit): IntervalUnit?` (StringEnum) · also available: `Product.Id (id): int?`, `Product.InitialChargeInCents (initial_charge_in_cents): long?`, `Product.TrialPriceInCents`/`TrialInterval`/`TrialIntervalUnit`, `Product.RequireCreditCard (require_credit_card): bool?`, `Product.ArchivedAt (archived_at): DateTimeOffset?` |
| Error | **Case A** — `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| Pagination | manual `page` + `perPage` (defaults 1 / 20) |

`productFamilyId` is a `string` path param. The map documents the `handle:my-family` format only for `ReadProductFamily` (whose own parameter is `int` — a generated mismatch); whether `ListProductsForProductFamily` accepts `handle:<handle>` is **UNVERIFIED** — do not rely on it. Grounded pattern: resolve handle → numeric id via `ListProductFamilies` (2.1), pass `id.ToString()`.

### 2.3 Steps 5–6 — find customer: `Customers.ReadCustomerByReference` (map: `operations/Customers.md`)

| | |
|---|---|
| Controller property | `client.Customers` |
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query param `reference`); single exact match |
| Returns | `CustomerResponse` |
| Envelope | `CustomerResponse.Customer (customer): Customer` (**required**, non-null) |
| Inner fields read | `Customer.Id (id): int?` · `Customer.Reference (reference): string?` · `Customer.Email (email): string?` · `Customer.FirstName (first_name): string?` · `Customer.LastName (last_name): string?` |
| Error | **Case B** — `SdkException<RawError>`; **customer-not-found = `ex.Error.StatusCode == HttpStatusCode.NotFound`** (that is the "create it" signal in the lookup-then-create pattern) |
| Pagination | none |

Note: `Customers.ListCustomers` has a `q` search param, but its own map notes say to use this lookup endpoint for an exact reference match — use `ReadCustomerByReference`, not `ListCustomers(q: …)`.

### 2.4 Step 5 — create customer: `Customers.CreateCustomer` (map: `operations/Customers.md`)

| | |
|---|---|
| Controller property | `client.Customers` |
| Signature | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Request model | `CreateCustomerRequest.Customer (customer): CreateCustomer` (**required**) |
| `CreateCustomer` fields | `FirstName (first_name): string` **!req** · `LastName (last_name): string` **!req** · `Email (email): string` **!req** · `Reference (reference): string?` ← **set to the eShopOnWeb user ID** · optional: `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `CcEmails`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` |
| Returns | `CustomerResponse` → `.Customer` (required) — read `Id`, `Reference` |
| Error | **Case A** — `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| 422 payload | `CustomerErrorResponse1.Errors (errors): Errors?` — ⚠ the shared `Errors` record models only `PerPage (per_page)` and `PricePoint (price_point)` lists, which does not match a customer-validation payload; unmodeled JSON fields are dropped on deserialize. Defensive directive: read the 422 via the accessor best-effort, but fall back to `TryGetRawError` → `RawError.ReadAsString()` for the message. **UNVERIFIED** that the typed payload carries the actual field errors. |
| Idempotency fact (map notes) | `reference` **must be unique — only one customer per reference value**. Create does **not** upsert: a duplicate-reference create fails with 422. Pattern: `ReadCustomerByReference` first → on 404, `CreateCustomer`; if `CreateCustomer` still 422s on reference uniqueness (concurrent create), re-`ReadCustomerByReference` and use the winner. |

### 2.5 Steps 5–6 — list a customer's subscriptions: `Customers.ListCustomerSubscriptions` (map: `operations/Customers.md`)

| | |
|---|---|
| Controller property | `client.Customers` |
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Envelope | `SubscriptionResponse.Subscription (subscription): Subscription?` (**nullable** — null-check before reading) |
| Error | **Case B** — `SdkException<RawError>` |
| Pagination | none |

### 2.6 Step 5 — create subscription: `Subscriptions.CreateSubscription` (map: `operations/Subscriptions.md`)

| | |
|---|---|
| Controller property | `client.Subscriptions` |
| Signature | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Request model | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription` (**required**) |
| `CreateSubscription` fields to set | `ProductHandle (product_handle): string?` ← the plan handle (e.g. `eshop-pro`) · `CustomerId (customer_id): int?` ← Maxio customer id from 2.3/2.4 (alternative: `CustomerReference (customer_reference): string?`) · `Reference (reference): string?` ← set a deterministic value, e.g. `{userId}:{productHandle}`, as an idempotency handle |
| Fields to leave unset | all payment fields (`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`), trial/setup fields — this site's products need no payment method, no trial, no setup fee. (Map notes: payment info is required only "depending on the options for the Product".) |
| Returns | `SubscriptionResponse` → `.Subscription` (nullable) |
| Response fields read | `Subscription.Id (id): int?` · `Subscription.State (state): SubscriptionState?` (StringEnum) · `Subscription.Product (product): Product?` — nested `Product` with `Handle`/`Name`/`PriceInCents`/`Interval`/`IntervalUnit` · `Subscription.ProductPriceInCents (product_price_in_cents): long?` · `Subscription.NextAssessmentAt (next_assessment_at): DateTimeOffset?` ← **the next-billing-date field on the read model; there is no `next_billing_at` on `Subscription`** · `Subscription.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `Subscription.ActivatedAt (activated_at): DateTimeOffset?` · `Subscription.Reference (reference): string?` · `Subscription.Customer (customer): Customer?` |
| Error | **Case A** — `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` (required) — clean string list. |
| Idempotency | No upsert on create. Grounded pattern: before creating, `ListCustomerSubscriptions(customerId)` and return the existing subscription if one already exists for the same `Product.Handle` in a non-terminal state (`Active`, `Trialing`, `AwaitingSignup`, `PastDue`, `OnHold` — see enum table). Belt-and-braces: also set `Reference` and use `FindSubscription` (2.7) pre-create. |

### 2.7 (supporting) — `Subscriptions.FindSubscription` (map: `operations/Subscriptions.md`)

| | |
|---|---|
| Signature | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly** |
| Returns | `SubscriptionResponse` |
| Error | **Case A** — `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404 = no such reference] · `TryGetRawError(out RawError)` [fallback] |

### 2.8 Enum values needed (map: `models/enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`)

Enums are `StringEnum<T>` records, **not** C# enums — use the static members (`SubscriptionState.Active`) or `Type.FromValue("active")`; never `.ToString()`-compare wire values by hand.

| Enum | Members (C# name = wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — only needed if you choose to set `PaymentCollectionMethod`; default plan: leave unset |

### 2.9 Endpoint → operation mapping summary

| Endpoint | Flow |
|---|---|
| `GET /api/subscription-plans` | (cached) `ListProductFamilies` → match `Handle == Maxio:ProductFamilyHandle` → `ListProductsForProductFamily(familyId.ToString(), dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null)` → map each `.Product` to { handle, name, description, price = `PriceInCents` / 100, interval = `Interval` + `IntervalUnit` } |
| `POST /api/subscriptions` | `ReadCustomerByReference(userId)` → 404 ⇒ `CreateCustomer` (Reference = userId) → `ListCustomerSubscriptions(customer.Id)` → existing active-ish sub for the product handle? return it : `CreateSubscription(ProductHandle + CustomerId + Reference)` → return { product, price, `State`, `NextAssessmentAt` } |
| `GET /api/my-subscriptions` | `ReadCustomerByReference(userId)` → 404 ⇒ empty list → `ListCustomerSubscriptions(customer.Id)` → map each non-null `.Subscription` to { `State`, `Product` info, `ProductPriceInCents`, `NextAssessmentAt` } |

---

## 3. Trap notes

> ⚠ Step 3 (client registration) — the `HttpClient`/handler pipeline behind the SDK client must be long-lived and reused (socket exhaustion / DNS-staleness hazards otherwise); what may be transient vs singleton is not what the constructor signature suggests. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 3 (auth) — where credentials must be set relative to client construction, and how to load the key from configuration without hardcoding, is not visible from the options shape. **MUST load `dotnet-authentication`**.

> ⚠ Steps 4–6 (every call) — list/search operations take many nullable parameters with **no C# default**; positional calls mis-bind. Call with named arguments (`ct:` for the token). **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 4–6 (models) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>` records, not C# enums (comparison and construction differ); unmodeled JSON fields are silently dropped on deserialize (bites the 2.4 422 payload). **MUST load `dotnet-models`**.

> ⚠ Step 5 (idempotency vs retries) — whether a failed `CreateSubscription`/`CreateCustomer` can be re-sent *by the SDK itself* (and which failures that covers) determines whether the list-then-create check is sufficient on its own. **MUST load `dotnet-configuration-resilience`** before finalizing the idempotency design and before tuning `Retry`/`Timeout` (what `Timeout` actually bounds is also non-obvious).

> ⚠ Step 7 (error boundary) — which operations are Case A vs Case B is per-operation (see sheet); `TryGetRawError` is not a catch-all on typed errors. **MUST load `dotnet-error-handling`**.

> ⚠ Step 8 (tests) — the SDK's test seam is a specific constructor argument; stubbing at the wrong layer couples tests to SDK internals. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — step 3 (client construction, DI lifetime)
- `dotnet-authentication` — step 3 (Basic credentials from config)
- `dotnet-calling-endpoints` — steps 4–6 (named arguments, envelopes, async/ct)
- `dotnet-models` — steps 4–6 (StringEnum, nullability, dropped fields)
- `dotnet-error-handling` — step 7 (Case A/B mechanics, catch ladder)
- `dotnet-configuration-resilience` — steps 3 & 5 (retry/timeout semantics, base-URL selection, pagination)
- `dotnet-testing` — step 8 (faking the SDK)

Mandatory hazard rows for the error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**

1. Product-family handle is resolved to a numeric family id via `ListProductFamilies` at runtime (cache it). Whether `ListProductsForProductFamily` accepts a `handle:<handle>` path value directly is **UNVERIFIED** (the map documents that format only for `ReadProductFamily`, whose own parameter is `int` — a generated mismatch); the two-step pattern is fully grounded and needs no such support.
2. `Maxio:BaseUrl` override maps to `options.Server.Production.Us.BaseUrl` (US environment assumed; sandbox subdomain `cp-exp-1` implies US hosting). If the account is EU-hosted, switch `Environment` and use `.Eu.*` override points instead.
3. Products are configured in Maxio with no payment requirement, so `CreateSubscription` is sent with only `ProductHandle` + `CustomerId` + `Reference`. If the site/product still demands a payment method, the create will 422 — surfaced via `ErrorListResponse1.Errors` (string list).
4. "Next billing date" is served from `Subscription.NextAssessmentAt` (the read model has no `next_billing_at` field); `CurrentPeriodEndsAt` is available as a companion field.
5. Customer `Reference` = eShopOnWeb user ID (string). Subscription `Reference` = `{userId}:{productHandle}` as a deterministic idempotency handle; whether the server *enforces* subscription-reference uniqueness on create is **UNVERIFIED** — the list-then-create check (2.6) is the primary guard.
6. The 422 typed payload of `CreateCustomer` (`CustomerErrorResponse1.Errors`, shaped as the shared `Errors` record with only `PerPage`/`PricePoint` members) is a suspicious shared model — plan reads it best-effort and falls back to the raw body. **UNVERIFIED** against live traffic.
7. JWT authentication/identity plumbing on `PublicApi` is existing app infrastructure; this plan covers only the Maxio side.

**Blockers** — none.
