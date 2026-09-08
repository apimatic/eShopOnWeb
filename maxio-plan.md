# Maxio Advanced Billing integration plan — eShopOnWeb `src/PublicApi`

Scope: browse subscription plans, find-or-create the Maxio customer for the authenticated user, subscribe that
customer to a chosen plan (idempotent), list the caller's subscriptions. Sandbox `cp-exp-7` is **already
seeded** — nothing below creates products/families/price points/components.

Package: NuGet `AsadAli.AdvancedBilling.Sdk`, version `1.0.2` (map stamp: source commit `15db14b`, tag `v1.0.2`).
Add with `dotnet add package AsadAli.AdvancedBilling.Sdk` (root namespace to import is `MaxioAdvancedBilling`).
SDK targets `netstandard2.0`; fine on the ASP.NET Core target of PublicApi.

Config (section `Maxio:`, env-bound): `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` (optional override).
Lookup keys = product handles + family handle only; never numeric ids in config or logic.

---

## 1. Scope & sequence

| Step | Endpoint | SDK operations used (client property → method) |
|---|---|---|
| 0 | — | Add package reference; wire client + Basic auth + base URL (see §2.6); load companion skills (REQUIRED READING) |
| 1 | `GET /api/subscription-plans` | `client.ProductFamilies.ListProductFamilies` → find family whose `ProductFamily.Handle` == `Maxio:ProductFamilyHandle`; then `client.ProductFamilies.ListProductsForProductFamily(familyId, …)` |
| 2 | `POST /api/subscriptions` | (a) `client.Customers.ReadCustomerByReference(ref)` — 404 ⇒ (b) `client.Customers.CreateCustomer`; (c) `client.Customers.ListCustomerSubscriptions(customerId)` — existing live sub for same product ⇒ return it (idempotent replay); (d) `client.Subscriptions.CreateSubscription` |
| 3 | `GET /api/my-subscriptions` | `client.Customers.ReadCustomerByReference(ref)` → `client.Customers.ListCustomerSubscriptions(customerId)` |

Reference format (customer key): `eshop-{stableAppUserId}` where `{stableAppUserId}` is the authenticated user's
stable id from the JWT (`sub`). Rationale in §4.2.

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

Wire-name legend below is `CSharpName (wire_name)`; `!req` = C# `required` (object-initializer-set or ctor);
`T?` = nullable/optional. All records are `init`-only (`{ Property = … }`).

### 2.1 Operation rows

