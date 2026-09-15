# Maxio Advanced Billing — eShopOnWeb hero flow (subscribe)

Package `AsadAli.AdvancedBilling.Sdk` (`v1.0.2` / commit `15db14b`). Root namespace `MaxioAdvancedBilling`. Map: `sdk-map.md`.

Hero flow (additive/parallel; does not replace cart/checkout): JWT-authenticated PublicApi endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. Sandbox catalog already exists — **do not create it**. Payment method is **not** required; **do not** call Maxio.js / `chargify_token` / payment profiles.

Stable handles: family `eshop-subscribe`; products `eshop-pro` ($299.00/mo), `basic-plan` ($29.00/mo). Metered component `api-call` is seeded and unused here.

**Out of scope (do not call):** payment profiles, coupons, plan migration, component allocations/usage, cancel/pause/resume, invoices/credits, billing portal, webhooks, catalog writes (`CreateProduct` / `CreateProductFamily` / …).

---

## Scope & sequence

1. **Package + client + auth + site** — NuGet `AsadAli.AdvancedBilling.Sdk`; `AddMaxioAdvancedBillingClient`; bind `Maxio:ApiKey` (`MAXIO_API_KEY`), `Maxio:Subdomain` (`MAXIO_SITE_SUBDOMAIN`), `Maxio:ProductFamilyHandle` (`MAXIO_DEFAULT_PRODUCT_FAMILY`), `Maxio:BaseUrl`, `MAXIO_ENVIRONMENT`. Hard-code none of those values.
2. **GET /api/subscription-plans** — `ProductFamilies.ListProductsForProductFamily` with `productFamilyId: "handle:" + Maxio:ProductFamilyHandle` (page until empty). Optional: `Products.ReadProductByHandle` for a single handle. Do **not** use `ReadProductFamily(int id)` to resolve a handle (C# param is `int`).
3. **Ensure customer (idempotent)** — `Customers.ReadCustomerByReference` with eShop user id as `reference`. On miss (`SdkException<RawError>`, check `StatusCode`), `Customers.CreateCustomer`. On 422 unique-reference, `ReadCustomerByReference` again.
4. **POST /api/subscriptions (idempotent enroll)** — `Subscriptions.FindSubscription(reference)` with a stable per-user-per-plan `Reference` (e.g. `"{buyerId}:{productHandle}"`). If 404 (`TryGetNoContent`), `Subscriptions.CreateSubscription` with `ProductHandle` + `CustomerId` (or `CustomerReference`) + that `Reference` + `PaymentCollectionMethod = CollectionMethod.Remittance`. Omit every payment-profile / card / `chargify_token` field.
5. **GET /api/my-subscriptions** — `Customers.ListCustomerSubscriptions(customerId)` (no customer filter on `ListSubscriptions`). Return plan handle, price, `State`, `NextAssessmentAt`.
6. **Error boundary + tests** — throw-only SDK; `HttpClient` seam.

---

## CONTRACT SHEET

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

### Client construction & auth (`sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/ServerEnvironment.cs`)

| Fact | Value |
|---|---|
| Package | `AsadAli.AdvancedBilling.Sdk` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` |
| Only ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| DI | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| Auth | HTTP Basic — `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = config["Maxio:ApiKey"], Password = "x" }` |
| Environment | `options.Environment` is `MaxioAdvancedBilling.Servers.ServerEnvironment` (`StringEnum`). Members: `Us` (wire `US`, default), `Eu` (wire `EU`). **No `FromValue` on this type** (unlike `CollectionMethod`). Map `MAXIO_ENVIRONMENT` `"US"`/`"EU"` (case-insensitive) onto `ServerEnvironment.Us` / `ServerEnvironment.Eu`. Resolve uses **either** Us **or** Eu Production options, not both. |
| Site (always bind subdomain) | `options.Server` is `MaxioAdvancedBilling.ServerOptions` (root namespace). `options.Server.Production` is `MaxioAdvancedBilling.Servers.ProductionOptions`. Subdomain: `options.Server.Production.Us.Site` (`ProductionOptions.UsOptions.Site: string`, default `"subdomain"`) **and** `options.Server.Production.Eu.Site` (`ProductionOptions.EuOptions.Site`). Bind both from `Maxio:Subdomain`. |
| BaseUrl override | Default templates: Us `"https://{site}.chargify.com"`, Eu `"https://{site}.ebilling.maxio.com"`. `{site}` is substituted from `.Site` only when the token `{site}` appears in `BaseUrl` (`Core/TemplateParamsFactory.cs`). When `Maxio:BaseUrl` is set, assign that string **verbatim** to **both** `options.Server.Production.Us.BaseUrl` and `options.Server.Production.Eu.BaseUrl`. Still set `.Site` from `Maxio:Subdomain` (harmless if the override has no `{site}` placeholder; required if it does). |
| Retry | `options.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (`required` members — full instance or `RetryOptions.Default()`): `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` |
| Controllers used | `client.ProductFamilies`, `client.Products`, `client.Customers`, `client.Subscriptions` (`MaxioAdvancedBilling.Api`) |
| Config keys | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional), plus env `MAXIO_ENVIRONMENT`. Hard-code none. |

