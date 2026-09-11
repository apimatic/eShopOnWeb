# Maxio Advanced Billing .NET SDK — Plan: subscription billing endpoints

Output path (dictated by brief): `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-001\repo\maxio-plan.md`
Mode: Plan only — no project-file edits, no build, no SDK source clone used (map sufficient; one int-vs-string note resolved from map signatures, no source open needed).
Sandbox: site subdomain `cp-exp-1`, US hosting (`ServerEnvironment.Us`); family handle `eshop-subscribe`; plan handles `eshop-pro` (id 7126957) and `basic-plan` (id 7126958); component handle `api-call` (id 3057195). Package `AsadAli.AdvancedBilling.Sdk`; root ns `MaxioAdvancedBilling` (package id ≠ ns).

---

## 1. Scope & sequence

Implementation order for `src/PublicApi` in the eShopOnWeb repo (application design is implementer's; SDK facts only below):

1. **Client & auth / server nodes** — wire `MaxioAdvancedBillingClient` with Basic auth and site `cp-exp-1`.
2. **List subscription plans by product family** — `client.ProductFamilies.ListProductsForProductFamily("eshop-subscribe", ...)` → `ProductResponse` list (plans = products in family).
3. **Retrieve customer by email (search)** — `client.Customers.ListCustomers(q: email, ...)` → `CustomerResponse`; extract first match (id needed for next steps). Idempotency handled in step 4.
4. **Create customer (idempotent by email)** — search first (step 3); if missing, `client.Customers.CreateCustomer(new CreateCustomerRequest { Customer = new CreateCustomer { Email = ..., FirstName = ..., LastName = ... } })`; use `reference` = email to make the create itself idempotent per map Notes (only reference is enforced unique, not email — see Blocker / Assumption).
5. **Create subscription** — `client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest { Subscription = new CreateSubscription { CustomerId = ..., ProductId = 7126957 or 7126958, ... } })`; pass existing `customer_id` / `customer_reference` and `product_id`; optionally include `component_id` (3057195) via component fields if required.
6. **List subscriptions for customer** — `client.Customers.ListCustomerSubscriptions(customerId)` → `IReadOnlyList<SubscriptionResponse>`.
7. **Optional retrieve customer by reference / id** — `client.Customers.ReadCustomer(id)` or `ReadCustomerByReference(...)` if needed by caller.

Every call is **throw-only** (no `...Result` variants exist — sdk-map.md "No-throw variants: absent across this SDK"). All reads of non-2xx bodies must go through the accessors named in each row.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Client construction / auth / server (map: sdk-map.md, `map/operations/Customers.md`, `map/operations/Subscriptions.md`, `map/operations/ProductFamilies.md`)

| Fact | Value / Shape | Source |
|---|---|---|
| Client class | `MaxioAdvancedBillingClient` (ns `MaxioAdvancedBilling`) | sdk-map.md |
| Options | `MaxioAdvancedBillingClientOptions` (`MaxioAdvancedBilling`) | sdk-map.md |
| Constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | sdk-map.md |
| Auth type | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` | sdk-map.md |
| Auth pattern | Basic: `Username` = API key, `Password` = literal `"x"` | sdk-map.md |
| Env enum | `MaxioAdvancedBilling.Servers.ServerEnvironment` (`Us` default, `Eu`) | sdk-map.md |
| Server override | `options.Server.Production.Us.Site = "cp-exp-1"`; base URL `https://{site}.chargify.com` | sdk-map.md Servers & auth |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`; `required` members — start from `RetryOptions.Default()` | sdk-map.md |

### Operations in scope

| Step | Controller property | Method signature (literal params, order, types) | Request model + key fields (`Name (wire_name): Type`, required?) | Response envelope (inner payload) | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 2 — list plans by family | `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params nullable no-default + defaults; `productFamilyId` is `string` (accepts handle directly) | `ListProductsFilter` (`filter`): see enums; no direct request body — query-only | `IReadOnlyList<ProductResponse>` (each `ProductResponse` has `Product`?) — verify from `map/models/records-*.md`: response envelope is `ProductResponse` with field `Product` (record page citation); list returns array | Case A: `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404]; `TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage` (defaults 1/20) | `map/operations/ProductFamilies.md` |
| 3 — find customer by email | `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 nullable + defaults; `q` = email query | No body — query `q` = email | `IReadOnlyList<CustomerResponse>` (envelope `CustomerResponse` → inner `Customer`) | Case B: `SdkException<RawError>`; `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | manual `page`/`perPage` (1/50) | `map/operations/Customers.md` |
| 4 — create customer | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)`; `body` nullable, no default → pass explicitly | `CreateCustomerRequest` (ns `MaxioAdvancedBilling.Models`) → `Customer (customer): CreateCustomer !req`; `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, etc. | `CustomerResponse` (`Customer`) | Case A: `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md`, `map/models/records-1-Ac-Cr.md` |
| 5 — create subscription | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)`; `body` nullable, no default | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`; `CreateSubscription` fields include `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `ComponentId (component_id): int?`, `PaymentProfileId`, etc. | `SubscriptionResponse` (`Subscription`) | Case A: `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`, `map/models/records-2-Cr-Ne.md` (request) |
| 6 — list customer subs | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | None — `customerId` path param | `IReadOnlyList<SubscriptionResponse>` | Case B: `SdkException<RawError>` | none | `map/operations/Customers.md` |
| 7 — read customer by id | `client.Customers` | `ReadCustomer(int id, CancellationToken ct = default)` | None — `id` path | `CustomerResponse` | Case B: `SdkException<RawError>` | none | `map/operations/Customers.md` |

### Request / response model notes (key fields only — not full dumps; implementer reads `map/models/records-*.md` for full field list and wire names)

- `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`. `CreateCustomer` required: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`. `Reference (reference): string?` is the idempotency lever per operation Notes (only reference enforced unique; email not enforced unique by SDK/operation — see Assumptions).
- `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`. Use `ProductId` = `7126957` (`eshop-pro`) or `7126958` (`basic-plan`). `CustomerId` = integer from step 3/4. Component `api-call` (3057195) can be passed via component fields if the subscription model supports it — verify in `CreateSubscription` fields on `map/models/records-2-Cr-Ne.md`.
- Response envelopes (verified from map): `CustomerResponse` contains `Customer`; `SubscriptionResponse` contains `Subscription`; `ProductResponse` contains `Product`. All reads of payload fields must go one level down: `resp.Customer.Email`, `resp.Subscription.Id`, etc.
- `SubscriptionResponse` inner `Subscription` fields include `Id`, `ProductId`, `CustomerId`, `State`, `Reference`, etc. — read `map/models/records-3-Of-Su.md` or `records-2-Cr-Ne.md` for precise wire names.

