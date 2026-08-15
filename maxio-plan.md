# Maxio Advanced Billing integration — eShopOnWeb PublicApi

Recurring-subscription billing with Maxio Advanced Billing as system of record. Three
JWT-authenticated endpoints on `src/PublicApi`. Every SDK fact below is grounded in the bundled
SDK map (page cited per row). Package: `AsadAli.AdvancedBilling.Sdk`; root namespace
`MaxioAdvancedBilling`; client `MaxioAdvancedBillingClient`.

---

## 1. Scope & sequence

1. **Client/DI + config binding** — register a long-lived `MaxioAdvancedBillingClient` via
   `AddMaxioAdvancedBillingClient`; bind `Maxio:ApiKey`, `Maxio:Subdomain`,
   `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`. Set Basic auth + server (site/base-URL). Uses no
   operations. (`sdk-map.md`)
2. **`GET /api/subscription-plans`** — resolve product-family HANDLE → family id via
   `client.ProductFamilies.ListProductFamilies` (filter by `Handle` in memory), then
   `client.ProductFamilies.ListProductsForProductFamily(id.ToString(), …)`. Map each
   `ProductResponse.Product` to a plan DTO. (`operations/ProductFamilies.md`)
3. **`POST /api/subscriptions`** (idempotent hero flow):
   - **(a) ensure customer** — `client.Customers.ReadCustomerByReference(reference)`; on 404 create
     with `client.Customers.CreateCustomer(body)`. (`operations/Customers.md`)
   - **(b) ensure subscription** — `client.Customers.ListCustomerSubscriptions(customerId)`; if an
     active subscription to the target product handle already exists, reuse it; else
     `client.Subscriptions.CreateSubscription(body)` with NO payment attributes.
     (`operations/Customers.md`, `operations/Subscriptions.md`)
   - **(c)** return plan/price/state/next-billing from the resulting `Subscription`.
4. **`GET /api/my-subscriptions`** — `ReadCustomerByReference` → `ListCustomerSubscriptions(customerId)`;
   map each `Subscription`. On customer-not-found (404) return empty list.
   (`operations/Customers.md`)

**Stable reference:** derive one deterministic string from the JWT identity (e.g. the eShop
username/email) and use it as the Maxio customer `reference` on create AND as the lookup key on
`ReadCustomerByReference` — that is what makes the flow idempotent across double-clicks.

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

### 2.1 Client construction / auth / server (source: `sdk-map.md`)

- **Client type:** `MaxioAdvancedBilling.MaxioAdvancedBillingClient`. Only ctor:
  `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- **Options type:** `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` with properties
  `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`,
  `BasicAuth: BasicAuthCredentials?`.
- **Auth (Basic only):** `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`.
  `Username` = API key; `Password` = the literal string `"x"`.
  Type `BasicAuthCredentials` → namespace `MaxioAdvancedBilling.Core.Authentication.Basic`.
- **Environment:** `options.Environment = ServerEnvironment.Us` (default). Type `ServerEnvironment`
  → namespace `MaxioAdvancedBilling.Servers`. Values: `Us` (`https://{site}.chargify.com`), `Eu`
  (`https://{site}.ebilling.maxio.com`). **There is NO built-in "maxiotest"/sandbox environment**
  (see Assumptions & Blockers).
- **Subdomain:** `options.Server.Production.Us.Site = <Maxio:Subdomain>` → base becomes
  `https://{subdomain}.chargify.com`.
- **Base-URL override (verbatim):** when `Maxio:BaseUrl` is set,
  `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` (used verbatim as the API base address;
  set this INSTEAD of relying on Site). `Server`/`ServerOptions`/`ProductionOptions` are reached
  through the `options.Server` property tree — no extra `using` needed for property access.
- **DI:** `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Server.Production.Us.Site = …; })`
  (`ServiceCollectionExtensions.cs`).

### 2.2 Operations table

