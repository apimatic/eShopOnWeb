# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Plan-only. No project code has been changed. Repo-convention slots (endpoint base classes, folder layout, JWT plumbing in `src/PublicApi`) are intentionally generic — fill them from the repo's existing patterns.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|------|---------------------|
| 1 | Add NuGet package + `Maxio:` config binding + client DI registration | — (client construction only) |
| 2 | `GET /api/subscription-plans` — list plans in the configured family | `ProductFamilies.ListProductFamilies` → `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — idempotent subscribe | `Customers.ReadCustomerByReference` → (`Customers.CreateCustomer`) → `Subscriptions.FindSubscription` → (`Subscriptions.CreateSubscription`) |
| 4 | `GET /api/my-subscriptions` — list caller's subscriptions | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 5 | Error boundary + status mapping for all three endpoints | error rows below |
| 6 | Tests for the SDK-touching layer | — (test seam: `HttpClient` ctor arg) |

Customer `reference` convention: the eShopOnWeb user id (from the JWT), e.g. `"eshop-user-{userId}"`. Subscription `reference` convention: `"eshop-user-{userId}-{productHandle}"` — deterministic, so a retried subscribe is detectable before any create call.

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

### 2.1 Package, client, auth, servers (map: `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` version **1.0.2** (`dotnet add package AsadAli.AdvancedBilling.Sdk`) |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` — only ctor |
| Options | `MaxioAdvancedBillingClientOptions` { `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials? }` |
| Auth | HTTP Basic — `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (password is the literal `"x"`) |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `.Eu` → `https://{site}.ebilling.maxio.com` |
| Subdomain | `options.Server.Production.Us.Site = <Maxio:Subdomain>` (fills `{site}`) |
| Base-URL override | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` — verbatim override, use instead of the derived URL when `Maxio:BaseUrl` is set |
| DI helper | `services.AddMaxioAdvancedBillingClient(o => { … })` exists (`ServiceCollectionExtensions.cs`) |
| API groups | properties on the client: `client.ProductFamilies`, `client.Customers`, `client.Subscriptions` |

### 2.2 Operations

| Step | Controller · signature (verbatim) | Request model | Response envelope → fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| 2 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must be passed explicitly (pass `null`) | — | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (nullable) → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<RawError>` | none |
| 2 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField…include` must be passed explicitly (pass `null`); `productFamilyId` is the **numeric id as string** from the family lookup (handle support on this path param is not stated in the map — do not pass a handle) | — | `IReadOnlyList<ProductResponse>` → `.Product` (**required**) → `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` (filter out archived) | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual `page`+`perPage` (default 20; 2 seeded plans fit one page) |
| 3, 4 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` (query `reference`) | — | `CustomerResponse` → `.Customer` (**required**) → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName`, `LastName` | **Case B** `SdkException<RawError>` — 404 = customer does not exist → create path | none |
| 3 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest` { `Customer (customer): CreateCustomer` **!req** }; `CreateCustomer` { `FirstName (first_name): string` **!req**, `LastName (last_name): string` **!req**, `Email (email): string` **!req**, `Reference (reference): string?` ← set to the user-id reference } | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none |
| 3 | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed explicitly (query `reference`) | — | `SubscriptionResponse` → `.Subscription` (nullable) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404 = no such subscription → create path] · `TryGetRawError(out RawError)` | none |
| 3 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest` { `Subscription (subscription): CreateSubscription` **!req** }; `CreateSubscription` fields used: `ProductHandle (product_handle): string?` ← plan handle, `CustomerReference (customer_reference): string?` ← same user reference (alternative: `CustomerId (customer_id): int?`), `Reference (reference): string?` ← deterministic idempotency key, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` ← **required in practice** (see below), `NetTerms (net_terms): string?` ← optional companion documented as "(on invoice billing)… 0 (due immediately) to 180". **Live-verified:** a create with no payment fields was rejected 422 `["No payment method was on file for the $299.00 balance"]` despite the seeded products' `RequireCreditCard`/`RequestCreditCard` flags being false; setting `payment_collection_method` to `invoice` (`CollectionMethod.Invoice`) succeeded, and the provider normalized/echoed it as `remittance` on the read model (site is on Relationship Invoicing architecture — valid values there: `remittance`, `automatic`, `prepaid`) | `SubscriptionResponse` → `.Subscription` (nullable) → `Id`, `State`, `Product.Name`/`Product.Handle`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)`; `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **!req** | none |
| 4 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` (nullable) → same `Subscription` fields as above | **Case B** `SdkException<RawError>` | none |

Map citations: `operations/ProductFamilies.md`, `operations/Customers.md`, `operations/Subscriptions.md`, `records-1-Ac-Cr.md` (CreateCustomer/CreateCustomerRequest), `records-2-Cr-Ne.md` (CreateSubscription, CreateSubscriptionRequest, Customer, CustomerResponse, CustomerErrorResponse1, ErrorListResponse1, ListProductsFilter), `records-3-Of-Su.md` (Product, ProductFamily, ProductFamilyResponse, ProductResponse, Subscription), `records-4-Su-We.md` (SubscriptionResponse).

### 2.3 Read-model note — "next billing date"

The `Subscription` read model has **no `next_billing_at` field** (`next_billing_at` exists only on the *create* request). Surface next-billing as `CurrentPeriodEndsAt (current_period_ends_at)`; `NextAssessmentAt (next_assessment_at)` is the assessment companion. Price shown to the user: `ProductPriceInCents` on the subscription, or `Product.PriceInCents` + `Interval`/`IntervalUnit` from the plan list. The read model also echoes `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — surfaced on the API DTO as `paymentCollectionMethod` (expect the provider-normalized value, e.g. `remittance`, not necessarily the value sent).

