# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Planning artifact only. Produced by the maxio-sdk agent; every contract fact below is grounded in the
bundled SDK map (source pages cited per row). No clone paths, no reproduced map pages.

## 1. Scope & sequence

| # | Step | SDK operations used | Notes |
|---|------|---------------------|-------|
| 1 | Bind config (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional) and DI-register the SDK client in PublicApi | none (client construction) | See Client construction below |
| 2 | `GET /api/subscription-plans` — list available plans | `ProductFamilies.ListProductFamilies` → `ProductFamilies.ListProductsForProductFamily` (paginate) | Resolve family **by handle**, then list its products |
| 3 | Customer idempotency (shared by subscribe) | `Customers.ReadCustomerByReference`, on 404 `Customers.CreateCustomer` | `reference` = eShopOnWeb user id (Assumption §5) |
| 4 | `POST /api/subscriptions` — subscribe caller to a plan | `Products.ReadProductByHandle` (if product id needed), `Subscriptions.FindSubscription`, on 404 `Subscriptions.CreateSubscription` | Idempotent via `FindSubscription` + `CreateSubscription.Reference` |
| 5 | `GET /api/my-subscriptions` — caller's subscriptions | `Customers.ListCustomerSubscriptions` | **Not** `Subscriptions.ListSubscriptions` — the site-wide list has **no** customer filter parameter |
| 6 | Error boundary around every SDK call | catch ladder per Error column below | See Trap notes + REQUIRED READING |

All endpoints are JWT-authenticated minimal APIs in PublicApi; identity from token is an application
decision (`YOUR CALL — not in the map`). The metered component on the family is **out of scope** for the
hero flow; the SDK surface for it exists (`client.Components`) but is not planned here.

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

### 2.1 Package identity

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` — add with `dotnet add package AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root namespace (the `using`) | `MaxioAdvancedBilling` (differs from package id) | `sdk-map.md` |
| Target framework | `netstandard2.0` → consumable from the net8.0 PublicApi project | `sdk-map.md` |
| Transitive deps | `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents` | `sdk-map.md` |
| Install rule | NuGet package only — never a project reference to the SDK source | `sdk-map.md` |

### 2.2 Client construction, auth, servers (exact C#)

| Fact | Value | Source |
|---|---|---|
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` with properties `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md` |
| Client class / only ctor | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Auth scheme | HTTP Basic — `new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (password is the literal `"x"`); assign to `options.BasicAuth` | `sdk-map.md` |
| Subdomain binding | `options.Server.Production.Us.Site = <Maxio:Subdomain>` — `{site}` defaults to subdomain; default env `ServerEnvironment.Us` → `https://{site}.chargify.com` | `sdk-map.md` |
| **BaseUrl override** | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` — when this config key is present (non-empty), set it **verbatim** as the base address **instead of** deriving from `Site`/subdomain | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Server.Production.Us.Site/BaseUrl = …; })` | `sdk-map.md` |
| API groups | Properties on the client: `client.Subscriptions`, `client.Customers`, `client.Products`, `client.ProductFamilies`, … | `sdk-map.md` |

### 2.3 Operations

Signatures verbatim from the map. `body` and the other nullable-without-default parameters must be
passed explicitly (pass `null` to skip). Response envelopes **wrap the payload in one field** — reads
go one level down (`resp.Product`, `resp.Customer`, `resp.Subscription`).