| # | Controller property · SDK method signature (params in order) | Request model (fields the integration sets) | Response envelope → members the integration reads | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| O1 | `client.ProductFamilies` · `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable params, no defaults → **pass every one, `null` to skip** | none | `IReadOnlyList<ProductFamilyResponse>`; per item read `ProductFamilyResponse.ProductFamily` (`ProductFamily?`) → `.Handle: string?`, `.Id: int?` (match `.Handle` against configured family handle) | **Case B** — `SdkException<RawError>`; status = `ex.Error.StatusCode: HttpStatusCode`, body = `ex.Error.ReadAsString()` | none | `operations/ProductFamilies.md` |
| O2 | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params (**`productFamilyId` first**, then pass `null` for each of the 8 unless filtering) | none | `IReadOnlyList<ProductResponse>`; per item read `ProductResponse.Product` (`Product !req`) → `.Name`, `.Handle`, `.PriceInCents: long?` (`price_in_cents` — "The product price, in integer cents"), `.Interval: int?`, `.IntervalUnit: IntervalUnit?`, `.ProductPricePointId/ProductPricePointHandle/ProductPricePointName` (`int?`/`string?`/`string?`), `.DefaultProductPricePointId: int?`, `.ProductFamily: ProductFamily?` | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` fallback | manual `page`/`perPage` (default 1/20) | `operations/ProductFamilies.md`; `map/models/records-3-Of-Su.md` |
| O3 | `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query param wire `reference` ← `reference` | `CustomerResponse` → `CustomerResponse.Customer` (`Customer !req`) → `.Id: int?`, `.Reference: string?`, `.FirstName/LastName/Email: string?` | **Case B** — `SdkException<RawError>`; 404 (not found) = `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| O4 | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass** | `CreateCustomerRequest` (`Models`) → field `Customer (customer): CreateCustomer !req`. `CreateCustomer` (`Models`) → **required**: `FirstName (first_name) !req`, `LastName (last_name) !req`, `Email (email) !req`; **we set**: `Reference (reference): string?` = `eshop-{userId}` (unique per site — see §3/§4.2). Optionals we omit: `Organization`, addresses, `Locale`, `VatNumber`, `TaxExempt`, `ParentId`, `SalesforceId`, `CcEmails` | `CustomerResponse` → `CustomerResponse.Customer` → `.Id`, `.Reference` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` fallback. ⚠ `CustomerErrorResponse1.Errors: Errors?` models only `PerPage`/`PricePoint` keys — a duplicate-reference message is **dropped** by typed parsing; resolve duplicates by re-lookup (§3), never by message text | none | `operations/Customers.md`; `map/models/records-1-Ac-Cr.md`; `map/models/records-2-Cr-Ne.md` |
| O5 | `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path param `customer_id` ← `customerId` (Maxio customer `Id`, never the app user id) | `IReadOnlyList<SubscriptionResponse>`; per item read `SubscriptionResponse.Subscription` (`Subscription?`) → `.Id: int?`, `.State: SubscriptionState?`, `.ProductPriceInCents: long?` (`product_price_in_cents` — recurring amount of the subscribed product), `.Product: Product?` → `.Name`, `.Handle`, `.ProductPricePointId`; `.CurrentPeriodEndsAt: DateTimeOffset?` (`current_period_ends_at` — "end of the current (recurring) period (i.e., when the next regularly scheduled attempted charge will occur)"), `.NextAssessmentAt: DateTimeOffset?`, `.Currency: string?`, `.Reference: string?` | **Case B** — `SdkException<RawError>` | none (all subs for the customer) | `operations/Customers.md`; `map/models/records-3-Of-Su.md`; `Models/Subscription.cs` (source — map row truncated at 2000 chars) |
| O6 | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass** | `CreateSubscriptionRequest` (`Models`) → field `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` (`Models`) — all optional; **we set**: `ProductHandle (product_handle): string?` (the chosen plan's handle from the request body; required by the server unless `product_id` given), `CustomerReference (customer_reference): string?` (identifies the existing customer — "Required, unless a `customer_id` or a set of `customer_attributes` is given"); optionally `Reference (reference): string?` (your app's reference for the subscription itself — **not** needed for our dedupe, see §3). **Do NOT set** any of: `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `CustomerAttributes`, `Components`, `CalendarBilling`, `NextBillingAt`, `DeferSignup` (no card capture, no trial, immediate start) | `SubscriptionResponse` → `SubscriptionResponse.Subscription` (`Subscription?`) — read exactly the O5 members (`.State`, `.ProductPriceInCents`, `.Product.Name/.Handle`, `.CurrentPeriodEndsAt`, `.Id`, `.Currency`) | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — `ErrorListResponse1.Errors: IReadOnlyList<string> !req` · `TryGetRawError(out RawError)` fallback | none | `operations/Subscriptions.md`; `map/models/records-2-Cr-Ne.md`; `Models/CreateSubscription.cs` (source — map row truncated at 2000 chars) |

Envelope note for every read: the response types wrap the payload one level down — `ProductResponse.Product`,
`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductFamilyResponse.ProductFamily`.
Never treat the envelope itself as the domain object.

