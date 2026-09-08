# maxio-plan.md — Maxio Advanced Billing subscriptions for eShopOnWeb (PublicApi)

SDK: NuGet `AsadAli.AdvancedBilling.Sdk` **1.0.2** (latest published; verified against api.nuget.org — matches the map's spec stamp) · root `using` namespace `MaxioAdvancedBilling` · `netstandard2.0` · target `src/PublicApi`.

---

## 1. Scope & sequence

Additive/parallel capability; the existing cart/checkout flow is untouched. All HTTP endpoints live on **PublicApi**, JWT-authenticated, identity from token.

| # | Step | SDK operations used |
|---|---|---|
| 1 | Config + client registration: bind `Maxio:*` settings, validate at startup, register SDK client | `MaxioAdvancedBillingClient` + `MaxioAdvancedBillingClientOptions` (§CONTRACT) |
| 2 | Error boundary: translate `SdkException<…>` → HTTP status codes + problem responses; the deserialization hazard rows below | all operations |
| 3 | `GET /api/subscription-plans` — resolve the configured product family, list its products | `ProductFamilies.ListProductFamilies` → `Products.ListProductsForProductFamily` |
| 4 | Idempotent create-or-get customer for the current user | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` — idempotent subscribe (lookup by reference, else create) | `Subscriptions.FindSubscription` → `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — list current user's subscriptions | `Customers.ListCustomerSubscriptions` |
| 7 | (Nice-to-have) `POST /api/subscriptions/{id}/cancel` | `SubscriptionStatus.CancelSubscription` |

Idempotency is grounded in the provider contract, not invented: **customer `reference` is unique per site** (CreateCustomer Notes: "you may only create one customer for a given reference value"), and both the customer and subscription lookups are dedicated endpoints (`/customers/lookup.json?reference=`, `/subscriptions/lookup.json?reference=`). Pattern: **lookup first → create on miss**; a 422 on the create race is surfaced, never retried blindly (see Step-2 hazard on transport-failure retries).

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

Every operation is **throw-only** — the SDK generates no `…Result` no-throw variants. Response envelopes wrap their payload in **one field** (e.g. `ProductResponse.Product`); reads go one level down.

### 2.1 Operations

| Operation | Signature (verbatim) | Returns / envelope | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `CustomerResponse` → `.Customer` (`Customer`) | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` · `ex.Error.StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | none | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | `CustomerResponse` → `.Customer` | **Case A** `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — the 5 date params nullable, **must pass explicitly** (pass `null`) | `IReadOnlyList<ProductFamilyResponse>` → each `.ProductFamily` | **Case B** `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` nullable, **must pass explicitly** (pass `null`) | `IReadOnlyList<ProductResponse>` → each `.Product` | **Case A** `SdkException<ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1 / 20) | `operations/ProductFamilies.md` |
| `client.Products.ListProducts` *(alternative: site-wide list, client-side filter by family)* | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` nullable, **must pass explicitly** | `IReadOnlyList<ProductResponse>` → each `.Product` (`.ProductFamily` nested on each) | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (defaults 1 / 20) | `operations/Products.md` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `ProductResponse` → `.Product` | **Case B** `SdkException<RawError>` | none | `operations/Products.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **must pass explicitly** | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>` · `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, **must pass explicitly** (pass `null`) | `SubscriptionResponse` → `.Subscription` | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| `client.SubscriptionStatus.CancelSubscription` *(⚠ lives on `client.SubscriptionStatus`, NOT `client.Subscriptions`)* | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** (pass `null` for immediate cancel) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<CancelSubscriptionApiError>` · `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] (AnyOf: `ErrorListResponse1` \| `SingleErrorResponse1`) · `TryGetRawError(out RawError)` [fallback] | none | `operations/SubscriptionStatus.md` |

### 2.2 Request models

**`CreateCustomerRequest`** (`MaxioAdvancedBilling.Models`) — one field: `Customer (customer): CreateCustomer !req`

`CreateCustomer` fields (nothing else required — set `Reference`, `FirstName`, `LastName`, `Email`):

| Field | Wire | Type | Req |
|---|---|---|---|
| `FirstName` | `first_name` | `string` | **req** |
| `LastName` | `last_name` | `string` | **req** |
| `Email` | `email` | `string` | **req** |
| `Reference` | `reference` | `string?` | opt |
| `Organization`, `Phone`, `Locale`, `Country`, `City`, `State`, `Zip`, `Address`, `Address2`, `CcEmails`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | — | nullable | opt |

