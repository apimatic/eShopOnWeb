# maxio-plan.md — Recurring subscriptions for eShopOnWeb via Maxio Advanced Billing (.NET SDK)

SDK: `AsadAli.AdvancedBilling.Sdk` (NuGet) · root namespace `MaxioAdvancedBilling` · APIMatic-generated,
`netstandard2.0`, C# `LangVersion 14`, `Nullable enable`. Target: sandbox site `cp-exp-1` (US hosting).

---

## 1. Scope & sequence

Additive to the existing one-time cart flow. Hero flow: list plans → subscribe → see it in account.

1. **Install the SDK** — `dotnet add package AsadAli.AdvancedBilling.Sdk` into `src/PublicApi`. Never
   add a project reference to the SDK source. Transitive deps arrive automatically (`Polly`,
   `Microsoft.Extensions.Http`, `System.Net.Http.Json`).
2. **Config binding** — bind a `Maxio`-section options record with keys `Maxio:ApiKey`,
   `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional; when set, used verbatim as
   API base address). Values are loaded into user-secrets by the main agent from env vars
   MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_ENVIRONMENT / MAXIO_DEFAULT_PRODUCT_FAMILY.
3. **Client registration** — build `MaxioAdvancedBillingClientOptions` (Basic auth + server node +
   verbatim base-URL override) and register the client in DI (§2, Client construction).
4. **Plan catalog service** → `GET /api/subscription-plans`
   (ops: `ListProductFamilies`, `ListProductsForProductFamily`) — resolve product family by handle
   `Maxio:ProductFamilyHandle`, list its products, map to plan DTOs (id, handle, name, price, interval).
5. **Idempotent customer service** → shared by both write flows
   (ops: `ReadCustomerByReference`, `CreateCustomer`) — find-or-create by a stable per-eShop-user
   reference value; tolerate the concurrent-create 422 by re-reading by reference.
6. **Enrollment service** → `POST /api/subscriptions`
   (ops: `FindSubscription`, `CreateSubscription`, `ReadSubscription`, `ActivateSubscription` [only if
   state is `awaiting_signup`]) — dedupe on a subscription reference key, create on product handle +
   customer id **without any payment profile** (products are card-not-required), then read back
   state / price / next-billing date to confirm to the caller.
7. **My-subscriptions service** → `GET /api/my-subscriptions`
   (ops: `ReadCustomerByReference`, `ListCustomerSubscriptions`) — customer by reference, then their
   subscriptions, mapped to account-visible DTOs (state, product handle, price, next billing date).
8. **Error boundary** — one translation layer for all five routes: `SdkException<TError>` (Case A/B) →
   HTTP status + best-effort message; `System.Text.Json.JsonException` handled per the two hazard rows
   below. Written once, before or alongside steps 4–7.
9. **Endpoint wiring in PublicApi** — the three routes, JWT-authenticated, caller identity from the
   token, following that project's existing endpoint conventions (application-owned; not an SDK fact).
10. **Optional tests** for the integration layer — see REQUIRED READING.

The customer reference value, subscription reference value, persistence, and route handlers are
application decisions — see `YOUR CALL` rows.

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

### 2.1 Namespaces you will need

| Contents | Namespace | Grounding |
|---|---|---|
| Client, options, `ServerOptions`, DI extension | `MaxioAdvancedBilling` | sdk-map.md; ServerOptions.cs (repo root ⇒ root ns) |
| `ServerEnvironment`, `ProductionOptions` | `MaxioAdvancedBilling.Servers` | sdk-map.md; Servers/ProductionOptions.cs |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | sdk-map.md |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | sdk-map.md |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | source-verified `Core/Exceptions/SdkException.cs` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | source-verified `Core/ErrorResponse/RawError.cs` |
| Controllers (`client.X` groups) | `MaxioAdvancedBilling.Api` | sdk-map.md namespaces table |
| Request/response records | `MaxioAdvancedBilling.Models` | sdk-map.md namespaces table |
| Enums (`StringEnum<T>`) | `MaxioAdvancedBilling.Models.Enums` | sdk-map.md namespaces table |
| Typed error classes | `MaxioAdvancedBilling.Errors` | sdk-map.md namespaces table |

### 2.2 Client construction, auth, server node (source: sdk-map.md "Getting a client" + "Servers & auth"; `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `ServiceCollectionExtensions.cs` source-verified)

