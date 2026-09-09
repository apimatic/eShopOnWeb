# maxio-plan.md — Recurring-subscription billing for eShopOnWeb via Maxio Advanced Billing (.NET SDK)

Feature: additive/parallel subscription capability. Logged-in shopper browses plans, subscribes,
sees plan/price/state/next-billing-date in their account. Maxio Advanced Billing is the billing
system of record; the existing cart/checkout flow is untouched. Endpoints on `src/PublicApi`
(JWT-authenticated), under `/api/`.

---

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Package + configuration: add NuGet `AsadAli.AdvancedBilling.Sdk` (version **1.0.2**, the release the SDK map was generated from); bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional) from user-secrets/config; register the SDK client in DI | (client construction — `MaxioAdvancedBillingClient`, `AddMaxioAdvancedBillingClient`) |
| 2 | Plan catalog: resolve the product family by handle from config, list its products, expose `GET /api/subscription-plans` | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 3 | Customer provisioning (idempotent): `GET /api/subscriptions` POST path first resolves-or-creates a Maxio customer keyed on the eShopOnWeb user id via the customer `reference` field | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 4 | Subscribe (app-side idempotent): check the user's existing subscriptions for an active subscription to the same product before creating; create with no payment profile; expose `POST /api/subscriptions` | `Customers.ReadCustomerByReference` (reuse of step 3 result), `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 5 | My subscriptions: list the user's subscriptions with state / price / next-billing date; expose `GET /api/my-subscriptions` | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Plan/price verification: read plan details back from the subscription response (embedded `Product` + `ProductPriceInCents`); for a single subscription `Subscriptions.ReadSubscription` | `Subscriptions.ReadSubscription` (optional), `Subscriptions.FindSubscription` (optional correlation lookup) |
| 7 | Error boundary + endpoints wiring: typed catch ladder per call site, JSON problem responses, JWT auth on all three endpoints | (no SDK calls — see trap notes) |