| # | Controller.Method (signature, params in order) | Request model + key fields (`Name (wire): type, req?`) | Response envelope → inner fields read | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|
| List families | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — pass all 5 filters `null` | none | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily?`) → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<RawError>` → `.Error.StatusCode`, `.Error.ReadAsString()` | none | `operations/ProductFamilies.md` |
| List products in family | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass `productFamilyId` = numeric family id `.ToString()`; the 8 middle params `null` | none | `IReadOnlyList<ProductResponse>` → `.Product` (`Product !req`) → see Product fields below | **Case A** `SdkException<ListProductsForProductFamilyError>` → `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | `operations/ProductFamilies.md` |
| Find customer by reference | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` = the stable per-user key | none (query `reference`) | `CustomerResponse` → `.Customer` (`Customer !req`) → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | **Case B** `SdkException<RawError>` → `.Error.StatusCode` (detect `HttpStatusCode.NotFound` = no customer) | none | `operations/Customers.md` |
| Create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — must pass `body` explicitly | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set to stable key), `Organization`, `Phone`, address fields all optional | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| List a customer's subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` (`Subscription?`) → see Subscription fields below | **Case B** `SdkException<RawError>` → `.Error.StatusCode` | none | `operations/Customers.md` |
| Create subscription (no payment, invoice-billed) | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — must pass `body` explicitly | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`; `CreateSubscription`: set `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`; set `ProductHandle (product_handle): string?` (prefer handle, default `eshop-pro`); **set `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` = `CollectionMethod.Remittance` (wire `remittance`) so the balance is invoiced, not auto-charged — this is what makes the create succeed with NO card on file** (fall back to `CollectionMethod.Invoice` (wire `invoice`) on the legacy Statements architecture — see Assumptions). Leave ALL of `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` unset → no payment profile. `NetTerms (net_terms): string?` and `ReceivesInvoiceEmails (receives_invoice_emails): string?` are the only other invoice-related fields the model exposes (both optional per map). | `SubscriptionResponse` → `.Subscription` → see Subscription fields below | **Case A** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `enums.md` |

Alternative site-wide lookups (NOT needed for the hero flow, listed for completeness): filter
subscriptions site-wide with `client.Subscriptions.ListSubscriptions(state, product, …)` where
`product` is an int **product id** (not handle); `client.Subscriptions.FindSubscription(reference)`
looks up by **subscription** reference (Case A, `TryGetNoContent` [404]) — not by customer+product,
so it does NOT serve step 3(b). Use `ListCustomerSubscriptions` + in-memory filter instead.

### 2.3 Model field detail (source: records pages)

**`Product`** (`ProductResponse.Product`) — `records-3-Of-Su.md`, namespace `MaxioAdvancedBilling.Models`:
- `Handle (handle): string?` — plan handle
- `Name (name): string?` — display name
- `Description (description): string?`
- `PriceInCents (price_in_cents): long?` — **price is in CENTS** (÷100 for dollars)
- `Interval (interval): int?` — interval count
- `IntervalUnit (interval_unit): IntervalUnit?` — `Day (day)` / `Month (month)`
- `Id (id): int?`, `RequireCreditCard (require_credit_card): bool?`,
  `RequestCreditCard (request_credit_card): bool?` (inspect to confirm no card needed),
  `ProductFamily (product_family): ProductFamily?`

**`ProductFamily`** — `records-3-Of-Su.md`, `MaxioAdvancedBilling.Models`:
`Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`.

**`Customer`** (`CustomerResponse.Customer`) — `records-2-Cr-Ne.md`, `MaxioAdvancedBilling.Models`:
`Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`,
`FirstName (first_name): string?`, `LastName (last_name): string?`,
`Organization (organization): string?`.

**`Subscription`** (`SubscriptionResponse.Subscription`, nullable `?`) — `records-4-Su-We.md`
→ `records-3-Of-Su.md` for the `Subscription` record itself, `MaxioAdvancedBilling.Models`:
- `Id (id): int?`
- `State (state): SubscriptionState?` — enum, values below
- `ProductPriceInCents (product_price_in_cents): long?` — price in **cents**
- `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`
- `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`
- `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` — **use this as the
  next-billing / period-end date**
- `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — secondary next-assessment date
- `Product (product): Product?` — nested product (read `.Handle`, `.Name` for plan info)
- `Customer (customer): Customer?`, `Reference (reference): string?`

> **CONTRACT FACT — there is NO `NextBillingAt` on the `Subscription` response record.**
> `next_billing_at` exists only on the `CreateSubscription` **request** model. On reads, the next
> billing date is `CurrentPeriodEndsAt` (the SDK's own `UpdateSubscription` notes confirm the
> server "will not return data under `next_billing_at`; view `current_period_ends_at`"). Read
> `CurrentPeriodEndsAt` (and optionally `NextAssessmentAt`); do not look for a `NextBillingAt`
> getter — it does not exist and will not compile.

### 2.4 Enum value tables (source: `map/models/enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`)

