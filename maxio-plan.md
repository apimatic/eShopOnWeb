# maxio-plan.md — Recurring-subscription billing for eShopOnWeb (Maxio Advanced Billing)

Additive, parallel capability: Maxio Advanced Billing becomes the billing system of record for
recurring subscriptions. All SDK facts below come from the bundled SDK map (`sdk-map.md`,
`map/operations/*.md`, `map/models/*.md`, `map/models/enums.md`) of the
`maxio-getting-started` skill. Page citations appear in each row's **Source** cell.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Register the SDK client in DI; bind configuration section `Maxio:` (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional) | none (construction only) |
| 2 | `GET /api/subscription-plans` — list plans in the configured product family (by handle), expose handle/name/price/interval | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 3 | Ensure a Maxio customer exists for the eShopOnWeb user (idempotent, keyed on `reference` = user id) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 4 | `POST /api/subscriptions` — subscribe the customer to a plan by handle (default `eshop-pro`, alt `basic-plan`); idempotent via subscription `reference`; confirm plan/price/state/next-billing-date back to the caller | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, `Products.ReadProductByHandle` (plan validation) |
| 5 | `GET /api/my-subscriptions` — the user's subscriptions | `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary for every call above (Case A typed / Case B raw ladder, JsonException handling) | — |

Catalog facts (given, stable handles; numeric IDs may be stale — **always resolve by handle,
never hardcode a numeric id**): Product Family `eshop-subscribe`; Plan `eshop-pro` ($299/mo,
default target); Plan `basic-plan` ($29/mo); metered component `api-call` ($0.01/unit, not used
by any in-scope flow — see §5). Both plans: no trial, no setup fee, payment method not required
(`require_credit_card = false`) — but live sandbox evidence shows signup still 422s unless
`PaymentCollectionMethod` is set (§2.4).

---

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

### 2.1 Package & client construction

| Fact | Value | Source |
|---|---|---|
| NuGet package / version | `AsadAli.AdvancedBilling.Sdk` **1.0.2** (`dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2`) — package id differs from root namespace | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` (client, options) | `sdk-map.md` |
| Target framework | `netstandard2.0` — works on .NET 8 | `sdk-map.md` |
| Client constructor (only one) | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options properties | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` — no other properties exist (notably: **no custom-headers / idempotency-key property**) | `sdk-map.md` |
| Auth shape | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<Maxio:ApiKey>", Password = "x" }` — **Username = API key, Password = the literal string `"x"`** | `sdk-map.md` |
| Sandbox targeting (subdomain) | `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) and `options.Server.Production.Us.Site = "<Maxio:Subdomain>"` — `{site}` defaults to the subdomain in the US template `https://{site}.chargify.com` | `sdk-map.md` |
| `Maxio:BaseUrl` optional override | when the binding is set, assign **verbatim**: `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"`; when unset, leave `BaseUrl` untouched (template `https://{site}.chargify.com` applies). Do not touch `options.Server.Ebb.*` (events-only group, unused here) | `sdk-map.md` |
| `RetryOptions` | namespace `MaxioAdvancedBilling.Core.Configuration`; **all members `required`** — start from `RetryOptions.Default()` and mutate, or set every member | `sdk-map.md` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| API groups used | `client.Customers`, `client.ProductFamilies`, `client.Products`, `client.Subscriptions` — every group is a property on the client | `sdk-map.md` |

### 2.2 Customers — ensure-exists (idempotent)

