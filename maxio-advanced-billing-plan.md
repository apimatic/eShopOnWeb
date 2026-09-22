# Maxio Advanced Billing — integration plan (eShopOnWeb subscriptions)

Additive, parallel recurring-subscription capability on **`src/PublicApi`** (JWT). Maxio Advanced
Billing is the system of record. Three endpoints:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.

The SDK is APIMatic-generated, root namespace `MaxioAdvancedBilling`, not published to NuGet —
**vendored into the repo** as `src/MaxioAdvancedBilling/` (source build) and referenced from
`src/Infrastructure`.

---

## 1. Scope & sequence

1. **Build/env** — vendor SDK project; `global.json` rollForward → `latestMajor`; baseline build.
2. **Domain** (`ApplicationCore`) — `BuyerSubscription` entity (local duplicate-claim/tracking row),
   `ISubscriptionBillingService` + result records (`SubscriptionPlanInfo`, `SubscriptionInfo`,
   `SubscribeResult`).
3. **Infrastructure** — `MaxioSettings` (bound + fail-fast), Maxio client DI, EF config with unique
   index, `MaxioSubscriptionBillingService` implementing the flows below.
4. **PublicApi** — 3 endpoints (Ardalis `EndpointBaseAsync`, `[Authorize]`), DTOs, DI wiring.
5. **Secrets/config** — env vars → user-secrets; `Maxio:` section keys; test-host placeholders.
6. **Verify** — build, test (host-booting project green), live sandbox run, fill §5/§ table `where`.

**Flows (operations used):**
- `subscription-plans`: `ProductFamilies.ListProductFamilies` (resolve configured handle → family id) →
  `ProductFamilies.ListProductsForProductFamily` (paged, family-scoped) → map non-archived products.
- `subscriptions` (Subscribe hero): resolve caller (token → `UserManager` → userId/email) →
  validate `planHandle` ∈ family plans (cross-op invariant) → **ensure customer** idempotently
  (`Customers.ReadCustomerByReference`; on 404 `Customers.CreateCustomer`; on 422-race re-read) →
  insert local `BuyerSubscription(Pending)` (unique-index claim) → `Subscriptions.CreateSubscription`
  (`product_handle` + `customer_reference` + deterministic `reference`) → update local row → confirm.
- `my-subscriptions`: `Customers.ReadCustomerByReference` (404 → empty) →
  `Customers.ListCustomerSubscriptions(customerId)` → map.

A capability the map lacks is a Blocker (§6), never invented.

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C# identifier;
> the cancellation-token parameter is named `ct`, so named args write `ct:`. Optional list params
> have no C# default and **must be passed explicitly** (`null` to skip) — call with named args.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`; `Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`;
> root/servers/options per their files), taken from the path the map gives for THAT type.

| # | Operation (controller.method) | Signature (verbatim) | Request model → fields used | Response envelope → inner fields read | Error case + accessors | Pagination | source |
|---|---|---|---|---|---|---|---|
| A | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (`reference` query = userId) | `CustomerResponse.Customer` (`Customer`): `Id`, `Reference`, `Email` | **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString`); **404 = not found** | none | map/operations/Customers.md |
| B | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest.Customer` (`CreateCustomer`, required) → `FirstName`*, `LastName`*, `Email`*, `Reference` | `CustomerResponse.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | map/operations/Customers.md; Models/CreateCustomer.cs |
| C | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` (`Subscription`) | **Case B** `SdkException<RawError>` | **none** (not paged — no page/perPage in sig) | map/operations/Customers.md |
| D | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription` (`CreateSubscription`, required) → `ProductHandle`, `CustomerReference`, `Reference` | `SubscriptionResponse.Subscription` (`Subscription`): `Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Product`, `Reference` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | map/operations/Subscriptions.md; Models/CreateSubscription.cs |
| E | `Subscriptions.FindSubscription` | `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (`reference` query = subKey) | `SubscriptionResponse.Subscription` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | map/operations/Subscriptions.md |
| F | `ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (all `null`) | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily`): `Id`, `Handle` | **Case B** `SdkException<RawError>` | none | map/operations/ProductFamilies.md |
| G | `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (`productFamilyId` = family id string; `includeArchived: false`; rest `null`) | `IReadOnlyList<ProductResponse>` → `.Product` (`Product`): `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | **page-based**: `page` (1), `perPage` (20). Loop pages. | map/operations/ProductFamilies.md; Models/Product.cs |

