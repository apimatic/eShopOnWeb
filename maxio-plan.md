# maxio-plan.md — Recurring-subscription billing for eShopOnWeb via Maxio Advanced Billing (.NET SDK)

SDK: NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · `netstandard2.0` · every fact below is grounded in the bundled SDK map (v1.0.2, commit `15db14b`).

---

## 1. Scope & sequence

Additive/parallel to the existing one-time commerce flow. Steps in order; each names the SDK operations it uses:

| # | Step | Operations used |
|---|---|---|
| 1 | Bind `Maxio:` configuration (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` — the last an optional verbatim override) | — |
| 2 | Register the SDK client in DI (HttpClient via factory; Basic auth from config) | client construction |
| 3 | Resolve the configured product family **by handle** | `ProductFamilies.ListProductFamilies` (filter by `Handle` client-side) |
| 4 | `GET /api/subscription-plans` — list plans in the family + component presentation | `ProductFamilies.ListProductsForProductFamily`, `Components.ListComponentsForProductFamily` |
| 5 | Idempotent ensure-customer (lookup-then-create keyed on `reference`) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, (`Customers.ReadCustomer`, `Customers.ListCustomers` as auxiliaries) |
| 6 | `POST /api/subscriptions` — subscribe the caller, guarded against double-click | `Subscriptions.FindSubscription` (guard) + `Subscriptions.CreateSubscription`; optionally `Products.ReadProductByHandle` to resolve the default target |
| 7 | `GET /api/my-subscriptions` — the caller's subscriptions with plan/price/state/next-billing | `Customers.ListCustomerSubscriptions` (+ `Subscriptions.ReadSubscription` for single reads) |
| 8 | Error boundary around every SDK call | per-operation error cases below |
| 9 | *(future, not implemented now)* metered usage reporting for component `api-call` | `SubscriptionComponents.CreateUsage` — note only (§ contract sheet, last row) |

Catalog facts (from the task's seeded sandbox — code must resolve **by handle**, numeric IDs are reassigned on re-seed): Product Family `eshop-subscribe`; products `eshop-pro` ($299.00/mo, default target) and `basic-plan` ($29.00/mo); metered component `api-call`; both plans no trial, no setup fee, payment method NOT required.

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

⚠ **A request model may mark nothing required, and then `required?` selects nothing for you.**
`CreateSubscription` and `CreateCustomer` are both fully optional at the type level — what makes
the call *accepted* comes from the operation's Notes, not the compiler. The Notes-named fields
carried below are: for `CreateSubscription` — `product_id` **or** `product_handle` (plan), `customer_id`
**or** `customer_reference` (customer), payment-profile fields **omitted** to subscribe without a card;
for `CreateCustomer` — `first_name`, `last_name`, `email` (the only members marked `!req` in the model),
`reference` optional-but-unique. Fields deliberately left out of this sheet (trial/coupon/group/import/
3-DS machinery, calendar billing, stored credentials, payment-profile attribute bags) are not needed for
this scope; no compiler catches a field you drop.

### 2.1 Client construction — `MaxioAdvancedBilling` (root namespace)

Only constructor (source: `MaxioAdvancedBillingClient.cs`):
`new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — takes an `HttpClient` you supply.

`MaxioAdvancedBillingClientOptions` properties (source: `MaxioAdvancedBillingClientOptions.cs`):

| Property | Type | Namespace of the type |
|---|---|---|
| `Environment` | `ServerEnvironment` | `MaxioAdvancedBilling.Servers` |
| `Retry` | `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |
| `Server` | `ServerOptions` | (never named in code — set via `options.Server.…` property paths) |
| `BasicAuth` | `BasicAuthCredentials?` | `MaxioAdvancedBilling.Core.Authentication.Basic` |

**Base address — how the SDK accepts the override (answers the BaseUrl question):**

- **(a) api key + subdomain + environment:**
  ```csharp
  var options = new MaxioAdvancedBillingClientOptions
  {
      BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }, // Basic: Username = API key, Password = literal "x"
      Environment = ServerEnvironment.Us,                                         // US-hosted (default); ServerEnvironment.Eu = EU
      Server =
      {
          Production =
          {
              Us = { Site = subdomain }   // fills {site} in https://{site}.chargify.com
          }
      }
  };
  var client = new MaxioAdvancedBillingClient(httpClient, options);
  ```
- **(b) verbatim base-URL override:** when `Maxio:BaseUrl` is set, use it VERBATIM as a **full base address** —
  `options.Server.Production.Us.BaseUrl = baseUrl;` (the map's own example is a full host, `"http://localhost:8080"`).
  `BaseUrl` replaces the derived template; it is not combined with `Site`. **Do not** also derive a URL from the
  subdomain in that case.
- `Environment` (`ServerEnvironment.Us` / `.Eu`) selects only **US vs EU hosting templates**
  (`https://{site}.chargify.com` vs `https://{site}.ebilling.maxio.com`). The map's `ServerEnvironment` has **no
  sandbox/production member** — sandbox identity rides on the site subdomain + API key, not on an options flag.
  `MAXIO_ENVIRONMENT=sandbox` is application configuration validation, not an SDK input.