### Error model (every operation) (`sdk-map.md`)

Throw-only. Catch `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`. `TError` is Case A (`MaxioAdvancedBilling.Errors.{Op}Error` with `TryGet…`) or Case B (`MaxioAdvancedBilling.Core.ErrorResponse.RawError`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). Typed errors inherit `TryGetRawError(out RawError)`. No `…Result` / no-throw variants exist.

Shared error payloads (`MaxioAdvancedBilling.Models`, `records-2-Cr-Ne.md` unless noted):

| Record | Fields |
|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` |
| `CustomerErrorResponse1` | `Errors (errors): Errors?` — `Errors` is `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?`. **UNVERIFIED** vs a typical customer 422 body — if that shape is empty, fall back to `TryGetRawError` + `ReadAsString()` (best-effort). |

---

### Step 2 — List plans for the configured family handle

There is **no** `ReadProductFamilyByHandle`. `ReadProductFamily(int id, …)` cannot take a handle (`Api/ProductFamilies.cs` path param is `int`). `ListProductsFilter` has **no** family-handle field. Family-scoped list:

| Controller | Method (params in order) | Request | Response envelope / reads | Error | Pagination | Map |
|---|---|---|---|---|---|---|
| `ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params (`dateField` … `include`) nullable, no default → **must pass `null`**. Defaults: `page` = 1, `perPage` = 20 (max 200; values over 200 become 200 — `Api/ProductFamilies.cs`). | Path `{product_family_id}` ← `productFamilyId`. Pass **`"handle:" + Maxio:ProductFamilyHandle`** (XML: “id or its handle prefixed with `handle:`”). Query: `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include`. Hero call: `productFamilyId: "handle:eshop-subscribe"` (from config), all optionals `null`, then page. | `IReadOnlyList<ProductResponse>` → `.Product` (`product`) | Case A `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual `page`+`perPage`; empty list = last page | `operations/ProductFamilies.md`, `Api/ProductFamilies.cs` |
| `Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `api_handle` | `ProductResponse` → `.Product` | Case B `SdkException<RawError>` | none | `operations/Products.md` |
| `Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — first 8 must pass explicitly. **Not family-scoped** — do not use for GET /api/subscription-plans. | query `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `page`, `per_page`, `include_archived`, `include` | `IReadOnlyList<ProductResponse>` → `.Product` | Case B | `page`=1, `perPage`=20 | `operations/Products.md` |

**`ListProductsFilter`** (`MaxioAdvancedBilling.Models`, `records-2-Cr-Ne.md` / `Models/ListProductsFilter.cs`) — same type for `ListProducts` and `ListProductsForProductFamily`. **No product-family member.**

| C# name | Wire | Type |
|---|---|---|
| `Ids` | `ids` | `IReadOnlyList<int>?` |
| `PrepaidProductPricePoint` | `prepaid_product_price_point` | `PrepaidProductPricePointFilter?` |
| `UseSiteExchangeRate` | `use_site_exchange_rate` | `bool?` |

`PrepaidProductPricePointFilter` (`records-3-Of-Su.md`): `ProductPricePointId (product_price_point_id): string` (source default `"not_null"`). `ListProductsInclude`: `PrepaidProductPricePoint (prepaid_product_price_point)` (`enums.md`).

