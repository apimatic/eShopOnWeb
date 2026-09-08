# Maxio Advanced Billing — recurring-subscription integration plan (eShopOnWeb PublicApi)

## 1. Scope & sequence

Additive recurring-subscription billing on `src/PublicApi` (JWT-authenticated), Maxio Advanced Billing (sandbox `cp-exp-5`) as system of record. No payment method / card capture anywhere.

| # | Step | SDK operations used | Notes |
|---|---|---|---|
| 1 | Package reference, config binding, client & DI registration | — (construction only) | NuGet `AsadAli.AdvancedBilling.Sdk`; load `dotnet-client-initialization`, `dotnet-configuration-resilience`, `dotnet-authentication` first |
| 2 | `GET /api/subscription-plans` | `client.ProductFamilies.ListProductsForProductFamily` | List plans in the seeded family (`handle:eshop-subscribe`), price/interval from `Product` |
| 3 | Shared "ensure Maxio customer" helper (used by steps 4–5) | `client.Customers.ReadCustomerByReference` → on miss `client.Customers.CreateCustomer` (422 → re-lookup) | Customer identity keyed by a deterministic `reference` derived from the token identity |
| 4 | `POST /api/subscriptions` (idempotent) | step 3 helpers + `client.Customers.ListCustomerSubscriptions` + `client.Subscriptions.CreateSubscription` | Algorithm in §2.3 row 4 |
| 5 | `GET /api/my-subscriptions` | step 3 lookup + `client.Customers.ListCustomerSubscriptions` | If no customer exists → empty list (never create a customer from a GET) |
| 6 | Error boundary & HTTP mapping | all of the above | Load `dotnet-error-handling` before writing it (mandatory — see §4) |
| 7 | Tests for the integration layer | — | Load `dotnet-testing` before stubbing the SDK |

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

