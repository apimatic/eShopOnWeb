# Maxio Advanced Billing — Integration Contract Sheet (eShopOnWeb / ASP.NET Core)

Grounded against the bundled SDK map (`sdk-map.md` + `map/operations/*` + `map/models/*`),
SDK source commit `15db14b` / tag `v1.0.2`. Package `AsadAli.AdvancedBilling.Sdk`, root
namespace `MaxioAdvancedBilling`. Every fact below cites the map page it came from.

---

## 1. Scope & sequence

1. **Client + DI + auth** — register `MaxioAdvancedBillingClient` (Basic auth, site subdomain,
   optional explicit BaseUrl).  → `sdk-map.md` (Getting a client / Servers & auth)
2. **List plans in a family** — `ProductFamilies.ListProductsForProductFamily` (family id) or
   resolve the handle→id via `ProductFamilies.ListProductFamilies`. → `operations/ProductFamilies.md`
3. **Find-or-create customer** — `Customers.ReadCustomerByReference` then `Customers.CreateCustomer`.
   → `operations/Customers.md`
4. **Find-or-create subscription** — `Customers.ListCustomerSubscriptions` (existence check),
   then `Subscriptions.CreateSubscription`. → `operations/Customers.md`, `operations/Subscriptions.md`
5. **Read confirmation fields** off `SubscriptionResponse.Subscription`. → `records-4-Su-We.md`,
   `records-3-Of-Su.md`
6. **My-subscriptions view** — `Customers.ListCustomerSubscriptions`. → `operations/Customers.md`
7. **Error boundary** — `SdkException<TError>` (Case A typed / Case B raw). → `sdk-map.md` (Error-handling model)

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

### 2a. Namespaces in scope (add a separate `using` per kind — C# does not import child namespaces transitively)

| Type(s) | Namespace | Source hint |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | root |
| `AddMaxioAdvancedBillingClient` (DI ext.) | `MaxioAdvancedBilling` | `ServiceCollectionExtensions.cs` |
| Controllers (`Customers`, `Subscriptions`, `Products`, `ProductFamilies`) | `MaxioAdvancedBilling.Api` | `Api/` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `Core/Authentication/Basic/` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Servers/ServerEnvironment.cs` |
| Records (`Customer`, `CreateCustomer`, `CreateCustomerRequest`, `CustomerResponse`, `Product`, `ProductResponse`, `ProductFamily`, `ProductFamilyResponse`, `Subscription`, `CreateSubscription`, `CreateSubscriptionRequest`, `SubscriptionResponse`, `ErrorListResponse1`, `CustomerErrorResponse1`) | `MaxioAdvancedBilling.Models` | `Models/` |
| Enums (`ServerEnvironment` excepted): `CollectionMethod`, `IntervalUnit`, `SubscriptionState`, `SubscriptionStateFilter`, `SortingDirection`, etc. | `MaxioAdvancedBilling.Models.Enums` | `Models/Enums/` |
| Error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` | `Errors/` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `Core/ErrorResponse/` |
| `RetryOptions` (if tuning resilience) | `MaxioAdvancedBilling.Core.Configuration` | `Core/Configuration/RetryOptions.cs` |

### 2b. Client construction, auth, environment, base URL  (source: `sdk-map.md`)

- **Constructor (only one):** `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- **DI:** `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`).
- **Auth — HTTP Basic only:** `options.BasicAuth = new BasicAuthCredentials { Username = "<API_KEY>", Password = "x" }`.
  Username = your Maxio/Chargify **API key**; Password = the **literal string** `"x"`. (`options.BasicAuth` is `BasicAuthCredentials?`.)
- **Environment enum `ServerEnvironment`** (`Servers/ServerEnvironment.cs`): members `Us` (wire `US`, default) and `Eu` (wire `EU`). **There is no "Sandbox" environment value.** In Maxio, a sandbox/test site is just a different **site subdomain** (test mode), reached through the site setting below — not through `ServerEnvironment`.
- **Site subdomain:** `options.Server.Production.Us.Site = "<your-subdomain>"` → resolves the US template `https://{site}.chargify.com`. (EU template: `https://{site}.ebilling.maxio.com`.)
- **Explicit BaseUrl override:** `options.Server.Production.Us.BaseUrl = "<explicit-url>"` (e.g. a mock/dev host). Set this instead of `Site` when you have an explicit URL.
- **Options bag** `MaxioAdvancedBillingClientOptions`: `Environment: ServerEnvironment`, `Server: ServerOptions`, `Retry: RetryOptions`, `BasicAuth: BasicAuthCredentials?`.