- All operations in scope use the **Production** server group. (Only event-ingest endpoints use the separate
  Ebb group — irrelevant here; its override point is `options.Server.Ebb.Us.BaseUrl` if ever needed.)
- DI alternative (source: `ServiceCollectionExtensions.cs`):
  `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; });`
- `RetryOptions` members are **all `required`** — build a full instance or start from `RetryOptions.Default()`
  (namespace `MaxioAdvancedBilling.Core.Configuration`, source `Core/Configuration/RetryOptions.cs`). Members:
  `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout: TimeSpan?`, `BackOffFactor`,
  `UseExponentialBackoff`, `MaxJitter`, `OnRetry: Action<RetryAttempt>?`.

### 2.2 Operation table

Controller accessors: `client.ProductFamilies`, `client.Products`, `client.Components`, `client.Customers`,
`client.Subscriptions`, `client.SubscriptionComponents` (namespace `MaxioAdvancedBilling.Api`).

| Operation | Signature (params in order; "must-pass" = nullable, no default) | Request model + fields | Response envelope (inner fields read) | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| ProductFamilies.ListProductFamilies | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 must-pass (pass `null` to skip) | — (query only: `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`) | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — `.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| ProductFamilies.ListProductsForProductFamily | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 must-pass between `productFamilyId` and `page` | — (query: `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include`) | **Array-wrapped envelopes**: `IReadOnlyList<ProductResponse>`, each `.Product` → `Handle (handle)`, `Name (name)`, `Description (description)`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointName (product_price_point_name): string?`, `Id (id): int?` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage`; response model carries **no** metadata field | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `records-1-Ac-Cr.md` |
| Products.ReadProductByHandle | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` (fields as above) | **Case B** `SdkException<RawError>` | none | `operations/Products.md`, `records-3-Of-Su.md` |
| Components.ListComponentsForProductFamily | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 must-pass after `productFamilyId` | — | `IReadOnlyList<ComponentResponse>`, each `.Component` → `Name (name)`, `Handle (handle)`, `Kind (kind): ComponentKind?`, `UnitName (unit_name): string?`, `PricePerUnitInCents (price_per_unit_in_cents): long?`, `Prices (prices)` | **Case B** `SdkException<RawError>` | manual `page`+`perPage`; no metadata field | `operations/Components.md`, `records-1-Ac-Cr.md` |
| Customers.CreateCustomer | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must-pass | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` (wire envelope key `customer`). `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (unique — see Notes), + optional address/org/phone fields | `CustomerResponse` → `.Customer` → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName/LastName`, `CreatedAt (created_at): DateTimeOffset?` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload model `CustomerErrorResponse1 { Errors (errors): Errors? }` where `Errors` exposes only `per_page`/`price_point` string lists | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| Customers.ReadCustomerByReference | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — (query `reference` ← `reference`) | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` — 404 = not found: `.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| Customers.ReadCustomer | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| Customers.ListCustomers | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 must-pass (`direction`…`q`) | — (query incl. `q` ← `q` free-text search) | `IReadOnlyList<CustomerResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Customers.md` |
| Subscriptions.CreateSubscription | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must-pass | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` (wire envelope key `subscription`). `CreateSubscription` fields used here: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`; **omit** `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` entirely → no card capture / no 3-DS | `SubscriptionResponse` → `.Subscription` → `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `BalanceInCents (balance_in_cents): long?`, `Currency (currency): string?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Customer (customer): Customer?`, `Product (product): Product?` (→ plan **handle** = `.Product.Handle`, price = `.Product.PriceInCents`, name = `.Product.Name`) | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422], payload `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` |
| Subscriptions.FindSubscription | `FindSubscription(string? reference, CancellationToken ct = default)` — must-pass | — (query `reference` ← `reference`) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| Subscriptions.ReadSubscription | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must-pass (pass `null`) | — | `SubscriptionResponse` → `.Subscription` | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| Customers.ListCustomerSubscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` (each `.Subscription`, fields as in CreateSubscription row) | **Case B** `SdkException<RawError>` | none — returns all subscriptions of the customer | `operations/Customers.md` |
| SubscriptionComponents.CreateUsage *(future note — not in this scope)* | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` | `CreateUsageRequest { Usage (usage): CreateUsage !req }`; `CreateUsage`: `Quantity (quantity): double?`, `Memo (memo): string?`, `PricePointId`, `BillingSchedule`, `CustomPrice` | `UsageResponse` | **Case A** `SdkException<CreateUsageError>` — `TryGetErrorListResponse1` [422] · raw fallback | none | `operations/SubscriptionComponents.md`, `records-2-Cr-Ne.md` |

