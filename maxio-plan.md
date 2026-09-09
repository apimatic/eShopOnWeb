# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

SDK: `AsadAli.AdvancedBilling.Sdk` (NuGet) · root namespace `MaxioAdvancedBilling` · `netstandard2.0` · map stamp: source commit `15db14b` (tag `v1.0.2`).

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Client registration + config binding (`Maxio:ApiKey`, `Maxio:Subdomain`, optional `Maxio:BaseUrl`) | client construction only (§Contract sheet: client & auth) |
| 2 | **EnsureCustomer(userId)** — idempotent Maxio customer keyed by reference `eshop-{userId}` | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, fallback `Customers.ListCustomers` (`q` search) |
| 3 | **ListPlans()** — plans are Maxio Products in the family whose handle is `Maxio:ProductFamilyHandle` | `ProductFamilies.ListProductFamilies` (match handle client-side), `ProductFamilies.ListProductsForProductFamily` |
| 4 | **Subscribe(userId, productHandle)** — validate plan, dedupe, create | `Products.ReadProductByHandle`, `Subscriptions.FindSubscription` (pre-check), `Subscriptions.CreateSubscription` |
| 5 | **ListUserSubscriptions(userId)** — user → customer → subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |

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

### Client & auth (source: `sdk-map.md` §Getting a client / §Servers & auth; `ServerOptions.cs`, `ProductionOptions.cs`, `SdkException.cs`)

```csharp
using MaxioAdvancedBilling;                              // client, options, ServerOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;    // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                      // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    Environment = ServerEnvironment.Us,                  // sandbox sites are hosted on the US host
    BasicAuth = new BasicAuthCredentials { Username = "<api key>", Password = "x" },
};
// {site} in the US base-URL template https://{site}.chargify.com defaults to the LITERAL string
// "subdomain" — you MUST set Site to the real subdomain or every call hits the wrong host:
options.Server.Production.Us.Site = "<Maxio:Subdomain>";
// Only when a base-URL override is configured (bind Maxio:BaseUrl):
// options.Server.Production.Us.BaseUrl = "<override host>";
var client = new MaxioAdvancedBillingClient(httpClient, options);   // sole ctor: (HttpClient, MaxioAdvancedBillingClientOptions)
```

- `MaxioAdvancedBillingClientOptions` properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`). `ServerOptions.Production` and `ProductionOptions.Us` are non-null by default — set `options.Server.Production.Us.Site` in place, do not reassign.
- DI alternative: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })`.
- Auth is **HTTP Basic only**: `Username` = API key, `Password` = the literal `"x"`.
- Namespaces: controllers `MaxioAdvancedBilling.Api` · records `MaxioAdvancedBilling.Models` · enums `MaxioAdvancedBilling.Models.Enums` · error classes `MaxioAdvancedBilling.Errors` · `SdkException<TError>` in `MaxioAdvancedBilling.Core.Exceptions` · `RawError` in `MaxioAdvancedBilling.Core.ErrorResponse` · `RetryOptions` in `MaxioAdvancedBilling.Core.Configuration`. C# does not import child namespaces transitively — add a `using` per kind of type referenced.
- Exceptions are throw-only: every operation throws `SdkException<TError>` with `.Error`; there are **no** no-throw `…Result` variants in this SDK.

### Operations

Signature convention below: `body`/nullable-with-no-default parameters **must be passed explicitly** (pass `null` to skip); `ct` always has `= default`.

