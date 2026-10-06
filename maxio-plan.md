# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Source for every row below: the bundled SDK map (pages cited per row). Package `AsadAli.AdvancedBilling.Sdk`, root `using MaxioAdvancedBilling`, .NET 8 target (SDK targets `netstandard2.0`, runs on .NET 8+).

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Config binding + client registration from `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:BaseUrl` (optional, verbatim), `Maxio:ProductFamilyHandle` | client construction (§2 facts) |
| 2 | `GET /api/subscription-plans` — plan catalog (Pro `eshop-pro`, Basic `basic-plan`) is site configuration, not an SDK call; optionally hydrate live name/price per handle | `Products.ReadProductByHandle` (optional) |
| 3 | `POST /api/subscriptions` → ensure Maxio customer exists for the JWT user (idempotent) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ReadCustomer` |
| 4 | `POST /api/subscriptions` → create the subscription on the chosen plan handle (idempotent by subscription reference) | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` → resolve the user's Maxio customer, list its subscriptions | `Customers.ReadCustomerByReference` / `CreateCustomer` (reuse step 3), `Customers.ListCustomerSubscriptions` |
| 6 | Confirm plan/price/state/next-billing for a single subscription | `Subscriptions.ReadSubscription` |
| 7 | Error boundary mapping 401/404/422; plan-handle resolution (nested Product, fallback product read) | all of the above |
| 8 | Tests for the integration layer | fakes over the client seam |

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

### 2.1 Operations

| Operation | Signature (params in order, literal names) | Returns | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` | `CustomerResponse` | **Case B** `MaxioAdvancedBilling.SdkException<RawError>` — read `ex.Error.StatusCode` (404 = no customer with that reference; 401/403 also land here), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` | none | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** | `CustomerResponse` | **Case A** `SdkException<Errors.CreateCustomerError>` — `ex.Error.TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], `ex.Error.TryGetRawError(out RawError)` [fallback]. ⚠ the typed 422 payload is weak (see §2.3) — extract messages via the raw fallback | none | `operations/Customers.md` |
| `client.Customers.ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | `CustomerResponse` | **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`) | none | `operations/Customers.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** | `SubscriptionResponse` | **Case A** `SdkException<Errors.CreateSubscriptionError>` — `ex.Error.TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], `ex.Error.TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — query param `reference`; `reference` nullable, **no default → must pass explicitly** (pass `null` is allowed but meaningless here) | `SubscriptionResponse` | **Case A** `SdkException<Errors.FindSubscriptionError>` — `ex.Error.TryGetNoContent(out RawError)` [**404** = no subscription with that reference], `ex.Error.TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<Models.Enums.SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, **no default → must pass explicitly** (pass `null`) | `SubscriptionResponse` | **Case B** `SdkException<RawError>` (`StatusCode` — 404 unknown id — `ReadAsString()`, `ReadAsJson<T>()`) | none | `operations/Subscriptions.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` (all of the customer's subscriptions) | **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`) | **none** — returns the full set in one call; do not loop pages | `operations/Customers.md` |
| `client.Products.ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | `ProductResponse` | **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`) | none | `operations/Products.md` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `ProductResponse` | **Case B** `SdkException<RawError>` (`StatusCode` — 404 unknown handle, `ReadAsString()`, `ReadAsJson<T>()`) | none | `operations/Products.md` |

**Not usable for this scope (map evidence):** `Subscriptions.ListSubscriptions` (`operations/Subscriptions.md`) has **no customer filter** among its parameters (`state`, `product`, `productPricePointId`, `coupon`, `couponCode`, `dateField`, dates, `metadata`, `direction`, `sort`, `include`, `page`, `perPage`) — it cannot back "my subscriptions". Use `ListCustomerSubscriptions(customerId)` instead. `CreateSubscription` takes **no product-family parameter** — the plan is selected by `product_handle` (site-unique); the configured `Maxio:ProductFamilyHandle` is site/catalog context only, not a create argument.

### 2.2 Request / response models (namespace `MaxioAdvancedBilling.Models` unless stated)

`CSharpName (wire_name): Type`; `!req` = C# `required` (must appear in the object initializer); trailing `?` = optional.

**CreateCustomerRequest** (`records-1-Ac-Cr.md`) — `Customer (customer): CreateCustomer !req`

