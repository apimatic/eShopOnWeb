# maxio-plan.md — Maxio Advanced Billing .NET SDK integration for eShopOnWeb PublicApi

Package `AsadAli.AdvancedBilling.Sdk`, root namespace `MaxioAdvancedBilling` (differs from package id —
install by package id, import by namespace). Target `netstandard2.0`, works on .NET 8. Auth is HTTP
**Basic**: username = API key, password = the literal `"x"`.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Install package, bind `MaxioOptions`, register SDK client in DI | — (construction; §3 client facts) |
| 2 | Wrap SDK behind `IMaxioBillingService` (DTOs only at the endpoints) | — |
| 3 | `GET /api/subscription-plans` — resolve configured product family, list its products | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | Idempotently ensure Maxio customer for the logged-in user | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` — create subscription, no card capture | `Subscriptions.CreateSubscription` (customer pre-created in step 4 → pass `customer_id`) |
| 6 | `GET /api/my-subscriptions` — list subscriptions for that customer | `Customers.ListCustomerSubscriptions` |
| 7 | Error boundary (exception translation, 404 vs 422) | applies to every call above |

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

Namespaces: client/options root `MaxioAdvancedBilling`; controllers `MaxioAdvancedBilling.Api`;
records `MaxioAdvancedBilling.Models`; enums `MaxioAdvancedBilling.Models.Enums`; errors
`MaxioAdvancedBilling.Errors`; retry config `MaxioAdvancedBilling.Core.Configuration`; auth
`MaxioAdvancedBilling.Core.Authentication.Basic`; servers `MaxioAdvancedBilling.Servers`.
C# does not import child namespaces transitively — add one `using` per kind of type referenced.

### Client construction, base URL, auth

| Fact | Value | Source |
|---|---|---|
| Client class | `MaxioAdvancedBillingClient` — only ctor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options properties | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — Username = API key, Password = literal `"x"` | `sdk-map.md` |
| Subdomain-derived base URL | `options.Server.Production.Us.Site = "<subdomain>"` → `https://{site}.chargify.com` (`{site}` defaults to `subdomain`) | `sdk-map.md` (Servers & auth) |
| Base-URL override (mock/dev host) | `options.Server.Production.Us.BaseUrl = "http://localhost:8080"` | `sdk-map.md` (Servers & auth) |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu` → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` exists (`ServiceCollectionExtensions.cs`) — or construct via `IHttpClientFactory`-managed `HttpClient` | `sdk-map.md` |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions`: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` — all members `required`; start from `RetryOptions.Default()` | `sdk-map.md` |

### Operations

⚠ **`CreateSubscription`'s request model marks nothing `required`.** The fields that decide whether
the call is accepted are chosen from the operation's Notes (customer reference, product reference,
payment method) — the compiler will not catch one you drop. Fields deliberately left out of this
plan: `payment_profile_id` / `credit_card_attributes` / `payment_profile_attributes` /
`bank_account_attributes` (no card capture), `custom_price`, `components`, `coupon_code(s)`,
`calendar_billing`, `metafields`, `next_billing_at` / `initial_billing_at` / `previous_billing_at`,
`defer_signup`, group/offer/prepaid/dunning fields.

| Op | Signature | Request model + fields | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| `ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 params nullable, **no default → must pass explicitly (pass `null`)** | — | `IReadOnlyList<ProductFamilyResponse>` → read `.ProductFamily` (nullable): `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params (`dateField`…`include`) nullable, no default → pass explicitly; defaults `page`=1, `perPage`=20 | — | `IReadOnlyList<ProductResponse>` → read `.Product` (required): `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (loop until a short/empty page) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference ← reference` | — | `CustomerResponse` → read `.Customer` (required): `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` | **Case B** `SdkException<RawError>` — 404 = no customer with that reference (`ex.Error.StatusCode`) | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateCustomerRequest` → `.Customer (customer): CreateCustomer !req`; `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, plus optional address/phone/org fields (all nullable) | `CustomerResponse` → `.Customer` as above | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. **422 on duplicate `reference`** | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateSubscriptionRequest` → `.Subscription (subscription): CreateSubscription !req`; `CreateSubscription` — **nothing `required`**; fields used here: `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`, `CustomerId (customer_id): int?` (or `CustomerReference (customer_reference): string?`), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Reference (reference): string?`, `Currency (currency): string?`, `NetTerms (net_terms): string?` | `SubscriptionResponse` → read `.Subscription` (nullable): `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (= next-billing date), `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Product (product): Product?` (fields as in the products row), `Customer (customer): Customer?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` fields as in the create row (Id, State, ProductPriceInCents, CurrentPeriodEndsAt, Product, Customer, PaymentCollectionMethod) | **Case B** `SdkException<RawError>` | none (returns all for the customer) | `operations/Customers.md`, `records-4-Su-We.md` |
| `Subscriptions.FindSubscription` (idempotency option) | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → pass explicitly | — | `SubscriptionResponse` → `.Subscription` as above | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

### Enum values needed (all are `StringEnum<T>` — **not** C# enums; construct via the static members shown or `Type.FromValue("wire")`)

| Enum | Wire values |
|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — legacy Statements Architecture accepts `invoice`/`automatic`; Relationship Invoicing accepts `remittance`/`automatic`/`prepaid` |
| `MaxioAdvancedBilling.Models.Enums.BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `MaxioAdvancedBilling.Models.Enums.SortingDirection` | `Asc (asc)`, `Desc (desc)` |