### 2c. Operations

| # | Controller.Op (accessor) | Signature (params in order; `ct` last) | Request model + fields used | Response envelope → inner fields read | Error case + accessors (payload) | Pagination | Map page |
|---|---|---|---|---|---|---|---|
| 2 | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 middle params are nullable-no-default → pass explicitly (`null` to skip); call with **named args** | none (path + query only). `productFamilyId` is the numeric family id as a string | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product` (**required**, non-null) | Case A `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 1 / 20) | `operations/ProductFamilies.md` |
| 2 (alt) | `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable-no-default → pass explicitly | none | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` (nullable) → `Id (int?)`, `Handle (string?)`, `Name (string?)` | Case B `SdkException<RawError>` | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 3-read | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference ← reference` | none | `CustomerResponse.Customer` (**required**) → `Id (int?)`, `Reference (string?)`, `FirstName`, `LastName`, `Email` | Case B `SdkException<RawError>` — **not-found is an EXCEPTION, not null** (see §3) | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| 3-create | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable-no-default → pass explicitly | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer` required: `FirstName (first_name) !req`, `LastName (last_name) !req`, `Email (email) !req`; optional idempotency key `Reference (reference): string?` | `CustomerResponse.Customer` (**required**) → `Id (int?)`, `Reference (string?)` | Case A `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 4-check | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — path `customer_id ← customerId` | none | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` (nullable) → `State (SubscriptionState?)`, `Product (Product?)` → `Product.Handle` | Case B `SdkException<RawError>` | none | `operations/Customers.md`, `records-4-Su-We.md`, `records-3-Of-Su.md` |
| 4-create | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable-no-default → pass explicitly | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`; on `CreateSubscription` set customer via `CustomerReference (customer_reference): string?` **or** `CustomerId (customer_id): int?`; product via `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`; `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (see §4 note) | `SubscriptionResponse.Subscription` (**nullable** — null-check) → `Id (int?)`, `State`, `Product` | Case A `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| 5/6 | (read confirmation off `SubscriptionResponse` returned by CreateSubscription/ListCustomerSubscriptions) | — | — | `SubscriptionResponse.Subscription` (**nullable**) → fields in §2e | — | — | `records-4-Su-We.md`, `records-3-Of-Su.md` |

Notes on the response envelopes (all `records-*` pages): responses wrap their payload one level down.
`ProductResponse` = `Product (product): Product !req` · `CustomerResponse` = `Customer (customer): Customer !req` ·
`ProductFamilyResponse` = `ProductFamily (product_family): ProductFamily?` · `SubscriptionResponse` = `Subscription (subscription): Subscription?` (**nullable** — unlike the Product/Customer envelopes).

### 2d. `Product` record fields read for the plans list  (source: `records-3-Of-Su.md`, `Models/Product.cs`)

| C# property (wire) | Type |
|---|---|
| `Id (id)` | `int?` |
| `Name (name)` | `string?` |
| `Handle (handle)` | `string?` |
| `PriceInCents (price_in_cents)` | `long?` — **yes, price is in cents** |
| `Interval (interval)` | `int?` |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` (enum: `Day`/`Month`) |
| `ProductFamily (product_family)` | `ProductFamily?` (→ `.Handle`, `.Id` for family filtering) |

### 2e. `Subscription` record — confirmation fields  (source: `records-3-Of-Su.md`, `Models/Subscription.cs`)