**`ProductResponse`** → `Product (product): Product !req` (`records-3-Of-Su.md`). Hero reads on `Product`: `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `TrialPriceInCents (trial_price_in_cents): long?`, `InitialChargeInCents (initial_charge_in_cents): long?`, `ProductPricePointHandle (product_price_point_handle): string?`.

---

### Step 3 — Ensure customer (idempotent)

| Method | Signature | Request | Response | Error | Pag | Map |
|---|---|---|---|---|---|---|
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` ← eShop user id | `CustomerResponse` → `Customer (customer): Customer !req` | **Case B** `SdkException<RawError>` — accessors `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. Map/source **do not** declare a typed 404. Treat miss as `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound` (**UNVERIFIED** that the live lookup is always 404; there is no `TryGetNoContent`). This **is** the lookup for “ensure customer”. | none | `operations/Customers.md`, `Api/Customers.cs` |
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`. `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set to the same eShop user id; unique). Optional address/phone/locale omitted unless the user record has them. | `CustomerResponse` → `.Customer` | Case A `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError`. Unique-`reference` collision is a 422. After 422, call `ReadCustomerByReference` again (race). | none | `operations/Customers.md`, `Errors/CreateCustomerError.cs` |

**`Customer` reads** (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`. Persist `Id`.

---

### Step 4 — Enroll (idempotent subscribe, no payment)

| Method | Signature | Request | Response | Error | Pag | Map |
|---|---|---|---|---|---|---|
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must pass | query `reference` | `SubscriptionResponse` → `Subscription (subscription): Subscription?` | Case A `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError`. **This is the correct 404 path** for “already enrolled?” | none | `operations/Subscriptions.md`, `Errors/FindSubscriptionError.cs` |
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req` | `SubscriptionResponse` → `.Subscription` | Case A `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | `operations/Subscriptions.md`, `Models/CreateSubscription.cs` |

