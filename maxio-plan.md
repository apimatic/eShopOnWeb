# Maxio Advanced Billing — "Subscribe" capability plan & contract sheet

## 1. Scope & sequence

Additive capability in `src/PublicApi` (routes `GET /api/subscription-plans`, `POST /api/subscriptions`,
`GET /api/my-subscriptions`, JWT-authenticated). Maxio Advanced Billing sandbox is the system of record.

Recommended host shape: a thin application layer per concern that owns the single registered SDK client and
translates SDK types to endpoint DTOs — (1) plan catalog, (2) customer find-or-create, (3) subscribe
(find-or-create customer → idempotency guard → create subscription), (4) my-subscriptions / refresh. The
PublicApi controllers only map HTTP/JWT identity to these services. Maxio customer ↔ eShop user mapping is
keyed on the Maxio customer `reference` = eShop user-id `Guid.ToString()`, resolved on every request (Maxio is
the system of record; no local cache is required by the SDK facts).

Implementation order:

1. **Add the package** — `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` (PublicApi project;
   the one project that needs it; see §3 for target-framework constraint).
2. **Client + options registration** — build `MaxioAdvancedBillingClientOptions` from the four `Maxio:*`
   settings and register the client (facts in §3). Uses client construction only.
3. **Plan catalog service** — `client.ProductFamilies.ListProductFamilies` (find the family whose
   `Handle` = `Maxio:ProductFamilyHandle`) → `client.ProductFamilies.ListProductsForProductFamily` →
   optionally `client.Products.ReadProductByHandle`. Reads `ProductResponse.Product`.
4. **Customer find-or-create service** — `client.Customers.ReadCustomerByReference` (404 ⇒ not found) →
   `client.Customers.CreateCustomer`. Reads `CustomerResponse.Customer`.
5. **Subscribe service** — resolve customer (step 4), run the idempotency guard via
   `client.Customers.ListCustomerSubscriptions`, then `client.Subscriptions.CreateSubscription`. Reads
   `SubscriptionResponse.Subscription`. Consequence: the SDK has **no API-side idempotency key**, so the
   same-plan guard is a read-then-write check in this service (see §7 for the server-enforced constraint that
   backs it).
6. **My-subscriptions service** — `client.Customers.ListCustomerSubscriptions` (+ optional
   `client.Subscriptions.ReadSubscription` refresh).
7. **Error boundary** — one translation layer for every SDK call, per §4.

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

Controller accessors on the client (each property's type is `MaxioAdvancedBilling.Api.<Group>`, e.g.
`client.Products` is `MaxioAdvancedBilling.Api.Products`): `client.ProductFamilies`, `client.Products`,
`client.Customers`, `client.Subscriptions`. Every operation is `Task`-returning, throw-only, and takes
`CancellationToken ct = default` as its last parameter. **There are no no-throw/`…Result` variants — every
call must be wrapped.**

Envelope rule: response records wrap their payload in exactly one field; **reads go one level down**:
`ProductResponse.Product`, `ProductFamilyResponse.ProductFamily`, `CustomerResponse.Customer`,
`SubscriptionResponse.Subscription` (note: this last inner field is *nullable*). Request records likewise wrap
their payload: `CreateCustomerRequest.Customer`, `CreateSubscriptionRequest.Subscription`.

### Operations

