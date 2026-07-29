# Maxio Advanced Billing — Recurring Subscription Billing Plan (PublicApi)

Adds three endpoints to the ASP.NET Core **PublicApi** project, with Maxio Advanced Billing
as the system of record. Everything below is grounded in the bundled SDK map (pages cited per
row). Load the companion `dotnet-*` skills in the REQUIRED READING block **before** writing code.

## SDK identity (from `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (install by this id) |
| Version / source tag | `v1.0.2` (commit `15db14b`) |
| Root namespace (the `using`) | `MaxioAdvancedBilling` |
| Client class | `MaxioAdvancedBillingClient` |
| Options class | `MaxioAdvancedBillingClientOptions` |
| Target framework | `netstandard2.0` |

---

## 1. Scope & sequence

| Step | Endpoint | SDK operations used |
|---|---|---|
| 0 | Client construction + DI | `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient(httpClient, options)` |
| 1 | `GET /api/subscription-plans` | `ProductFamilies.ListProductFamilies` (resolve handle→id) → `ProductFamilies.ListProductsForProductFamily` |
| 2a | `POST /api/subscriptions` — ensure customer | `Customers.ReadCustomerByReference` → (if absent) `Customers.CreateCustomer` |
| 2b | `POST /api/subscriptions` — idempotency guard | `Customers.ListCustomerSubscriptions` |
| 2c | `POST /api/subscriptions` — create | `Subscriptions.CreateSubscription` |
| 3 | `GET /api/my-subscriptions` | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |

**Idempotency design (Step 2):** the guard is the customer `reference` field (the eShopOnWeb
stable user id/email). Look up by reference; create the customer only on a confirmed 404. Then
list that customer's subscriptions and skip create if a live subscription to the same product
already exists. This guard is also the mitigation for the transport-retry write hazard noted in
the trap notes below.

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

### Namespaces (`using` directives)

| Type(s) | Namespace | Source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | `sdk-map.md` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` |
| All records (`Product`, `ProductResponse`, `Customer`, `CustomerResponse`, `CreateCustomer`, `CreateCustomerRequest`, `CreateSubscription`, `CreateSubscriptionRequest`, `Subscription`, `SubscriptionResponse`, `ProductFamily`, `ProductFamilyResponse`, `CustomerErrorResponse1`, `ErrorListResponse1`, `Errors`) | `MaxioAdvancedBilling.Models` | `records-*.md` |
| Enums (`SubscriptionState`, `IntervalUnit`, `SubscriptionStateFilter`, `CollectionMethod`, `ExpirationIntervalUnit`) | `MaxioAdvancedBilling.Models.Enums` | `enums.md` |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` | `sdk-map.md` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` (derived from source path `Core/Exceptions/SdkException.cs`) | `sdk-map.md` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` (derived from source path `Core/ErrorResponse/RawError.cs`) | `sdk-map.md` |

### Client construction, auth, base URL/subdomain (from `sdk-map.md` — Getting a client / Servers & auth)

- **Constructor (only one):** `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- **Auth (Basic only):** `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. Username = the API key; Password = the literal string `"x"`.
- **Environment:** `options.Environment = ServerEnvironment.Us` (default; `.Eu` only for EU-hosted accounts). US host template `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com`.
- **Subdomain:** `options.Server.Production.Us.Site = <Maxio:Subdomain>` (sets `{site}` in the template).
- **Base-URL override (`Maxio:BaseUrl`, optional):** when set, use verbatim — `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`; when unset, leave `BaseUrl` alone and rely on `Site` + environment template.
- **DI shape:** `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Environment = …; o.Server.Production.Us.Site = …; /* o.Server.Production.Us.BaseUrl = … if override */ });` (source `ServiceCollectionExtensions.cs`).
- **HttpClient ownership/lifetime:** see the client-initialization trap note — the HttpClient/handler pipeline is long-lived and must come from `IHttpClientFactory`, not be rebuilt per request.

### Operations table

Cancellation token param is `ct` on every operation. "must pass" = nullable param with no C# default → pass explicitly (`null` to skip).

