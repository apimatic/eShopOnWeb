# Maxio Advanced Billing — Recurring Subscription Billing (eShopOnWeb `src/PublicApi`)

Grounded entirely against the bundled SDK map (`AsadAli.AdvancedBilling.Sdk`, root namespace
`MaxioAdvancedBilling`, source tag `v1.0.2` / commit `15db14b`). No SDK-source clone was needed —
every row below cites the map page it came from. This is additive to the existing one-time flow;
it introduces no changes to existing commerce code.

---

## 1. Scope & sequence

| # | Step | Maxio operation(s) | Notes |
|---|---|---|---|
| 1 | Register the SDK client + auth in `PublicApi` DI | `AddMaxioAdvancedBillingClient` | Basic auth (key/`"x"`), site vs BaseUrl override |
| 2 | `GET /api/subscription-plans` — list plans in the family | `ProductFamilies.ListProductFamilies` → `ProductFamilies.ListProductsForProductFamily` | Resolve family handle→id, then list its products |
| 3 | On subscribe — ensure Maxio customer exists idempotently | `Customers.ReadCustomerByReference` → (on 404) `Customers.CreateCustomer` | `reference` = stable eShop user id |
| 4 | On subscribe — enroll customer in chosen plan | `Subscriptions.FindSubscription` → (on 404) `Subscriptions.CreateSubscription` | product by `product_handle`, customer by `customer_id`/`customer_reference` |
| 5 | `GET /api/my-subscriptions` — list the customer's subscriptions | `Customers.ListCustomerSubscriptions` | filtered by customer id |

Duplicate-prevention (double-click) is only partly an SDK guarantee — see §5 Blockers/Assumptions.

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

### 2a. Namespaces (`using` directives)

| Type(s) | Namespace | Source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | sdk-map.md |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | sdk-map.md (Getting a client) |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | sdk-map.md (Servers & auth) |
| Controller types (`Api/`) — normally reached via `client.X` properties | `MaxioAdvancedBilling.Api` | sdk-map.md (namespaces) |
| All request/response records (`CreateSubscription`, `CreateCustomer`, `ProductResponse`, `Subscription`, error-payload records, …) | `MaxioAdvancedBilling.Models` | sdk-map.md (namespaces); records pages |
| Enums (`SubscriptionState`, `IntervalUnit`, `CollectionMethod`, `SubscriptionStateFilter`) | `MaxioAdvancedBilling.Models.Enums` | enums.md |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `FindSubscriptionError`) | `MaxioAdvancedBilling.Errors` | sdk-map.md (namespaces) |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` (implied by source path `Core/Exceptions/SdkException.cs`) | sdk-map.md (error model) |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` (implied by source path `Core/ErrorResponse/RawError.cs`) | sdk-map.md (error model) |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | sdk-map.md (options) |

### 2b. Operations