| # | Purpose | SDK operation (type, signature — `ct: CancellationToken` last) | Request model / members sent (C# name `(wire_name)`: type, required?) | Response read — envelope member + inner fields (wire_name) | Error behaviour (throw-type + accessors) | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 | Resolve the configured product family by handle (`Maxio:ProductFamilyHandle`) | `Task<IReadOnlyList<ProductFamilyResponse>> client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — pass all 5 filter params explicitly as `null` | Query params only: `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime` (all skipped) | `ProductFamilyResponse.ProductFamily (product_family): ProductFamily` — match client-side on `Handle (handle): string?`; read `Id (id): int?` for op 2 | Case B — `SdkException<RawError>`; read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none (no page params) | `operations/ProductFamilies.md` |
| 2 | List the site's plans that belong to the resolved family | `Task<IReadOnlyList<ProductResponse>> client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass the 8 filter params (`dateField`…`include`) explicitly as `null`; set `includeArchived: false` to keep the catalog clean | Path/query only: `{product_family_id}` ← `productFamilyId` (numeric id from op 1, stringified); `page`, `per_page`, `include_archived`. **No request body.** | `ProductResponse.Product (product): Product` — read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?` (default price point, in **cents**), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` (product has **no `state` field** — displayable = `ArchivedAt == null`), `Taxable (taxable): bool?`, `RequireCreditCard (require_credit_card): bool?`, `ProductPricePointId/Handle` | Case A — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404 family not found] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage`; response is a **bare list, no page envelope** — stop when a page returns fewer than `perPage` | `operations/ProductFamilies.md` |
| 3 | Read one plan by handle (pre-subscribe validation / price confirmation) | `Task<ProductResponse> client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | Path only: `/products/handle/{api_handle}` ← `apiHandle` = plan handle (`eshop-pro`, `basic-plan`) | Same `ProductResponse.Product` read as op 2 | Case B — `SdkException<RawError>`; 404 = `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Products.md` |
| 4 | Look up existing Maxio customer by our reference (= user-id Guid string) | `Task<CustomerResponse> client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query: `reference` ← `reference` (`/customers/lookup.json`; exact single match) | `CustomerResponse.Customer (customer): Customer` — read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` | Case B — `SdkException<RawError>`; **not-found = `ex.Error.StatusCode == HttpStatusCode.NotFound`** (no typed 404, no null return) | none | `operations/Customers.md` |
| 5 | Create the Maxio customer (idempotent find-or-create) | `Task<CustomerResponse> client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateCustomerRequest` (namespace `MaxioAdvancedBilling.Models`) — exactly one member: `Customer (customer): CreateCustomer !req` (required). `CreateCustomer` members to set: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (optional — set to the user-id Guid string; **server enforces uniqueness of `reference`**). First/last/email values: app data from the logged-in identity | `CustomerResponse.Customer` — as op 4 | Case A — `SdkException<CreateCustomerError>` (namespace `MaxioAdvancedBilling.Errors`); `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ On 422 the typed branch is taken and `TryGetRawError` is **false**, so the raw body is not reachable there | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| 6 | (Fallback only) search customers by reference if the direct lookup is ever unavailable | `Task<IReadOnlyList<CustomerResponse>> client.Customers.ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params (`direction`…`q`) nullable, no default → pass explicitly | Query: `q` ← reference value (map Notes: `q` supports search **by a reference value**); others `null` | `IReadOnlyList<CustomerResponse>` → each `.Customer`; confirm exact match on `Reference` client-side (Notes: lookup endpoint is the exact-match one) | Case B — `SdkException<RawError>` | manual `page`+`perPage` (default 50); bare list | `operations/Customers.md` |
| 7 | Create the subscription (flat-price plan, no payment capture) | `Task<SubscriptionResponse> client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateSubscriptionRequest` — exactly one member: `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` members to set (nothing on it is `!req`; these three are the whole payload): `ProductHandle (product_handle): string?` (plan handle; or `ProductId` — **Notes: specify product with `product_id` OR `product_handle`**), `CustomerReference (customer_reference): string?` (Maxio customer reference; **Notes: identify an existing customer with `customer_id` OR `customer_reference`**), optionally `Reference (reference): string?` (subscription reference). **Send no `payment_profile_*` / no `CreditCardAttributes` / no `PaymentProfileAttributes`** — no card is captured. Do NOT set `Components`, `CustomPrice`, price-point fields, `CalendarBilling`, `NetTerms`. Left out by design (Notes-tied, unused here): `PaymentCollectionMethod`, `SkipBillingManifestTaxes`, trial/initial-charge fields | `SubscriptionResponse.Subscription (subscription): Subscription?` — read `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?` (current price, cents), `Product (product): Product?` → read `.Handle (handle)`, `.Name (name)` (the nested product object in this SDK is typed `Product` but the live payload is an abbreviated shape — read only `Handle`/`Name`, see §5 assumptions), `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (**next-billing-date field**), `CreatedAt (created_at): DateTimeOffset?`, `Reference (reference): string?` | Case A — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1` member: `Errors (errors): IReadOnlyList<string> !req` (flat message list) | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| 8 | List one customer's subscriptions (my-subscriptions + same-plan guard) | `Task<IReadOnlyList<SubscriptionResponse>> client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path only: `/customers/{customer_id}/subscriptions.json` ← `customerId` = **Maxio customer `Id` (int)** from op 4/5 — the endpoint does **not** accept reference; resolve reference → id first | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` — read `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` → `.Handle`, `CreatedAt (created_at): DateTimeOffset?`. ⚠ **No state filter parameter exists on this operation** — filtering by `State`/plan handle is **client-side** | Case B — `SdkException<RawError>` (404 if the customer id is unknown) | none (no page params) | `operations/Customers.md`, `records-3-Of-Su.md` |
| 9 | Refresh one stored subscription (state / next billing date) | `Task<SubscriptionResponse> client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default → pass `null` explicitly (skip self-service token) | Path only: `/subscriptions/{subscription_id}` ← stored `subscription.Id` | `SubscriptionResponse.Subscription` — as op 7 | Case B — `SdkException<RawError>`; 404 = raw `NotFound` | none | `operations/Subscriptions.md` |
| 10 | (Alternative guard) find a subscription by its own reference | `Task<SubscriptionResponse> client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → pass explicitly | Query: `/subscriptions/lookup.json` `reference` ← subscription-level reference (only useful if op 7 sets `Reference`) | `SubscriptionResponse.Subscription` — as op 7 | Case A — `SdkException<FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

