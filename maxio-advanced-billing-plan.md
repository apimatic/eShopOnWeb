# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Additive, parallel subscription-billing capability on **`src/PublicApi`** (JWT).
Maxio Advanced Billing is the **system of record**. Three endpoints:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.

Grounded against the SDK map + source at branch `main` (spec `1.0`), read this session.

---

## 1. Scope & sequence

Layering follows eShopOnWeb (`ApplicationCore` abstractions, `Infrastructure` implementation,
`PublicApi` endpoints). The SDK is vendored into the repo as a project and referenced from
`Infrastructure` only (the SDK is not on NuGet — build-from-source per getting-started).

1. **Vendor SDK** — copy the SDK source into `src/MaxioAdvancedBilling.Sdk/`, opt it out of the
   repo's Central Package Management, add to `eShopOnWeb.sln`, `ProjectReference` from `Infrastructure`.
2. **ApplicationCore** — `MaxioSettings` (config POCO), `ISubscriptionBillingService` (abstraction),
   boundary DTOs (`SubscriptionPlan`, `CustomerSubscription`, `SubscribeRequest`/result),
   `SubscriptionBillingException` (surfaces provider failures to the endpoint boundary).
3. **Infrastructure** — `AddMaxioBilling(config)` extension: bind+validate `Maxio:` (fail-fast),
   register the SDK client via `AddMaxioAdvancedBillingClient`, register `MaxioSubscriptionBillingService`.
   The service implements the three flows using these operations:
   - **list-plans** → `client.ProductFamilies.ListProductFamilies` (resolve configured handle → family id)
     then `client.ProductFamilies.ListProductsForProductFamily(familyId, …)`.
   - **subscribe** → ensure customer (`Customers.ReadCustomerByReference` → else `Customers.CreateCustomer`,
     catch 422 → re-read) → idempotency pre-check (`Subscriptions.FindSubscription(reference)`; if a live
     subscription exists return it) → `Subscriptions.CreateSubscription` (catch 422 → re-read via
     `FindSubscription`) → return plan/price/state/next-billing-date.
   - **my-subscriptions** → `Customers.ReadCustomerByReference` (404 → empty) →
     `Customers.ListCustomerSubscriptions(customerId)`.
4. **PublicApi** — three `IEndpoint<…>` endpoints (MinimalApi.Endpoint convention, JWT `[Authorize]`),
   request/response DTOs on `BaseRequest`/`BaseResponse`, resolve caller via `UserManager<ApplicationUser>`
   from the token's name claim; wire `AddMaxioBilling` in `Program.cs`.
5. **Secrets** → `.NET user-secrets` on `PublicApi` (values never in repo).
6. **Tests** → integration-layer tests faking the SDK's `HttpClient` seam.

Identity mapping (eShop userId ↔ Maxio) is stored **in Maxio** as the customer `reference` (= eShop
`ApplicationUser.Id`) and the subscription `reference`. This is deliberate: the only local DB available is
the in-memory provider, which loses data on restart and enforces no constraints, so a local mapping table
would be neither durable nor a valid uniqueness claim. Maxio (the system of record) is durable and enforces
customer-reference uniqueness. **No local persistence is introduced.**

