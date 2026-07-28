# Maxio Advanced Billing — Integration Contract Sheet (eShopOnWeb, .NET 8)

Grounded entirely against the bundled SDK map (source tag `v1.0.2`, commit `15db14b`). Every row
cites its map page. No capability in the brief is missing from the map.

## SDK identity (from `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` |
| Root namespace (`using`) | `MaxioAdvancedBilling` (differs from the package id) |
| Client class | `MaxioAdvancedBillingClient` (namespace `MaxioAdvancedBilling`) |
| Options class | `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`) |
| Target framework | `netstandard2.0` (fine for .NET 8) |
| Package **version** | NOT carried by the map — see Assumptions & Blockers. The map stamps only the **source** tag `v1.0.2` / commit `15db14b`. |

## Scope & sequence

1. **Client & DI setup** — register `MaxioAdvancedBillingClient` (ops: none; construction + auth + server/base-URL).
2. **Find-or-create customer (idempotent)** — `client.Customers.ReadCustomerByReference` then, on 404, `client.Customers.CreateCustomer`.
3. **Resolve product family by handle → id** — `client.ProductFamilies.ListProductFamilies` (match `Handle` client-side).
4. **List plans (products) in that family** — `client.ProductFamilies.ListProductsForProductFamily`.
5. **Create subscription (no payment method)** — `client.Subscriptions.CreateSubscription`.
6. **List a customer's subscriptions** — `client.Customers.ListCustomerSubscriptions`.
7. **Read subscription fields for confirmation** — fields off the `Subscription` model.
8. **Error boundary** — around every call (Case A typed vs Case B raw; plus `JsonException` — see REQUIRED READING).

---

## CONTRACT SHEET

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

### Namespaces (`using` directives) — one per kind of type touched

| Type kind | Namespace | Source basis |
|---|---|---|
| Client, options, `client.*` accessors | `MaxioAdvancedBilling` | root (`sdk-map.md`) |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `Core/Authentication/Basic/…` (`sdk-map.md`) |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Servers/ServerEnvironment.cs` (`sdk-map.md`) |
| `RetryOptions`, `RetryAttempt` | `MaxioAdvancedBilling.Core.Configuration` | `Core/Configuration/…` (`sdk-map.md`) |
| Records (requests/responses/models below) | `MaxioAdvancedBilling.Models` | `Models/…` (records pages) |
| Enums (`SubscriptionState`, `IntervalUnit`, `CollectionMethod`) | `MaxioAdvancedBilling.Models.Enums` | `Models/Enums/…` (`enums.md`) |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, …) | `MaxioAdvancedBilling.Errors` | `Errors/…` (`sdk-map.md`) |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` | `Core/Exceptions/SdkException.cs` (`sdk-map.md`) |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `Core/ErrorResponse/RawError.cs` (`sdk-map.md`) |

### Client construction & auth (`sdk-map.md` — "Getting a client" / "Servers & auth")

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    // Basic auth: Username = API key, Password = the literal "x"
    BasicAuth   = new BasicAuthCredentials { Username = maxioApiKey, Password = "x" },
    Environment = ServerEnvironment.Us,          // US (default) or ServerEnvironment.Eu
};
options.Server.Production.Us.Site = subdomain;   // {site} in https://{site}.chargify.com
// Optional explicit base-URL override (e.g. sandbox/mock host):
// options.Server.Production.Us.BaseUrl = baseUrlOverride;   // e.g. "http://localhost:8080"
var client = new MaxioAdvancedBillingClient(httpClient, options); // httpClient: System.Net.Http.HttpClient
```

- The **only** constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- DI form: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = new BasicAuthCredentials{ Username = key, Password = "x" }; o.Environment = ServerEnvironment.Us; o.Server.Production.Us.Site = subdomain; });` (source `ServiceCollectionExtensions.cs`, `sdk-map.md`).
- `MaxioAdvancedBillingClientOptions` properties (`sdk-map.md`): `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?`.
- Server override points (`sdk-map.md` "Servers & auth"): `options.Server.Production.Us.BaseUrl` / `.Us.Site` (and `.Eu.*`). US template `https://{site}.chargify.com`; EU template `https://{site}.ebilling.maxio.com`. `{site}` defaults to the subdomain.
- **`MAXIO_ENVIRONMENT`** is a Maxio "sandbox/env name" in your app — the SDK has no such concept. It maps to `ServerEnvironment.Us`/`.Eu` **plus** the `Site` (subdomain) and/or an explicit `BaseUrl`. Do not try to pass it to the SDK as an environment token.