### Enum values actually used in code

All in namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>` records — build/compare via the static
member (`SubscriptionState.Active`), never a wire string.

| Enum | Members (C# name `(wire)`) | Used for | Source |
|---|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | op 7/8/9 `.State` reads; the same-plan guard compares against `Active` (and any other state the app treats as "current" — app decision) | `models/enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | op 2/3 `.IntervalUnit` reads → display "monthly/€x" | `models/enums.md` |

Other enum-typed *parameters* appear only as `null` arguments (no members constructed): `BasicDateField`,
`ListProductsFilter` is a record (skip), `ListProductsInclude`, `SortingDirection`, `ListProductsInclude`.
For the record, members: `BasicDateField`: `UpdatedAt (updated_at)`, `CreatedAt (created_at)`;
`SortingDirection`: `Asc (asc)`, `Desc (desc)`; `ListProductsInclude`: `PrepaidProductPricePoint
(prepaid_product_price_point)`; `SubscriptionInclude`: `Coupons (coupons)`, `SelfServicePageToken
(self_service_page_token)`.

### Client construction, auth, servers (scenario 1)

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` **version `1.0.2`** (latest on NuGet; the map is generated from the same source, tag `v1.0.2` — map and package agree). Install: `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` | `sdk-map.md`; nuget.org package page |
| Root namespace / using | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` — sole constructor `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (namespace `MaxioAdvancedBilling`) | `sdk-map.md`; `MaxioAdvancedBillingClient.cs` |
| Options class | `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`) — members: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs` |
| Auth scheme | HTTP Basic on every call. `BasicAuthCredentials` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`; `required string Username`, `required string Password`) — set **`Username` = API key, `Password` = literal `"x"`**. Credentials are baked into `options.BasicAuth` before construction / in the configure callback | `sdk-map.md`; `BasicAuthCredentials.cs` |
| Environment (hosting region, not prod/sandbox) | `ServerEnvironment` (namespace `MaxioAdvancedBilling.Servers`): `Us` → `https://{site}.chargify.com` (default), `Eu` → `https://{site}.ebilling.maxio.com`. **There is no "sandbox" selector — the sandbox vs live distinction lives in which subdomain + API key you configure.** Set `Environment = ServerEnvironment.Us` (default) | `sdk-map.md`; `ServerEnvironment.cs` |
| Custom base address | Override chain: `options.Server.Production.Us.BaseUrl` (and `.Us.Site`). `ServerOptions` (namespace `MaxioAdvancedBilling`) has `Production: ProductionOptions` and `Ebb: EbbOptions`; `ProductionOptions` (namespace `MaxioAdvancedBilling.Servers`) has `Us`/`Eu`, each with `BaseUrl: string` and `Site: string`. Default `Us.BaseUrl = "https://{site}.chargify.com"`, default `Us.Site = "subdomain"`. Consequence: with **no** `Maxio:BaseUrl`, set `Us.Site = <Maxio:Subdomain>` and keep the default `Us.BaseUrl` (the `{site}` token is replaced by `Site`); with `Maxio:BaseUrl` set, assign `Us.BaseUrl = <Maxio:BaseUrl>` **verbatim** and `Site` is unused (no `{site}` token remains to substitute) | `sdk-map.md`; `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| Retry/timeout | `options.Retry: RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`; members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout: TimeSpan?`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` — all `required`, so start from `RetryOptions.Default()`). Defaults and semantics are NOT in the map | `sdk-map.md` |
| DI registration | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — extension on `IServiceCollection`; internally calls `services.AddHttpClient()` and registers the client as **singleton** built from `IHttpClientFactory.CreateClient()` | `sdk-map.md`; `ServiceCollectionExtensions.cs` |
| Version header / extra config | None required. Options surface is exactly `Environment`, `Retry`, `Server`, `BasicAuth` — there is no version-header or custom-header option; extra headers would require an `HttpMessageHandler` on the `HttpClient` (app-level decision, not an SDK contract) | `MaxioAdvancedBillingClientOptions.cs` |
| Target frameworks | `netstandard2.0`. Runtime deps (transitive): `Microsoft.Extensions.Http >= 10.0.8`, `Polly >= 8.6.5`, `System.Net.Http.Json >= 10.0.8`, `System.Net.ServerSentEvents >= 10.0.8` — the 10.x dependency line needs a host on a supported modern framework | `sdk-map.md`; nuget.org |

### Error-type index (scenario 6) — namespaces and members

| Type | Namespace | Members | Source |
|---|---|---|---|
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `sealed`, `: Exception`; `TError Error { get; }` (`required init`) | `Core/Exceptions/SdkException.cs` |
| Typed `…Error` classes (163 ops) | `MaxioAdvancedBilling.Errors` | `: ApiError`; per-op `TryGet…` accessors (each maps to one HTTP status) **plus** inherited `TryGetRawError(out RawError)` (true only when the status fell through to the raw fallback) | `sdk-map.md`; e.g. `Errors/CreateSubscriptionError.cs` |
| `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | abstract base; `TryGetRawError(out RawError error): bool` | `Core/ErrorResponse/ApiError.cs` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `StatusCode: System.Net.HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` | `Core/ErrorResponse/RawError.cs` |