### Enum values needed (from `map/models/enums.md` — literal member names shown, build with `Type.FromValue("wire")` or static members; not C# `enum`)

- `SortingDirection`: members include `Asc`, `Desc` (verify exact member names in enums.md — do not guess wire strings).
- `BasicDateField`: members for `date_field` query (e.g., `CreatedAt`, `UpdatedAt`, etc.).
- `SubscriptionStateFilter`: members for `state` query on `ListSubscriptions` (if used); not required for in-scope `ListCustomerSubscriptions`.
- `ListProductsFilter`: filter values for product list.
- `ListProductsInclude`: include flags.
- `SubscriptionListInclude`: include flags for subscription lists (if expanded).

**NO attempt to list full 98 enum value sets here** — implementer loads `map/models/enums.md` for the specific enums above; sheet names which enums each step touches.

---

## 3. Trap notes (named hazards + `MUST load` pointers; consequences only — answers stay in skills)

> ⚠ Step 1 (client / auth) — `BasicAuthCredentials.Username` is the API key, `Password` literal `"x"`; setting it on `options` must happen before constructing `client`. **MUST load `dotnet-authentication`** before wiring credentials.
>
> ⚠ Step 1 (client / server) — `ServerEnvironment.Us` is default; to reach `cp-exp-1` set `options.Server.Production.Us.Site = "cp-exp-1"`; `Environment` and `Server` live in different namespaces (`Servers` vs root). **MUST load `dotnet-client-initialization`** before registering the client.
>
> ⚠ Step 2/5 (list / create) — every parameter that has no C# default and is nullable (`BasicDateField?`, etc.) must be passed explicitly (`null` to skip); positional omission binds wrong. Named arguments required for clarity. **MUST load `dotnet-calling-endpoints`** before first call.
>
> ⚠ Step 4 (create customer) — idempotency by email is NOT enforced by `CreateCustomer` (only `reference` is enforced unique per Notes). Whether to search-then-create or set `reference = email` is the application's design; SDK provides both `ListCustomers(q: email)` and `Reference`. **MUST load `dotnet-models`** to build `CreateCustomerRequest` with correct wire names.
>
> ⚠ Step 5 (create subscription) — `CreateSubscription` is throw-only; no `CreateSubscriptionResult`. Payment info may be required depending on product options (Notes). **MUST load `dotnet-error-handling`** before writing error boundary.
>
> ⚠ Step 6 (list customer subs) — `ListCustomerSubscriptions` takes `int customerId`; must extract from `CustomerResponse.Customer.Id` (not from email). **MUST load `dotnet-calling-endpoints`** again for parameter naming.
>
> ⚠ All steps (resilience) — `RetryOptions.Timeout` is per-attempt, not total; `HttpMethodsToRetry` gates status-triggered retries only — `POST` is NOT resent on 503 but IS resent on transport failure (`HttpRequestException`) regardless of verb, so non-idempotent `CreateSubscription` can execute more than once (floor `MaxRetries = 1`; `0` rejected at construction). **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts.

---

## 4. REQUIRED READING

Load **before implementation starts** (contract sheet does NOT carry their contents):