**Create body optional fields carried (with purpose):**
- `CreateCustomer.Reference` — our claim key = eShop userId (GUID). **Unique per site (provider-enforced;**
  see Api/Customers.cs remarks: *"you may only create one customer for a given reference value… must be unique"*).
  Purpose: idempotent customer + duplicate claim.
- `CreateSubscription.ProductHandle` — the plan's API handle (from request `planHandle`). Purpose: names the plan.
- `CreateSubscription.CustomerReference` — links to existing customer by our reference (avoids passing numeric id).
- `CreateSubscription.Reference` — deterministic `eshop:{userId}:{planHandle}`. Purpose: reconciliation key for
  `FindSubscription` on unknown outcome / race. **Note: subscription-reference uniqueness is `UNVERIFIED`** (not in
  remarks) — so it is NOT relied on as the duplicate claim; the local unique index is (§ DUPLICATE CLAIMS).
- `CreateSubscription.PaymentCollectionMethod` = `CollectionMethod.Remittance`. **YOUR CALL — not in the map.**
  Verified against the sandbox: the `automatic` provider default attempts an immediate charge for the plan's
  balance and 422s with *"No payment method was on file for the $299.00 balance"* even though the product's
  `require_credit_card` is false. Remittance (invoice) collection creates the subscription **active** with an
  invoice issued instead — honouring "subscribe works without card capture." (Legacy Statements-Architecture
  sites would need `invoice`; `cp-exp-1` is Relationship Invoicing, so `remittance`.) Source enum:
  `Models/Enums/CollectionMethod.cs`.
- **Deliberately omitted** (omit → provider default): `ProductPricePointHandle`/`ProductPricePointId` (use product
  default price point), `CustomerAttributes`/`*_attributes` card fields (payment method not required — never send
  card data), `NextBillingAt`/`InitialBillingAt`/`DeferSignup` (immediate activation), all coupon/offer/prepaid
  fields. `DeferSignup`/`DunningCommunicationDelayEnabled` default `false` in the model.

**Enum value tables needed** (`Models/Enums/`; all `StringEnum<T>` — read wire value via `.Value`, PascalCase static members):
- `SubscriptionState` (`Subscription.State`): `active`, `trialing`, `pending`, `awaiting_signup`, `assessing`,
  `paused`, `past_due`, `soft_failure`, `unpaid`, `canceled`, `expired`, `failed_to_create`, `on_hold`,
  `suspended`, `trial_ended`. **Source: Models/Enums/SubscriptionState.cs.**
- `IntervalUnit` (`Product.IntervalUnit`): `day`, `month`. **Source: Models/Enums/IntervalUnit.cs.**

**Client construction / auth / server (sources: MaxioAdvancedBillingClientOptions.cs, ServerOptions.cs,
Servers/ProductionOptions.cs, Servers/ServerEnvironment.cs, ServiceCollectionExtensions.cs, sdk-map.md §Servers):**
- DI: `services.AddMaxioAdvancedBillingClient(options => { … })` → registers a **singleton** client over
  `IHttpClientFactory` (`AddHttpClient()`); LoggerFactory defaulted from `sp.GetService<ILoggerFactory>()` if unset.
- Auth: `options.BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }` (Basic; US/EU only).
- Env: `options.Environment = ServerEnvironment.Us` (`MaxioAdvancedBilling.Servers`).
- Base URL: default template `https://{site}.chargify.com`. If `Maxio:BaseUrl` blank →
  `options.Server.Production.Us.Site = <Subdomain>`; if set → `options.Server.Production.Us.BaseUrl = <BaseUrl>`
  verbatim (no `{site}` token → used as-is).
