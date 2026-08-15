# Maxio Advanced Billing — CONTRACT SHEET (Subscribe hero flow + reads)

Ground truth: bundled SDK map (`sdk-map.md` + `map/…`), SDK `v1.0.2` / commit `15db14b`.
Every fact below cites the map page it came from. This sheet is the sole SDK reference for the
integration; load the `dotnet-*` companion skills listed in REQUIRED READING before coding.

## 1. Scope & sequence

| # | Step | Operation(s) |
|---|---|---|
| 1 | Register + configure client (subdomain-derived base URL, optional explicit BaseUrl override, Basic auth) | client construction (`sdk-map.md`) |
| 2 | List plans in a product family (by handle) | `client.ProductFamilies.ListProductsForProductFamily` (`operations/ProductFamilies.md`) |
| 3 | Find-or-create customer idempotently | `client.Customers.ReadCustomerByReference` / `ListCustomers` (search) / `CreateCustomer` (`operations/Customers.md`) |
| 4 | Create subscription (no card) | `client.Subscriptions.CreateSubscription` (`operations/Subscriptions.md`) |
| 5 | List a customer's subscriptions | `client.Customers.ListCustomerSubscriptions` (`operations/Customers.md`) |
| 6 | Error boundary | all of the above (see error rows) |

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

### 2a. Package, client, auth, server (`sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (install by this id) |
| Root namespace (`using`) | `MaxioAdvancedBilling` — differs from the package id |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` |
| Only constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — `httpClient` is `System.Net.Http.HttpClient` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| API groups | properties on client: `client.Customers`, `client.Subscriptions`, `client.Products`, `client.ProductFamilies` (controller types in `MaxioAdvancedBilling.Api`, but reached via the property — no `using` needed to call them) |

**Options members** (`MaxioAdvancedBillingClientOptions.cs`): `Environment: ServerEnvironment` ·
`Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?`.

**Auth — HTTP Basic only.**
`options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<api_key>", Password = "x" }`.
Convention: **`Username` = your Maxio/Chargify API key; `Password` = the literal string `"x"`.**
Types: `BasicAuthCredentials` → namespace `MaxioAdvancedBilling.Core.Authentication.Basic`
(source `Core/Authentication/Basic/BasicAuthCredentials.cs`).

**Environment / server selection** (`sdk-map.md` Servers & auth; `Servers/ServerEnvironment.cs`):
- `options.Environment` is `MaxioAdvancedBilling.Servers.ServerEnvironment`. Members:
  `ServerEnvironment.Us` (default, wire `US`) → `https://{site}.chargify.com`;
  `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com`.
- **Derive base URL from subdomain**: set `options.Server.Production.Us.Site = "<subdomain>"`
  (e.g. `"eshop"`). `{site}` in the template defaults to the configured `subdomain`. For EU set
  `options.Server.Production.Eu.Site`. Type behind `options.Server` is `ServerOptions` (source
  `ServerOptions.cs` at repo root ⇒ namespace `MaxioAdvancedBilling`); `.Production` is a
  `ProductionOptions` (source `Servers/ProductionOptions.cs` ⇒ `MaxioAdvancedBilling.Servers`)
  with `.Us` / `.Eu` sub-options each carrying `.Site` and `.BaseUrl`.
- **Explicit BaseUrl override, used verbatim when set**: `options.Server.Production.Us.BaseUrl =
  "https://eshop.chargify.com"` (or a mock/dev host like `"http://localhost:8080"`). When set it
  is used as-is; leave it unset to let the `{site}` template + `Site` derive the URL.
- Sources: `Server.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/EbbOptions.cs`.
  (Ebb group is only for `SubscriptionComponents` event ingest — not in this scope.)

`RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`, source
`Core/Configuration/RetryOptions.cs`) — all members `required`; start from `RetryOptions.Default()`.
See trap note + `dotnet-configuration-resilience` before tuning.

### 2b. Operations