### Operations

| # | Op (map page) | Signature (verbatim, params in order) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|---|
| 2a | `client.Customers.ReadCustomerByReference` (`operations/Customers.md`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference ← reference` (the stable external key) | `CustomerResponse` → `Customer (customer): Customer !req` | **Case B** `SdkException<RawError>`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. **404 = not found → create.** | none |
| 2b | `client.Customers.CreateCustomer` (`operations/Customers.md`) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **pass explicitly** | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`. `CreateCustomer` **required**: `FirstName (first_name) !req`, `LastName (last_name) !req`, `Email (email) !req`. Optional incl. `Reference (reference): string?`, `Organization`, `CcEmails`, address fields, `Locale`, `TaxExempt`, etc. | `CustomerResponse` → `Customer` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| 3 | `client.ProductFamilies.ListProductFamilies` (`operations/ProductFamilies.md`) | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable, **pass explicitly** (`null` to skip) | — | `IReadOnlyList<ProductFamilyResponse>`; each → `ProductFamily (product_family): ProductFamily?`. Match `ProductFamily.Handle (handle): string?` to get `ProductFamily.Id (id): int?`. | **Case B** `SdkException<RawError>` | none |
| 4 | `client.ProductFamilies.ListProductsForProductFamily` (`operations/ProductFamilies.md`) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 middle params nullable, **pass explicitly**; `page`/`perPage` defaulted | path `productFamilyId` = the **numeric family id as string** (from step 3) | `IReadOnlyList<ProductResponse>`; each → `Product (product): Product !req`. | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 20) |
| 5 | `client.Subscriptions.CreateSubscription` (`operations/Subscriptions.md`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **pass explicitly** | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`. See CreateSubscription field selection below. | `SubscriptionResponse` → `Subscription (subscription): Subscription?` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| 6 | `client.Customers.ListCustomerSubscriptions` (`operations/Customers.md`) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `customer_id ← customerId` (the numeric customer id) | `IReadOnlyList<SubscriptionResponse>`; each → `Subscription?` | **Case B** `SdkException<RawError>` | none |

Adjacent alternatives (if useful): `client.Customers.ReadCustomer(int id)` and `client.Products.ReadProductByHandle(string apiHandle)` / `client.Products.ListProducts(...)` (site-wide, not family-scoped) — all Case B (`operations/Customers.md`, `operations/Products.md`).

### `CreateSubscription` field selection — no payment method (`records-2-Cr-Ne.md`, `Models/CreateSubscription.cs`)

All fields are optional (`?`); this SDK model marks **none** `required` at the C# level — the server enforces the real rules. Field = `CSharpName (wire_name): Type`.

**Set exactly one of each pair, leave the other null:**
- Customer by id: `CustomerId (customer_id): int?` — OR — customer by reference: `CustomerReference (customer_reference): string?`
- Product by handle: `ProductHandle (product_handle): string?` — OR — product by id: `ProductId (product_id): int?`

**Leave null to skip payment capture (no card / no 3-DS):**
`PaymentProfileId (payment_profile_id): int?`, `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?`, `BankAccountAttributes (bank_account_attributes): BankAccountAttributes?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`.

**Optionally relevant:** `ProductPricePointHandle (product_price_point_handle): string?` / `ProductPricePointId (product_price_point_id): int?` (else product default price point), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum — `Invoice`/`Remittance`/`Automatic`/`Prepaid`), `CouponCode (coupon_code): string?`, `Reference (reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?` (only to create a NEW customer inline — you are using an existing customer, so leave null).

> Whether a card is actually **required** is a per-product server setting (`Product.RequireCreditCard (require_credit_card): bool?` / `RequestCreditCard (request_credit_card): bool?`, `records-3-Of-Su.md`). A no-payment `CreateSubscription` succeeds only if the target product has `require_credit_card = false`. If the product requires a card, the SDK returns a **422** (see the 3-DS note in the op's map notes). This is a product-config fact, not something the request body can override.

### Response models to read

**`Customer` (`records-2-Cr-Ne.md`, `Models/Customer.cs`)** — read after find-or-create:
`Id (id): int?` (the numeric Maxio id), `Reference (reference): string?`, `FirstName`, `LastName`, `Email`, `Organization`, `CreatedAt (created_at): DateTimeOffset?`, … (all optional/nullable).

**`Product` (`records-3-Of-Su.md`, `Models/Product.cs`)** — plan/price fields:
`Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointHandle (product_price_point_handle): string?`, `DefaultProductPricePointId (default_product_price_point_id): int?`.
> There is **no** `price` field and **no** `product_price_in_cents` on `Product` — the price is `price_in_cents` (a `long?`, cents). Do not look for `Product.Price`.

**`Subscription` (`records-3-Of-Su.md`, `Models/Subscription.cs`)** — confirmation fields:
`Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` (read plan back via `Product.Handle`/`.Name`/`.PriceInCents`), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `TrialStartedAt`/`TrialEndedAt: DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `Customer (customer): Customer?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `Reference (reference): string?`.
> The `Subscription` response has **no `next_billing_at` and no `next_billing_date`** property. Use `CurrentPeriodEndsAt` (`current_period_ends_at`) for "next billing date"; `NextAssessmentAt` (`next_assessment_at`) is the next assessment. This is confirmed by the `UpdateSubscription` map note: the server does not return `next_billing_at`; verify via `current_period_ends_at`.

### Enum value tables (`enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>`, **not** C# enums — write the C# member (`SubscriptionState.Active`) or `SubscriptionState.FromValue("active")`. Members below as `CSharpMember (wire_value)`.

**`SubscriptionState`** (`Models/Enums/SubscriptionState.cs`):
`Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.

**`IntervalUnit`** (`Models/Enums/IntervalUnit.cs`): `Day (day)`, `Month (month)`.

**`CollectionMethod`** (`Models/Enums/CollectionMethod.cs`): `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`.

**`ExpirationIntervalUnit`** (`Models/Enums/ExpirationIntervalUnit.cs`): `Day (day)`, `Month (month)`, `Never (never)`.

### Error handling — types & accessors (`sdk-map.md` "Error-handling model")

Every operation is **throw-based**; there are **no** no-throw `…Result` variants. On an error status the SDK throws `SdkException<TError>` (`MaxioAdvancedBilling.Core.Exceptions`) exposing `.Error` of type `TError`.

- **Case B (raw)** — `TError` is `RawError` (`MaxioAdvancedBilling.Core.ErrorResponse`, `Core/ErrorResponse/RawError.cs`). Read: `.Error.StatusCode` (`HttpStatusCode`), `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()`. Used by the read/list ops here: `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductFamilies`, `ReadCustomer`, `ReadProduct`, `ListSubscriptions`.
- **Case A (typed)** — `TError : ApiError` with status-specific `TryGet…(out …)` accessors returning `true` when that shape is present, plus inherited `TryGetRawError(out RawError)` fallback:
  - `CreateCustomer` → `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [**422**]; else `TryGetRawError(out RawError)`.
  - `CreateSubscription` → `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [**422**]; else `TryGetRawError(out RawError)`.
  - `ListProductsForProductFamily` → `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [**404**]; else `TryGetRawError(out RawError)`.
- **422 validation payloads:**
  - `ErrorListResponse1` (`records-2-Cr-Ne.md`, `Models/ErrorListResponse1.cs`): `Errors (errors): IReadOnlyList<string> !req` — a flat list of message strings. (Used by CreateSubscription, CreateProduct, UpdateSubscription, etc.)
  - `CustomerErrorResponse1` (`records-2-Cr-Ne.md`, `Models/CustomerErrorResponse1.cs`): `Errors (errors): Errors?` where `Errors` (`Models/Errors.cs`) = `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?`. **`UNVERIFIED`** — this generated `Errors` shape (only `per_page`/`price_point` keys) does not look like a customer-create validation body; the live 422 for `CreateCustomer` may carry different/absent keys. **Defensive-coding directive:** when handling `CreateCustomerError`, try `TryGetCustomerErrorResponse1` and best-effort read `.Errors?.PerPage`/`.PricePoint`; if null/empty, **fall back to `TryGetRawError` and surface `RawError.ReadAsString()`** rather than assuming those two lists carry the message. Never treat this typed shape as the guaranteed message source.
- **401 / wrong host / timeouts** are Case B `RawError` (`.StatusCode`) or transport exceptions — for 401 recheck auth (Username = API key, Password = literal `"x"`) and the `Site`/`BaseUrl`.

---

## Trap notes

⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline lifetime is **not** shown by the constructor signature; getting it wrong (rebuilding per request vs long-lived via `IHttpClientFactory`, and which of client/handler is transient) is the classic ASP.NET Core socket/DI hazard. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ Step 1 (auth) — the exact credential property names and *when* credentials must be set relative to client construction are not derivable from the signature; load the key from config, never hardcode. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ Steps 3–6 (calls) — every list/search op here has many optional params with **no C# default** (e.g. `ListProductsForProductFamily`'s 8 middle params, `ListProductFamilies`'s 5) that mis-bind in a positional call. Whether/how to call with named arguments and pass `null` to skip is a call-convention hazard. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Step 5 (request body) — `CollectionMethod`/`IntervalUnit`/`SubscriptionState` are `StringEnum<T>`, not C# enums, and unmodeled JSON is dropped on (de)serialize; how to build enum values and read them back is a modelling hazard. **MUST load `dotnet-models`** before building the `CreateSubscription` body or mapping responses.

⚠ Step 1 (resilience) — the SDK's `RetryOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs/statuses retry, whether a failed `POST` (create-subscription/create-customer) can be re-sent, and what `Timeout` actually bounds are all hidden by the option names. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts/base-URL.

⚠ Step 8 (error boundary) — which exception types actually reach your catch blocks (Case A vs Case B, and `JsonException`), and why an SDK-exception-only ladder is silently wrong, are not visible in any signature. **MUST load `dotnet-error-handling`** before writing the boundary (see REQUIRED READING for the two mandatory `JsonException` hazards).

---

## REQUIRED READING (load BEFORE implementation starts)

The sheet deliberately does **not** carry these skills' contents (defaults, worked examples, the parts you must still wire yourself). Load each before the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic-auth credential wiring, when to set credentials |
| `dotnet-calling-endpoints` | Steps 2–6 — named-argument calling, request/response envelopes, async/cancellation |
| `dotnet-models` | Step 5 — building request payloads, `StringEnum`, nullability, dropped-field trap |
| `dotnet-configuration-resilience` | Step 1 — retries/backoff, what `Timeout` bounds, base-URL/server selection, pagination |
| `dotnet-error-handling` | Step 8 — Case A/B mechanics, reading status/body safely, the catch-ladder traps |
| `dotnet-testing` | Tests — the `HttpClient` test seam, error/edge coverage |

Two mandatory `JsonException` hazards for the error boundary (`System.Text.Json.JsonException` reaches the boundary from two directions, needing opposite handling):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **NuGet package version is not exposed by the map (`UNVERIFIED`).** The map stamps only the SDK **source** tag `v1.0.2` (commit `15db14b`); the published `AsadAli.AdvancedBilling.Sdk` NuGet version number is not carried by the map or resolvable from source. Reference the package version that corresponds to source tag `v1.0.2`; confirm the exact `<PackageReference Version="…">` against nuget.org before pinning. This is the only fact in the brief the map cannot settle.
- **`ListProductsForProductFamily` accepts only what its `string productFamilyId` path is given.** The reliable path is to resolve the family handle → numeric id via `ListProductFamilies` (match `ProductFamily.Handle`) and pass `id.ToString()`. Note: `ReadProductFamily` takes `int id` (its API-doc note mentions a `handle:my-family` form, but the C# signature is `int`, so you cannot pass a handle there). Whether the list endpoint's `productFamilyId` string also accepts the `handle:{handle}` form is server behavior only live traffic can confirm (`UNVERIFIED`) — do not rely on it; resolve to the numeric id first.
- **Assumed intent:** you want family-scoped product listing (`ListProductsForProductFamily`), not the site-wide `client.Products.ListProducts`. If you actually want all site products, that op exists and is Case B.
- **Assumed:** `MAXIO_ENVIRONMENT` is your app's sandbox/site descriptor, mapped to `ServerEnvironment` + `Site` (+ optional `BaseUrl`), since the SDK has no free-form environment token.
- No other blockers — all seven requested capability areas are present in the map.