---

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier; the cancellation-token parameter is named `ct`, so named args write `ct:`. Optional
> reference-type params with no default (nullable, "must pass explicitly") must be passed — pass `null` to skip.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`; `Models/Enums/` → `…Models.Enums`;
> `Errors/` → `…Errors`; `Core/Authentication/Basic/` → `…Core.Authentication.Basic`;
> `Servers/` → `…Servers`; client/options/`ServerOptions` → root `MaxioAdvancedBilling`).

### Operations

| op | controller · signature | request model + fields used | response envelope → inner fields read | error case + accessors | pagination | source |
| --- | --- | --- | --- | --- | --- | --- |
| ListProductFamilies | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass first 5 as `null` | none | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`.Id`, `.Handle`) | Case B `SdkException<RawError>` | none (defaults) | map ProductFamilies.md; `Models/ProductFamilyResponse.cs`, `Models/ProductFamily.cs` |
| ListProductsForProductFamily | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `productFamilyId` = resolved family **id** as string; pass `dateField..include` `null` except `includeArchived:false`; page through | none | `IReadOnlyList<ProductResponse>` → `.Product` (`.Handle`,`.Name`,`.Description`,`.PriceInCents`,`.Interval`,`.IntervalUnit`,`.ArchivedAt`,`.RequireCreditCard`) | Case A `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | **page-based**, `per_page` (default 20) — loop pages until a short page | map ProductFamilies.md; `Models/ProductResponse.cs`, `Models/Product.cs` |
| ReadCustomerByReference | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none (query `reference`) | `CustomerResponse` → `.Customer` (`.Id`,`.Reference`,`.Email`) | Case B `SdkException<RawError>` (404 when absent → `.StatusCode`) | none | map Customers.md; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| CreateCustomer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `body` | `CreateCustomerRequest{ Customer: CreateCustomer }`; `CreateCustomer` **required**: `FirstName`,`LastName`,`Email`; set `Reference` (= eShop userId) | `CustomerResponse` → `.Customer.Id` | Case A `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | map Customers.md; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/CustomerErrorResponse1.cs` |
| ListCustomerSubscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` (see Subscription fields) | Case B `SdkException<RawError>` | none (defaults) | map Customers.md; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| FindSubscription | `client.Subscriptions.FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `reference` | none (query `reference`) | `SubscriptionResponse` → `.Subscription` | Case A `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | map Subscriptions.md; `Errors/FindSubscriptionError.cs` |
| CreateSubscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `body` | `CreateSubscriptionRequest{ Subscription: CreateSubscription }`; on `CreateSubscription` set `ProductHandle` (plan handle), `CustomerId` (ensured customer id), `Reference` (deterministic idempotency key) | `SubscriptionResponse` → `.Subscription` (`.Id`,`.State`,`.CurrentPeriodEndsAt`,`.NextAssessmentAt`,`.ProductPriceInCents`,`.Reference`,`.Product.Handle`,`.Product.Name`) | Case A `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | map Subscriptions.md; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/ErrorListResponse1.cs` |

### `CreateCustomer` optional fields carried (each with purpose)

| field (wire) | type | purpose |
| --- | --- | --- |
| `Reference` (reference) | string? | idempotency claim = eShop `ApplicationUser.Id`; **omit → provider default** (blank, no dedup) — so we always set it |

All other `CreateCustomer` optionals **omitted → provider default** (address/tax/locale/etc. not in scope; setting them would override provider defaults).

### `CreateSubscription` optional fields carried (each with purpose)

| field (wire) | type | purpose |
| --- | --- | --- |
| `ProductHandle` (product_handle) | string? | selects the plan by stable handle (source: "we recommend using the API Handle instead" of product_id) |
| `CustomerId` (customer_id) | int? | attaches to the already-ensured Maxio customer (avoids re-sending customer_attributes) |
| `Reference` (reference) | string? | app-side subscription idempotency key; enables `FindSubscription` reconciliation. **omit → provider default** (null, no dedup) — so we set it |
| `PaymentCollectionMethod` (payment_collection_method) | `CollectionMethod`? | **verified at runtime (see §6):** omitting it defaults to `automatic`, which attempts to charge the plan price at signup and returns **422 "No payment method was on file"** for the card-less plans. Set to `remittance` (invoice-based, Relationship Invoicing) so a no-card plan enrolls to an **active, invoiced** subscription. Configurable via optional `Maxio:PaymentCollectionMethod`; default `remittance`. |

Deliberately **omitted** (each omit → provider default; setting would override plan/site config or require unavailable data): `ProductPricePointHandle`/`ProductPricePointId` (use the plan's default price point), all payment-profile/credit-card/bank fields (plans require **no payment method** — sending card data is out of scope and PCI-relevant), `CouponCodes`, `NextBillingAt`/`InitialBillingAt`/`DeferSignup` (activate immediately — no future billing), `Components`, `Currency`, `Metafields`, `CustomerReference` (using `CustomerId` instead). `import_mrr`/`previous_billing_at` = **import/migration only**, omitted.

### Enum tables needed

`SubscriptionState` (`Models/Enums/SubscriptionState.cs`, `StringEnum<SubscriptionState>`, read `.Value` for wire string) — live states used to decide "already subscribed": `Active` (`active`), `Trialing` (`trialing`), `AwaitingSignup` (`awaiting_signup`), `Assessing` (`assessing`), `Pending` (`pending`), `Paused` (`paused`). End-of-life/problem states (`canceled`,`expired`,`failed_to_create`,`trial_ended`,`on_hold`,`suspended`,`past_due`,`soft_failure`,`unpaid`) are **not** treated as an active subscription (a re-subscribe is allowed).

`IntervalUnit` (`Models/Enums/IntervalUnit.cs`) — read `.Value` for display ("month"/"day") alongside `Product.Interval`.

### Client construction / auth / server node (source: sdk-map.md *Getting a client* + *Servers & auth*; `MaxioAdvancedBillingClientOptions.cs`; `Servers/ProductionOptions.cs`; `Core/Authentication/Basic/BasicAuthCredentials.cs`)

- Register via `services.AddMaxioAdvancedBillingClient(options => { … })` (`ServiceCollectionExtensions.cs`) — builds options **once at registration**, captures in a **singleton**, uses `IHttpClientFactory`.
- `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (source: *Servers & auth* — "username is a Maxio Chargify API key and the password is `x`"; Basic works with US/EU).
- `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (deployment default; `MAXIO_ENVIRONMENT=US`).
- Base URL: if `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` **verbatim** (used as-is; `{site}` substitution is a no-op when the URL has no placeholder). Else → `options.Server.Production.Us.Site = <Maxio:Subdomain>` (fills `https://{site}.chargify.com`). (source: `Servers/ProductionOptions.cs` — `UsOptions{ BaseUrl, Site }`, `Resolve` substitutes `site`).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| the `planHandle` a caller subscribes to must be one of the plans returned for the configured family | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` | implementation — the subscribe service validates the requested handle against the live plan list for the configured family (unknown/foreign/archived handle → 400 to caller) before any write |
| the customer the subscription attaches to must be the one owned by this eShop user | `Subscriptions.CreateSubscription`/`Customers.ListCustomerSubscriptions` ← `Customers.ReadCustomerByReference`/`CreateCustomer` | implementation — `customer_id`/lookup always derived from `reference = ApplicationUser.Id`, never caller input |