- `RequestOptions`/`CancellationToken` carry per-call timeout budget (§ PRODUCTION READINESS #3).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
|---|---|---|
| `planHandle` accepted by Subscribe must be a product **handle returned by the configured family's plan list** (else 404/422 from Maxio and a bad customer state) | `Subscriptions.CreateSubscription` (D) ← `ProductFamilies.ListProductsForProductFamily` (G) | implementation — `MaxioSubscriptionBillingService.SubscribeAsync` validates `planHandle` against the family's product handles before any write; unknown handle → `PlanNotFound` (400) |
| `customer_reference` on CreateSubscription must be a reference **that ReadCustomerByReference/CreateCustomer produced** | `Subscriptions.CreateSubscription` (D) ← `Customers.ReadCustomerByReference`/`CreateCustomer` (A/B) | implementation — customer is ensured first; its reference is reused |
| family id passed to G must be one **returned by ListProductFamilies** for the configured handle | `ListProductsForProductFamily` (G) ← `ListProductFamilies` (F) | implementation — resolve handle→id; unknown handle → configuration Blocker surfaced as 500 |

---

## 3. Trap notes (hazard + skill pointer; not resolved here)

- **Client/HttpClient lifetime & DI singleton capture.** How the SDK client and its HttpClient/handler must
  be owned (not rebuilt per request) is not visible in `new MaxioAdvancedBillingClient(...)`. **MUST load
  dotnet-client-initialization.**
- **Credential property + when to set it.** Which options property carries Basic creds and the set-before-construct
  ordering are not obvious from the signature. **MUST load dotnet-authentication.**
- **Named-argument binding on list ops.** `ListProductsForProductFamily`/`ListProductFamilies` have many
  no-default nullable params that mis-bind positionally. **MUST load dotnet-calling-endpoints.**
- **`StringEnum<T>` is not a C# enum; response envelopes wrap one level; unknown-field bag.** Reading `State`/
  `IntervalUnit`, and `.Subscription`/`.Customer`/`.Product` unwrapping. **MUST load dotnet-models.**
- **Two exception directions at the boundary.** `SdkException<TError>` Case A vs B accessors, AND
  `System.Text.Json.JsonException` from a drifted 2xx or an error body that doesn't match its generated shape.
  **MUST load dotnet-error-handling.**
- **Per-attempt timeout, retry method allow-list, JSON body logged unredacted.** `Timeout` is per attempt not
  total; POST is never resent, PUT/GET are; `LogRequestBody` prints PII. **MUST load dotnet-configuration-resilience.**
- **Faking the seam / matching MSTest style.** The `HttpClient` ctor arg is the test seam. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load all before implementation; contents deliberately not copied here)

| skill (plugin: maxio-platforms-team) | step it governs |
|---|---|
| `dotnet-client-initialization` | Maxio client construction + DI singleton over IHttpClientFactory |
| `dotnet-authentication` | Basic-auth credential wiring (`BasicAuth`) |
| `dotnet-calling-endpoints` | list/read/create calls, named args, idempotency reality |
| `dotnet-models` | building request bodies; reading enums, envelopes, nullable fields |
| `dotnet-error-handling` | try/catch boundary — **always required** |
| `dotnet-configuration-resilience` | retries, per-attempt timeout, base-URL, pagination, logging/PII |
| `dotnet-testing` | integration-layer tests |

**Two mandatory `JsonException` hazard rows** (`System.Text.Json.JsonException` reaches the boundary from two
directions, needing opposite handling):
1. A drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` **from
   deserialization — not** an `SdkException`; an SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that doesn't match its operation's generated `{Operation}Error` shape throws
   `JsonException` **while the error object is being constructed**, replacing the `SdkException` and destroying
   the HTTP status. → the boundary catches `JsonException` explicitly and maps to 502.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | **Credential fail-fast** | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` each validated non-null/non-whitespace at DI registration (`AddMaxioBilling`); any blank → `InvalidOperationException` before the host starts. `Maxio:BaseUrl` optional. Each part checked separately (blank ≠ missing). |