### 2.4 Enum values needed (map: `enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>`, **not** C# enums — use static members, e.g. `SubscriptionState.Active`)

| Enum | Members (wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — only if the plan later sets `PaymentCollectionMethod`; not required for the seeded no-card signup |

### 2.5 Error model (map: `sdk-map.md` error-handling section)

- All operations are **throw-only** — no `…Result` no-throw variants exist in this SDK.
- Case A: `SdkException<{Op}Error>`, `ex.Error.TryGet…(out …)` per the table above, plus inherited `TryGetRawError(out RawError)` fallback. Case B: `SdkException<RawError>` directly.
- `RawError`: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Expected statuses: 404 on `ReadCustomerByReference` (Case B raw — check `StatusCode`), 404 on `FindSubscription` (Case A `TryGetNoContent`), 422 on `CreateCustomer` / `CreateSubscription` (typed accessors above).
- ⚠ `CustomerErrorResponse1.Errors` is typed as the shared `Errors` record whose only fields are `PerPage (per_page)` / `PricePoint (price_point)` — that shape does not match customer-validation errors, so what a live 422 from `CreateCustomer` actually populates is **UNVERIFIED**. Defensive directive: extract messages best-effort from the typed payload, fall back to `TryGetRawError` → `ReadAsString()`, and never let payload parsing mask the 422 status.
- Duplicate-reference races: `reference` is unique per customer; a double-click is handled by lookup-before-create, and a true race surfaces as 422 from `CreateCustomer`/`CreateSubscription` — treat that 422 as "already exists", re-read by reference, and return the existing entity.

### 2.6 Idempotency

No SDK-level idempotency keys exist on any create signature (map-verified). Idempotency is application-layer: deterministic `reference` values + `ReadCustomerByReference` / `FindSubscription` before any create, plus the 422-then-re-read fallback above.

## 3. File-by-file plan (src/PublicApi + wiring)

| File (repo-convention slots in *italics*) | Contents |
|---|---|
| `src/PublicApi/*appsettings / Program.cs*` | Bind `MaxioOptions` from section `Maxio` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` optional); validate `ApiKey`/`Subdomain`/`ProductFamilyHandle` non-empty at startup. Values from env vars / user-secrets — nothing hard-coded |
| `src/PublicApi/*Program.cs or a ServiceCollection extension*` | Register the Maxio client per §2.1 (auth, `Site` from `Subdomain`, `BaseUrl` override when set) via the SDK's DI helper or an equivalent factory |
| `src/PublicApi/*Endpoints/SubscriptionPlans/List.cs*` (or the project's endpoint convention) | `GET /api/subscription-plans`: `ListProductFamilies(null,null,null,null,null)` → first family with `Handle == Maxio:ProductFamilyHandle` → `ListProductsForProductFamily(family.Id.ToString(), null×8, page: 1, perPage: 100)` → map to plan DTO `{ id, handle, name, description, priceInCents, interval, intervalUnit }`, skip `ArchivedAt != null` |
| `src/PublicApi/*Endpoints/Subscriptions/Create.cs*` | `POST /api/subscriptions` body `{ productHandle }`: identity from JWT → reference; `ReadCustomerByReference` → 404 ⇒ `CreateCustomer`; `FindSubscription(ref)` → found ⇒ return existing (idempotent); else `CreateSubscription` with `ProductHandle`, `CustomerReference`, `Reference`, `PaymentCollectionMethod`; return `{ id, state, productHandle, productName, priceInCents, currentPeriodEndsAt, paymentCollectionMethod }` |
| `src/PublicApi/*Endpoints/Subscriptions/ListMine.cs*` | `GET /api/my-subscriptions`: `ReadCustomerByReference` → 404 ⇒ empty list; else `ListCustomerSubscriptions(customer.Id)` → map each to the same DTO |
| `src/PublicApi/*error mapping (middleware or per-endpoint)*` | One translation layer: `SdkException<RawError>` → status from `RawError.StatusCode`; typed `SdkException<…Error>` → typed accessor first, `TryGetRawError` fallback; 404 → NotFound/empty, 422 → Conflict/BadRequest with messages, else 502 |
| `tests/*PublicApi tests*` | Fake the `HttpClient` seam for find-or-create, double-subscribe, and 422-race paths |

## 4. Trap notes (hazards — load the named skill before writing that step's code)

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has lifetime requirements the ctor signature does not show; registering it wrong sockets-exhausts under load. **MUST load `dotnet-client-initialization`**.
- ⚠ Step 1 (auth) — when credentials must be set relative to client construction, and how they flow from configuration without hard-coding, is not visible from the options shape. **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–4 (every call) — list/search operations take many nullable params with no C# default that mis-bind in positional calls; call with named arguments. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Steps 2–4 (models) — enums are `StringEnum<T>` not C# enums, records are immutable with `required` init members, and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`**.
- ⚠ Step 5 (error boundary) — which operations are Case A vs Case B differs per operation (see §2.2), `TryGetRawError` is not a catch-all on typed errors, and every operation is throw-only. **MUST load `dotnet-error-handling`**.
- ⚠ Step 5 (error boundary) — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- ⚠ Step 1/5 (resilience) — whether a failed subscribe `POST` can be re-sent by the SDK's retry layer, and what the `Timeout` option actually bounds, interact directly with this plan's application-layer idempotency; the option names alone do not say. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 6 (tests) — the SDK's test seam is the `HttpClient` constructor argument, not mocking the client surface. **MUST load `dotnet-testing`**.

## 5. REQUIRED READING (load all before implementation starts — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — Step 1 (client construction & DI)
- `dotnet-authentication` — Step 1 (Basic credentials from config)
- `dotnet-calling-endpoints` — Steps 2–4 (named-argument calling convention)
- `dotnet-models` — Steps 2–4 (records, StringEnum, required members)
- `dotnet-error-handling` — Step 5 (Case A/B boundary, JsonException hazards)
- `dotnet-configuration-resilience` — Steps 1 & 5 (retry/timeout behaviour vs idempotency)
- `dotnet-testing` — Step 6 (HttpClient seam)

## 6. Assumptions & Blockers

**Assumptions**
- eShopOnWeb users always have an email + display name derivable from the JWT/identity store to satisfy `CreateCustomer`'s required `FirstName`/`LastName`/`Email`; if only email exists, plan to split or placeholder names deliberately.
- `ListProductsForProductFamily.productFamilyId` is passed the family's **numeric id** (as string) obtained from `ListProductFamilies`; the map does not state handle support on this path parameter, so the plan does not rely on it.
- "Next billing date" is served as `current_period_ends_at` (the read model has no `next_billing_at` — see §2.3).
- The metered `api-call` component is out of scope for these three endpoints (no usage-reporting endpoint was requested); `SubscriptionComponents` operations are not planned.
- EU hosting is not needed; `ServerEnvironment.Us` is used. `Maxio:BaseUrl` covers any dev/mock redirect.
- The seeded products' payment-method-not-required flags (`RequireCreditCard`/`RequestCreditCard` false) do **not** permit a no-payment-field signup for a paid product (live-verified: 422 "No payment method was on file for the $299.00 balance"); paid signup requires a non-`automatic` collection method — `invoice` sent, normalized to `remittance` by the provider on this Relationship-Invoicing site.
- One active subscription per (user, product) is the intended idempotency granularity.

**Blockers** — none.