| # | Operation | Signature (params in order) | Request model + fields (`Name (wire_name): type, required?`) | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 | Create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` → `CustomerResponse` | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer !req` — inner `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, `Address/Address2/City/State/Zip/Country/Phone (address…): string?`, `Locale (locale): string?` | `CustomerResponse.Customer` (`MaxioAdvancedBilling.Models.Customer`): `Id (id): int?`, `Reference (reference): string?`, `FirstName/LastName/Email: string?` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 2 | Lookup customer by reference (idempotency probe) | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` → `CustomerResponse` | — (query param `reference` ← `reference`) | same as row 1 | **Case B** `SdkException<RawError>`: `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` — 404 reads as `StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| 3 | Customer search fallback (email) | `client.Customers.ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` → `IReadOnlyList<CustomerResponse>` | — — all 7 nullable params (`direction`…`q`) must be passed explicitly, `null` to skip; `q` searches by email, reference, name, organization, ID | `IReadOnlyList<CustomerResponse>` as row 1 | **Case B** `SdkException<RawError>` | manual `page`/`perPage` (defaults 1/50) | `operations/Customers.md` |
| 4 | List product families (resolve family by handle — no by-handle lookup exists) | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` → `IReadOnlyList<ProductFamilyResponse>` | — — all 5 nullable params must be passed explicitly, `null` to skip | `ProductFamilyResponse.ProductFamily` (`MaxioAdvancedBilling.Models.ProductFamily`, nullable): `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `ArchivedAt (archived_at): DateTimeOffset?` — **match `Handle` against the configured family handle client-side** | **Case B** `SdkException<RawError>` | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 5 | List products in family | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` → `IReadOnlyList<ProductResponse>` | — — `productFamilyId` is a **string** (pass the family `Id` from row 4 as string); all 8 nullable params (`dateField`…`include`) must be passed explicitly, `null` to skip | `IReadOnlyList<ProductResponse>` — `ProductResponse.Product` (`MaxioAdvancedBilling.Models.Product`): `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductPricePointName (product_price_point_name): string?`, `ProductPricePointHandle (product_price_point_handle): string?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`, `Description (description): string?` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual `page`/`perPage` (defaults 1/20 — page until a short page) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 6 | Read product by handle (validate plan handle before subscribe) | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse` | — | as row 5 | **Case B** `SdkException<RawError>` (404 → `StatusCode == HttpStatusCode.NotFound`) | none | `operations/Products.md` |
| 7 | Create subscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` → `SubscriptionResponse` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req` — inner `MaxioAdvancedBilling.Models.CreateSubscription` (nothing is `!req`; build from these): `ProductHandle (product_handle): string?` *(or `ProductId (product_id): int?`)*, `CustomerReference (customer_reference): string?` *(or `CustomerId (customer_id): int?`)*, `Reference (reference): string?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `ProductPricePointHandle (product_price_point_handle): string?`, `CustomPrice (custom_price): SubscriptionCustomPrice?` *(union-typed inner fields — see notes)*, `DeferSignup (defer_signup): bool? = false`, `CouponCode (coupon_code): string?` | `SubscriptionResponse.Subscription` (`MaxioAdvancedBilling.Models.Subscription`, nullable): `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `Product (product): Product?` → `Handle`/`Name`/`PriceInCents`, `Customer (customer): Customer?` → `Id`, `BalanceInCents (balance_in_cents): long?`, `Reference (reference): string?` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` |
| 8 | Find subscription by reference (dedupe probe) | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` → `SubscriptionResponse` | — — `reference` nullable, no default → pass it explicitly (query param `reference` ← `reference`) | as row 7 | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md` |
| 9 | List a customer's subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` → `IReadOnlyList<SubscriptionResponse>` | — | `IReadOnlyList<SubscriptionResponse>` as row 7 | **Case B** `SdkException<RawError>` (404 → `StatusCode == HttpStatusCode.NotFound`) | none (returns all) | `operations/Customers.md` |

### Notes the contract depends on (from the map's Notes cells)