| 2 | **Secret sourcing & rotation** | `Maxio:ApiKey` from **.NET user-secrets** (loaded from env `MAXIO_API_KEY`; never in repo). Options object built **once at registration** and captured in the singleton client → rotation needs a process restart (documented; acceptable for this app). |
| 3 | **Total timeout budget** | SDK `Timeout` is **per attempt**. Caller budget bounded by a **linked `CancellationTokenSource`** (request-abort + 30 s cap) created per service call and passed as `ct:` to every SDK op — the only thing bounding a whole retried call. |
| 4 | **Write-retry ownership** | Keep default `HttpMethodsToRetry` (`GET,HEAD,PUT,OPTIONS`). Both writes (`CreateCustomer`, `CreateSubscription`) are **POST → never resent** by the SDK. GET reads (A/C/F/G/E) may retry safely. POST is deliberately **not** added to the retry list. |
| 5 | **Idempotency & ambiguous writes** | `CreateCustomer`: real key = `customers.reference` (= userId), provider-unique. `CreateSubscription`: **no real caller-supplied idempotency key param exists** (generator-injected `Idempotency-Key: Guid.NewGuid()` is NOT one). Reconciliation instead: local unique index (§ DUPLICATE CLAIMS) + `FindSubscription(eshop:{userId}:{planHandle})`. |
| 6 | **Observability** | `Info` on ensure-customer/subscribe start+success (userId, planHandle, subscriptionId — no PII bodies); `Warning` on 422 validation (Maxio error messages from `ErrorListResponse1.Errors`/`CustomerErrorResponse1` surfaced to logs); `Error` on unexpected/`JsonException`. `LogRequestBody` **off**. No provider request-id documented in these error bodies → none propagated. |
| 7 | **Sensitive data** | `CreateCustomer` carries **email + name (PII)**; no card data is ever sent (payment not required). Therefore `LogRequestBody` stays **off**; `LoggingOptions.LoggerFactory` is bound to the app `ILoggerFactory` (via the SDK DI extension) so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot force body logging from outside code; app logs never echo request bodies. |
| 8 | **Environment selection** | One environment: `ServerEnvironment.Us` → Production group `https://{site}.chargify.com`, `{site}` = `Maxio:Subdomain`. `Ebb`/`Oauth` groups unused. No separate "sandbox" enum exists — **test isolation is by site**: the configured subdomain (`cp-exp-1`) IS the sandbox site; production sets a different subdomain (or `Maxio:BaseUrl`). Documented so live traffic can't hit test. |
| 9 | **Duplicate prevention under concurrency** | Store **`BuyerSubscriptions` table**, column pair **(`BuyerId`,`PlanHandle`)** with a **unique index** `IX_BuyerSubscriptions_BuyerId_PlanHandle`; the 2nd concurrent insert throws `DbUpdateException`, caught → reconcile via `FindSubscription`. Customer duplicates are additionally rejected by Maxio's `customers.reference` unique constraint (caught 422). ⚠ Dev-provider caveat (§6): the mandated EF **InMemory** provider does not enforce unique indexes, so on this machine the local claim is inert; the design targets the SQL Server provider where it is enforced. |
| 10 | **Partial results** | Only paged read is `ListProductsForProductFamily` (G). The service loops all pages until a short page; a safety cap (`MaxPlanPages`) bounds it, and the plans response DTO carries a **`Truncated` bool** set true iff the cap is hit — the caller learns via that field (not a log). |
| 11 | **Startup validation vs test host** | Host-booting project = **`tests/PublicApiIntegrationTests`** (`WebApplicationFactory<Program>`). It is given **placeholder** `Maxio:` config in its `appsettings.test.json` (`ApiKey=test-*`, `Subdomain=test`, `ProductFamilyHandle=eshop-subscribe` — not secrets) so the fail-fast passes and the host boots; no Maxio network call happens at startup. Must be **run green** (existing catalog/auth tests unaffected). |
| 12 | **Ordering & no-op side effects** | Local `BuyerSubscription(Pending)` row is written **before** `CreateSubscription` and updated (`Active`/state + ids) **after** it returns. On repeat (unique-index hit / already-active) the outbound `CreateSubscription` is **gated** — skipped, existing subscription returned; no duplicate provider write, no duplicate log. |
| 13 | **Unknown outcomes** | If `CreateSubscription` transport fails (timeout/`HttpRequestException`/`JsonException`) after the request may have been received, the catch **re-reads `Subscriptions.FindSubscription`** by `reference = eshop:{userId}:{planHandle}`; found → complete the row + return success; not found → mark row `Unknown` (a state distinct from `Failed` — the write may have landed), surface 502. No definite failure is returned without re-reading. |
| 14 | **Provider status & reconciliation** | `CreateSubscription`/`ListCustomerSubscriptions` return `subscription.state`. Branching: `failed_to_create` → treat as failure (row `Failed`, 502); `active`/`trialing`/`pending`/`awaiting_signup` → success, state surfaced verbatim to caller; **no `?? "active"` defaulting** — a null/unreadable state is surfaced as `unknown`, never as success. Reconciliation between local rows and Maxio filters on the **Maxio subscription `reference`**, not a local timestamp. |