| Op | Signature (params in order) | Request model + fields used | Response envelope → fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — pass 5 nulls | none | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` (nullable) → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<RawError>` → `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | operations/ProductFamilies.md; records-3-Of-Su.md |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 must-pass-explicitly (`dateField`…`include`); `page`=1,`perPage`=20 | `productFamilyId` = numeric family id as string (see §5 for the `handle:` shortcut) | `IReadOnlyList<ProductResponse>`; each `.Product` (**!req**) → see Product projection §2c | **Case A** `SdkException<ListProductsForProductFamilyError>` → `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | operations/ProductFamilies.md |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` ← `reference` | `reference` = stable eShop user identity | `CustomerResponse`; `.Customer` (**!req**) → `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName` | **Case B** `SdkException<RawError>` → `StatusCode` (**404 = not found**), `ReadAsString()` | none | operations/Customers.md; records-2-Cr-Ne.md |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer` required: `FirstName (first_name) !req`, `LastName (last_name) !req`, `Email (email) !req`; optional-but-relevant: `Reference (reference)` ← eShop user id | `CustomerResponse`; `.Customer` → `Id`, `Reference` | **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Customers.md; records-1-Ac-Cr.md |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must pass explicitly; query `reference` ← `reference` | `reference` = deterministic subscription reference you set on create | `SubscriptionResponse`; `.Subscription` (**nullable — see §5**) | **Case A** `SdkException<FindSubscriptionError>` → `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`; `CreateSubscription` fields used: `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`, `Reference (reference): string?` (idempotency key), optional `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | `SubscriptionResponse`; `.Subscription` → see Subscription projection §2c | **Case A** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md; records-2-Cr-Ne.md |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` = Maxio customer id (from step 3) | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` (**nullable**) → see projection | **Case B** `SdkException<RawError>` → `StatusCode`, `ReadAsString()` | **none** (no page/perPage on this op) | operations/Customers.md |

### 2c. Field projections (records)

**`CreateSubscription`** — model marks **NOTHING required** except the wrapper's `Subscription`. The
operation Notes say: specify the product with `product_handle` (or `product_id`); identify the
customer with `customer_id` **or** `customer_reference` (or pass `customer_attributes` to create a
new one). Fields to set for the hero flow (all from records-2-Cr-Ne.md):
- `ProductHandle (product_handle): string?` — set to the chosen plan handle (default `eshop-pro`).
- `CustomerId (customer_id): int?` — preferred: the id from find-or-create (step 3).
- (alt) `CustomerReference (customer_reference): string?` — the eShop user reference, if you skip the id.
- `Reference (reference): string?` — set a deterministic value for idempotent `FindSubscription` (§5).
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — optional; leave `null`
  to use the product default. "Payment method not required" is **NOT** a field on this request — it
  is a product-level setting (`require_credit_card` on the Product), already configured on the seeded
  plans, so a subscribe with only product+customer succeeds without card capture (see §5 note).

**`Product`** (from `ProductResponse.Product`, records-3-Of-Su.md) → plan DTO:
- handle ← `Handle (handle): string?`
- name ← `Name (name): string?`
- description ← `Description (description): string?`
- **price ← `PriceInCents (price_in_cents): long?` — MINOR UNITS (cents).** e.g. Pro = 29900.
- interval ← `Interval (interval): int?` (numeric count, e.g. 1)
- interval unit ← `IntervalUnit (interval_unit): IntervalUnit?` (enum `day`/`month`, §2e)
- product family ← `ProductFamily (product_family): ProductFamily?` → `.Handle`, `.Name`
- **currency: NOT present on the `Product` model** — see §5 (row is `UNVERIFIED`/site-default).

**`Subscription`** (from `SubscriptionResponse.Subscription`, records-3-Of-Su.md) → subscribe confirmation:
- state ← `State (state): SubscriptionState?` (enum, §2e)
- **next billing date ← `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`.** There is
  **no `next_billing_at` field on the response** — the `UpdateSubscription` Notes state `next_billing_at`
  is not returned and `current_period_ends_at` is the field to read for the next billing date.
  (`NextAssessmentAt (next_assessment_at)` also exists but is the assessment date, not the period end.)
- product price ← `ProductPriceInCents (product_price_in_cents): long?` (cents) or `Product.PriceInCents`
- plan handle/name ← `Product (product): Product?` → `.Handle`, `.Name`
- customer ← `Customer (customer): Customer?` → `.Id`, `.Reference`

**Error payloads:**
- `ErrorListResponse1` (CreateSubscription 422) → `Errors (errors): IReadOnlyList<string> !req` — a
  usable list of human-readable messages. (records-2-Cr-Ne.md)
- `CustomerErrorResponse1` (CreateCustomer 422) → `Errors (errors): Errors?`, where nested `Errors`
  has ONLY `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`.
  **This typed shape does not model a general customer-validation message** (e.g. "Email: is invalid"),
  so those messages are dropped on deserialize — read the actual body via the `TryGetRawError` →
  `RawError.ReadAsString()` fallback instead. (records-2-Cr-Ne.md; see §4 error-handling row.)

### 2d. Client construction & auth (§1)

From sdk-map.md "Getting a client" + "Servers & auth":
- Only constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- DI: `services.AddMaxioAdvancedBillingClient(o => { … })` (source `ServiceCollectionExtensions.cs`).
- Auth (Basic, the ONLY scheme): `o.BasicAuth = new BasicAuthCredentials { Username = "<Maxio:ApiKey>", Password = "x" }`.
  **Username = the API key; Password = the literal string `"x"`.**
- Environment: `o.Environment = ServerEnvironment.Us` (default; EU only if the account requested it —
  config supplies no EU signal, so US is assumed — §5).
- **Subdomain (site) vs verbatim BaseUrl override** — both live on the Production server group:
  - Derive from subdomain: `o.Server.Production.Us.Site = "<Maxio:Subdomain>"` → base becomes `https://{site}.chargify.com`.
  - Verbatim override (when `Maxio:BaseUrl` is set): `o.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` used as-is.
  - Bind logic: if `Maxio:BaseUrl` non-empty → set `BaseUrl`; else → set `Site` from `Maxio:Subdomain`.
  - The Ebb (events) server group is not touched by this flow (no event-ingest ops in scope).
  - Source: sdk-map.md "Servers & auth" (override points `options.Server.Production.Us.BaseUrl` / `.Site`).

