# Maxio Advanced Billing .NET SDK — recurring-subscription plan (eShopOnWeb / src/PublicApi)

**Plan file:** `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-014\repo\maxio-plan.md` (default, per brief — path dictated explicitly)
**SDK:** `AsadAli.AdvancedBilling.Sdk` (NuGet) · root namespace `MaxioAdvancedBilling` · client `MaxioAdvancedBillingClient` · options `MaxioAdvancedBillingClientOptions`
**Source commit:** `15db14b` / `v1.0.2` (map stamp, `sdk-map.md`)
**Sandbox:** site `cp-exp-4`; env vars `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY` (default family handle/ID for lookup); base URL derived from subdomain + `ServerEnvironment` unless `Maxio:BaseUrl` override set (`MaxioAdvancedBillingClientOptions.Server.Production.Us.BaseUrl` or `.Site`).

---

## 1. Scope & sequence

| Step | What | SDK operation(s) | Notes / handles |
|---|---|---|---|
| 1 | Find product family | `client.ProductFamilies.ReadProductFamily(int id, ct)` or `ListProductFamilies` | Family id `3023074`; handle `eshop-subscribe` (map: can specify `handle:…` in `ReadProductFamily`) |
| 2 | List products in family | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, …, ct)` | Filter by family; read `eshop-pro` / `basic-plan` via `ReadProductByHandle` |
| 3 | List components in family | `client.Components.ListComponentsForProductFamily(int productFamilyId, …, ct)` / `FindComponent(string handle, ct)` | Component handles for subscription components |
| 4 | Find / create customer (idempotent by email/username) | `client.Customers.ListCustomers(q: email, …, ct)` then `CreateCustomer(CreateCustomerRequest? body, ct)` if missing | Use `reference` (eShop user id/username) for idempotency if desired; email search via `q` |
| 5 | Subscribe customer to plan | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, ct)` | Plan via `ProductHandle` (`eshop-pro` / `basic-plan`) or `ProductId`; include `Components`; `DeferSignup = false`; no trial / no payment-method-required fields set |
| 6 | List subscriptions for customer | `client.Customers.ListCustomerSubscriptions(int customerId, ct)` | Returns `IReadOnlyList<SubscriptionResponse>`; filter by state via caller if needed |
| 7 | Confirm plan / price / state / next-billing-date | Read `SubscriptionResponse.Subscription` fields (see Contract) | `Subscription.State`, `Subscription.Product` (handle + price), `Subscription.NextAssessmentAt` / `CurrentPeriodEndsAt` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Client construction / auth / server (map: `sdk-map.md`, `dotnet-authentication`, `dotnet-client-initialization`)

