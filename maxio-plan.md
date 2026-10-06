# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Additive/parallel billing layer on `src/PublicApi` (JWT-authenticated), Maxio Advanced Billing as
system of record. Hero flow: ensure Maxio customer (idempotent) → create subscription (idempotent)
→ confirm plan/price/state/next-billing-date. Endpoints: `GET /api/subscription-plans`,
`POST /api/subscriptions`, `GET /api/my-subscriptions`.

Configuration contract (binding): settings bound from the `Maxio:` section, keys exactly
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional — when
set, used verbatim as the API base address instead of deriving from subdomain). Values from
user-secrets/env, never hard-coded.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Reference NuGet package `AsadAli.AdvancedBilling.Sdk`; bind `Maxio:*` settings | — (config only) |
| 2 | Register the SDK client in DI: Basic auth (API key + `"x"`), subdomain → site, optional `Maxio:BaseUrl` → verbatim base-URL override | client construction |
| 3 | `GET /api/subscription-plans` — resolve product-family numeric id from configured handle (cache it), list its products, map to plan DTOs (name, handle, price, interval) | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — identity from JWT; idempotently ensure Maxio customer via unique `reference`; idempotently create the subscription with a deterministic `reference`; read back state/price/next-billing-date | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve customer by reference, list that customer's subscriptions, map state/price/next-billing-date | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary around every SDK call (Case A/B catch ladder + the two `JsonException` directions) | — |
| 7 | Tests for the integration layer | — |

Single active subscription per user+plan is the hero-flow assumption (§5); the deterministic
reference scheme below enforces it idempotently without any local persistence requirement.

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

### 2.1 Package & client construction (source: `sdk-map.md`)

- NuGet: **`AsadAli.AdvancedBilling.Sdk`** (map generated from tag `v1.0.2`, commit `15db14b`). Targets
  `netstandard2.0` — reference it directly from the .NET 8 project. **Do NOT** add a project reference to
  the SDK source or clone it into the solution.
- Root namespace: **`MaxioAdvancedBilling`** (differs from the package id — install by package id, import
  by namespace).

```csharp
using MaxioAdvancedBilling;                                  // client & options
using MaxioAdvancedBilling.Core.Authentication.Basic;        // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                          // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // Username = API key, Password = literal "x"
    Environment = ServerEnvironment.Us,                          // US-hosted site (default)
};
options.Server.Production.Us.Site = subdomain;                   // → https://{site}.chargify.com
// Maxio:BaseUrl set ⇒ used verbatim as the API base address instead of deriving from subdomain:
options.Server.Production.Us.BaseUrl = baseUrl;                  // e.g. "https://acme.chargify.com" or a dev/mock host
var client = new MaxioAdvancedBillingClient(httpClient, options); // ONLY constructor: (HttpClient, MaxioAdvancedBillingClientOptions)
```

- Constructor (verbatim, the only one): `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- `MaxioAdvancedBillingClientOptions` properties: `Environment` (`ServerEnvironment`), `Retry`
  (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`).
  Set `Server.*` by property access (`options.Server.Production.Us.Site` / `.BaseUrl`) — you never
  construct those types yourself, so no extra `using` is needed for them.
