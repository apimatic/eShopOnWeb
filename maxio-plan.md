# maxio-plan.md — Maxio Advanced Billing subscriptions for eShopOnWeb

Integration: recurring-subscription billing via the **AsadAli.AdvancedBilling.Sdk** NuGet package
(root namespace `MaxioAdvancedBilling`), additive to the existing cart/checkout flow. Three
PublicApi endpoints: plan listing, idempotent enrollment, and "my subscriptions".

---

## 1. Scope & sequence

| Step | Work | SDK operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` (v1.0.2) to the PublicApi project; bind the `Maxio:` config section; build/register the SDK client (DI singleton over `IHttpClientFactory`). | client construction only |
| 2 | `GET /api/subscription-plans` — list products site-wide, filter client-side to the configured product family, map to plan DTOs (handle, name, price/cents, interval). | `client.Products.ListProducts` |
| 3 | Find-or-create Maxio customer for the authenticated user, keyed on an external reference derived from the eShopOnWeb user identity. | `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer` |
| 4 | `POST /api/subscriptions` — validate the plan handle (`ReadProductByHandle`), check duplicates (`FindSubscription` by reference + `ListCustomerSubscriptions`), then `CreateSubscription` carrying a deterministic subscription `reference`. On post-create failure (e.g. response drift), roll back with `CancelSubscription`. | `client.Subscriptions.CreateSubscription`, `client.Subscriptions.FindSubscription`, `client.Products.ReadProductByHandle`, `client.Customers.ListCustomerSubscriptions`, `client.SubscriptionStatus.CancelSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve the user's Maxio customer by reference, list their subscriptions, map plan/price/state/next-billing. | `client.Customers.ReadCustomerByReference`, `client.Customers.ListCustomerSubscriptions` |
| 6 | Error boundary shared by all three endpoints (Case A / Case B ladder + the two JsonException hazards). | — |

Out of scope for these three endpoints: the `api-call` metered component (present in the
catalog but not exercised by plan list / enroll / my-list), invoices, coupons, webhooks.

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

Controller properties live on the client: `client.Customers`, `client.Subscriptions`,
`client.SubscriptionStatus`, `client.Products`, `client.ProductFamilies`
(namespace `MaxioAdvancedBilling.Api`). Every operation is **throw-only** — there are no
`…Result` no-throw variants anywhere in this SDK.

### 2.1 Operations