`Record model / wire` references above are in `MaxioAdvancedBilling.Models`; error payload records (the `out`
types) are ordinary records in `MaxioAdvancedBilling.Models` too.

## 3. Settings / DI / lifetime notes

- Config binding keys (host-provided): `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`,
  `Maxio:BaseUrl` (optional). Map: `ApiKey` → `BasicAuth.Username` (`Password` is the literal `"x"`);
  `Subdomain`/`BaseUrl` → `options.Server.Production.Us.Site`/`.BaseUrl` as in the §2 table. If the sandbox
  site is EU-hosted the `Environment` would be `Eu`; otherwise leave the default `Us`.
- ⚠ Step 2 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are
  **not** the timeout on the `HttpClient` you register, and the client must sit on a long-lived
  `HttpClient`/`IHttpClientFactory` — the singleton wrapper is not per-request. **MUST load
  `dotnet-client-initialization`** before registering the client.
- ⚠ Step 2 (auth) — credentials must be in `options.BasicAuth` when the client is built (or in the DI
  configure callback); the scheme is Basic with password literally `"x"`, and a 401 means the key/subdomain
  pair (or region) is wrong. **MUST load `dotnet-authentication`** before wiring credentials / diagnosing
  401.
- ⚠ Step 2 (resilience) — see the retry trap in the client row above: which verbs/statuses are retried, what
  `Timeout` bounds, and what you still must wire yourself are not in the map. **MUST load
  `dotnet-configuration-resilience`** before tuning anything on `options.Retry`.
