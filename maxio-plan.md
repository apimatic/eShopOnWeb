# Maxio Advanced Billing — eShopOnWeb PublicApi integration plan

Path dictated by brief: `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-008\repo\maxio-plan.md` (repo root).

No project file edited yet; agent returned plan only. Hard gate (integrate-maxio Step 1) satisfied — sheet exists and has been read; implementation is for the main agent after this return.

---

## 1. Scope & sequence

All Maxio interaction via SDK only (`AsadAli.AdvancedBilling.Sdk`, namespace `MaxioAdvancedBilling`). Project target: `src/PublicApi/PublicApi.csproj`.

Sequence (each names SDK operations):

1. **Client init / auth / server** — register `MaxioAdvancedBillingClient` with `BasicAuth` + `ServerEnvironment` / `ServerOptions`; bind env vars to config (no hard-coding); set retry/timeout via `RetryOptions` (MUST load `dotnet-configuration-resilience`).
2. **Product list** — `client.Products.ListProducts(...)` → `GET /api/subscription-plans`; returns `IReadOnlyList<ProductResponse>`. Filter to stable family `eshop-subscribe` (family handle from config `Maxio:ProductFamilyHandle` = `eshop-subscribe`); expose `eshop-pro` / `basic-plan` handles.
3. **Idempotent customer** — `client.Customers.ReadCustomerByReference(reference)` (search by eShopOnWeb user id as `reference`); if missing, `client.Customers.CreateCustomer(...)` with `CreateCustomerRequest` where `Customer.Reference = userId`. Note: `CreateCustomer` notes say reference must be unique.
4. **Idempotent subscription** — `client.Subscriptions.CreateSubscription(...)` with `CreateSubscriptionRequest` setting `Subscription.CustomerReference = userId` (or `CustomerId` after lookup) and `Subscription.ProductHandle` selected from step 2; no trial/setup fee; card not required (`RequireCreditCard` false on product). Use `Reference` on subscription for idempotency if needed.
5. **My-subscriptions list** — `client.Subscriptions.ListSubscriptions(...)` with `state` / customer filter derived from JWT identity (`customer_reference`); returns `IReadOnlyList<SubscriptionResponse>`.
6. **Error boundary / JWT identity** — catch ladder around every SDK call; identity from token (not SDK); endpoint auth JWT (not SDK basic auth). MUST load `dotnet-error-handling`; include both `JsonException` caveats verbatim.

End points under `src/PublicApi` (controllers/routes):
- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`
JWT-authenticated; identity from token (application's role, not SDK).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

All signatures from map pages: `operations/Products.md`, `operations/Customers.md`, `operations/Subscriptions.md` (accessor properties `client.Products`, `client.Customers`, `client.Subscriptions`; source files `Api/Products.cs`, `Api/Customers.cs`, `Api/Subscriptions.cs`). All request/response/envelope fields from `map/models/records-*.md` (namespace `MaxioAdvancedBilling.Models`); enum values from `map/models/enums.md`.

### 2.1 List products — `client.Products.ListProducts`

- Controller property: `client.Products` (source `Api/Products.cs`)
- Signature (literal param names, nullable unless noted): `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`
  - 8 leading nullable params → **must pass explicitly** (`null` to skip); defaults `page=1`, `perPage=20`
- Request model (filter): `ListProductsFilter` (wire `filter`); `BasicDateField` (wire `date_field`); `ListProductsInclude` (wire `include`)
- Response envelope: `IReadOnlyList<ProductResponse>` (no wrapper around list); read inner payload via `ProductResponse.Product`
- Envelop shape `ProductResponse`: `Product (product): Product !req` (from records page; source `Models/ProductResponse.cs`)
- Error: `SdkException<RawError>` — **Case B** (from `operations/Products.md` line 36)
  - Accessors: `.Error.StatusCode` (`HttpStatusCode`), `.Error.ReadAsString()`, `.Error.ReadAsBytes()`, `.Error.ReadAsJson<T>()`
  - No typed `TryGet…` for this operation
- Pagination: manual `page`/`perPage`; no `NextPage` helper in SDK
- Source: `operations/Products.md` · `models/records-*.md` (ProductResponse) · `models/enums.md` (BasicDateField, ListProductsInclude)

Fields read from `Product` (record; wire names): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `ProductFamilyId (product_family_id): int?`, `ProductFamilyName (product_family_name): string?`, `ProductFamilyHandle (product_family_handle): string?`, `Description (description): string?`, `RequireCreditCard (require_credit_card): bool?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `Archived (archived): bool?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` — only `Handle`, `Name`, `ProductFamilyHandle`, `RequireCreditCard` needed for this integration.