**CreateCustomer** (`records-1-Ac-Cr.md`) — nothing marked `!req`-beyond-these; fields the provider validates:
- `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req`
- `Reference (reference): string?` — **the idempotency key: set to the eShopOnWeb user id.** The provider's Notes state a `reference` must be unique per site ("you may only create one customer for a given reference value") — this is what makes find-or-create sound.
- `Organization (organization): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `Address/City/State/Zip/Country…`: all optional; leave out (state/country, when supplied, must be ISO 2-char codes per the operation's Notes).

**CustomerResponse** (`records-2-Cr-Ne.md`) — `Customer (customer): Customer !req` (exactly one field — reads go one level down)

**Customer** (`records-2-Cr-Ne.md`) — read-back fields: `Id (id): int?` (**the Maxio customer id**), `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `CreatedAt (created_at): DateTimeOffset?`

**CreateSubscriptionRequest** (`records-2-Cr-Ne.md`) — `Subscription (subscription): CreateSubscription !req`

**CreateSubscription** (`records-2-Cr-Ne.md`) — **no field is `!req`**; the operation's Notes name what the provider requires: identify the plan by `product_id` **or** `product_handle`, and the customer by `customer_id` **or** `customer_reference`. Set these (everything else is deliberately left out):
- `ProductHandle (product_handle): string?` — `eshop-pro` / `basic-plan`
- `CustomerId (customer_id): int?` — the Maxio customer id from step 3
- `Reference (reference): string?` — **set a deterministic value for idempotent creates**, e.g. `{userId}:{planHandle}`; enables `FindSubscription` lookup-back
- `PaymentCollectionMethod (payment_collection_method): Models.Enums.CollectionMethod?` — optional; plans without card capture can omit it
- `Metafields (metafields): IReadOnlyDictionary<string, string>?` — **available at create, but not readable back from `Subscription`** (see §2.5)
- Left out (Notes-named, not needed): `PaymentProfileId`, `CreditCardAttributes`/`BankAccountAttributes`/`PaymentProfileAttributes` (no card capture), `Components` (the metered `api-call` component is allocated later, not at signup), `CouponCode(s)`, `CustomerAttributes` (customer already exists), `ProductPricePointHandle/Id`, `CustomPrice`, `NextBillingAt`, `Deferred`/calendar fields.

**SubscriptionResponse** (`records-4-Su-We.md`) — `Subscription (subscription): Subscription?` (exactly one field — reads go one level down)

**Subscription** (`records-3-Of-Su.md`) — fields the integration reads:
- `Id (id): int?` — the subscription id
- `State (state): Models.Enums.SubscriptionState?`
- `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — **this is the next billing date on the response** (there is no `NextBillingAt` on the response model; `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` is the period end)
- `ProductPriceInCents (product_price_in_cents): long?` · `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` · `BalanceInCents (balance_in_cents): long?` — price in **cents**
- `Product (product): Product?` — nested; carries the plan handle (see §2.5)
- `Customer (customer): Customer?` — nested (`Id`, `Reference`)
- `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`

**Product** (`records-3-Of-Su.md`) — `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductFamily (product_family): ProductFamily?`

**Error payloads** (`records-2-Cr-Ne.md`):
- `CustomerErrorResponse1` — `Errors (errors): Models.Errors?` where `Errors` has only `PerPage (per_page)`/`PricePoint (price_point)` — **does not carry the real 422 messages; treat the typed accessor as a presence check and read `TryGetRawError` → `ReadAsString()` for the body.**
- `ErrorListResponse1` — `Errors (errors): IReadOnlyList<string> !req` — direct message list; use for subscription-create 422s.

### 2.3 Enums (namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>` — write the member name, wire value goes over the wire) — `models/enums.md`

| Enum | Members (`Member (wire)`) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionInclude` (only for `ReadSubscription`'s `include` param) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` — pass `null` in this integration |

### 2.4 Client construction & configuration (from `sdk-map.md`)

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic; // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                   // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // Username = API key, Password literal "x"
    Environment = ServerEnvironment.Us,                                          // US-hosted sandbox = default
};
options.Server.Production.Us.Site = subdomain;        // {site} placeholder in https://{site}.chargify.com
if (!string.IsNullOrWhiteSpace(baseUrl))
    options.Server.Production.Us.BaseUrl = baseUrl;   // used VERBATIM when set — full host incl. scheme, no path
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