---

## 3. Trap notes (hazard + consequence + skill; not resolved here)

- **Client/HttpClient lifetime**: the SDK client + its HttpClient pipeline must be long-lived/reused, not rebuilt per request; getting this wrong exhausts sockets. `MUST load dotnet-client-initialization`.
- **Credentials placement**: where/when Basic creds are set on options vs. constructing the client, and loading from config not literals. `MUST load dotnet-authentication`.
- **List/search calls with named args**: many optional params have no C# default and mis-bind positionally; and whether any write takes a real idempotency key vs. the injected `Idempotency-Key` header (which is `Guid.NewGuid()` per call and dedupes nothing). `MUST load dotnet-calling-endpoints`.
- **Models**: `StringEnum<T>` is not a C# enum (build with static members / `FromValue`, read `.Value`); response models keep unknown fields in `AdditionalProperties`; required members must be set in initializer. `MUST load dotnet-models`.
- **Error boundary**: Case A vs Case B per operation; `TryGet…` accessors; `TryGetRawError` is the fallback, not a catch-all on typed errors; no no-throw variants exist. `MUST load dotnet-error-handling`.
- **Config/resilience**: `Timeout` is per-attempt not total; `HttpMethodsToRetry` excludes POST/PATCH/DELETE (so `CreateCustomer`/`CreateSubscription` are never auto-resent) but includes PUT; `LogRequestBody` logs JSON bodies unredacted and the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var can arm logging unless `LoggerFactory` is set explicitly. `MUST load dotnet-configuration-resilience`.
- **Testing**: the `HttpClient` constructor arg is the fake seam; match the repo's xUnit/Moq style. `MUST load dotnet-testing`.

---

## 4. REQUIRED READING (load all before implementation starts; this sheet does not carry their contents)

| skill | governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | building options + DI-registering the client (Infrastructure) |
| `maxio-platforms-team:dotnet-authentication` | setting Basic credentials from config |
| `maxio-platforms-team:dotnet-calling-endpoints` | every `client.*` call, named args, idempotency-key reality |
| `maxio-platforms-team:dotnet-models` | building request bodies, `StringEnum` reads, `AdditionalProperties` |
| `maxio-platforms-team:dotnet-error-handling` | the try/catch boundary in the service |
| `maxio-platforms-team:dotnet-configuration-resilience` | retry/timeout/logging tuning + total-timeout budget |
| `maxio-platforms-team:dotnet-testing` | integration-layer tests faking the HttpClient seam |