Source: `records-1-Ac-Cr.md`. **Idempotency key: `Reference` = the app's stable user id.** CreateCustomer Notes: reference must be unique per site.

**`CreateSubscriptionRequest`** (`MaxioAdvancedBilling.Models`) — one field: `Subscription (subscription): CreateSubscription !req`

`CreateSubscription` marks **nothing required** (only `DeferSignup` has a default, `= false`). The Notes define what a valid call needs — these are the fields the plan uses:

| Field | Wire | Type | Use here |
|---|---|---|---|
| `ProductHandle` | `product_handle` | `string?` | **set** — plan handle (`eshop-pro` / `basic-plan`); handle, not numeric id, because ids are not stable |
| `CustomerReference` | `customer_reference` | `string?` | **set** — reference the customer was created with (alternative: `CustomerId: int?` after resolving the numeric id) |
| `Reference` | `reference` | `string?` | **set** — app-generated unique subscription reference = the double-click dedupe key (then `FindSubscription(reference)` before create) |
| `PaymentProfileId` / `PaymentProfileAttributes` / `CreditCardAttributes` / `BankAccountAttributes` | — | nullable | **omit entirely** — plans require no card; per Notes payment info is only required depending on product options. Also omittable: `PaymentCollectionMethod` (`CollectionMethod?`) |
| Other fields (`CustomPrice`, `CouponCode(s)`, `NextBillingAt`, `Components`, `Metafields`, …) | — | nullable | omitted — not in scope |

Source: `records-2-Cr-Ne.md` (full `CreateSubscription` row). Notes: `operations/Subscriptions.md` — product via `product_id` **or** `product_handle`; customer via `customer_id` **or** `customer_reference`.

**`CancellationRequest`** (`MaxioAdvancedBilling.Models`) — `Subscription (subscription): CancellationOptions !req`; pass `null` body for immediate cancel. `CancellationOptions` fields all optional (`cancellation_message`, `reason_code`, `cancel_at_end_of_period`, `scheduled_cancellation_at`, `refund_prepayment_account_balance`). Source: `records-1-Ac-Cr.md`.

### 2.3 Response models (fields the integration reads)

**`CustomerResponse`** → `Customer (customer): Customer !req`. `Customer` reads: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`. Source: `records-2-Cr-Ne.md`.

**`ProductResponse`** → `Product (product): Product !req`. Plan-display reads off `Product`:

| Field | Wire | Type |
|---|---|---|
| `Handle` | `handle` | `string?` |
| `Name` | `name` | `string?` |
| `Description` | `description` | `string?` |
| `PriceInCents` | `price_in_cents` | `long?` |
| `Interval` | `interval` | `int?` |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` (`day` / `month`) |
| `ProductFamily` | `product_family` | `ProductFamily?` (`.Handle` for filtering) |
| `ArchivedAt` | `archived_at` | `DateTimeOffset?` (filter out archived) |

Source: `records-3-Of-Su.md`. `ProductFamilyResponse` → `ProductFamily (product_family): ProductFamily?` (`Id: int?`, `Handle: string?`, `Name: string?`).

**`SubscriptionResponse`** → `Subscription (subscription): Subscription?` *(nullable envelope — null-check before deref)*. Confirm/list reads off `Subscription`:

| Field | Wire | Type | Note |
|---|---|---|---|
| `Id` | `id` | `int?` | |
| `State` | `state` | `SubscriptionState?` | enum below |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | **the "next billing date" — the record has NO `next_billing_at` field** (verified against the full `Subscription` row) |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | |
| `Product` | `product` | `Product?` | nested plan: `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | |
| `BalanceInCents` | `balance_in_cents` | `long?` | |
| `CurrentBillingAmountInCents` | `current_billing_amount_in_cents` | `long?` | |
| `Customer` | `customer` | `Customer?` | |
| `Reference` | `reference` | `string?` | app dedupe key echoed back |
| `CancelAtEndOfPeriod` | `cancel_at_end_of_period` | `bool?` | |
| `CanceledAt` | `canceled_at` | `DateTimeOffset?` | |

Source: `records-3-Of-Su.md`. ⚠ All these properties are **nullable (`T?`)** — every read needs a null-tolerant mapping path.

### 2.4 Enum value tables (`MaxioAdvancedBilling.Models.Enums` — `StringEnum<T>`, NOT C# enums; source `map/models/enums.md`)

| Enum | Members (member name → wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` *(only if ever set — default plan: omit)* |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended`, `TrialEnded (trial_ended)`, `Trialing`, `Unpaid` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |

### 2.5 Error payloads (typed error bodies, `MaxioAdvancedBilling.Models`)

| Type | Fields | Maps to |
|---|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | 422 on `CreateSubscription`, `ListProductsForProductFamily` 404 is a bare string (`TryGetString`) |
| `CustomerErrorResponse1` | `Errors (errors): Errors?` — ⚠ **trust warning below** | 422 on `CreateCustomer` |
| `CancelSubscriptionErrorResponse` | AnyOf union: `TryGetErrorListResponse1(out ErrorListResponse1)` / `TryGetSingleErrorResponse1(out SingleErrorResponse1)`; `SingleErrorResponse1` = `Error (error): string !req` | 422 on `CancelSubscription` |

**Trust warning — `CustomerErrorResponse1.Errors` is a suspicious shared model.** The generated `Errors` record declares only `PerPage (per_page)` and `PricePoint (price_point)` lists (`records-2-Cr-Ne.md`) — plainly not a customer-validation body. Meanwhile the sibling 422 payloads (`ErrorListResponse1`) are plain string lists. This is a map-visible disagreement between two generated definitions. **Directive: extract 422 messages best-effort — try `TryGetCustomerErrorResponse1`, but fall back to `TryGetRawError` → `ReadAsString()` for the message — and never branch logic on `CustomerErrorResponse1.Errors` fields.** Whether the live 422 body matches `ErrorListResponse1`'s shape is **UNVERIFIED** (only live traffic can confirm).

### 2.6 Client construction, auth, base URL

All from `sdk-map.md`:

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth   = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // Username = API key, Password = literal "x"
    Environment = ServerEnvironment.Us,          // Us (default) | Eu
    Server      = new ServerOptions              // subdomain + BaseUrl override — see rule below
    {
        Production = { Us = { Site = subdomain /* {site} in the template */ } }
    },
    Retry       = RetryOptions.Default(),        // every RetryOptions member is required — start from Default()
};
var client = new MaxioAdvancedBillingClient(httpClient, options); // ONLY constructor: (HttpClient, MaxioAdvancedBillingClientOptions)
```