| Purpose | Call (controller property `client.X`) | Request model + fields | Response envelope + inner fields the integration reads | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| Find product family by handle | `ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable, pass `null` | — | `IReadOnlyList<ProductFamilyResponse>`; envelope `ProductFamilyResponse.ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` — read `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`. Match `Handle == Maxio:ProductFamilyHandle` client-side (no handle query param exists) | Case B `SdkException<MaxioAdvancedBilling.Core.Exceptions.RawError>`: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()` | none (returns all families) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| List plans in the family | `ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params without defaults must be passed; `productFamilyId` is a **string** (the numeric family id as string) | — | `IReadOnlyList<ProductResponse>`; envelope `ProductResponse.Product (product): MaxioAdvancedBilling.Models.Product !req` — see Product fields table below | Case A `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `.Error.TryGetString(out string)` [404], `.Error.TryGetRawError(out RawError)` | manual `page` + `perPage` (defaults 1 / 20) | `operations/ProductFamilies.md` |
| Read one product by handle | `Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` (envelope as above) | Case B `SdkException<RawError>` (404 ⇒ plan handle unknown) | none | `operations/Products.md` |
| Site-wide product list (alternative to family listing) | `Products.ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params must be passed | — | `IReadOnlyList<ProductResponse>` — filter by `Product.ProductFamily.Handle` client-side | Case B `SdkException<RawError>` | manual `page` + `perPage` | `operations/Products.md` |
| Customer lookup (idempotency check) | `Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` | — | `CustomerResponse`; envelope `CustomerResponse.Customer (customer): MaxioAdvancedBilling.Models.Customer !req` — read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` | Case B `SdkException<RawError>` — **404 `StatusCode` = no customer yet ⇒ create** | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| Create customer | `Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed | `CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req`; `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` ← **yes, a `reference` field exists — put the eShopOnWeb user id here** (provider note: only one customer per `reference` value; a duplicate ⇒ 422), plus optional `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | `CustomerResponse` (as above) | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `.Error.TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], `.Error.TryGetRawError(out RawError)`. ⚠ `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.Errors?` models only `PerPage (per_page)` / `PricePoint (price_point)` arrays — **extract best-effort, fall back to `TryGetRawError(out raw)` + `raw.ReadAsString()` for messages**; whether a live 422 body matches that generated `Errors` shape is `UNVERIFIED` | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| Search customers (optional secondary path) | `Customers.ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 nullable params must be passed; `q` searches email / id / organization / reference / name | — | `IReadOnlyList<CustomerResponse>` | Case B `SdkException<RawError>` | manual `page` + `perPage` | `operations/Customers.md` |
| Subscription idempotency check | `Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed (may be `null`); query param `reference`; `GET /subscriptions/lookup.json` | — | `SubscriptionResponse` (envelope below) | Case A `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `.Error.TryGetNoContent(out RawError)` [404 ⇒ none exists], `.Error.TryGetRawError(out RawError)` | none | `operations/Subscriptions.md` |
| Create subscription (no payment method) | `Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed | `CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req`; fields the integration sets: `ProductId (product_id): int?` **or** `ProductHandle (product_handle): string?` (prefer handle — plan ids are not stable across re-seeds), `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`, `Reference (reference): string?` ← idempotency key. **No "skip payment profile" flag exists**: payment info is required by the provider only when the product demands it — our plans have no trial/no setup fee and no card required, so **omit** `PaymentProfileId (payment_profile_id)`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` entirely and leave `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` unset. Other fields available but unused here: `ProductPricePointHandle`, `ProductPricePointId`, `CustomPrice`, `CouponCode(s)`, `NextBillingAt`, `InitialBillingAt`, `DeferSignup`, `CustomerAttributes`, `Components`, `CalendarBilling`, `Metafields`, … (full list on the records page) | `SubscriptionResponse`; envelope `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` — see Subscription fields below | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `.Error.TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] — `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (readable validation messages), `.Error.TryGetRawError(out RawError)` | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| List the caller's subscriptions | `Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `GET /customers/{customer_id}/subscriptions.json` | — | `IReadOnlyList<SubscriptionResponse>` (envelope below) | Case B `SdkException<RawError>` | **none** — returns all of that customer's subscriptions; `Subscriptions.ListSubscriptions` exists but has **no** customer filter (its params are `state`, `product`, `productPricePointId`, `coupon`, `couponCode`, date fields, `metadata`, `direction`, `sort`, `include`) | `operations/Customers.md`, `operations/Subscriptions.md` |
| Read one subscription (confirm after create) | `Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed (pass `null`) | — | `SubscriptionResponse` | Case B `SdkException<RawError>` | none | `operations/Subscriptions.md` |

### 2.4 Product fields (`MaxioAdvancedBilling.Models.Product`, source `records-3-Of-Su.md`)

| Display need | C# property (wire name) | Type |
|---|---|---|
| Plan id | `Id (id)` | `int?` |
| Stable plan identifier | `Handle (handle)` | `string?` |
| Plan name | `Name (name)` | `string?` |
| Description | `Description (description)` | `string?` |
| Price | `PriceInCents (price_in_cents)` | `long?` — **cents**; the product's own price is the default price point's price. Decimal display = `PriceInCents / 100m` (`YOUR CALL — not in the map`). A full `ProductPricePoint` record is **not** embedded; only `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointName (product_price_point_name): string?` are carried |
| Billing frequency | `Interval (interval): int?` + `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?` (`Day` / `Month`) | per map |
| Trial info | `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?` — our plans have no trial (per brief); `null`/0 ⇒ no trial (`UNVERIFIED` which shape sandbox returns) | per map |
| Payment requirement | `RequestCreditCard (request_credit_card): bool?`, `RequireCreditCard (require_credit_card): bool?` — `false` ⇒ cardless signup supported | per map |
| Family back-ref | `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` (nested `Handle` for filtering) | per map |

### 2.5 Subscription fields (`MaxioAdvancedBilling.Models.Subscription`, source `records-3-Of-Su.md`)

| Display need | C# property (wire name) | Type |
|---|---|---|
| Subscription id | `Id (id)` | `int?` |
| State | `State (state)` | `MaxioAdvancedBilling.Models.Enums.SubscriptionState?` (§2.6) |
| **Next billing date** | `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` — map also carries `NextAssessmentAt (next_assessment_at): DateTimeOffset?`; which of the two to display is `YOUR CALL — not in the map` |
| Current period start | `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` |
| Price | `ProductPriceInCents (product_price_in_cents)` | `long?` — cents; same /100m mapping |
| Product id / plan info | **No scalar `product_id` exists on `Subscription`** — read the embedded `Product (product): MaxioAdvancedBilling.Models.Product?` → `Product.Id`, `Product.Handle`, `Product.Name`, `Product.PriceInCents`, `Product.Interval`, `Product.IntervalUnit` | per map |
| Customer | embedded `Customer (customer): MaxioAdvancedBilling.Models.Customer?` (no scalar `customer_id` field) | per map |
| Misc | `BalanceInCents (balance_in_cents): long?`, `Currency (currency): string?`, `ActivatedAt`, `TrialStartedAt`, `TrialEndedAt`, `CanceledAt` | per map |

### 2.6 Enums (source `map/models/enums.md` — all are `StringEnum<T>`, **not** C# enums; build with `Type.FromValue("wire")` or the static members, namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | Members (C# name → wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` — hero flow expects `Active`; whether sandbox cardless signups return `active` or `awaiting_signup` at creation is `UNVERIFIED` — read `State` from the response and render it, do not hardcode. If `AwaitingSignup` appears, `Subscriptions.ActivateSubscription` exists in the map |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards (expired_cards)`, `OnHold`, `PastDue`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` (filter values for `Subscriptions.ListSubscriptions` only) |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |

### 2.7 Pagination pattern (map-sourced)

List operations take `int? page` and `int? perPage` (defaults per row: products 1/20, customers 1/50) and
return a plain `IReadOnlyList<…>` with **no page metadata** — paging is manual: increment `page` until a
page returns fewer than `perPage` items. `ListCustomerSubscriptions` and `ListProductFamilies` have **no**
pagination parameters at all.

### 2.8 Error boundary contract

- Every operation is **throw-only** — the SDK generates **no** `…Result`/`ApiResult` no-throw variants. Always wrap the throwing call.
- Catch type: `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` — a single `required` property `TError Error { get; init; }` (namespace confirmed from the SDK source file the map names, `Core/Exceptions/SdkException.cs`; the map's namespace table was silent on this one type).
- **Case A** (typed): `TError` = `MaxioAdvancedBilling.Errors.{Operation}Error` — status-specific `TryGet…(out …)` accessors per the rows above plus inherited `.TryGetRawError(out RawError)`.
- **Case B** (raw): `TError` = `MaxioAdvancedBilling.Core.ErrorResponse.RawError` (namespace per map's error-core section; source `Core/ErrorResponse/RawError.cs`) — `.StatusCode: HttpStatusCode`, `.ReadAsString(): string`, `.ReadAsJson<T>(): T?`, `.ReadAsBytes(): ReadOnlyMemory<byte>`.

> ⚠ `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite
> handling:
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
>   `JsonException` from deserialization, **not** as an `SdkException` — so an
>   SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
>   throws `JsonException` *while the error object is being constructed*, so the `JsonException`
>   **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
>   maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
>   and a caller that retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.

---

## 3. Trap notes (per step)

- ⚠ Step 1 (DI registration) — the `HttpClient`/handler pipeline must be long-lived and reused via
  `IHttpClientFactory`; how the SDK wrapper's lifetime pairs with it is exactly the trap the signature
  hides. **MUST load `dotnet-client-initialization`** before wiring the client into the container.
- ⚠ Step 1 (credentials) — set `BasicAuth` before/at client construction and load the key from
  configuration, never hardcoded; the Basic scheme's username/password convention is easy to invert.
  **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–5 (calls) — every list operation above has 5–8 nullable parameters with **no C# default**;
  a positional call mis-binds them. Call with named arguments and pass `null` explicitly. **MUST load
  `dotnet-calling-endpoints`** before the first `client.{Group}.{Operation}(...)` call.
- ⚠ Steps 2–5 (models) — enums are `StringEnum<T>` (comparison/`FromValue` semantics are non-obvious),
  records are immutable with `init`-only setters, `required` members must be set in the initializer, and
  wire names (`snake_case`) differ from C# property names. **MUST load `dotnet-models`** before building
  `CreateCustomerRequest` / `CreateSubscriptionRequest`.
- ⚠ Steps 2–5 (errors) — the Case A/B split is per-operation (see the Error column), `TryGetRawError` is
  **not** a catch-all on typed errors, and the two `JsonException` directions (§2.8) shape the whole
  boundary. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Steps 1 & 4 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are
  **not** the `HttpClient` timeout; retry semantics can re-execute a `POST` whose transport failed, which
  is why Steps 3–4 carry their own idempotency keys (`reference`) rather than relying on transport
  behavior. Whether a failed create can be re-sent safely is decided by those keys. Also governs the
  manual `page`/`perPage` loop (§2.7). **MUST load `dotnet-configuration-resilience`** before wiring the
  client or paginating.
- ⚠ Tests — the `HttpClient` constructor argument is the test seam; the SDK's controller properties are
  not designed for mocking. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING (load **before** implementation starts — the sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, `HttpClient` ownership/lifetime, options shape |
| `dotnet-authentication` | Step 1 — Basic credentials (`Username` = API key, `Password = "x"`), config loading, rotation |
| `dotnet-calling-endpoints` | Steps 2–5 — controller access, named arguments, envelope unwrapping, cancellation |
| `dotnet-models` | Steps 2–5 — request models, `StringEnum<T>` semantics, required members, wire names |
| `dotnet-error-handling` | Step 6 — Case A/B catch ladder, `TryGet…` accessors, both `JsonException` directions |
| `dotnet-configuration-resilience` | Step 1 & 4 — retries/timeouts semantics, `BaseUrl` override wiring, pagination loop |
| `dotnet-testing` | Test phase — the `HttpClient` seam, covering error paths |

## 5. Assumptions & Blockers

**Assumptions**
1. Idempotency keys (`YOUR CALL — not in the map`): customer `reference` = the eShopOnWeb user id;
   subscription `reference` = a deterministic per-user-per-plan key (e.g. `{userId}:{planHandle}`). Both
   `reference` fields exist in the map (`CreateCustomer`, `CreateSubscription`, `FindSubscription`).
2. Uniqueness semantics of a *subscription* `reference` are not stated in the map — the map only says
   `FindSubscription` "finds a subscription by its reference". Defensive directive (UNVERIFIED): on a 422
   from `CreateSubscription`, re-check `FindSubscription` and return the existing subscription instead of
   surfacing the error; the race window between two concurrent double-click creates is otherwise
   unresolvable from the map alone.
3. Plan price → display string (cents → decimal, interval → "per month") is an application decision
   (`YOUR CALL — not in the map`); the map supplies only `PriceInCents` (long, cents) and
   `Interval`/`IntervalUnit`.
4. Customer names/email are sourced from the JWT claims of the caller (`YOUR CALL — not in the map`);
   `CreateCustomer` requires `first_name`, `last_name`, `email` as non-null strings.
5. `Maxio:BaseUrl`, when configured, is applied verbatim to
   `options.Server.Production.Us.BaseUrl` and suppresses the subdomain-derived `Site` (per `sdk-map.md`
   override points); when absent, `Site = Maxio:Subdomain` under the default `ServerEnvironment.Us`.
6. Family resolution is by listing all families and matching `Handle` client-side: the map's
   `ReadProductFamily` Notes mention a `handle:my-family` id format, but the generated C# signature takes
   `int id`, so that format cannot be passed through this SDK. Whether the same format works in
   `ListProductsForProductFamily`'s string `productFamilyId` path segment is `UNVERIFIED` — the plan does
   not rely on it.
7. Which cardless-signup state sandbox returns at creation (`active` vs `awaiting_signup`) is
   `UNVERIFIED`; the endpoint renders the returned `State` rather than assuming one.

**Blockers**

None — every operation, model, enum, and error shape needed for the hero flow is present in the map.
