# Maxio Advanced Billing — "Subscribe" hero flow: plan + CONTRACT SHEET

Integration target: eShopOnWeb (ASP.NET Core, C#/.NET). Three HTTP endpoints backed by the
Maxio Advanced Billing .NET SDK (`AsadAli.AdvancedBilling.Sdk`, root namespace
`MaxioAdvancedBilling`, client `MaxioAdvancedBillingClient`).

All facts below are grounded in the bundled SDK map (`sdk-map.md` + `map/operations/*` +
`map/models/*`). Each row cites its map page. Install via NuGet only:
`dotnet add package AsadAli.AdvancedBilling.Sdk`.

---

## 1. Scope & sequence

Config binds from a `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`,
`Maxio:ProductFamilyHandle` (`eshop-subscribe`), `Maxio:DefaultPlanHandle` (`eshop-pro`),
`Maxio:BaseUrl` (optional explicit override).

**Step 0 — Client registration & auth.** Register `MaxioAdvancedBillingClient` (see client-
construction facts below). Basic auth: Username = API key, Password = literal `"x"`.

**Step 1 — GET /api/subscription-plans.** Resolve the product-family *handle* → numeric id,
then list that family's products.
- `client.ProductFamilies.ListProductFamilies(...)` → find the `ProductFamilyResponse` whose
  `ProductFamily.Handle == Maxio:ProductFamilyHandle`; take its `ProductFamily.Id`.
- `client.ProductFamilies.ListProductsForProductFamily(id.ToString(), ...)` → map each
  `ProductResponse.Product` to the DTO (handle, name, price-from-cents, interval, product id).

**Step 2 — POST /api/subscriptions (idempotent).**
- **Customer ensure (idempotent read-then-create):** `client.Customers.ReadCustomerByReference(reference)`
  where `reference` = the stable eShopOnWeb user id/email. On success use `Customer.Id`. On the
  not-found `SdkException<RawError>` (404) create via `client.Customers.CreateCustomer(body)`.
  The `reference` field is unique server-side, so a create race yields a 422 rather than a
  duplicate customer.
- **Duplicate-subscription guard:** `client.Customers.ListCustomerSubscriptions(customerId)` →
  if any `Subscription` has matching `Product.Handle` (or `Product.Id`) and a live `State`
  (`Active`/`Trialing`/`Assessing`/`PastDue`/`SoftFailure`), return that one instead of creating.
- **Create (no card):** `client.Subscriptions.CreateSubscription(body)` with `ProductHandle` +
  `CustomerId` + **`PaymentCollectionMethod = CollectionMethod.Remittance`** — omit all payment-profile
  fields. VERIFIED against the live sandbox: the default (automatic) collection attempts an immediate
  card charge and 422s ("No payment method was on file for the $299.00 balance"); `remittance`
  collection issues an invoice instead and creates the subscription with no card. Implementation also
  falls back to `remittance` + `NetTerms="0"`, then `CollectionMethod.Invoice` (legacy Statements sites)
  if the first attempt is rejected.
- Return: plan/product (`Subscription.Product.Handle`/`.Name`), price, `Subscription.State`,
  next billing (`Subscription.CurrentPeriodEndsAt`).

**Step 3 — GET /api/my-subscriptions.** `ReadCustomerByReference` → `ListCustomerSubscriptions(customerId)`
→ map each `Subscription` (product, price, state, `CurrentPeriodStartedAt`/`CurrentPeriodEndsAt`).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C#
> identifier. The cancellation-token parameter really is named `ct`: in named arguments write
> `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each
> one from that type's own map row, never from where a neighbouring type sits. A members table
> names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### Namespaces (using-directives) — C# does NOT import child namespaces transitively

| Type(s) | Namespace | `using` |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | root | `using MaxioAdvancedBilling;` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | add its own `using` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | add its own `using` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | only if tuning retries |
| All request/response records (Customer, Subscription, Product, ProductFamily, their `*Response`, `CreateCustomer*`, `CreateSubscription*`) | `MaxioAdvancedBilling.Models` | `using MaxioAdvancedBilling.Models;` |
| Enums (`SubscriptionState`, `IntervalUnit`, `CollectionMethod`) | `MaxioAdvancedBilling.Models.Enums` | `using MaxioAdvancedBilling.Models.Enums;` |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) + payloads (`CustomerErrorResponse1`, `ErrorListResponse1`) | `MaxioAdvancedBilling.Errors` (payload records: `.Models`) | add `using MaxioAdvancedBilling.Errors;` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` | add its own `using` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | add its own `using` |

(Source rows: `sdk-map.md` namespaces table + Getting-a-client block.)

### Client construction & auth  (source: `sdk-map.md` "Getting a client" / "Servers & auth")

- Only constructor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- DI: `services.AddMaxioAdvancedBillingClient(o => { ... })` (extension defined in root file
  `ServiceCollectionExtensions.cs` ⇒ root namespace `MaxioAdvancedBilling`). **UNVERIFIED:**
  ServiceCollection extension methods commonly also sit under
  `Microsoft.Extensions.DependencyInjection` — if the `AddMaxioAdvancedBillingClient` name is not
  resolved with the root `using`, add `using Microsoft.Extensions.DependencyInjection;`. Confirm
  the exact namespace and the HttpClient lifetime wiring via `dotnet-client-initialization`.
- `MaxioAdvancedBillingClientOptions` members: `Environment: ServerEnvironment`,
  `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?`.
- **Auth:** `o.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" };`
  — Username = API key, Password = the literal string `"x"`.
- **Environment / base URL:**
  - `o.Environment = ServerEnvironment.Us` (default) → `https://{site}.chargify.com`;
    `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`.
  - `{site}` defaults to subdomain — set `o.Server.Production.Us.Site = <Maxio:Subdomain>`
    (site `apimatic-hackathon`).
  - **Explicit base-URL override (the gotcha):** when `Maxio:BaseUrl` is set, override the group
    URL directly: `o.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>;` (this replaces the
    subdomain-derived URL). Use the `.Eu.*` chain instead if `Environment = Eu`. Sources:
    `Server.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`.

### Operations

| Op (controller.method) | Signature (params in order, types; required-but-nullable ⇒ pass explicitly) | Request model + fields used | Response envelope → inner fields read | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — first 5 nullable, no default ⇒ pass `null` | none | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` (nullable): `Handle`, `Id (int?)`, `Name` | **Case B** `SdkException<RawError>`: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — params 2–9 nullable, no default ⇒ pass `null`; `productFamilyId` is the **numeric family id as a string** (`id.ToString()`) | none | `IReadOnlyList<ProductResponse>`; each `.Product !req` (see Product fields below) | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 20) | `operations/ProductFamilies.md` |
| `client.Customers.ReadCustomerByReference` | `(string reference, CancellationToken ct = default)` | none (query `reference`) | `CustomerResponse` → `.Customer !req` (see Customer fields) | **Case B** `SdkException<RawError>` — not-found is a `RawError` with `.StatusCode == 404`; catch and branch to create | none | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `(CreateCustomerRequest? body, CancellationToken ct = default)` — body nullable, no default ⇒ pass explicitly | `CreateCustomerRequest { Customer: CreateCustomer !req }`; `CreateCustomer` required: `FirstName`, `LastName`, `Email` (all `string !req`); set `Reference` (stable user id/email) | `CustomerResponse` → `.Customer.Id (int?)` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| `client.Customers.ListCustomerSubscriptions` | `(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` (nullable) → `State`, `Product.Handle`, `Product.Id` | **Case B** `SdkException<RawError>`: `.StatusCode`, `.ReadAsString()` | none | `operations/Customers.md` |
| `client.Subscriptions.CreateSubscription` | `(CreateSubscriptionRequest? body, CancellationToken ct = default)` — body nullable, no default ⇒ pass explicitly | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }`; on `CreateSubscription` set only `ProductHandle (string?)` + `CustomerId (int?)` — **omit all payment fields** (`PaymentProfileId`, `CreditCardAttributes`, `PaymentProfileAttributes`, `BankAccountAttributes`) | `SubscriptionResponse` → `.Subscription` (see fields) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

### Request models (fields = `Name (wire_name): type, required?`)

- **`CreateCustomerRequest`** (`records-1-Ac-Cr.md`): `Customer (customer): CreateCustomer, required`.
- **`CreateCustomer`** (`records-1-Ac-Cr.md`): `FirstName (first_name): string, REQUIRED` · `LastName (last_name): string, REQUIRED` · `Email (email): string, REQUIRED` · `Reference (reference): string?, optional` · plus optional `Organization`, address fields, `Phone`, `Locale`, `VatNumber`, `TaxExempt (bool?)`, `ParentId`, `SalesforceId`.
- **`CreateSubscriptionRequest`** (`records-2-Cr-Ne.md`): `Subscription (subscription): CreateSubscription, required`.
- **`CreateSubscription`** (`records-2-Cr-Ne.md`) — fields relevant to no-payment hero flow:
  `ProductHandle (product_handle): string?` · `ProductId (product_id): int?` ·
  `CustomerId (customer_id): int?` · `CustomerReference (customer_reference): string?` ·
  `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` ·
  `PaymentProfileId (payment_profile_id): int?` (LEAVE UNSET) ·
  `CustomerAttributes (customer_attributes): CustomerAttributes?` (only if creating customer inline — not used; we create the customer separately). No field is `required` on `CreateSubscription` itself; identity comes from `ProductHandle` + `CustomerId`.

### Response models — envelopes wrap their payload one level down

- **`ProductFamilyResponse`** (`records-3-Of-Su.md`): `ProductFamily (product_family): ProductFamily?` (**nullable** — null-check).
- **`ProductFamily`** (`records-3-Of-Su.md`): `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `Description`, `AccountingCode`, timestamps.
- **`ProductResponse`** (`records-3-Of-Su.md`): `Product (product): Product !req`.
- **`Product`** (`records-3-Of-Su.md`) — fields for the plan DTO: `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `PriceInCents (price_in_cents): long?` (**cents → format /100.0**) · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` (enum Day/Month) · `Description (description): string?` · `ProductFamily (product_family): ProductFamily?`.
- **`CustomerResponse`** (`records-2-Cr-Ne.md`): `Customer (customer): Customer !req`.
- **`Customer`** (`records-2-Cr-Ne.md`): `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName`/`LastName`/`Organization`.
- **`SubscriptionResponse`** (`records-4-Su-We.md`): `Subscription (subscription): Subscription?` (**nullable** — null-check).
- **`Subscription`** (`records-3-Of-Su.md`) — fields returned to the user:
  `Id (id): int?` · `State (state): SubscriptionState?` · `Product (product): Product?` (→ `.Handle`, `.Name`, `.PriceInCents`) · `ProductPriceInCents (product_price_in_cents): long?` · `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` · `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (**this is the "next billing date"** — per the UpdateSubscription note, `next_billing_at` is NOT returned; read `current_period_ends_at`) · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (mirrors the period end for active subs).

### Enums needed  (source: `map/models/enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>`, NOT C# enums — compare with the static members, e.g. `SubscriptionState.Active`)

| Enum | C# member (wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |

For the duplicate-active guard, treat these `SubscriptionState` values as "live/occupies the
plan": `Active`, `Trialing`, `Assessing`, `PastDue`, `SoftFailure`, `Paused`, `OnHold`. Treat
`Canceled`, `Expired`, `Unpaid`, `TrialEnded`, `FailedToCreate` as free-to-resubscribe.

### Error-payload record shapes (for reading typed errors)

- `CustomerErrorResponse1` (`records-2-Cr-Ne.md`): `Errors (errors): Errors?` — `Errors` record has `PerPage`, `PricePoint` (both `IReadOnlyList<string>?`). Best-effort extract; fall back to `TryGetRawError` + `ReadAsString()`.
- `ErrorListResponse1` (`records-2-Cr-Ne.md`): `Errors (errors): IReadOnlyList<string> !req` — join the message list for display.

---

## 3. Trap notes (load the named skill at that step)

> ⚠ Step 0 (client registration) — the SDK client wraps an `HttpClient` whose handler pipeline
> must be long-lived/reused (not rebuilt per request), and the DI extension's lifetime + exact
> namespace are not visible from the signature. **MUST load `dotnet-client-initialization`**
> before wiring the client.

> ⚠ Step 0 (auth) — Basic auth credentials must be set at construction/DI-callback time, not
> mutated per call; load the key from config, never hardcode. **MUST load `dotnet-authentication`.**

> ⚠ Steps 1–3 (every list/search call) — `ListProductFamilies`,
> `ListProductsForProductFamily`, and `ListSubscriptions` have many nullable params with **no
> C# default**; a positional call mis-binds and a named call is mandatory for the ones you skip.
> **MUST load `dotnet-calling-endpoints`.**

> ⚠ Step 1 (family handle → id) — `ListProductsForProductFamily` takes `productFamilyId` as a
> **string**, and whether the wire accepts a `handle:eshop-subscribe` shorthand in that slot is
> not settle­able from the SDK (the SDK only substitutes the string into the path). Resolve the
> handle to a numeric id via `ListProductFamilies` first and pass `id.ToString()`. `UNVERIFIED`:
> the `handle:` shorthand may work live, but the deterministic two-step is the safe path — do not
> depend on the shorthand.

> ⚠ Step 2 (idempotency / retries) — the SDK's retry policy does **not** re-send a `POST` on a
> status trigger, but a **transport failure** on any verb (incl. the `CreateCustomer` /
> `CreateSubscription` POSTs) *is* retried and can execute more than once, and no setting
> disables that. This is why the customer `reference` uniqueness and the
> `ListCustomerSubscriptions` duplicate-guard both matter. **MUST load
> `dotnet-configuration-resilience`** before tuning retries/timeouts/base-URL.

> ⚠ Steps 1–3 (models) — enums are `StringEnum<T>`, not C# enums (compare via
> `SubscriptionState.Active`, not `== "active"`); response envelopes and nested objects
> (`ProductFamilyResponse.ProductFamily`, `SubscriptionResponse.Subscription`, `Subscription.Product`)
> are nullable and JSON fields the model doesn't declare are dropped on deserialize. **MUST load
> `dotnet-models`** before mapping SDK models to DTOs.

> ⚠ All steps (error boundary) — mixed Case A (typed `{Op}Error`) and Case B
> (`SdkException<RawError>`) across the operations in scope; there is no no-throw variant, so
> every call must be wrapped. **MUST load `dotnet-error-handling`** — see REQUIRED READING.

> ⚠ Testing — the `HttpClient` constructor argument is the fake seam; match eShopOnWeb's existing
> test framework. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately does not carry their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 0 — Basic-auth credential wiring |
| `dotnet-calling-endpoints` | Steps 1–3 — named-argument calls, async/cancellation |
| `dotnet-models` | Steps 1–3 — building requests, `StringEnum`, nullable envelopes, wire names |
| `dotnet-configuration-resilience` | Step 0/2 — retries, timeouts, base-URL/server override, pagination |
| `dotnet-error-handling` | All steps — the exception boundary (Case A/B, status/body reading) |
| `dotnet-testing` | Tests — faking the HttpClient seam |

**Two `System.Text.Json.JsonException` hazards the error boundary MUST handle — a `JsonException`
reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only
  catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps
  every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller
  that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Assumption (customer reference).** The idempotency key is the eShopOnWeb user's stable id (or
  email) written to Maxio `Customer.reference`. The brief says "user id / email" — pick ONE stable
  value and use it consistently for both `ReadCustomerByReference` and `CreateCustomer.Reference`;
  mixing them breaks idempotency. `CreateCustomer` requires `FirstName`, `LastName`, `Email` — if
  the eShopOnWeb identity lacks first/last name, supply safe placeholders (e.g. derive from email)
  so the required fields are satisfied.