Ordering rationale: the error boundary (step 7's hazards) must be *designed* with step 2–5, not
bolted on — the two `JsonException` hazard rows in §4 belong in the first pass. Step 3's customer
resolution is a subroutine of steps 4 and 5.

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

### Client, auth, servers

| Fact | Detail | Source |
|---|---|---|
| Package / namespace | NuGet `AsadAli.AdvancedBilling.Sdk` v**1.0.2**; root `using MaxioAdvancedBilling;` (package id ≠ namespace) | `sdk-map.md` |
| Client | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — the only constructor; API groups are properties (`client.Customers`, `client.Subscriptions`, `client.Products`, `client.ProductFamilies`) | `sdk-map.md` |
| Options | `MaxioAdvancedBillingClientOptions { Environment: ServerEnvironment; Retry: RetryOptions; Server: ServerOptions; BasicAuth: BasicAuthCredentials? }` | `sdk-map.md` |
| Auth | HTTP Basic only: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <MAXIO_API_KEY>, Password = "x" }` — username = API key, password = the literal string `"x"`. **No `X`-timestamp header, no HMAC — this API is Basic-only.** | `sdk-map.md` §Servers & auth |
| Sandbox targeting | `options.Server.Production.Us.Site = "<MAXIO_SITE_SUBDOMAIN>"` → base URL `https://{site}.chargify.com`. `Environment` stays `ServerEnvironment.Us` unless the account is EU-hosted (`ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`). | `sdk-map.md` §Servers & auth |
| BaseUrl override (verbatim) | `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` — set it **instead of** `Site` when configured; the value is used as the base address verbatim (no subdomain interpolation). Only override `Production` — no in-scope operation uses the `Ebb` server group. | `sdk-map.md` §Servers & auth |
| RetryOptions | namespace `MaxioAdvancedBilling.Core.Configuration`; **all members `required`** — start from `RetryOptions.Default()` and mutate, never build a bare instance | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` (root namespace, `ServiceCollectionExtensions`) | `sdk-map.md` |

### Operations

All controller classes live in `MaxioAdvancedBilling.Api` (reachable as properties on the client).
Request/response records live in `MaxioAdvancedBilling.Models`; enums in `MaxioAdvancedBilling.Models.Enums`.
Nullable-with-no-default parameters **must be passed explicitly** (pass `null` to skip).

| Controller · method | Signature (params in order) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` | — | `CustomerResponse { Customer (customer): Customer !req }` → `Customer.Reference (reference)`, `.Id (id)`, `.Email (email)`, `.FirstName`, `.LastName` | **Case B** `SdkException<RawError>` — `ex.Error.StatusCode` (expect **404 = no customer yet**), `.ReadAsString()`, `.ReadAsJson<T>()` | none | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — body nullable, no default → pass explicitly | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` wrapping `CreateCustomer`: `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` · optional address/city/state/zip/country/phone/locale… | `CustomerResponse { Customer }` (as above) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [**422**: duplicate reference, validation] where `CustomerErrorResponse1 { Errors (errors): Errors? }` and `Errors { PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>? }`; `TryGetRawError(out RawError)` fallback | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — the 5 nullable params must be passed explicitly (pass `null`s) | — | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse { ProductFamily (product_family): ProductFamily? }` → match `ProductFamily.Handle (handle)` against `Maxio:ProductFamilyHandle`, read `.Id (id)` | **Case B** `SdkException<RawError>` | none (full list) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 nullable params must be passed explicitly; pass the family **Id** from the previous row (as string) | — | `IReadOnlyList<ProductResponse>`; `ProductResponse { Product (product): Product !req }` → present `Product.Id`, `.Handle`, `.Name`, `.Description`, `.PriceInCents (price_in_cents): long?`, `.Interval (interval): int?`, `.IntervalUnit (interval_unit): IntervalUnit?` (enum `MaxioAdvancedBilling.Models.Enums.IntervalUnit`), `.ProductPricePointName`, `.RequestCreditCard`, `.RequireCreditCard`, `.TrialInterval` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404], `TryGetRawError(out RawError)` fallback | manual `page`+`perPage` (defaults 1/20) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `client.Customers.ListCustomerSubscriptions` — **lives on the `Customers` controller** (`Api/Customers.cs`), NOT on `client.Subscriptions` (the `Subscriptions` controller has no such method) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — HTTP `GET /customers/{customer_id}/subscriptions.json` | — | `IReadOnlyList<SubscriptionResponse>`; each `SubscriptionResponse { Subscription (subscription): Subscription? }` → filter on `Subscription.State` + `Subscription.Product.Id` / `.Product.Handle` | **Case B** `SdkException<RawError>` — `ex.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` | none — no `page`/`perPage` params exist; returns the full list for the customer | `operations/Customers.md`, `records-3-Of-Su.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — body nullable, no default → pass explicitly | **Yes — the body is wrapped**: `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. Fields of `CreateSubscription` used here: `ProductHandle (product_handle): string?` (or `ProductId (product_id): int?`) · `CustomerId (customer_id): int?` · `CustomerReference (customer_reference): string?` · `Reference (reference): string?` (app correlation id) · `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — leave unset; no payment-profile fields are sent (sandbox plans don't require a payment method) | `SubscriptionResponse { Subscription }` — see the Subscription field table below; read plan/price/state/next-billing from it directly for the immediate confirmation response | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [**422**] where `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }`; `TryGetRawError(out RawError)` fallback | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default → pass `null` | — | `SubscriptionResponse { Subscription }` (as below) | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → pass explicitly — query `reference` | — | `SubscriptionResponse { Subscription }` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [**404 = not found**], `TryGetRawError(out RawError)` fallback | none | `operations/Subscriptions.md` |
| `client.Subscriptions.ListSubscriptions` (site-wide; only if per-customer listing is insufficient) | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 14 nullable params must be passed explicitly | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Subscriptions.md` |

### `Subscription` fields the integration reads (`MaxioAdvancedBilling.Models.Subscription`, wire names in parens)