- DI alternative: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })`
  (`ServiceCollectionExtensions.cs`, root namespace). Every API group is a property on the client
  (`client.Customers`, `client.Subscriptions`, `client.ProductFamilies`, `client.Products`, `client.Components`).
- `RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`) has all-`required` members — build a full
  instance or start from `RetryOptions.Default()`.

### 2.2 Operations

| Controller property | Method signature (params in order) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — GET `/customers/lookup.json?reference=…` | — (query param `reference`) | `CustomerResponse` → `.Customer`: `Customer !req` — read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName/LastName`, `CreatedAt` | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`; **404 = no customer with that reference** (idempotency's "not found" signal) | none | `operations/Customers.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` → `.Customer: CreateCustomer !req`; `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization`, `Phone`, `City`, `State`, `Country`, `Zip` (all optional) | `CustomerResponse` → `.Customer` (`Id`, `Reference`, …) | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] — see ⚠ below on the 422 payload | none | `operations/Customers.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — GET `/customers/{customer_id}/subscriptions.json` | — | `IReadOnlyList<SubscriptionResponse>` (each wraps `Subscription?` — nullable, guard it) | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — POST `/subscriptions.json` | `CreateSubscriptionRequest` → `.Subscription: CreateSubscription !req`; fields used here: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `PaymentProfileId (payment_profile_id): int?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | `SubscriptionResponse` → `.Subscription: Subscription?` (**nullable — guard**) — read `Id`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Customer (customer): Customer?`, `Product (product): Product?` (has `Name`, `Handle`, `PriceInCents`) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (validation messages) · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` is nullable-with-no-default ⇒ **must pass explicitly** (pass `null`) | — | `SubscriptionResponse` → `.Subscription?` (same fields as above) | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — GET `/subscriptions/lookup.json?reference=…` | — (`reference` nullable-no-default ⇒ pass explicitly, may be `null`) | `SubscriptionResponse` → `.Subscription?` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] — 404 = no subscription with that reference | none | `operations/Subscriptions.md` |
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable-no-default ⇒ pass explicitly (`null` to skip) | — | `IReadOnlyList<ProductFamilyResponse>` → each `.ProductFamily: ProductFamily?` — read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` is a **string**; the 8 params `dateField`…`include` must pass explicitly (`null`) | — (optional `ListProductsFilter` record exists but has no family filter) | `IReadOnlyList<ProductResponse>` → each `.Product: Product !req` — read `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents/TrialInterval/TrialIntervalUnit`, `RequestCreditCard (request_credit_card): bool?`, `RequireCreditCard (require_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?` (has `Handle`) | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1/20) | `operations/ProductFamilies.md` |
| `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — GET `/products/handle/{api_handle}.json` | — | `ProductResponse` → `.Product: Product !req` | **Case B** `SdkException<RawError>` | none | `operations/Products.md` |
| `client.Components` | `FindComponent(string handle, CancellationToken ct = default)` — GET `/components/lookup.json?handle=…` (metered `api-call`, not needed for hero flow) | — | `ComponentResponse` → `.Component: Component !req` | **Case B** `SdkException<RawError>` | none | `operations/Components.md` |

### 2.3 Which product identifier to use when creating a subscription

The `CreateSubscription` Notes say it verbatim: specify the product with **`product_id` or
`product_handle`**; to pin a specific price point use **`product_price_point_handle` or
`product_price_point_id`**; identify the customer with **`customer_id` or `customer_reference`**.
Both seeded plans price on their **default price point** ⇒ subscribe with **`product_handle` alone**
(e.g. `"eshop-pro"` from the request body / plan catalog) and **`customer_id`** from the ensure-customer
step. Omitting `product_price_point_*` uses the product's default price point — exactly the $299/mo and
$29/mo configs. Do **not** send `product_id` (numeric IDs may be stale in the sandbox) — the handle is
the stable selector.

### 2.4 Idempotency contract (customer + subscription)

- **Customer `reference` semantics** (from the `CreateCustomer` Notes, `operations/Customers.md`):
  "you may only create one customer for a given reference value. If provided, the `reference` value
  must be unique. It represents a unique identifier for the customer from your own app." A lookup
  endpoint exists and returns a single exact match by reference: `ReadCustomerByReference`.
  ⇒ **Recommended scheme**: customer reference = a deterministic value derived from the eShopOnWeb user
  identity (e.g. `"eshopweb-user-{userId}"` from the JWT claims). Flow: `ReadCustomerByReference` →
  404 (Case B, `ex.Error.StatusCode`) ⇒ `CreateCustomer` with that `reference`; **on a 422 from the
  create (double-click race), do NOT parse the 422 body — re-run `ReadCustomerByReference` and use the
  customer it returns.** A double-click therefore can never produce two customers.
- **Subscription `reference`**: `CreateSubscription` accepts `Reference (reference): string?` and
  `FindSubscription(reference)` looks a subscription up by it. The Notes do **not** state that Maxio
  rejects a duplicate subscription reference — `UNVERIFIED` whether the server enforces uniqueness on
  subscription `reference`. Defensive directive (covers both worlds): subscription reference =
  deterministic per user+plan (e.g. `"eshopweb-sub-{userId}-{planHandle}"`); before creating, call
  `FindSubscription(reference)` — `TryGetNoContent` [404] ⇒ create; if create throws 422, re-run
  `FindSubscription` and treat a hit as "already subscribed" (idempotent success).
- `CreateSubscription` also accepts `CustomerReference (customer_reference): string?` — an alternative
  one-shot path that creates the subscription for the customer *by reference* without a prior lookup.
  If used, the ensure-customer step still needs the reference-lookup/create dance above; the plan keeps
  the explicit two-step (lookup/create, then `customer_id`) so the Maxio customer id is in hand for
  `GET /api/my-subscriptions`.

### 2.5 Enum values needed (namespace `MaxioAdvancedBilling.Models.Enums` — `StringEnum<T>`, NOT C# enums; write the literal member names, e.g. `SubscriptionState.Active`, or `SubscriptionState.FromValue("active")`)

| Enum | Members (C# name → wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` (only if `ListSubscriptions` is ever used) | `Active`, `Canceled`, `Expired`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended`, `TrialEnded (trial_ended)`, `Trialing`, `Unpaid` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `IntervalUnit` (product interval) | `Day (day)`, `Month (month)` |

### 2.6 Error surface

- Every operation is **throw-only** — the SDK generates **no** no-throw `…Result` variants. On an error
  status the SDK throws `SdkException<TError>` (`MaxioAdvancedBilling.Core.Exceptions`) exposing `.Error`.
- **Case A (typed)** — `TError` is a generated `…Error : ApiError` with status-specific `TryGet…(out …)`
  accessors plus inherited `TryGetRawError(out RawError)`. Case A in scope:
  `CreateCustomerError` (422 → `CustomerErrorResponse1`), `CreateSubscriptionError` (422 →
  `ErrorListResponse1`), `FindSubscriptionError` (404 → `TryGetNoContent`),
  `ListProductsForProductFamilyError` (404 → `TryGetString`).
- **Case B (raw)** — `TError` is `RawError` (`MaxioAdvancedBilling.Core.ErrorResponse`):
  `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` ·
  `ReadAsBytes(): ReadOnlyMemory<byte>`. Case B in scope: `ReadCustomerByReference`,
  `ListCustomerSubscriptions`, `ReadSubscription`, `ListProductFamilies`, `ReadProductByHandle`,
  `FindComponent`.
- **"Already exists" / conflict detection**: there are **no 409 shapes on any in-scope operation** —
  conflicts surface as **422** (`CreateCustomer`, `CreateSubscription`) or as a lookup hit on the
  reference endpoints. Detect idempotent-replay by **lookup** (`ReadCustomerByReference`,
  `FindSubscription`), never by parsing the 422 body.
- ⚠ **Map-visible trust warning on `CreateCustomerError`'s 422 payload**: `CustomerErrorResponse1` wraps
  `Errors (errors): Errors?`, and the generated `Errors` record has only two fields —
  `PerPage (per_page)` and `PricePoint (price_point)` — neither of which can carry a customer-validation
  message like "reference has already been taken". Two generated definitions disagree about what a
  customer-create 422 body looks like, so **do not build conflict logic on the typed 422 payload**;
  treat 422 as "create failed — re-resolve via `ReadCustomerByReference`". Whether the live wire really
  carries per-attribute errors there is `UNVERIFIED`; the fallback directive makes it moot.
- The two `JsonException` directions (mandatory rows):
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the `JsonException`
    **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
    maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
    and a caller that retries 5xx retries something that can never succeed.

### 2.7 Map-visible drift notes (grounded, each with its citation)

- `ReadProductFamily`'s Notes say the family "can be specified either with the id number, or with the
  `handle:my-family` format", but its signature is `ReadProductFamily(int id, …)` — a `handle:…`
  string cannot be passed to an `int`. **Do not use `ReadProductFamily` for handle-based lookup.**
  Resolve the numeric family id instead via `ListProductFamilies` (match `ProductFamily.Handle` to the
  configured `Maxio:ProductFamilyHandle`) and cache it. Whether `ListProductsForProductFamily`'s
  `string productFamilyId` accepts the `handle:` prefix is not stated in its Notes — `UNVERIFIED`;
  pass the cached numeric id as a string, which the signature unambiguously accepts.

---

## 3. Trap notes

> ⚠ Step 2 (client registration) — the SDK's retry/timeout options do **not** bound a whole call, are
> **not** the `HttpClient` timeout, and a transport failure is retried even on `POST` — so a
> non-idempotent write can execute more than once and no setting fully disables that. The deterministic
> reference scheme in §2.4 is what makes replays safe. **MUST load `dotnet-configuration-resilience`**
> before wiring the client.

> ⚠ Step 2 (credentials) — Maxio is HTTP Basic where the **username is the API key and the password is
> the literal `"x"`**; set credentials from the `Maxio:*` configuration binding, never hard-coded, and
> mind per-environment handling. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

> ⚠ Step 2 (DI/lifetime) — the `HttpClient`/handler pipeline must be long-lived and reused
> (IHttpClientFactory), not rebuilt per request; the SDK client wrapper over it may be transient.
> **MUST load `dotnet-client-initialization`** before registering the client.

> ⚠ Steps 3–5 (calls) — call list/search operations with **named arguments**: many optional parameters
> have no C# default and mis-bind positionally; several nullable-with-no-default parameters (e.g.
> `ReadSubscription`'s `include`, `CreateCustomer`'s `body`) must be passed explicitly even when `null`.
> **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

> ⚠ Steps 3–5 (models) — request models mark nothing required except the nested `.Customer` /
> `.Subscription` envelope members, so the compiler catches almost nothing you drop; enums are
> `StringEnum<T>` (not C# enums); `CustomerResponse`/`SubscriptionResponse` wrap their payload one level
> down and `SubscriptionResponse.Subscription` is nullable. **MUST load `dotnet-models`** before
> building request payloads or mapping responses.

> ⚠ Step 6 (error boundary) — Case A/B split per operation (§2.6), `TryGetRawError` is not a catch-all
> on typed errors, and the two `JsonException` directions (§2.6) need opposite handling; a boundary that
> maps every `JsonException` to a 5xx turns a deterministic 422 rejection into a retried "outage".
> **MUST load `dotnet-error-handling`** before writing any try/catch around an SDK call.

> ⚠ Step 7 (tests) — the `HttpClient` constructor argument is the test seam; match the project's
> existing test framework and assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry these skills' contents
(defaults, worked examples, semantics); it names each hazard and points here.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, DI registration, HttpClient lifetime |
| `dotnet-authentication` | Step 2 — Basic auth credential shape and per-environment handling |
| `dotnet-calling-endpoints` | Steps 3–5 — named arguments, must-pass-explicitly params, envelope unwrapping |
| `dotnet-models` | Steps 3–5 — request model construction, required members, StringEnum handling, nullable envelopes |
| `dotnet-error-handling` | Step 6 — Case A/B catch ladder, `JsonException` in both directions, status-code reading |
| `dotnet-configuration-resilience` | Step 2 — retries, what `Timeout` bounds, base-URL/server override, pagination |
| `dotnet-testing` | Step 7 — the HttpClient test seam and error-path coverage |

---

## 5. Assumptions & Blockers

- **Assumption** — the JWT caller identity yields a stable user id suitable for deriving the Maxio
  `reference` values (`"eshopweb-user-{userId}"`, `"eshopweb-sub-{userId}-{planHandle}"`). The exact
  claim to read and the reference formats are `YOUR CALL — not in the map` (application identity
  design); the *semantics* of Maxio `reference` (unique, lookup-able) come from
  `operations/Customers.md` and `operations/Subscriptions.md`.
- **Assumption** — one active subscription per user+plan is the hero-flow model; the deterministic
  subscription reference enforces it. Whether to allow plan changes/cancellations via
  `UpdateSubscription` is out of scope here.
- **Assumption** — the seeded sandbox products are configured payment-not-required as stated in the
  task, so `CreateSubscription` works with no `payment_profile_id`. The `CreateSubscription` Notes say
  payment "may be required … depending on the options for the Product being subscribed" — if a 422
  `ErrorListResponse1` reports a payment error, the sandbox product config, not the code, is wrong.
- **Blocker (none hard)** — no capability the hero flow needs is missing from the map; no open rows.
  Two `UNVERIFIED` items are folded into defensive directives (§2.4 subscription-reference uniqueness;
  §2.6 customer-422 body shape) and one into the §2.7 handle-prefix note — none blocks implementation.