### 2e. Enums touched (exact C# member ← wire value; namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | Members used (C# ← wire) | Source |
|---|---|---|
| `SubscriptionState` | `Pending`←`pending`, `Trialing`←`trialing`, `Active`←`active`, `PastDue`←`past_due`, `Suspended`←`suspended`, `Canceled`←`canceled`, `Expired`←`expired`, `Paused`←`paused`, `Unpaid`←`unpaid`, `TrialEnded`←`trial_ended`, `OnHold`←`on_hold`, `AwaitingSignup`←`awaiting_signup`, `FailedToCreate`←`failed_to_create`, `Assessing`←`assessing`, `SoftFailure`←`soft_failure` | enums.md |
| `IntervalUnit` | `Day`←`day`, `Month`←`month` | enums.md |
| `CollectionMethod` | `Automatic`←`automatic`, `Remittance`←`remittance`, `Prepaid`←`prepaid`, `Invoice`←`invoice` | enums.md |
| `SubscriptionStateFilter` | (if you later filter lists) `Active`←`active`, `Canceled`←`canceled`, `Trialing`←`trialing`, … | enums.md |

Enums are `StringEnum<T>`, **not** C# enums — build via the static member (`IntervalUnit.Month`) or
`IntervalUnit.FromValue("month")`; compare/serialize by value, never cast.

---

## 3. Trap notes (one per step; each ends in a MUST load)

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline the SDK client wraps has
> lifetime rules a constructor signature does not show, and the SDK's own retry/timeout options do
> **not** bound a whole call and are **not** the `HttpClient` timeout. **MUST load
> `dotnet-client-initialization`** (client + DI + HttpClient ownership) **and
> `dotnet-configuration-resilience`** (what `Timeout`/retries actually bound, base-URL selection)
> before wiring the client.

> ⚠ Steps 2–5 (every list/find call) — several params on `ListProductsForProductFamily`,
> `ListCustomerSubscriptions` and `FindSubscription` are nullable with no C# default and bind wrong
> in a positional call. **MUST load `dotnet-calling-endpoints`** before writing the first call.

> ⚠ Steps 2–4 (building request bodies / reading responses) — enums are `StringEnum<T>`, response
> envelopes wrap their payload one level down, and **unmodeled JSON fields are silently dropped on
> deserialize** (this is exactly why `CustomerErrorResponse1` loses its real message — §2c). **MUST
> load `dotnet-models`** before constructing payloads or projecting SDK models onto DTOs.

> ⚠ Step 3–4 (idempotent writes under double-click) — whether a failed or retried write can be
> re-sent, and what the SDK re-sends on transport failure, is governed by resilience config, not by
> anything visible in the call signature. **MUST load `dotnet-configuration-resilience`** before
> relying on any retry behaviour for `CreateCustomer`/`CreateSubscription`.

> ⚠ All steps (error boundary) — which exception type actually reaches each catch, and how to read a
> status code / message safely, is not inferable from the signatures. **MUST load
> `dotnet-error-handling`** before writing the try/catch (see §4).

---

## 4. REQUIRED READING (load BEFORE implementation starts)

These `dotnet-*` companion skills are mandatory; this sheet deliberately does **not** carry their
contents (defaults, worked examples, and the parts you must still wire yourself live in the skill).

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, DI registration, HttpClient lifetime |
| `dotnet-authentication` | Step 1 — supplying Basic credentials (key / `"x"`), loading key from config |
| `dotnet-configuration-resilience` | Step 1 & 3–4 — base-URL/site selection, retries, timeout semantics |
| `dotnet-calling-endpoints` | Steps 2–5 — named-argument calls, request/response envelope shapes |
| `dotnet-models` | Steps 2–4 — building request models, `StringEnum<T>`, dropped-field trap |
| `dotnet-error-handling` | All steps — exception types, safe status/body reading, catch-ladder traps |
| `dotnet-testing` | Test phase — the `HttpClient` seam for stubbing the SDK |

**Two error-boundary hazards that MUST be handled — `System.Text.Json.JsonException` reaches the
boundary from two directions and they need opposite handling:**