| # | Controller.Method (signature, params in order) | Request model + fields | Response envelope → inner fields read | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|
| 1a | `ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable must-pass | none | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` (`ProductFamily?`) → `.Id (id): int?`, `.Handle (handle): string?` | Case B `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/ProductFamilies.md` |
| 1b | `ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass `productFamilyId` = resolved numeric id as string; 8 nullable must-pass; `page`/`perPage` default 1/20 | none | `IReadOnlyList<ProductResponse>`; each `.Product` (`Product !req`) → `.Handle (handle): string?`, `.Name (name): string?`, `.Description (description): string?`, `.PriceInCents (price_in_cents): long?`, `.Interval (interval): int?`, `.IntervalUnit (interval_unit): IntervalUnit?` | Case A `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 2a | `Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` ← `reference` | none | `CustomerResponse` → `.Customer (customer): Customer !req` → `.Id (id): int?`, `.Reference (reference): string?` | Case B `SdkException<RawError>` — 404 ⇒ customer absent (see error section) | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| 2a' | `Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | `CreateCustomerRequest.Customer (customer): CreateCustomer !req` → `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set = eShop user id for idempotency), rest optional | `CustomerResponse` → `.Customer` → `.Id` | Case A `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 2b/3b | `Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription (subscription): Subscription?` → fields per Subscription row below | Case B `SdkException<RawError>` | none (returns all) | `operations/Customers.md`, `records-3-Of-Su.md` |
| 2c | `Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req` → `CreateSubscription` (all fields optional): set `ProductHandle (product_handle): string?` = chosen product handle **and** `CustomerId (customer_id): int?` = resolved customer id (or `CustomerReference (customer_reference): string?` = reference), **and** `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` = `CollectionMethod.Remittance` (bill by invoice/remittance, no card). See "Billing without a card" note below — the default is `Automatic` (auto-collect), which 422s with no card on file even for a "payment-method-not-required" product. No credit-card/payment-profile fields (`CreditCardAttributes`, `PaymentProfileId`, etc.) are needed. | `SubscriptionResponse` → `.Subscription` → confirm fields below | Case A `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `enums.md` |

**Subscription confirmation/display fields** (`records-3-Of-Su.md`, `Models/Subscription.cs`) — read via `SubscriptionResponse.Subscription`:

| Display | Accessor | Type |
|---|---|---|
| State | `.State (state)` | `SubscriptionState?` (enum below) |
| Plan/product (name, handle) | `.Product (product): Product?` → `.Name`, `.Handle`, `.Description` | nested `Product` |
| Price (per period, subscription level) | `.CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` (or `.ProductPriceInCents (product_price_in_cents): long?`) | `long?` (cents) |
| Next billing date | `.NextAssessmentAt (next_assessment_at): DateTimeOffset?` (primary); fall back to `.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` | `DateTimeOffset?` |
| Reference (for correlation) | `.Reference (reference): string?` | `string?` |

Note on next-billing: the `UpdateSubscription` map notes state the API does **not** echo a `next_billing_at`; the readable field is `current_period_ends_at`. Prefer `NextAssessmentAt`, then `CurrentPeriodEndsAt`.

### Resolving product-family handle → id (Step 1)

`ListProductsForProductFamily` takes `productFamilyId` as a **string**, and `ReadProductFamily` only accepts `int id`. Deterministic resolution (no live uncertainty): call `ListProductFamilies(null, null, null, null, null, ct)`, find the `ProductFamilyResponse` whose `.ProductFamily.Handle == <Maxio:ProductFamilyHandle>`, take `.ProductFamily.Id` (int), and pass `id.Value.ToString()` as `productFamilyId`.
- **UNVERIFIED (live-only) shortcut:** the `ReadProductFamily` map note says a family may be addressed as `handle:my-family`; the `product_family_id` path param being typed `string` is consistent with accepting a `handle:eshop-subscribe` value directly on `ListProductsForProductFamily` too, but the map does not confirm the `handle:` prefix is honored on *this* endpoint. Defensive directive: use the deterministic ListProductFamilies→Id resolution as the code path; only if you choose the `handle:` shortcut, on a 404 from `ListProductsForProductFamily` fall back to the deterministic resolution. Do not rely on the shortcut unconfirmed.

### Enums we touch (`enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`)

`StringEnum<T>` — NOT C# enums. Compare/read via static members (`SubscriptionState.Active`) or `Type.FromValue("wire")`. Wire value in parens.

**`SubscriptionState`** — `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.
- Idempotency "already has a live subscription" set (treat as existing): `Active`, `Trialing`, `Assessing`, `Pending`, `SoftFailure`, `PastDue`, `Suspended`, `OnHold`, `AwaitingSignup`, `Unpaid`, `Paused`. Terminal/ignorable: `Canceled`, `Expired`, `TrialEnded`, `FailedToCreate`. (Business decision — confirm intent in Assumptions.)