- **Base-URL rule** (map, `Servers & auth`): US → `https://{site}.chargify.com`; EU → `https://{site}.ebilling.maxio.com`; `{site}` = `options.Server.Production.Us.Site`. The brief's "https://{subdomain}.{environment}.chargify.com" guess is **wrong** for EU — the SDK's templates above are the rule.
- **BaseUrl override** (the app's `Maxio:BaseUrl`): when set, assign verbatim to `options.Server.Production.Us.BaseUrl` — replaces the derived template (mock/dev-host override point per map).
- **DI**: `services.AddMaxioAdvancedBillingClient(o => { … })` exists (`ServiceCollectionExtensions.cs`, root namespace). Lifetime/thread-safety and HttpClient ownership are **not** stated in the map → Step-1 hazard below.
- Retry/resilience: `options.Retry` is a full `RetryOptions` (Polly-backed) — members listed in `sdk-map.md`; semantics are Step-1's hazard.

Config binding keys (app-fixed): `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). Missing `ApiKey`/`Subdomain`/`ProductFamilyHandle` → fail startup validation (app decision). Validate `BaseUrl` is absolute HTTPS when present (app decision).

---

## 3. Trap notes

- ⚠ Step 2/5 (error boundary + create race) — the two `System.Text.Json.JsonException` directions (verbatim, load before writing the boundary):
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
  **MUST load `dotnet-error-handling`** before writing that boundary.
- ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a non-idempotent write (`CreateSubscription`) can be re-sent by the pipeline, and whether `MaxRetries = 0` even sticks, decides your double-click safety. **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline must be long-lived and reused (the SDK's only constructor takes an `HttpClient`), and the DI extension's registration lifetime/thread-safety is not documented in the map. **MUST load `dotnet-client-initialization`** before registering.
- ⚠ Step 1 (auth) — credentials are Basic with **Username = API key, Password = literal `"x"`**; load the key from configuration (`Maxio:ApiKey`), never hardcode; a 401/403 means config-shape failure, not code. **MUST load `dotnet-authentication`** before setting credentials.
- ⚠ Steps 4–7 (every call) — many list/lookup ops are Case B (`SdkException<RawError>`, no typed accessors — e.g. `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductsForProductFamily`'s siblings) while create ops are Case A; `TryGetRawError` is not a catch-all on the typed errors. Call list/search ops with **named arguments** — several optional params have no C# default. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 3/5 (models) — enums are `StringEnum<T>` built from members like `CollectionMethod.Invoice` (never wire values), `CreateSubscription` marks nothing `required` so the compiler cannot catch a dropped field, and unmodeled JSON fields are silently dropped on deserialize (relevant to the nullable-everywhere `Subscription` reads). **MUST load `dotnet-models`** before constructing payloads.
- ⚠ Steps 4–6 (tests) — the `HttpClient` constructor argument is the test seam; stub the SDK there rather than mocking controllers. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

| Skill | Governs | Hazard rationale |
|---|---|---|
| `dotnet-error-handling` | Step 2 (boundary) + every catch | Case A vs B per operation, `TryGet…` mechanics, and the two JsonException directions (both rows above) |
| `dotnet-configuration-resilience` | Step 1 | what retry/timeout actually bound; transport-failure retries on POST vs the double-click guarantee |
| `dotnet-client-initialization` | Step 1 | HttpClient ownership/lifetime, DI registration shape, thread-safety |
| `dotnet-authentication` | Step 1 | Basic auth key-as-username / `"x"` password; config-shaped 401 diagnosis |
| `dotnet-calling-endpoints` | Steps 3–7 | named arguments on no-default optionals; envelope one-level-down reads |
| `dotnet-models` | Steps 3/5 | `StringEnum<T>`, unions/`TryGet…`, required-member gaps, dropped unmodeled fields |
| `dotnet-testing` | Steps 4–6 | which seam to fake (the `HttpClient` arg), real-behaviour assertions |

---

## 5. Assumptions & Blockers

- **Assumption — `Maxio:Environment` key**: the env-var list includes `MAXIO_ENVIRONMENT` but the `Maxio:` key list does not name a key for it. Assumed a `Maxio:Environment` setting exists, holding the SDK's hosting value (`US` default / `EU`), mapping to `ServerEnvironment.Us`/`.Eu`. If it actually holds "sandbox", note that the SDK has **no sandbox `ServerEnvironment`** — sandbox is just a site subdomain (`ServerEnvironment.Us` + that subdomain) whose site runs in test mode. YOUR CALL — not in the map.
- **Assumption — plan seeding**: assumed the sandbox already contains product family `eshop-subscribe` with products `eshop-pro` ($299/mo) and `basic-plan` ($29/mo); plan handles/`Maxio:ProductFamilyHandle` are config-driven. YOUR CALL — not in the map.
- **Assumption — customer reference value**: assumed the app has a stable per-user identifier (e.g. the ASP.NET Identity user id) to use as the Maxio `reference`. The concrete value is the app's identity path. YOUR CALL — not in the map.
- **Assumption — subscription reference**: `CreateSubscription.Reference` + `FindSubscription(reference)` is the map-documented dedupe pair, but the map does not document that subscription `reference` is enforced unique the way customer `reference` is. Lookup-before-create is the pattern; a duplicate-create race is handled by re-looking-up, not by assuming a 422 means duplicate. **UNVERIFIED** (only live traffic can confirm uniqueness enforcement).
- **UNVERIFIED — live 422 body shape** for `CreateCustomer` (see §2.5 trust warning): the generated `CustomerErrorResponse1` disagrees with sibling list-shaped 422 payloads; defensive best-effort extraction is mandated in the sheet.
- **No Blockers.** Every operation in scope exists in the map; no capability gap was found (family→products resolution is two calls: `ListProductFamilies` → match `Handle` → `ListProductsForProductFamily(family.Id)`; there is no direct list-products-by-family-handle endpoint).
- Environment note: .NET 8 target with only the .NET 10 SDK installed — build with `DOTNET_ROLL_FORWARD=Major` per the app's `global.json` (`rollForward latestMajor`). App-side, not SDK.
