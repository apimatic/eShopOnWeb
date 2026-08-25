# Maxio Advanced Billing integration plan — eShopOnWeb PublicApi

Billing system of record: Maxio Advanced Billing (sandbox site `cp-exp-1`). Three JWT-authenticated
endpoints on `src/PublicApi`: list plans, subscribe caller to a plan, list caller's subscriptions.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|------|---------------------|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` (central package management is ON — version goes in `Directory.Packages.props`, version-less `PackageReference` in `src/PublicApi/PublicApi.csproj`). **Not currently present** in `Directory.Packages.props` or any csproj (verified this session). | — |
| 2 | Bind `Maxio:*` config (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl`); construct + DI-register `MaxioAdvancedBillingClient`. | — |
| 3 | `GET /api/subscription-plans` — resolve family handle → id (cache it), list products in family, project to plan DTO (name, handle, price, interval). | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — find-or-create customer by reference (idempotent), then find-or-create subscription by deterministic reference, return plan/price/state/next billing. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve customer by reference (404 ⇒ empty list), list their subscriptions. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary: translate SDK exceptions to HTTP results (404/422/502). | — |
| 7 | Tests for the integration layer. | — |

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

### 2.1 Package & namespaces

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` version **`1.0.2`** (the map's stamped source tag; add `PackageVersion` to `Directory.Packages.props`, `PackageReference` to `src/PublicApi/PublicApi.csproj`) | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) — client, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` | `sdk-map.md` |
| Servers | `MaxioAdvancedBilling.Servers` — `ServerEnvironment`, `ProductionOptions` | `sdk-map.md` |
| Auth | `MaxioAdvancedBilling.Core.Authentication.Basic` — `BasicAuthCredentials` | `sdk-map.md` |
| Retry config | `MaxioAdvancedBilling.Core.Configuration` — `RetryOptions` | `sdk-map.md` |
| Exceptions | `MaxioAdvancedBilling.Core.Exceptions` — `SdkException<T>`; `MaxioAdvancedBilling.Core.ErrorResponse` — `RawError`, `ApiError` | `sdk-map.md` + `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/RawError.cs` |
| Records | `MaxioAdvancedBilling.Models` | `sdk-map.md` |
| Enums | `MaxioAdvancedBilling.Models.Enums` (`StringEnum<T>`, **not** C# enums) | `sdk-map.md`, `models/enums.md` |
| Typed errors | `MaxioAdvancedBilling.Errors` — e.g. `CreateSubscriptionError` | `sdk-map.md` + `Errors/CreateSubscriptionError.cs` |

### 2.2 Client construction (source-verified)

`MaxioAdvancedBillingClientOptions` auto-initializes the whole graph: `Server = new()`,
`Server.Production = new()`, `Server.Production.Us = new()` with `BaseUrl = "https://{site}.chargify.com"`
and `Site = "subdomain"`; `{site}` in `BaseUrl` is substituted from `Site` at request time — so a
`BaseUrl` override containing no `{site}` placeholder is used **verbatim**
(`MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`).
The only client constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    // Basic auth: Username = API key, Password = literal "x"
    BasicAuth = new BasicAuthCredentials { Username = config["Maxio:ApiKey"]!, Password = "x" },
    Environment = ServerEnvironment.Us, // default; EU only if account is EU-hosted
};

var baseUrl = config["Maxio:BaseUrl"];
if (!string.IsNullOrWhiteSpace(baseUrl))
    options.Server.Production.Us.BaseUrl = baseUrl;              // verbatim override
else
    options.Server.Production.Us.Site = config["Maxio:Subdomain"]; // fills {site} ⇒ https://cp-exp-1.chargify.com

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

A DI extension `AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`
exists (`ServiceCollectionExtensions.cs`, namespace `MaxioAdvancedBilling`); it registers the client as a
**singleton** over a plain `IHttpClientFactory.CreateClient()`. Whether that registration shape fits this
app's `HttpClient` lifetime policy is a trap note below — do not adopt it blind.

### 2.3 Operations

| Step | Call (named args; `null` = pass explicitly) | Returns | Error case | Pagination | Map page |
|---|---|---|---|---|---|
| 3a | `client.ProductFamilies.ListProductFamilies(dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct)` — all 5 filter params are nullable with **no C# default ⇒ must pass explicitly** | `IReadOnlyList<ProductFamilyResponse>`; envelope `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` → read `Id (id): int?`, `Handle (handle): string?` | **B** `SdkException<RawError>` | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 3b | `client.ProductFamilies.ListProductsForProductFamily(productFamilyId: familyId.ToString(), dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null, page: 1, perPage: 20, ct: ct)` — 8 middle params must pass explicitly; **`productFamilyId` is `string`** | `IReadOnlyList<ProductResponse>`; envelope `ProductResponse.Product (product): Product !req` | **A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` **[404]** (unknown family ⇒ plain-string body) · `TryGetRawError(out RawError)` | manual `page`/`perPage` (defaults 1/20) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 4a | `client.Customers.ReadCustomerByReference(reference: userRef, ct: ct)` (wire query param `reference`) | `CustomerResponse` → `Customer (customer): Customer !req` → `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName` | **B** `SdkException<RawError>` — "customer not found" = `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| 4b | `client.Customers.CreateCustomer(body: …, ct: ct)` — `body` must pass explicitly. `CreateCustomerRequest.Customer (customer): CreateCustomer !req`; `CreateCustomer` required: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; optional: `Reference (reference): string?` (**always set it** — see idempotency), `Organization`, `CcEmails`, address fields | `CustomerResponse` (as 4a) | **A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)`. Payload `CustomerErrorResponse1.Errors (errors): Errors?` — see hazard H2 before reading it | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 4c | `client.Subscriptions.FindSubscription(reference: subRef, ct: ct)` — `reference` nullable, must pass explicitly | `SubscriptionResponse` → `Subscription (subscription): Subscription?` (**nullable — null-check**) | **A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` **[404 = no such subscription]** · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md`, `records-4-Su-We.md` |
| 4d | `client.Subscriptions.CreateSubscription(body: …, ct: ct)` — `body` must pass explicitly. `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req`. On `CreateSubscription` set: `ProductHandle (product_handle): string?` (product by **handle**, e.g. `eshop-pro`), `CustomerId (customer_id): int?` (from step 4a/4b), `Reference (reference): string?` (deterministic — see idempotency), **`PaymentCollectionMethod (payment_collection_method): CollectionMethod?` = `CollectionMethod.Remittance`** — required in practice: when omitted the key is not sent at all (the property is `[JsonIgnore(WhenWritingNull)]` with no client-side default) and the server applies automatic collection, which 422s on a paid plan with no card on file (`{"errors":["No payment method was on file for the $299.00 balance"]}` — observed live on `cp-exp-1`). `Remittance` (invoice billing, customer remits later) is the valid value for the current Relationship Invoicing architecture per the property's doc comment; `Invoice` is the legacy Statements architecture value — which architecture the site runs is `UNVERIFIED`, so if the server rejects `remittance` itself, retry with `CollectionMethod.Invoice`. Optional alongside: `NetTerms (net_terms): string?` (invoice-billing due days, 0–180, server default null) and `ReceivesInvoiceEmails (receives_invoice_emails): string?` (server default true) — neither is contractually required | `SubscriptionResponse` → `Subscription?` → read `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (echoes the value sent). Same envelope either way — the collection method changes no response *shape*, only values (expect `State` = `active` on a no-trial product once collection isn't blocking; the returned state is live behavior — read it, don't hardcode it) | **A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]**; payload `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` — a flat list of messages · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md`, `Models/CreateSubscription.cs` |
| 5 | `client.Customers.ListCustomerSubscriptions(customerId: id, ct: ct)` | `IReadOnlyList<SubscriptionResponse>` (same envelope + fields as 4d) | **B** `SdkException<RawError>` | none | `operations/Customers.md`, `records-4-Su-We.md` |

**Family handle → id resolution (step 3a→3b):** `ReadProductFamily` is typed `ReadProductFamily(int id, …)`
— the API's `handle:my-family` form is **not reachable** through this SDK. Resolve instead:
`ListProductFamilies(…)` → match `ProductFamily.Handle == config["Maxio:ProductFamilyHandle"]`
(`eshop-subscribe`) → take `Id` → pass `Id.ToString()` to `ListProductsForProductFamily`. Cache the
resolved id (and the plan list) rather than re-resolving per request.

**"Next billing date":** the `Subscription` record has **no `next_billing_at` field** — expose
`NextAssessmentAt ?? CurrentPeriodEndsAt` as the next-billing date in the API DTOs (map-verified absence,
`records-3-Of-Su.md` `Subscription` row).

**Plan DTO fields (step 3):** from `Product` — `Name`, `Handle`, `PriceInCents (price_in_cents): long?`
(29900 ⇒ $299.00), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`,
`Description`, `ProductFamily`. Availability: `ArchivedAt (archived_at): DateTimeOffset?` — see §2.5.

### 2.4 Enum values needed (all `StringEnum<T>` in `MaxioAdvancedBilling.Models.Enums`; construct via static members, e.g. `SubscriptionState.Active`, or `SubscriptionState.FromValue("active")`)

| Enum | Members (C# name = wire value) | Map page |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `models/enums.md` |
| `CollectionMethod` (set `CollectionMethod.Remittance` on `CreateSubscription.PaymentCollectionMethod` for cardless signup — see 4d) | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `models/enums.md` |

### 2.5 Product status — no such enum exists

There is **no `ProductStatus` enum and no `status` field on `Product`** (verified against
`models/enums.md` and the `Product` row in `records-3-Of-Su.md`). A product's lifecycle is expressed by
`ArchivedAt (archived_at): DateTimeOffset?` — `null` ⇒ available. `ListProductsForProductFamily` takes
`includeArchived: false` to exclude archived products. Do not invent a status field.

### 2.6 Error-handling model (applies to every call)

Throw-only SDK — no `…Result` no-throw variants exist. Case A: `SdkException<{Op}Error>`,
`ex.Error.TryGet…(out …)` per the table above, plus inherited `TryGetRawError(out RawError)` for
unmodeled statuses. Case B: `SdkException<RawError>` with `ex.Error.StatusCode`,
`ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. (`sdk-map.md` error-handling section.)

Expected shapes for this integration: unknown product handle / bad request on create ⇒ **422** with
`ErrorListResponse1.Errors` (flat `IReadOnlyList<string>`); unknown customer reference ⇒ **404** Case B
(status check); unknown subscription reference ⇒ **404** via `TryGetNoContent`; unknown family id ⇒
**404** via `TryGetString`.

### 2.7 Idempotency (per the map)

- **Customer** — `CreateCustomer` enforces `reference` uniqueness ("you may only create one customer for
  a given reference value", `operations/Customers.md`). Pattern: `ReadCustomerByReference(userRef)` →
  404 ⇒ `CreateCustomer` with `Reference = userRef`; if a concurrent double-click races the create and
  returns 422, re-run `ReadCustomerByReference` and use the winner. Use the authenticated caller's stable
  eShopOnWeb user id as `userRef`.
- **Subscription** — `CreateSubscription` has **no idempotency-key parameter** (signature,
  `operations/Subscriptions.md`). Pattern: set `CreateSubscription.Reference` to a deterministic value
  (e.g. `{userRef}:{productHandle}`), pre-check with `FindSubscription(reference)` → 404
  (`TryGetNoContent`) ⇒ create; a found subscription is returned as-is. Belt-and-braces:
  `ListCustomerSubscriptions` + match `Product.Handle` also works with map-verified fields.
- **Retries** — whether the SDK's own retry layer can re-send a failed `POST` (and thus double-create) is
  hazard H4 below; the deterministic-reference pre-check is what makes a retried/duplicated subscribe
  converge.

## 3. Trap notes

- ⚠ **H1 · Step 2 (client registration)** — the SDK's built-in DI extension registers a singleton client
  over a plain `CreateClient()`; whether that, or manual registration, keeps the `HttpClient`/handler
  pipeline correctly long-lived is not visible from any signature. **MUST load `dotnet-client-initialization`**
  before wiring the client into eShopOnWeb's service collection.
- ⚠ **H2 · Steps 4b/6 (422 customer payload is lossy)** — `CustomerErrorResponse1.Errors` is typed
  `Errors?`, and the shared `Errors` record models only `PerPage (per_page)` and `PricePoint (price_point)`
  keys (`records-2-Cr-Ne.md`) — a suspicious shared model; unmodeled JSON keys are dropped on deserialize,
  so the real field errors may be unrecoverable from the typed payload. Directive: extract best-effort,
  fall back to a generic "customer rejected" message. What the live 422 body actually carries is
  `UNVERIFIED` (only live traffic could confirm).
- ⚠ **H3 · Steps 3–5 (calls)** — the nullable middle parameters on the list/search operations have no C#
  defaults and mis-bind in positional calls; every call above is written with named arguments for that
  reason. **MUST load `dotnet-calling-endpoints`** before writing the call sites.
- ⚠ **H4 · Steps 2/4d (resilience)** — whether a failed `CreateSubscription`/`CreateCustomer` POST can be
  re-sent by the SDK's retry layer, what `RetryOptions.Timeout` actually bounds, and what logging you must
  wire yourself are not visible from the options' names. **MUST load `dotnet-configuration-resilience`**
  before tuning or relying on retries/timeouts.
- ⚠ **H5 · Step 2 (auth)** — credentials must be set before the client is constructed and loaded from
  configuration (`Maxio:ApiKey`), never hardcoded; the Basic convention (key as username, `"x"` as
  password) is in §2.2. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ **H6 · Steps 3–5 (models)** — SDK enums are `StringEnum<T>` records, not C# enums (comparison and
  `ToString()` traps), and records are immutable with `required` members set only in object initializers.
  **MUST load `dotnet-models`** before constructing request payloads or mapping responses onto DTOs.
- ⚠ **H7 · Step 6 (error boundary)** — which exception types actually reach a `catch`, and why
  `TryGetRawError` is not a catch-all on typed errors, are not visible from the signatures. **MUST load
  `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ **H8 · Step 7 (tests)** — the test seam is the `HttpClient` constructor argument, not mocking SDK
  internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 2 (client construction + DI registration).
- `dotnet-authentication` — Step 2 (Basic credentials from config).
- `dotnet-calling-endpoints` — Steps 3–5 (named-argument calls, must-pass-explicitly nulls, `ct:`).
- `dotnet-models` — Steps 3–5 (request models, `StringEnum<T>`, envelope reads).
- `dotnet-error-handling` — Step 6 (the exception boundary).
- `dotnet-configuration-resilience` — Steps 2/4 (retry/timeout semantics, base-URL override behavior).
- `dotnet-testing` — Step 7 (faking the SDK at the `HttpClient` seam).

Two boundary hazards that need opposite handling, both reaching you as
`System.Text.Json.JsonException` — **MUST load `dotnet-error-handling`** before writing that boundary:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

## 5. Assumptions & Blockers

**Assumptions**
- The authenticated caller's stable eShopOnWeb user id (Identity `NameIdentifier` claim) is the Maxio
  customer `reference`; user name/email for `CreateCustomer` come from the Identity user record.
- Deterministic subscription `Reference` = `{userId}:{productHandle}`; the map does not state that
  subscription references are server-enforced unique (unlike customer references) — the pre-check
  converges double-clicks regardless.
- Signup sends `PaymentCollectionMethod = Remittance` (no card captured; billed by invoice). Which
  billing architecture site `cp-exp-1` runs (Relationship Invoicing vs legacy Statements) is
  `UNVERIFIED` — if `remittance` is rejected as a value, fall back to `Invoice`.
- Site `cp-exp-1` is US-hosted (`ServerEnvironment.Us`); `Maxio:BaseUrl` override covers any deviation.
- Plan catalog is small (2 plans), so a single `page: 1, perPage: 20` products call suffices; add a page
  loop only if the catalog grows.
- Package version pinned to `1.0.2`, the map's stamped source tag.

**Blockers** — none.