- A drifted or malformed **2xx** body (e.g. a missing `required` member such as
  `ProductResponse.Product` or `CustomerResponse.Customer`) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection (e.g. a 422) as an outage, and a
  caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Operation-specific error facts to wire (from §2b):
- Lookups that "miss" throw rather than returning null: `ReadCustomerByReference` → Case B
  `RawError.StatusCode == 404`; `FindSubscription` → Case A `TryGetNoContent` [404]. Treat 404 as
  "not found → create", not as a fault.
- `CreateCustomer` 422 → `TryGetCustomerErrorResponse1`, but read the real message via the
  `TryGetRawError` → `RawError.ReadAsString()` fallback (typed payload models only per_page/price_point).
- `CreateSubscription` 422 → `TryGetErrorListResponse1(out ErrorListResponse1)` gives
  `Errors: IReadOnlyList<string>` (usable messages).

---

## 5. Assumptions & Blockers

**Assumptions**
- **US hosting.** `Maxio:` config supplies no environment key; the SDK default is `ServerEnvironment.Us`.
  If the sandbox is EU-hosted, `o.Environment = ServerEnvironment.Eu` and the EU override points
  (`o.Server.Production.Eu.*`) must be used instead. (sdk-map.md Servers & auth.)
- **Stable customer `reference`.** The plan uses the eShop user identity from the JWT as the customer
  `reference`. Which claim (email vs username vs a stable user-id) is the reference is an application
  decision — `| customer reference value | resolve from the app's JWT identity path | YOUR CALL — not
  in the map |`. The SDK fact: `CreateCustomer` Notes state `reference` must be unique and only one
  customer may exist per reference value, so it is the correct idempotency key. (operations/Customers.md.)
- **Plan currency.** The `Product` model exposes `price_in_cents` (minor units) but **no currency
  field**; the plan's display currency is `| plan currency | site default currency | UNVERIFIED |`
  — only live traffic (or a separate site/price-point read) can confirm it. Defensive directive:
  render price from `price_in_cents` and take the currency from the site's known default
  (USD for this sandbox per the brief) rather than reading it off the Product model.
- **Family resolution is two-step.** The map documents the `handle:my-family` path format only for
  `ReadProductFamily`, not for `ListProductsForProductFamily` (whose `productFamilyId` is a plain
  string). Grounded primary: call `ListProductFamilies`, match `.ProductFamily.Handle ==
  Maxio:ProductFamilyHandle` in code to get the numeric `Id`, then pass `Id.ToString()` to
  `ListProductsForProductFamily`. Whether `"handle:eshop-subscribe"` is accepted directly by
  `ListProductsForProductFamily` is `UNVERIFIED` — treat the two-step form as the contract and the
  `handle:` shortcut as an optimization to confirm against live traffic before relying on it.

**Blockers**
- **Subscription double-click idempotency is not fully an SDK guarantee.** `CreateCustomer` enforces
  `reference` uniqueness (documented), so concurrent customer creates are safe *if* the second create's
  422 is caught and re-looked-up — that catch-and-relookup is a required application behaviour.
  `CreateSubscription`, however, has **no idempotency key and the map documents no uniqueness
  constraint on subscription `reference`** — `FindSubscription` looks a subscription up by reference
  but nothing in the map says two subscriptions cannot share one. Therefore preventing a *second
  subscription* under a concurrent double-click cannot be delegated to Maxio: the application must
  serialize the subscribe operation per (user, plan) — e.g. a lock/uniqueness constraint in its own
  store keyed on the deterministic subscription `reference` — before calling `CreateSubscription`.
  `| per-user-per-plan subscribe serialization | resolve in the app's own persistence/concurrency
  layer | YOUR CALL — not in the map |`. The SDK-level contribution is: set a deterministic
  `CreateSubscription.reference`, `FindSubscription` on it first, and create only on a 404. This is a
  Blocker rather than an assumption because the plan's "double-click must not create two
  subscriptions" requirement is not satisfiable by the SDK contract alone.
- **`SubscriptionResponse.Subscription` is nullable (`Subscription?`), unlike `ProductResponse.Product`
  and `CustomerResponse.Customer` which are `!req`.** So a `FindSubscription`/`CreateSubscription`
  response can carry a null `.Subscription` without a `JsonException`. Defensive directive: null-check
  `.Subscription` before projecting confirmation fields, and treat a null payload on a 2xx as a
  not-found/failed-create rather than dereferencing it. (records-4-Su-We.md.)