| | |
|---|---|
| **Lookup by reference** | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json?reference=…`, "Returns a customer by their unique reference ID … a single match" |
| Request | `string reference` (path of the eShopOnWeb user id, your chosen key) |
| Response | `MaxioAdvancedBilling.Models.CustomerResponse` → single field `Customer (customer): Customer !req` — read `Customer.Id (id): int?`, `Customer.Reference (reference): string?`, `Customer.Email (email): string?` |
| Error | **Case B** `SdkException<RawError>` — `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. A 404 (`StatusCode == HttpStatusCode.NotFound`) is the expected "no customer yet" signal |
| **Create** | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** |
| Request | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` → `CreateCustomer`: required **`FirstName (first_name): string !req`**, **`LastName (last_name): string !req`**, **`Email (email): string !req`**; in-scope optional **`Reference (reference): string?`** (= eShopOnWeb user id — "you may only create one customer for a given reference value"); everything else (`Organization`, `Address*`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, …) optional — leave out unless the app supplies them |
| Response | `CustomerResponse` (same envelope as above) |
| Error | **Case A** `MaxioAdvancedBilling.Errors.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — accessors: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` **[422]** · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback]. ⚠ the generated `CustomerErrorResponse1.Errors` payload is a suspicious shared model — see §2.6 defensive directive |

Source: `operations/Customers.md` (`CreateCustomer`, `ReadCustomerByReference`), `models/records-1-Ac-Cr.md` (`CreateCustomer`), `models/records-2-Cr-Ne.md` (`Customer`, `CustomerErrorResponse1`, `Errors`).

### 2.3 Catalog — list plans by product family handle; fetch one plan by handle

| | |
|---|---|
| **List families** | `client.ProductFamilies.ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 date params nullable, **no default → pass `null` explicitly** |
| Response | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; envelope `ProductFamily (product_family): ProductFamily?` → filter client-side on `ProductFamily.Handle (handle): string?` == `Maxio:ProductFamilyHandle` (`eshop-subscribe`); take `ProductFamily.Id (id): int?` for the next call. Handles are stable; numeric ids may be stale — this lookup is how the plan stays handle-driven |
| **List plans in family** | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params **must pass `null` explicitly**; `productFamilyId` is a **string** — pass the numeric family id as a string (`family.Id.Value.ToStringInvariant()` / `.ToString()`) |
| Response | `IReadOnlyList<ProductResponse>` |
| **Fetch one plan by handle** | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — `GET /products/handle/{api_handle}.json`; use for `eshop-pro` / `basic-plan` detail |
| Response | `ProductResponse { Product (product): Product !req }` — read `Product.Handle (handle): string?`, `Product.Name (name): string?`, `Product.PriceInCents (price_in_cents): long?`, `Product.Interval (interval): int?`, `Product.IntervalUnit (interval_unit): IntervalUnit?`, `Product.RequireCreditCard (require_credit_card): bool?`, `Product.ProductFamily (product_family): ProductFamily?` |
| Error / pagination | `ListProductFamilies`, `ReadProductByHandle`: **Case B** `SdkException<RawError>` (404 on unknown handle). `ListProductsForProductFamily`: **Case A** `MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError` — `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)`. Pagination: **manual `page`+`perPage`** (defaults 1 / 20), no page-token helper — loop pages until a short page returns |

Source: `operations/ProductFamilies.md`, `operations/Products.md`, `models/records-3-Of-Su.md` (`Product`, `ProductFamily`, `ProductResponse`, `ProductFamilyResponse`), `models/records-2-Cr-Ne.md` (`ListProductsFilter`), `models/enums.md` (`BasicDateField`, `ListProductsInclude`, `IntervalUnit`).

### 2.4 Subscriptions — create (payment-method-not-required), lookup, list mine