- Namespace cheat-sheet for `using` directives: client+options+`ServerOptions`+DI extension →
  `MaxioAdvancedBilling`; controllers are reached via client properties (no extra `using` needed, but the
  property types live in `MaxioAdvancedBilling.Api`); records → `MaxioAdvancedBilling.Models`; enums →
  `MaxioAdvancedBilling.Models.Enums`; auth → `MaxioAdvancedBilling.Core.Authentication.Basic`; exceptions →
  `MaxioAdvancedBilling.Core.Exceptions` + `MaxioAdvancedBilling.Core.ErrorResponse` + `MaxioAdvancedBilling.Errors`;
  environment/server option leaf types → `MaxioAdvancedBilling.Servers`.

## 4. Error-boundary contract (scenario 6) — implementation checklist

The host's translation layer (one place, used by every SDK call) must implement:

1. **Catch the right exception type per operation** — Case B ops throw `SdkException<RawError>`; Case A ops
   throw `SdkException<{Operation}Error>` for **every** non-2xx status, with the typed accessor true only for
   its one mapped status and `ex.Error.TryGetRawError(out RawError)` true for everything else (it is **not**
   a catch-all while the typed branch is taken — on the typed status it returns `false`). The per-op case and
   accessor names are in each §2 row.
2. **401/403** → surfaces as `RawError.StatusCode == Unauthorized/Forbidden` (any op, typed or raw) — before
   blaming call sites, verify `BasicAuth.Username` (API key) + subdomain + region per §3.
3. **404** → per op: `ReadCustomerByReference`, `ReadProductByHandle`, `ListCustomerSubscriptions`,
   `ReadSubscription`, `ListProductFamilies` (all Case B): `ex.Error.StatusCode == HttpStatusCode.NotFound`.
   `ListProductsForProductFamily`: `TryGetString(out string)` maps 404. `FindSubscription`:
   `TryGetNoContent(out RawError)` maps 404. Not-found on the reference lookup is the **normal** find-or-create
   path, not an error boundary case.