### 2.1 Package, identity, construction

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (root namespace differs: `MaxioAdvancedBilling`) | `sdk-map.md` identity row |
| Package version to pin | `1.0.2` (the ref the map is generated from — tagged `v1.0.2`; restore will confirm it exists on NuGet) | `sdk-map.md` stamp row |
| Client class | `MaxioAdvancedBillingClient` — sole ctor `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` "Getting a client" |
| DI registration | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure)` — registers a **singleton** client over an `IHttpClientFactory`-created `HttpClient`; the options instance is captured **once at registration** | `sdk-map.md` + source `ServiceCollectionExtensions.cs` |
| Auth | HTTP Basic — `BasicAuthCredentials.Username` = API key, `.Password` = literal `"x"` | `sdk-map.md` "Auth — Basic only" |
| Environment | `ServerEnvironment.Us` (default; US-hosted sandbox → `https://{site}.chargify.com`) | `sdk-map.md` Servers & auth |
| Site / base URL | `options.Server.Production.Us.Site` = subdomain; `options.Server.Production.Us.BaseUrl` override redirects verbatim (map's documented mock/dev redirect mechanism) | `sdk-map.md` Servers & auth; source `Servers/ProductionOptions.cs` |

**Configuration binding keys** (config section name and env vars dictated by the feature brief — bind these names exactly):

| Binding key (section `Maxio:`) | Env var | Wire into |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | `BasicAuthCredentials.Username` |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | `options.Server.Production.Us.Site` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | bare family handle, e.g. `eshop-subscribe` (prefix with `handle:` at the call site) |
| `Maxio:BaseUrl` (optional) | — | when set: `options.Server.Production.Us.BaseUrl` verbatim (overrides site-derived default); else leave default template `https://{site}.chargify.com` |

Construction sketch (shape only — the wiring recipe lives in `dotnet-client-initialization` / `dotnet-authentication`):

```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = cfg.Maxio.ApiKey, Password = "x" }; // literal "x"
    o.Server.Production.Us.Site = cfg.Maxio.Subdomain;            // e.g. cp-exp-5
    if (!string.IsNullOrEmpty(cfg.Maxio.BaseUrl))
        o.Server.Production.Us.BaseUrl = cfg.Maxio.BaseUrl;       // verbatim base address
});
```

### 2.2 Namespaces (every type used below, fully-qualified)

| Type(s) | Namespace (`using`) |
|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` | `MaxioAdvancedBilling` |
| Controller classes `Customers`, `Subscriptions`, `ProductFamilies` | `MaxioAdvancedBilling.Api` |
| Records: `CreateCustomer`, `CreateCustomerRequest`, `Customer`, `CustomerResponse`, `CreateSubscription`, `CreateSubscriptionRequest`, `Subscription`, `SubscriptionResponse`, `Product`, `ProductResponse`, `ErrorListResponse1`, `CustomerErrorResponse1` | `MaxioAdvancedBilling.Models` |
| Enums: `SubscriptionState`, `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| Typed errors: `CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `FindSubscriptionError` | `MaxioAdvancedBilling.Errors` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment`, `ProductionOptions` (nested `UsOptions`, `EuOptions`) | `MaxioAdvancedBilling.Servers` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |

### 2.3 Operations

Envelope convention: **list ops return `IReadOnlyList<ResponseWrapper>`; single-entity ops return one `ResponseWrapper`. Every wrapper carries the payload in one field** (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`) — reads go one level down. All operations are **throw-only** (no `…Result` no-throw variants exist anywhere in this SDK).

**Row 1 — `GET /api/subscription-plans`**: `client.ProductFamilies.ListProductsForProductFamily(...)`

| | |
|---|---|
| Signature | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` have **no C# default** → call with named arguments passing `null` to skip |
| Family path param | `productFamilyId: "handle:" + <ProductFamilyHandle config>` — the param is documented as "Either the product family's id or its handle prefixed with `handle:`" (use the handle form; numeric IDs are not stable). `eshop-subscribe` is seeded. |
| Returns (awaited) | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product` (`!req`, non-null): read `Handle (handle)`, `Name (name)`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?` |
| Rendering `"$299.00/mo"` | cents → `PriceInCents / 100.0`; cadence → `Interval` + `IntervalUnit` (`Day (day)` → "/day", `Month (month)` → "/mo"). **The `Product` payload carries no currency code** (no `currency` member on `Product`) → symbol/code is the app's own assumption (seed site is USD) — see §5. Defensive: drop plans whose `ArchivedAt != null`. |
| Error case | **Case A** — `SdkException<ListProductsForProductFamilyError>`; accessors `TryGetString(out string)` [404 — family not found] · `TryGetRawError(out RawError)` [fallback] |
| Pagination | manual `page`/`perPage`, defaults 1/20 |
| Source | `map/operations/ProductFamilies.md` (ListProductsForProductFamily); handle-form confirmed in SDK source `Api/ProductFamilies.cs` param doc |

**Row 2 — customer lookup (steps 3–5)**: `client.Customers.ReadCustomerByReference(...)`

| | |
|---|---|
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` |
| Returns (awaited) | `CustomerResponse`; read `CustomerResponse.Customer` (`!req`) → `Customer.Id (id): int?`, `Customer.Reference (reference): string?`, `Customer.Email (email): string?` (plus `FirstName`/`LastName` if needed) |
| Error case | **Case B** — `SdkException<RawError>`: not-found = `.Error.StatusCode == HttpStatusCode.NotFound` (catch and treat as "no customer yet") |
| Pagination | none |
| Source | `map/operations/Customers.md` (ReadCustomerByReference) |

**Row 3 — customer creation (step 3)**: `client.Customers.CreateCustomer(...)`

| | |
|---|---|
| Signature | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Request model | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` → `CreateCustomer`: `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` (optional member — **but see acceptance note**). Other optional members not used: `CcEmails`, `Organization`, addresses, `Locale`, etc. |
| Acceptance note (from op Notes) | "The only validation restriction is that you may only create one customer for a given reference value. If provided, the `reference` value must be unique." → the idempotency key is `reference`; **always set it** (deterministic from the token identity). Fields omitted above (address, tax, etc.) are not tied to acceptance for this flow. |
| Returns (awaited) | `CustomerResponse` → `Customer.Id`, `.Reference`, `.Email` as Row 2 |
| Error case | **Case A** — `SdkException<CreateCustomerError>`; accessors `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. **Duplicate `reference` is a 422** (per the Notes' uniqueness restriction). ⚠ The 422 typed payload `CustomerErrorResponse1 { Errors (errors): Errors? }` models **only** `Errors.PerPage`/`Errors.PricePoint` — it cannot carry the duplicate-reference message; on 422, `TryGetRawError` is **false** (typed branch holds, no fallback) → you cannot read the server's message text through this SDK on a 422. Duplicate detection is therefore "any 422 → re-run Row 2 lookup" (§2.4), never message parsing. |
| Pagination | none |
| Source | `map/operations/Customers.md` (CreateCustomer row Notes); payload shape `map/models/records-2-Cr-Ne.md` + source `Errors/CreateCustomerError.cs`, `Models/CustomerErrorResponse1.cs` (map row for `CustomerErrorResponse1` is on records-2) |

**Row 4 — `POST /api/subscriptions` (idempotent)**: rows 2–3 + `client.Customers.ListCustomerSubscriptions(...)` + `client.Subscriptions.CreateSubscription(...)`

| | |
|---|---|
| List existing (pre-check): signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — **numeric** Advanced Billing customer id (from Row 2/3 `Customer.Id`) |
| Returns (awaited) | `IReadOnlyList<SubscriptionResponse>`; per element `SubscriptionResponse.Subscription` (**nullable** — skip nulls) → `Subscription.Product (product): Product?` with `.Handle`, `.State (state): SubscriptionState?`, `.Reference (reference): string?` |
| Error case | **Case B** — `SdkException<RawError>` |
| Create: signature | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Request model | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` → members used: `ProductHandle (product_handle): string?` · `CustomerReference (customer_reference): string?` · `Reference (reference): string?`. **Omit every payment member** (`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`) — no-card behavior. |
| No-payment-method fact | There is **no `requireCard`-style flag and no dunning flag** in this SDK's `CreateSubscription` payload (full member list read from source `Models/CreateSubscription.cs`). Whether payment info is required is decided by the provider per product option `Product.RequireCreditCard (require_credit_card)` (visible in Row 1 responses; seed plans are configured "payment method NOT required"). So "no payment method required" = *omit* all payment-profile/credit-card members. |
| Acceptance note (from op Notes) | Product specified via `product_handle` **or** `product_id` (handle preferred — product IDs are "not currently published"); customer specified via `customer_id` **or** `customer_reference` (use `customer_reference`). `product_handle` is site-scoped, so family is implied — no family member exists on this payload. `reference` = "The reference value (provided by your app) for the subscription itself." |
| Returns (awaited) | `SubscriptionResponse` → `SubscriptionResponse.Subscription` (**nullable**) → `Subscription.Id (id): int?`, `.State (state): SubscriptionState?`, `.ProductPriceInCents (product_price_in_cents): long?` (recurring amount of the subscribed product version), `.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (end of current period = when the next regularly scheduled charge occurs), `.NextAssessmentAt (next_assessment_at): DateTimeOffset?` (tracks `current_period_ends_at`, diverges only after a failed renewal retry), `.Product.Product`-nested plan `Handle`/`Name`, `.Currency (currency): string?` |
| Error case | **Case A** — `SdkException<CreateSubscriptionError>`; accessors `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` — 422 body is a *list of human-readable messages*; there is **no structured duplicate/conflict code**; any duplicate/conflict also lands here as 422 → recovery = re-list (§2.4), never message matching. ⚠ Because `Errors` is a **required** member, a 422 whose body is not exactly `{"errors": [...]}` (e.g. a 3DS `action_link` body) throws `JsonException` while the error object is built → the `SdkException` never surfaces (see §4 hazard rows). |
| Idempotency algorithm (contract for this endpoint) | (1) ensure customer: Row 2 by `reference`; on 404 → Row 3 create (catch **any** 422 → Row 2 again; if now found, use it, else rethrow as validation conflict). (2) `ListCustomerSubscriptions(customer.Id)`; if a subscription whose `Product.Handle == requested plan` **and whose `State` is not end-of-life** exists → return it as the response (idempotent success — no create). (3) else `CreateSubscription` with `ProductHandle` + `CustomerReference` + (app-chosen deterministic `Reference`); on 422 → re-run step 2 and return the now-present subscription if found, else surface a 422/409 "conflict". |
| Pagination | none (ListCustomerSubscriptions and CreateSubscription) |
| Source | `map/operations/Subscriptions.md` (CreateSubscription row Notes; FindSubscription exists as a reference lookup), `map/operations/Customers.md` (ListCustomerSubscriptions); full `CreateSubscription` member list from source `Models/CreateSubscription.cs` (map row truncated); error payloads `map/models/records-2-Cr-Ne.md` |

**Row 5 — `GET /api/my-subscriptions`**: rows 2 + `ListCustomerSubscriptions`

| | |
|---|---|
| Flow | Row 2 lookup by `reference`; if 404 → return empty list (a GET must never create a customer). Else `ListCustomerSubscriptions(customer.Id)` (Row 4 signature/error) |
| Response mapping (per non-null `SubscriptionResponse.Subscription`) | subscription id = `.Id` · plan = `.Product.Name` / `.Product.Handle` · price = `.ProductPriceInCents` (fall back `.Product.PriceInCents`) · state = `.State` · next billing date = `.CurrentPeriodEndsAt` (fall back `.NextAssessmentAt`) |
| Source | `map/operations/Customers.md` (ListCustomerSubscriptions) |

**Recovery-only (optional)**: `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` → `SubscriptionResponse`; Case A `SdkException<FindSubscriptionError>` with `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback]. Use only if the provider's subscription-`reference` uniqueness is confirmed by live traffic (§5). Source: `map/operations/Subscriptions.md`.

### 2.4 Enums actually used (exact values)

**`SubscriptionState`** (namespace `MaxioAdvancedBilling.Models.Enums`; wire value in parens). SDK-documented grouping from `Subscription.State` docs (source `Models/Subscription.cs`):

| Group (per SDK docs) | Members |
|---|---|
| Live | `Active (active)` · `Trialing (trialing)` · `Paused (paused)` (internal: `Assessing (assessing)`, `Pending (pending)` — "do not base access decisions on these") |
| Problem | `PastDue (past_due)` · `SoftFailure (soft_failure)` · `Unpaid (unpaid)` |
| End of Life | `Canceled (canceled)` · `Expired (expired)` · `FailedToCreate (failed_to_create)` · `OnHold (on_hold)` · `Suspended (suspended)` · `TrialEnded (trial_ended)` |

"Already subscribed" (Row 4 step 2) means state is not end-of-life — which exact set is the app's policy; SDK fact is only the vocabulary and the grouping above. Enum type is a `sealed record … : StringEnum<…>` with static members — compare with `==` against the static member (`SubscriptionState.Active`), never string literals. Source: `map/models/enums.md` (SubscriptionState) + source `Models/Enums/SubscriptionState.cs`.

**`IntervalUnit`**: `Day (day)` · `Month (month)` — these are the **only** two values (no week/year at product level). Source: `map/models/enums.md`.

### 2.5 Wire-name vs C#-name drift (models we construct/read)

From records pages (format `C#Name (wire_name)`): `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`, `Reference (reference)`, `CustomerId (customer_id)`, `CustomerReference (customer_reference)`, `ProductHandle (product_handle)`, `PriceInCents (price_in_cents)`, `Interval (interval)`, `IntervalUnit (interval_unit)`, `ProductPriceInCents (product_price_in_cents)`, `CurrentPeriodEndsAt (current_period_ends_at)`, `NextAssessmentAt (next_assessment_at)`, `RequireCreditCard (require_credit_card)`, `ArchivedAt (archived_at)`. Envelope members: `CreateCustomerRequest.Customer (customer)`, `CreateSubscriptionRequest.Subscription (subscription)`, `ProductResponse.Product (product)`, `CustomerResponse.Customer (customer)`, `SubscriptionResponse.Subscription (subscription)`. Everything in this SDK is snake_case wire ↔ PascalCase C#; the map records pages are authoritative and nothing on this scope's models diverges.

## 3. Trap notes

- ⚠ Step 1 (client/DI registration) — the `HttpClient`/handler pipeline must be long-lived and shared, and the SDK client wrapper's lifetime rules + the options-captured-once behavior decide where config binding happens. **MUST load `dotnet-client-initialization`** before writing the registration.
- ⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call the way a wrapper timeout would, and retry semantics interact with idempotency: a transport failure on a write can mean the write executed server-side even though the call threw. **MUST load `dotnet-configuration-resilience`** before tuning or relying on any retry/timeout behavior (and before deciding the `HttpClient` timeout story).
- ⚠ Step 1 (credentials) — Basic auth is username=API key with a literal password, and getting credentials into the options at the right point (plus what a 401 actually means) is not visible from the property name. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Steps 2–5 (calling) — most signatures have optional parameters with **no C# default** (Row 1 has eight); positional calls silently mis-bind, and required-but-nullable params must be passed explicitly. **MUST load `dotnet-calling-endpoints`** before writing the first call.
- ⚠ Steps 2–5 (models) — request payloads are **wrapped** (`CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`) and responses are wrapped too; records are immutable `init`-only; enums are `StringEnum<T>` records, not C# enums. **MUST load `dotnet-models`** before constructing payloads or mapping responses.
- ⚠ Step 6 (error boundary) — Case A vs Case B operations differ in what reaches your catch ladder, and `TryGetRawError` is a fallback, not a catch-all; on this scope Row 3's 422 gives you a typed payload with no raw text at all. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Steps 1, 4 (idempotency under retries) — whether a failed write can be re-sent determines whether the find-then-create + 422-fallback design is sufficient; the SDK may retry a `POST` on transport failure regardless of method. **MUST load `dotnet-configuration-resilience`** before finalizing the idempotency design.
- ⚠ Step 7 (tests) — which seam to fake (the `HttpClient` ctor argument) and how to cover the 422/404/not-found paths without SDK internals is not guessable from the client type. **MUST load `dotnet-testing`** before writing tests.

## 4. REQUIRED READING

Load **before implementation starts**; the sheet deliberately does not carry these skills' contents.

Always include, verbatim, **both** of these hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it. (Both hazards are live on this scope: Row 1/2/4/5 wrappers have `!req` members, and Row 4's 422 payload has a `required` `Errors` member that a 3DS-shaped 422 body will fail to populate.)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, DI registration, HttpClient lifetime |
| `dotnet-configuration-resilience` | Step 1 + idempotency — retry/timeout semantics, base-URL override, what retries mean for POST |
| `dotnet-authentication` | Step 1 — Basic credentials (API key / literal `"x"`), 401 handling, key rotation |
| `dotnet-calling-endpoints` | Steps 2–5 — signatures, must-pass-explicitly params, named arguments, async/ct usage |
| `dotnet-models` | Steps 2–5 — wrapped request/response records, `StringEnum<T>` enums, wire vs C# names |
| `dotnet-error-handling` | Step 6 — Case A/B mechanics, accessors, the two `JsonException` hazard rows above |
| `dotnet-testing` | Step 7 — fake the `HttpClient` ctor seam; cover 404/422/not-found paths |

## 5. Assumptions & Blockers

- **YOUR CALL — token identity → customer identity mapping.** SDK facts: `Customer.Reference` is the app-provided unique key and the only *exact* lookup (`ReadCustomerByReference`); `ListCustomers(q:)` is a fuzzy multi-field search, so email is **not** exactly lookable. The brief keys the customer on the authenticated user's email: either set `Reference` = the token's stable user identifier (preferred — guaranteed unique) or = the email itself (only safe if the app guarantees email uniqueness); store the email in `Customer.Email`. Whatever is chosen must be deterministically derivable from the JWT on every call. — not in the map (application mapping).
- **UNVERIFIED — what the create-subscription response looks like on this seeded site.** No card + $299 product + no trial/setup fee: expected `Active` with `CurrentPeriodEndsAt` ≈ +1 month, but the resulting `State`/dates depend on the sandbox site's product (`require_credit_card`) and dunning configuration, which only live traffic can confirm. Do not hard-code a state assumption — read `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt` from the create response and surface them; the idempotency pre-check (Row 4 step 2) is state-based and must not assume `Active`.
- **UNVERIFIED — duplicate-detection on the wire.** 422 is the only conflict signal the SDK documents (customer: uniqueness restriction in `CreateCustomer` Notes; subscription: no structured code). Whether the provider actually rejects duplicate subscription `reference` values, and the exact 422 message text, are not documented in the SDK source → the sheet's recovery is "any 422 → re-lookup / re-list", never message parsing. Consequence for the app: under *true* simultaneous concurrency (two POSTs that both pass the pre-check), nothing documented in the SDK stops two subscriptions being created; if the provider does not enforce subscription-`reference` uniqueness, the app must serialize subscribe attempts per user+plan (single-flight/lock) to honor "never two subscriptions". That serialization is application design — the SDK offers no upsert/create-or-find for subscriptions.
- **YOUR CALL — plan-listing currency formatting.** The `Product` payload carries cents/interval/interval-unit but no currency code; `Subscription.Currency` exists but is only on subscription objects. Seed site is USD — render with the app's own currency assumption; do not attempt to read a currency from the plans endpoint. — not in the map.
- **Assumption — NuGet version.** `1.0.2` (the map's source tag) is assumed to be the NuGet package version to pin; if restore shows otherwise, use the latest published `AsadAli.AdvancedBilling.Sdk` and re-check the map's staleness stamp.
- **Blocker: none.** All three endpoints are fully covered by the operation set above; no map capability gap forces an invented data path.
