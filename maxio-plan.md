# Maxio Advanced Billing — SDK integration plan (eShopOnWeb PublicApi)

Grounded against the bundled Maxio SDK map (`sdk-map.md` + `map/operations/*`, `map/models/*`).
SDK: package `AsadAli.AdvancedBilling.Sdk`, root namespace `MaxioAdvancedBilling`, client
`MaxioAdvancedBillingClient`, auth HTTP Basic (username = API key, password = literal `"x"`),
source tag `v1.0.2`. Every operation is **throw-only** (no `…Result`/no-throw variants).

---

## 1. Scope & sequence

The three PublicApi endpoints, in build order. A Maxio "product" = a subscribable plan.

1. **Client wiring (all endpoints)** — register `MaxioAdvancedBillingClient` in DI, Basic auth
   from `Maxio:ApiKey`, site from `Maxio:Subdomain`, optional verbatim `Maxio:BaseUrl` override,
   `ServerEnvironment.Us`. Uses no operations. (Step trap notes §3; skills §4.)
2. **GET /api/subscription-plans** — resolve the configured family handle
   (`Maxio:ProductFamilyHandle`) to a numeric family id via `ProductFamilies.ListProductFamilies`,
   then `ProductFamilies.ListProductsForProductFamily`. Map each `ProductResponse.Product` to the
   returned plan shape.
3. **POST /api/subscriptions** (hero flow) —
   a. Ensure customer: `Customers.ReadCustomerByReference` (stable reference) → on 404 (Case B
      throw) `Customers.CreateCustomer`. Optionally `Customers.ListCustomers` (`q` = email) as a
      secondary lookup.
   b. Detect existing subscription: `Customers.ListCustomerSubscriptions(customerId)`; inspect
      each `SubscriptionResponse.Subscription.State` / `.Product` to skip re-enroll.
   c. Create: `Subscriptions.CreateSubscription` with `product_handle` + `customer_id` (or
      `customer_reference`), **no** payment profile.
   d. Return plan / price / state / next-billing-date from `SubscriptionResponse.Subscription`.
4. **GET /api/my-subscriptions** — resolve caller → customer id (as 3a), then
   `Customers.ListCustomerSubscriptions(customerId)`; map each `.Subscription`.

Idempotency: the SDK exposes **no idempotency-key parameter or helper** on any operation in
scope (confirmed: `CreateCustomer`/`CreateSubscription` signatures take only the body + `ct`). You
must do **find-before-create yourself**. See the retry trap in §3 — find-before-create does not
fully close the double-execute window on `POST`.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each
> one from that type's own map row, never from where a neighbouring type sits. Enums, unions,
> auth, server and client-config types live in different child namespaces, and two types
> configured side by side routinely live in different ones. Dropping a type to the root or to
> `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Namespaces (add a `using` per kind — child namespaces do NOT import transitively)

| Type(s) | Namespace | Source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | sdk-map.md |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | sdk-map.md |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | sdk-map.md |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | sdk-map.md |
| Controller types (if referenced by type) | `MaxioAdvancedBilling.Api` | sdk-map.md |
| All request/response records (`Product`, `Customer`, `Subscription`, `*Response`, `CreateCustomer*`, `CreateSubscription*`, `CustomerAttributes`, `ProductFamily*`) | `MaxioAdvancedBilling.Models` | sdk-map.md (Models table) |
| Enums (`SubscriptionState`, `SubscriptionStateFilter`, `IntervalUnit`, `CollectionMethod`) | `MaxioAdvancedBilling.Models.Enums` | sdk-map.md (Namespaces table) |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` (implied by source path `Core/Exceptions/SdkException.cs`) | sdk-map.md |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` (implied by source path `Core/ErrorResponse/RawError.cs`) | sdk-map.md |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` | sdk-map.md (Namespaces table) |

### 2b. Client construction & auth