**`IntervalUnit`** (product/subscription period unit) — `Day (day)`, `Month (month)`.

**`CollectionMethod`** (`Subscription.PaymentCollectionMethod` / `CreateSubscription.PaymentCollectionMethod`, wire `payment_collection_method`) — `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. Enum doc: legacy Statements sites → `invoice`/`automatic`; current Relationship-Invoicing sites → `remittance`/`automatic`/`prepaid`.

**Billing without a card (Step 2c — required to create a no-payment-method subscription):** the default collection method is `Automatic`, which makes Maxio try to auto-collect the first balance and returns HTTP 422 ("No payment method was on file for the $X balance") when no card/payment profile exists — even for products flagged payment-method-not-required, because they still generate an immediate balance. Fix: set `CreateSubscription.PaymentCollectionMethod = CollectionMethod.Remittance` (Relationship-Invoicing sites) or `CollectionMethod.Invoice` (legacy Statements sites) so the subscription bills by invoice/remittance with no card capture. Per the map, `CreateSubscription` has **no** `!req` members, so setting the collection method alone is contract-sufficient. `NetTerms (net_terms): string?` is optional (note: a **string**, e.g. `"30"`). `UNVERIFIED` (live-only): whether the site's server-side rules additionally require `NetTerms` for a remittance/invoice subscription is not settleable from the map — defensive directive: submit with only `PaymentCollectionMethod` first; if a 422 about net-terms comes back, also set `NetTerms` and retry.

**`SubscriptionStateFilter`** (only if you later filter `Subscriptions.ListSubscriptions`) — `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`.

### Error handling — status/not-found reading

- **Two cases (from `sdk-map.md` error model):** Case A ops throw `SdkException<{Op}Error>` with typed `TryGet…` accessors + inherited `TryGetRawError(out RawError)`; Case B ops throw `SdkException<RawError>` (`StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). No no-throw variants exist — every call must be wrapped.
- **Not-found vs real failure for the idempotency lookup (`ReadCustomerByReference`, Case B):** `CustomerResponse.Customer` is `required`, so any 2xx always carries a customer — absence therefore surfaces as a non-2xx. Directive: catch `SdkException<RawError>`; if `ex.Error.StatusCode == HttpStatusCode.NotFound` treat the customer as **absent** (proceed to create); any other status is a **real failure** — rethrow/surface. `UNVERIFIED` (live-only): that "absent" maps to exactly `404` on this endpoint is standard-but-unconfirmed by the map; code the `== NotFound` check and let non-404 statuses propagate as failures so a real error is never silently read as "absent".
- **CreateCustomer 422 body (`CustomerErrorResponse1`):** its only field is `Errors (errors): Errors?`, and that `Errors` record (`Models/Errors.cs`) carries only `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` — it does **not** expose a general validation-message list. Trust judgment (map evidence): extracting a human-readable customer-validation message from this typed accessor is unreliable. `UNVERIFIED` (live-only): whether the live 422 customer body actually matches this generated shape can only be confirmed by traffic. Defensive directive: try `TryGetCustomerErrorResponse1`, best-effort read those two lists, and **fall back to `TryGetRawError(out raw)` → `raw.ReadAsString()`** for the message; never assume a populated message field.
- **CreateSubscription 422 body (`ErrorListResponse1`):** `Errors (errors): IReadOnlyList<string> !req` — a clean message list; surface it to the caller.

---

## 3. Trap notes (load the named skill before that step)