- The **only** constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- Config binding keys: `Maxio:ApiKey` → `BasicAuth.Username`; `Maxio:Subdomain` → `Server.Production.Us.Site`; `Maxio:BaseUrl` (optional) → `Server.Production.Us.BaseUrl` **verbatim when set** (it replaces the whole `https://{site}.chargify.com` template); `Maxio:ProductFamilyHandle` (`eshop-subscribe`) is app/catalog configuration only — no SDK call in this scope takes it.
- Sandbox = the US environment (`ServerEnvironment.Us`); `ServerEnvironment.Eu` exists for EU-hosted accounts only.

### 2.5 Idempotency & plan-handle resolution (decisions, map-grounded)

**Customer find-or-create (step 3):** `reference` = the eShopOnWeb user id (from JWT claims). Flow: `ReadCustomerByReference(reference)` → 2xx ⇒ customer exists, read `resp.Customer.Id.Value`; `RawError.StatusCode == 404` ⇒ `CreateCustomer` (FirstName/LastName/Email/Reference) ⇒ read `resp.Customer.Id.Value`. Double-click race: both legs 404 then both create — the second create fails 422 (provider enforces one customer per reference, per the `CreateCustomer` Notes) ⇒ catch Case A, read the raw 422 body, **re-run `ReadCustomerByReference`** and return the now-existing customer. Create is therefore never assumed idempotent — lookup-first plus 422-recovery makes it so.

**Subscription create (step 4):** set `subscription.Reference = "{userId}:{planHandle}"` (deterministic). Flow: `FindSubscription(reference)` → 2xx ⇒ already subscribed, return it; `TryGetNoContent` 404 ⇒ `CreateSubscription`. On create-time 422 (race), re-run `FindSubscription`. Two subscriptions on different plans for one user are allowed (reference includes the plan handle).

**Plan handle resolution — recommendation: (b) read the nested Product off the subscription; do NOT build a metadata scheme.** Map evidence: `Subscription` carries `Product (product): Product?` and `Product` carries `Handle (handle): string?` — the handle is directly readable from every `SubscriptionResponse` (create, read, list). Reject (a) on map evidence: `CreateSubscription` accepts `Metafields`, but the `Subscription` **response** record has **no metadata field**, so a value stored at create is not readable back through this SDK's models. Defensive rule: read `subscription.Product?.Handle`; if `Product` is null, call `ReadProduct` — but **`Product.Id` is also nested**, so if both are missing the handle cannot be resolved from that response at all — surface the subscription with an unresolved plan marker rather than guessing. Whether the live create/list responses actually populate the nested `Product`/`Customer` objects is **UNVERIFIED** (the model declares the fields; only live traffic confirms they arrive populated).

### 2.6 Error mapping (for the boundary)

| Status | Where it appears | What it means here |
|---|---|---|
| 401/403 | `RawError.StatusCode` on every Case-B op; via `TryGetRawError` on Case-A ops | credential/config fault — never retry, never map to 5xx |
| 404 | `RawError.StatusCode` (Case B); `TryGetNoContent` on `FindSubscription` | "absent" — the expected branch of find-or-create |
| 422 | `TryGetCustomerErrorResponse1` / `TryGetErrorListResponse1` (Case A) | deterministic validation rejection (duplicate reference, bad handle) — read messages via `TryGetRawError`→`ReadAsString()` (customer op: typed payload is weak) or the `ErrorListResponse1.Errors` string list (subscription op) |

**Retry safety:** all GET/lookup operations in §2.1 (`ReadCustomerByReference`, `ReadCustomer`, `FindSubscription`, `ReadSubscription`, `ListCustomerSubscriptions`, `ReadProduct`, `ReadProductByHandle`) are safe to re-issue. `CreateCustomer`/`CreateSubscription` are **not** HTTP-idempotent — their safety comes from the §2.5 reference strategy, not from retry settings. Whether the SDK's retry layer re-sends a POST after a transport failure is a resilience-option question — see the Step-1 trap below before touching `options.Retry`.

## 3. Trap notes (hazard + MUST load — the skill carries the resolution)

