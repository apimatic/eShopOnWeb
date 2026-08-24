# Maxio plan — eShopOnWeb recurring-subscription billing (PublicApi, JWT-authenticated)

Grounded against the bundled SDK map (`sdk-map.md`, `map/operations/*`, `map/models/*`; SDK stamp: commit `15db14b`, tag `v1.0.2`). One source-file lookup was made where the map was silent; it is cited inline.

## 1. Scope & sequence

| # | Step | Operations used |
|---|------|-----------------|
| 1 | Add NuGet package; bind `Maxio:` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) | — |
| 2 | Register `MaxioAdvancedBillingClient` in DI with Basic auth + server/site or BaseUrl override | — |
| 3 | `GET /api/subscription-plans` — list plans in the configured family | `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — find-or-create customer, idempotency check, create subscription, return plan/price/state/next-billing | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` → `Customers.ListCustomerSubscriptions` → `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve caller's customer, list their subscriptions | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 6 | Integration error boundary (SDK exceptions → HTTP responses) | all of the above |
| 7 | Tests for the integration layer | — |
| 8 | (Optional) record metered usage against component `api-call` | `SubscriptionComponents.CreateUsage` |

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

### 2.1 Package, client, auth, server (map: `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk`, version **1.0.2** (the ref this sheet is grounded on) — `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`) |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` (source: `ServiceCollectionExtensions.cs`, root namespace) |
| Auth type | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` — `Username` = **API key**, `Password` = **literal `"x"`** |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` — `.Us` (default, `https://{site}.chargify.com`) / `.Eu` (`https://{site}.ebilling.maxio.com`) |
| Site (subdomain) | `options.Server.Production.Us.Site = "<subdomain>"` (fills `{site}` in the template) |
| BaseUrl override | `options.Server.Production.Us.BaseUrl = "<verbatim url>"` — replaces the derived template entirely |
| Retry config (if tuned) | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — all members `required`; start from `RetryOptions.Default()` |

Config mapping (no hard-coded values):

| `Maxio:` key | Wires to |
|---|---|
| `ApiKey` | `BasicAuth = new BasicAuthCredentials { Username = ApiKey, Password = "x" }` |
| `Subdomain` | `Server.Production.Us.Site` (used only when `BaseUrl` is **not** set) |
| `BaseUrl` (optional) | when non-empty: `Server.Production.Us.BaseUrl = BaseUrl` verbatim, and skip `Site` |
| `ProductFamilyHandle` | passed to `ListProductsForProductFamily` as `"handle:" + ProductFamilyHandle` (see 2.2-a) |

`Environment = ServerEnvironment.Us` unless the account is EU-hosted (see Assumptions).

### 2.2 Operations (signatures verbatim; every row cites its map page)

**a. List plans in the configured product family** — `operations/ProductFamilies.md`

```csharp
client.ProductFamilies.ListProductsForProductFamily(
    string productFamilyId,                 // pass "handle:eshop-subscribe" (config: "handle:" + Maxio:ProductFamilyHandle)
    BasicDateField? dateField,              // pass null
    ListProductsFilter? filter,             // pass null
    DateTimeOffset? startDate,              // pass null
    DateTimeOffset? endDate,                // pass null
    DateTimeOffset? startDatetime,          // pass null
    DateTimeOffset? endDatetime,            // pass null
    bool? includeArchived,                  // pass null (or false)
    ListProductsInclude? include,           // pass null
    int? page = 1, int? perPage = 20,
    CancellationToken ct = default)
// returns IReadOnlyList<ProductResponse>
```

- The 8 params `dateField`…`include` are **nullable with no C# default — pass each explicitly (`null`)**, ideally as named arguments.
- `productFamilyId` accepts **"either the product family's id or its handle prefixed with `handle:`"** — confirmed in the SDK source doc comment for this parameter (`Api/ProductFamilies.cs`, the file this map row names); the map row itself does not state it. So the family handle from config is used directly: no family-id resolution call is needed.
- (`Products.ListProducts` exists but its `ListProductsFilter` model has only `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` — **no family filter** (`records-2-Cr-Ne.md`). The family endpoint above is the correct path.)
- Error: **Case A** — `SdkException<ListProductsForProductFamilyError>` (`MaxioAdvancedBilling.Errors`); accessors: `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` [fallback].
- Pagination: manual `page`/`perPage` (defaults 1/20; per the source doc, `perPage` over 200 is clamped to 200). Response is a **bare list, no metadata envelope** — an empty list means end of pages.

**b. Find customer by stable external reference** — `operations/Customers.md`

```csharp
client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)
// GET /customers/lookup.json?reference=…  →  CustomerResponse
```

