# Maxio Advanced Billing .NET SDK — Integration Plan

**Repo:** eShopOnWeb (`C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-002\repo`)  
**Public API:** `src/PublicApi` (ASP.NET Core Web API, JWT auth)  
**SDK:** `AsadAli.AdvancedBilling.Sdk` — root namespace `MaxioAdvancedBilling`  
**Environment:** Maxio sandbox — site `cp-exp-1`; product family handle `eshop-subscribe`; plans `eshop-pro` / `basic-plan`; component `api-call`.  
**Plan file (this):** `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-002\repo\maxio-plan.md`

> No project files edited; no code implemented yet — plan + contract sheet only.

---

## 1. Scope & sequence

| # | Endpoint | Direction | SDK operation(s) | Notes |
|---|---|---|---|---|
| 1 | `GET /api/subscription-plans` | Web API → Maxio | `client.Products.ListProducts(...)` (and `ReadProductByHandle` for handle lookups) | Returns plans (products) for family `eshop-subscribe` |
| 2 | `POST /api/subscriptions` | Web API → Maxio | `client.Customers.CreateCustomer(...)` (if new) → `client.Subscriptions.CreateSubscription(...)` | Customer idempotency must be handled by app (see §5) |
| 3 | `GET /api/my-subscriptions` | Web API → Maxio | `client.Customers.ListCustomers(q=...)` or `client.Customers.ReadCustomerByReference(...)` → `client.Customers.ListCustomerSubscriptions(customerId)` | Per-authenticated-user lookup |

Sequence rule: client created once (DI or factory); each endpoint calls SDK in order; error boundary catches `SdkException<T>` / `SdkException<RawError>`; no `…Result` no-throw variants exist (§4 error model).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits.

### 2.1 Auth / client / server

| Fact | Value / Source |
|---|---|
| Package | `AsadAli.AdvancedBilling.Sdk` (`sdk-map.md` top) |
| Root namespace | `MaxioAdvancedBilling` (`sdk-map.md`) |
| Controllers namespace | `MaxioAdvancedBilling.Api` (`sdk-map.md` namespaces) |
| Records namespace | `MaxioAdvancedBilling.Models` (`sdk-map.md`) |
| Enums namespace | `MaxioAdvancedBilling.Models.Enums` (`sdk-map.md`) |
| Error namespace | `MaxioAdvancedBilling.Errors` (`sdk-map.md`) |
| Client type | `MaxioAdvancedBillingClient` — constructor `MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` (`sdk-map.md` line 38) |
| Options type | `MaxioAdvancedBillingClientOptions` (`sdk-map.md` line 31) — members: `BasicAuth`, `Environment`, `Retry`, `Server` |
| Auth schema | Basic — `BasicAuthCredentials` (`Core/Authentication/Basic`) — `Username = <API key>`, `Password = literal "x"` (`sdk-map.md` line 33) |
| Env enum | `ServerEnvironment` (`Servers` namespace) — `Us` (default) / `Eu` (`sdk-map.md`) |
| Sandbox site | `cp-exp-1` — server base URL is **YOUR CALL — not in the map** (`sdk-map.md`; configure via `ServerOptions` / env) |
| Client registration | `services.AddMaxioAdvancedBillingClient(...)` or manual `new MaxioAdvancedBillingClient(httpClient, options)` (`sdk-map.md` lines 44–48) |

### 2.2 Operation rows (scope)

#### A. List products (plan list)