- `dotnet-authentication` — governs Step 1 auth wiring (Basic `Username` = key, `Password` = `"x"`).
- `dotnet-client-initialization` — governs client construction, `HttpClient` ownership, DI (`ServiceCollectionExtensions` / `AddMaxioAdvancedBillingClient`), and server-node override (`Site`, `BaseUrl`).
- `dotnet-calling-endpoints` — governs named-argument usage (`ct:`, explicit `null` for optional nullable params), response-envelope one-level-down reads (`resp.Product`, `resp.Customer`, `resp.Subscription`), pagination params (`page`/`perPage`), and which operations are Case A vs B (confirmed per map row above).
- `dotnet-models` — governs `CreateCustomerRequest`, `CreateSubscriptionRequest`, `StringEnum<T>` construction / `FromValue`, union accessors (`TryGet…`), required vs nullable properties, wire names vs C# property names.
- `dotnet-error-handling` — governs every `try/catch`: typed `SdkException<{Op}Error>` (Case A) vs `SdkException<RawError>` (Case B); `TryGet…` accessors per row above; `TryGetRawError` fallback; and the two `System.Text.Json.JsonException` directions (must be handled, not mapped blindly to 5xx):
  - a drifted / malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
- `dotnet-configuration-resilience` — governs `RetryOptions`, `Timeout` semantics, pagination (`page`/`perPage`), base-URL selection, and the `POST`-retry / transport-failure trap.
- `dotnet-testing` — governs test seam (`HttpClient` constructor arg); match project's existing framework; do not stub SDK internals.

**MUST load `dotnet-error-handling`** before writing that boundary. These two `JsonException` rows belong in the first sheet, not a later revision.

---

## 5. Assumptions & Blockers

Assumptions (lightweight — implementer's call, not SDK facts):

- `cp-exp-1` is US-hosted (`ServerEnvironment.Us`); if account is EU-hosted the base URL and `Environment` change — verify with account/admin.
- The application's identity/caller mechanism for `PublicApi` is not in the SDK map; how the integration receives the API key (binding key from config, not env-var raw) is the implementer's design.
- Idempotency strategy: the brief asks "idempotent by email"; SDK enforces uniqueness on `Customer.Reference`, not `Email`. Defensively: search by `q: email` (`ListCustomers`), then either create with `reference = email` (makes create idempotent) or skip if found. The exact choice and whether to persist the found customer id is the application's design.
- Component `api-call` (3057195) inclusion in `CreateSubscription`: the `CreateSubscription` model supports component fields (verify exact field names in `map/models/records-2-Cr-Ne.md`). If not needed for initial billing endpoints, omit; sheet carries only the contract.
- Plan handles `7126957` / `7126958` are `int`; family handle `eshop-subscribe` is `string`; `ListProductsForProductFamily` accepts `string productFamilyId`, so handle passes directly without ID lookup.

Blocker (stops planning / requires resolution before first call):

- **NONE** — all required operations (`ListProductsForProductFamily`, `ListCustomers`, `CreateCustomer`, `CreateSubscription`, `ListCustomerSubscriptions`, `ReadCustomer`) exist in `map/operations/`; no missing controller. The only gap is the idempotency-meaning interpretation (email vs reference) which is an application policy choice, not an SDK gap, documented above as defensive-coding directive.

Unverified (only live traffic can confirm — converted to defensive directive, not open question):

- `UNVERIFIED`: whether `CreateSubscription` with `product_id = 7126957` and component `3057195` actually creates the expected billing arrangement in sandbox `cp-exp-1`; defensive directive — create subscription, then read back via `ReadSubscription`, compare `Subscription.ProductId` and component list, log mismatch rather than assume.
- `UNVERIFIED`: whether `ListCustomers(q: email)` returns exactly one result for unique emails vs multiple; defensive directive — if `result.Count != 1`, treat as ambiguous (log, do not auto-select first) and require caller disambiguation.

---

## 6. Source citations per row (map page only — no clone path, per rules)

- Client / auth / server / error model / namespaces / no-throw absence / pagination conventions: `sdk-map.md`
- Customers operations (Create, List, ListCustomerSubscriptions, Read): `map/operations/Customers.md`
- Subscriptions operations (Create, List): `map/operations/Subscriptions.md`
- ProductFamilies / ListProductsForProductFamily: `map/operations/ProductFamilies.md`
- Products (ReadByHandle, ListProducts — reference only): `map/operations/Products.md`
- Request/response records (`CreateCustomer`, `CreateSubscription`, `CustomerResponse`, `SubscriptionResponse`, `ProductResponse`): `map/models/records-1-Ac-Cr.md`, `map/models/records-2-Cr-Ne.md`, `map/models/records-3-Of-Su.md`, `map/models/records-4-Su-We.md` (select per type name)
- Enums (`SortingDirection`, `BasicDateField`, `ListProductsFilter`, etc.): `map/models/enums.md`
- Unions / any additional `TryGet…` accessors: `map/models/unions.md`

No SDK source file was opened; no clone was made (not required — map settled signatures, wire names, envelopes, error accessors, enum indices). If a map-sourced name fails at build, the failing symbol's source file is named in its map row (e.g., `Models/CreateCustomerRequest.cs`, `Errors/CreateCustomerError.cs`) and will be opened scoped in a follow-up, not via tree scan.

---

*Plan file exists at: `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-001\repo\maxio-plan.md`. Only that file was written; no `src/PublicApi` edits made; no `dotnet build` run; no clone path written into this file or replies.*