- Exact single-match lookup (the `ListCustomers` `q` param is a looser search — do not use it for identity resolution).
- Error: **Case B** — `SdkException<RawError>`; "no such customer" = `ex.Error.StatusCode == HttpStatusCode.NotFound` (404). This 404 is the find-or-create pivot, not a failure.

**c. Create customer** — `operations/Customers.md`, `records-1-Ac-Cr.md`

```csharp
client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)
// → CustomerResponse
```

- `CreateCustomerRequest` field: `Customer (customer): CreateCustomer` **`!req`**.
- `CreateCustomer` fields: `FirstName (first_name): string` **`!req`**, `LastName (last_name): string` **`!req`**, `Email (email): string` **`!req`**, `Reference (reference): string?` (optional in the model, but **this integration always sets it** — it is the idempotency key; the API enforces uniqueness of `reference` per the operation's notes), plus optional `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, etc.
- Error: **Case A** — `SdkException<CreateCustomerError>`; accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback].
  - ⚠ Shape caution (map-visible): `CustomerErrorResponse1.Errors (errors)` is typed `Errors`, whose only modeled fields are `PerPage (per_page)` and `PricePoint (price_point)` (`records-2-Cr-Ne.md`) — almost certainly a generator name-collision, not the real customer-validation body. Unmodeled JSON fields are dropped on deserialize, so the typed 422 accessor may yield an empty shell. **Defensive directive:** on 422 from `CreateCustomer`, read the detail via `TryGetRawError(out var raw)` → `raw.ReadAsString()` as the primary message source; treat the typed payload as best-effort. What the live 422 body actually carries is `UNVERIFIED` (only live traffic could confirm).

**d. Create subscription** — `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md`

```csharp
client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)
// → SubscriptionResponse
```

- `CreateSubscriptionRequest` field: `Subscription (subscription): CreateSubscription` **`!req`**.
- `CreateSubscription` fields this integration sets (all optional in the model — nothing is `!req`):
  - `ProductHandle (product_handle): string?` — **reference the product by handle** (`"eshop-pro"` / `"basic-plan"`). (`ProductId (product_id): int?` is the alternative; handles are stable, numeric IDs are not — use the handle.)
  - `CustomerId (customer_id): int?` — the numeric id from the find-or-create step. (`CustomerReference (customer_reference): string?` is the alternative; either works — `CustomerId` is preferred since the find-or-create already returned it.)
  - `Reference (reference): string?` — the subscription's own reference; set a deterministic value (e.g. `"{userRef}:{productHandle}"`) as a second idempotency lever (see 2.5).
  - No payment-profile fields — the seeded catalog requires no payment method.
- Response: `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable, null-check it** (unlike `ProductResponse.Product` / `CustomerResponse.Customer`, which are `!req`).
- Read from `Subscription` (`records-3-Of-Su.md`): `Id (id): int?` · `State (state): SubscriptionState?` · `Product (product): Product?` (nested — `Name`, `Handle`, `PriceInCents`) · `ProductPriceInCents (product_price_in_cents): long?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `Customer (customer): Customer?` · `Reference (reference): string?`.
  - **There is no `next_billing_at` on the response record.** The `UpdateSubscription` notes confirm the server does not return `next_billing_at` and says to view `current_period_ends_at`. "Next billing date" for the API DTO = `NextAssessmentAt ?? CurrentPeriodEndsAt`.
- Error: **Case A** — `SdkException<CreateSubscriptionError>`; accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **`!req`** (`records-2-Cr-Ne.md`) — the 422 message list.

**e. List the caller's subscriptions** — `operations/Customers.md`

```csharp
client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)
// GET /customers/{customer_id}/subscriptions.json  →  IReadOnlyList<SubscriptionResponse>
```

- Filter is by **numeric customer id only** — resolve the caller's reference via `ReadCustomerByReference` first; 404 there ⇒ the caller has no subscriptions (return an empty list, not an error).
- Error: **Case B** — `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, …).
- Pagination: **none** (no page params; full array returned).
- (`Subscriptions.ListSubscriptions` has `state`/`product` filters but **no customer filter** — not usable for "my subscriptions".)

**f. (Optional) Record metered usage on component `api-call`** — `operations/SubscriptionComponents.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md`, `unions.md`

```csharp
client.SubscriptionComponents.CreateUsage(
    SubscriptionIdOrReference subscriptionIdOrReference,  // SubscriptionIdOrReference.Int(subscriptionId)
    ComponentIdModel componentId,                         // ComponentIdModel.String("handle:api-call")
    CreateUsageRequest? body,                             // new CreateUsageRequest { Usage = new CreateUsage { Quantity = n, Memo = … } }
    CancellationToken ct = default)
