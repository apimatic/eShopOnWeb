# Maxio Advanced Billing integration plan — eShopOnWeb "Subscribe" capability

Additive, parallel subscription-billing capability on `src/PublicApi` (JWT auth). Maxio Advanced
Billing is the billing system of record. Three endpoints:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.

The Maxio **.NET SDK is not published to NuGet**, so it is vendored as source under
`src/Maxio.AdvancedBilling.Sdk/` (built from the pinned `main` source) and referenced by
`ProjectReference`. The vendored project opts out of central package management.

---

## 1. Scope & sequence

| # | Step | Operations used |
| --- | --- | --- |
| 1 | Vendor SDK source project; reference from `Infrastructure` | — |
| 2 | Bind `Maxio:*` settings (fail-fast), register `MaxioAdvancedBillingClient` (Basic auth, US env, site/base-url override) via `IHttpClientFactory` | client construction |
| 3 | Local billing store `MaxioBillingContext` (customer link + subscription enrollment, unique indexes) | — |
| 4 | `GET /api/subscription-plans` — resolve family handle→id, list plans | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` — ensure customer (idempotent), enroll (idempotent), confirm | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — resolve customer, list its subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |

No capability required here is missing from the map. No invented data paths.

---

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal C#
> identifier; the cancellation-token parameter is named `ct`, so named arguments write `ct:`.
> ⚠ Every SDK type is written **fully-qualified with the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `...Models.Enums`,
> `Errors/` → `...Errors`, client/options root → `MaxioAdvancedBilling`,
> `Servers/` → `MaxioAdvancedBilling.Servers`), taken from the path the map gives for THAT type.

| Op | Controller · Signature | Request model + fields set | Response envelope → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| List families | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions=null, CancellationToken ct=default)` — pass `null` for the 5 filter params | none | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily.Id (id): int?`, `.ProductFamily.Handle (handle): string?` | **Case B** `SdkException<RawError>` — `StatusCode`/`ReadAsString()` | none | map `ProductFamilies.md`; `Models/ProductFamilyResponse.cs`, `Models/ProductFamily.cs` |
| List plans | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page=1, int? perPage=20, RequestOptions? requestOptions=null, CancellationToken ct=default)` — `productFamilyId` = resolved numeric id as string; pass `null` for filters, `includeArchived: false` | none | `IReadOnlyList<ProductResponse>` → `.Product.{Handle(handle):string?, Name(name):string?, Description(description):string?, PriceInCents(price_in_cents):long?, Interval(interval):int?, IntervalUnit(interval_unit):IntervalUnit?, Id(id):int?, ArchivedAt(archived_at):DateTimeOffset?}` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none | map `ProductFamilies.md`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| Read customer by ref | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none (query `reference`) | `CustomerResponse` → `.Customer.Id (id): int?` | **Case B** `SdkException<RawError>` — 404 `StatusCode==NotFound` when no match | none | map `Customers.md`; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| Create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions=null, CancellationToken ct=default)` — body must pass explicitly | `CreateCustomerRequest{ Customer(customer): CreateCustomer{ FirstName(first_name) req, LastName(last_name) req, Email(email) req, Reference(reference) = buyerId } }` | `CustomerResponse` → `.Customer.Id (id): int?` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | none | map `Customers.md`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Errors/CreateCustomerError.cs`, `Models/CustomerErrorResponse1.cs` |
| Find subscription | `client.Subscriptions.FindSubscription(string? reference, RequestOptions? requestOptions=null, CancellationToken ct=default)` — reference must pass explicitly (query `reference`) | none | `SubscriptionResponse` → `.Subscription` (see below) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none | map `Subscriptions.md`; `Errors/FindSubscriptionError.cs` |
| Create subscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions=null, CancellationToken ct=default)` — body must pass explicitly | `CreateSubscriptionRequest{ Subscription(subscription): CreateSubscription{ ProductHandle(product_handle)=planHandle, CustomerId(customer_id)=maxioCustomerId, Reference(reference)=deterministic sub reference, PaymentCollectionMethod(payment_collection_method)=CollectionMethod.Remittance } }` | `SubscriptionResponse` → `.Subscription.{Id(id):int?, State(state):SubscriptionState?, ProductPriceInCents(product_price_in_cents):long?, CurrentPeriodEndsAt(current_period_ends_at):DateTimeOffset?, NextAssessmentAt(next_assessment_at):DateTimeOffset?, Reference(reference):string?, Product(product):Product?}` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | map `Subscriptions.md`; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/SubscriptionResponse.cs`, `Models/Subscription.cs`, `Errors/CreateSubscriptionError.cs`, `Models/ErrorListResponse1.cs` |
| List customer subs | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription.{State, Product.{Handle,Name}, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt, Id, Reference}` | **Case B** `SdkException<RawError>` | none | map `Customers.md`; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |

**Optional create-body fields:** on `CreateCustomer` everything except `FirstName/LastName/Email/Reference`
is omitted (address, tax, locale, branding, etc. → provider defaults). On `CreateSubscription` set
`ProductHandle`, `CustomerId`, `Reference`, and **`PaymentCollectionMethod = remittance`** — a purpose,
not a default: the priced plans carry no card, and `automatic` collection (the provider default) fails a
signup with "No payment method was on file for the $299.00 balance" (observed on `cp-exp-1`), so invoice/
remittance collection is what lets the brief's "payment method not required" hold. Configurable via
`Maxio:PaymentCollectionMethod` (default `remittance`; set `invoice` on legacy Statements-Architecture
sites). Still omitted: `credit_card_attributes`/`payment_profile_*` (no card), `product_price_point_*`
(→ product's default price point), `next_billing_at`/`initial_billing_at` (→ activate immediately).
`Reference` on both bodies is set because it IS our idempotency claim. No `import/migration only` field is set.

**Enums needed** (source `Models/Enums/…`):

| Enum | Members used (C# → wire) | Source |
| --- | --- | --- |
| `SubscriptionState` | `Active`→`active`, `Trialing`→`trialing`, `Pending`→`pending`, `Assessing`→`assessing`, `AwaitingSignup`→`awaiting_signup`, `FailedToCreate`→`failed_to_create`, `PastDue`→`past_due`, `SoftFailure`→`soft_failure`, `Unpaid`→`unpaid`, `Canceled`→`canceled`, `Expired`→`expired`, `OnHold`→`on_hold`, `Suspended`→`suspended`, `TrialEnded`→`trial_ended`, `Paused`→`paused` | `Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` | `Day`→`day`, `Month`→`month` | `Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` | `Remittance`→`remittance`, `Invoice`→`invoice`, `Automatic`→`automatic`, `Prepaid`→`prepaid` | `Models/Enums/CollectionMethod.cs` |

**Client construction / auth / server (source: `MaxioAdvancedBillingClientOptions.cs`, `Servers/ProductionOptions.cs`, sdk-map Servers&auth):**
- `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — only ctor. DI: `services.AddMaxioAdvancedBillingClient(opts => …)` (`ServiceCollectionExtensions.cs`).
- Auth: **Basic** — `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (username = Chargify API key, password literal `x`; Basic valid for US/EU). `using MaxioAdvancedBilling.Core.Authentication` (confirm namespace at impl).
- Environment: `options.Environment = ServerEnvironment.Us` (`MaxioAdvancedBilling.Servers`).
- Server target: when `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` verbatim; else `options.Server.Production.Us.Site = <Maxio:Subdomain>` (template `https://{site}.chargify.com`).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| `planHandle` accepted by subscribe must be a product `handle` returned for the configured family | `CreateSubscription` ← `ListProductsForProductFamily` | implementation — service validates handle ∈ family plans before subscribing; unknown → 400 |
| `customer_id` sent to subscribe must be the id returned for THIS buyer's reference | `CreateSubscription` ← `ReadCustomerByReference`/`CreateCustomer` | implementation — id comes only from the ensure-customer step keyed by buyerId |

