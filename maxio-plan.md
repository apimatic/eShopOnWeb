# Maxio Advanced Billing .NET SDK — Plan (eShopOnWeb PublicApi)

Path dictated by brief: `repo/maxio-plan.md` (default, as brief named absolute file at repo root).
Plan mode — no project files edited. Source: only the bundled SDK map (`maxio-getting-started`); clone not needed (no source-level gap found). All contract facts below cite the map page they came from.

---

## 1. Scope & sequence

Integration targets `PublicApi` (JWT auth already present). Endpoints to wire: `/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions`. SDK package: `AsadAli.AdvancedBilling.Sdk`; root namespace `MaxioAdvancedBilling`; client `MaxioAdvancedBillingClient`; options `MaxioAdvancedBillingClientOptions`; auth Basic (`Username` = API key, `Password` = literal `"x"`). Environments: `ServerEnvironment.Us` (default `https://{site}.chargify.com`) / `ServerEnvironment.Eu` (`https://{site}.ebilling.maxio.com`). Target `netstandard2.0`.

Sequence:
1. Client + DI registration (`dotnet-client-initialization`, `dotnet-authentication`, `dotnet-configuration-resilience`).
2. Read product / family by handle (`ReadProductByHandle`, `ReadProductFamily`, `ListProducts` with `ListProductsFilter`).
3. Idempotent customer lookup/create/update (`ListCustomers` with `q`, `CreateCustomer`, `UpdateCustomer`, `ReadCustomerByReference`).
4. Create subscription (`CreateSubscription`).
5. List customer's subscriptions (`ListCustomerSubscriptions`).
6. Error boundary + tests (`dotnet-error-handling`, `dotnet-testing`).

No `ApiResult` / `…Result` variants exist — every operation is throw-only (`dotnet-calling-endpoints`).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Client construction / auth / server node

| Item | Declaration / value | Source |
|---|---|---|
| Client type | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` (§Getting a client) |
| Constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Basic auth type (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`) | `BasicAuthCredentials { Username = string, Password = string }` — Password literal `"x"` | `sdk-map.md`; `dotnet-authentication` |
| Environment enum (`MaxioAdvancedBilling.Servers`) | `ServerEnvironment.Us` / `ServerEnvironment.Eu` | `sdk-map.md` |
| Retry options type (`MaxioAdvancedBilling.Core.Configuration`) | `RetryOptions` — all members `required`; build via `RetryOptions.Default()` | `sdk-map.md` |
| Auth pattern | HTTP Basic — username = Maxio/Chargify API key | `maxio-getting-started` (SDK identity) |

### 2.2 Operations (signature verbatim from map)

| Controller (`client.`) | Signature (params in order, types, required-explicit / nullable flags) | Request model + fields used (wire names from records pages) | Response envelope (inner field read) | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — `apiHandle` required; `ct` named exactly | — (GET, no body) | `ProductResponse` — read `.Product` (`Product`) | Case B `SdkException<RawError>`: `.Error.StatusCode`, `.ReadAsString()` | none | `map/operations/Products.md` (ReadProductByHandle) |
| `ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` — `id` required | — | `ProductFamilyResponse` — `.ProductFamily` (`ProductFamily`) | Case B `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` (ReadProductFamily) |
| `Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params nullable/no-default → pass `null`; `page`/`perPage` have defaults | `ListProductsFilter` (see §2.5) | `IReadOnlyList<ProductResponse>` — each `.Product` | Case B `SdkException<RawError>` | manual `page`+`perPage` | `map/operations/Products.md` (ListProducts) |
| `Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params nullable/no-default | — (search by email via `q`) | `IReadOnlyList<CustomerResponse>` — each `.Customer` | Case B `SdkException<RawError>` | manual `page`+`perPage` | `map/operations/Customers.md` (ListCustomers) |
| `Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateCustomerRequest` (see §2.5); body field `Customer` (`customer`) required `Customer` record — fields `Email (email): string?`, `Reference (reference): string?`, `FirstName`, `LastName`, etc. | `CustomerResponse` — `.Customer` (`Customer`) | Case A `SdkException<CreateCustomerError>`: `.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `.TryGetRawError(out RawError)` | none | `map/operations/Customers.md` (CreateCustomer) |
| `Customers` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `id` required; `body` pass explicitly | `UpdateCustomerRequest` — `Customer (customer): Customer ?` (optional inside request; check `records-2-Cr-Ne.md` / source) | `CustomerResponse` — `.Customer` | Case A `SdkException<UpdateCustomerError>`: `.TryGetNoContent(out RawError)` [404]; `.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `.TryGetRawError` | none | `map/operations/Customers.md` (UpdateCustomer) |
| `Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` required | — | `CustomerResponse` — `.Customer` | Case B `SdkException<RawError>` | none | `map/operations/Customers.md` (ReadCustomerByReference) |
| `Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` pass explicitly | `CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription !req` (record fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `PaymentProfileId`, `Components`, etc.) | `SubscriptionResponse` — `.Subscription` (`Subscription?`) | Case A `SdkException<CreateSubscriptionError>`: `.TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `.TryGetRawError` | none | `map/operations/Subscriptions.md` (CreateSubscription) |
| `Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` required | — | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` | Case B `SdkException<RawError>` | none | `map/operations/Customers.md` (ListCustomerSubscriptions) |