- **`IntervalUnit`** (`StringEnum`): `Day (day)`, `Month (month)`.
- **`SubscriptionState`** (`StringEnum`): `Pending (pending)`, `FailedToCreate (failed_to_create)`,
  `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`,
  `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`,
  `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`,
  `AwaitingSignup (awaiting_signup)`. (For step 3(b) "already active" test, compare against
  `SubscriptionState.Active`; enums are `StringEnum<T>`, not C# enums — compare via the static
  member or `.ToString()`/wire value, not `==` on a C# enum literal — see `dotnet-models`.)
- **`CollectionMethod`** (`StringEnum`, only if you choose to set it): `Automatic (automatic)`,
  `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`.
- **`SubscriptionStateFilter`** (for the optional site-wide `ListSubscriptions` `state` param):
  `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `PastDue (past_due)`,
  `Trialing (trialing)`, `OnHold (on_hold)`, `Suspended (suspended)`, `Unpaid (unpaid)`,
  `TrialEnded (trial_ended)`, `PendingCancellation (pending_cancellation)`,
  `PendingRenewal (pending_renewal)`, `ExpiredCards (expired_cards)`.

### 2.5 Error-payload shapes (source: records pages; namespace `MaxioAdvancedBilling.Models`)

- `CustomerErrorResponse1` — `Errors (errors): Errors?`; `Errors` record →
  `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?`.
- `ErrorListResponse1` — `Errors (errors): IReadOnlyList<string> !req` (the flat message list).
- `RawError` (Case B / fallback) — source `Core/ErrorResponse/RawError.cs` ⇒ namespace
  `MaxioAdvancedBilling.Core.ErrorResponse`. Members: `StatusCode: HttpStatusCode`,
  `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
- `SdkException<TError>` — source `Core/Exceptions/SdkException.cs` ⇒ namespace
  `MaxioAdvancedBilling.Core.Exceptions`; exposes `.Error` of type `TError`. (Confirm exact
  `using` against `dotnet-error-handling`; typed-error classes like `CreateCustomerError` live in
  `MaxioAdvancedBilling.Errors`.)

**Distinguishing "not found" from validation:** for Case-B reads (`ReadCustomerByReference`,
`ListCustomerSubscriptions`, `ListProductFamilies`) inspect `ex.Error.StatusCode` and treat
`HttpStatusCode.NotFound` as "absent." Validation failures on writes are Case-A 422s — read them
via the typed `TryGet…` accessor first, `TryGetRawError` as fallback. Do **not** parse exception
`.ToString()`.

---

## 3. Trap notes (load the named skill at the step where it bites)

⚠ **Step 1 (client/DI):** the `HttpClient`/handler pipeline must be long-lived and reused (whether
the SDK client wrapper may be transient, and how to register it so the handler isn't rebuilt per
request, is not visible in the ctor signature). **MUST load `dotnet-client-initialization`** before
wiring the client.

⚠ **Step 1 (auth):** whether credentials must be set before construction or in the DI callback, and
how key rotation is meant to flow, is not shown by the property. **MUST load `dotnet-authentication`**
before wiring credentials.

⚠ **Step 1 (base URL / server / retries):** the SDK's retry/timeout options do **not** bound a whole
call and are **not** the timeout on the `HttpClient` you register; and which verbs/failures actually
retry (a non-idempotent `POST` create can execute more than once on a transport failure) is not
visible in the option names. This bears directly on double-submit safety of `CreateCustomer` /
`CreateSubscription`. **MUST load `dotnet-configuration-resilience`** before tuning the client or base URL.

⚠ **Step 2/3 (calling list ops with many nullable params):** `ListProductFamilies`,
`ListProductsForProductFamily`, `ListSubscriptions` have optional params with **no C# default** that
mis-bind in a positional call. Whether a given param may be omitted vs must be passed `null` is a
usage detail. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 2/3 (models — enums & unions):** `SubscriptionState`, `IntervalUnit`, `CollectionMethod` are
`StringEnum<T>`, not C# enums (how to compare/construct them is not obvious from the type name);
unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** before building
request payloads or comparing state.

⚠ **Step 3/4 (error boundary):** which exception types actually reach the catch, and the traps that
make a reasonable catch ladder silently wrong, are not visible from the signatures. **MUST load
`dotnet-error-handling`** before writing any try/catch (see REQUIRED READING for the two mandatory
`JsonException` hazards).

---

## 4. REQUIRED READING (load BEFORE implementation starts)

The sheet deliberately does not carry these skills' contents — load each before its step:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — setting Basic credentials, where/when, key handling |
| `dotnet-configuration-resilience` | Step 1 — base-URL/server selection, retries/timeouts, double-submit semantics |
| `dotnet-calling-endpoints` | Steps 2–4 — calling ops, named args for many-nullable list ops |
| `dotnet-models` | Steps 2–3 — building requests, `StringEnum` vs C# enum, wire names |
| `dotnet-error-handling` | Steps 3–4 — the exception boundary (mandatory hazards below) |
| `dotnet-testing` | integration tests — the HttpClient test seam |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary** — it reaches the
boundary from two directions needing opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **No "maxiotest"/sandbox environment is modeled by the SDK.** `ServerEnvironment` exposes only
  `Us` (`https://{site}.chargify.com`) and `Eu` (`https://{site}.ebilling.maxio.com`) — there is no
  `.maxiotest` value. To target a `.maxiotest`-style sandbox host, the integration MUST set
  `Maxio:BaseUrl` and apply it verbatim to `options.Server.Production.Us.BaseUrl`; setting only the
  subdomain will resolve to `*.chargify.com`. (`sdk-map.md` Servers & auth.) **Assumption:** the
  seeded sandbox is reachable via the `Maxio:BaseUrl` override; if the sandbox host differs from the
  `Us` template, `BaseUrl` is REQUIRED, not optional.
- **Family-handle → id resolution: use `ListProductFamilies` + in-memory `Handle` match.** The C#
  `ReadProductFamily(int id, …)` signature takes an **int** id only, so the REST `handle:my-family`
  trick cannot be passed through the typed SDK. `ListProductFamilies` has no handle filter param, so
  fetch families and match `ProductFamily.Handle == "eshop-subscribe"` in memory to get the id, then
  call `ListProductsForProductFamily(id.ToString(), …)`. **UNVERIFIED (live-wire):** whether passing
  the string `"handle:eshop-subscribe"` directly as `productFamilyId` to
  `ListProductsForProductFamily` works is a live-API behavior the SDK cannot confirm (it just
  interpolates the string into the path) — do NOT rely on it; use the resolve-by-id path above.
- **No-payment subscription create REQUIRES an invoice/remittance collection method (CONFIRMED by
  live 422).** Omitting payment attributes is NOT sufficient: even on a `require_credit_card=false`
  product, Maxio rejects the create with `422 ["No payment method was on file for the $299.00
  balance"]` because the default collection method (`automatic`) tries to auto-charge the recurring
  balance. The SDK-exposed fix is the `PaymentCollectionMethod (payment_collection_method):
  CollectionMethod?` field on the `CreateSubscription` request model (namespace
  `MaxioAdvancedBilling.Models`). Set it to `CollectionMethod.Remittance` (enum type
  `MaxioAdvancedBilling.Models.Enums.CollectionMethod`, wire value `remittance`) so the balance is
  invoiced instead of auto-collected — no card / no 3-DS needed. C#:
  `PaymentCollectionMethod = CollectionMethod.Remittance` (`CollectionMethod` is a `StringEnum<T>`;
  use the static member, not a C# enum literal). Both `Remittance` and `Invoice` members exist and
  compile against `AsadAli.AdvancedBilling.Sdk` 1.0.2 (the map is generated from tag `v1.0.2`). The
  map exposes NO other required field for a no-card invoice-billed subscription — `NetTerms
  (net_terms): string?` and `ReceivesInvoiceEmails (receives_invoice_emails): string?` are the only
  other invoice-related fields and both are optional.
  **UNVERIFIED (live-wire / site architecture):** the `CollectionMethod` enum doc states the valid
  options differ by site billing architecture — Relationship Invoicing accepts `remittance`,
  `automatic`, `prepaid`; legacy Statements accepts `invoice`, `automatic`. Which architecture the
  configured sandbox site runs is a live/site-config fact the map cannot settle. Defensive directive: send
  `CollectionMethod.Remittance` first; if the create still returns a 422 that names the collection
  method as invalid (via `TryGetErrorListResponse1(out var e)`, fallback `TryGetRawError` →
  `ReadAsString()`), retry once with `CollectionMethod.Invoice`. Surface any remaining 422 message
  best-effort rather than assuming success.
- **Customer-not-found detection is via status code.** `ReadCustomerByReference` is Case B; treat
  `ex.Error.StatusCode == HttpStatusCode.NotFound` as "no customer, create one." **UNVERIFIED
  (live-wire):** whether an unmatched lookup returns exactly 404 (vs another status/empty) can only
  be confirmed against live traffic. Defensive directive: catch `SdkException<RawError>`, branch to
  "create" on `NotFound`, and re-throw / surface other statuses rather than blindly creating (so a
  transient 5xx never spawns a duplicate customer).
- **Idempotency of writes is not absolute at the SDK layer.** A transport failure can re-send a
  `POST` (see the resilience trap). The look-up-before-create logic in steps 3(a)/3(b) is the
  primary idempotency guard; whether additional protection is needed depends on
  `dotnet-configuration-resilience` semantics — load it before finalizing retry config.