| | |
|---|---|
| **Find by reference (idempotency check)** | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `GET /subscriptions/lookup.json?reference=…`, "Finds a subscription by its reference." |
| Response | `SubscriptionResponse` |
| Error | **Case A** `MaxioAdvancedBilling.Errors.FindSubscriptionError` — `TryGetNoContent(out RawError)` **[404]** (= no subscription with that reference — the expected first-run signal) · `TryGetRawError(out RawError)` |
| **Create** | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly**; `POST /subscriptions.json` |
| Request | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. **No field of `CreateSubscription` is `!req`** — `required?` selects nothing; what makes the call accepted is identifying a product + customer. In-scope fields: **`ProductHandle (product_handle): string?`** (`eshop-pro` / `basic-plan`), **`CustomerId (customer_id): int?`** (from §2.3 lookup) *or* `CustomerReference (customer_reference): string?`, **`Reference (reference): string?`** (your deterministic idempotency key — see §2.5). Also available if the app needs them: `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `CustomPrice (custom_price): SubscriptionCustomPrice?`, `CouponCode (coupon_code): string?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `DeferSignup (defer_signup): bool? = false` (delayed signup creation — not a payment-method substitute), `NetTerms (net_terms): string?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`. **Set for cardless signup — `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`** → `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance` (wire `remittance`). The map's `CollectionMethod` summary (`models/enums.md`) names `remittance` as the manual-collection option of the current Relationship Invoicing Architecture (`automatic`/`prepaid` are the other RI options); on a legacy Statements Architecture site the valid manual option is `invoice` (`CollectionMethod.Invoice`). Live sandbox evidence: omitting this field 422'd with `{"errors":["No payment method was on file for the $299.00 balance"]}` even though both plans have `RequireCreditCard = false` — so set it explicitly. `NetTerms` and `DeferSignup` are **not** substitutes for a collection method — the map carries no semantic note tying either to cardless signup. Still omitted (no payment profile is captured): `PaymentProfileId (payment_profile_id)`, `PaymentProfileAttributes (payment_profile_attributes)`, `CreditCardAttributes (credit_card_attributes)`, `BankAccountAttributes (bank_account_attributes)` — do not send empty placeholders |
| Response | `SubscriptionResponse { Subscription (subscription): Subscription? }` — **nullable inner field: null-check before reading.** Fields this integration reads: `Subscription.Id (id): int?` · `Subscription.State (state): SubscriptionState?` · `Subscription.ProductPriceInCents (product_price_in_cents): long?` · `Subscription.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (period end) · `Subscription.NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date) · `Subscription.Reference (reference): string?` · `Subscription.Product (product): Product?` → `Product.Handle`, `Product.Name`, `Product.PriceInCents (long?)`, `Product.Interval (int?)`, `Product.IntervalUnit (IntervalUnit?)` |
| Error | **Case A** `MaxioAdvancedBilling.Errors.CreateSubscriptionError` — `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` **[422]** where `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (human-readable rejection list) · `TryGetRawError(out RawError)` [fallback]. A 422 is a deterministic rejection (bad handle, missing customer, product requires payment info) — surface it as a caller-facing failure, never as a transient outage |
| **List mine** | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `GET /customers/{customer_id}/subscriptions.json`, "Lists all subscriptions that belong to a customer." **Note the accessor: `client.Customers`, not `client.Subscriptions` — the operation lives on the Customers controller** |
| Response / Error | `IReadOnlyList<SubscriptionResponse>` (same inner fields as above; filter by `State` / `Product.Handle` client-side). **Case B** `SdkException<RawError>` |
| **Read one** | `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, **no default → pass `null` explicitly**; `SubscriptionResponse`; **Case B** `SdkException<RawError>` |

Source: `operations/Subscriptions.md` (`CreateSubscription`, `FindSubscription`, `ReadSubscription`), `operations/Customers.md` (`ListCustomerSubscriptions`), `models/records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`, `ErrorListResponse1`), `models/records-3-Of-Su.md` (`Subscription`), `models/records-4-Su-We.md` (`SubscriptionResponse`), `models/enums.md` (`CollectionMethod`).

### 2.5 Idempotency

| Fact | Value | Source |
|---|---|---|
| SDK idempotency-key support | **None.** `CreateSubscription`'s signature takes only `body` and `ct` — no idempotency-key parameter; `MaxioAdvancedBillingClientOptions` has no custom-header property (§2.1). There is no SDK-level mitigation for a duplicated `POST /subscriptions.json` | `operations/Subscriptions.md`, `sdk-map.md` |
| Customer ensure-exists pattern | **Lookup-by-reference, not list-and-filter**: `ReadCustomerByReference(userId)` → 404 ⇒ `CreateCustomer` with `Reference = userId`; on a 422 from `CreateCustomer` (e.g. a concurrent double-click created it first), **re-run `ReadCustomerByReference`** and treat the found customer as the result. `ListCustomers` with `q` is a search filter (paged, multiple matches) — the lookup endpoint is the single-match path the Notes name for exact reference matching | `operations/Customers.md` |
| Subscription create pattern | Set `CreateSubscription.Reference` to a deterministic value per (user, plan) — e.g. `"{userId}:{productHandle}"` — then call `FindSubscription(reference)` first: `TryGetNoContent` (404) ⇒ safe to create; found ⇒ return the existing subscription. This survives a double-click even though the SDK has no idempotency key | `operations/Subscriptions.md` |
| Retry interaction | Because a transport failure is retried by the client pipeline on every verb (including `POST`), the application-level `reference` pattern above is the *only* guard — see the ⚠ trap note on Step 1 | `sdk-map.md` |