```csharp
using MaxioAdvancedBilling;                                 // client, options, ServerOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;       // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                         // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    Environment = ServerEnvironment.Us,                     // sandbox cp-exp-1 is US-hosted
    BasicAuth = new BasicAuthCredentials
    {
        Username = apiKey,                                  // Maxio:ApiKey
        Password = "x",                                     // literal "x" — never the subdomain
    },
};
options.Server.Production.Us.Site = subdomain;              // Maxio:Subdomain → https://{site}.chargify.com
if (!string.IsNullOrWhiteSpace(baseUrlOverride))
    options.Server.Production.Us.BaseUrl = baseUrlOverride; // Maxio:BaseUrl — used VERBATIM
```

| Fact | Value | Grounding |
|---|---|---|
| Only client constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | sdk-map.md |
| Options properties | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | sdk-map.md |
| Server node override | `options.Server.Production.Us.Site` (string, default `"subdomain"`) and `options.Server.Production.Us.BaseUrl` (string, default `"https://{site}.chargify.com"`) | `Servers/ProductionOptions.cs` |
| Verbatim base URL | setting `Us.BaseUrl` replaces the `{site}` template; a value without a `{site}` placeholder is used literally as the base address — `Maxio:BaseUrl` works verbatim when set | `Servers/ProductionOptions.cs` `Resolve()` |
| EU option | `ServerEnvironment.Eu` + `options.Server.Production.Eu.*` — only if the account is EU-hosted | sdk-map.md |
| DI extension | `IServiceCollection AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` in `MaxioAdvancedBilling` (root ns). Generated as a C# 14 **extension member** (`extension(IServiceCollection services)`), not a classic extension method — it will not compile against an older C# compiler version. If the PublicApi project does not build with C# 14, register manually instead: `services.AddHttpClient();` + `services.AddSingleton(sp => new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), options));` — this mirrors exactly what the SDK's own extension does (registers a singleton client over one `IHttpClientFactory.CreateClient()`). | source-verified `ServiceCollectionExtensions.cs` |
| Which env var selects Us/Eu | `MAXIO_ENVIRONMENT` is an application-configuration input; the SDK only offers `ServerEnvironment.Us` / `.Eu` | YOUR CALL — not in the map (SDK fact: only those two values exist, sdk-map.md) |

### 2.3 Operations

Parameter names are literal. Nullable params with no C# default are marked **must-pass-explicitly** —
pass them positionally in the shown order, or by exact named argument; pass `null` to skip a filter.
`ct` is `CancellationToken ct = default`.

**O1 · Resolve product family by handle** — no by-handle read exists; see note.
- `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 params must-pass-explicitly (pass `null`).
- Returns `IReadOnlyList<ProductFamilyResponse>`; **no pagination** — full list.
- Match `resp.ProductFamily.Handle == Maxio:ProductFamilyHandle` client-side; take `ProductFamily.Id`.
- Error: **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.
- ⚠ Note: `ReadProductFamily(int id)` takes an `int`; the provider prose that the family "can be specified … with the `handle:my-family` format" is **not reachable through this int-typed parameter** — do not try it. The list-and-match above is the grounded path.
- Source: `operations/ProductFamilies.md`, `records-3-Of-Su.md` (`ProductFamily`, `ProductFamilyResponse`).

**O2 · List plans (products) in the product family**
- `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params must-pass-explicitly; pass the **numeric family id as a string** (the generated parameter is `string`).
- Returns `IReadOnlyList<ProductResponse>`; pagination manual `page`+`perPage` (default 1/20 — plans list is small; loop pages only if you expect >20 products).
- Error: **Case A** `SdkException<ListProductsForProductFamilyError>` (`MaxioAdvancedBilling.Errors`) — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback].
- Source: `operations/ProductFamilies.md`.

