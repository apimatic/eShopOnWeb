# Maxio Advanced Billing — eShopOnWeb contract sheet

Path dictated by brief: `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-007\repo\maxio-plan.md`.
Plan mode — no project files edited; SDK facts from bundled map (`sdk-map.md`, `map/operations/*.md`, `map/models/*.md`) only. Source clone used only if map gap confirmed; no clone path appears below.

---

## 1. Scope & sequence

Integration targets `src/PublicApi` (new controllers/endpoints). No SQL Server — in-memory DB (`AddDbContext<...>(options => options.UseInMemoryDatabase(...))` per `dotnet-client-initialization` / app convention, not SDK).

Steps (order):
1. **Client / auth / server-node wiring** — register `MaxioAdvancedBillingClient` with Basic auth (`Username = API key`, `Password = "x"`), environment from `Maxio:Environment` (default `ServerEnvironment.Us`), optional `ServerOptions` override from `Maxio:BaseUrl`. **MUST load `dotnet-authentication`** and `dotnet-configuration-resilience`.
2. **Config binding** — bind keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). Environment variables `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY` supply values; binding key names are authoritative, never raw env vars.
3. **Customer idempotency** — find by email: `client.Customers.ListCustomers(q: email, ...)`; if empty, `client.Customers.CreateCustomer(new CreateCustomerRequest { Customer = new CreateCustomer { Email = email, FirstName = ..., LastName = ..., Reference = email } })`. Alternative: set `Reference = email`, use `ReadCustomerByReference(email)`. **YOUR CALL — not in the map** which idempotency route the app takes; both are supported. Source rows: `operations/Customers.md`, `models/records-1-Ac-Cr.md` (CreateCustomer / CreateCustomerRequest).
4. **Product-family / plan / component lookup** — `client.ProductFamilies.ReadProductFamily(3023074)` for family; `client.ProductFamilies.ListProductsForProductFamily(3023074, ...)` for plans (ids 7126957/7126958); `client.Components.ListComponentsForProductFamily(3023074, ...)` for component `api-call` (3057195). Handles from config: family `shoppable-subscribe`; product handles `eshop-pro`, `basic-plan`; component handle `api-call`. Source: `operations/ProductFamilies.md`, `operations/Components.md`.
5. **Subscribe** — `client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = configFamilyHandle, CustomerReference = customer.Reference, ... } })`. Can also pass `CustomerId`; use reference for idempotency. Source: `operations/Subscriptions.md`, `models/records-2-Cr-Ne.md`.
6. **List customer subscriptions** (`GET /api/my-subscriptions`) — `client.Customers.ListCustomerSubscriptions(customerId)` (requires resolved customer id from step 3). If listing across site: `client.Subscriptions.ListSubscriptions(...)`. **YOUR CALL — not in the map** which endpoint semantics the app exposes; contract only gives accessors.
7. **Public endpoints** — `GET /api/subscription-plans` returns family + products + components aggregated from steps 4; `POST /api/subscriptions` calls step 5; `GET /api/my-subscriptions` calls step 6.

Blockers (§5): none — SDK supports all required operations. Unverified: whether live wire payloads for `CreateSubscription` with `product_handle` alone match the generated `CreateSubscription` model; defensive directive below labels `UNVERIFIED`.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — each type below comes from its operation/model row (root namespace `MaxioAdvancedBilling`; auth/config/sub-models spread to `MaxioAdvancedBilling.Core.*`, `MaxioAdvancedBilling.Core.Configuration`, `MaxioAdvancedBilling.Servers`).

### Client construction / auth / server (not per-operation)

| Fact | Value / Type | Source |
|---|---|---|
| Client constructor | `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` | `sdk-map.md` (Getting a client) |
| Namespace (root) | `MaxioAdvancedBilling` | `sdk-map.md` |
| Package / NuGet | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Auth scheme | Basic; `BasicAuthCredentials` (`Username = api_key`, `Password = "x"`) | `sdk-map.md`; `dotnet-authentication` |
| Client options type | `MaxioAdvancedBillingClientOptions` (`BasicAuth`, `Environment`, `Retry`, `Server`) | `sdk-map.md` |
| Auth namespace | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` |
| Environment type / values | `ServerEnvironment` (US default, EU) — `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| Retry options | `RetryOptions` (`StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`) all `required`; build from `RetryOptions.Default()` | `sdk-map.md`; `dotnet-configuration-resilience` |
| Server override | `ServerOptions` via `Maxio:BaseUrl`; optional | `Maxio:BaseUrl` binding — YOUR CALL |

### Operations in scope