| Op | Signature (params in order) | Request model + fields used | Response envelope → inner reads | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| **List plans in a family** `client.ProductFamilies.ListProductsForProductFamily` (`operations/ProductFamilies.md`) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass all 8 optional-but-no-default params explicitly (`null` to skip); use named args. **`productFamilyId` accepts `handle:my-family` format** (per `ReadProductFamily` note; a family may be addressed by id or `handle:eshop-subscribe`). | none (GET) | Returns `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each `ProductResponse.Product` (`!req`) is a `Product` — read fields below. | **Case A (typed)** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 20) |
| **Find customer by reference** `client.Customers.ReadCustomerByReference` (`operations/Customers.md`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — exact single match, query `reference ← reference` | none (GET) | `CustomerResponse.Customer` (`!req`) → `Customer` | **Case B** `SdkException<RawError>` — `StatusCode` (404 when no match) · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | none |
| **Search customers (e.g. by email)** `client.Customers.ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — put the email in `q` (fuzzy search, `q ← q`); pass the 7 leading params explicitly (`null` to skip). | none (GET) | `IReadOnlyList<CustomerResponse>` → each `.Customer` | **Case B** `SdkException<RawError>` — `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` | manual `page`+`perPage` (default 50) |
| **Create customer** `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable/no default, pass explicitly | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` → `CreateCustomer` fields below | `CustomerResponse.Customer` (`!req`) → `Customer` (read `Id`, `Reference`) | **Case A (typed)** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. **See UNVERIFIED note — the typed 422 payload is nearly useless; prefer `TryGetRawError`.** | none |
| **Create subscription** `client.Subscriptions.CreateSubscription` (`operations/Subscriptions.md`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — pass `body` explicitly | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` → `CreateSubscription` fields below | `SubscriptionResponse.Subscription` — **NULLABLE (`Subscription?`, NOT `!req`)**, null-check before use → `Subscription` fields below | **Case A (typed)** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| **List customer's subscriptions** `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — takes the **numeric Maxio customer id**, not reference | none (GET) | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` (nullable) | **Case B** `SdkException<RawError>` — `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` | none |

Adjacent reads if you prefer id/handle lookups (all `operations/Products.md`, all **Case B** `SdkException<RawError>`):
`client.Products.ReadProductByHandle(string apiHandle, ct)` → `ProductResponse`;
`client.Products.ReadProduct(int productId, ct)` → `ProductResponse`;
`client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, ct)` → `SubscriptionResponse` (pass `include` explicitly).

### 2c. Request/response record fields (`MaxioAdvancedBilling.Models`; wire name in parens)

**`CreateCustomer`** (`records-1-Ac-Cr.md`) — **`FirstName`, `LastName`, `Email` are `!req`** (must set):
`FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` ·
`Reference (reference): string?` · `Organization (organization): string?` · `CcEmails (cc_emails): string?` ·
plus optional address/locale/tax fields. Map your eShop user id/username into `Reference` for idempotent lookup.

**`Customer`** (response, `records-2-Cr-Ne.md`) — read: `Id (id): int?` (numeric Maxio id) ·
`Reference (reference): string?` · `Email (email): string?` · `FirstName (first_name): string?` ·
`LastName (last_name): string?` · `Organization (organization): string?` · `CreatedAt (created_at): DateTimeOffset?`.

**`CreateSubscription`** (request, `records-2-Cr-Ne.md`) — all fields optional (`?`); set the ones for a no-card signup:
- Product: `ProductHandle (product_handle): string?` (use the plan handle) **or** `ProductId (product_id): int?`;
  optional price point `ProductPricePointHandle (product_price_point_handle): string?` / `ProductPricePointId (product_price_point_id): int?`.
- Customer link (choose one): `CustomerReference (customer_reference): string?` **or** `CustomerId (customer_id): int?`;
  to create-with-subscription instead, `CustomerAttributes (customer_attributes): CustomerAttributes?`.
- No-card / payment collection: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`
  — set to `CollectionMethod.Invoice` or `CollectionMethod.Remittance` for a no-card/invoiced signup
  (see enum table + UNVERIFIED note; there is **no** boolean "no payment" flag).
- Do **not** set `CreditCardAttributes` / `PaymentProfileAttributes` / `PaymentProfileId` for a no-card flow.
- Optional timing: `NextBillingAt`, `InitialBillingAt`, `CouponCode`/`CouponCodes`, `NetTerms (net_terms): string?`.