Source for all four: `map/models/enums.md`.

### No-card signup (payment method NOT required)

- **Request side (map-grounded):** omit every payment field — `PaymentProfileId`,
  `CreditCardAttributes`, `PaymentProfileAttributes`, `BankAccountAttributes` are all optional on
  `CreateSubscription`. The map's `CreateSubscription` Notes state payment information "may be
  required … depending on the options for the Product being subscribed", and the operation offers no
  other explicit "skip card" flag on the request model. Setting `PaymentCollectionMethod =
  CollectionMethod.Invoice` (legacy architecture) or `.Remittance` (Relationship Invoicing) signals a
  non-automatic collection method. **Which value is right depends on your site's invoicing
  architecture — YOUR CALL — not in the map.**
- **Site/product side:** whether the signup is accepted without a card is governed by the product's
  `RequireCreditCard` / `RequestCreditCard` settings in Maxio (`Product.RequireCreditCard`,
  `Product.RequestCreditCard` are readable in the list-products response — surface them on the
  plans endpoint so the app can warn). If the product requires a card, the create returns 422
  (`ErrorListResponse1`). No SDK call can flip that setting; it is Maxio site/product configuration.
  A rejection here is **expected provider behaviour, not an outage** — see Blocker B2.

### Recommended layering (application design — YOUR CALL — not in the map)

- `MaxioOptions` bound from configuration section `Maxio` with keys `ApiKey`, `Subdomain`,
  `ProductFamilyHandle`, `BaseUrl` (optional; empty/absent = subdomain-derived URL). Bind via
  `IOptions<MaxioOptions>`; validate `ApiKey`/`Subdomain`/`ProductFamilyHandle` at startup.
- `IMaxioBillingService` (+ impl) owns the SDK client; endpoints receive/return only app DTOs
  (`SubscriptionPlanDto`, `SubscriptionDto`) — no `MaxioAdvancedBilling.*` type crosses the
  endpoint boundary. The DTO carries exactly what the endpoints promise: plan id/handle/name,
  price (cents), state, next-billing date.
- Client registered once in DI; the SDK wrapper's `HttpClient` must be long-lived (see trap note
  step 1 — resolved by the companion skill, not here).

### Idempotency recommendation

- **Customer:** set `CreateCustomer.Customer.Reference` = the eShopOnWeb **user id** (a stable app
  key; the map's Notes say `reference` is "a unique identifier for the customer from your own app"
  and duplicates are rejected). Flow: `ReadCustomerByReference(userId)` → 404 (Case B,
  `StatusCode == HttpStatusCode.NotFound`) → `CreateCustomer` with the same `Reference`; if the
  create 422s with a duplicate-reference error (concurrent race), re-run the lookup and use the
  winner. Email alone is not unique — key on user id; use `ListCustomers(q: email, …)` only as a
  diagnostic fallback, never as the identity key.
- **Subscription (optional but recommended):** `CreateSubscription` accepts `Reference`; pair it
  with `FindSubscription(reference)` keyed deterministically (e.g. `"{userId}:{productHandle}"`)
  so a retried POST cannot double-subscribe. Whether the app needs this guard is
  **YOUR CALL — not in the map**; the contract above supports it either way.

---

## 3. Trap notes (name the hazard; load the skill before touching that step)

> ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are
> **not** the timeout on the `HttpClient` you register; how `HttpClient` lifetime and the SDK wrapper
> interact decides whether sockets and credentials survive. **MUST load `dotnet-client-initialization`**
> before wiring the client.

> ⚠ Step 2 (auth wiring) — credentials must be set before the first call and loaded from configuration,
> not hardcoded; the Basic scheme here is unusual (API key as *username*, literal `"x"` as password) and
> the skill carries the exact wiring order. **MUST load `dotnet-authentication`** before setting
> `BasicAuth`.

