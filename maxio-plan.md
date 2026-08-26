# Maxio Advanced Billing integration plan — eShopOnWeb `src/PublicApi`

SDK: `AsadAli.AdvancedBilling.Sdk` (NuGet) · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / commit `15db14b`.

## 1. Scope & sequence

Additive JWT-authenticated endpoints on `src/PublicApi`, backed by one SDK client:

| # | Step | Endpoint | SDK operations used |
|---|------|----------|---------------------|
| 1 | Install package + register client & options (auth, server) | — | — |
| 2 | List plans | `GET /api/subscription-plans` | `ProductFamilies.ListProductsForProductFamily` |
| 3 | Subscribe (idempotent) | `POST /api/subscriptions` | `Customers.ReadCustomerByReference` → (`Customers.CreateCustomer` if 404) → `Subscriptions.CreateSubscription` |
| 4 | List caller's subscriptions | `GET /api/my-subscriptions` | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 5 | Error boundary (SDK → HTTP mapping) | all | per-operation error cases below |

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

Namespaces needed (`sdk-map.md`): `MaxioAdvancedBilling` (client + options + `ServerOptions`), `MaxioAdvancedBilling.Core.Authentication.Basic` (`BasicAuthCredentials`), `MaxioAdvancedBilling.Servers` (`ServerEnvironment`), `MaxioAdvancedBilling.Core.Configuration` (`RetryOptions`), `MaxioAdvancedBilling.Models` (records), `MaxioAdvancedBilling.Models.Enums` (enums), `MaxioAdvancedBilling.Errors` (typed error classes), `MaxioAdvancedBilling.Core.Exceptions` (`SdkException<T>`), `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`).

### Client construction / auth / server (map: `sdk-map.md`)

```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = config["Maxio:ApiKey"], Password = "x" }, // password is the literal "x"
    Environment = ServerEnvironment.Us, // default; US hosting assumed for cp-exp-2 (see Assumptions)
};
// Subdomain-derived default: {site} defaults to "subdomain" in the US template https://{site}.chargify.com
options.Server.Production.Us.Site = config["Maxio:Subdomain"];            // "cp-exp-2"
// Verbatim override — when Maxio:BaseUrl is set, use it INSTEAD of Site:
options.Server.Production.Us.BaseUrl = config["Maxio:BaseUrl"];           // e.g. mock/dev host; only when set
var client = new MaxioAdvancedBillingClient(httpClient, options);         // only ctor: (HttpClient, MaxioAdvancedBillingClientOptions)
```