| Need | C# property (wire) | Type |
|---|---|---|
| plan/product handle + name | `Product (product): Product?` → `Product.Handle`, `Product.Name` | `Product?` |
| price | `ProductPriceInCents (product_price_in_cents)` | `long?` (cents). Also `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` for the amount billed now |
| state | `State (state)` | `SubscriptionState?` (enum — values in §2f) |
| current period end | `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` |
| next assessment (renewal charge) | `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` |
| period start | `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` |

**Contract fact — there is NO `next_billing_at` field on the `Subscription` response.** The
`Subscriptions.UpdateSubscription` note states the server does not return `next_billing_at`; use
`CurrentPeriodEndsAt` to read the next billing / current period end. (source: `operations/Subscriptions.md` UpdateSubscription notes; `Models/Subscription.cs`)

### 2f. Enums in scope  (source: `map/models/enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`)

`StringEnum<T>` records — **not** C# enums. Build via static member (`CollectionMethod.Automatic`) or `T.FromValue("wire")`; the C# member name is the identifier to write.

- **`SubscriptionState`** (`Models/Enums/SubscriptionState.cs`): `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.
- **`CollectionMethod`** (`Models/Enums/CollectionMethod.cs`): `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. (Relationship Invoicing valid: remittance/automatic/prepaid; legacy Statements: invoice/automatic.)
- **`IntervalUnit`** (`Models/Enums/IntervalUnit.cs`): `Day (day)`, `Month (month)`.
- **`ServerEnvironment`** (`Servers/ServerEnvironment.cs`, namespace `MaxioAdvancedBilling.Servers`): `Us (US)`, `Eu (EU)`.
- (If filtering a general subscription list) **`SubscriptionStateFilter`** and **`SortingDirection (Asc/Desc)`** — but the customer+product existence check uses `ListCustomerSubscriptions` (no filter enums needed).

### 2g. Error handling contract  (source: `sdk-map.md` Error-handling model)