Notes from operation rows (only the provider's Notes matter for acceptance):
- `CreateSubscription` Notes: specify product by `product_id` or `product_handle`; customer by `customer_id` or `customer_reference`; can include `customer_attributes` to create new customer inline; `payment_profile_attributes` / `credit_card_attributes` for payment; 3DS post-auth yields 422 with `action_link`; do not send real card data in production without PCI compliance.
- `ListCustomers` Notes: use `q` to search by email / Advanced Billing ID / organization / reference / first/last name; for exact single match by reference use `ReadCustomerByReference`.
- `CreateCustomer` Notes: `reference` must be unique if provided; country = ISO 3166-1 2-char; state = 2-char US or 2-3-char outside US.
- `ReadProductByHandle`: reads by `api_handle`; `ReadProductFamily` takes id or `handle:my-family` format (per `ReadProductFamily` Notes).

### 2.3 Request/response envelopes the integration reads

- `ProductResponse` — exactly one field: `Product (product): Product !req` (`models/records-3-Of-Su.md` line 70; source `Models/ProductResponse.cs`).
- `ProductFamilyResponse` — `ProductFamily (product_family): ProductFamily !req` (map/operations/ProductFamilies.md header; source `Api/ProductFamilies.cs`).
- `CustomerResponse` — `Customer (customer): Customer !req` (`models/records-2-Cr-Ne.md` line 43; source `Models/CustomerResponse.cs`).
- `SubscriptionResponse` — `Subscription (subscription): Subscription?` (`models/records-4-Su-We.md` line 66; source `Models/SubscriptionResponse.cs`).
- `CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription !req` (`models/records-2-Cr-Ne.md` line 21; source `Models/CreateSubscriptionRequest.cs`). The inner `CreateSubscription` fields used by this plan: `ProductHandle`, `ProductId`, `CustomerId`, `CustomerReference`, `Reference`, `PaymentProfileId`, `Components`, `CustomerAttributes`. All wire names shown with `(wire_name)` above come from that records line.
- `CreateCustomerRequest` — `Customer (customer): Customer !req` (`models/records-2-Cr-Ne.md` / source); inner `Customer` fields `Email (email)`, `Reference (reference)`, `FirstName (first_name)`, etc.
- `UpdateCustomerRequest` — `Customer (customer): Customer ?` (optional; confirm with source file `Models/UpdateCustomerRequest.cs` if ambiguity; map row on Customers.md for UpdateCustomer names the type but not inner fields). **UNVERIFIED** — if the live wire sends fields not in `UpdateCustomerRequest`, extract best-effort from `Customer` record and fall back to generic message.

### 2.4 Error boundary facts (every operation)

Every call throws `SdkException<TError>` (namespace `MaxioAdvancedBilling.Core.Exceptions`). No `ApiResult`; always wrap.

- Case A examples in scope: `CreateCustomer` (`CreateCustomerError`), `UpdateCustomer` (`UpdateCustomerError`), `CreateSubscription` (`CreateSubscriptionError`). Accessors named verbatim by operation row (e.g., `TryGetCustomerErrorResponse1`, `TryGetErrorListResponse1`, `TryGetRawError`).
- Case B examples in scope: `ReadProductByHandle`, `ReadProductFamily`, `ListProducts`, `ListCustomers`, `ReadCustomerByReference`, `ListCustomerSubscriptions`. Accessor = `RawError`: `.StatusCode`, `.ReadAsString()`, `.ReadAsBytes()`, `.ReadAsJson<T>()`.
- `JsonException` from deserialization reaches the boundary from two directions (see REQUIRED READING). Do not parse `ex.ToString()` when an accessor exists.

### 2.5 Filter / enum / supporting types actually referenced

| Type / enum | Namespace | Key members / usage | Source |
|---|---|---|---|
| `ListProductsFilter` | `MaxioAdvancedBilling.Models` (record) | Filter by handle / family / price point — construct per `models/records-2-Cr-Ne.md` / `models/records-3-Of-Su.md` (search `ListProductsFilter`) | map records pages |
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | `CreatedAt`, `UpdatedAt`, `StartedAt`? — check `map/models/enums.md`; pass `null` when not filtering by date | `map/models/enums.md`; operation page query mapping confirms wire `date_field` |
| `SortingDirection` | `MaxioAdvancedBilling.Models.Enums` | `Asc`, `Desc` | `map/models/enums.md` |
| `SubscriptionStateFilter` / `SubscriptionListInclude` / `SubscriptionInclude` / `ListProductsInclude` | `MaxioAdvancedBilling.Models.Enums` | Used only if the integration passes them; default to `null` | `map/models/enums.md` |
| `CollectionMethod` (payment) | `MaxioAdvancedBilling.Models.Enums` | Used inside `CreateSubscription` (`payment_collection_method`); values from `enums.md` | `map/models/enums.md` |

**Your call — not in the map:** which exact `ListProductsFilter` fields (e.g., `Handle`, `FamilyId`) the endpoint passes; verify against `models/records-*.md` `ListProductsFilter` row before coding the filter object.

---

## 3. Trap notes (hazard + consequence — do NOT resolve inline; load the named skill)

- ⚠ Step 1 (client registration / DI) — `HttpClient`/handler must be long-lived / reused via `IHttpClientFactory`; SDK client wrapper may be transient; retry/timeout options do **not** bound a whole call and are **not** the `HttpClient` timeout. **MUST load `dotnet-client-initialization`** before wiring `AddMaxioAdvancedBillingClient` or `new MaxioAdvancedBillingClient(...)`.
- ⚠ Step 1 (auth) — Basic credentials (`BasicAuth`) must be set before client construction or in DI callback; load from config, never hardcode; `Username` = API key, `Password` = literal `"x"`. **MUST load `dotnet-authentication`**.
- ⚠ Step 2 (calling endpoints) — many params (e.g., `dateField`, `filter`, `q`, `include`) have no C# default and mis-bind if passed positionally; always use named arguments; cancellation token parameter is named `ct`. **MUST load `dotnet-calling-endpoints`** before first `client.Products...` call.
- ⚠ Step 2 (models / unions) — `CreateSubscriptionRequest` wraps `CreateSubscription` (required); enums are `StringEnum<T>` not C# enums (`CollectionMethod.FromValue(...)` or static members); unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`**.
- ⚠ Step 3 (idempotent customer) — the SDK has no direct "find by email/handle" singular endpoint; idempotency requires `ListCustomers(q: email)` or `ReadCustomerByReference(reference: ...)` then branch to `CreateCustomer` / `UpdateCustomer`. Whether a failed `CreateCustomer` (duplicate reference) can be re-sent safely is **NOT** settled by the map — defensive coding: check existing first; if live wire returns a shape not matching `CreateCustomerError`, fall back to generic message (`UNVERIFIED`). **MUST load `dotnet-error-handling`**.
- ⚠ Step 4 (subscription write) — `CreateSubscription` is a `POST`; transport failures (`HttpRequestException`) retry on **every** verb (including POST), so a non-idempotent write can execute >1 time; `MaxRetries = 0` is rejected at construction (floor is 1); `Timeout` is per-attempt, not total; `HttpMethodsToRetry` gates only status-trigger retries (`503` on POST not resent, but transport failure is). **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 5 (error boundary) — `SdkException<T>` accessors (`TryGet…`) are the read path; never parse `.ToString()` when accessor exists. **MUST load `dotnet-error-handling`**.
- ⚠ Step 6 (tests) — test seam is the `HttpClient` constructor argument; match project framework. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING (load BEFORE implementation starts — these skills carry content the sheet deliberately omits)

- `dotnet-client-initialization` — Step 1 (client / DI / `HttpClient` lifetime).
- `dotnet-authentication` — Step 1 (Basic auth setup; `BasicAuthCredentials`; config loading).
- `dotnet-calling-endpoints` — Steps 2–5 (named arguments; `ct`; response envelopes; pagination; case A/B confirmation per operation row).
- `dotnet-models` — Steps 2–4 (request construction; `StringEnum`; unions; required members; wire names vs C# names; dropped unmodeled fields).
- `dotnet-configuration-resilience` — Step 1 / Step 4 (`RetryOptions`; `Timeout` per-attempt; `HttpMethodsToRetry`; transport retry on POST; `MaxRetries` floor).
- `dotnet-error-handling` — Steps 3–5 (Case A vs B per operation; `TryGet…` accessors; `RawError`; `JsonException` boundary mechanics; no `.ToString()` parsing).
- `dotnet-testing` — Step 6 (seam; framework match).

Both `JsonException` boundary rows (mandatory, first sheet, not a later revision):
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

Assumptions (application-side; not SDK contract):
- The PublicApi project uses JWT for its own endpoints; Maxio auth (Basic) is a separate concern — the SDK client's `BasicAuth` is configured independently, not derived from the JWT token. **YOUR CALL — not in the map.**
- "List product families/plans by handle" interpreted as `ReadProductByHandle` + `ListProducts` (optional `filter`) + `ReadProductFamily`; if the endpoint needs family-level listing, `ListProductFamilies` (Case B) is available (`map/operations/ProductFamilies.md`).
- "Idempotent by email/handle" interpreted as search-then-create/update using `ListCustomers`/`ReadCustomerByReference`; no SDK operation guarantees idempotency — implementer must sequence.
- The `PublicApi` endpoints (`/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions`) are application design decisions — the SDK does not dictate endpoint names or response DTOs; those map to the SDK calls above.

Blockers (would stop planning / require clarification):
- None from map; all needed operations exist (`Products`, `ProductFamilies`, `Customers`, `Subscriptions`). If the live wire sends a `CreateSubscription` body whose `subscription` object has fields not on `CreateSubscription`, the contract is incomplete — label `UNVERIFIED` and apply defensive extraction + fallback (see §3 trap note). If the live wire for `UpdateCustomer` uses a different inner shape than `UpdateCustomerRequest`, same.
- `UpdateCustomerRequest` inner fields not listed in the map's records page (only the wrapper is indexed on `records-2-Cr-Ne.md`); source file `Models/UpdateCustomerRequest.cs` should be checked if the compiler reports missing members — this is a real map-side gap only if compilation fails, per `maxio-getting-started` (source only on real gap).

---

## 6. Source references (map pages only — no clone path included per rules)

- SDK identity / namespace / auth / client construction: `maxio-getting-started/sdk-map.md`
- Operations — `Products`: `map/operations/Products.md`
- Operations — `ProductFamilies`: `map/operations/ProductFamilies.md`
- Operations — `Customers`: `map/operations/Customers.md`
- Operations — `Subscriptions`: `map/operations/Subscriptions.md`
- Models — `CreateSubscriptionRequest` / `CustomerResponse` / `CreateCustomer` / `CreateSubscription` fields: `map/models/records-2-Cr-Ne.md`
- Models — `ProductResponse` / `SubscriptionResponse`: `map/models/records-3-Of-Su.md` (ProductResponse) / `map/models/records-4-Su-We.md` (SubscriptionResponse)
- Enums — `BasicDateField`, `SortingDirection`, `CollectionMethod`: `map/models/enums.md`
- Error core / `SdkException` / `RawError` / accessors: `maxio-getting-started/sdk-map.md` (§Error-handling model)