- ⚠ Step 1 (client registration) — the SDK client must be built over an `HttpClient` whose handler pipeline lives long enough for the SDK's transport and retry stack; a per-request client/`HttpClient` disposal pattern can break that pipeline. **MUST load `dotnet-client-initialization`** before writing the factory/DI registration.
- ⚠ Step 1 (credentials) — credentials must be on the options before the client is constructed, and the Basic pair is asymmetric (username = API key, password = literal `"x"`); loading them from the config keys rather than hardcoding is the point. **MUST load `dotnet-authentication`** before wiring `BasicAuthCredentials`.
- ⚠ Step 1 (resilience options) — the SDK's retry/timeout option names do **not** reveal which calls get retried, what a timeout bounds, or what happens to a failed POST on a transport error — i.e. whether a customer/subscription create can execute twice — and the `BaseUrl` override semantics (verbatim full host) must not be confused with the `Site` placeholder. **MUST load `dotnet-configuration-resilience`** before setting anything on `options.Retry` or choosing between `Site`/`BaseUrl`.
- ⚠ Steps 3–6 (calling) — several parameters above are nullable with **no C# default** (`body`, `include`) and must be passed explicitly even when empty; list/find ops should be called with **named arguments** because positional binding can mis-bind long optional lists elsewhere in this SDK. Response payloads sit one level down inside the envelope (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`). **MUST load `dotnet-calling-endpoints`** before the first `client.…` call.
- ⚠ Steps 3–6 (models) — "enums" are `StringEnum<T>` records (write `SubscriptionState.Active`, never a C# enum cast); `!req` members must appear in the initializer; wire names differ from C# names; unmodeled JSON fields are silently dropped on deserialize — a field missing from §2.2 does not exist on the type. **MUST load `dotnet-models`** before constructing request payloads or mapping responses to app DTOs.
- ⚠ Step 7 (error boundary) — the Case A / Case B split is per-operation (§2.1), `TryGetRawError` is not a catch-all on typed errors, and `JsonException` reaches the boundary from two directions (see the two mandatory rows in §4) with opposite consequences. **MUST load `dotnet-error-handling`** before writing any try/catch, translation layer, or middleware.
- ⚠ Step 8 (tests) — the test seam is the `HttpClient` constructor argument of the client, not the controllers; stub at that seam and keep tests off SDK internals. **MUST load `dotnet-testing`** before writing the integration tests.

## 4. REQUIRED READING (load BEFORE implementation starts — the sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client factory/DI registration, `HttpClient` ownership and lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` wiring from configuration, 401/403 diagnosis |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions` semantics, `Timeout` bounds, `Site` vs `BaseUrl` override, POST re-send behavior |
| `dotnet-calling-endpoints` | Steps 3–6 — named arguments, must-pass-explicitly nullable params, envelope unwrapping, cancellation |
| `dotnet-models` | Steps 3–6 — request builders, `StringEnum<T>`, wire names, unions (none in scope), unmodeled-field drops |
| `dotnet-error-handling` | Step 7 — Case A/B catch ladders, `TryGet…` accessors, the two `JsonException` hazards below |
| `dotnet-testing` | Step 8 — faking the `HttpClient` seam, covering error paths |

Mandatory hazard rows (belong in the first sheet — the boundary is written early):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumption:** the sandbox site is seeded as described (product family `eshop-subscribe`, products `eshop-pro` $299/mo and `basic-plan` $29/mo, no card required on these plans). No map row can confirm site contents — the first `ReadProductByHandle` call against the real sandbox verifies it.
- **Assumption:** the eShopOnWeb JWT exposes a stable, non-empty user id and email claim usable as customer `reference`/`email`. Which claims exist is the app's identity path — your call, not in the map (`operations/Customers.md` documents the `reference` semantics only).
- **Assumption:** "plans endpoint" is served from site configuration (the two handles + prices are already known); `ReadProductByHandle` is wired only if live name/price is wanted.
- **UNVERIFIED (defensive-coding directive):** whether create/list subscription responses actually populate the nested `Product`/`Customer` objects. Extract best-effort (`subscription.Product?.Handle`), and when the nested object is absent surface an unresolved-plan marker instead of guessing; only live traffic can confirm population.
- **No Blockers.** Every operation and model in scope is fully specified by the map; no capability required by the scope is missing from the SDK.