| # | Operation | Signature (verbatim) | Returns | Error case & accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| 1 | Find customer by reference | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` required | `MaxioAdvancedBilling.Models.CustomerResponse` | **Case B** `SdkException<RawError>` — `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. 404 ⇒ customer absent (the find half of find-or-create) | none | operations/Customers.md |
| 2 | Create customer | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default ⇒ **must pass explicitly** | `CustomerResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Customers.md |
| 3 | Create subscription | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `MaxioAdvancedBilling.Models.SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md |
| 4 | Find subscription by reference (idempotency lookup) | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must pass explicitly (may be `null`) | `SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]. 404 = no subscription with that reference | none | operations/Subscriptions.md |
| 5 | List a customer's subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` required, non-nullable | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` | **Case B** `SdkException<RawError>` | none (returns all) | operations/Customers.md |
| 6 | List products (plans) — **primary path** | `client.Products.ListProducts(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable with **no default ⇒ must pass explicitly; pass `null` to skip** | `IReadOnlyList<ProductResponse>` | **Case B** `SdkException<RawError>` | manual `page` + `perPage` (defaults 1/20) — page until a short/empty page | operations/Products.md |
| 7 | List products for a product family — alternative to #6 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` must pass explicitly (`null` to skip) | `IReadOnlyList<ProductResponse>` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` | operations/ProductFamilies.md |
| 8 | Read product by handle (validate plan handle on enroll) | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — `apiHandle` required | `ProductResponse` | **Case B** `SdkException<RawError>` — 404 ⇒ unknown handle | none | operations/Products.md |
| 9 | Cancel subscription (error rollback) | `client.SubscriptionStatus.CancelSubscription(int subscriptionId, MaxioAdvancedBilling.Models.CancellationRequest? body, CancellationToken ct = default)` — `body` must pass explicitly; to cancel **immediately**, omit the schedule params inside the body | `SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CancelSubscriptionApiError>` (note the name — not `CancelSubscriptionError`): `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out MaxioAdvancedBilling.Models.AnyOf.CancelSubscriptionErrorResponse)` [422, union] · `TryGetRawError(out RawError)` [fallback] | none | operations/SubscriptionStatus.md; union in models/unions.md |

Notes carried from the map's operation pages:

- **CreateSubscription** (row 3): "Specify the product with `product_id` or `product_handle`.
  … Identify an existing customer with `customer_id` or `customer_reference`. Optionally,
  include an existing payment profile using `payment_profile_id`. To create a new customer,
  pass `customer_attributes`." → **referencing the product by handle** = set
  `CreateSubscription.ProductHandle`; by id = `CreateSubscription.ProductId`. Referencing the
  customer by external key = `CreateSubscription.CustomerReference` (no pre-created customer
  needed) or by Maxio id = `CreateSubscription.CustomerId`.
- **Payment**: the Notes say payment information "may be required … depending on the options
  for the Product".
- **LIVE FINDING (sandbox):** `CreateSubscription` built exactly per §2.2 —
  `CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle, CustomerId,
  Reference } }`, **no payment fields, no `PaymentCollectionMethod`** — returned **422 with body
  "No payment method was on file for the $299.00 balance"**. What the map (models/enums.md) and the
  SDK source (`Models/Enums/CollectionMethod.cs`) document about `CollectionMethod` is only
  **architecture validity**, not per-value behaviour: legacy Statements Architecture valid options
  are `invoice` / `automatic`; current Relationship Invoicing valid options are `remittance` /
  `automatic` / `prepaid`. No member of the map or source documents a default when
  `payment_collection_method` is omitted, or which value enrolls without a payment profile — both
  are **UNVERIFIED**; the live 422 shows only that the omitted path behaves as charge-requiring
  for this product. Source-documented alternatives that defer payment at creation (semantics
  verbatim from `Models/CreateSubscription.cs`): `NextBillingAt` in the future ⇒ "no payment will
  be captured at all" at creation; `InitialBillingAt` (future) or `DeferSignup = true` ⇒
  subscription created in **Awaiting Signup** state with no immediate billing, activatable later
  via `client.Subscriptions.ActivateSubscription` (map row: operations/Subscriptions.md — RI
  architecture only). None of these is documented as clearing the payment-method-on-file check —
  acceptance is live-only. Probe order: `PaymentCollectionMethod = CollectionMethod.Remittance`
  first, then deferred signup as fallback.
- **CancelSubscription** (row 9): "To cancel the subscription immediately, omit any schedule
  parameters from the request" — send a `CancellationRequest` with only
  `CancellationMessage`/`ReasonCode` set, or no options at all.

### 2.2 Request models

All in namespace `MaxioAdvancedBilling.Models`. Format: `CSharpName (wire_name): Type` —
`!req` = `required` (must be set in the object initializer); trailing `?` = nullable/optional.
Records are immutable with `init`-only setters.

| Model | Fields (the integration's subset; full record is larger) | Source |
|---|---|---|
| `CreateSubscriptionRequest` | `Subscription (subscription): CreateSubscription !req` | records-2-Cr-Ne.md |
| `CreateSubscription` (enrollment uses) | `ProductHandle (product_handle): string?` · `ProductId (product_id): int?` · `CustomerId (customer_id): int?` · `CustomerReference (customer_reference): string?` · `CustomerAttributes (customer_attributes): CustomerAttributes?` · `Reference (reference): string?` · `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` — **live-relevant (see §2.1 LIVE FINDING):** set `CollectionMethod.Remittance` (or `.Automatic` / `.Prepaid` / `.Invoice`) to select the collection method explicitly; omitted ⇒ sandbox 422 "No payment method was on file" · `CouponCode (coupon_code): string?` · `NextBillingAt (next_billing_at): DateTimeOffset?` — future value is source-documented as capturing no payment at creation · `InitialBillingAt (initial_billing_at): DateTimeOffset?` / `DeferSignup (defer_signup): bool? = false` — create in **Awaiting Signup** (no payment at creation), activate later via `client.Subscriptions.ActivateSubscription` · `NetTerms (net_terms): string?` — source doc: due-days after renewal on invoice billing, 0–180, default null — **set nothing else** for this feature (no `CreditCardAttributes`/`PaymentProfileId`/`Components`); the record also carries many more optional fields the integration leaves out | records-2-Cr-Ne.md; member semantics from `Models/CreateSubscription.cs` (SDK source) |
| `CustomerAttributes` (inline customer when creating the subscription directly) | `FirstName (first_name): string?` · `LastName (last_name): string?` · `Email (email): string?` · `Reference (reference): string?` · plus address/organization/tax fields — all optional | records-2-Cr-Ne.md |
| `CreateCustomerRequest` | `Customer (customer): CreateCustomer !req` | records-1-Ac-Cr.md |
| `CreateCustomer` | `FirstName (first_name): string !req` · `LastName (last_name): string !req` · `Email (email): string !req` · `Reference (reference): string?` · `Organization (organization): string?` · address/city/state/zip/country/phone/locale — all optional | records-1-Ac-Cr.md |
| `CancellationRequest` | `Subscription (subscription): CancellationOptions !req` | records-1-Ac-Cr.md |
| `CancellationOptions` | `CancellationMessage (cancellation_message): string?` · `ReasonCode (reason_code): string?` · `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?` · `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?` · `RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool?` | records-1-Ac-Cr.md |

⚠ **No `!req` member of `CreateSubscription` selects itself.** A request model may mark
nothing required, and then `required?` selects nothing for you — the compiler catches a
missing `Subscription`, nothing else. The fields this feature's contract depends on:
`ProductHandle` (or `ProductId`), one customer pointer (`CustomerId`, `CustomerReference`, or
`CustomerAttributes`), and `Reference` (the idempotency key — see §2.4). Deliberately left
out (per this feature's scope, not per the compiler): `PaymentProfileId`,
`CreditCardAttributes`, `PaymentProfileAttributes`, `BankAccountAttributes`,
`Components`, `CustomPrice`, `CalendarBilling`, `Metafields`, `Group`, coupon fields,
`Currency`, `ExpiresAt`. `NetTerms` stays omitted unless the accepted collection method turns
out to be invoiced billing that needs due-days tuning. No compiler catches a field you drop.

### 2.3 Response envelopes

Responses **wrap** their payload in exactly one field — reads go one level down:

| Envelope | Wrapper field | Inner model fields the integration reads | Source |
|---|---|---|---|
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` — **nullable; null-check before use** | `Id (id): int?` · `State (state): SubscriptionState?` · `ProductPriceInCents (product_price_in_cents): long?` — **price is in cents** · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — **next billing date** · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `Reference (reference): string?` · `Currency (currency): string?` · `CreatedAt (created_at): DateTimeOffset?` · `ActivatedAt (activated_at): DateTimeOffset?` · `CanceledAt (canceled_at): DateTimeOffset?` · `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?` · `Customer (customer): Customer?` · `Product (product): Product?` — nested product carries `Handle`, `Name`, `PriceInCents (long?)`, `Interval (int?)`, `IntervalUnit (IntervalUnit?)` | records-4-Su-We.md (`SubscriptionResponse`), records-3-Of-Su.md (`Subscription`, `Product`) |
| `CustomerResponse` | `Customer (customer): Customer !req` | `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName (first_name): string?` · `LastName (last_name): string?` · `CreatedAt (created_at): DateTimeOffset?` | records-2-Cr-Ne.md (`CustomerResponse`, `Customer`) |
| `ProductResponse` | `Product (product): Product !req` | `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `PriceInCents (price_in_cents): long?` — **cents** · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `ProductFamily (product_family): ProductFamily?` (nested `ProductFamily.Handle` is the family filter key) · `ArchivedAt (archived_at): DateTimeOffset?` · `RequestCreditCard (request_credit_card): bool?` · `RequireCreditCard (require_credit_card): bool?` | records-3-Of-Su.md |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` | `Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` | records-3-Of-Su.md |

Interval on `Product`/`ProductPricePoint` is `Interval: int?` (a count) **plus**
`IntervalUnit: IntervalUnit?` (`day`/`month`) — a plan is "$299.00 every 1 month", not "$299
monthly" as one value.

### 2.4 Error payloads & the idempotency mechanism

Typed-error payload models (the `out` types), all in `MaxioAdvancedBilling.Models`:

| Payload | Fields | Source |
|---|---|---|
| `ErrorListResponse1` (422 of CreateSubscription / others) | `Errors (errors): IReadOnlyList<string> !req` | records-1-Ac-Cr.md |
| `CustomerErrorResponse1` (422 of CreateCustomer) | `Errors (errors): Errors?` — where `Errors` is a shared record carrying only `PerPage (per_page)` and `PricePoint (price_point)` lists | records-1-Ac-Cr.md, records-2-Cr-Ne.md (`Errors`) |
| `CancelSubscriptionErrorResponse` (422 of CancelSubscription) | **union** (`ErrorListResponse1` \| `SingleErrorResponse1`); read via `TryGetErrorListResponse1(out …)` / `TryGetSingleErrorResponse1(out …)` — never `new`, never a cast | models/unions.md |
| `RawError` (Case B everywhere) | `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` | sdk-map.md error-core |

⚠ **Suspicion visible in the map:** `CustomerErrorResponse1`'s `Errors` field is typed as the
shared `Errors` record, whose only members are `per_page` and `price_point` — a real 422
customer payload (`{"errors": {…}}`) cannot carry its messages in that shape, so treat the
typed 422 extraction as best-effort and fall back to `TryGetRawError(out var raw).ReadAsString()`.
The 422 payload of `CreateSubscription` (`ErrorListResponse1`) is a plain string list and is
trustworthy. What the live wire actually sends is **UNVERIFIED**; extract best-effort, fall
back to the generic message.

**Idempotency facts (grounded):** `CreateSubscription.Reference (reference): string?` is an
application-defined key stored on the Maxio subscription, and `FindSubscription(reference)`
returns the subscription carrying that reference (404 = none). `ListCustomerSubscriptions`
returns every subscription of a customer, each with `Product` (→ `Handle`) — so
duplicate detection can be: reference lookup first, then a product-handle scan of the
customer's subscriptions. **Whether this satisfies the double-click concurrency requirement is
the application's design decision — YOUR CALL — not in the map**; the SDK offers no
transactional create-if-absent.

### 2.5 Enums needed (`MaxioAdvancedBilling.Models.Enums`)

Enums are **not** C# enums — they are `StringEnum<T>` wrappers: build via the static member
(e.g. `SubscriptionState.Active`) or `SubscriptionState.FromValue("active")`; the map lists
member name (wire value). Full lists on models/enums.md.

| Enum | Members — `C#Member (wire value)` | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)` · `FailedToCreate (failed_to_create)` · `Trialing (trialing)` · `Assessing (assessing)` — internal/transient; the map's own note says do not base access decisions on it · `Active (active)` · `SoftFailure (soft_failure)` · `PastDue (past_due)` · `Suspended (suspended)` · `Canceled (canceled)` · `Expired (expired)` · `Paused (paused)` · `Unpaid (unpaid)` · `TrialEnded (trial_ended)` · `OnHold (on_hold)` · `AwaitingSignup (awaiting_signup)` | models/enums.md |
| `IntervalUnit` | `Day (day)` · `Month (month)` | models/enums.md |
| `CollectionMethod` | `Automatic (automatic)` · `Remittance (remittance)` · `Prepaid (prepaid)` · `Invoice (invoice)` | models/enums.md |
| `CancellationMethod` | `MerchantUi (merchant_ui)` · `MerchantApi (merchant_api)` · `Dunning (dunning)` · `BillingPortal (billing_portal)` · `Unknown (unknown)` · `Imported (imported)` | models/enums.md |

### 2.6 Client construction, auth, config binding

Package & identity: `AsadAli.AdvancedBilling.Sdk` **v1.0.2** (map stamp: source commit
`15db14b`, tag `v1.0.2`), root namespace `MaxioAdvancedBilling`, target framework
`netstandard2.0`. Runtime deps (`Polly`, `Microsoft.Extensions.Http`, …) arrive transitively.

Config binding keys (as dictated by the brief): `Maxio:ApiKey`, `Maxio:Subdomain`,
`Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`.

| Fact | Value | Source |
|---|---|---|
| Options class | `MaxioAdvancedBillingClientOptions` (root ns): `Environment: ServerEnvironment` (default `ServerEnvironment.Default()`), `Retry: RetryOptions` (default `RetryOptions.Default()`), `Server: ServerOptions` (default `new()`), `BasicAuth: BasicAuthCredentials?` | MaxioAdvancedBillingClientOptions.cs (SDK source) |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` — **Username = API key, Password = the literal `"x"`** | sdk-map.md Servers & auth |
| Environments | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu` — **there is no sandbox member**; "sandbox" is just the site the subdomain points to | sdk-map.md |
| Subdomain | `options.Server.Production.Us.Site = <Maxio:Subdomain>` — `{site}` in the base-URL template `https://{site}.chargify.com` defaults to subdomain | sdk-map.md Servers |
| BaseUrl override | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` — used verbatim as the API base address, replacing the derived host | sdk-map.md Servers |
| Client ctor | `new MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — the **only** constructor; API groups are properties (`client.Customers`, …) | sdk-map.md Getting a client |
| DI extension | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` exists in `MaxioAdvancedBilling` — but it is declared as a C# 14 **`extension` member** (SDK source `ServiceCollectionExtensions.cs`, compiled `LangVersion 14`); a .NET 8 project compiling C# 12 may fail to bind it. **Defensive directive:** mirror the source's own body instead — `services.AddHttpClient();` then `services.AddSingleton(sp => new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), options));` — the same shape the SDK source registers (singleton client over a factory-created `HttpClient`) | ServiceCollectionExtensions.cs (SDK source) |

`RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`) members are all `required` when
built by hand — start from `RetryOptions.Default()` and mutate, never `new RetryOptions { … }`
from scratch.

---

## 3. Trap notes (hazard + consequence; the named skill carries the resolution)

> ⚠ Step 1 (client registration) — the SDK ships a DI extension but it is a C# 14 `extension`
> member; whether a .NET 8 (C# 12) consumer can even bind it decides registration before the
> first line runs, and the wrong `HttpClient` lifetime (new client per request vs. factory-
> backed singleton) leaks handlers. **MUST load `dotnet-client-initialization`** before wiring
> the client.

> ⚠ Step 1 (credentials) — the auth scheme's shape is trivial to get half-right: which option
> property carries credentials, when they must be set relative to client construction, and how
> to rotate the key without redeploying are all contract the signature does not show; a 401 at
> runtime is the symptom of getting it wrong. **MUST load `dotnet-authentication`** before
> setting `BasicAuth`.

> ⚠ Step 2 & 5 (list calls) — the list operations take 8–14 nullable parameters with **no C#
> defaults**; a positional call mis-binds silently into the wrong query param, and pagination
> is manual (`page`/`perPage` with server-side defaults) so a single page is not the full
> catalog. **MUST load `dotnet-calling-endpoints`** before the first `client.X.Y(…)` call.

> ⚠ Steps 2–5 (models) — enum "values" are `StringEnum<T>` wrappers (never compared as C#
> enums), response reads go one envelope level down (`resp.Subscription`, not `resp`), and
> unmodeled JSON fields are silently dropped on deserialize — a plan DTO built off the wrong
> level or a dropped field fails at runtime, not compile time. **MUST load `dotnet-models`**
> before constructing requests or mapping responses.

> ⚠ Step 4 (enroll) — whether a failed non-idempotent POST create can be re-sent is governed
> by the SDK's retry rules and is invisible in the signature; the consequence of getting it
> wrong is a duplicate Maxio subscription (a billing record), and no option set after
> construction can undo one. Whether the retry/timeout options bound the whole call or one
> attempt also decides how long a shopper waits on enroll. **MUST load
> `dotnet-configuration-resilience`** before wiring the client and before relying on enroll
> latency.

> ⚠ Step 6 (error boundary) — every operation is throw-only, and the two error cases differ
> per operation (§2.1): a catch ladder written for one shape silently misclassifies the other,
> turning a deterministic 422 into a reported outage. **MUST load `dotnet-error-handling`**
> before writing any `try/catch` around an SDK call. The two `JsonException` hazards,
> verbatim:
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
>   `JsonException` from deserialization, **not** as an `SdkException` — so an
>   SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
>   throws `JsonException` *while the error object is being constructed*, so the
>   `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it —
>   a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection
>   as an outage, and a caller that retries 5xx retries something that can never succeed.

> ⚠ Testing (if the integration layer gets tests) — the SDK's test seam is not obvious from
> the signatures, and stubbing the wrong layer produces tests that assert execution, not
> behaviour. **MUST load `dotnet-testing`** before writing tests for these endpoints.

---

## 4. REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry these skills'
contents (their defaults and semantics are theirs to state):

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client/options construction, `HttpClient` ownership & lifetime, DI registration (incl. the C# 14 extension-member trap). |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` supply, per-environment config, key rotation; 401/403 diagnosis. |
| `dotnet-calling-endpoints` | Steps 2–5 — named-argument discipline for the many-param list ops, envelope reads, async/cancellation (`ct:`). |
| `dotnet-models` | Steps 2–5 — `StringEnum<T>` enums, required members, wire names, union `TryGet…` accessors, dropped unmodeled fields. |
| `dotnet-error-handling` | Step 6 — the Case A/B catch ladder, `TryGetRawError` vs typed accessors, and both `JsonException` hazards. |
| `dotnet-configuration-resilience` | Steps 1 & 4 — retry/timeout semantics that decide whether enroll can double-execute and how long it blocks. |
| `dotnet-testing` | Tests — the SDK test seam (`HttpClient` argument), covering error/edge paths without SDK internals. |

---

## 5. Assumptions & Blockers

**Assumptions (things I had to assume about intent):**

1. The sandbox site is **US-hosted** ⇒ `ServerEnvironment.Us`. If the account requested EU
   hosting, switch to `ServerEnvironment.Eu` (`https://{site}.ebilling.maxio.com`).
2. `Maxio:ProductFamilyHandle` (`eshop-subscribe`) is used as the **client-side filter key**
   against `Product.ProductFamily.Handle` on the plans-list path, and as the family named in
   validation messages; the plan catalog (`eshop-pro` $299.00/mo, `basic-plan` $29.00/mo) is
   assumed already seeded as stated in the brief.
3. The external reference keying the Maxio customer (and the subscription `Reference`)
   derivation from the eShopOnWeb user identity is an application decision — **YOUR CALL — not
   in the map**; the SDK contract only requires the value to be unique per customer (map,
   operations/Customers.md: "you may only create one customer for a given reference value").
4. JWT authentication on the PublicApi endpoints is the application's existing infrastructure —
   **YOUR CALL — not in the map** (the map knows nothing about eShopOnWeb).
5. The `api-call` metered component is out of scope for the three endpoints in this brief; it
   would need `client.Components`/`client.SubscriptionComponents` operations not surveyed here.

**Blockers:**

- None. The remaining **UNVERIFIED** items (only live traffic can confirm them), each carried
  with a directive above: (a) ~~whether `CreateSubscription` with no payment-profile fields is
  accepted~~ — **settled live: it is not** (422 "No payment method was on file for the $299.00
  balance", §2.1 LIVE FINDING). Still UNVERIFIED: which remediation shape the sandbox accepts —
  `PaymentCollectionMethod = CollectionMethod.Remittance` (probe first), or a deferred-signup
  shape (`DeferSignup = true` / future `InitialBillingAt` → Awaiting Signup, then
  `client.Subscriptions.ActivateSubscription`) as fallback; (b) whether
  `ListProductsForProductFamily` accepts a `handle:…` value in its
  `productFamilyId` string parameter — the map documents the `handle:my-family` format **only**
  for `ReadProductFamily` (operations/ProductFamilies.md), so the plan's primary listing path
  is the grounded `ListProducts` + client-side family filter (§2.1 row 6), with row 7 as an
  alternative to probe.