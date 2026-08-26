# Maxio Advanced Billing integration plan — eShopOnWeb `src/PublicApi`

Grounded against the bundled SDK map (`sdk-map.md` + `map/operations/*` + `map/models/*`, SDK source tag `v1.0.2`). Every sheet row cites its map page.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|------|---------------------|
| 1 | Add NuGet package (central package management — see §2.0) | — |
| 2 | Register `MaxioAdvancedBillingClient` in `PublicApi` DI, wired to `Maxio:*` config | — (client construction, §2.1) |
| 3 | `GET /api/subscription-plans` — resolve family by handle, list its products | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — ensure customer (by reference), guard duplicate active sub, create subscription by product handle | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — list caller's subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary + tests | (all above) |

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

### 2.0 Package (map: `sdk-map.md`)

- Package id: **`AsadAli.AdvancedBilling.Sdk`** — note the root namespace you import is `MaxioAdvancedBilling`, NOT the package id.
- Version: **`1.0.2`** (the source tag `v1.0.2` this sheet is grounded on). Verify against NuGet at add time; if a newer 1.0.x patch exists, prefer it and treat any compile failure on a sheet name as a staleness signal.
- Repo uses **central package management** (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`): add `<PackageVersion Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2" />` there, and a **versionless** `<PackageReference Include="AsadAli.AdvancedBilling.Sdk" />` in `src/PublicApi/PublicApi.csproj`.
- SDK targets `netstandard2.0` — compatible with the repo's `net8.0`. Transitive deps (Polly, Microsoft.Extensions.Http, System.Net.Http.Json, System.Net.ServerSentEvents) flow automatically.

### 2.1 Client construction, auth, base URL (map: `sdk-map.md` — *Getting a client*, *Servers & auth*)

| Fact | Value |
|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`) |
| Auth type | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` — `BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (password is the literal `"x"`) |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` — `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com` |
| Subdomain | `options.Server.Production.Us.Site = <Maxio:Subdomain>` (`"cp-exp-3"`); `{site}` defaults to `subdomain` if unset |
| **BaseUrl override** | When `Maxio:BaseUrl` is set: `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` **verbatim** — this replaces the derived host entirely (the mock/dev-host override point) |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (source: `ServiceCollectionExtensions.cs`) |
| API groups | Properties on the client: `client.ProductFamilies`, `client.Products`, `client.Customers`, `client.Subscriptions` |

Namespaces to import: `MaxioAdvancedBilling` (client/options), `MaxioAdvancedBilling.Core.Authentication.Basic` (credentials), `MaxioAdvancedBilling.Servers` (`ServerEnvironment`), `MaxioAdvancedBilling.Core.Configuration` (`RetryOptions`, if tuned), `MaxioAdvancedBilling.Models` (records), `MaxioAdvancedBilling.Models.Enums` (enums), `MaxioAdvancedBilling.Errors` (typed error classes). `SdkException<T>` lives at source path `Core/Exceptions/SdkException.cs` ⇒ namespace `MaxioAdvancedBilling.Core.Exceptions`; `RawError` at `Core/ErrorResponse/RawError.cs` ⇒ `MaxioAdvancedBilling.Core.ErrorResponse`.

### 2.2 Operations

| Step | Controller property · signature (verbatim) | Request model | Response envelope → fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| 3 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable params **must be passed explicitly** (pass `null`) | none | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`product_family`): `ProductFamily?` → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`. **Match `Handle == Maxio:ProductFamilyHandle` client-side** | **Case B** `SdkException<RawError>` → `.Error.StatusCode`, `.Error.ReadAsString()` | none |
| 3 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params **must be passed explicitly**; pass `productFamilyId` as the matched family's `Id.ToString()` | none | `IReadOnlyList<ProductResponse>` → `.Product` (`product`): `Product` **!req** → `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` | **Case A** `SdkException<ListProductsForProductFamilyError>` → `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1/20) |
| 3 (alt, single plan) | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none | `ProductResponse` → `.Product` (as above) | **Case B** `SdkException<RawError>` | none |
| 4/5 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` | none | `CustomerResponse` → `.Customer` (`customer`): `Customer` **!req** → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | **Case B** `SdkException<RawError>` — a missing customer is **404 detected via `ex.Error.StatusCode == HttpStatusCode.NotFound`** (no typed accessor) | none |
| 4 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `CreateCustomerRequest` → `.Customer` (`customer`): `CreateCustomer` **!req** with `FirstName (first_name): string` **!req**, `LastName (last_name): string` **!req**, `Email (email): string` **!req**, `Reference (reference): string?` ← set to the stable eShopOnWeb user key | `CustomerResponse` → `.Customer` (as above) | **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. ⚠ `CustomerErrorResponse1.Errors` is typed `Errors?` whose only fields are `PerPage`/`PricePoint` string lists — a suspicious shared model for a customer error; **extract best-effort, fall back to `TryGetRawError`/`ReadAsString()`** `UNVERIFIED` | none |
| 4/5 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` (`subscription`): `Subscription?` → fields below | **Case B** `SdkException<RawError>` | none |
| 4 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `CreateSubscriptionRequest` → `.Subscription` (`subscription`): `CreateSubscription` **!req**. Fields (all optional, set only these): `ProductHandle (product_handle): string?` ← plan handle; `CustomerId (customer_id): int?` ← from ensured customer; `Reference (reference): string?` ← idempotency key (e.g. `{userId}:{productHandle}`); `CustomerReference (customer_reference): string?` exists as an alternative customer key. **Omit all payment fields** (`PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `PaymentProfileId`) | `SubscriptionResponse` → `.Subscription` (as below) | **Case A** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **!req**; `TryGetRawError(out RawError)` [fallback] | none |
| 4 (idempotency probe, optional) | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must be passed explicitly** | none | `SubscriptionResponse` | **Case A** `SdkException<FindSubscriptionError>` → `TryGetNoContent(out RawError)` [404 = no such reference], `TryGetRawError(out RawError)` [fallback] | none |

`Subscription` fields the integration reads (map: `records-3-Of-Su.md`): `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` (nested — `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit` as in §2.2), `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` ← next billing date, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Reference (reference): string?`, `Customer (customer): Customer?`.

### 2.3 Enum values needed (map: `models/enums.md`; all namespace `MaxioAdvancedBilling.Models.Enums`, all `StringEnum<T>` — use static members, e.g. `SubscriptionState.Active`, never a C# enum)

| Enum | Members (C# name ← wire) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` (only if `Subscriptions.ListSubscriptions` is ever used) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` (only if overriding collection on create) | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |

### 2.4 Idempotency & handle-lookup facts

- **Customer dedupe**: `ReadCustomerByReference(reference)` is an exact-match single-result lookup; `reference` is unique per customer (map: `operations/Customers.md` CreateCustomer notes). 404 (Case B `StatusCode`) ⇒ create.
- **Duplicate-subscription guard**: `ListCustomerSubscriptions(customerId)` then client-side match on `Subscription.Product?.Handle == planHandle && Subscription.State == SubscriptionState.Active`. There is **no server-side idempotency key** on `POST /subscriptions.json`; `CreateSubscription.Reference` + `FindSubscription(reference)` is the available probe.
- **Family by handle**: `ReadProductFamily` takes `int id` — its doc note about `handle:my-family` format **cannot be used through the generated `int` signature**. Use `ListProductFamilies` + client-side `Handle` match (this is the plan's path). `ListProductsForProductFamily` takes `string productFamilyId`; passing `"handle:eshop-subscribe"` there directly is plausible but **UNVERIFIED** — pass the numeric `Id.ToString()`.
- **Product by handle**: `ReadProductByHandle(apiHandle)` is a first-class handle lookup for single-plan reads.
- **No-payment signup**: omitting all payment fields works only because the seeded products don't require a card — that is live site/product configuration, **UNVERIFIED** from the map. Defensive directive: on 422 from `CreateSubscription`, surface `ErrorListResponse1.Errors` messages verbatim to the caller/logs.
- `ListSubscriptions` has **no customer filter param** — per-customer listing is `Customers.ListCustomerSubscriptions`.

## 3. Trap notes (hazard named, answer NOT inlined — load the skill)

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has specific lifetime requirements; rebuilding it per request is a defect. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)` or the DI registration.
>
> ⚠ Step 2 (auth) — when in the construction sequence credentials must be set, and where the API key must come from (never hardcoded). **MUST load `dotnet-authentication`**.
>
> ⚠ Steps 3–5 (every call) — most list/read signatures carry nullable params with **no C# default** that mis-bind in positional calls; named arguments are required. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(...)` call.
>
> ⚠ Steps 3–5 (models) — enums are `StringEnum<T>` records (not C# enums), unions are built via factories and read via `TryGet…`, and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** when mapping `Product`/`Subscription` onto eShopOnWeb DTOs.
>
> ⚠ Step 6 (error boundary) — which of the eight operations above are Case A vs Case B is per-operation (see §2.2); `TryGetRawError` is not a catch-all on typed errors; this SDK has **no** no-throw `…Result` variants — every call is throw-only. **MUST load `dotnet-error-handling`**.
>
> ⚠ Step 2/4 (resilience) — whether a failed `CreateSubscription` POST can be re-sent by the retry layer (a duplicate-create risk no setting fully removes), and what the `Timeout` option actually bounds. **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` or relying on retries near the non-idempotent create.
>
> ⚠ Step 6 (tests) — the SDK's test seam is a specific constructor argument; match the repo's xunit/NSubstitute style. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — governs step 2 (client construction & DI).
- `dotnet-authentication` — governs step 2 (Basic credentials wiring).
- `dotnet-calling-endpoints` — governs steps 3–5 (every operation call).
- `dotnet-models` — governs steps 3–5 (request/response model construction & mapping).
- `dotnet-error-handling` — governs step 6 (the integration's error boundary). Mandatory regardless of trap-note count:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
- `dotnet-configuration-resilience` — governs step 2/4 (retries, timeout, base-URL override semantics).
- `dotnet-testing` — governs step 6 (faking the SDK seam).

## 5. Assumptions & Blockers

- **Assumed**: product-family resolution is `ListProductFamilies` + client-side handle match (the `handle:` doc format is unreachable through `ReadProductFamily(int id)`; `"handle:…"` into `ListProductsForProductFamily(string)` is UNVERIFIED).
- **Assumed**: the eShopOnWeb user key used as Maxio customer `reference` is the Identity user ID (stable); email alone is acceptable but mutable.
- **UNVERIFIED** (live-config only): seeded products requiring no payment method means card-less `CreateSubscription` succeeds; 422 handling path specified defensively in §2.4.
- **UNVERIFIED**: `CustomerErrorResponse1.Errors` payload shape (suspicious shared `Errors` model — `PerPage`/`PricePoint` fields); extract best-effort, fall back to raw body.
- **Assumed**: prices are cents (`long`); format to dollars in the API DTOs, not in the SDK layer.
- Metered component `api-call` is out of scope; no component operations are needed for these three endpoints.
- No blockers.