| Fact | Value | Source |
|---|---|---|
| Client ctor (only one) | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | sdk-map.md |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) | sdk-map.md |
| Auth | `o.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` | sdk-map.md |
| Environment | `o.Environment = ServerEnvironment.Us` (default `Us`; `Eu` only if account is EU-hosted) | sdk-map.md |
| Site from subdomain | `o.Server.Production.Us.Site = <Maxio:Subdomain>` → base becomes `https://{subdomain}.chargify.com` | sdk-map.md |
| Explicit base-URL override | `o.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` — used verbatim in place of the derived template. Set ONLY when `Maxio:BaseUrl` is present | sdk-map.md |
| No builder type | there is no fluent builder — options object + ctor/DI only | sdk-map.md |

> Note on `Maxio:BaseUrl`: all in-scope operations run on the **Production** server group, US
> node, so overriding `Server.Production.Us.BaseUrl` covers them. There is no single "global base
> URL" knob — it is per server-group/node. (The Ebb events group is not used by this integration.)

### 2c. Operations

| # | Call | Signature (params in order) | Request model + fields | Response envelope → fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| P1 | `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable params, no defaults → pass `null` to skip | none | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` (nullable) → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`. Filter client-side where `Handle == Maxio:ProductFamilyHandle` to get the numeric `Id` | **Case B** `SdkException<RawError>`: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` | none | operations/ProductFamilies.md |
| P2 | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params (`dateField`…`include`) no default → pass `null`; `page`/`perPage` default 1/20 | `productFamilyId` = the numeric id from P1, as a string | `IReadOnlyList<ProductResponse>`; each `.Product` (`Product !req`) → see Product fields below | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | operations/ProductFamilies.md |
| C1 | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` | `reference` = your stable per-user reference | `CustomerResponse` → `.Customer` (`Customer !req`) → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | **Case B** `SdkException<RawError>` — **404 throws** (no match → catch and treat as "not found") | none | operations/Customers.md |
| C2 | `client.Customers.ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 nullable params no default; `page`/`perPage` default 1/50 | `q` = search text (email / reference / name); server-side substring search, NOT exact | `IReadOnlyList<CustomerResponse>`; each `.Customer` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | operations/Customers.md |
| C3 | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest { Customer = new CreateCustomer { … } }` (see CreateCustomer fields below) | `CustomerResponse` → `.Customer` → `Id`, `Reference` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Customers.md |
| C4 | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` = numeric `Customer.Id` (resolve via C1 first) | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` (nullable) → see Subscription fields | **Case B** `SdkException<RawError>` | none (returns all) | operations/Customers.md |
| S1 | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest { Subscription = new CreateSubscription { … } }` (see CreateSubscription fields below) | `SubscriptionResponse` → `.Subscription` (**nullable** — see §3 defensive note) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md |

> **No operation filters subscriptions by customer except `ListCustomerSubscriptions(int customerId)`.**
> `Subscriptions.ListSubscriptions` has NO customer-id parameter (its filters are `state`,
> `product`, `productPricePointId`, `coupon`, `couponCode`, dates, `metadata`, `direction`,
> `sort`, `include`), so "my-subscriptions" must go through `ListCustomerSubscriptions`, which
> requires the numeric customer id resolved from the reference first. Source: operations/Subscriptions.md, operations/Customers.md.

### 2d. Response-envelope shapes (what wraps the payload)