---

## 3. Trap notes (name the hazard; do not resolve it)

- **Client/DI lifetime:** the `HttpClient`/handler pipeline must be long-lived & pooled, the SDK client wrapper's lifetime chosen accordingly — getting it wrong causes socket exhaustion or stale DNS. **MUST load dotnet-client-initialization.**
- **Auth silent-skip:** a credential never set is skipped and the request is sent *unauthenticated* rather than throwing — the failure looks like a bad key. **MUST load dotnet-authentication.**
- **List param binding:** `ListProductFamilies`/`ListProductsForProductFamily`/`ListCustomerSubscriptions` have many no-default nullable params; a positional call mis-binds. **MUST load dotnet-calling-endpoints.**
- **Models:** enums are `StringEnum<T>` (not C# enums) and request records use `required` init + wire names that differ from C# names. **MUST load dotnet-models.**
- **Error boundary:** each op is Case A (typed `{Operation}Error`) or Case B (`RawError`); mixing them up drops the status. **MUST load dotnet-error-handling.**
- **Resilience/logging:** `Timeout` is per-attempt not total, `POST` is not retried by default, and `LogRequestBody` logs JSON bodies unredacted. **MUST load dotnet-configuration-resilience.**
- **Testing seam:** the `HttpClient` ctor arg is the fake seam. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load ALL before implementation starts; this file does not carry their contents)