4. **422** → `CreateCustomer`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)`; `CustomerErrorResponse1`
   carries only `Errors (errors): Errors?` where the `Errors` record models **only** the keys `per_page` and
   `price_point` — the per-field map Advanced Billing returns (e.g. a duplicate-`reference` message under a
   `customer` key) is **dropped on deserialize**, and on 422 `TryGetRawError` is `false`, so the body text is
   unreachable. Consequence: detect "duplicate reference" by **status 422 then re-run the lookup by
   reference** and adopt the found customer; do not attempt to read the message. `CreateSubscription`:
   `TryGetErrorListResponse1(out ErrorListResponse1)` with `Errors: IReadOnlyList<string>` (wire `errors`,
   flat messages).
5. **429** → `RawError.StatusCode == TooManyRequests` (raw or fallback) — the retry policy decision belongs to
   the resilience skill, not the boundary.
6. **Structured vs raw body** — read via accessors first; when the body must be inspected generically (only on
   statuses where the raw fallback is active), use `raw.ReadAsString()` or
   `raw.ReadAsJson<System.Text.Json.JsonDocument>()`.
7. **Untyped exception paths** — `System.Net.Http.HttpRequestException` (transport) and
   `System.Text.Json.JsonException` (deserialization) can reach the boundary from SDK internals and are **not**
   `SdkException`s. Do not map every `JsonException` to an outage; see the two hazard rows in §5.
8. Async convention: every call is awaited; flow the request's `CancellationToken` through `ct:`.

## 5. REQUIRED READING

Load every skill below **before implementation starts** (per `integrate-maxio` Step 1c). The sheet
deliberately does not carry their contents — a pointer names the hazard so you load the skill that resolves
it.

Always include, verbatim, **both** of these hazard rows — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the
FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives
afterwards arrives too late to shape it.

| Skill | Why it must be loaded (governs…) |
|---|---|
| `dotnet-client-initialization` | Client construction & DI registration — HttpClient ownership/lifetime, singleton vs transient, `AddMaxioAdvancedBillingClient` (step 2). |
| `dotnet-authentication` | Basic-auth wiring — username = API key / password `"x"`, where credentials must be set, and 401 diagnosis (step 2). |
| `dotnet-configuration-resilience` | Retry/timeout/base-URL semantics that the option names do not reveal (steps 2, and 429 handling in §4). |
| `dotnet-calling-endpoints` | Calling operations — optional params without C# defaults must be passed explicitly by name; `ct:`; envelopes. Steps 3–10 every call site. |
| `dotnet-models` | Building request models (`init`-only, `required` members), response-envelope reads, `StringEnum<T>` members vs wire strings (steps 3–7). |
| `dotnet-error-handling` | The whole §4 boundary — typed vs raw catches, the two `JsonException` hazard rows above, status handling. |
| `dotnet-testing` | Before writing tests that fake the SDK — which seam to fake (the `HttpClient` constructor argument), what to assert. |

## 6. Assumptions & Blockers

**Major assumptions**

- **A1 — Host target framework.** The SDK is `netstandard2.0`, but its transitive dependencies
  (`Microsoft.Extensions.Http` ≥ 10.0.8 and friends) are the 10.x line, which resolves only on a modern
  host. Assumed the `src/PublicApi` project targets **.NET 8 or newer** so `dotnet add package … --version
  1.0.2` restores cleanly. `YOUR CALL — not in the map` (repo survey): confirm the PublicApi TFM before the
  package add; if it targets an older framework this is a Blocker.
- **A2 — Create-subscription minimalism is acceptable.** The sandbox plans are flat-price with
  payment-method-not-required (stated in the brief), so `CreateSubscription` with only `product_handle` +
  `customer_reference` and no payment profile is the minimum; if the product/family carried required
  metered/quantity components, creation would need the `Components` array (`IReadOnlyList<CreateSubscriptionComponent>`)
  — not contractable from the map. `YOUR CALL — not in the map` (provider-side plan config).
- **A3 — Resulting subscription state.** After a successful no-card subscription creation the host must not
  assume which `State` value comes back (`Active` vs `AwaitingSignup`/others). The enum and the
  guard/filtering mechanics are contracted above; the actual landed state for this sandbox configuration is
  `UNVERIFIED` (only live traffic settles it). Display the returned state; treat only the states the app
  chooses as "current" for the same-plan guard. Consequence: do not hard-fail on an unexpected-but-valid
  state.
- **A4 — Nested `Subscription.Product` shape.** The SDK types it as the full `Product` record; Advanced
  Billing's live subscription payload nests an abbreviated product. Fields outside the abbreviated shape
  (e.g. `PriceInCents` on the nested object) may be null — read price from the subscription-level
  `ProductPriceInCents` instead. `UNVERIFIED` for the exact live nesting; safe read = subscription-level
  fields only.
- **A5 — Product-family resolution needs an extra call.** There is no "read family by handle" operation
  (`ReadProductFamily` takes `int id` only), so the family is resolved client-side from
  `ListProductFamilies` by matching `ProductFamily.Handle`, then listed via
  `ListProductsForProductFamily(familyId)`. Passing the handle string straight into the
  `product_family_id` path slot is `UNVERIFIED` (the SDK accepts a string there, but whether the wire accepts
  `handle:…` is not in the map) — do not rely on it.

**Minor assumptions**

- The reference value is the eShop user-id `Guid` string, stable for the user's lifetime; Maxio
  `reference` must never change for that user after creation or lookups break.
- First/last name and email for `CreateCustomer` come from the authenticated shopper's identity claims;
  the PublicApi JWT/claim-to-user mapping is the host's existing concern.
- The catalog has ≤ `perPage` plans in the sandbox (page 1 suffices), but op 2's loop should still honor the
  fewer-than-`perPage` stop rule.
- Double-submit protection is app-level: the same-plan guard (op 8) is read-then-write and does not
  serialize two concurrent POSTs — an app-level lock/unique constraint is the host's call (`YOUR CALL`); the
  only server-enforced idempotency fact the SDK exposes is the customer-`reference` uniqueness on
  `CreateCustomer` (op 5 → 422 duplicate), which the find-or-create flow already absorbs.