### 2.6 Error surface (whole sheet at a glance)

- Every operation is **throw-only**; there are **no** no-throw `…Result`/`ApiResult` variants in this SDK.
- **Case A (typed)** — `MaxioAdvancedBilling.Errors.SdkException<{Operation}Error>` where `{Operation}Error : MaxioAdvancedBilling.Core.ErrorResponse.ApiError`; read via `TryGet…(out …)` accessors; every typed error also has `TryGetRawError(out RawError)` as fallback. In scope: `CreateCustomerError` (422 → `CustomerErrorResponse1`), `CreateSubscriptionError` (422 → `ErrorListResponse1`), `FindSubscriptionError` (404 → `TryGetNoContent`), `ListProductsForProductFamilyError` (404 → `TryGetString`).
- **Case B (raw)** — `MaxioAdvancedBilling.Errors.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`. In scope: `ReadCustomerByReference` (404 expected), `ReadProductByHandle` (404 on bad handle), `ListProductFamilies`, `ListCustomerSubscriptions`, `ReadSubscription`.
- **401/403** are config-shaped: check `BasicAuthCredentials.Username` = API key and `Password` = literal `"x"`, and the subdomain/BaseUrl wiring — not a call-site bug.
- **422** on create ops is a deterministic rejection carrying an error list (`ErrorListResponse1.Errors: IReadOnlyList<string>`) — map to a caller-facing failure; a caller retrying it retries something that can never succeed.
- **Defensive directive (UNVERIFIED):** the generated `CustomerErrorResponse1.Errors` payload is `Errors { PerPage (per_page): IReadOnlyList<string>? · PricePoint (price_point): IReadOnlyList<string>? }` — only two field-specific keys for an endpoint whose validation messages are mostly about `first_name`/`last_name`/`email`/`reference`. Unmodeled JSON fields are dropped on deserialize, so the real 422 messages will likely be absent from this object. Extract best-effort; **fall back to `TryGetRawError` + `ReadAsString()` for the raw body** before reporting "unknown rejection". (Suspicious shared model — `CustomerErrorResponse` is a second, identically-shaped variant; whether the live wire matches either is only confirmable from live traffic.)
- The 422 payload shape on `CreateSubscription` (`ErrorListResponse1.Errors: IReadOnlyList<string>`) is a plain string list — read it directly, no defensive fallback needed beyond the raw-error fallback. Confirmed by the live sandbox 422 body (`{"errors":["No payment method was on file for the $299.00 balance"]}`), which matches this shape.

### 2.7 Enum values needed (namespace `MaxioAdvancedBilling.Models.Enums` — `StringEnum<T>` records, NOT C# enums; members are the literal C# identifiers)

| Enum | Members (`CSharpMember (wire)`) | Used by |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `Subscription.State`; treat `Active`/`Trialing`/`Assessing` as live; `Assessing` is transient — do not gate access on it |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `Product.IntervalUnit` (both plans: `Month`) |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `CreateSubscription.PaymentCollectionMethod` (§2.4) — set `Remittance` for cardless/manual-billing signup; `Invoice` is the legacy Statements-architecture equivalent. Summary: "For legacy Statements Architecture valid options are - `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are - `remittance`, `automatic`, `prepaid`." |

Source: `models/enums.md`. (`BasicDateField`, `ListProductsInclude` are not constructed by this flow — date/include params are passed `null`.)

---

## 3. Trap notes (one line per hazard, at the step where it bites)

> ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register, and the retry pipeline's treatment of non-idempotent verbs decides whether a retried `CreateSubscription` can double-create. **MUST load `dotnet-configuration-resilience`** before wiring the client.

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline must be long-lived and shared via `IHttpClientFactory`; constructing per request degrades the connection pool and the retry pipeline's state. **MUST load `dotnet-client-initialization`** before writing the factory or the DI registration.

> ⚠ Step 1 (credentials) — credentials are set on the options object before/at client construction and must come from configuration, never hardcoded; rotation and per-environment wiring have shape traps. **MUST load `dotnet-authentication`** before wiring `Maxio:ApiKey` in.

> ⚠ Step 2–4 (calls) — `ListProductFamilies`, `ListProductsForProductFamily` and `ListCustomers` take 5–14 nullable parameters with **no C# default**; a positional call mis-binds silently. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Operation}(...)` call.