**`Subscription`** (response, `records-3-Of-Su.md`) — read for the Subscribe result / "my subscriptions":
`Id (id): int?` · `State (state): SubscriptionState?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ·
`NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?` ·
`CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` · `ProductPriceInCents (product_price_in_cents): long?` ·
`PaymentCollectionMethod (payment_collection_method): CollectionMethod?` · `Reference (reference): string?` ·
`Product (product): Product?` (nested plan info) · `Customer (customer): Customer?` · `ProductPricePointId (product_price_point_id): int?`.
**There is NO `next_billing_at` field on this model** — the server does not return `next_billing_at`; use
`CurrentPeriodEndsAt` (and `NextAssessmentAt`) as the "next billing/assessment" date (`operations/Subscriptions.md` UpdateSubscription note).

**`Product`** (response, `records-3-Of-Su.md`) — plan display fields:
`Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` · `Description (description): string?` ·
`PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` ·
`TrialPriceInCents (trial_price_in_cents): long?` · `TrialInterval (trial_interval): int?` · `TrialIntervalUnit (trial_interval_unit): IntervalUnit?` ·
`InitialChargeInCents (initial_charge_in_cents): long?` · `RequireCreditCard (require_credit_card): bool?` ·
`ProductFamily (product_family): ProductFamily?` · `ArchivedAt (archived_at): DateTimeOffset?` · `ProductPricePointId (product_price_point_id): int?`.

**Envelope wrappers** (single-field, `!req` unless noted):
`ProductResponse { Product (product): Product !req }` · `CustomerResponse { Customer (customer): Customer !req }` ·
`SubscriptionResponse { Subscription (subscription): Subscription? }` **(inner is nullable)** ·
`CreateCustomerRequest { Customer (customer): CreateCustomer !req }` ·
`CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` ·
`ProductFamilyResponse { ProductFamily (product_family): ProductFamily? }`.

### 2d. Error payload shapes (typed errors, namespace `MaxioAdvancedBilling.Models`)

- `ErrorListResponse1` (`records-2-Cr-Ne.md`) — **usable**: `Errors (errors): IReadOnlyList<string> !req` (flat message list).
  This is the CreateSubscription 422 payload.
- `CustomerErrorResponse1` (`records-2-Cr-Ne.md`) — `Errors (errors): Errors?` where `Errors` (`Models/Errors.cs`)
  models **only** `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`.
  This is the CreateCustomer 422 payload — see UNVERIFIED note below.
- `RawError` (`Core/ErrorResponse/RawError.cs`, namespace `MaxioAdvancedBilling.Core.ErrorResponse`):
  `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`.
- `SdkException<T>` (`Core/Exceptions/SdkException.cs`, namespace `MaxioAdvancedBilling.Core.Exceptions`) exposes `.Error` of type `T`.
- `ApiError` base (all typed errors) exposes `TryGetRawError(out RawError)` as the fallback accessor.

### 2e. Enums (namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>`, NOT C# enums — write the member name, e.g. `CollectionMethod.Invoice`)

