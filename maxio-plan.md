# Maxio Advanced Billing — Integration Plan & Contract Sheet (eShopOnWeb)

Scope: add recurring-subscription billing to eShopOnWeb, additive to the existing one-time
commerce. Three JWT-authenticated HTTP endpoints on the PublicApi project, backed by the Maxio
Advanced Billing .NET SDK. Maxio is the system of record.

- Package id: **`AsadAli.AdvancedBilling.Sdk`** (install via NuGet). Root `using` namespace:
  **`MaxioAdvancedBilling`** (note: differs from the package id). Version pinned by the map:
  **v1.0.2** (source commit `15db14b`). Target `netstandard2.0`. Source: `sdk-map.md`.
- Every SDK operation below is **throw-only** (no `…Result`/no-throw variant exists) — every call
  must be wrapped. Source: `sdk-map.md` error model.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Register the SDK client (options: auth + subdomain/base-URL), DI-wire it | client construction (`sdk-map.md`) |
| 2 | `GET /api/subscription-plans` — list products in the configured family | `ProductFamilies.ListProductsForProductFamily` (family by `handle:<handle>`), or resolve family via `ProductFamilies.ReadProductFamily` |
| 3 | `POST /api/subscriptions` — ensure customer (idempotent), then subscribe | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` → `Customers.ListCustomerSubscriptions` (idempotency guard) → `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — list the current user's subscriptions | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |

Idempotency design (step 3): use a **stable per-shopper `reference`** (e.g. the eShop user id/email)
as the Maxio customer `reference`. Look the customer up by that reference first; only create when the
lookup 404s. Before creating a subscription, list the customer's existing subscriptions and short-circuit
if an active subscription for the target product already exists. See Assumptions & Blockers for the
capability the SDK does *not* natively provide (no server-side create-or-get).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types live in different child namespaces, and two types configured side
> by side in the same options object routinely live in different ones. Dropping a type to the root
> or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Client construction, auth, server/base-URL

| Concern | Fact | Namespace / source |
|---|---|---|
| Client class | `MaxioAdvancedBillingClient` — sole ctor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `MaxioAdvancedBilling` (root); `MaxioAdvancedBillingClient.cs` |
| Options class | `MaxioAdvancedBillingClientOptions` — props: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`) | `MaxioAdvancedBilling` (root); `MaxioAdvancedBillingClientOptions.cs` |
| Auth | HTTP **Basic**: `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. **Username = API key, Password = the literal string `"x"`.** | `BasicAuthCredentials` → `MaxioAdvancedBilling.Core.Authentication.Basic`; `sdk-map.md` Servers & auth |
| Environment | `options.Environment = ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `.Eu` → `https://{site}.ebilling.maxio.com` | `ServerEnvironment` → `MaxioAdvancedBilling.Servers`; `sdk-map.md` |
| Subdomain (site) | `options.Server.Production.Us.Site = <Maxio:Subdomain>` — `{site}` in the base-URL template defaults to `subdomain` | `ServerOptions`/`ProductionOptions`; `sdk-map.md` Servers & auth |
| Custom base URL override (`Maxio:BaseUrl`) | When set, use verbatim: `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`. When unset, leave `BaseUrl` and set only `Site` so the template derives from subdomain. | `sdk-map.md` Servers & auth |
| DI | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Server.Production.Us.Site = …; });` | `ServiceCollectionExtensions.cs`; `sdk-map.md` |

All in-scope operations use the **Production** server group (not Ebb). Source: each operation row marks `(Production)`.

### 2b. Operations

| Op (controller.method) | Signature (params in order) | Request model + fields used | Response envelope → inner fields read | Error case + accessors + payload | Pagination | Map page |
|---|---|---|---|---|---|---|
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` ← `reference` | (no body) `reference` = stable shopper ref | `CustomerResponse` → `.Customer` (`Customer`): read `.Id` (`id`: int?), `.Reference` (`reference`), `.Email`, `.FirstName`, `.LastName` | **Case B** `SdkException<RawError>`: `.Error.StatusCode` (**404 = not found → create path**), `.ReadAsString()`, `.ReadAsJson<T>()` | none | operations/Customers.md |
| `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer` **required**: `FirstName (first_name) !req`, `LastName (last_name) !req`, `Email (email) !req`; set `Reference (reference)` = stable shopper ref for idempotency; optional `Organization`, `Phone`, address fields | `CustomerResponse` → `.Customer.Id` (the Maxio customer id used downstream), `.Customer.Reference` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload `CustomerErrorResponse1 { Errors (errors): Errors? }` — see ⚠ trust note below | none | operations/Customers.md |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — path `{customer_id}` | (no body) `customerId` = `Customer.Id` from lookup/create | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` (`Subscription?`, **nullable — null-check each element**): read `.State`, `.Product`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt`, `.CurrentPeriodStartedAt` | **Case B** `SdkException<RawError>`: `.Error.StatusCode`, `.ReadAsString()` | none (no page/perPage) | operations/Customers.md |
| `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`; on `CreateSubscription` set **`ProductHandle (product_handle)`** = plan handle (e.g. `eshop-pro`) and identify the customer with **`CustomerId (customer_id): int?`** (preferred — from the ensured customer) OR **`CustomerReference (customer_reference): string?`**. Payment-not-required plans: send **no** `credit_card_attributes` / `payment_profile_attributes`. Optionally `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`. | `SubscriptionResponse` → `.Subscription` (**`Subscription?` — nullable**): read `.State` (`SubscriptionState?`), `.CurrentPeriodEndsAt` (`current_period_ends_at`), `.NextAssessmentAt` (`next_assessment_at` — the effective next-billing date; see note), `.CurrentPeriodStartedAt`, `.ProductPriceInCents`, `.Product` (`Product?` → `.Handle`, `.Name`, `.PriceInCents`, `.Interval`, `.IntervalUnit`) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` | none | operations/Subscriptions.md |
| `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass the 8 nullable filters explicitly as `null`; call with **named args** | `productFamilyId` accepts **`"handle:<ProductFamilyHandle>"`** (e.g. `"handle:eshop-subscribe"`) or a numeric id string; `includeArchived: false` to exclude archived | `IReadOnlyList<ProductResponse>`; each `.Product` (`Product !req`): read `.Handle`, `.Name`, `.Description`, `.PriceInCents` (`price_in_cents`), `.Interval` (`interval`), `.IntervalUnit` (`interval_unit`), `.Id`, `.ProductPricePointHandle` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | operations/ProductFamilies.md |
| `ProductFamilies.ReadProductFamily` (only if id must be resolved separately) | `ReadProductFamily(int id, CancellationToken ct = default)` — accepts numeric id; per notes the family may also be addressed as `handle:my-family` in the family-id string form used by the list op | (no body) | `ProductFamilyResponse` → `.ProductFamily` (`ProductFamily?`): `.Id`, `.Handle`, `.Name` | **Case B** `SdkException<RawError>`: `.Error.StatusCode` (404 if handle unknown) | none | operations/ProductFamilies.md |