**O3 · Read a single product by handle** (resolve/confirm a plan without listing)
- `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — returns `ProductResponse`.
- Error: **Case B** `SdkException<RawError>`.
- Source: `operations/Products.md`.

**O4 · Find-or-create customer (idempotent)**
- Lookup: `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` → `CustomerResponse`. Error: **Case B** `SdkException<RawError>` — 404 ⇒ no customer with that reference.
- Create: `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` → `CustomerResponse`. Error: **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback].
- Map Notes (authoritative for idempotency): "you may only create one customer for a given reference value. If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app." → the **`reference` field is the external/idempotent key**; a concurrent duplicate create fails 422 and the caller must re-read by reference (defensive directive, since the live 422 body shape is suspect — see §2.6).
- Source: `operations/Customers.md`, `records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`), `records-2-Cr-Ne.md` (`Customer`, `CustomerResponse`).

**O5 · Create subscription (card-not-required path)**
- `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` → `SubscriptionResponse`.
- Error: **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`ErrorListResponse1.Errors: IReadOnlyList<string>` — plain message list) · `TryGetRawError(out RawError)` [fallback].
- Map Notes: product identified by **`product_id` OR `product_handle`** (handle is sufficient — no handle→id resolution needed); existing customer identified by **`customer_id` OR `customer_reference`**; payment info "may be required … depending on the options for the Product being subscribed" — our products are card-not-required, so **omit every payment-profile field entirely**.
- Source: `operations/Subscriptions.md`, `records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`, `SubscriptionCustomPrice`).

**O6 · Dedupe lookup for the subscription reference**
- `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` → `SubscriptionResponse`.
- Error: **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]. 404 ⇒ no subscription with that reference.
- Source: `operations/Subscriptions.md`.

**O7 · Read one subscription (confirm state / price / next billing date)**
- `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must-pass-explicitly (pass `null`).
- Returns `SubscriptionResponse`. Error: **Case B** `SdkException<RawError>`.
- Source: `operations/Subscriptions.md`.

**O8 · Activate if the signup landed in `awaiting_signup`** (defensive; see §2.6)
- `client.Subscriptions.ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must-pass-explicitly (pass `null`).
- Returns `SubscriptionResponse`. Error: **Case A** `SdkException<ActivateSubscriptionError>` — `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [400] (`Errors: IReadOnlyDictionary<string, object>?`) · `TryGetRawError(out RawError)` [fallback].
- Map Notes: activates `awaiting signup` and `trialing` subscriptions (Relationship Invoicing architecture).
- Source: `operations/Subscriptions.md`.

**O9 · List a customer's subscriptions**
- `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` → `IReadOnlyList<SubscriptionResponse>`; **no pagination**.
- Error: **Case B** `SdkException<RawError>`.
- Source: `operations/Customers.md`.