> ⚠ Step 3 (first call) — many list/search operations have nullable parameters with **no C# default**
> (`ListProductsForProductFamily`'s `dateField`…`include`, `CreateSubscription`'s `body`); they must be
> passed explicitly, and positional calls can mis-bind. **MUST load `dotnet-calling-endpoints`** before
> writing the first `client.X.Y(...)` call.

> ⚠ Step 4 (request/response models) — enums are `StringEnum<T>` built from static members or
> `Type.FromValue`, responses wrap their payload one level down (`SubscriptionResponse.Subscription`),
> and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** before
> constructing any payload or mapping a response.

> ⚠ Step 5 (resilience tuning) — whether a failed write can be re-sent, what `Timeout` actually bounds,
> and what `MaxRetries` floors at are NOT visible in the options table above; a wrong guess here can
> execute a non-idempotent signup more than once or turn a config error into an outage. **MUST load
> `dotnet-configuration-resilience`** before touching `RetryOptions` or the base-URL override.

> ⚠ Step 7 (error boundary) — every operation is throw-only (no `…Result` variants); Case A/B differ
> per operation (see sheet), and `System.Text.Json.JsonException` reaches the boundary from two
> directions with opposite required handling:
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
>   `JsonException` from deserialization, **not** as an `SdkException` — so an
>   SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
>   throws `JsonException` *while the error object is being constructed*, so the `JsonException`
>   **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
>   maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
>   and a caller that retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.

> ⚠ Tests — the `HttpClient` constructor argument is the test seam; faking the SDK client wrapper
> directly is not the supported shape. **MUST load `dotnet-testing`** before stubbing the SDK in tests.

---

## 4. REQUIRED READING — load before implementation starts (the sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, `HttpClient` ownership/lifetime, builder shape. |
| `dotnet-authentication` | Step 2 — Basic credential wiring (API key as username, `"x"` password), config loading, rotation. |
| `dotnet-calling-endpoints` | Step 3 — named arguments, must-pass-explicitly nullable params, envelope unwrapping. |
| `dotnet-models` | Step 4 — `StringEnum<T>` construction, required members, wire names, unions (none in this scope, but the rules apply if scope grows). |
| `dotnet-configuration-resilience` | Step 5 — retry/timeout semantics, `MaxRetries` floor, base-URL override, pagination looping. |
| `dotnet-error-handling` | Step 7 — Case A/B catch ladders, `TryGet…` accessors, both `JsonException` hazards above. |
| `dotnet-testing` | Tests — the `HttpClient` seam, covering 404/422 paths. |

---

## 5. Assumptions & Blockers

**Assumptions**

- A1 — US hosting (`ServerEnvironment.Us`, `https://{site}.chargify.com`); EU hosting is not needed.
  The config supports override via `BaseUrl` only, not via an EU flag — add a setting if EU is real.
- A2 — `Maxio:ProductFamilyHandle` identifies one product family; family → numeric id resolved at
  runtime via `ListProductFamilies` (filter by `Handle`). The map's ProductFamilies Notes document a
  `handle:my-family` addressing format for product families; passing that string directly to
  `ListProductsForProductFamily`'s `string productFamilyId` may work, but the lookup-by-listing path
  above is the map-safe route and is what this plan prescribes.
- A3 — the eShopOnWeb user's first/last name and email are available to the subscription endpoint
  (JWT identity) and satisfy `CreateCustomer`'s three required fields.
- A4 — subscription-level idempotency (§ "Idempotency recommendation", `FindSubscription`) is offered
  as supported-by-the-map; whether the app adopts it is the implementer's call.

**Blockers**

- B1 — **No card-free guarantee without site config.** The map shows no SDK request field that forces a
  cardless signup past product settings; if the Maxio product has `require_credit_card` enabled, the
  create is rejected 422. Resolution: the Maxio site's products must be configured to not require a
  credit card before this integration can succeed; verify once in the Maxio UI. Until verified, the
  `POST /api/subscriptions` success path is **UNVERIFIED**.
- B2 — **Whether `PaymentCollectionMethod.Invoice` vs `.Remittance` is valid depends on the site's
  invoicing architecture** (legacy Statements vs Relationship Invoicing — `map/models/enums.md`). Pick
  one at config time; the wrong one is a 422 you will see on the first live call. Treat the first
  live signup as the verification of this choice.
- B3 — **Live-wire shape trust:** the sheet reflects the SDK's generated models at source commit
  `15db14b` (`v1.0.2`). That two generated definitions agree is the only evidence available offline;
  whether the live wire's 2xx/error bodies match them can only be confirmed by traffic — handled
  defensively by the Step 7 boundary (both `JsonException` hazards) rather than assumed.