> ⚠ Step 2–4 (models) — enums are `StringEnum<T>` (not C# enums), request records are immutable with `required`-member initializers, and unmodeled JSON fields are silently dropped on deserialize — which is exactly what makes the §2.6 `CustomerErrorResponse1` payload unreliable. **MUST load `dotnet-models`** before constructing any request or reading any response field.

> ⚠ Step 6 (error boundary) — the Case A / Case B split is per-operation (§2.2–2.4 name each one), `TryGetRawError` is not a catch-all on typed errors, and `JsonException` reaches the boundary from two directions (see REQUIRED READING) — a catch ladder written without the skill will let a malformed 2xx escape and map a deterministic 422 to a 5xx. **MUST load `dotnet-error-handling`** before writing any try/catch around an SDK call.

> ⚠ Tests — the `HttpClient` constructor argument is the test seam for stubbing the SDK; the wrong seam forces live HTTP or reflection. **MUST load `dotnet-testing`** before writing tests for the integration layer.

---

## 4. REQUIRED READING

Load each of these **before implementation starts** — the sheet deliberately does not carry their contents:

- `dotnet-client-initialization` · Step 1 — DI registration, factory shape, `HttpClient` lifetime.
- `dotnet-authentication` · Step 1 — wiring `Maxio:ApiKey` into `BasicAuthCredentials`, per-environment config, rotation.
- `dotnet-calling-endpoints` · Steps 2–4 — named-argument rule for the multi-parameter list/search operations.
- `dotnet-models` · Steps 2–4 — request construction, `StringEnum<T>`, required members, wire-name vs property-name.
- `dotnet-error-handling` · Step 6 — the Case A/B exception boundary and the two JsonException hazards below.
- `dotnet-configuration-resilience` · Step 1 — retry/timeout semantics, base-URL override, manual pagination.
- `dotnet-testing` · Tests — the seam to fake for every operation above.

Mandatory hazard rows (reaching the error boundary from two directions, needing **opposite** handling):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Assumption — US hosting:** the sandbox site is US-hosted, so `ServerEnvironment.Us` + `options.Server.Production.Us.Site` is the right wiring; if the account is EU-hosted, use the `.Eu` mirror properties instead (map documents both templates).
- **Assumption — `BaseUrl` scope:** `Maxio:BaseUrl` overrides only the Production server group (`options.Server.Production.Us.BaseUrl`), set verbatim; the Ebb (events) group is untouched because no in-scope operation uses it.
- **YOUR CALL — not in the map:** subscription `reference` format `"{userId}:{productHandle}"` (per-user-per-plan uniqueness) and customer `reference` = eShopOnWeb user id are application decisions; the map only guarantees reference uniqueness/enforcement semantics.
- **YOUR CALL — not in the map:** the metered component `api-call` is in the sandbox catalog but no in-scope flow records usage, so it is not attached at signup (`CreateSubscription.Components` exists if a later scope adds it). Endpoint conventions on `PublicApi` (routes, JWT identity extraction, persistence) are likewise the implementer's.
- **Trust note — generated disagreement:** `ReadProductFamily`'s Notes say a family can be addressed as `handle:my-family`, but its generated parameter is `int id` — the plan avoids that operation entirely and resolves the family via `ListProductFamilies` (client-side handle filter), so nothing depends on the ambiguity.
- **UNVERIFIED:** whether the live sandbox's 422 customer-error body matches `CustomerErrorResponse1`/`Errors` (see §2.6 defensive directive). Everything else in this sheet is settled by the map.
- **UNVERIFIED (from live sandbox evidence):** which billing architecture the sandbox site runs. `remittance` is a valid collection method only on Relationship Invoicing sites; `invoice` is the legacy Statements-architecture equivalent (map: `models/enums.md`, `CollectionMethod` summary). The `Site` model carries `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` (`models/records-3-Of-Su.md`) but no in-scope operation reads it. Defensive directive: if the `Remittance` signup is rejected with a 422, extract best-effort and retry the same create with `CollectionMethod.Invoice` as the fallback, surfacing whichever rejection remains.
- **Blockers:** none — every operation the scope needs exists in the map and is contracted above.