// → UsageResponse
```

- Unions (namespace `MaxioAdvancedBilling.Models.AnyOf`): `SubscriptionIdOrReference` = int|string, factories `.Int(int)` / `.String(string)`; `ComponentIdModel` = int|string, factories `.Int(int)` / `.String(string)`. The `componentId` doc comment (SDK source `Api/SubscriptionComponents.cs`, the file the map row names) confirms **"the component's handle prefixed by `handle:`"** is accepted — so the seeded numeric component id is never needed.
- `CreateUsageRequest.Usage (usage): CreateUsage` **`!req`**; `CreateUsage.Quantity (quantity): double?`, `Memo (memo): string?`.
- Response `UsageResponse.Usage (usage): Usage` **`!req`** — `Id`, `Quantity` (union), `ComponentHandle`, `SubscriptionId`.
- Error: **Case A** — `SdkException<CreateUsageError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError` [fallback].

### 2.3 Enums needed (map: `enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>` records, **not C# enums** — use the static members (`SubscriptionState.Active`) or `FromValue("active")`; never `.ToString()`-compare raw strings blindly.

| Enum | Members (C# name = wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` (both seeded plans: `Interval = 1`, `IntervalUnit = Month`) |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` — only if date-filtering is ever added; hero flow passes `null` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` — hero flow passes `null` |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` — only relevant if `ListSubscriptions` is ever adopted; not needed now |

### 2.4 Error-handling matrix (map: `sdk-map.md` error model + per-op rows above)

All operations are **throw-only** (no `…Result` no-throw variants exist in this SDK). `SdkException<TError>` exposes `.Error`; `SdkException<>` lives at `Core/Exceptions/SdkException.cs` ⇒ namespace `MaxioAdvancedBilling.Core.Exceptions`; `RawError` at `Core/ErrorResponse/RawError.cs` ⇒ `MaxioAdvancedBilling.Core.ErrorResponse` with `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.

| Operation | Error type | 401 | 404 | 422 |
|---|---|---|---|---|
| `ListProductsForProductFamily` | `SdkException<ListProductsForProductFamilyError>` | `TryGetRawError` → `StatusCode` | `TryGetString(out string)` | — |
| `ReadCustomerByReference` | `SdkException<RawError>` | `Error.StatusCode` | `Error.StatusCode == NotFound` ⇒ **create path** | — |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | `TryGetRawError` | — | `TryGetCustomerErrorResponse1` (+ raw fallback — see shape caution in 2.2-c) |
| `ListCustomerSubscriptions` | `SdkException<RawError>` | `Error.StatusCode` | `Error.StatusCode` | — |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | `TryGetRawError` | — | `TryGetErrorListResponse1(out ErrorListResponse1)` → `.Errors` message list |
| `CreateUsage` (optional) | `SdkException<CreateUsageError>` | `TryGetRawError` | — | `TryGetErrorListResponse1` |

No operation here has a typed 401 accessor — a 401 always arrives via the raw path. Before touching call sites for a 401, check config-shaped causes: `Username` = API key with `Password = "x"`, correct `Site`/subdomain or `BaseUrl`, US-vs-EU environment.

Suggested outward mapping: 401 → 502/500 ("billing upstream auth failed", log loudly — it means our config, not the caller); 404 on lookup ops → empty result / create path; 422 → 400/409 with the provider messages; anything else → 502.

### 2.5 Idempotency design (grounded in the operations above)

1. **Customer — find-or-create by reference.** Derive a stable reference from the caller's identity (e.g. `"eshop-user:{IdentityUserId}"`). `ReadCustomerByReference(ref)`; on Case-B 404, `CreateCustomer` with `Reference = ref`. The API enforces `reference` uniqueness (operation notes, `operations/Customers.md`), so a lost race surfaces as a 422 on create — catch it and re-`ReadCustomerByReference` to get the winner's id.
2. **Subscription — check-before-create.** Before `CreateSubscription`, call `ListCustomerSubscriptions(customerId)` (already needed for `GET /api/my-subscriptions`) and look for a subscription whose `Product?.Handle` equals the requested handle **and** whose `State` is not one of the terminal values visible in the enum list — `Canceled`, `Expired`, `FailedToCreate`, `TrialEnded`. If found, return the existing subscription (200-style response) instead of creating a duplicate.
3. **Second lever (cheap, recommended).** Set `CreateSubscription.Reference = "{userRef}:{productHandle}"`; a pre-create `Subscriptions.FindSubscription(reference)` (`operations/Subscriptions.md` — Case A, `TryGetNoContent(out RawError)` [404]) detects a prior success even if the customer-id path is bypassed.
4. **Retry-layer caveat.** Whether a failed `CreateSubscription` POST can be re-sent by the SDK's own retry layer (and which failures that covers) is a resilience-semantics hazard — it is named, not resolved, in the trap notes below; the checks in (2)/(3) are what make a re-executed POST safe regardless.

### 2.6 Pagination summary