| Field | Wire name | Type | Used for |
|---|---|---|---|
| `Id` | `id` | `int?` | correlation, dedupe check |
| `State` | `state` | `SubscriptionState?` (enum) | live/active check — enum values below |
| `Product` | `product` | `Product?` (embedded) | plan confirmation: `.Id`, `.Handle`, `.Name`, `.PriceInCents`, `.Interval`, `.IntervalUnit` |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | current price (cents) |
| `ProductPricePointId` | `product_price_point_id` | `int?` | price point correlation |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | period end |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | **next billing date** |
| `CurrentPeriodStartedAt` | `current_period_started_at` | `DateTimeOffset?` | period start |
| `ActivatedAt` | `activated_at` | `DateTimeOffset?` | activation time |
| `Customer` | `customer` | `Customer?` (embedded) | customer echo (`Customer.Reference`, `.Id`) |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` (enum) | echo |
| `Reference` | `reference` | `string?` | app-set correlation id, read back |

Source: `records-3-Of-Su.md` (record `Subscription`, `Models/Subscription.cs`).

### Enum values actually needed (`MaxioAdvancedBilling.Models.Enums`, `StringEnum<T>` — build with `Type.FromValue("wire")` or the static members shown; these are **not** C# enums)

| Enum | Members (C# literal → wire value) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` — treat `Active` as subscribed; `Assessing` is a transient internal state, never gate access on it | `models/enums.md` |
| `SubscriptionStateFilter` (for `ListSubscriptions` only) | `Active`, `Canceled`, `Expired`, `ExpiredCards (expired_cards)`, `OnHold`, `PastDue`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` — note: no `Assessing` here | `models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — leave unset when creating with no payment method | `models/enums.md` |
| `IntervalUnit` | monthly/yearly-type unit on `Product.IntervalUnit` — use static members; consult `models/enums.md` for the full list if displayed | `models/enums.md` |

### Wire bodies the app builds (JSON shapes implied by the envelope records)

- **Create customer** — `POST /customers.json`:
  `{ "customer": { "first_name": …, "last_name": …, "email": …, "reference": "<eShopOnWeb user id>" } }`
- **Create subscription** — `POST /subscriptions.json` (**body is wrapped in `subscription:`**):
  `{ "subscription": { "product_handle": "<plan handle>", "customer_id": <maxio customer id>, "reference": "<app correlation id>" } }`
  No `payment_profile_attributes` / `credit_card_attributes` / `payment_collection_method` — the sandbox plans require no payment method. Build these via the SDK records (`CreateSubscriptionRequest` → `CreateSubscription`), never via hand-written JSON — the records carry the exact wire names.

### Idempotency facts

| Mechanism | Fact | Source |
|---|---|---|
| Customer `reference` | **Maxio enforces uniqueness**: "you may only create one customer for a given reference value" — a duplicate-create returns **422** on `CreateCustomer`. So: `ReadCustomerByReference(user-id)` first; on 404 → create; on create-422 (double-click race) → re-read by reference. | `operations/Customers.md` Notes |
| Subscription reference | `CreateSubscription` accepts `reference` and `FindSubscription` looks up by it, but the map documents uniqueness **only for customer reference** — do not rely on Maxio rejecting a duplicate subscription reference. Treat subscription `reference` as a correlation id (`"{userId}:{productHandle}"`), dedupe **app-side**. | `operations/Subscriptions.md` Notes |
| App-side subscribe dedupe | Before create: `ReadCustomerByReference` → `ListCustomerSubscriptions(customer.Id)` → if any subscription has `State == SubscriptionState.Active` (or other live state you choose to honor) **and** `Product.Id == target product id`, return the existing subscription instead of creating. The check-then-create window is inherent; whether a true double-submit slips two subscriptions through is `UNVERIFIED` (only live traffic can show whether the provider dedupes anything here) — code the create path defensively (handle 422, re-read) regardless. | derived from `operations/Customers.md`, `operations/Subscriptions.md` |

---

## 3. Trap notes (attached to the step where each bites)

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline must be long-lived and reused via
  `IHttpClientFactory`, not rebuilt per request; how the SDK's DI extension and the `HttpClient`
  constructor argument interact decides the client's lifetime. **MUST load `dotnet-client-initialization`**
  before wiring the client.