- **Customer idempotency is a documented contract:** CreateCustomer Notes — *"you may only create one customer for a given reference value. If provided, the `reference` value must be unique."* So a duplicate `reference` returns **422**. Pattern: `ReadCustomerByReference("eshop-{userId}")` → if 404, `CreateCustomer` with that `Reference` → if the create 422s (concurrent double-click), re-read by reference and return the winner. Do **not** interpret every 422 as "already exists" — verify via the reference lookup.
- **Subscription create identification:** per CreateSubscription Notes, identify the customer with `customer_id` **or** `customer_reference`, and the product with `product_id` **or** `product_handle`. Since step 2 already resolved the numeric customer id, pass `CustomerId` (int) and `ProductHandle` (string — stable across re-seeds).
- **Payment not required** depends on the *product's* configuration (CreateSubscription Notes: "Payment information may be required … depending on the options for the Product being subscribed"). No payment fields need to be sent when the product is configured that way; the product's flags are readable on the response (`RequireCreditCard`, `RequestCreditCard`) so the plan-listing endpoint can surface a misconfigured plan. **LIVE 2026-09-09:** sandbox subscribe (`ProductHandle`+`CustomerId`+`Reference`) returned 422 *"No payment method was on file for the $299.00 balance"* (read via `TryGetErrorListResponse1`) — the sandbox product **is** demanding a payment method. **Grounded finding (map + source): no `CreateSubscription` field is documented to bypass this requirement.** `PaymentCollectionMethod`'s doc covers only which values are valid per architecture (Statements: `invoice`/`automatic`; Relationship Invoicing: `remittance`/`automatic`/`prepaid`) — no default, no bypass; `DeferSignup: true` only creates in Awaiting Signup state (no documented lift of the payment requirement); `NextBillingAt` in the future is documented as "no payment will be captured at all" but is an import-flow field, not a payment-requirement bypass. Sending `PaymentCollectionMethod = CollectionMethod.Remittance` as a bypass is **YOUR CALL — not in the map** (may or may not clear the 422; only live traffic can confirm). The map-grounded fix is the **product's site configuration** (set the product payment-not-required), not the request payload.
- **Subscription create idempotency:** the map documents a uniqueness guarantee **only for customer `reference`**. `CreateSubscription` accepts a `Reference` and `FindSubscription` looks a subscription up by it, but nothing in the map states Maxio rejects duplicate subscription references server-side. **UNVERIFIED.** Defensive pattern: set `Reference` to a deterministic per-user-per-plan value, probe `FindSubscription(reference)` first, and treat a create-422 as *probably* duplicate → re-probe before surfacing an error.
- **"Next billing date" on the response:** the `Subscription` record has **no** `next_billing_at` field. Read `CurrentPeriodEndsAt` (and `NextAssessmentAt`) — that is the map-visible next-billing surface. Likewise current period = `CurrentPeriodStartedAt`/`CurrentPeriodEndsAt`.
- **No find-product-family-by-handle operation exists.** `ReadProductFamily(int id)` takes only an `int` — its Notes mention a `handle:my-family` path form, but the generated signature cannot express it. Handle resolution is therefore: `ListProductFamilies` (returns all families, no pagination) + client-side `Handle` match. This is the grounded path, not an invented one.
- `SubscriptionCustomPrice` (only if you ever override price per subscription) has union-typed required fields (`PriceInCents (price_in_cents): PriceInCents !req` (union), `Interval (interval): Interval !req` (union)) built via factories `PriceInCents.Long(long)` / `Interval.Int(int)` — not needed when subscribing at the product's default price point.

### Enums (namespace `MaxioAdvancedBilling.Models.Enums` — `StringEnum<T>` records, use the static members; not C# enums)

| Enum | Values `CSharpMember (wire_value)` |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |

### Error boundary facts (applies to every row)