### DUPLICATE CLAIMS
| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
|---|---|---|---|---|
| CreateCustomer | Maxio `customers.reference` | provider unique constraint on `reference` → 422 | `catch SdkException<CreateCustomerError>` → re-read `TryReadCustomerAsync` returns the existing customer | `MaxioSubscriptionBillingService.EnsureCustomerAsync` (catch `SdkException<CreateCustomerError>`) |
| CreateSubscription | `BuyerSubscriptions` (`BuyerId`,`PlanHandle`) | unique index `IX_BuyerSubscriptions_BuyerId_PlanHandle` → `DbUpdateException` | `catch (DbUpdateException)` → `ReadExistingSubscriptionAsync` (also a pre-write Provisioned fast-path) | `MaxioSubscriptionBillingService.SubscribeAsync` (the `AddAsync` try/catch) |

### PAGED READS
| read | what caps it | how the caller learns it was cut short | where in the code |
|---|---|---|---|
| ListProductsForProductFamily (plans) | `MaxPlanPages` safety cap (20 pages, perPage 100) | `SubscriptionPlanList.Truncated` → `SubscriptionPlansResponse.Truncated` bool = true | `MaxioSubscriptionBillingService.ListPlansForFamilyAsync` (page loop) |

### REPEATED OPERATIONS
| operation | what tells you the state actually changed | the effects gated on that | where in the code |
|---|---|---|---|
| Subscribe (same user+plan) | Provisioned `BuyerSubscription` found (fast-path) or `AddAsync` raises `DbUpdateException` | the `CreateSubscriptionAsync` call + "subscribed" Info log (skipped; `AlreadySubscribed=true` returned) | `MaxioSubscriptionBillingService.SubscribeAsync` |
| EnsureCustomer | `TryReadCustomerAsync` returns non-null (found) vs null (404) | the `CreateCustomer` call | `MaxioSubscriptionBillingService.EnsureCustomerAsync` |

### UNKNOWN OUTCOMES
| write | the operation you re-read with | the reference you search by | where in the code |
|---|---|---|---|
| CreateSubscription | `Subscriptions.FindSubscription` | `eshop:{userId}:{planHandle}` (subscription `reference`) | `CreateSubscriptionAsync` catch (`JsonException` / transport) → `ReconcileAfterUnknownOutcomeAsync` → `FindSubscriptionByReferenceAsync` |
| CreateCustomer | `Customers.ReadCustomerByReference` | userId (`reference`) | `EnsureCustomerAsync` catch (`JsonException` / transport) → `TryReadCustomerAsync` |