### 2.2 Create customer — `client.Customers.CreateCustomer`

- Controller: `client.Customers` (source `Api/Customers.cs`)
- Signature: `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → must pass explicitly
- Request model: `MaxioAdvancedBilling.Models.CreateCustomerRequest` (namespace from record page)
  - `Customer (customer): CreateCustomer !req`
  - `CreateCustomer` wire/fields (required unless `?`): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `TaxExempt (tax_exempt): bool?`, `ParentId (parent_id): int?`
- For idempotency: set `Reference = eShopOnWeb user id` (app's identity); `Email` from token/identity; `FirstName`/`LastName` from token or defaults (YOUR CALL — not in map)
- Response envelope: `CustomerResponse` — `Customer (customer): Customer !req`
- Error: `SdkException<CreateCustomerError>` — **Case A** (typed)
  - Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]
- Source: `operations/Customers.md` · `models/records-1-Ac-Cr.md` (CreateCustomerRequest, CustomerResponse, CreateCustomer) · `models/enums.md`

### 2.3 Create subscription — `client.Subscriptions.CreateSubscription`

- Controller: `client.Subscriptions` (`Api/Subscriptions.cs`)
- Signature: `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, must pass
- Request model: `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`
  - `Subscription (subscription): CreateSubscription !req`
- `CreateSubscription` key fields (from `messages/records-2-Cr-Ne.md`): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `CalendarBilling (calendar_billing): CalendarBilling?`, `DeferSignup (defer_signup): bool? = false`, `AgreementAcceptance (agreement_acceptance): AgreementAcceptance?`
- Idempotency / no-card / no-trial / no-setup-fee: pass `ProductHandle` selected from step 2 (e.g. `eshop-pro` or `basic-plan` — YOUR CALL); `CustomerReference = userId` (avoids needing `CustomerId` lookup); do NOT pass `CreditCardAttributes` / `PaymentProfileAttributes` (card not required — confirm product `RequireCreditCard` false via step 2); do NOT pass trial fields (`TrialType` etc not on CreateSubscription directly — trial set on product, not subscription creation).
- Response envelope: `SubscriptionResponse` — `Subscription (subscription): Subscription !req`
- Error: `SdkException<CreateSubscriptionError>` — **Case A**
  - Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]
- Pagination: none
- Source: `operations/Subscriptions.md` · `models/records-2-Cr-Ne.md` (CreateSubscriptionRequest, CreateSubscription, SubscriptionResponse, Subscription)

### 2.4 List subscriptions (my-subscriptions) — `client.Subscriptions.ListSubscriptions`

- Controller: `client.Subscriptions`
- Signature: `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`
  - 14 nullable leading params → must pass explicitly; defaults `page=1`, `perPage=20`
- Filter to calling user: use `metadata` or query by derived `customer_reference` if endpoint supports; else filter in-app after fetch (YOUR CALL — not in map). The SDK operation accepts `SubscriptionStateFilter? state` (`subscription_state`); `SubscriptionDateField? dateField`; `SubscriptionSort? sort`; `SubscriptionListInclude? include`.
- Response: `IReadOnlyList<SubscriptionResponse>`; inner `SubscriptionResponse.Subscription`
- Error: `SdkException<RawError>` — **Case B**
  - Accessors: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsBytes()`, `.Error.ReadAsJson<T>()`
- Pagination: manual `page`/`perPage`
- Source: `operations/Subscriptions.md` · `models/enums.md` (SubscriptionStateFilter, SubscriptionDateField, SubscriptionSort, SubscriptionListInclude, SortingDirection)

### 2.5 Client construction / auth / server / env bindings (contract facts, not implementation design)

- Package: `AsadAli.AdvancedBilling.Sdk` (`nuget`); root namespace `MaxioAdvancedBilling` (`sdk-map.md` line 11)
- Client constructor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (only constructor; source `MaxioAdvancedBillingClient.cs`)
- Options type (namespace root): `MaxioAdvancedBillingClientOptions`
  - Properties (names exact): `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server` (`MaxioAdvancedBilling.Servers.ServerOptions`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`)