- **Case A ops** throw `SdkException<{Operation}Error>` (`MaxioAdvancedBilling.Errors`): use the row's typed `TryGet…` first, then the inherited `TryGetRawError(out RawError)` fallback for every other status. `TryGetRawError` is the fallback on typed errors — it is not a catch-all that replaces the typed accessors.
- **Case B ops** throw `SdkException<RawError>` (`MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.
- **401/403** (auth) and **404** on Case B ops are not modeled shapes — they arrive as `RawError`; read `ex.Error.StatusCode` (`HttpStatusCode`), body via `ReadAsString()`.
- **422 payloads:** CreateSubscription's 422 is cleanly readable (`ErrorListResponse1.Errors: IReadOnlyList<string>`). CreateCustomer's typed 422 shape (`CustomerErrorResponse1.Errors` of type `Errors`) models only `per_page`/`price_point` keys — a duplicate-reference or email validation message is **not** representable in the typed shape. **Defensive directive (map-visible gap):** on customer-create 422 extract best-effort and fall back to `TryGetRawError().ReadAsString()`; do not branch business logic on the typed `Errors` record's contents. **UNVERIFIED** live body shape.
- **422 is deterministic, not an outage:** never map it to a retryable/5xx path.

## 3. Trap notes (attached to the step where they bite)

- ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call the way an `HttpClient` timeout does, and `HttpClient`/handler lifetime rules determine whether retry policy survives across requests; a wrong registration can drop retry state or pin sockets. **MUST load `dotnet-client-initialization`** before wiring the client into DI.
- ⚠ Step 1 (auth) — Maxio is Basic with a non-obvious credential mapping (username = API key, password = literal `"x"`); where and when credentials are set relative to client construction matters, and the key must come from configuration, never a literal. **MUST load `dotnet-authentication`** before setting `BasicAuth`.
- ⚠ Step 1 (resilience) — whether a failed **write** (customer create / subscription create) can be re-sent safely is decided by which verbs and triggers the SDK's retry machinery actually covers; a non-idempotent create may execute more than server-side once despite "retry" settings you did not choose. **MUST load `dotnet-configuration-resilience`** before tuning or accepting retry defaults.
- ⚠ Steps 2–5 (calls) — the list/search operations take many optional parameters that have **no C# default** and mis-bind in a positional call; required-but-nullable params must be passed explicitly. **MUST load `dotnet-calling-endpoints`** before the first `client.{Controller}.{Operation}(...)` call.
- ⚠ Steps 2–5 (models) — enums are `StringEnum<T>` built from static members (write `SubscriptionState.Active`, never `"active"` in place), response envelopes wrap their payload one level down (`.Customer`, `.Product`, `.Subscription`), and fields absent from a response deserialize to `null` rather than erroring. **MUST load `dotnet-models`** before constructing request payloads or mapping responses.
- ⚠ Steps 2–5 (errors) — Case A vs Case B differ per operation (rows above), typed error payloads can silently drop unmodeled fields (customer 422), and the JsonException hazards below reach the boundary from two directions. **MUST load `dotnet-error-handling`** before writing any try/catch around an SDK call.
- ⚠ Tests — the SDK client takes the `HttpClient` as its first constructor argument; that is the seam to fake. **MUST load `dotnet-testing`** before stubbing the SDK in tests.

## 4. REQUIRED READING (load before implementation starts — the sheet deliberately does not carry these skills' contents)

- `dotnet-client-initialization` · Step 1 — client construction, `HttpClient` ownership/handler lifetime, DI registration.
- `dotnet-authentication` · Step 1 — Basic credentials shape (API key / `"x"`), loading the key from configuration.
- `dotnet-configuration-resilience` · Step 1 — retries, what `Timeout` actually bounds, base-URL/site override semantics, pagination behaviour.
- `dotnet-calling-endpoints` · Steps 2–5 — named-argument discipline, required-but-nullable parameters, async usage, cancellation.
- `dotnet-models` · Steps 2–5 — request builders, `StringEnum<T>` members, envelope unwrapping, wire names vs C# names.
- `dotnet-error-handling` · Steps 2–5 — Case A/B catch ladder, `TryGet…` accessors, the JsonException hazards below.
- `dotnet-testing` · tests — faking at the `HttpClient` constructor seam, covering error paths.

Both of these hazards are load-bearing for the error boundary:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Blockers:** none — all four features map onto existing documented operations.

**Assumptions (proceeding):**

- Sandbox site is US-hosted: `ServerEnvironment.Us` + `Production.Us.Site` = subdomain. If the account is EU-hosted, switch to `Environment = ServerEnvironment.Eu` and set `Production.Eu.*` — one-line change, no contract impact.
- Plans are Maxio **Products** inside the single product family whose handle is bound to `Maxio:ProductFamilyHandle`; products are seeded with no trial, no setup fee, and payment not required. `RequireCreditCard`/`RequestCreditCard` on the Product record are readable, so plan listing can surface a mis-seeded plan; first live subscribe confirms. (First-subscribe confirmation is an environment check, not an SDK gap.)
- Reference formats — customer `eshop-{userId}`, subscription reference per user+plan — are **YOUR CALL — not in the map** (application design); the map only requires the customer reference to be unique.
- `Maxio:BaseUrl`, when set, overrides `options.Server.Production.Us.BaseUrl`; otherwise only `Site` is set.
- `ListCustomerSubscriptions` returns all of a customer's subscriptions with no pagination — fine for per-user listing; re-verify only if a user's subscription count grows large.

**UNVERIFIED (live-traffic only):**

- Whether Maxio enforces uniqueness on subscription `reference` server-side (map documents it only for customers). Dedupe relies on the `FindSubscription` pre-check + 422 re-probe pattern.
- Exact live 422 body shape for duplicate customer reference (typed shape provably models only two unrelated keys). Extract best-effort, fall back to the raw string.
- That sandbox products are seeded with payment-not-required.