Namespaces needed (`using` per kind — C# does not import child namespaces transitively):
records = `MaxioAdvancedBilling.Models` · enums = `MaxioAdvancedBilling.Models.Enums` · typed errors =
`MaxioAdvancedBilling.Errors` · `SdkException<T>` = `MaxioAdvancedBilling.Core.Exceptions` · `RawError` =
`MaxioAdvancedBilling.Core.ErrorResponse` · `BasicAuthCredentials` = `MaxioAdvancedBilling.Core.Authentication.Basic` ·
`ServerEnvironment` = `MaxioAdvancedBilling.Servers` · client/options/`ServiceCollectionExtensions` = `MaxioAdvancedBilling`
(root). Controllers = `MaxioAdvancedBilling.Api` (only if you name a controller's type; calling via `client.X` needs no using).
`ServerOptions` (root ns) has `Production`/`Ebb`; `ProductionOptions.Us` is the **nested type**
`MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions` — assign via property chain, not by naming the type.

### 2.2 Enum value tables actually needed

`SubscriptionState` (StringEnum — compare with the static member, e.g. `Subscription.State == SubscriptionState.Active`; wire value in parens). Categories from the SDK doc-comment:

| C# member (wire) | Doc category |
|---|---|
| `SubscriptionState.Pending (pending)` | internal/transient — "do not base any access decisions on this state" |
| `SubscriptionState.Assessing (assessing)` | internal/transient |
| `SubscriptionState.FailedToCreate (failed_to_create)` | end of life |
| `SubscriptionState.Trialing (trialing)` | live |
| `SubscriptionState.Active (active)` | live — "paid and up to date" |
| `SubscriptionState.SoftFailure (soft_failure)` | problem |
| `SubscriptionState.PastDue (past_due)` | problem |
| `SubscriptionState.Suspended (suspended)` | end of life (prepaid exhausted) |
| `SubscriptionState.Canceled (canceled)` | end of life |
| `SubscriptionState.Expired (expired)` | end of life |
| `SubscriptionState.Paused (paused)` | internal — "account in arrears" |
| `SubscriptionState.Unpaid (unpaid)` | problem (dunning final action) |
| `SubscriptionState.TrialEnded (trial_ended)` | end of life (no-obligation trial, no card) |
| `SubscriptionState.OnHold (on_hold)` | end of life ("expected to resume") |
| `SubscriptionState.AwaitingSignup (awaiting_signup)` | created but first billing date pending |

`IntervalUnit` (StringEnum): `Day (day)`, `Month (month)` — `Product.IntervalUnit` / product billing interval.
Not needed for requests in this scope (all create/list/find calls here take plain strings/ints — no unions involved).

### 2.3 Response members for the confirmation/DTO payloads (all in `MaxioAdvancedBilling.Models`)

| DTO need | Read from (envelope → path) | Type |
|---|---|---|
| plan name (browse + subscription) | `ProductResponse.Product.Name` / `SubscriptionResponse.Subscription.Product?.Name` | `string?` |
| plan handle | `ProductResponse.Product.Handle` / `.Product?.Handle` | `string?` |
| price (amount) | browse: `Product.PriceInCents` (default price point's amount, in integer cents — see §5 note) · subscription: `Subscription.ProductPriceInCents` (recurring amount of the product currently subscribed) | `long?` (cents) |
| billing interval | `Product.Interval: int?` + `Product.IntervalUnit: IntervalUnit?` | e.g. 1 × Month |
| subscription state | `Subscription.State` | `SubscriptionState?` |
| next billing date | `Subscription.CurrentPeriodEndsAt` (doc: end of current period = when the next regularly scheduled attempted charge will occur) | `DateTimeOffset?` |
| money currency | browse: **no currency member on `Product`** — see §5. Subscription: `Subscription.Currency` | `string?` |

State expectation on create (no trial, no initial charge, no card required): **`Active`**. `Trialing` is impossible
(no trial on either plan); `AwaitingSignup` only if `defer_signup`/`initial_billing_at` were set (they are not).
Confirmation surface: read `Subscription.State` from the create response and return it **as returned** — never
hard-map an assumption to the wire. Expected-state note is `UNVERIFIED` (only live traffic confirms what the sandbox
returns for a cardless, no-trial signup).

### 2.4 Error handling per call (summary of the rows above)

Case A typed (`SdkException<{Op}Error>` — `using MaxioAdvancedBilling.Errors` + `…Core.Exceptions`): O2 (`…ListProductsForProductFamilyError`), O4 (`…CreateCustomerError`), O6 (`…CreateSubscriptionError`). Case B raw (`SdkException<RawError>` — `using MaxioAdvancedBilling.Core.ErrorResponse`): O1, O3, O5. No `{Operation}Result`/no-throw variants exist in this SDK — every operation is throw-only; always wrap the call.

### 2.5 Client wiring (`MaxioAdvancedBilling` root ns)

```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // Username = Maxio API key, Password = literal "x"
    Environment = ServerEnvironment.Us,
};
options.Server.Production.Us.Site = subdomain;            // base URL derives from subdomain
// when an explicit override is configured:
options.Server.Production.Us.BaseUrl = baseUrlOverride;   // replaces "https://{site}.chargify.com" entirely
var client = new MaxioAdvancedBillingClient(httpClient, options); // only ctor: (HttpClient, MaxioAdvancedBillingClientOptions)
```

`ProductionOptions.UsOptions` defaults: `BaseUrl = "https://{site}.chargify.com"`, `Site = "subdomain"`; EU equivalents under `options.Server.Production.Eu.*` → `https://{site}.ebilling.maxio.com`. Both forms must compile for any site: **subdomain-derived** = set `.Site` only (use `ServerEnvironment.Us`, the default); **override** = set `.BaseUrl` (when `Maxio:BaseUrl` is present, ignore subdomain derivation). If an override is configured, `{site}` is not substituted unless the override contains it.

DI alternative (no manual `HttpClient`): `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Server.Production.Us.Site = …; });`
(`MaxioAdvancedBilling.ServiceCollectionExtensions`). Load **dotnet-client-initialization** and
**dotnet-authentication** before writing this wiring (see §3 traps).

---

## 3. Idempotency facts (grounded)

**No idempotency-key request header or parameter exists anywhere in the SDK map.** No CreateCustomer/CreateSubscription
row or signature carries one, and the SDK offers no per-call header hook (only `HttpClient.DefaultRequestHeaders`, which
would stamp every request, not one call). Treat Maxio-side idempotency as **unsupported** and implement double-click
safety purely from lookups:

1. **Customer — unique by reference, server-enforced.** `CreateCustomer` map note (verbatim intent): "you may only
   create one customer for a given reference value. If provided, the `reference` value must be unique." So:
   - find first: `client.Customers.ReadCustomerByReference(reference)` — 404 (Case B, `StatusCode == NotFound`) ⇒ create
     via O4; any other status ⇒ surface the error;
   - concurrent-create race: two calls can both miss the lookup and both POST. The loser gets a **422** →
     `SdkException<CreateCustomerError>` + `TryGetCustomerErrorResponse1`. The typed error model cannot name the field
     (see O4 ⚠), so on any 422 **re-run `ReadCustomerByReference`**: found ⇒ the other request won — continue with that
     customer; not found ⇒ genuine validation failure — rethrow.
   - No Maxio field-length or charset constraint on `reference` is stated in the map or the SDK source (zero validation
     attributes on the model). Format therefore: prefix + stable id, ASCII/URL-safe, kept short — `eshop-{userId}`
     (`eshop-` + a GUID ≈ 42 chars). Label `UNVERIFIED` for the undocumented server-side cap.
2. **Subscription — dedupe by (customer, product, live state) read-back.** CreateSubscription imposes no documented
   uniqueness constraint on any field, so the guard is: before O6, call O5 (`ListCustomerSubscriptions(customerId)`) and
   if any `SubscriptionResponse.Subscription` has `.Product?.Handle == {requested plan handle}` and `.State` is not a
   final state ⇒ **return that subscription** (idempotent replay, no create). Matchable fields: `Subscription.Product.Handle`
   (`string?`) + `Subscription.State` (`SubscriptionState?`); recommended "already subscribed" = state **not in**
   {`Canceled`, `Expired`, `TrialEnded`, `FailedToCreate`} — the doc's end-of-life set. Whether `OnHold`/`Suspended`
   count as "already subscribed" is a product decision: `YOUR CALL — not in the map`.
   - True concurrency (two requests pass the check together) is NOT covered by the Maxio API alone → serialize per user
     inside the app (per-user lock/single-flight around the check+create). That is application design, not an SDK fact.
   - Ambiguous create failure (transport error / 5xx after commit) is retried automatically by the SDK's Polly policy on
     **every verb including POST** — so after catching a `CreateSubscription` failure, reconcile by re-running O5 with the
     same match; found ⇒ treat as success. This closes the retry-created-its-own-duplicate hole.
3. **Reference values that are not lookups**: Maxio `Customer.Id`/`Subscription.Id` are opaque ints — never key app logic
   on them (re-seeded site); the app user id and product/family handles are the only stable keys.

---

## 4. Coverage mapping (endpoint → facts)

### 4.1 List plans in the family (`GET /api/subscription-plans`)
- Family lookup is by handle, **not** by id: `ListProductFamilies` (O1) → match `ProductFamily.Handle` against
  `Maxio:ProductFamilyHandle`; take `ProductFamily.Id`. (The map's note that `GET /product_families/{id}` accepts
  `handle:my-family` cannot be used — `ReadProductFamily` takes `int id`; trust the signature. Passing `handle:…` into
  the *string* `productFamilyId` of O2 is untested against the live API → do not rely on it.)
- Then `ListProductsForProductFamily(productFamilyId: family.Id.ToString(), …)` (O2) — this is the family filter; the
  site-wide `client.Products.ListProducts` and its `ListProductsFilter` (only `Ids`/`PrepaidProductPricePoint`/
  `UseSiteExchangeRate`) cannot filter by family.
- Both `eshop-pro` and `basic-plan` must appear because they belong to family `eshop-subscribe`; leave `includeArchived`
  unset so archived products stay out.
- Per plan read: `Name`, `Handle`, `PriceInCents` (default price point's amount in cents; `long?`), `Interval` +
  `IntervalUnit`; identify the default price point via `DefaultProductPricePointId`/`ProductPricePointId`/
  `ProductPricePointHandle`/`ProductPricePointName` (all present on the list response model).

### 4.2 Find-or-create customer keyed by app user id (in `POST /api/subscriptions`)
- Reference = `eshop-{stableAppUserId}` (see §3.1). Single exact-match lookup: `ReadCustomerByReference` (O3) — the map
  note: "Returns a customer by their unique reference ID. It will return a single match."
- `CreateCustomer` (O4) requires `FirstName`, `LastName`, `Email` — the app must supply them from the authenticated
  user's identity (token claims and/or its own user store); this is PublicApi's identity schema, not a Maxio contract
  fact → `YOUR CALL — not in the map` which claims carry email/display name.
- Only on a 404-create path does the customer get created; concurrent 422 ⇒ re-lookup (win/lose logic, §3.1).

### 4.3 Subscribe a customer to a plan, no payment profile (in `POST /api/subscriptions`)
- One body: `CreateSubscription` with `ProductHandle = {plan handle from request}` + `CustomerReference = eshop-{userId}`;
  no payment/profile/card fields at all. Sandbox fact: payment method NOT required, so no card capture/3-DS/payment
  profile is expected. (`PAYMENT METHOD NOT REQUIRED` is the seeded site's setting — not an SDK contract fact; the SDK's
  only statement is "Payment information may be required… depending on the options for the Product being subscribed.")
- Idempotent per §3.2: O5 check → O6 create → reconcile on failure by re-running O5.
- Confirmation reads the O6 response envelope (state as-returned, `ProductPriceInCents`, `Product.Name`,
  `CurrentPeriodEndsAt`, `Id`, `Currency`). Expected state `Active` (§2.3, `UNVERIFIED`).

### 4.4 Read back / list subscriptions (`GET /api/my-subscriptions`, plus §4.3 matching)
- O3 → customer, then O5 `ListCustomerSubscriptions(customerId)` — the only per-customer subscription listing; the
  site-wide `client.Subscriptions.ListSubscriptions` filters by product/state/date, **not** by customer, and is
  paginated (page/perPage, 14 must-pass params) — unnecessary here.
- Per subscription map: plan name `Subscription.Product?.Name`, handle `Subscription.Product?.Handle`, price
  `Subscription.ProductPriceInCents`, state `Subscription.State`, next billing date `Subscription.CurrentPeriodEndsAt`,
  plus `Subscription.Id`, `Subscription.Currency`. (`UNVERIFIED`: the live list payload always carries the nested
  `Product` — the model allows it but only live traffic proves presence; `ProductPriceInCents` and the dates are
  top-level on the same record and are the safe primary source.)

---

## 5. Assumptions & Blockers

- **MAJOR (assumption)** — The authenticated user's identity (JWT claims) exposes everything `CreateCustomer` requires
  (`FirstName`, `LastName`, `Email`) or the service can resolve them. Which claims carry these is PublicApi's design:
  `YOUR CALL — not in the map`. If the token has only a subject id, customer creation cannot proceed without a
  name/email source.
- **MAJOR (assumption)** — `Maxio:Subdomain` on the `cp-exp-7` sandbox resolves through the **US** server group
  (`https://{subdomain}.chargify.com`); the config list has no environment key, so `ServerEnvironment.Us` is fixed. If
  the sandbox is ever EU-hosted, a `Maxio:Environment`-style switch would be needed (EU template
  `https://{site}.ebilling.maxio.com`): `YOUR CALL — not in the map`.
- **MINOR (assumption)** — Subscribe semantics: a live subscription to a *different* plan is not touched — this plan
  only creates; plan *changes* (Maxio `UpdateSubscription` product migration) are out of scope. A second live
  subscription to the same plan is prevented by §3.2 matching.
- **MINOR (assumption)** — `Product.PriceInCents` returned by the family-products list is the default price point's
  amount (source doc: "The product price, in integer cents"; the record also carries the default-price-point
  id/handle/name members). Live-payload confirmation is `UNVERIFIED`.
- **MINOR (assumption)** — No product/component/price-point configuration is read beyond the two product handles and
  the family handle; the `api-call` metered component is not allocated by these endpoints.
- **MINOR (assumption)** — State after cardless no-trial signup is `Active`; confirmation surface returns state
  as-received regardless (`UNVERIFIED` — only live traffic settles it).
- **MINOR (assumption)** — `reference` has no documented server max length/charset (map and SDK source are silent; no
  validation attributes exist on the models). Format `eshop-{userId}` keeps it ASCII/URL-safe and short; treat the
  undocumented server cap as `UNVERIFIED`.
- **No blockers** — every member, signature, enum value, and error accessor used above is grounded in the map or in the
  named SDK source files; no capability the plan needs is missing from the SDK.

---

## 6. REQUIRED READING

Load all of the following **before implementation starts**; the contract sheet deliberately does not carry their
contents (defaults, worked examples, and what you must still wire yourself live in the skills). This block is
mandatory, and it always includes the JsonException hazard below, because the error boundary is written early and the
sheet cannot shape it after the fact.

- **dotnet-client-initialization** — step 0: building/registering `MaxioAdvancedBillingClient` (the `HttpClient`
  constructor argument, lifetime, `AddMaxioAdvancedBillingClient`).
- **dotnet-authentication** — step 0: Basic credentials (username = API key, password = `"x"`), reading `Maxio:ApiKey`,
  rotation. Load before the first 401/403 investigation too.
- **dotnet-configuration-resilience** — steps 0–2: what `RetryOptions` actually retries (a transport failure retries a
  `POST`; `HttpMethodsToRetry` gates only status-triggered retries), what `Timeout` bounds, base-URL override vs `Site`,
  and what is not wired for you. Directly relevant to §3.2's reconcile-on-failure rule.
- **dotnet-models** — steps 1–3: building the `CreateCustomer`/`CreateSubscription` records, `init`-only members,
  `StringEnum<T>` comparisons, wire-name vs C#-name.
- **dotnet-calling-endpoints** — steps 1–3: named-argument calls (every list op has many must-pass-explicitly nullable
  params), envelope read-down (`ProductResponse.Product` …).
- **dotnet-error-handling** — every step with a call: Case A vs Case B per operation (§2.4), accessors vs `RawError`,
  plus the two JsonException hazards below.
- **dotnet-testing** — before writing tests of the integration service (the `HttpClient` constructor argument is the
  test seam).

Always include, verbatim, **both** of these hazard rows — `System.Text.Json.JsonException` reaches the boundary from
two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Per-step trap notes (name the hazard + consequence; resolve in the skill, not here):

> ⚠ Step 0 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the
> timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.
>
> ⚠ Step 2 (guarded create) — a transport-level failure on `POST /subscriptions` is retried by the SDK itself, so a
> single logical create can reach Maxio more than once; the §3.2 post-failure re-list reconcile is what keeps one live
> subscription per (customer, plan). **MUST load `dotnet-configuration-resilience`** before deciding what to do after a
> thrown create.
>
> ⚠ Steps 1–3 (building request bodies) — records are immutable `init`-only, enums are `StringEnum<T>` (compare
> `== SubscriptionState.Active`, never a wire string), and unmodeled JSON fields are dropped on deserialize (see the O4
> ⚠). **MUST load `dotnet-models`** before constructing payloads or reading responses.
>
> ⚠ Steps 1–3 (calling) — list/search operations have many nullable parameters with **no C# default**; a positional
> call mis-binds and a named call must use the literal names above (including `ct:`). **MUST load
> `dotnet-calling-endpoints`** before the first SDK call.
>
> ⚠ Step 2 (customer creation concurrency) — the 422 typed error model does not expose a per-field message, so
> duplicate-reference detection must be done by re-lookup, never by parsing the error body. **MUST load
> `dotnet-error-handling`** before writing the create-customer catch.
>
> ⚠ Steps 2–3 (reading `my-subscriptions`) — list responses are `IReadOnlyList<SubscriptionResponse>` envelopes; the
> nested `Product` object's presence in a live list payload is unverified, so prefer top-level `Subscription` members
> (`ProductPriceInCents`, `CurrentPeriodEndsAt`, `State`) and treat `Product.Name` as best-effort. **MUST load
> `dotnet-calling-endpoints`** for envelope handling.