| | |
|---|---|
| Controller | `client.Products` (`map/operations/Products.md`) |
| Signature | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Required-but-nullable params (must pass explicitly) | `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include` (pass `null` to skip) |
| Request / query wire | `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `include_archived`, `include`, `page`, `per_page` (`map/operations/Products.md`) |
| Response envelope | `IReadOnlyList<ProductResponse>` — each item has `Product (product): Product !req` (`map/models/records-…` — `ProductResponse`) |
| Error case | B — `SdkException<RawError>`; accessors: `.Error.StatusCode` / `.Error.ReadAsString()` (`map/operations/Products.md`) |
| Pagination | manual `page` (1) + `perPage` (20) |
| Source | `map/operations/Products.md` |

Also used: `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse` (`map/operations/Products.md` line 52) for handle-based plan read (`eshop-pro` / `basic-plan`).

#### B. Create customer (idempotent lookup by app)

| | |
|---|---|
| Controller | `client.Customers` (`map/operations/Customers.md`) |
| Signature (create) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Request envelope | `CreateCustomerRequest` — `Customer (customer): CreateCustomer !req` (`map/models/records-1-Ac-Cr.md` line 125) |
| Inner model fields (relevant) | `CreateCustomer`: `FirstName`, `LastName`, `Email (email)`, `Organization`, `Reference (reference)`, `Address`, `City`, `State`, `Country`, `Phone`, `Verified`, `TaxExempt`, `Metafields` (`map/models/records-1-Ac-Cr.md` — `CreateCustomer` row) |
| Response envelope | `CustomerResponse` — `Customer (customer): Customer !req` (`map/models/records-2-Cr-Ne.md` line 43) |
| Error case | A — `SdkException<CreateCustomerError>`; accessors `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError` (fallback) (`map/operations/Customers.md`) |
| Source | `map/operations/Customers.md`, `map/models/records-1-Ac-Cr.md`, `map/models/records-2-Cr-Ne.md` |

**Idempotency — YOUR CALL:** The map notes "only validation restriction is that you may only create one customer for a given reference value" (`map/operations/Customers.md`). It does **not** say email is unique/enforced. Make the app idempotent (search by `email` via `ListCustomers(q=email)` or `ReadCustomerByReference`) before calling `CreateCustomer`; do not assume `CreateCustomer` is idempotent by email.

#### C. List by email / reference (find customer)

| | |
|---|---|
| Controller | `client.Customers` (`map/operations/Customers.md`) |
| Signature (list) | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` |
| Search / filter | `q` = email or reference (`map/operations/Customers.md`) |
| Signature (lookup by ref) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` → `CustomerResponse` |
| Pagination | `page`/`perPage` (default 50) |
| Source | `map/operations/Customers.md` |

#### D. List subscriptions by customer

| | |
|---|---|
| Controller | `client.Customers` (`map/operations/Customers.md`) |
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Error | B — `SdkException<RawError>` |
| Source | `map/operations/Customers.md` |

Also available: `client.Subscriptions.ListSubscriptions(...)` — 14 optional filter params (`state`, `product`, `productPricePointId`, `couponCode`, `dateField`, `startDate`, `endDate`, `metadata`, `direction`, `sort`, `include`) — manual pagination (`map/operations/Subscriptions.md`). Not needed for `/api/my-subscriptions` if using `ListCustomerSubscriptions`, but document for future.

#### E. Create subscription

| | |
|---|---|
| Controller | `client.Subscriptions` (`map/operations/Subscriptions.md`) |
| Signature | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Request envelope | `CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription !req` (`map/models/records-2-Cr-Ne.md` line 21) |
| Inner model key fields | `CreateSubscription`: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `PaymentProfileId (payment_profile_id): int?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `CreditCardAttributes`, `BankAccountAttributes`, `Components`, `Reference`, `Group`, `OfferId` (union), `Metafields`, `DeferSignup = false`, etc. (`map/models/records-2-Cr-Ne.md` — `CreateSubscription` row, line 17) |
| Payment method | **Not required by SDK** — notes say "Payment information may be required … depending on product options" (`map/operations/Subscriptions.md` line 33). Pass `payment_profile_id` or include `credit_card_attributes` only when required by product. |
| Response envelope | `SubscriptionResponse` — `Subscription (subscription): Subscription !req` (`map/models/records-2-Cr-Ne.md` / `SubscriptionResponse`) |
| Error case | A — `SdkException<CreateSubscriptionError>`; accessors `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError` (`map/operations/Subscriptions.md`) |
| Source | `map/operations/Subscriptions.md`, `map/models/records-2-Cr-Ne.md` |

Usage for this plan: pass `ProductHandle = "eshop-pro"` (or `"basic-plan"`) + `CustomerId` (from prior customer lookup) + optional `PaymentProfileId` / `CustomerAttributes` as needed.

---

## 3. Reled enums / types actually needed

From `map/models/enums.md` (cite page; do not reproduce full list):

- `CollectionMethod` — used if setting `PaymentCollectionMethod` on subscription create
- `SubscriptionState` / `SubscriptionStateFilter` — if filtering `ListSubscriptions`
- `SubscriptionListInclude` — if using `include` on list
- `BasicDateField` / `SubscriptionDateField` / `SortingDirection` — list filters
- `ServerEnvironment` — client config (see §2.1)

Union / AnyOf not needed for basic flows unless using `OfferId` (`OneOf`) or `ComponentId` variants — leave out of first pass.

---

## 4. Trap notes (must load companion skills — do NOT resolve inline)