Alternative for the plans endpoint if you prefer the by-handle single-product route:
`Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse`
(Case B `SdkException<RawError>`). Source: operations/Products.md. The family-scoped list above is the
match for "list available plans in the configured product family".

> **Next-billing date:** the `Subscription` model has **no** `next_billing_at` field. The effective
> next assessment/billing date is `NextAssessmentAt (next_assessment_at)`; `CurrentPeriodEndsAt
> (current_period_ends_at)` is the current-period end. Map both to the endpoint's "next billing"
> field from these two. Source: records-4-Su-We.md (`Subscription`) — the model carries no
> `next_billing_at` member. `UNVERIFIED` (map-derived): which of the two the product treats as the
> displayed "next billing" is a product decision; both are present on the model.

### 2c. Request/response record shapes (fields the integration references)

- `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`. Source: records-1-Ac-Cr.md.
- `CreateCustomer` (required in **bold**): **`FirstName (first_name): string !req`**, **`LastName (last_name): string !req`**,
  **`Email (email): string !req`**, `Reference (reference): string?` (set for idempotency),
  `Organization?`, `Phone?`, `Address?`, `City?`, `State?`, `Zip?`, `Country?`. Source: records-1-Ac-Cr.md.
- `CustomerResponse` → `Customer (customer): Customer !req`. `Customer`: `Id (id): int?`,
  `Reference (reference): string?`, `Email`, `FirstName`, `LastName`, … Source: records-2-Cr-Ne.md.