### 2.3 Client-construction / auth / server facts (recap)

- Auth: HTTP **Basic** — `BasicAuthCredentials { Username = <API key>, Password = "x" }` (password is the literal
  string `x`). Namespace `MaxioAdvancedBilling.Core.Authentication.Basic`. Source: `Core/Authentication/Basic/BasicAuthCredentials.cs`.
- Subdomain: `options.Server.Production.Us.Site = <subdomain>` → `https://{site}.chargify.com` (US) /
  `https://{site}.ebilling.maxio.com` (EU). Source: `Server.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`.
- Verbatim override: `options.Server.Production.Us.BaseUrl = <full base address>` — full address, used as-is.
- Namespaces needed at construction: `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Core.Authentication.Basic`,
  `MaxioAdvancedBilling.Servers` (`ServerEnvironment` lives there).

### 2.4 Enum values needed (namespace `MaxioAdvancedBilling.Models.Enums`; these are `StringEnum<T>` records, NOT C# enums — members are the literal C# identifiers, wire value in parens; construct with the static member or `Type.FromValue(wire)`)

| Enum | Members (C# (wire)) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `SubscriptionInclude` (ReadSubscription `include`) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `ListProductsInclude` (ListProductsForProductFamily `include`) | `PrepaidProductPricePoint (prepaid_product_price_point)` |

### 2.5 Error boundary facts

- All operations are **throw-only** — there are no `…Result` no-throw variants in this SDK. On an error status the
  SDK throws `SdkException<TError>` (source `Core/Exceptions/SdkException.cs` ⇒ namespace
  `MaxioAdvancedBilling.Core.Exceptions`, per the map's path-implies-namespace rule) exposing `.Error`.
- **Case A** (typed): `TError` is a generated `…Error : ApiError` class (namespace `MaxioAdvancedBilling.Errors`)
  with status-specific `TryGet…(out …)` accessors plus inherited `TryGetRawError(out RawError)`.
- **Case B** (raw): `TError` is `RawError` (source `Core/ErrorResponse/RawError.cs` ⇒
  `MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode: HttpStatusCode`, `ReadAsString(): string`,
  `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
- **404:** Case B operations (e.g. `ReadCustomerByReference`, `ReadCustomer`, `ReadProductByHandle`,
  `ReadSubscription`) surface it as `RawError.StatusCode == HttpStatusCode.NotFound`. Case A operations expose
  their 404 through the named accessor (`TryGetNoContent` on FindSubscription/UpdateCustomer; `TryGetString` on
  ListProductsForProductFamily).
- **422 (validation):** Case A `TryGetErrorListResponse1(out ErrorListResponse1)` whose `Errors` is
  `IReadOnlyList<string>` — user-readable messages. For `CreateCustomer` the typed payload model
  (`CustomerErrorResponse1`→`Errors`) only models `per_page`/`price_point` keys; whether the live 422 body for a
  customer error (e.g. duplicate reference) matches that shape is **UNVERIFIED** — read the typed accessor
  best-effort and always fall back to `TryGetRawError` + `ReadAsString()` for user-visible text.
- **Duplicate/idempotency conflicts:**
  - Customer: the map's CreateCustomer Notes state **only one customer may exist per `reference` value** — a
    duplicate-reference create is a 422 (accessor shape as above).
  - Subscription: the map documents **no** uniqueness guarantee on a subscription `reference`. A double-subscribe
    therefore cannot be prevented by the API contract — the app must guard (FindSubscription / ListCustomerSubscriptions
    check before create, plus per-request idempotency key). If the live API does reject a duplicate, it can only
    manifest through `TryGetErrorListResponse1` [422] — **UNVERIFIED**.

### 2.6 Contract decisions marked per the three labels

| Fact / decision | Label |
|---|---|
| Numeric IDs not stable across re-seeds → resolve family by `Handle` via ListProductFamilies; plan by handle via `ReadProductByHandle` / `product_handle` | Task input; map shows no read-family-by-handle method whose signature accepts a handle (`ReadProductFamily(int id)` is `int`) — `operations/ProductFamilies.md` |
| `ListProductsForProductFamily`'s `productFamilyId` is a `string`; pass the resolved numeric id (`family.Id.Value.ToString()`). Whether this endpoint also accepts the `handle:` prefix form is documented in the map Notes only for `ReadProductFamily` | UNVERIFIED for this endpoint — resolve the numeric id first |
| Pagination for the two list ops is manual `page`+`perPage`; the generated response types carry **no** metadata field (no total-count) — page until a page returns fewer than `perPage` items. Whether live responses carry pagination headers the model drops | response-model fact is map-grounded; header presence is UNVERIFIED |
| Idempotent ensure-customer pattern: `ReadCustomerByReference` first → on 404 `CreateCustomer` with the same `reference` → on 422 re-lookup (race) | lookup-then-create mechanics are map facts (`operations/Customers.md`); **the value of the reference key** (e.g. app user id) is YOUR CALL — not in the map |
| Double-subscribe guard: `FindSubscription(reference)` before create, and/or `ListCustomerSubscriptions(customerId)` filtered by `Product.Handle` + live state | operations are map facts; **the guard key design and state set** are YOUR CALL — not in the map |
| Whether omitting `PaymentCollectionMethod` inherits the product/site default | map says only that payment requirement depends on the Product's options (`CreateSubscription` Notes); inheritance behaviour is UNVERIFIED — leave unset unless the app decides otherwise |
| `Maxio:BaseUrl` override used verbatim as `options.Server.Production.Us.BaseUrl`; `Site` used when not overridden | map-grounded (`Server.cs`/`ServerOptions.cs`) |

---

## 3. Trap notes (attached to the step where each bites)

> ⚠ Step 2 (client registration) — the `HttpClient` you pass to the `MaxioAdvancedBillingClient` constructor must
> be long-lived and handler-reused; rebuilding it per request breaks the pipeline. **MUST load
> `dotnet-client-initialization`** before wiring the client into the service container.

> ⚠ Step 2 (credentials) — the Basic pair is asymmetric: the username carries the API key and the password is the
> literal `"x"`; set credentials before/at client construction and load them from configuration, never hardcode.
> **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Steps 3–7 (calling) — the multi-parameter list operations (`ListProductsForProductFamily`: 8 must-pass
> nullable params; `ListCustomers`: 7) have parameters with no C# default that mis-bind in positional calls —
> call with named arguments and pass `null` explicitly to skip. **MUST load `dotnet-calling-endpoints`** before
> the first `client.…` call.

> ⚠ Steps 3–7 (models) — envelopes are one level down (`ProductResponse.Product`, `SubscriptionResponse.Subscription`,
> `CustomerResponse.Customer`); enums are `StringEnum<T>` records built from static members or `FromValue`, not C#
> enums; request models with nothing `required` give the compiler no guard for the Notes-named fields. **MUST load
> `dotnet-models`** before constructing request payloads or reading responses.

> ⚠ Step 8 (error boundary) — the boundary must handle `SdkException<CreateSubscriptionError>` (Case A) and
> `SdkException<RawError>` (Case B) differently, and must treat deserialization failures as a separate class from
> HTTP errors (see §4 mandatory rows). **MUST load `dotnet-error-handling`** before writing any try/catch around an
> SDK call.

> ⚠ Steps 2/6 (resilience & idempotency) — the SDK's retry settings do **not** bound a whole call and are **not**
> the `HttpClient` timeout; and transport-failure retries apply to every verb including `POST`, so a retried
> `CreateSubscription` may execute more than server-side once — which is exactly why the double-click guard and a
> request-scoped idempotency key matter regardless of HTTP-layer behaviour. **MUST load
> `dotnet-configuration-resilience`** before tuning retries/timeouts or the base URL.

> ⚠ Tests — the `HttpClient` constructor argument is the test seam; fake there, not SDK internals. **MUST load
> `dotnet-testing`** before writing tests for the integration layer.

---

## 4. REQUIRED READING (load ALL before implementation starts — the sheet deliberately does not carry their contents)

| Skill | Governs | One-line reason |
|---|---|---|
| `dotnet-client-initialization` | Step 2 | HttpClient ownership/lifetime and DI registration shape for the SDK client — the constructor taking an `HttpClient` hides the handler-lifetime trap. |
| `dotnet-authentication` | Step 2 | Basic auth wiring (API key as username, `"x"` as password) and per-environment credential configuration. |
| `dotnet-calling-endpoints` | Steps 3–7 | Required-vs-optional parameters, named-argument calls, envelope unwrapping, async/cancellation (`ct`) for every operation above. |
| `dotnet-models` | Steps 3–7 | `StringEnum<T>` construction, union/AnyOf accessor rules, wire names vs C# names, and the unmodeled-fields drop hazard. |
| `dotnet-error-handling` | Step 8 | Case A vs Case B catch ladders, `TryGet…`/raw-body reading, and the `JsonException`-from-two-directions traps in §4 below. |
| `dotnet-configuration-resilience` | Steps 2/6 | What `Retry`/`Timeout` actually bound, base-URL override semantics, pagination loop discipline, and POST-retry consequences for idempotency. |
| `dotnet-testing` | Tests | Which seam to fake (the `HttpClient` argument), covering error/edge paths without SDK internals. |

**Mandatory hazard rows (belong to the FIRST sheet; the boundary is written early):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection (e.g. a 422) as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions** (each explicitly YOUR CALL — not an SDK fact — unless noted):

1. *YOUR CALL — not in the map:* the JWT-authenticated caller identity supplies a stable, immutable user
   identifier (to key the Maxio customer `reference`) and an email (for `first_name`/`last_name`/`email` on
   create). The SDK contract only requires the reference to be unique per customer.
2. *YOUR CALL — not in the map:* the double-subscribe guard key (subscription `reference` value, e.g.
   user+plan-scoped) and which `SubscriptionState` values count as "already subscribed" are application design;
   the sheet carries the operations (`FindSubscription`, `ListCustomerSubscriptions`) and their exact shapes.
3. *YOUR CALL — not in the map:* endpoint auth/authorization on the three PublicApi routes (JWT extraction,
   validation) is the app's existing identity path — the sheet does not name routes or handlers, only the SDK
   calls behind them.
4. Task-input fact (not independently verifiable from the SDK map): the seeded sandbox catalog's handles and the
   "payment method NOT required" product setting. `CreateSubscription`'s Notes (map) confirm payment
   requirement is product-driven, so omitting all payment-profile fields is the correct no-card shape.
5. `MAXIO_ENVIRONMENT=sandbox` has no SDK-side counterpart — `ServerEnvironment` is US/EU only (map);
   sandbox-ness rides on the site subdomain + API key. The setting is treated as app-config validation only.

**Blockers:** none — every operation in scope exists in the map with a full contract (signature, envelope,
error case, pagination). No capability the plan needs is missing from the SDK.