| Controller | Method (verbatim sig) | Request model + key fields (`wire_name: type`, required?) | Response envelope + inner | Error (A/B) + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` | None (query params); `q` searches by email/reference/name | `IReadOnlyList<CustomerResponse>` → `.Customer` (`Customer`) | Case B (`SdkException<RawError>`); `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | Manual `page`/`perPage` | `operations/Customers.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default, must pass | `CreateCustomerRequest.Customer` (`CreateCustomer`): `FirstName (!req)`, `LastName (!req)`, `Email (!req)`, `Reference?`, `Organization?`, `Address?`, `City?`, `State?`, `Zip?`, `Country?`, `Phone?`, `Locale?`, `TaxExempt?`, `ParentId?`, `SalesforceId?` (wires: `first_name`, `last_name`, `email`, `reference`, `organization`, `address`, `city`, `state`, `zip`, `country`, `phone`, `locale`, `tax_exempt`, `parent_id`, `salesforce_id`) | `CustomerResponse` → `.Customer` | Case A (`SdkException<CreateCustomerError>`); `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `TryGetRawError` [fallback] | None | `operations/Customers.md`; `models/records-1-Ac-Cr.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | None | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` each | Case B (`SdkException<RawError>`); accessors above | None | `operations/Customers.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default | `CreateSubscriptionRequest.Subscription` (`CreateSubscription`): `ProductId?`, `ProductHandle?`, `ProductPricePointId?`, `ProductPricePointHandle?`, `CustomerId?`, `CustomerReference?`, `SubscriptionGroupUid?`, `PaymentProfileId?`, `PaymentProfileAttributes?`, `CustomerAttributes?` (nested `CreateCustomer` / `CustomerAttributes`), `Ref?`, `ActivatedAt?`, `CanceledAt?`, `CancelReason?`, `Wrapping?` etc. (see `CreateSubscription` model full definition) | `SubscriptionResponse` → `.Subscription` (`Subscription`) | Case A (`SdkException<CreateSubscriptionError>`); `TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `TryGetRawError` | None | `operations/Subscriptions.md`; `models/records-2-Cr-Ne.md` |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params before `ct`, all nullable except `page`/`perPage` with defaults | None (filters/query) | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` | Case B (`SdkException<RawError>`); accessors above | Manual `page`/`perPage` (default 20) | `operations/Subscriptions.md` |
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` | None | `ProductFamilyResponse` → `.ProductFamily` | Case B (`SdkException<RawError>`); accessors above | None | `operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 params, all nullable, no defaults | None | `IReadOnlyList<ProductFamilyResponse>` | Case B (`SdkException<RawError>`); accessors above | None | `operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` is `string` (handle or id format) | None | `IReadOnlyList<ProductResponse>` → `.Product` | Case A (`SdkException<ListProductsForProductFamilyError>`); `TryGetString(out string)` [404]; `TryGetRawError` | Manual `page`/`perPage` | `operations/ProductFamilies.md` |
| `client.Components` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | None | `IReadOnlyList<ComponentResponse>` → `.Component` | Case A (`SdkException<ListComponentsForProductFamilyError>`); check page | Manual `page`/`perPage` | `operations/Components.md` |

### Response envelopes to read (one level down)

- `CustomerResponse` has exactly one public field: `Customer` (`Customer`). Read `CustomerResponse` row in model pages (`models/records-...`).
- `SubscriptionResponse` has exactly one field: `Subscription` (`Subscription`).
- `ProductFamilyResponse` has exactly one field: `ProductFamily` (`ProductFamily`).
- `ProductResponse` has `.Product`. `ComponentResponse` has `.Component`.
- **Consequence**: `await client.Customers.CreateCustomer(...)` yields a response object; to get identity, read `.Customer.Id`; never assume flat payload.

### Config / environment values (application-controlled, not SDK)

| Binding key | Source / default | Notes |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` env | Required; no SDK default |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` env | Used to construct base URL; `ServerEnvironment` separate |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` env; default `Us` (`ServerEnvironment.Us`) | Maps to SDK `ServerEnvironment` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` env; value `shoppable-subscribe` | Family handle for subscription creation |
| `Maxio:BaseUrl` | Optional override; if set, feed into `ServerOptions` / `MaxioAdvancedBillingClientOptions.Server` | YOUR CALL — not in SDK map |

### Sandbox handles / ids (given by task, not SDK)

- Family handle `shoppable-subscribe` (id `3023074`)
- Pro Plan handle `eshop-pro` (id `7126957`)
- Basic Plan handle `basic-plan` (id `7126958`)
- Component handle `api-call` (id `3057195`)

These are **application data**; SDK accepts handles/ids via operation parameters.

---

## 3. Trap notes

> ⚠ Step 1 (client registration) — `RetryOptions.Timeout` bounds retry attempts, not an `HttpClient` timeout; `HttpClient` lifetime/ownership is the app's responsibility (`dotnet-client-initialization`). **MUST load `dotnet-configuration-resilience`** before wiring retries.
>
> ⚠ Step 1 (auth) — Basic auth username = API key; password literal `"x"`; wrong password = 401, not SDK exception. **MUST load `dotnet-authentication`**.
>
> ⚠ Step 3 (customer lookup/create) — `ListCustomers` `q` searches broadly; no exact-match guarantee. If idempotency requires exact reference, prefer `CreateCustomer` with `Reference = email` then `ReadCustomerByReference(email)`. **MUST load `dotnet-calling-endpoints`** before writing call site.
>
> ⚠ Step 3 / 5 (nullable params) — `CreateSubscriptionRequest`, `ListCustomers` first 7 params, `CreateCustomer` body all require explicit `null` to skip; compiler does not enforce presence of `null` arguments for nullable reference types when optional values are omitted — pass them explicitly to avoid unintended wire omissions. **MUST load `dotnet-models`**.
>
> ⚠ Step 5 (subscription creation) — `CreateSubscription` notes say `product_handle` or `product_id`; live payload for `product_handle` with `customer_reference` only is not confirmed by map. Defensive directive: extract `SubscriptionResponse.Subscription` best-effort; if fields missing, fall back to generic message. **UNVERIFIED** — only live traffic confirms full payload shape. **MUST load `dotnet-calling-endpoints`**.
>
> ⚠ Step 6 (listing) — `ListCustomerSubscriptions` takes `int customerId` (not handle/reference); resolve id from step 3 first. **MUST load `dotnet-calling-endpoints`**.
>
> ⚠ All response reads — response envelopes wrap payload; missing `.Subscription` / `.Customer` is a build-time/naming failure, not a runtime default. **MUST load `dotnet-models`**.

---

## 4. REQUIRED READING

Load **before implementation starts**; sheet carries only hazard names + pointers, not resolved answers.

- `dotnet-authentication` — governs Basic auth binding (`Username`, `Password = "x"`) and credential rotation.
- `dotnet-calling-endpoints` — governs controller access (`client.Customers`, `client.Subscriptions`, etc.), parameter order (`ct` named arg), nullable-explicit rules, response-envelope reads, pagination (`page`/`perPage`).
- `dotnet-configuration-resilience` — governs `RetryOptions`, `Timeout` semantics, `ServerOptions`/`ServerEnvironment`, `HttpClient` ownership.
- `dotnet-models` — governs `CreateCustomerRequest`/`CreateSubscriptionRequest` construction, required members (`!req`), wire-name mapping, enums (`SubscriptionStateFilter`, etc.), union accessors.
- `dotnet-client-initialization` — governs `AddMaxioAdvancedBillingClient`, DI registration, `HttpClient` lifetime.
- `dotnet-error-handling` — governs `SdkException<...>` case A/B mechanics, `TryGet...` accessors, `RawError` fallback; **mandatory for boundary code**.
- `dotnet-testing` — if tests written for integration layer.

Both `JsonException` boundary hazards, verbatim (added to first sheet per design):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

Assumptions (user intent, not SDK facts):
- `GET /api/subscription-plans` aggregates family/plans/components from SDK lookups; specific response DTO is application-defined.
- `GET /api/my-subscriptions` resolves current customer from app identity (not from SDK session); SDK provides `ListCustomerSubscriptions(customerId)` only.
- `POST /api/subscriptions` accepts a JSON body that maps to `CreateSubscriptionRequest`; exact input schema belongs to the implementer (`YOUR CALL`).
- `In-memory DB` is application's persistence choice; SDK is stateless.
- No live traffic verification performed; `CreateSubscription` with handle-only payload labeled `UNVERIFIED` — defensive extraction + fallback required.

Blockers: **none** — all required SDK operations exist in the bundled map (`Customers`, `Subscriptions`, `ProductFamilies`, `Components`). If a later compile error on a symbol occurs, resolve via `map/operations/*.md` first, then named source file only for confirmed gap (per agent rules — clone never leaves temp, never appears here).

---

## 6. Source citations (map pages only — no clone path exposed)

- `sdk-map.md` (client construction, auth, error model, namespaces)
- `map/operations/Customers.md` (ListCustomers, CreateCustomer, ListCustomerSubscriptions)
- `map/operations/Subscriptions.md` (CreateSubscription, ListSubscriptions)
- `map/operations/ProductFamilies.md` (ReadProductFamily, ListProductFamilies, ListProductsForProductFamily)
- `map/operations/Components.md` (ListComponentsForProductFamily)
- `map/models/records-1-Ac-Cr.md` (CreateCustomer / CreateCustomerRequest)
- `map/models/records-2-Cr-Ne.md` (CreateSubscription / CreateSubscriptionRequest)
- `map/models/enums.md` (SubscriptionStateFilter, SortingDirection, BasicDateField, SubscriptionSort, SubscriptionListInclude, etc. — load via `dotnet-models` as needed)