| Skill (load the copy shipped with **maxio-platforms-team**) | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | Step 2 — client + DI + HttpClient lifetime |
| `maxio-platforms-team:dotnet-authentication` | Step 2 — Basic credentials |
| `maxio-platforms-team:dotnet-calling-endpoints` | Steps 4-6 — every SDK call, named args |
| `maxio-platforms-team:dotnet-models` | Steps 4-6 — request records, enums, wire names |
| `maxio-platforms-team:dotnet-error-handling` | error boundary around every call |
| `maxio-platforms-team:dotnet-configuration-resilience` | Step 2 — retries/timeout/logging |
| `maxio-platforms-team:dotnet-testing` | integration tests |

**Two mandatory `System.Text.Json.JsonException` hazards (it reaches the boundary from two directions, needing opposite handling):**
1. A drifted/malformed **2xx** body (a missing `required` member) surfaces as `JsonException` from deserialization — **not** an `SdkException` — so an SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, replacing the `SdkException` and destroying the HTTP status.
The error boundary catches `JsonException` (and generic `Exception`) in addition to `SdkException<…>`.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioBillingServiceCollectionExtensions.AddMaxioBilling` validates `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` are non-blank (each part checked) and, if `Maxio:BaseUrl` present, non-blank; throws `InvalidOperationException` at registration so the host refuses to start. `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (`Maxio:ApiKey` etc.), loaded into config by the host; never in repo. Options built once at registration and captured in the singleton client → a rotated key needs a process restart (acceptable; documented). |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt** (set to 15s on the client). Each public service method opens a linked `CancellationToken` with a wall-clock deadline and passes it to every SDK + DB call: 30s for the reads (plans, my-subscriptions), 60s for subscribe (which fans out to ~5 sequential calls). Enforced in `MaxioSubscriptionBillingService` via `CancellationTokenSource.CreateLinkedTokenSource(ct)` + `CancelAfter(ReadBudget/SubscribeBudget)`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes are `POST` (`CreateCustomer`, `CreateSubscription`) → **never resent by the SDK**. Reads (`ReadCustomerByReference`, `FindSubscription`, both lists) are GET → may retry (idempotent). No PUT used. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: claim = Maxio `customer.reference` = buyerId (**provider-enforced unique**, source-verified). `CreateSubscription`: no caller idempotency key param exists; claim = deterministic subscription `reference` + local unique row; reconcile via `FindSubscription(reference)`. The generator's `Idempotency-Key` header (random GUID) is **not** used as a key. |
| 6 | Observability | Info: subscribe start/success (buyerId, planHandle, subscriptionId, state). Warn: 422/duplicate reconciled. Error: unexpected SDK/JSON failures with `RawError.StatusCode` + `ReadAsString()` body. `LogRequestBody` stays **off**. No card/PII in scope to correlate; Maxio error body string is logged at Error only. |
| 7 | Sensitive data | Request models in scope carry only name/email + handles/ids — **no** card or bank fields (no `payment_profile_attributes`/`credit_card_attributes` set). `LogRequestBody` stays off and `options.Logging.LoggerFactory` is assigned explicitly so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on from the environment. |
| 8 | Environment selection | Single server group `Production`, environment `ServerEnvironment.Us`. The SDK declares **no separate sandbox environment** — a Maxio sandbox is a dedicated **site** (`cp-exp-1`). Test traffic is kept off any live site by binding `Maxio:Subdomain`/`Maxio:BaseUrl` to the sandbox site; production deployments set them to the production site. Basic auth (US/EU only). |
| 9 | Duplicate prevention under concurrency | **Customer:** store = Maxio `customers`, column = `reference`; its unique constraint rejects the 2nd create with 422 (`CustomerErrorResponse1`), caught → re-read by reference. **Subscription:** store = local `MaxioBillingContext.SubscriptionEnrollments`, column = `Reference` (unique index); 2nd insert rejected by the unique constraint → `DbUpdateException` caught → adopt existing. (EF InMemory provider in this dev box does not enforce indexes — documented env limitation; production SQL Server enforces it. `FindSubscription(reference)` is the additional provider-side reconciliation.) Not an in-process lock, not a bare existence check. |
| 10 | Partial results | `subscription-plans`: single family, plans fit one page; we request `perPage: 200` and if a full page returns, the plan DTO list carries `Truncated=true` and the endpoint surfaces it. `my-subscriptions`: `ListCustomerSubscriptions` returns the full list (no page param) — not capped. |
| 11 | Startup validation vs test host | The credential check runs in `AddMaxioBilling`. The host-booting project `tests/PublicApiIntegrationTests` (`WebApplicationFactory<Program>`) is given placeholder `Maxio:*` config + `UseOnlyInMemoryDatabase=true` via `appsettings.test.json`; a new `SubscriptionEndpointsSmokeTest` boots the host and asserts the endpoints reject unauthenticated calls (401) without any Maxio call. **Run and green** (17 tests). The `TestApiApplication` fixture used by `tests/FunctionalTests` was given the same placeholders. |
| 12 | Ordering & no-op side effects | Local `SubscriptionEnrollment` row (state `pending`, carrying the deterministic `Reference`) is written **before** the `CreateSubscription` call, then updated with the Maxio id/state **after** it returns. The success log + confirmation are gated on an actually-created (or newly-adopted) subscription; a replay that finds an existing active subscription returns it **without** re-logging "created". |
| 13 | Unknown outcomes | If `CreateSubscription` transport fails after the request may have been received, the catch block re-reads via `Subscriptions.FindSubscription(reference)` (the same deterministic reference) and adopts the subscription if it landed; only if still absent is the failure surfaced. |
| 14 | Provider status & reconciliation | `CreateSubscription`/`ListCustomerSubscriptions` return `Subscription.State`. Branches: **done** = `active`/`trialing`; **not-yet** = `pending`/`assessing`/`awaiting_signup` (+ null/unreadable state → treated not-yet); **failed** = `failed_to_create`/`past_due`/`soft_failure`/`unpaid`/`canceled`/`expired`/`on_hold`/`suspended`/`trial_ended`/`paused`. A returned id alone is not treated as success — state is read. No two-source time reconciliation clock in scope. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| Create Maxio customer | Maxio `customers.reference` (= buyerId) | Maxio unique-reference constraint → 422 `CustomerErrorResponse1` | catch `SdkException<CreateCustomerError>`, `TryGetCustomerErrorResponse1` → re-read by reference | `MaxioSubscriptionBillingService.EnsureCustomerAsync` (the `catch (SdkException<CreateCustomerError>)` → `TryReadCustomerIdByReferenceAsync`) |
| Create subscription | local `MaxioBillingContext.SubscriptionEnrollments.Reference` unique index (durable, prod SQL Server) + Maxio subscription `reference` (observed to reject duplicates on `cp-exp-1`) | DB unique constraint → `DbUpdateException`; and Maxio 422 → reconcile | `SubscribeAsync` (`catch (DbUpdateException)` around the pre-call `_db.SaveChangesAsync`) and `CreateSubscriptionReconciledAsync` (`catch (SdkException<CreateSubscriptionError>)` → `TryFindSubscriptionAsync`) |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short | where in the code |
| --- | --- | --- | --- |
| List plans | `perPage` (request 200) | plan-list DTO field `Truncated: bool` returned in the endpoint response | `MaxioSubscriptionBillingService.GetPlansCoreAsync` (`products.Count >= PlansPageSize`) → `SubscriptionPlansResult.Truncated` → `ListSubscriptionPlansEndpoint` (`SubscriptionPlansResponse.Truncated`) |
| List customer subscriptions | uncapped (SDK returns full list, no page param) | n/a — not truncatable | `MaxioSubscriptionBillingService.GetMySubscriptionsAsync` |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| POST /api/subscriptions (double-click) | `wasNew = !enrollment.IsConfirmed` (the enrollment had no Maxio subscription id before this call) | the "Subscribed…" info log and `Created`/`Pending`/`Failed` result; a replay/adoption returns `AlreadySubscribed` and does not re-log | `MaxioSubscriptionBillingService.SubscribeAsync` (`wasNew` gate on the `_logger.LogInformation`) |
| Ensure customer | no existing link/reference resolved → a customer was created | the "Created Maxio customer…" log + link row insert; when the link or reference already resolves, no create, no log | `MaxioSubscriptionBillingService.EnsureCustomerAsync` |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| Create subscription | `Subscriptions.FindSubscription` | deterministic subscription `reference` | `MaxioSubscriptionBillingService.CreateSubscriptionReconciledAsync` (`catch (HttpRequestException or JsonException)` → `TryFindSubscriptionAsync`) |
| Create customer | `Customers.ReadCustomerByReference` | buyerId (customer reference) | `MaxioSubscriptionBillingService.EnsureCustomerAsync` (`catch (HttpRequestException or JsonException)` → `TryReadCustomerIdByReferenceAsync`) |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| Create subscription | `Subscription.State` (`SubscriptionState?`) | **done**→ `Created`/`AlreadySubscribed`: `active`,`trialing`. **not-yet**→ `Pending` (202): `pending`,`assessing`,`awaiting_signup`, and null/unreadable. **failed**→ `Failed` (502): `failed_to_create`,`past_due`,`soft_failure`,`unpaid`,`canceled`,`expired`,`on_hold`,`suspended`,`trial_ended`,`paused` | `MaxioSubscriptionBillingService.ClassifyFresh` / `ClassifyExisting` (consumed in `SubscribeAsync`/`BuildResult`; endpoint maps outcome→status in `SubscribeEndpoint`) |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| Create subscription | `SubscriptionEnrollment` row (buyerId, planHandle, deterministic `Reference`, unconfirmed, MaxioCustomerId) added + saved before the call | same row `Confirm(MaxioSubscriptionId, State)` + saved after it returns | `MaxioSubscriptionBillingService.SubscribeAsync` (add+`SaveChangesAsync` before `CreateSubscriptionReconciledAsync`; `enrollment.Confirm` + `SaveEnrollmentAsync` after) |
| Create customer | `MaxioCustomerLink` is written **after** the ensure-customer step resolves an id (customer identity lives in Maxio, keyed by `reference` = buyerId, which is written into the create request before the call) | `MaxioCustomerLink{ BuyerId, MaxioCustomerId }` | `MaxioSubscriptionBillingService.SaveCustomerLinkAsync` (called from `EnsureCustomerAsync`) |