| Operation | Params | Response shape |
|---|---|---|
| `ListProductsForProductFamily` | `page` (default 1), `perPage` (default 20, clamped to 200) | bare `IReadOnlyList<ProductResponse>` — no metadata envelope; empty list = last page |
| `ListCustomerSubscriptions` | none | full `IReadOnlyList<SubscriptionResponse>` |
| `ListCustomers` (not in hero flow) | `page` (1), `perPage` (50) | bare `IReadOnlyList<CustomerResponse>` |

The seeded catalog has 2 products — one page with the defaults suffices; still loop-until-empty if you want to be safe.

## 3. Trap notes (named hazards — load the skill; answers deliberately NOT inlined)

- ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the SDK has lifetime rules that differ from the SDK client wrapper's; getting either wrong leaks sockets or defeats pooling. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)` or the `AddMaxioAdvancedBillingClient` registration.
- ⚠ Step 2 (auth) — when in the construction sequence credentials must be supplied, and loading the key from configuration vs. code, are not visible from the options shape. **MUST load `dotnet-authentication`** before wiring `BasicAuthCredentials`.
- ⚠ Steps 3–5 (every call) — list/read operations take many nullable parameters with **no C# default**; positional calls mis-bind silently. **MUST load `dotnet-calling-endpoints`** before the first `client.{Api}.{Operation}(...)` call.
- ⚠ Steps 3–5 (models) — enums are `StringEnum<T>` (not C# enums), unions are built via factories and read via `TryGet…`, and unmodeled JSON fields are dropped on deserialize (this is why the `CustomerErrorResponse1.Errors` caution in 2.2-c bites). **MUST load `dotnet-models`** before constructing request payloads or mapping responses onto eShopOnWeb DTOs.
- ⚠ Step 6 (error boundary) — which operations are Case A vs Case B is per-operation (see 2.4), `TryGetRawError` is not a catch-all on typed errors, and there are no no-throw `Result` variants in this SDK. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Step 2/4 (resilience) — whether a failed non-idempotent write (`CreateSubscription`, `CreateCustomer`) can be re-sent by the SDK's retry layer, what `Timeout` actually bounds, and what you must still wire yourself are not revealed by the option names. **MUST load `dotnet-configuration-resilience`** before tuning `Retry`/`Timeout` or relying on defaults.
- ⚠ Step 7 (tests) — the test seam for stubbing the SDK is specific (a constructor argument), and tests should assert behaviour, not SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — governs step 2 (client construction & DI registration).
- `dotnet-authentication` — governs step 2 (Basic credentials, config-sourced key).
- `dotnet-calling-endpoints` — governs steps 3–5 (parameter passing, envelopes, async/cancellation).
- `dotnet-models` — governs steps 3–5 (request models, enums, unions, wire names).
- `dotnet-error-handling` — governs step 6 (the exception boundary). Mandatory for every integration:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — governs steps 2 & 4 (retries, timeouts, base URL, pagination).
- `dotnet-testing` — governs step 7 (stubbing the SDK in tests).

## 5. Assumptions & Blockers

**Assumptions (minimal, safe):**

1. **US hosting** — `ServerEnvironment.Us` is the SDK default and matches `*.chargify.com` sandboxes; if the account is EU-hosted, set `Environment = ServerEnvironment.Eu` (or use `Maxio:BaseUrl`, which overrides host derivation entirely).
2. **Customer reference derivation** — the exact stable reference (e.g. `eshop-user:{IdentityUserId}` from the JWT `sub`) is an app-side choice; the contract only requires it be stable and unique per eShopOnWeb user.
3. **`CreateCustomer` requires non-empty `FirstName`/`LastName`/`Email`** (`!req` in the model). If eShopOnWeb identity lacks names, the implementer supplies safe fallbacks (e.g. derive from the username) — an app-side mapping concern, not an SDK gap.
4. **Prices are integer cents** (`PriceInCents`, `ProductPriceInCents`) — divide by 100 for display; no currency field is read in the hero flow.
5. **Seeded catalog needs no payment profile** — per the brief, both plans subscribe without card capture, so no payment fields are sent. If the catalog ever changes, the signal is a 422 from `CreateSubscription` (read via `ErrorListResponse1.Errors`).
6. **Metered component `api-call`** — usage recording (2.2-f) is fully contracted but optional; no endpoint is required for it in this scope.
7. `UNVERIFIED` items (only live traffic could confirm): the real field content of a live `CreateCustomer` 422 body (see 2.2-c); whether live list responses populate every nullable model field read here (all response fields read are nullable in the models — code must null-tolerate).

**Blockers:** none. Every required capability — family-handle product listing, exact-match customer lookup by reference, customer create, subscription create by product handle + customer id, per-customer subscription listing, and (optionally) handle-addressed usage recording — is present in the SDK surface as mapped.