- ⚠ Step 1 (credentials) — the auth shape above is the *pattern*; where credentials get set relative to
  client construction (and how to rotate/load them from configuration) has traps the one-liner cannot
  carry. **MUST load `dotnet-authentication`** before setting `BasicAuth`.
- ⚠ Step 1 (BaseUrl override / sandbox) — the retry/timeout options do **not** bound a whole call and are
  **not** the `HttpClient` timeout; how `Environment`, `Server.*.Site`, and a verbatim `BaseUrl` override
  interact decides whether your sandbox host is actually hit. **MUST load
  `dotnet-configuration-resilience`** before wiring client options — also before reasoning about whether
  a retried POST can execute twice (a transport failure is retried on every verb, `POST` included).
- ⚠ Steps 2–5 (every call) — many list/find ops here are Case B (`SdkException<RawError>`, no typed
  accessors) while create ops are Case A; several nullable parameters have no C# default and mis-bind in
  positional calls. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–5 (request/response models) — request bodies are envelope records (`CreateSubscriptionRequest.Subscription`,
  `CreateCustomerRequest.Customer`) and enums are `StringEnum<T>`, not C# enums; unmodeled JSON fields are
  dropped on deserialize. **MUST load `dotnet-models`** before constructing payloads or mapping responses.
- ⚠ Step 7 (error boundary) — see the two `JsonException` hazard rows in §4; the Case A/B mechanics and
  the `TryGet…` ladder live in the skill, not here. **MUST load `dotnet-error-handling`** before writing
  any try/catch.
- ⚠ Tests — the `HttpClient` constructor argument is the test seam for stubbing the SDK; match the
  project's existing test framework. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

| Skill | Governs | Hazard one-liner |
|---|---|---|
| `dotnet-client-initialization` | Step 1 | HttpClient ownership/lifetime and DI registration — the signature won't tell you the pipeline must be long-lived. |
| `dotnet-authentication` | Step 1 | Credential placement relative to client construction; Basic username = API key, password = `"x"` — loaded from config, never hardcoded. |
| `dotnet-calling-endpoints` | Steps 2–5 | Named arguments for nullable-no-default params; Case B vs Case A per call; envelope unwrapping (`.Customer`, `.Subscription`, `.Product`). |
| `dotnet-models` | Steps 2–5 | Envelope request records, `StringEnum<T>` construction, wire names vs C# names, unmodeled fields dropped. |
| `dotnet-error-handling` | Step 7 | Case A/B catch ladders, `TryGet…` accessors, and the two `JsonException` hazards below. |
| `dotnet-configuration-resilience` | Step 1 | What `Timeout` actually bounds, retry gating (`HttpMethodsToRetry` vs transport-failure retry on POST), pagination defaults, BaseUrl override semantics. |
| `dotnet-testing` | Tests | The `HttpClient` seam; asserting real behavior, not execution. |

**Both of these hazard rows are mandatory in the first pass** — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

**Assumptions**
1. `Maxio:ProductFamilyHandle` holds the family handle (e.g. `eshop-subscribe`) and the plans are the
   products inside it; plan product handles (`eshop-pro`, `basic-plan`) are sandbox data, discovered at
   runtime via `ListProductsForProductFamily`, not hardcoded.
2. `MAXIO_ENVIRONMENT` maps to `ServerEnvironment.Us` unless its value indicates EU hosting; the config
   section only carries `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` — the US/EU choice is
   derived from the environment setting the deployment supplies.
3. The eShopOnWeb user's identity (id/email) is available to the PublicApi endpoints from the JWT
   principal — **your call — not in the map**; the plan only requires that a stable user id and email
   reach the customer-resolution step.
4. Sandbox plans require no payment method and have no trial/setup fee, so no payment-profile fields are
   sent at create time; if a plan unexpectedly requires one, `CreateSubscription` returns 422 and the
   error list surfaces it.
5. Package version **1.0.2** is pinned (the release the SDK map was generated from).

**Blockers**
- None that stop planning. One **unverified** item (not a blocker): whether the provider dedupes
  concurrent identical subscription creates (see Idempotency facts) — only live traffic can confirm;
  the create path is coded defensively either way.