- **Assumption (price formatting).** "price formatted from cents" = `PriceInCents / 100m` rendered
  as currency; currency code is not on `Product` in the fields needed here — assume the site
  default (USD, per sandbox `$` amounts). If a specific currency must be shown, it is not in the
  Product envelope fields listed and would need a separate lookup — flag if required.
- **Assumption (default plan).** POST body plan identifier defaults to `Maxio:DefaultPlanHandle`
  (`eshop-pro`) when the request omits it.
- **RESOLVED (was UNVERIFIED, live-only).** A bare no-payment create does NOT succeed against this
  product config — automatic collection requires a card (422). The subscription is created without a
  card by setting `PaymentCollectionMethod = CollectionMethod.Remittance` (invoice the balance). The
  implementation still surfaces `CreateSubscription` 422s best-effort (`TryGetErrorListResponse1`, else
  `TryGetRawError().ReadAsString()`) and degrades across collection strategies, so it does not assume
  the first attempt always succeeds.
- **No blocking gaps.** Every capability the three endpoints need is exposed by the SDK:
  family-by-handle resolution (`ListProductFamilies`), product listing
  (`ListProductsForProductFamily`), idempotent customer lookup (`ReadCustomerByReference`),
  customer create (`CreateCustomer`), a customer's subscriptions (`ListCustomerSubscriptions`),
  and no-payment subscription create (`CreateSubscription`). Nothing was invented.