---

## 6. Assumptions & Blockers

- **Buyer identity** = the JWT `ClaimTypes.Name` (the eShop username/email, e.g. `demouser@microsoft.com`); used verbatim as the Maxio `customer.reference` and to derive the deterministic subscription reference. (Minor assumption — matches how PublicApi issues tokens.)
- **Default plan** when `POST /api/subscriptions` omits a plan: `eshop-pro` per the brief, but the handle is a request field and defaults only when omitted; the configured product family (`Maxio:ProductFamilyHandle`) bounds the valid set.
- **Subscription `reference` uniqueness is NOT documented** by Maxio (source silent) → was `UNVERIFIED`. Design does not depend on it: the durable claim is the **local** unique index, plus a deterministic reference and `FindSubscription` before/after create to reconcile. **Observed on `cp-exp-1`:** 5 concurrent subscribe calls for the same buyer+plan produced exactly **one** subscription (verified via `my-subscriptions`), i.e. the find-before-create + create-then-reconcile path holds even though EF InMemory does not enforce the index — consistent with Maxio rejecting a duplicate subscription reference. The code still does not *assume* the 422; it reconciles regardless.
- **Payment method:** the seeded plans have a recurring price but no card. `automatic` collection fails signup ("No payment method was on file…"); the integration sends `payment_collection_method = remittance` so the subscription activates on invoice billing — this is what makes the brief's "payment method not required" true. Configurable via `Maxio:PaymentCollectionMethod`.
- No Blockers: every required capability exists in the map.