- `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`. Source: records-2-Cr-Ne.md.
- `CreateSubscription` (all fields optional at the type level — the *server* requires product + customer
  identification): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`,
  `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`,
  `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`,
  `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`,
  `CustomerAttributes (customer_attributes): CustomerAttributes?`. Source: records-2-Cr-Ne.md.
- `SubscriptionResponse` → `Subscription (subscription): Subscription?` (**nullable envelope field**). Source: records-4-Su-We.md.
- `Subscription` fields read: `State (state): SubscriptionState?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`,
  `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`,
  `ProductPriceInCents (product_price_in_cents): long?`, `Product (product): Product?`,
  `Customer (customer): Customer?`, `Reference (reference): string?`. Source: records-4-Su-We.md.
- `ProductResponse` → `Product (product): Product !req`. Source: records-3-Of-Su.md.
- `Product` fields read: `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`,
  `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`,
  `Id (id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`. Source: records-3-Of-Su.md.
- `ProductFamilyResponse` → `ProductFamily (product_family): ProductFamily?`; `ProductFamily`:
  `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`. Source: records-3-Of-Su.md.

All the above records live in namespace **`MaxioAdvancedBilling.Models`** (source: records page headers).

### 2d. Enums

| Enum | Namespace | Member (wire) values | Source |
|---|---|---|---|
| `SubscriptionState` (StringEnum) | `MaxioAdvancedBilling.Models.Enums` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | enums.md |
| `CollectionMethod` (StringEnum) | `MaxioAdvancedBilling.Models.Enums` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | enums.md |
| `IntervalUnit` (StringEnum) | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` | enums.md |

Enums are `StringEnum<T>`, **not** C# enums: build with `SubscriptionState.FromValue("active")` or the
static member `SubscriptionState.Active`; compare via `.Value` when serialising to your API. To read the
state as a string for the response DTO, use the enum's value accessor (confirm the accessor when
constructing DTOs — see `dotnet-models`). Source: `sdk-map.md` model conventions.

### 2e. Error-handling contract (critical for idempotency)

- **Case B ops** (`ReadCustomerByReference`, `ListCustomerSubscriptions`, `ReadProductFamily`): catch
  `SdkException<RawError>`; read `ex.Error.StatusCode` (an `HttpStatusCode`). **A 404 on
  `ReadCustomerByReference` is the "customer does not exist" signal** — branch to CreateCustomer. Any
  other status (401/422/5xx) is a real error, not the not-found signal. Body via `ex.Error.ReadAsString()`
  or `ex.Error.ReadAsJson<T>()`.
- **Case A ops** (`CreateCustomer`, `CreateSubscription`, `ListProductsForProductFamily`): catch the
  typed `SdkException<{Op}Error>`; call the named `TryGet…` accessor for the mapped status, else
  `TryGetRawError(out RawError)` for everything else. `TryGetRawError` is **not** a catch-all layered on
  top of the typed accessor — it is the explicit fallback branch.
- Namespaces: `SdkException<T>` ⇒ `MaxioAdvancedBilling.Core.Exceptions` (source `Core/Exceptions/SdkException.cs`);
  `RawError` ⇒ `MaxioAdvancedBilling.Core.ErrorResponse` (source `Core/ErrorResponse/RawError.cs`); the typed
  `{Op}Error` classes and payload records' `TryGet…` accessors live under `MaxioAdvancedBilling.Errors`
  and `MaxioAdvancedBilling.Models` respectively. Source: `sdk-map.md` error-core table.

---

## 3. Trap notes (load the named skill before writing that step)

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client must be
> long-lived and reused (via `IHttpClientFactory`), not rebuilt per request; how the SDK client
> wrapper's own lifetime should be registered is not visible in the signature. **MUST load
> `dotnet-client-initialization`** before wiring the client into DI.

> ⚠ Step 1 (auth) — whether credentials are set before constructing the client or in the DI callback,
> and where the API key should be loaded from, is a usage decision the `BasicAuthCredentials` shape does
> not show. **MUST load `dotnet-authentication`** before setting credentials.

> ⚠ Step 1 (resilience / base URL) — the SDK's `Retry`/`Timeout` options do **not** bound a whole call
> and are **not** the timeout on the `HttpClient` you register; and whether a failed non-idempotent
> write (`CreateCustomer`/`CreateSubscription`) can be silently re-sent by the retry layer is not
> visible in the option names. This bears directly on the double-click idempotency requirement. **MUST
> load `dotnet-configuration-resilience`** before wiring retries/timeouts/base-URL.

> ⚠ Steps 2–4 (list/search calls) — `ListProductsForProductFamily` and `ListCustomerSubscriptions`
> have many optional parameters with no C# default; a positional call mis-binds. **MUST load
> `dotnet-calling-endpoints`** before the first call, and pass filters as **named arguments**.