- Every operation is **throw-only** (no `…Result`/no-throw variants exist in this SDK). Wrap every call.
- On an error status the SDK throws **`SdkException<TError>`** (`MaxioAdvancedBilling.Core.Exceptions`), exposing `.Error` of type `TError`. You must catch the **closed generic per operation** — `SdkException<RawError>` and `SdkException<CreateCustomerError>` are different catch types.
- **Case B (raw)** — `TError` = `RawError` (`MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. This covers `ReadCustomerByReference`, `ReadCustomer`, `ListCustomers`, `ListCustomerSubscriptions`, `ListProducts`, `ReadProduct`, `ReadProductByHandle`, `ListProductFamilies`, `ReadProductFamily`, `ListSubscriptions`, `ReadSubscription`.
- **Case A (typed)** — `TError` = a generated `{Op}Error : ApiError` with status-specific `TryGet…(out …)` accessors plus inherited `TryGetRawError(out RawError)`. To read the **HTTP status** on a Case-A exception, call `TryGetRawError(out var raw)` then `raw.StatusCode`, or branch on the typed accessor.
  - `CreateCustomer` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422].
  - `CreateSubscription` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` (the human-readable validation messages).
  - `ListProductsForProductFamily` → `TryGetString(out string)` [404].
- **401 (auth failure)**: not a dedicated typed accessor on these ops — it surfaces through the `RawError` fallback (Case B directly, or `TryGetRawError` on a Case-A op) with `StatusCode == HttpStatusCode.Unauthorized`. Check credentials/base-URL first (see trap notes).

---

## 3. Not-found signalling for "read customer by reference" (item 7 specific)

`ReadCustomerByReference` is **Case B**. A miss does **not** return `null` — it **throws
`SdkException<RawError>`** with `ex.Error.StatusCode == HttpStatusCode.NotFound` (404). So the
find-or-create must be `try { read } catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound) { create }`.
(source: `operations/Customers.md` ReadCustomerByReference — Case B, no null variant.)

`UNVERIFIED (live-traffic only): the exact status Maxio returns for an unknown reference on
/customers/lookup.json.` The map fixes the type (RawError with a `StatusCode`) but not the
numeric value. Directive: treat **any** non-2xx from `ReadCustomerByReference` whose
`StatusCode` is `NotFound` **or** where the returned customer is otherwise absent as
"not found → create", and let other statuses (401/5xx) propagate; read `StatusCode` off
`RawError`, never by parsing the exception message.

---

## 4. Subscription "no payment required" — field is known, value choice is site-dependent

The create-subscription request carries `PaymentCollectionMethod (payment_collection_method):
CollectionMethod?` on `CreateSubscription`. Omitting all payment/card attributes
(`CreditCardAttributes`, `PaymentProfileAttributes`, `PaymentProfileId`) is what makes a no-card
signup; whether the site accepts it depends on the product's `RequestCreditCard`/`RequireCreditCard`
flags and the site architecture. (source: `records-2-Cr-Ne.md` `CreateSubscription`; `records-3-Of-Su.md` `Product`; `enums.md` `CollectionMethod`.)

`UNVERIFIED (live-traffic / site-config only): which CollectionMethod value yields a clean
no-payment signup for this specific site.` The map documents the enum and the field but not the
site's product settings. Directive: for a card-less signup set no card attributes and use
`CollectionMethod.Remittance` (Relationship Invoicing) or `CollectionMethod.Invoice` (legacy
Statements); catch the 422 `SdkException<CreateSubscriptionError>` and surface
`ErrorListResponse1.Errors` (the messages) rather than assuming success — a site that still
requires payment info returns 422 here.

`UNVERIFIED (endpoint-specific): whether ListProductsForProductFamily accepts a
"handle:<handle>" value in its {product_family_id} path segment.` The `handle:` prefix form is
documented on `ReadProductFamily` (source: `operations/ProductFamilies.md`), not on this op.
Directive: to list plans from a family **handle**, first call `ListProductFamilies`, match on
`ProductFamily.Handle`, take its numeric `Id`, and pass `Id.ToString()` to
`ListProductsForProductFamily` — a deterministic path that avoids the ambiguity. There is no
by-handle list overload; the only path parameter is the string `productFamilyId`.

**Existence check before create (item 4):** `Subscriptions.ListSubscriptions` has **no customer
filter** (its filters are `state`, `product`, `productPricePointId`, `coupon`, `couponCode`,
dates, `metadata`, `direction`, `sort`, `include`). To find a customer's existing active
subscription for a product, use `Customers.ListCustomerSubscriptions(customerId)` and filter
client-side on `Subscription.Product.Handle` and `Subscription.State == SubscriptionState.Active`.
(source: `operations/Subscriptions.md`, `operations/Customers.md`.)

---

## 5. Trap notes (load the companion skill at the step where each bites)

- ⚠ Step 1 (client + DI) — the `HttpClient`/handler pipeline lifetime and whether the SDK client wrapper is transient vs singleton are not shown by the constructor; getting it wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient(...)`.
- ⚠ Step 1 (auth) — where/when Basic credentials must be set relative to client construction, and loading the key from config vs hardcoding. **MUST load `dotnet-authentication`** before setting `BasicAuth`.
- ⚠ Step 2/4 (list/search calls) — the many nullable-no-default params on `ListProductsForProductFamily`, `ListProductFamilies`, `ListCustomerSubscriptions` bind wrong in a positional call. **MUST load `dotnet-calling-endpoints`** before the first list call.
- ⚠ Step 3/4 (building requests, reading responses) — `CollectionMethod`/`IntervalUnit`/`SubscriptionState` are `StringEnum<T>` (not C# enums); unmodeled JSON fields are dropped on deserialize; `SubscriptionResponse.Subscription` is nullable. **MUST load `dotnet-models`** before constructing `CreateSubscription`/`CreateCustomer` or mapping responses.
- ⚠ Step 1 (resilience) — the SDK retry/timeout options do **not** bound a whole call and are **not** the `HttpClient` timeout; whether a failed non-idempotent write (e.g. `CreateSubscription` POST) can be re-sent is not visible from the option names. **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` / registering the client.
- ⚠ Step 7 (error boundary) — you catch a **closed generic per operation**; `SdkException<RawError>` and `SdkException<CreateCustomerError>` are distinct types, and `TryGetRawError` is not a catch-all on the typed errors. **MUST load `dotnet-error-handling`** before writing the try/catch ladder.
- ⚠ Step 7 (testing) — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 6. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic credentials wiring, config sourcing |
| `dotnet-calling-endpoints` | Steps 2/4 — named-argument calls, request/response envelopes, cancellation |
| `dotnet-models` | Steps 3/4/5 — StringEnum, nullability/required, dropped unmodeled fields |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts semantics, base-URL/server selection, pagination |
| `dotnet-error-handling` | Step 7 — which exceptions actually reach catch, reading status/body safely |
| `dotnet-testing` | Step 7 — the HttpClient seam, error/edge coverage |

**Mandatory `System.Text.Json.JsonException` hazard rows (the boundary is written early — these belong here, not a later revision):**

- A drifted or malformed **2xx** body (e.g. a missing `required` member such as
  `ProductResponse.Product` / `CustomerResponse.Customer`) surfaces as a
  `System.Text.Json.JsonException` from **deserialization**, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape (e.g.
  a `CreateSubscriptionError` / `CreateCustomerError` whose payload differs from the generated
  record) throws `JsonException` **while the error object is being constructed**, so the
  `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a
  boundary that maps every `JsonException` to a 5xx then reports a deterministic 4xx rejection as
  an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

`UNVERIFIED (live-traffic only): whether the 422 CreateCustomer payload actually deserializes
into CustomerErrorResponse1.` Evidence from the map: `CustomerErrorResponse1.Errors` is typed as
the generated `Errors` record whose only fields are `PerPage (per_page)` and `PricePoint
(price_point)` — a suspicious/generic shape that does not obviously match customer validation
messages (contrast `CreateSubscription`'s `ErrorListResponse1.Errors: IReadOnlyList<string>`,
which clearly carries messages). Directive: when handling a 422 from `CreateCustomer`, extract
best-effort from `CustomerErrorResponse1` but **fall back to `TryGetRawError(out var raw)` →
`raw.ReadAsString()`** for the human-readable body, and never assume the typed accessor populated
the message. (source: `records-2-Cr-Ne.md` `CustomerErrorResponse1`/`ErrorListResponse1`; `records-2-Cr-Ne.md` `Errors`.)

---

## 7. Assumptions & Blockers

- **Assumption:** "environment (sandbox)" means a Maxio test-mode **site**, addressed via the site
  subdomain (`options.Server.Production.Us.Site`) plus optional explicit `BaseUrl` — there is no
  `Sandbox` value on `ServerEnvironment` (only `Us`/`Eu`). Confirm the sandbox is a US-hosted site;
  if EU-hosted, use `ServerEnvironment.Eu` and the `.ebilling.maxio.com` template.
- **Assumption:** `reference` (customer) will be set to a stable eShopOnWeb identifier (user id or
  email) and is globally unique per site — required for the read-by-reference idempotency to work
  (the API allows only one customer per reference value).
- **Assumption:** the target site is on Relationship Invoicing (affects the valid `CollectionMethod`
  for card-less signups — see §4); verify before choosing `Remittance` vs `Invoice`.
- **No blockers** — every operation, model, enum, and error type in scope was resolved from the map.
- Three facts are labeled **UNVERIFIED** (live-traffic / site-config only) with defensive directives:
  the exact not-found status for `ReadCustomerByReference` (§3), the `CollectionMethod` value for a
  no-payment signup and the `handle:` path form for `ListProductsForProductFamily` (§4), and the
  `CustomerErrorResponse1` 422 deserialization (§6).