**O10 · Find a component by handle** (only if the plan catalog should expose `api-call`; not needed for the hero flow)
- `client.Components.FindComponent(string handle, CancellationToken ct = default)` → `ComponentResponse`. Error: **Case B** `SdkException<RawError>`.
- Alternative (family-scoped listing): `client.Components.ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params must-pass-explicitly; manual page+perPage; **Case B**.
- Source: `operations/Components.md`, `records-1-Ac-Cr.md` (`Component`, `ComponentResponse`).

### 2.4 Models — fields we construct or read (`CSharpName (wire_name): type, required?`)

**Request models to construct**

| Model | Field | Notes |
|---|---|---|
| `MaxioAdvancedBilling.Models.CreateCustomerRequest` | `Customer (customer): CreateCustomer !req` | wrapper; envelope key `customer` |
| `MaxioAdvancedBilling.Models.CreateCustomer` | `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` · (optional also: `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `CcEmails`, `TaxExempt`, …) | Only `first_name`, `last_name`, `email` are required. **`Reference` is the external idempotent key — unique per site.** The reference *value format* (e.g. an eShop user id) is YOUR CALL — not in the map. |
| `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` | `Subscription (subscription): CreateSubscription !req` | wrapper; envelope key `subscription` |
| `MaxioAdvancedBilling.Models.CreateSubscription` | fields **we set**: `ProductHandle (product_handle): string?` · `CustomerId (customer_id): int?` · `Reference (reference): string?` · `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | Other fields exist and we deliberately leave them out (per the record, all fields are optional; acceptance per Notes needs product + customer identifiers): `ProductId`, `ProductPricePointHandle`/`Id`, `CustomPrice` (union-bearing `SubscriptionCustomPrice`), `CouponCode`/`CouponCodes`, `ReceivesInvoiceEmails`, `NetTerms`, `NextBillingAt`, `InitialBillingAt`, `DeferSignup`, `SalesRepId`, `PaymentProfileId`, `CustomerReference`, `CustomerAttributes`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `Components`, `CalendarBilling`, `Metafields`, `Group`, `Ref`, `CancellationMessage`, `CancellationMethod`, `Currency`, `ExpiresAt`, `AgreementTerms`, `AuthorizerFirstName/LastName`, and more. **Leave the payment-profile fields unset** — that is the card-not-required path. Which values to send for `PaymentCollectionMethod` is a plan-for-the-plan decision; the enum's live-state options are in §2.5. |

⚠ A request model may mark nothing required, and then `required?` selects nothing for you. The
operational minimum comes from the operation's **Notes**, not the compiler: product via
`product_handle`, customer via `customer_id`, and no payment profile (products are configured
card-not-required). No compiler catches a field you drop.

**Response envelopes — reads go one level down**

| Envelope | Inner field(s) | Inner model fields the integration reads |
|---|---|---|
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` — **nullable inner** | `Subscription`: `Id (id): int?` · `State (state): SubscriptionState?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (**next billing date**) · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `ProductPriceInCents (product_price_in_cents): long?` · `BalanceInCents (balance_in_cents): long?` · `Product (product): Product?` · `Customer (customer): Customer?` · `ActivatedAt`, `CreatedAt`, `CancelAtEndOfPeriod`, `PaymentCollectionMethod`, … |
| `ProductResponse` | `Product (product): Product !req` | `Product`: `Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `RequireCreditCard (require_credit_card): bool?` · `RequestCreditCard (request_credit_card): bool?` · `Taxable (taxable): bool?` · `ArchivedAt (archived_at): DateTimeOffset?` · `ProductFamily (product_family): ProductFamily?` |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` — **nullable inner** | `ProductFamily`: `Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` · `ArchivedAt (archived_at): DateTimeOffset?` |
| `CustomerResponse` | `Customer (customer): Customer !req` | `Customer`: `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName`, `LastName` |
| `ComponentResponse` | `Component (component): Component !req` | `Component`: `Id`, `Handle (handle)`, `Name (name)`, `UnitName (unit_name)`, `PricePerUnitInCents (price_per_unit_in_cents): long?`, `Kind (kind): ComponentKind?` |

Sources: `records-4-Su-We.md` (`SubscriptionResponse`, `SubscriptionCustomPrice`), `records-3-Of-Su.md`
(`Subscription`, `Product`, `ProductFamily`, `ProductFamilyResponse`), `records-2-Cr-Ne.md`
(`CreateSubscription`, `CreateSubscriptionRequest`, `Customer`, `CustomerResponse`, `CustomerAttributes`),
`records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`, `Component`, `ComponentResponse`).

### 2.5 Enums (`MaxioAdvancedBilling.Models.Enums`) — StringEnum wrappers, not C# enums

| Enum | Literal member (wire value) list | Used for |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | compare `sub.State` against `SubscriptionState.Active` etc. |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `CreateSubscription.PaymentCollectionMethod` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `ReadSubscription` `include` (we pass `null`) |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards (expired_cards)`, `OnHold`, `PastDue`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended`, `TrialEnded (trial_ended)`, `Trialing`, `Unpaid` | only if filtering `ListSubscriptions` by state (we use `ListCustomerSubscriptions` instead) |
| `IntervalUnit` | `Day (day)`, `Month (month)` | plan DTO mapping |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | plan expiry mapping (plans never expire) |
| `BasicDateField`, `ListProductsInclude` | (pass `null` everywhere in scope) | skipped filter params |

Source: `map/models/enums.md`. Build/compare enums via the static members shown or
`Type.FromValue("wire")` — never by casting.

### 2.6 Error boundary — exact types and reading rules

- All operations are **throw-only** — there are no `{Operation}Result` no-throw variants in this SDK.
- Case A (typed): `catch (SdkException<CreateSubscriptionError> ex)` etc. — `ex.Error` exposes the
  operation's `TryGet…` accessors (per row above) plus the inherited
  `TryGetRawError(out RawError)` fallback (every typed error derives from `ApiError`).
- Case B (raw): `catch (SdkException<RawError> ex)` — `ex.Error.StatusCode: HttpStatusCode`,
  `ex.Error.ReadAsString(): string`, `ex.Error.ReadAsJson<T>(): T?`, `ex.Error.ReadAsBytes()`.
- Case assignment for in-scope ops: **Case A** — `CreateCustomer`, `CreateSubscription`,
  `FindSubscription`, `ActivateSubscription`, `ListProductsForProductFamily`; **Case B** —
  `ReadCustomerByReference`, `ListProductFamilies`, `ReadProductByHandle`,
  `ListComponentsForProductFamily`, `FindComponent`, `ReadSubscription`,
  `ListCustomerSubscriptions`.
- **Typed-422 trust caveat (evidence-based):** `CreateCustomer`'s 422 payload record
  `CustomerErrorResponse1` maps `errors` to a shared record `Errors` whose only fields are
  `PerPage (per_page)` and `PricePoint (price_point)` — it cannot represent customer validation
  messages. Defensive directive: extract best-effort from the typed accessor, and on any miss or
  null fall back to `TryGetRawError(out var raw)` + `raw.ReadAsString()` for the user-visible
  message. Whether the live 422 body matches the generated shape is **UNVERIFIED** (only live
  traffic can confirm).
- The two `JsonException` hazard rows (must be handled in the boundary, first sheet):
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the `JsonException`
    **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
    maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
    and a caller that retries 5xx retries something that can never succeed.

### 2.7 Client construction & config facts (summary row)

| Concern | Fact | Source |
|---|---|---|
| Credentials | HTTP Basic — `Username` = Maxio API key (`Maxio:ApiKey`), `Password` = literal `"x"` | sdk-map.md auth section |
| Subdomain/site | `options.Server.Production.Us.Site = <Maxio:Subdomain>` → `https://{site}.chargify.com` | sdk-map.md servers; `ProductionOptions.cs` |
| Base-URL override | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` verbatim, only when the key is set to a non-empty value | `ProductionOptions.cs` `Resolve()` + default template |
| Retry/timeout | configured on `options.Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`, all members `required` — start from `RetryOptions.Default()`) | sdk-map.md options table |

---

## 3. Trap notes (hazard + pointer; the skill carries the resolution)

> ⚠ Step 3 (client registration) — the SDK's generated DI extension is a C# 14 extension-member
> declaration and registers a singleton client over one factory-created `HttpClient`; whether the
> PublicApi project's compiler/language version accepts that syntax, and what client/handler
> lifetime the app should run, are exactly the things the signature does not show. **MUST load
> `dotnet-client-initialization`** before wiring the client.

> ⚠ Step 3 (auth) — credentials must be in place before the first call, the password slot is a
> literal `"x"` (not the subdomain, not blank), and the key must come from configuration. Get the
> exact credential property wiring and rotation/refresh behavior from the skill. **MUST load
> `dotnet-authentication`** before constructing the options.

> ⚠ Steps 4–7 (every call) — several in-scope signatures carry 5–9 optional params with **no C#
> defaults** (must-pass-explicitly), and the token parameter is literally `ct` — a positional or
> misnamed call silently binds wrong or fails to compile. **MUST load `dotnet-calling-endpoints`**
> before writing the first `client.X.Y(...)` call.

> ⚠ Steps 4–7 (models) — records are immutable with `init`-only setters and envelope inners are
> nullable (`SubscriptionResponse.Subscription` is `Subscription?`); enums are `StringEnum<T>`
> wrappers; a response field the generated model lacks is dropped silently on deserialize.
> **MUST load `dotnet-models`** before constructing any request or reading any response.

> ⚠ Step 8 (error boundary) — Case A vs Case B differs per operation (see §2.6), `TryGetRawError`
> is not a catch-all on typed errors, and `JsonException` reaches the boundary from two directions
> with opposite handling (§2.6 rows). **MUST load `dotnet-error-handling`** before writing the
> try/catch ladder.

> ⚠ Steps 6 and 8 (resilience) — whether a failed `CreateSubscription` write can be re-sent, what
> a transport-level retry does to this non-idempotent POST, what `Timeout` actually bounds, and
> what `MaxRetries = 0` does are all decided by the SDK's retry layer, not by these signatures.
> **MUST load `dotnet-configuration-resilience`** before tuning or even defaulting `options.Retry`.

> ⚠ Step 10 (tests) — the `HttpClient` constructor argument is the seam to fake; match the
> project's existing test framework and assertion style. **MUST load `dotnet-testing`** before
> stubbing the SDK.

---

## 4. REQUIRED READING (load before implementation starts — the sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 3 — client construction, DI registration, HttpClient ownership/lifetime |
| `dotnet-authentication` | Step 3 — Basic credential wiring, per-environment config, rotation |
| `dotnet-calling-endpoints` | Steps 4–7 — calling operations, optional-param discipline, async/cancellation |
| `dotnet-models` | Steps 4–7 — request construction, required members, StringEnum, wire names, nullable envelope inners |
| `dotnet-error-handling` | Step 8 — the exception boundary, Case A/B accessors, the two JsonException hazards |
| `dotnet-configuration-resilience` | Steps 6, 8 — retries/backoff, what `Timeout` bounds, base-URL/server selection, pagination behavior |
| `dotnet-testing` | Step 10 — the fake seam and edge/error-path coverage for the integration layer |

`dotnet-error-handling` appears even though the sheet already summarizes the two JsonException
hazards: the summary names the hazard, the skill carries the handling pattern.

---

## 5. Assumptions & Blockers

**Assumptions (application decisions — YOUR CALL — not in the map):**
- The customer `reference` value is derived from the JWT caller identity by the application (the SDK
  only provides the unique-string field; which string is an app decision).
- The subscription `reference` value used for dedupe is likewise app-chosen (e.g. per user+plan key).
- Route handlers, JWT validation, persistence of any user↔customer mapping, and concurrency control
  inside PublicApi are application-owned; this sheet only states the SDK facts those layers consume.
- `MAXIO_ENVIRONMENT` maps to `ServerEnvironment.Us` for the cp-exp-1 sandbox; the SDK's only values
  are `Us` and `Eu` (sdk-map.md).
- User-secrets are loaded by the main agent before build/run.

**Unverified (only live traffic can confirm; defensive coding directives above cover them):**
- Whether a card-not-required subscription is created in state `active` directly or lands in
  `awaiting_signup` on this site — the plan reads state back and calls `ActivateSubscription` only
  in the `awaiting_signup` case (map Notes confirm that operation exists for exactly that state).
- Whether live 422 bodies match the generated `{Operation}Error` shapes — §2.6's raw-fallback
  directive covers the mismatch, and the two JsonException hazard rows cover the worse case.
- Whether Maxio rejects a duplicate subscription `reference` on create (the map documents uniqueness
  only for **customer** reference). The find-then-create flow with `FindSubscription(reference)`
  must therefore be paired with an application-level dedupe guard — YOUR CALL — not in the map.

**Blockers:** none. Every in-scope operation, model, enum, error type, and client-construction fact
was resolved from the SDK map (two core-namespace facts source-verified).