| Fact | Value / type | Namespace | Source |
|---|---|---|---|
| Client class | `MaxioAdvancedBillingClient` | `MaxioAdvancedBilling` | `sdk-map.md` §Getting a client |
| Options | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | `sdk-map.md` |
| Auth | HTTP Basic — `BasicAuthCredentials` (`Username` = API key, `Password` = literal `"x"`) | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` §Servers & auth; `map/operations/*` notes |
| Environment enum | `ServerEnvironment.Us` (default) / `.Eu` | `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| Base URL override | `options.Server.Production.Us.BaseUrl` / `.Site`; also `Maxio:BaseUrl` binding if app uses it | — | `sdk-map.md` §Servers & auth |
| Retry/options | `RetryOptions` (required members; use `RetryOptions.Default()` start) | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` §Getting a client |

### Operations — signatures, envelopes, errors

**Convention:** parameter names literal; `ct` = cancellation token; nullable params with no default must be passed explicitly (pass `null`). All operations throw; no `…Result` variants exist in this SDK (`sdk-map.md` §Error-handling model).

#### A. Product family + products (find plan handles)

| Controller | Method signature (verbatim params) | Returns | Error (Case) | Source (map page) |
|---|---|---|---|---|
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` | `ProductFamilyResponse` | Case B `SdkException<RawError>` | `map/operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<ProductResponse>` | Case A `SdkException<ListProductsForProductFamilyError>` (accessors `TryGetString(out string)` [404] + `TryGetRawError`) | `map/operations/ProductFamilies.md` |
| `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `ProductResponse` | Case B `SdkException<RawError>` | `map/operations/Products.md` |
| `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<ProductResponse>` | Case B `SdkException<RawError>` | `map/operations/Products.md` |

**Envelope — `ProductResponse`** (`map/models/records-4-Su-We.md` via index; also referenced in `Products.md`): exactly one field `Product (product): Product?` (required in response context). Inner `Product` fields relevant: `Id`, `Handle` (`handle`), `Name`, `ProductFamily` (`product_family`), `PricePoint` / price fields.

**Envelope — `ProductFamilyResponse`**: `ProductFamily (product_family): ProductFamily?` (`map/operations/ProductFamilies.md` notes response contains Product Family object; can be referenced by id or `handle:my-family`).

#### B. Components (subscriptions use components by handle)

| Controller | Method signature | Returns | Error (Case) | Source |
|---|---|---|---|---|
| `client.Components` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<ComponentResponse>` | Case B `SdkException<RawError>` | `map/operations/Components.md` |
| `client.Components` | `FindComponent(string handle, CancellationToken ct = default)` | `ComponentResponse` | Case B `SdkException<RawError>` | `map/operations/Components.md` |

**Envelope — `ComponentResponse`**: `Component (component): Component?`. `Component` inner fields: `Id`, `Handle`, `Name`; use handle for `CreateSubscription.Components`.

#### C. Customer create / lookup (idempotent by email/username)

| Controller | Method signature | Returns | Error (Case) | Source |
|---|---|---|---|---|
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` | `IReadOnlyList<CustomerResponse>` | Case B `SdkException<RawError>` | `map/operations/Customers.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CustomerResponse` | Case A `SdkException<CreateCustomerError>` (`TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] + `TryGetRawError`) | `map/operations/Customers.md` |

**Lookup by email:** pass `q: "user@eshop.com"` (notes: "Search by an email"). Verify exact match (email / reference) before creating.

**Envelope — `CustomerResponse`**: `Customer (customer): Customer?` (`map/models/records-2-Cr-Ne.md` line 43). Inner `Customer` fields: `Id (id): int?`, `Email (email): string?`, `Reference (reference): string?`, `FirstName`, `LastName`, `Organization`, `Address`, etc.

**Request — `CreateCustomerRequest`** (`map/models/records-2-Cr-Ne.md`): wrapper `Customer (customer): CustomerAttributes !req` — so construct `new CreateCustomerRequest { Customer = new CustomerAttributes { Email = …, FirstName = …, Reference = … } }`.

**Request inner — `CustomerAttributes`** (`map/models/records-2-Cr-Ne.md` line 35): `FirstName`, `LastName`, `Email`, `Organization`, `Reference`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Verified`, `TaxExempt`, `Metafields`, etc.

#### D. Subscription create (post to `/subscriptions.json`)

| Controller | Method signature | Returns | Error (Case) | Source |
|---|---|---|---|---|
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | Case A `SdkException<CreateSubscriptionError>` (`TryGetErrorListResponse1(out ErrorListResponse1)` [422] + `TryGetRawError`) | `map/operations/Subscriptions.md` |

**Request — `CreateSubscriptionRequest`** (`map/models/records-2-Cr-Ne.md` line 21): wrapper `Subscription (subscription): CreateSubscription !req`.

**Inner — `CreateSubscription`** (`map/models/records-2-Cr-Ne.md` line 17; fields listed verbatim from record page): key fields for this plan:
- `ProductHandle (product_handle): string?` — use `"eshop-pro"` or `"basic-plan"`
- `ProductId (product_id): int?` — alternative to handle
- `CustomerId (customer_id): int?` — existing customer id (preferred; from `Customer.Id`)
- `CustomerReference (customer_reference): string?` — alternative lookup key
- `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` — component allocations by handle/id
- `DeferSignup (defer_signup): bool? = false` — keep `false`
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — leave null / default; not set to enforce no-payment-method-required
- `NextBillingAt (next_billing_at): DateTimeOffset?`
- `Reference (reference): string?`
- `CustomerAttributes (customer_attributes): CustomerAttributes?` — if creating customer inline (not needed if pre-creating)
- `Group (group): GroupSettings?`
- `Currency (currency): string?`

**No trial / no payment-method-required:** the brief asks “no trial/payment-method-required.” The SDK does not expose a direct “no_payment_required” bool; the contract is: do not pass trial-related fields (`TrialEndsAt` not on `CreateSubscription`; trial is a product-level setting), and do not pass `PaymentProfileAttributes` / `CreditCardAttributes` / `PaymentProfileId` unless needed. Confirm with live traffic whether a 422 is returned when product requires payment method (map Notes on `CreateSubscription` reference “Payment information may be required… depending on the options for the Product”). **UNVERIFIED:** whether the sandbox product `eshop-pro` allows subscription creation without a payment profile — defensive directive: try without `PaymentProfileAttributes`; if 422, fall back to creating a tokenized profile via `PaymentProfiles` (outside this sheet) or set product option “no payment required” in sandbox UI.

**Sub-component request `CreateSubscriptionComponent`**: use handle (`ComponentHandle`) + quantity if needed; check `map/models/enums.md` / records for exact fields (not fully expanded here — load `dotnet-models` before building payload).

**Envelope — `SubscriptionResponse`** (`map/models/records-4-Su-We.md` line 66): `Subscription (subscription): Subscription?`.

**Inner `Subscription`** (from `map/models/records-3-Of-Su.md` line 156; key read fields for confirmation):
- `State (state): SubscriptionState?` — `SubscriptionState` enum (`map/models/enums.md` line 96): `Active`, `Trialing`, `PastDue`, `Canceled`, `Expired`, `Suspended`, `OnHold`, `Unpaid`, `Pending`, `AwaitingSignup`, `TrialEnded`, etc.
- `Product (product): Product?` — includes `Handle`, price fields (`ProductPriceInCents`, or via price-point)
- `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — next billing assessment
- `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` — end of current billing period
- `NextBillingAt` not directly on `Subscription` but derived from assessment / period; read from preview if needed (`PreviewSubscription` — outside scope but noted)
- `Customer (customer): Customer?` — confirm linked customer id / email
- `Id (id): int?`

#### E. List customer subscriptions

| Controller | Method signature | Returns | Error (Case) | Source |
|---|---|---|---|---|
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | Case B `SdkException<RawError>` | `map/operations/Customers.md` |

No pagination parameters; returns all customer subscriptions.

---

## 3. Trap notes (load companion skills before each step — do not resolve inline)

> ⚠ Step 1 (client & auth) — `BasicAuthCredentials` lives in `MaxioAdvancedBilling.Core.Authentication.Basic`; `ServerEnvironment` in `MaxioAdvancedBilling.Servers`; `RetryOptions` in `MaxioAdvancedBilling.Core.Configuration`. Do not assume a single `using MaxioAdvancedBilling;` covers all. **MUST load `dotnet-client-initialization`** and `dotnet-authentication`.
>
> ⚠ Step 2 (list/find) — `ListCustomers` takes 7 nullable query params; named arguments required (`dateField:`, `q:`) because some have no C# default and mis-bind positionally. `q` is the email search. **MUST load `dotnet-calling-endpoints`**.
>
> ⚠ Step 3 (models / requests) — `CreateSubscriptionRequest` wraps `CreateSubscription`; `CreateCustomerRequest` wraps `CustomerAttributes`; `SubscriptionResponse` wraps `Subscription`. Unions (`OfferId`, `Amount2`) build via factory / implicit conversion, read via `TryGet…`. Enums (`SubscriptionState`, `SubscriptionStateFilter`, `CollectionMethod`) are `StringEnum<T>` — build with `SubscriptionState.FromValue("active")` or members, never C# enum cast. **MUST load `dotnet-models`** before constructing any payload.
>
> ⚠ Step 4 (error boundary) — `CreateSubscription` is Case A (`SdkException<CreateSubscriptionError>`); `ListCustomerSubscriptions` / `ListCustomers` / `ListProducts` / `ReadSubscription` are Case B (`SdkException<RawError>`). Two `JsonException` directions must be handled separately: a drifted 2xx body (missing `required`) surfaces as `JsonException`, not `SdkException`; a non-2xx body that doesn't match `{Op}Error` throws `JsonException` and destroys the HTTP status. **MUST load `dotnet-error-handling`** before writing the catch ladder (see §4 of this sheet).
>
> ⚠ Step 5 (resilience / config) — `Timeout` is per-attempt, not total; `HttpMethodsToRetry` gates only status-triggered retries (a `503` on POST is not status-retried, but transport `HttpRequestException` retries every verb including POST — non-idempotent writes can execute twice; `MaxRetries` floor is 1, `0` rejected). `Server.Production.Us.BaseUrl` overrides the subdomain-derived URL. **MUST load `dotnet-configuration-resilience`**.
>
> ⚠ Step 6 (testing) — the `HttpClient` constructor argument is the test seam; match project framework (xUnit/NUnit) and assertion style. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING (load BEFORE implementation starts — not resolved here)

- `dotnet-client-initialization` — client construction / DI / `HttpClient` lifetime (Step 1)
- `dotnet-authentication` — Basic auth pattern (`Username` = API key, `Password` = `"x"`) (Step 1)
- `dotnet-calling-endpoints` — named args, optional param binding, cancellation token `ct` (Steps 2–5)
- `dotnet-models` — request/response envelopes, `StringEnum<T>`, unions (`TryGet…`), required/init-only setters (Steps 3–5)
- `dotnet-error-handling` — Case A (`TryGet…`) vs Case B (`RawError`), `JsonException` two directions, no `Result` variants (Step 4; **mandatory** — boundary written early)
- `dotnet-configuration-resilience` — retries/backoff, `Timeout` semantics, base-URL override, pagination (`page`/`perPage`) (Steps 1, 5)
- `dotnet-testing` — seam (`HttpClient`), faking SDK, error-path assertions (Step 6)

**Mandatory error-boundary caveat (from `dotnet-error-handling` — include in boundary code):**
- A drifted / malformed 2xx body (missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape.
- A non-2xx body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — mapping every `JsonException` to 5xx then reports a deterministic rejection as an outage, and retrying 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

- **Assumption (your call — not in map):** The app's `PublicApi` project already has `AsadAli.AdvancedBilling.Sdk` referenced; if not, add via `dotnet add package AsadAli.AdvancedBilling.Sdk` (package id ≠ namespace).
- **Assumption (your call):** `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` come from config (not hard-coded); `MAXIO_DEFAULT_PRODUCT_FAMILY` is used as a fallback lookup handle/ID when `eshop-subscribe` isn't resolved.
- **Assumption (your call):** Customer lookup by email uses `ListCustomers(q: email)`; the map does not expose a direct `GetCustomerByEmail` — exact match verification is the implementer's responsibility.
- **Blocker / UNVERIFIED:** Whether the sandbox product family `3023074` / handles `eshop-pro` and `basic-plan` allow subscription creation without a payment profile / with `CustomerAttributes` only. The SDK `CreateSubscription` Notes indicate payment info may be required depending on product options. **Defensive directive:** attempt creation without payment attributes first; on 422 with payment-required message, either configure product in sandbox UI for no-payment-required, or create a minimal `PaymentProfile` via `client.PaymentProfiles` before subscribing (outside this sheet). Label `UNVERIFIED` until live traffic confirms.
- **Blocker / YOUR CALL:** Component handles to include in subscription (`Components` list) are not specified in the brief; the implementer must supply them from `FindComponent` / `ListComponentsForProductFamily` results.
- **No map gap found** for any named operation; all signatures and envelope shapes are cited above. If a compiled name fails, re-read the single source file the map row names (e.g., `Api/Subscriptions.cs`, `Models/CreateSubscription.cs`) per `maxio-getting-started`.

---

*Sheet prepared from bundled SDK map (`sdk-map.md` + `map/operations/*.md` + `map/models/*.md`) and companion skills. Every operation row cites its map page; no claim relies on training-data memory. Clone path not included (per rules); source consulted only if needed (not required here — all names verified by lookup).*