> ⚠ Steps 2–4 (models) — enums are `StringEnum<T>` (not C# enums), union fields are read via `TryGet…`,
> and JSON fields the SDK does not model are dropped on deserialize; reading `SubscriptionState`/
> `IntervalUnit` as strings for your DTOs needs the value accessor, not a cast. **MUST load
> `dotnet-models`** before constructing request payloads or mapping SDK models to eShop DTOs.

> ⚠ Step 3 (error boundary + idempotency) — distinguishing "customer not found" (404 on a Case B op)
> from a genuine failure, and reading typed vs raw errors correctly, is the crux of the idempotency
> logic. **MUST load `dotnet-error-handling`** before writing the boundary. See the two `JsonException`
> hazard rows in Required Reading — they apply directly to `CreateCustomer`'s 422 shape (below).

> ⚠ Step 3 (CreateCustomer 422 payload — trust concern, map-visible) — the two validation-error models
> in scope **disagree**: `CreateSubscription`'s `ErrorListResponse1.Errors` is a clean
> `IReadOnlyList<string>` of messages, but `CreateCustomer`'s `CustomerErrorResponse1.Errors` is an
> `Errors` object whose only fields are `PerPage (per_page): IReadOnlyList<string>?` and
> `PricePoint (price_point): IReadOnlyList<string>?` (source: records-2-Cr-Ne.md `Errors`). That shape
> does not look like a customer-validation payload (e.g. a duplicate-`reference` 422), so
> `TryGetCustomerErrorResponse1` may yield an object with no usable message, and if the live 422 body
> is instead a `{"errors":[…]}` array it will **not** deserialize into `CustomerErrorResponse1` at all
> — throwing `JsonException` while the error object is constructed (see Required Reading hazard #2).
> **Directive (defensive):** on `CreateCustomer` 422, extract a message best-effort from
> `CustomerErrorResponse1`; if it is empty, fall back to `TryGetRawError` → `ReadAsString()` for the
> message, and preserve the 422 status. Label: `UNVERIFIED` — only live traffic confirms the actual
> 422 wire shape. Do not map this deterministic 422 to a 5xx.

---

## 4. REQUIRED READING (load ALL before implementation starts)

These `dotnet-*` companion skills carry the defaults, worked examples, and traps this sheet deliberately
does **not** restate. Load each before writing the step it governs.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — setting Basic credentials, loading the key from config |
| `dotnet-configuration-resilience` | Step 1 — retries/backoff, what `Timeout` bounds, base-URL/server selection, whether writes can be resent |
| `dotnet-calling-endpoints` | Steps 2–4 — named-argument calls, request/response envelopes, async + `ct` |
| `dotnet-models` | Steps 2–4 — `StringEnum<T>`, unions via `TryGet…`, required members, wire vs C# names |
| `dotnet-error-handling` | Step 3 — which exceptions reach the catch, reading status/body safely, the boundary |
| `dotnet-testing` | Tests — the `HttpClient` constructor arg is the test seam; match the project's framework |

**Two `System.Text.Json.JsonException` hazards that reach the error boundary — they need opposite
handling and must shape the boundary from the start:**

- A drifted or malformed **2xx** body (e.g. a missing `required` member such as
  `ProductResponse.Product` or `ErrorListResponse1.Errors`) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it escape the
  integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape (the
  `CreateCustomer` 422 concern above is exactly this) throws `JsonException` **while the error object is
  being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is
  destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic
  rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
1. **Stable customer reference:** idempotency relies on writing a stable per-shopper identifier (eShop
   user id or email) into Maxio `Customer.reference`. The SDK exposes no server-side "create-or-get";
   the create-or-lookup is assembled client-side: `ReadCustomerByReference` (404 ⇒ not found) then
   `CreateCustomer`. Assumed acceptable. A concurrent double-click can still race between the lookup
   404 and the create — mitigate by treating the CreateCustomer 422 "reference already taken" as
   "already exists" and re-reading by reference (this is why the CreateCustomer 422 message extraction
   in the trap note matters).
2. **Subscription idempotency:** there is no native "one subscription per customer+product" guard;
   before `CreateSubscription`, list the customer's subscriptions (`ListCustomerSubscriptions`) and skip
   creation if an `Active`/`Trialing`/`Pending` subscription already exists for the target product
   handle. Assumed the intended guard.
3. **Payment-not-required plans:** the target plans have `request_credit_card = false`, so
   `CreateSubscription` is called with **no** payment attributes and no 3-DS flow. Confirmed consistent
   with the `CreateSubscription` model (payment attributes are all optional). Product-side
   `request_credit_card` is a sandbox/config fact, not an SDK contract fact.
4. **Plans endpoint** resolves the family by handle using the `"handle:<ProductFamilyHandle>"` form of
   the `productFamilyId` string on `ListProductsForProductFamily` (the map notes the family may be
   addressed as `handle:my-family`). If that form is rejected at runtime, resolve the numeric id first
   via a family lookup, then pass the numeric id.
5. **Next-billing date** is mapped from `NextAssessmentAt (next_assessment_at)` (the model has no
   `next_billing_at` field); `current_period_ends_at` supplies the current-period end. Labeled
   `UNVERIFIED` as to which the product surfaces as the displayed "next billing".

**Blockers**
- None that block planning. Two items are `UNVERIFIED` (only live traffic can confirm) and are handled
  as defensive directives on the sheet, not open lookups: (a) the actual `CreateCustomer` 422 wire shape
  vs the generated `CustomerErrorResponse1`/`Errors` model; (b) which date field the product treats as
  the displayed "next billing". No in-scope capability is missing from the SDK map.