- Auth: Basic — `Username = API key`, `Password = literal "x"` (`sdk-map.md` lines 33, 44)
- Server / environment: `ServerEnvironment.Us` (default) or `Eu`; site subdomain via server node / base URL. The user's site is sandbox `cp-exp-1`; binding `Maxio:Subdomain` binds subdomain, `Maxio:BaseUrl` optional override.
- Env binding config (binding keys, not env var names — per rules): `Maxio:ApiKey` → `BasicAuth.Username`; `Maxio:Subdomain` → server-node / subdomain; `Maxio:ProductFamilyHandle` → `eshop-subscribe` (stable family handle used for list/selection); `Maxio:BaseUrl` → optional `ServerOptions` / base URL override. No hard-coding of subdomain, key, family, or base URL.
- Retry: `RetryOptions` lives in `MaxioAdvancedBilling.Core.Configuration`; members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`; all `required`; build from `RetryOptions.Default()` or full init.
- Cancellation token parameter in every call is literally named `ct`.
- Source: `sdk-map.md` (Getting a client / Client construction / Error-handling model) · `MaxioAdvancedBillingClient.cs` · `MaxioAdvancedBillingClientOptions.cs` · `Core/Configuration/RetryOptions.cs` · `Core/Authentication/Basic/BasicAuthCredentials.cs` · `Servers/ServerEnvironment.cs`

---

## 3. Trap notes (attach to step; do NOT resolve — load the skill)

- ⚠ Step 1 (client registration) — `RetryOptions.Timeout` / `RetryOptions.Delay` / `RetryOptions.MaxRetries` do **not** bound the whole call and are **not** the `HttpClient.Timeout` you may register; `BasicAuth` is credentials, not the server node; subdomain selection is via `Server`/`Environment`, not `BasicAuth`. **MUST load `dotnet-configuration-resilience`** before wiring client and **MUST load `dotnet-authentication`** before wiring auth.
- ⚠ Step 2 (list products) — `ListProducts` has 8 nullable-leading params; passing `null` explicitly is required to skip; response is `IReadOnlyList<ProductResponse>` so read `.Product`; pagination is manual `page`/`perPage`. **MUST load `dotnet-calling-endpoints`** before first SDK call.
- ⚠ Step 3/4 (request payloads) — `CreateCustomerRequest` wraps `CreateCustomer`; `CreateSubscriptionRequest` wraps `CreateSubscription`; fields must match wire names (`customer_reference`, `product_handle`). `required` fields (`!req`) must be set; optional fields with no default can be omitted. **MUST load `dotnet-models`** before constructing any payload.
- ⚠ Step 5 (my-subscriptions) — `ListSubscriptions` has 14 nullable-leading params; filtering by user identity is an application decision; SDK does not inject JWT identity. **MUST load `dotnet-calling-endpoints`** and **MUST load `dotnet-configuration-resilience`** for pagination/retry.
- ⚠ Step 6 (error boundary) — every operation throws; there is no `…Result` variant. Case A vs B depends on operation; `ListProducts`/`ListSubscriptions`/`ReadCustomerByReference`/`CreateSubscription` error shapes differ. **MUST load `dotnet-error-handling`** before writing any try/catch.

---

## 4. REQUIRED READING (load before any implementation)

- `dotnet-calling-endpoints` — governs steps 2, 4, 5 (finding controllers, parameter order, named args `ct:`, envelopes, pagination).
- `dotnet-models` — governs steps 3, 4 (request model initializers, `required`/nullable, wire names, union access if used).
- `dotnet-authentication` — governs step 1 (Basic auth username/API key, password `"x"`, no rotation logic required here).
- `dotnet-client-initialization` — governs step 1 (`HttpClient` ownership, `AddMaxioAdvancedBillingClient`, `ServiceCollection` registration, `ServerOptions`).
- `dotnet-configuration-resilience` — governs step 1 (retry defaults, timeout semantics, base URL/server selection, pagination behavior; never assume timeout is per-attempt without reading).
- `dotnet-error-handling` — governs step 6 (Case A/B mechanics, `TryGet…` accessors, `SdkException<T>` catch ladder, `RawError` fallback; must include both `JsonException` rows below).
- `dotnet-testing` — if writing tests for integration layer (seam to fake, assert behavior not execution).

Mandatory `dotnet-error-handling` caveats (both directions, verbatim — boundary written early):

- A drifted or malformed **2xx** body (missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

Assumptions (your call / app design — not SDK contract):
- Endpoint routes (`/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions`) and JWT auth middleware are the application's existing convention; SDK knows only basic auth to Maxio.
- Identity for `user id` / `reference` comes from the JWT token claim; the exact claim name is YOUR CALL — not in map.
- Which product handle (`eshop-pro` vs `basic-plan`) is selected per call is YOUR CALL — not enforced by SDK; family filter `eshop-subscribe` comes from config binding `Maxio:ProductFamilyHandle`.
- Customer creation first vs subscription-with-embedded-customer is YOUR CALL — SDK supports both (`CreateSubscription` via `CustomerAttributes`; standalone `CreateCustomer` via reference). Idempotency via `reference` / `customer_reference` is the defensive choice.
- No trial / no setup fee / card-not-required are site/product settings (sandbox `cp-exp-1`); SDK sends what is requested, site enforces.
- `MAXIO_DEFAULT_PRODUCT_FAMILY` env binds to `Maxio:ProductFamilyHandle`; `MAXIO_SITE_SUBDOMAIN` binds to server-node/subdomain; `MAXIO_ENVIRONMENT` selects `ServerEnvironment` (US/EU default from env or `Us` default per map).
- The clone path / SDK source file paths are never exposed in this file or to the main agent; source references use only map page names (`operations/*.md`, `records-*.md`).

Blockers (none at plan time — all contract facts resolved from `sdk-map.md` + operations pages + records pages; only application-layer choices remain, labeled YOUR CALL):
- None that stop planning. The only open decisions (product selection per call, claim name for identity, whether to embed customer in subscription call) are application design and are explicitly marked YOUR CALL above.

---

## 6. Sources cited per row

| Row / fact | Source (map page / source file name only — no clone path) |
|---|---|
| SDK identity / package / namespace / constructor / options / auth / retry / server | `sdk-map.md` (§Getting a client, Client construction, Error-handling) · `Api/Products.cs` · `Core/Configuration/RetryOptions.cs` · `Core/Authentication/Basic/BasicAuthCredentials.cs` · `Servers/ServerEnvironment.cs` |
| `client.Products` / `ListProducts` signature / error / pagination / query params | `operations/Products.md` |
| `ProductResponse` / `Product` fields / wire names | `map/models/records-*.md` (ProductResponse / Product) |
| `client.Customers` / `CreateCustomer` / `ReadCustomerByReference` / `CreateCustomerRequest` / `CustomerResponse` | `operations/Customers.md` · `map/models/records-1-Ac-Cr.md` |
| `client.Subscriptions` / `CreateSubscription` / `ListSubscriptions` / signatures / errors | `operations/Subscriptions.md` |
| `CreateSubscriptionRequest` / `CreateSubscription` / `SubscriptionResponse` / `Subscription` fields | `map/models/records-2-Cr-Ne.md` |
| Enums (BasicDateField, ListProductsInclude, SubscriptionStateFilter, SubscriptionDateField, SubscriptionSort, SortingDirection, IntervalUnit, etc.) | `map/models/enums.md` |
| Response envelopes (all) | `map/models/records-*.md` (ProductResponse, CustomerResponse, SubscriptionResponse) |
| Error accessors (typed + raw) per operation | `operations/*.md` error cells + `sdk-map.md` (§Error-handling model) + `Core/ErrorResponse/ApiError.cs` · `Core/ErrorResponse/RawError.cs` |

No clone path appears; source references are filenames only, per rules.