| Enum | Members (C# name = wire) |
|---|---|
| `CollectionMethod` (`enums.md`) | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` (`enums.md`) | `Pending`, `FailedToCreate (failed_to_create)`, `Trialing`, `Assessing`, `Active`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` (`enums.md`) | `Day (day)`, `Month (month)` |
| `ExpirationIntervalUnit` (`enums.md`) | `Day (day)`, `Month (month)`, `Never (never)` |

### 2f. Capabilities the SDK/map does NOT expose (flagged)

- **No exact find-customer-by-email.** Only `ReadCustomerByReference` (exact, by your `reference`) and
  `ListCustomers(q: email …)` (fuzzy search returning a list) exist — there is no single-exact-match-by-email
  endpoint. For idempotency, key on `reference` (map your eShop user id/username into `CreateCustomer.Reference`),
  and treat email search as best-effort disambiguation. (`operations/Customers.md`)
- **No dedicated "find or create" op.** Compose it yourself: `ReadCustomerByReference` → on 404 (`RawError.StatusCode`)
  call `CreateCustomer`. CreateCustomer returns **422** (typed `CustomerErrorResponse1`) if the `reference` is a
  duplicate — treat that as "already exists, re-read".
- **No no-throw / `…Result` variants** anywhere — every op throws; wrap every call.
- **No "no payment" boolean.** Card-free signup is expressed only via `PaymentCollectionMethod`
  (`Invoice`/`Remittance`) and by omitting all payment-profile fields.

---

## 3. Trap notes (load the named skill before that step — do not treat these as resolved)

⚠ **Step 1 (client & DI)** — the `HttpClient`/handler pipeline lifetime and whether the SDK client wrapper is
transient or singleton is not visible in the constructor signature. **MUST load `dotnet-client-initialization`**
before wiring `new MaxioAdvancedBillingClient(...)` or `AddMaxioAdvancedBillingClient`.

⚠ **Step 1 (auth)** — where/when credentials must be set relative to client construction, and loading the key
from configuration, are conventions the signature hides. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ **Steps 2–5 (calling ops)** — the list/search ops (`ListProductsForProductFamily`, `ListCustomers`) have many
optional params with **no C# default**, so a positional call mis-binds; how to bind them safely (named args) and how
async/cancellation flow through is not shown by the signature. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Steps 3–4 (models)** — enums are `StringEnum<T>` (not C# enums), `required` members must be set in the object
initializer, and unmodeled JSON keys are dropped on deserialize — none of this is visible in the field list. **MUST
load `dotnet-models`** before building `CreateCustomer`/`CreateSubscription` payloads or mapping responses to your domain types.

⚠ **Step 1 (retries/timeouts)** — `RetryOptions.HttpMethodsToRetry`, `Timeout`, and `MaxRetries` do **not** mean what
their names suggest for whether a failed `POST` (create-subscription / create-customer) can be re-sent, and what
`Timeout` actually bounds. **MUST load `dotnet-configuration-resilience`** before tuning the client — critical because
CreateSubscription/CreateCustomer are non-idempotent writes.

⚠ **Step 6 (error boundary)** — which exception types actually reach your catch blocks, and why a `TryGetRawError`
fallback and status handling must be structured a particular way, is not derivable from the accessor list. **MUST load
`dotnet-error-handling`** before writing the boundary (see REQUIRED READING).

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying Basic credentials, when/where to set them |
| `dotnet-calling-endpoints` | Steps 2–5 — calling ops, named args for no-default optionals, async/cancellation |
| `dotnet-models` | Steps 3–4 — building request models, required members, `StringEnum<T>`, dropped JSON keys |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts/base-URL selection; non-idempotent write re-send semantics |
| `dotnet-error-handling` | Step 6 — the error/exception boundary (always required) |

**Two mandatory `System.Text.Json.JsonException` hazard rows** — it reaches the boundary from two directions that
need opposite handling:
- a drifted or malformed **2xx** body (e.g. a missing `required` member such as `ProductResponse.Product` or
  `CustomerResponse.Customer`) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException`
  *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP
  status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic
  rejection (e.g. a 422) as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Assumption:** the eShopOnWeb user is mapped to a Maxio customer via `reference` (eShop user id or username);
  the idempotent find step is `ReadCustomerByReference`. If you intend to key on email instead, note there is no
  exact-email endpoint (see 2f) and the plan step must change to a `q`-search + client-side match.
- **Assumption:** "Subscribe" is a card-free / invoiced signup, so no payment profile is attached and
  `PaymentCollectionMethod` carries the card-free intent. If cards are later required, the payment-profile fields
  on `CreateSubscription` (`CreditCardAttributes` / `PaymentProfileAttributes` / `PaymentProfileId`) and the
  `PaymentProfiles` controller come into scope — not covered here.
- **Assumption:** US hosting (`ServerEnvironment.Us`) unless the account is EU-provisioned.
- **UNVERIFIED (live traffic only) — CreateCustomer 422 payload:** the generated typed accessor
  `TryGetCustomerErrorResponse1` yields a `CustomerErrorResponse1.Errors` object that models **only** `per_page`
  and `price_point` — fields that have nothing to do with a real customer-validation 422 (duplicate reference,
  bad email/country/state). Whether the live 422 body ever populates those two keys cannot be confirmed from the
  map/source; on drift the real error keys are dropped on deserialize. **Directive:** in the CreateCustomer catch,
  do **not** rely on `CustomerErrorResponse1.Errors` for the message — call `TryGetRawError(out var raw)` and use
  `raw.StatusCode` + `raw.ReadAsString()` (best-effort extract) as the surfaced message; only fall back to the typed
  `Errors` fields if present and non-null. CreateSubscription's `ErrorListResponse1.Errors` (a flat
  `IReadOnlyList<string>`) is trustworthy by contrast and can be read directly.
- **UNVERIFIED (live traffic only) — no-card `PaymentCollectionMethod` value:** whether the live product's options
  accept `Invoice` vs `Remittance` for a card-free signup depends on the site's billing architecture (legacy
  Statements = `invoice`/`automatic`; Relationship Invoicing = `remittance`/`automatic`/`prepaid`, per the
  `CollectionMethod` enum doc). **Directive:** make the collection method configurable (default `Invoice`), and on a
  422 that names payment/collection, surface the raw message rather than assuming a fixed value.
- **No blockers** to planning — all six areas are fully grounded in the map.