**`CreateSubscription` runtime required (source XML, none are C# `required`):** `ProductHandle (product_handle)` **or** `ProductId (product_id)`; **and** `CustomerId (customer_id)` **or** `CustomerReference (customer_reference)` **or** `CustomerAttributes`. Hero body: `ProductHandle` + `CustomerId` (or `CustomerReference`) + `Reference` + **`PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance`** (wire `remittance`). Still **omit** `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, and any chargify token. Do **not** set `CollectionMethod.Automatic` (that is the 422 `No payment method was on file for the $299.00 balance` path — omitting the field uses the site default, which is automatic). Do **not** set `Prepaid` (prepaid collection). Do **not** set `Invoice` on Relationship Invoicing (legacy Statements only; RI valid values are `remittance` / `automatic` / `prepaid` — `enums.md`, `Models/Enums/CollectionMethod.cs`). Do **not** set `DeferSignup` (Awaiting Signup Date, unknown first bill). Do **not** set `NextBillingAt` (import field that skips the initial capture by moving the first bill; not remittance enroll). `require_credit_card: false` does **not** skip collecting a due balance under automatic collection. Live remittance-without-card on this sandbox is **UNVERIFIED**.

**`Reference` for double-click:** `CreateSubscription.Reference` is “the reference value (provided by your app) for the subscription itself.” `FindSubscription` “Finds a subscription by its reference.” Use a **stable unique** value per shopper+plan, e.g. `"{buyerId}:{productHandle}"`. On POST: `FindSubscription` first; if `TryGetNoContent` (404), create with that same `Reference`; if found, return the existing subscription (do not create a second).

**`Subscription` reads** (`records-3-Of-Su.md`): `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Product (product): Product?` (then `.Handle`), `Customer (customer): Customer?`, `Reference (reference): string?`. Envelope field `.Subscription` is nullable.

---

### Step 5 — List the shopper’s subscriptions

| Method | Signature | Request | Response | Error | Pag | Map |
|---|---|---|---|---|---|---|
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `{customer_id}` | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | Case B `SdkException<RawError>` | **none** (no `page`/`perPage`; one shot) | `operations/Customers.md`, `Api/Customers.cs` |

`ListSubscriptions` has **no** customer-id filter (params: `state`, `product`, `productPricePointId`, `coupon`, `couponCode`, dates, `metadata`, `direction`, `sort`, `include`, `page`, `perPage` — `operations/Subscriptions.md`). **Do not** use it for GET /api/my-subscriptions.

---

### Enums in scope (`map/models/enums.md`; `ServerEnvironment` in `MaxioAdvancedBilling.Servers`)

| Enum | Namespace | Members (`CSharp (wire)`) |
|---|---|---|
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Us (US)`, `Eu (EU)` — no `FromValue` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` |
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `ListProductsInclude` | `MaxioAdvancedBilling.Models.Enums` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. Hero: **`CollectionMethod.Remittance`**. RI valid: remittance / automatic / prepaid. Legacy Statements: invoice / automatic. |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime vs the SDK wrapper lifetime are not the same decision; getting either wrong exhausts sockets or shares handlers incorrectly. **MUST load `dotnet-client-initialization`** before `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — Basic username/password mapping is not a generic “API key header”; a 401 follows from putting the key in the wrong credential slot or hardcoding it. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ Step 1 (site URL / retries) — retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be re-sent is not visible from `RetryOptions` names; a missing `{site}` or a `BaseUrl` that still contains `{site}` without `.Site` set miss the tenant. **MUST load `dotnet-configuration-resilience`** before wiring `Retry`, `Timeout`, `Server.Production.*.Site`, or `BaseUrl`.

⚠ Steps 2–5 (every call) — optional parameters without C# defaults mis-bind if passed positionally; the token parameter is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(…)`.

⚠ Steps 2–5 (models) — envelopes (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`) must be unwrapped; enums are `StringEnum<T>`; `CreateCustomer` `required` members (`FirstName`/`LastName`/`Email`) fail at runtime if omitted. **MUST load `dotnet-models`** before constructing payloads or mapping responses.

⚠ Step 6 (error boundary) — Case A vs Case B differs per operation (this sheet); `TryGetRawError` is not a catch-all on every typed error the way a single `catch (Exception)` is. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 6 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (tests) — the test seam is not the generated controllers. **MUST load `dotnet-testing`** before stubbing Maxio.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `MaxioAdvancedBillingClient` and `HttpClient` ownership |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` and config-sourced API key |
| `dotnet-calling-endpoints` | Steps 2–5 — named arguments, `ct:`, optional-without-default params |
| `dotnet-models` | Steps 2–5 — envelopes, `required`, `StringEnum<T>` |
| `dotnet-error-handling` | Step 6 — Case A/B, `TryGet…`, both `JsonException` directions above |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, Production `{site}` / `BaseUrl`, list pagination |
| `dotnet-testing` | Step 6 — `HttpClient` seam, error-path tests |

---

## Assumptions & Blockers

**Assumptions**

- Additive PublicApi endpoints only; cart/checkout unchanged. JWT identity supplies the eShop user id (customer `reference`) plus name/email for `CreateCustomer`.
- Sandbox catalog already exists. Family handle from `Maxio:ProductFamilyHandle` (expected `eshop-subscribe`). Product handles `eshop-pro` and `basic-plan` are stable; numeric IDs are not stored. Do not create/update catalog. Metered component `api-call` is unused.
- Both plans: no trial, no setup fee, expires never, taxable no, **`require_credit_card` false**. Hero subscribe sends **no** payment profile, **no** Maxio.js/`chargify_token`, **no** card fields. **Do** set `PaymentCollectionMethod = CollectionMethod.Remittance` so a due product balance is not collected as automatic (live 422: `No payment method was on file for the $299.00 balance`).
- One Maxio customer per eShop user via unique `Customer.Reference` = user id. Idempotent ensure: `ReadCustomerByReference` then create; 422 unique-reference → read again.
- One Maxio subscription per user+plan via `CreateSubscription.Reference` = `"{buyerId}:{productHandle}"` looked up with `FindSubscription`. Double-click does not create a second subscription.
- `MAXIO_ENVIRONMENT` is `US` or `EU` (wire values). `Maxio:BaseUrl` when present is a full API origin assigned verbatim to Production Us and Eu `BaseUrl`.
- `CustomerErrorResponse1.Errors` shape vs live 422 bodies is **UNVERIFIED** — extract best-effort, then `TryGetRawError` / `ReadAsString()`.
- HTTP status for `ReadCustomerByReference` miss is **UNVERIFIED** in the map (Case B only); code should branch on `RawError.StatusCode == NotFound`.
- Create without payment succeeding for these products is **UNVERIFIED** beyond `require_credit_card` false and omitted payment fields.

**Blockers**

- None for planning. Implementation must bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`, and `MAXIO_ENVIRONMENT` from configuration — never hard-code them.