> ⚠ Step 1 (client construction / DI) — retry/timeout options (`RetryOptions`) do **not** bound a whole SDK call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring client.
>
> ⚠ Step 2 (calling endpoints) — parameter names are literal (`ct`, `dateField`, not `cancellationToken`). Response envelopes wrap payload (`ProductResponse.Product`, `SubscriptionResponse.Subscription`) — reads go one level down. **MUST load `dotnet-calling-endpoints`** and `dotnet-models` before writing first call.
>
> ⚠ Step 3 (auth / server) — Basic auth username = API key, password = literal `"x"`; sandbox site `cp-exp-1` base URL must be set in `ServerOptions`; no default server is guaranteed for sandbox. **MUST load `dotnet-authentication`** and `dotnet-client-initialization`.
>
> ⚠ Step 4 (request payload construction) — `required` properties must be set in initializer; nullable fields are optional; enums are not C# enums (use `Type.FromValue("wire")` or static members from `enums.md`). **MUST load `dotnet-models`**.
>
> ⚠ Step 5 (error boundary) — `SdkException<TError>` is throw-only (no `Result` variants). Case A (typed) vs Case B (`RawError`) differ by operation; read via documented accessors, never `.ToString()`. **MUST load `dotnet-error-handling`** before writing catch ladder.
>
> ⚠ Step 6 (defensive coding / unverified) — whether the live wire payload for `SubscriptionResponse` exactly matches `Subscription` model fields (e.g., new fields added by Maxio) can only be confirmed by live traffic. Defensively extract best-effort from the envelope and fall back to generic message. **UNVERIFIED** for live payload drift — label in boundary logic.

---

## 5. Required reading (load BEFORE implementation starts)

Per `integrate-maxio` Step 1c and this sheet's trap notes. Load these skills; they carry defaults and worked examples this sheet deliberately does not reproduce.

- `dotnet-authentication` — governs Step 3 (Basic auth, credential rotation, `BasicAuthCredentials`).
- `dotnet-calling-endpoints` — governs Step 2 (controller access, parameter passing, response envelopes, `ct` naming).
- `dotnet-client-initialization` — governs Step 1 (client construction, `HttpClient` ownership, DI `AddMaxioAdvancedBillingClient`).
- `dotnet-configuration-resilience` — governs Step 1 (retries/timeouts, `RetryOptions`, `Timeout` semantics, server selection).
- `dotnet-error-handling` — governs Step 5 (Case A/B mechanics, `TryGet…` accessors, `JsonException` from 2xx vs non-2xx — see mandatory rows below).
- `dotnet-models` — governs Step 4 (record initialization, `required`, enums, wire names, union construction).
- `dotnet-testing` — governs verification (seam to fake, error-path coverage; load when writing tests).

**Mandatory error-boundary caveat (from `dotnet-error-handling` — include verbatim in first boundary, not later):**

> `System.Text.Json.JsonException` reaches the boundary from two directions with opposite handling:
> - A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization — **not** an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
> - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 6. Assumptions & Blockers

- **Assumption (app design — YOUR CALL):** `GET /api/subscription-plans` maps to `ListProducts` / `ReadProductByHandle`; filtering to family `eshop-subscribe` and plan handles `eshop-pro`/`basic-plan` is done by app query / handle lookup, not by SDK filter (SDK `ListProductsFilter` shape is `YOUR CALL — not in map`).
- **Assumption (auth identity):** The PublicApi uses JWT for its callers; the Maxio SDK uses independent Basic auth with a site-specific API key. How the API key is mapped per deployed site (sandbox vs prod) is the app's config, not the SDK's.
- **Blocker:** Whether the sandbox site's `cp-exp-1` base URL requires a custom `ServerOptions` URL or uses the default `ServerEnvironment.Us` endpoint must be verified against live connection; the map does not list sandbox URLs. Confirm ticket with operator / Maxio portal before first live call.
- **Blocker / unverified:** Whether `SubscriptionResponse` includes fields beyond `Subscription` (e.g., embedded `Customer`, `Product`) on the live wire for this SDK version is `UNVERIFIED`; defensive extraction (best-effort from `Subscription`, fallback to generic message) required.
- **Not a blocker — clarification only:** Customer creation idempotency by email is an application decision; SDK does not enforce email uniqueness (`map/operations/Customers.md` mentions only `reference` uniqueness). The implementer chooses search-then-create vs upsert logic.

---

## 7. Source citations per row (map pages only — never `api-reference.md`)

- Client/auth/config server: `sdk-map.md` (lines 22–78, 199+)
- Operations: `map/operations/Products.md`, `map/operations/Customers.md`, `map/operations/Subscriptions.md`
- Request/response records: `map/models/records-1-Ac-Cr.md`, `map/models/records-2-Cr-Ne.md`, `map/models/records-3-Of-Su.md`, `map/models/records-4-Su-We.md`
- Enums: `map/models/enums.md`
- Unions (if needed): `map/models/unions.md`
- Error model / accessors: `sdk-map.md` lines 81–116; per-operation error rows on operation pages
- Namespaces / package identity: `sdk-map.md` lines 10, 167–195

*No clone path is shown here; clone (if ever needed for a real gap) stays in system temp per agent rules and never appears in this file or in replies.*