- `MaxioAdvancedBillingClientOptions` properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` (`sdk-map.md`).
- DI alternative exists: `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`, per `sdk-map.md`).
- All API groups are properties on the client: `client.ProductFamilies`, `client.Customers`, `client.Subscriptions`.

### Operation rows

| Endpoint step | Operation (controller property) | Signature (verbatim) | Request model | Response envelope → fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|---|
| List plans | `client.ProductFamilies.ListProductsForProductFamily` (`operations/ProductFamilies.md`) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable with no default → **must pass explicitly (pass `null`)** | none (query only). **Pass `productFamilyId: "handle:" + config["Maxio:ProductFamilyHandle"]`** — the param accepts "either the product family's id or its handle prefixed with `handle:`" (source param doc, `Api/ProductFamilies.cs`). No separate handle→ID resolve operation is needed. | `Task<IReadOnlyList<ProductResponse>>` — each `ProductResponse` has exactly one field `Product (product): Product` **required** (`records-3-Of-Su.md`). Read from `Product`: `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?` (cents), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404 — family not found] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 20; max 200) |
| Ensure customer (lookup) | `client.Customers.ReadCustomerByReference` (`operations/Customers.md`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` ← `reference`. Pass the eShopOnWeb username/identity. | none | `Task<CustomerResponse>` — `Customer (customer): Customer` **required** (`records-2-Cr-Ne.md`). Read: `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName` | **Case B** `SdkException<RawError>` — **"no customer yet" = catch and test `ex.Error.StatusCode == HttpStatusCode.NotFound`**; body via `ReadAsString()` | none |
| Ensure customer (create) | `client.Customers.CreateCustomer` (`operations/Customers.md`) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest` { `Customer (customer): CreateCustomer` **required** } (`records-1-Ac-Cr.md`). `CreateCustomer` **required: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`**; set `Reference (reference): string?` = eShopOnWeb username (unique per site; one customer per reference). Optional: `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `CcEmails`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | `Task<CustomerResponse>` → `.Customer` (required) → `Id`, `Reference` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ `CustomerErrorResponse1.Errors` is of type `Errors`, which models **only** `PerPage (per_page)` and `PricePoint (price_point)` (confirmed in source `Models/Errors.cs`) — real field-level messages (duplicate `reference`, bad email) are unmodeled and **dropped on deserialize**. Directive: on 422, read the typed accessor best-effort, then fall back to `TryGetRawError` → `ReadAsString()` for the actual messages. `UNVERIFIED` (live wire shape) | none |
| Subscribe | `client.Subscriptions.CreateSubscription` (`operations/Subscriptions.md`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest` { `Subscription (subscription): CreateSubscription` **required** } (`records-2-Cr-Ne.md`). `CreateSubscription` has **no required members** — set: `ProductHandle (product_handle): string?` = plan handle (`eshop-pro`/`basic-plan`); identify the customer with **either** `CustomerId (customer_id): int?` (from the ensure-customer step) **or** `CustomerReference (customer_reference): string?` (same value as customer `reference`) — both exist on the model; optional `Reference (reference): string?` for the subscription's own reference. Omit all payment fields (`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`) — see Assumptions | `Task<SubscriptionResponse>` — `Subscription (subscription): Subscription?` **nullable** (`records-4-Su-We.md`) — null-check before reading. Read: `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` (→ `Handle`, `Name`), `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1` { `Errors (errors): IReadOnlyList<string>` **required** } (`records-2-Cr-Ne.md`) | none |
| My subscriptions | `client.Customers.ListCustomerSubscriptions` (`operations/Customers.md`) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — **numeric customer id only** (no reference variant); obtain it from `ReadCustomerByReference` first (or a locally stored mapping) | none | `Task<IReadOnlyList<SubscriptionResponse>>` — each `.Subscription` (**nullable**) → `State`, `Product.Handle`/`Product.Name`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt` | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. 404 from the preceding lookup = user has no Maxio customer → return empty list, don't error | none |

### Enum values needed (`map/models/enums.md`)

| Enum (namespace `MaxioAdvancedBilling.Models.Enums`) | Kind | Members (C# name → wire value) |
|---|---|---|
| `SubscriptionState` | `StringEnum` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `StringEnum` | `Day (day)`, `Month (month)` |
| `CollectionMethod` (only if you set `PaymentCollectionMethod`) | `StringEnum` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |

Enums are **not** C# enums — compare with the static members (`SubscriptionState.Active`) or `Type.FromValue("active")`; never `.ToString()`-compare wire values blindly.

## 3. Trap notes

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has lifetime requirements a per-request `new HttpClient()` violates, and the SDK client wrapper's own lifetime differs from the pipeline's. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)` or the DI registration.
- ⚠ Step 1 (auth) — where in the options/DI callback credentials must be set, and why the API key belongs in configuration, not code. **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–4 (every call) — list/search operations carry nullable parameters with **no C# default** that mis-bind in positional calls; call with named arguments (and `ct:` for the token). **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–4 (models) — enums are `StringEnum<T>` (not C# enums), `required` members must be set in the object initializer, and **unmodeled JSON fields are silently dropped on deserialize** (this is exactly what bites the `CustomerErrorResponse1.Errors` 422 payload above). **MUST load `dotnet-models`**.
- ⚠ Step 5 (error boundary) — the Case A / Case B split is per-operation (see sheet); `TryGetRawError` is not a catch-all on typed errors; this SDK has **no** no-throw `…Result` variants — every call is throw-only, so wrap every call. **MUST load `dotnet-error-handling`**.
- ⚠ Step 5 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- ⚠ Step 5 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.
- ⚠ Step 3 (idempotency vs retries) — the SDK's retry policy interacts with non-idempotent writes: whether a failed `CreateSubscription` can be re-sent (by the SDK or by your own retry) without creating duplicates is a design constraint the signature hides. **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` or relying on default retries for `POST /subscriptions`.
- ⚠ Step 1 (timeouts/base URL) — what `RetryOptions.Timeout` actually bounds, and the exact precedence of `Server.Production.Us.BaseUrl` vs `.Site` when both are set. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Tests — the SDK's test seam is a specific constructor argument; stubbing at the wrong layer couples tests to SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — Step 1: client construction, `HttpClient` ownership, DI registration.
- `dotnet-authentication` — Step 1: Basic credentials wiring (`Username` = API key, `Password` = `"x"`).
- `dotnet-calling-endpoints` — Steps 2–4: named-argument calling convention, envelopes, async/`ct`.
- `dotnet-models` — Steps 2–4: `required` members, `StringEnum<T>`, dropped unmodeled fields.
- `dotnet-error-handling` — Step 5: Case A/B mechanics, `TryGet…` ladders, the two `JsonException` hazards above.
- `dotnet-configuration-resilience` — Steps 1 & 3: retry/timeout semantics, base-URL override, what retries mean for `POST` idempotency.
- `dotnet-testing` — tests for the integration layer.

## 5. Assumptions & Blockers

**Assumptions**
- `cp-exp-2` is US-hosted → `ServerEnvironment.Us`. If the sandbox is EU-hosted, switch to `ServerEnvironment.Eu` and set `options.Server.Production.Eu.Site` instead. `UNVERIFIED` (account hosting is not visible in the SDK).
- Payment-free signup works because the products are configured with no card required and no trial/setup fee; the SDK simply omits null payment fields. If the site/product still demands a payment method, `CreateSubscription` fails 422 with `ErrorListResponse1.Errors` listing the reason — surface those strings, don't swallow them. `UNVERIFIED` (server-side product config).
- Live 422 customer-error payloads vs the generated `Errors` model (which models only `per_page`/`price_point`): handled by the best-effort + raw-body fallback directive in the `CreateCustomer` row. `UNVERIFIED`.
- The eShopOnWeb user's username/identity is stable and unique — it is the Maxio customer `reference` and the idempotency key. `ListCustomerSubscriptions` needs the numeric id, so each `GET /api/my-subscriptions` does a lookup first unless eShopOnWeb persists the Maxio customer id locally (implementer's choice; not required).
- `Maxio:BaseUrl` override semantics ("used verbatim instead of deriving from subdomain") map to setting `options.Server.Production.Us.BaseUrl` and leaving `.Site` unset; exact precedence when both are set is covered by `dotnet-configuration-resilience`.
- JWT authentication on the endpoints is eShopOnWeb's existing infrastructure — this plan covers only the SDK side.

**Blockers** — none.