> ⚠ Step 0 (client construction/DI) — whether the `HttpClient`/handler pipeline may be rebuilt per request or must be long-lived and factory-owned, and whether the SDK client wrapper is transient vs singleton, is not visible in the constructor signature. **MUST load `dotnet-client-initialization`** before wiring the client/DI.
>
> ⚠ Step 0 (auth) — when and where credentials must be set relative to client construction, and loading the key from config vs hardcoding. **MUST load `dotnet-authentication`** before setting `BasicAuth`.
>
> ⚠ Steps 1/2b (list & lookup calls) — the many nullable, no-default params bind by position and mis-bind in a positional call; whether these must be passed by name. **MUST load `dotnet-calling-endpoints`** before the first `client.X.Y(...)` call.
>
> ⚠ Steps 1–3 (models) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>` (not C# enums), `CreateSubscription.OfferId` is a union, and unmodeled JSON fields are dropped on deserialize; how to build required members and read enums/unions is not shown by the field list. **MUST load `dotnet-models`** before constructing request bodies or mapping responses.
>
> ⚠ Step 0 / all steps (base URL, retries, timeouts, pagination) — the retry/timeout options do **not** bound a whole call, `Timeout` is per-attempt not total, and which calls actually retry (and on which verbs) is not visible in the option names; base-URL/subdomain override precedence likewise. **MUST load `dotnet-configuration-resilience`** before tuning the client.
>
> ⚠ Step 2c (create is a non-idempotent POST) — whether a failed write can be silently re-sent by the SDK's transport-failure retry (which can double-create a customer/subscription) is a resilience contract the signature hides; this is why the reference-based guard exists. **MUST load `dotnet-configuration-resilience`** before relying on the guard, and confirm the retry behavior for POST there.
>
> ⚠ All steps (error boundary) — which exception types actually reach your catch blocks and how a drifted body behaves. **MUST load `dotnet-error-handling`** before writing any try/catch (see mandatory rows in REQUIRED READING).
>
> ⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

The contract sheet deliberately does **not** carry these skills' contents (defaults, worked
examples, semantics you must still wire yourself). Load each before the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, options/builder shape, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 0 — supplying Basic credentials, where/when to set them |
| `dotnet-calling-endpoints` | Steps 1–3 — named-argument calling, required vs optional params, response envelopes |
| `dotnet-models` | Steps 1–3 — building request models, required members, `StringEnum<T>`, unions, wire names |
| `dotnet-configuration-resilience` | Step 0 & Step 2c — base-URL/subdomain selection, retry/timeout semantics, transport-retry-on-POST idempotency hazard, pagination |
| `dotnet-error-handling` | All steps — the error boundary (Case A/B, status/body reading, not-found detection) |
| `dotnet-testing` | Tests — SDK seam, error/edge paths |

**Mandatory error-boundary hazard rows (`System.Text.Json.JsonException` reaches the boundary from two directions — opposite handling):**
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
- The eShopOnWeb user's stable id/email is used verbatim as the Maxio customer `reference`; this is the sole idempotency key for the customer.
- `CreateCustomer` requires `FirstName`, `LastName`, `Email` (`!req`). The eShopOnWeb user must supply first/last name and email; if the app only has an email, a placeholder first/last name policy must be decided by the implementer (not an SDK constraint).
- "No payment method required" means the configured products in family `Maxio:ProductFamilyHandle` have `request_credit_card`/`require_credit_card` false, so `CreateSubscription` succeeds with only `ProductHandle` + customer identity and no payment attributes. This is a Maxio product-configuration prerequisite, not an SDK guarantee.
- The idempotency "live subscription" state set (see enum note) treats trialing/past-due/etc. as "exists". If the intended rule is stricter (e.g. only `Active`) or product-scoped only, adjust the guard predicate.
- Step 3 lists the customer's subscriptions via `ListCustomerSubscriptions` (returns all, no pagination). If a customer can have very many subscriptions and paging is required, switch to `Subscriptions.ListSubscriptions` (paginated) — but note it has no customer-id filter param, so client-side filtering by `.Subscription.Customer.Id` would be needed.

**Blockers** — none blocking planning.

**UNVERIFIED (live-traffic-only) items** carried inline above, each with a defensive directive:
1. Whether the `handle:` prefix is honored as `productFamilyId` on `ListProductsForProductFamily` (Step 1) — use deterministic handle→id resolution.
2. Whether customer-not-found on `ReadCustomerByReference` is exactly HTTP 404 (Step 2a) — code `== NotFound` as "absent", propagate other statuses.
3. Whether the live 422 CreateCustomer body matches `CustomerErrorResponse1.Errors` (Step 2a') — best-effort extract, fall back to `TryGetRawError().ReadAsString()`.