| Envelope | Inner field (C# / wire) | Nullable? | Source |
|---|---|---|---|
| `ProductResponse` | `Product (product): Product` | **required** (`!req`) | records-3-Of-Su.md |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` | nullable | records-3-Of-Su.md |
| `CustomerResponse` | `Customer (customer): Customer` | **required** (`!req`) | records-2-Cr-Ne.md |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` | **nullable** | records-4-Su-We.md |

### 2e. Fields to read/return (C# name (wire_name): type)

**Product** (plan list P1/P2, and `Subscription.Product`) — source records-3-Of-Su.md:
`Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`,
`PriceInCents (price_in_cents): long?`, `Interval (interval): int?`,
`IntervalUnit (interval_unit): IntervalUnit?`, `Description (description): string?`,
`ProductFamily (product_family): ProductFamily?`.
- There is **no** `formatted_price` field on `Product` — format from `PriceInCents` in the app
  (e.g. `PriceInCents / 100m`). Labeled `YOUR CALL` in the source column below.

**Subscription** (returned by S1 / C4) — source records-3-Of-Su.md:
`Id (id): int?`, `State (state): SubscriptionState?`,
`ProductPriceInCents (product_price_in_cents): long?`,
`CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`,
`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`  ← **use as "next billing date"**,
`NextAssessmentAt (next_assessment_at): DateTimeOffset?`,
`CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`,
`ActivatedAt (activated_at): DateTimeOffset?`,
`Product (product): Product?`, `Customer (customer): Customer?`,
`Reference (reference): string?`.

> **Next-billing-date field.** The `Subscription` response model has **no** `next_billing_at`
> field. The SDK's own `UpdateSubscription` notes state the server does not return
> `next_billing_at` and that you read **`current_period_ends_at`** to see the next billing date.
> So map "next billing date" → `CurrentPeriodEndsAt`. `NextAssessmentAt` is the assessment date
> and is a reasonable fallback if `CurrentPeriodEndsAt` is null. Source: operations/Subscriptions.md
> (UpdateSubscription notes), records-3-Of-Su.md.

**Customer** (read back) — source records-2-Cr-Ne.md:
`Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`,
`FirstName (first_name): string?`, `LastName (last_name): string?`.

### 2f. Request models to build

**CreateCustomer** (wrapped as `CreateCustomerRequest.Customer`) — source records-1-Ac-Cr.md.
Required (`!req`): `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`.
Optional but load-bearing for this flow: `Reference (reference): string?` — set it to your stable
per-user reference so C1 can look the customer up and the API's uniqueness guard prevents a
duplicate. All other fields optional: `Organization`, `Address`, `City`, `State`, `Zip`,
`Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `CcEmails`, `ParentId`, `SalesforceId`.
- The CreateCustomer notes require ISO country/state **formats** *when those fields are provided* —
  they are not required fields. Payment method not required by the plans → **no payment profile
  needed** to create the customer. Source: operations/Customers.md (CreateCustomer notes).

**CreateSubscription** (wrapped as `CreateSubscriptionRequest.Subscription`) — source
records-2-Cr-Ne.md. ⚠ **This model marks NOTHING as `required`** — the compiler will not stop you
omitting the product or the customer. Per the CreateSubscription notes you MUST set:
- product: `ProductHandle (product_handle): string?` (default `"eshop-pro"` per `Maxio:...`), OR
  `ProductId (product_id): int?`.
- customer: `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?` —
  use the same stable reference as the customer's `Reference`.
- **No** payment fields: leave `CreditCardAttributes` / `BankAccountAttributes` /
  `PaymentProfileId` / `PaymentProfileAttributes` unset (plans do not require payment).
Fields deliberately left OUT (Notes-named, not needed here): `ProductPricePointHandle`/`Id`
(uses product default price point), `CouponCode`/`CouponCodes`, `CustomerAttributes` (only if you
want create-customer-inline instead of find-before-create C1/C3), `PaymentCollectionMethod`,
`NextBillingAt`/`InitialBillingAt`, `CalendarBilling`, `Currency`, `OfferId`.
Source: operations/Subscriptions.md (CreateSubscription notes), records-2-Cr-Ne.md.

> Alternative to C1/C3 find-before-create: pass `CustomerAttributes (customer_attributes)` on the
> CreateSubscription to create the customer inline (`CustomerAttributes` fields: `FirstName`,
> `LastName`, `Email`, `Reference`, … — records-2-Cr-Ne.md). This plan uses explicit
> find-before-create instead so the idempotent customer lookup is under your control.

### 2g. Enums

**SubscriptionState** (`StringEnum`, `MaxioAdvancedBilling.Models.Enums`) — source enums.md.
Members (C# / wire): `Pending (pending)`, `FailedToCreate (failed_to_create)`,
`Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`,
`PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`,
`Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`,
`AwaitingSignup (awaiting_signup)`. "Active subscription" for the duplicate-detection check =
`SubscriptionState.Active` (compare via `StringEnum`, not a C# `enum`).

**SubscriptionStateFilter** (only if you later use `Subscriptions.ListSubscriptions(state:…)`) —
`Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`,
`OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`,
`PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`,
`Trialing (trialing)`, `Unpaid (unpaid)`. Source: enums.md.

**IntervalUnit** (`Product.IntervalUnit`) — `Day (day)`, `Month (month)`. Source: enums.md.

### 2h. Idempotency helpers

| Fact | Source |
|---|---|
| No idempotency-key parameter or helper on any in-scope operation — `CreateCustomer(body, ct)` and `CreateSubscription(body, ct)` take only body + `ct`. Do **find-before-create yourself**. | operations/Customers.md, operations/Subscriptions.md |
| Customer create has a server-side guard: "you may only create one customer for a given `reference` value" — a unique `Reference` makes duplicate customer creation fail rather than duplicate. | operations/Customers.md (CreateCustomer notes) |
| No equivalent uniqueness guard documented for subscription create — duplicate protection for S1 relies entirely on your C4 pre-check plus the retry caveat in §3. | operations/Subscriptions.md |

---

## 3. Trap notes (load the named skill before writing that step)

> ⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline must be long-lived and reused;
> whether the SDK client wrapper is transient or singleton, and how to register it correctly, is
> not visible in the ctor signature. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 1 (auth) — where and when to set `BasicAuth` (before ctor vs in the DI callback) and how
> to source the key from configuration is a usage detail the signature hides. **MUST load
> `dotnet-authentication`** before setting credentials.

> ⚠ Step 1 (base URL / server selection) — `Maxio:BaseUrl` overrides a per-server-group/node
> value, not a single global URL, and `Timeout`/retry options do not bound a whole call. What a
> timeout actually bounds and how base-URL/server selection interacts with the environment is not
> in the option names. **MUST load `dotnet-configuration-resilience`** before tuning the client.

> ⚠ Steps 3c / 3a (writes + retries + idempotency) — `HttpMethodsToRetry` gates only the *status*
> trigger, but a transport failure (`HttpRequestException`) is retried on **every** verb including
> `POST`, and no setting disables that (`MaxRetries` floor is 1). Consequence: whether a failed
> `CreateCustomer`/`CreateSubscription` can be silently re-sent — and therefore how completely
> your find-before-create actually prevents duplicates — depends on retry semantics you must read,
> not assume. **MUST load `dotnet-configuration-resilience`** before relying on find-before-create.

> ⚠ Steps 2–4 (building requests / reading models) — enums are `StringEnum<T>` not C# enums
> (build with `.FromValue("wire")` / static members, compare accordingly), records are immutable
> with `init` setters and `required` members, and unmodeled JSON is dropped on deserialize. How to
> construct `CreateCustomer`/`CreateSubscription` and read `SubscriptionState` correctly is a
> model-layer concern the field list cannot fully show. **MUST load `dotnet-models`** before
> building payloads or mapping responses.

> ⚠ Steps 2–4 (calling) — list/search operations have many optional params with no C# default
> that mis-bind in a positional call; call them with **named arguments** (`page:`, `perPage:`,
> `q:`, `ct:`). **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ All steps (error boundary) — which exception type actually reaches each catch, and how to
> read status + body safely, differ by operation (Case A typed vs Case B raw); a 404 from
> `ReadCustomerByReference` arrives as a thrown `SdkException<RawError>`, not a null. **MUST load
> `dotnet-error-handling`** before writing any try/catch. (See the two mandatory `JsonException`
> rows in §4.)

---

## 4. REQUIRED READING (load BEFORE implementation starts)

These `dotnet-*` companion skills carry the usage layer this sheet deliberately does **not**
restate (defaults, worked examples, semantics). Load each before writing the step it governs.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, options object, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic-auth credentials, when/where to set them, loading the key from config |
| `dotnet-configuration-resilience` | Step 1 & writes — base-URL/server selection, retries/timeouts, what a timeout bounds, the POST transport-retry caveat |
| `dotnet-calling-endpoints` | Steps 2–4 — named-argument calling, request/response envelope shapes, async/cancellation |
| `dotnet-models` | Steps 2–4 — building `CreateCustomer`/`CreateSubscription`, `StringEnum` handling, required/init members, dropped-unmodeled-JSON |
| `dotnet-error-handling` | All steps — the try/catch boundary, Case A vs Case B, reading status/body safely |
| `dotnet-testing` | Tests — the `HttpClient` test seam, error/edge coverage |

**Mandatory `System.Text.Json.JsonException` hazard rows — put the error boundary right the first time:**
- A drifted or malformed **2xx** body (e.g. a missing `required` member such as
  `CustomerResponse.Customer` or `ProductResponse.Product`) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it
  escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps
  every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller
  that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- The configured plans have "payment method NOT required" (per the brief), so `CreateSubscription`
  and `CreateCustomer` succeed with no payment profile. If any target product actually requires a
  credit card, the create will 422 (`TryGetErrorListResponse1`) — that would become a Blocker, not
  a code tweak, because this SDK's non-card create path cannot satisfy a card-required product.
- The "stable reference" for a customer is an app-owned identifier (e.g. the eShopOnWeb user id or
  email). Which value to use is an application decision — see the `YOUR CALL` row below.
- `ServerEnvironment.Us` is correct for this account (US hosting). Switch to `Eu` only if the
  account is EU-hosted.

**Non-blocking uncertainties (labeled, with defensive directives)**
- `ListProductsForProductFamily(string productFamilyId, …)` takes a **string** path segment.
  Whether the Maxio server accepts a *handle* there (vs a numeric family id) is server behaviour
  the map/source cannot settle — `UNVERIFIED`. This plan avoids the question by resolving the
  configured handle to a numeric id via `ListProductFamilies` (P1) and passing the numeric id as a
  string. Do not pass the raw handle to P2 on the assumption it resolves. There is also no typed
  read-family-by-handle op: `ReadProductFamily` takes `int id`, so it cannot resolve a handle.
- `SubscriptionResponse.Subscription` is typed **nullable** even on a successful `CreateSubscription`.
  Whether a 2xx create ever returns a null payload is only confirmable on the live wire —
  `UNVERIFIED`. Defensive directive: after S1, null-check `.Subscription` and, if null, treat as a
  failed create (surface a deterministic error) rather than dereferencing — do **not** map a null
  payload to a "success" response.
- `Customers.ListCustomers` `q` is a server-side **substring search**, not an exact match
  (per its notes). For exact idempotent lookup prefer `ReadCustomerByReference` (C1); if you fall
  back to C2, re-verify the returned `Reference`/`Email` equals the caller's before treating it as
  the match. Source: operations/Customers.md.

**Blockers**
- None. Every contract fact the three endpoints need is present in the map.

**`YOUR CALL — not in the map` (application decisions the SDK forces but does not settle)**
- `| Customer "reference" value | resolve from the app's own stable user identity (e.g. eShop user id / email) | YOUR CALL — not in the map |`
- `| Formatted price string | derive from Product.PriceInCents (e.g. /100m) — no formatted_price field exists on Product | YOUR CALL — not in the map |`
- `| Which existing state counts as "already subscribed" | app policy over SubscriptionState (e.g. Active, or Active+Trialing) | YOUR CALL — not in the map |`