### OPERATION OUTCOMES
| write | the status field | every value it can hold, and what the app does with each | where in the code |
|---|---|---|---|
| CreateSubscription | `subscription.state` (`SubscriptionState`) via `.State?.Value` | `active`/`trialing`/`pending`/`awaiting_signup`/`assessing`/`paused` → success, surfaced; `failed_to_create` → failure (row `Failed`, 502); `past_due`/`soft_failure`/`unpaid`/`canceled`/`expired`/`on_hold`/`suspended`/`trial_ended` → surfaced as-is (caller sees non-active state, row stores it in `ProviderState`); **null → surfaced as-is (no defaulting to success)** | `MaxioSubscriptionBillingService.CreateSubscriptionAsync` (the `failed_to_create` branch, then `MarkProvisioned`) |

### WRITE ORDER
| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
|---|---|---|---|
| CreateSubscription | `BuyerSubscription{ BuyerId, PlanHandle, SubscriptionReference, Status=Pending }` (via `AddAsync`, with `MaxioCustomerId` set) | `MarkProvisioned(subscriptionId, providerState)` → `Status=Provisioned` (via `UpdateAsync`) | before: `SubscribeAsync` (`AddAsync`); after: `CreateSubscriptionAsync` (`MarkProvisioned`) |
| CreateCustomer | (no local row — customer is a Maxio-side ensure keyed by `reference`; the `BuyerSubscription` claim is written before CreateSubscription) | Maxio customer id captured onto the claim via `SetCustomer` | `SubscribeAsync` calls `EnsureCustomerAsync` then `claim.SetCustomer(customerId)` |

---

## 6. Assumptions & Blockers

**Assumptions (minor — proceeding):**
- Caller identity: JWT carries `ClaimTypes.Name` = username/email (per `AuthenticateEndpoint`/`ApiTokenHelper`).
  The stable Maxio customer `reference` = the ASP.NET Identity `ApplicationUser.Id` (GUID) resolved via
  `UserManager.FindByNameAsync(username)`; if the user record is absent, fall back to the username itself.
- `ApplicationUser` (plain `IdentityUser`) has no first/last name → `CreateCustomer.FirstName`/`LastName` derived
  from username (local-part + `"eShopOnWeb"`), `Email` from the user record (or username if it's an email).
- All three endpoints require `[Authorize]` (JWT). Browsing plans while authenticated matches the hero flow.
- `planHandle` is **required** in the POST body (shopper picks a plan); validated against the family's plans.
- Environment fixed to `ServerEnvironment.Us`; any non-US host is reached via the `Maxio:BaseUrl` verbatim override
  (env is not among the four mandated `Maxio:` keys).

**Blockers:** none. (The InMemory-provider unique-index caveat in §9/#11 is an environment limitation of this dev
machine, disclosed — not a planning blocker; the SQL Server provider enforces the claim.)

**UNVERIFIED:** subscription-`reference` uniqueness (not documented) — not relied upon; local unique index is the claim.

---

## 7. Source references
- Operations: `map/operations/{Customers,Subscriptions,Products,ProductFamilies}.md`
- Models: `Models/{CreateCustomer,CreateSubscription,Customer,Subscription,Product,ProductFamily,
  CustomerErrorResponse1,ErrorListResponse1}.cs`; enums `Models/Enums/{SubscriptionState,IntervalUnit}.cs`
- Client/servers/auth: `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`,
  `Servers/ProductionOptions.cs`, `Servers/ServerEnvironment.cs`, `ServiceCollectionExtensions.cs`,
  `Core/Configuration/LoggingOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`,
  `Core/Enum/{StringEnum,TypedEnum}.cs`
- Provider semantics: `Api/Customers.cs` remarks (reference uniqueness), `Api/Subscriptions.cs` remarks.