**Two mandatory `System.Text.Json.JsonException` hazards (both bypass an SDK-exception-only catch ladder):**
1. A drifted/malformed **2xx** body (a missing `required` member — e.g. `CustomerResponse.Customer`, `ErrorListResponse1.Errors`) surfaces as a `JsonException` from **deserialization**, **not** an `SdkException` — so the catch ladder must also catch `JsonException` (or a broader boundary).
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is constructed**, **replacing** the `SdkException` and destroying the HTTP status — the boundary must treat a bare `JsonException` as an upstream failure (502-class), not a success.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AddMaxioBilling` binds `Maxio:` and **throws at startup** if `ApiKey` or (`Subdomain` **and** `BaseUrl`) or `ProductFamilyHandle` is missing/blank. Each part checked separately (blank ≠ missing). Host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Secrets live in **.NET user-secrets** (dev) / env-provided config (prod), bound into `MaxioSettings`. `AddMaxioAdvancedBillingClient` captures options in the **singleton at registration**, so a rotated `ApiKey` takes effect only on **process restart** — accepted (documented); no hot-rotation requirement for this demo. |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt**. The service passes a `CancellationToken` from the request with a **whole-call deadline (30s)** so a hung retried call is bounded end-to-end, not `Timeout × (MaxRetries+1)`. Enforced in the service via a linked `CancellationTokenSource`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes `CreateCustomer`/`CreateSubscription` are **POST** → **never auto-resent** by the SDK. Reads (`ReadCustomerByReference`, `FindSubscription`, list ops) are GET → safely retried. No PUT writes in scope. |
| 5 | Idempotency & ambiguous writes | **CreateCustomer**: real key = customer `reference` (= userId), **site-unique** (source: Api/Customers.cs remarks). Pre-check `ReadCustomerByReference`; on create-422 re-read. **CreateSubscription**: no server-confirmed idempotency key (see §6 UNVERIFIED); mitigations = deterministic subscription `reference`, pre-check `FindSubscription`, and re-read on 422. The injected `Idempotency-Key` header is **not** used as a key. |
| 6 | Observability | Structured logs via `IAppLogger<T>` (repo convention): info on ensure-customer/subscribe decisions (customer id, plan handle, subscription id, state — **no secrets**); warning on 422 reconcile paths; error on provider/transport failure incl. the provider error messages read from `ErrorListResponse1.Errors`. SDK `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Request models in scope (`CreateCustomer`, `CreateSubscription`) carry **no card/bank data** (we send none — plans need no payment method). Still, `LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is **set explicitly** (to the app `ILoggerFactory`) so the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot arm body logging from outside code. |
| 8 | Environment selection | Groups: `Production` (used), `Ebb`/`Oauth` (not used). Deployment sets `Environment = Us`. Test traffic is kept off live systems by targeting the **sandbox site** via `Maxio:Subdomain`/`Maxio:BaseUrl` config (no separate SDK "sandbox" env exists; the sandbox *is* a distinct Chargify site subdomain). |
| 9 | Duplicate prevention under concurrency | See `DUPLICATE CLAIMS`. Store = **Maxio** (the system of record), not the in-memory DB. |
| 10 | Partial results | See `PAGED READS`. |
| 11 | Startup validation vs test host | `PublicApi` boots in `tests/PublicApiIntegrationTests` (WebApplicationFactory). The added startup credential check must not break it: the factory supplies **placeholder `Maxio:` config** (non-blank dummy values) so the host starts; asserted by running that project green. If that project is absent/incompatible, the check is written to only trip on **missing** keys and the test config provides them. |
| 12 | Ordering & no-op side effects | No local record exists (Maxio is the record), so "local write before provider call" is **N/A** — there is nothing local to order. Idempotent transitions are gated: subscribe **returns the existing** live subscription without a second create; no unconditional post-transition side effects (no emails/webhooks emitted by us). |
| 13 | Unknown outcomes | If `CreateCustomer` transport fails after possible receipt → re-read `Customers.ReadCustomerByReference(reference)`. If `CreateSubscription` transport fails after possible receipt → re-read `Subscriptions.FindSubscription(reference)`. Reference searched by = the deterministic `reference` (userId / subscription key). Never report a flat failure without re-reading. |
| 14 | Provider status & reconciliation clocks | `CreateSubscription`/`FindSubscription`/`ListCustomerSubscriptions` return `Subscription.State`. The service branches on it: live states (§2) → report as active/subscribed; end-of-life/problem states → reported verbatim to the caller (never coerced to a success default). No two-source time reconciliation in scope → single clock N/A. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught |
| --- | --- | --- | --- |
| CreateCustomer | Maxio customer `reference` (site-unique per Api/Customers.cs remarks) | Maxio's reference-uniqueness constraint → **422** `CustomerErrorResponse1` | `catch (SdkException<CreateCustomerError>)` → `TryGetCustomerErrorResponse1` → `ReadCustomerByReference` returns the winning row |
| CreateSubscription | Maxio subscription `reference` | **UNVERIFIED** whether Maxio rejects a duplicate subscription `reference` on create (see §6). Mitigation: 422 catch + `FindSubscription` reconcile; documented limitation for true simultaneity | `catch (SdkException<CreateSubscriptionError>)` → `FindSubscription(reference)` |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short |
| --- | --- | --- |
| ListProductsForProductFamily | `per_page` (page-based; loop until a short page) | fully paginated in the service (loop to exhaustion), so `subscription-plans` is never truncated in normal catalogs; if the abnormal 20-page safety cap is ever hit, the service **throws** (502) rather than returning a silent partial |
| ListCustomerSubscriptions | single call (map: no pagination bullet) | returns the full set the SDK returns; no client-side cap applied |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that |
| --- | --- | --- |
| POST /api/subscriptions (subscribe) | a live subscription for the plan **did not already exist** (pre-check `FindSubscription` / live-state check) | the `CreateSubscription` call itself — skipped when a live subscription already exists (returns the existing one); response marks `alreadySubscribed: true` vs a fresh create |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by |
| --- | --- | --- |
| CreateCustomer | `Customers.ReadCustomerByReference` | customer `reference` = `ApplicationUser.Id` |
| CreateSubscription | `Subscriptions.FindSubscription` | subscription `reference` (deterministic per user/plan) |

---

## 6. Assumptions & Blockers

- **Assumption (minor):** `ApplicationUser` (bare `IdentityUser`) has no first/last name; Maxio `CreateCustomer`
  requires both. Derive `FirstName` from the email local-part and `LastName` = `"eShopOnWeb"`; `Email` = the
  user's email; `Reference` = `ApplicationUser.Id`. Non-blocking.
- **Assumption (minor):** subscription `reference` is deterministic per eShop user (one active plan per user in
  the hero flow): `eshopweb-{userId}`. A user changing plans reuses the reference (re-subscribe replaces).
- **Assumption (minor):** `POST /api/subscriptions` body carries an optional `planHandle`; when omitted it
  defaults to the configured Pro plan handle (`Maxio:ProductFamilyHandle` names the family; the default plan is
  the family's `eshop-pro` handle if present, else the first live plan). Validated against the live plan list.
- **UNVERIFIED:** Whether Maxio enforces **subscription** `reference` uniqueness on create is not stated in the
  SDK source (only `FindSubscription` returns a single result by reference / 404). Consequence recorded as a
  defensive directive: treat `CreateSubscription` as **not** guaranteed-idempotent under true simultaneity;
  always pre-check `FindSubscription` and, on 422, reconcile via `FindSubscription` rather than assuming failure.
  Customer-level dedup (the more important claim, since a customer is created first) **is** source-confirmed.
- **Runtime finding (resolved):** Although the plans require no payment method, Maxio's default `automatic`
  collection still tries to charge the plan price at signup and returns **422 "No payment method was on file"**.
  Verified live against the configured sandbox site. Resolved by creating subscriptions with
  `payment_collection_method = remittance` (invoice-based) — the card-less plans then enroll to an **active**
  subscription with a next-billing date. Made configurable via optional `Maxio:PaymentCollectionMethod`
  (default `remittance`; use `invoice` on legacy Statements sites).
- **No Blockers.** Every capability the hero flow needs exists in the map.
