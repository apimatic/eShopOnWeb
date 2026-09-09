# Maxio Advanced Billing .NET SDK — eShopOnWeb subscription integration plan

Target: `AsadAli.AdvancedBilling.Sdk` (NuGet) · root namespace `MaxioAdvancedBilling` · `netstandard2.0` · map generated from source tag `v1.0.2` (commit `15db14b`). Every signature, wire name, enum value and error accessor below comes from the bundled SDK map (page cited per row).

---

## 1. Scope & sequence

| # | Step | Operations used (controller) |
|---|---|---|
| 1 | Register the SDK client in DI from `Maxio:` configuration (API key, subdomain, optional verbatim BaseUrl) | — (client construction) |
| 2 | Resolve `Maxio:ProductFamilyHandle` → product-family id (lazily; cache the resolved id) | `ListProductFamilies` (ProductFamilies) |
| 3 | `GET /api/subscription-plans` — list the family's products, map to a plan DTO | `ListProductsForProductFamily` (ProductFamilies) |
| 4 | Ensure a Maxio customer exists for the eShopOnWeb user (idempotent, keyed on customer `reference`) | `ReadCustomerByReference`, `CreateCustomer` (Customers) |
| 5 | `POST /api/subscriptions` — enroll, idempotent via subscription `reference`; return plan/price/state/next-billing confirmation | `FindSubscription` (Subscriptions), `CreateSubscription` (Subscriptions) |
| 6 | `GET /api/my-subscriptions` — list the customer's subscriptions with state/price/next-billing | `ReadCustomerByReference` (Customers), `ListCustomerSubscriptions` (Customers) |
| 7 | Error boundary around every call (Case A/B ladder + JSON-malformed-body handling) | — (see §3, §4) |
| 8 | Informational, not implemented now: metered usage for component `api-call` | `FindComponent` (Components), `CreateUsage` (SubscriptionComponents) |

All three endpoints live on `src/PublicApi`, JWT-authenticated, caller identity from the token, following that project's existing endpoint conventions. The SDK is called with Basic auth (site API key); the JWT never reaches Maxio.

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

### 2.1 Client construction, auth, base URL, DI

| Fact | Contract | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — the **only** constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`; an `HttpClient` instance must be supplied | `sdk-map.md` (source `MaxioAdvancedBillingClient.cs`) |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` with properties `Environment` (`ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md` |
| Auth | HTTP **Basic**: `BasicAuthCredentials { Username = <API key>, Password = "x" }` — Username is the **site** API key (credential from config key `Maxio:ApiKey`), Password is the literal string `"x"` | `sdk-map.md` (source `Core/Authentication/Basic/BasicAuthCredentials.cs`) |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) → `https://{site}.chargify.com`. `{site}` is set via `options.Server.Production.Us.Site = "<subdomain>"` (from config key `Maxio:Subdomain`) | `sdk-map.md` (sources `Servers/ServerEnvironment.cs`, `Servers/ProductionOptions.cs`) |
| Verbatim BaseUrl override | When config key `Maxio:BaseUrl` has a value, set `options.Server.Production.Us.BaseUrl = "<value verbatim>"` instead of relying on environment + `Site`; do not append or normalize anything | `sdk-map.md` (sources `Server.cs`, `ServerOptions.cs`) |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — **every member is `required`**; either set all members or start from `RetryOptions.Default()` and mutate. Configure via `options.Retry` | `sdk-map.md` (source `Core/Configuration/RetryOptions.cs`) |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; … });` (extension in the SDK root namespace, source `ServiceCollectionExtensions.cs`); then resolve `MaxioAdvancedBillingClient` from the container | `sdk-map.md` |
| API groups | Every controller is a property on the client: `client.Customers`, `client.Subscriptions`, `client.ProductFamilies`, `client.Components`, `client.SubscriptionComponents` … (controllers live in `MaxioAdvancedBilling.Api`) | `sdk-map.md` |

Controller properties used below: `client.Customers`, `client.Subscriptions`, `client.ProductFamilies`, `client.Components`, `client.SubscriptionComponents`.

### 2.2 Operations

**Ensure customer (Step 4)** — `operations/Customers.md`

| Operation | Signature (verbatim) | Returns / envelope | Error case | Source |
|---|---|---|---|---|
| Lookup by reference | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `CustomerResponse` → inner `Customer.Customer (customer): MaxioAdvancedBilling.Models.Customer !req`. Relevant `Customer` fields: `Id (id): int?`, `Email (email): string?`, `Reference (reference): string?` | **Case B** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — 404 means "no customer with this reference" (use it as the not-found signal); `RawError.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | `operations/Customers.md` |
| Create | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CustomerResponse` (same envelope) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `operations/Customers.md` |

- `CreateCustomerRequest` fields: `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req` — the payload itself is nested one level down.
- `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, then optional `Reference (reference): string?`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, …
- Idempotency fact (from the operation's Notes): **only one customer may exist per `reference` value** — a duplicate-reference create is rejected with 422. Setting `Reference` to the eShop user's stable id is the idempotency key.
- ⚠ The generated 422 payload type `CustomerErrorResponse1` is `Errors (errors): MaxioAdvancedBilling.Models.Errors?` and `Errors` models only `PerPage (per_page)` / `PricePoint (price_point)` attribute lists — it does **not** model attribute-keyed validation errors (e.g. a `reference` error). Treat the typed accessor as best-effort; for the actionable body use `TryGetRawError` and `ReadAsString()`/`ReadAsJson<T>()`. The exact live 422 JSON is **UNVERIFIED**.

**Resolve product family (Step 2)** — `operations/ProductFamilies.md`

| Operation | Signature (verbatim) | Returns / envelope | Error case | Pagination |
|---|---|---|---|---|
| List families | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable params, no defaults → **must pass explicitly** (pass `null` to skip) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; envelope `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` (nullable — null-check). Match `ProductFamily.Handle (handle): string?` against `Maxio:ProductFamilyHandle`, take `ProductFamily.Id (id): int?` | **Case B** `SdkException<RawError>` | none |

Resolution strategy (grounded): call `ListProductFamilies` once with all `null` filters, find the entry whose `Handle` equals the configured handle, cache its `Id`. `ReadProductFamily`'s Notes document a `handle:my-family` path format for that endpoint, but no map page documents handle-format support on `ListProductsForProductFamily` — passing the resolved numeric id is the verified route.

**List plans (Step 3, `GET /api/subscription-plans`)** — `operations/ProductFamilies.md`

| Operation | Signature (verbatim) | Returns | Error case | Pagination |
|---|---|---|---|---|
| List products for family | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — first arg is the **resolved family id as string**; the 8 nullable params before `page` must pass explicitly | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; envelope `Product (product): MaxioAdvancedBilling.Models.Product !req` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual `page`+`perPage` (defaults 1 / 20); one page covers 2 plans, but keep the loop shape for growth |

`Product` fields the endpoint DTO needs (`records-3-Of-Su.md`): `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?` (`Day (day)`, `Month (month)`), `RequestCreditCard (request_credit_card): bool?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?` (filter archived out app-side). Price comes from the product's default price point via `PriceInCents`.

**Subscribe (Step 5, `POST /api/subscriptions`)** — `operations/Subscriptions.md`, `records-2-Cr-Ne.md`

| Operation | Signature (verbatim) | Returns | Error case | Pagination |
|---|---|---|---|---|
| Pre-check | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly** | `SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [**404 = not subscribed yet**] · `TryGetRawError(out RawError)` | none |
| Create | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — must pass explicitly | `SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422; `Errors (errors): IReadOnlyList<string> !req`] · `TryGetRawError(out RawError)` | none |

- `CreateSubscriptionRequest`: `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req`.
- `CreateSubscription` fields to set (nothing is `required` on the record — the model marks no fields mandatory, so acceptance depends entirely on which optional fields you carry): `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?` · `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` · `Reference (reference): string?` — the idempotency key · `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (`Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`) · `CouponCode`, `NextBillingAt`, `CalendarBilling`, `Components`, … — all out of scope here.
- **Payment profile omitted by design** (per the operation's Notes, payment depends on product options; card-capture state is visible in `Product.RequestCreditCard` / `Product.RequireCreditCard`): omit `PaymentProfileId`, `CreditCardAttributes`, `BankAccountAttributes`, `PaymentProfileAttributes` entirely. Do not send empty payment objects. With the default collection method (`Automatic`) this payload is rejected 422 on card-required products — `"No payment method was on file for the $X balance"` — resolved by the `Remittance` setting below.
- **Card-free enrollment — LIVE-VERIFIED (sandbox site `the sandbox site`)**: for products with `request_credit_card=true` / `require_credit_card=false`, set `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance` on the `CreateSubscription` payload — everything else unchanged (`ProductHandle`, `CustomerId`, `Reference`, no payment fields). Live result: subscription created in state `active`, correct product price, `next_assessment_at` populated, and `FindSubscription`-by-reference then returns it (idempotent double-click returns the existing subscription). With the default (`Automatic`) the same payload is rejected 422 `"No payment method was on file for the $X balance"`. `NetTerms (net_terms): string?` is unrelated to this path (invoice-billing due days, 0–180); enum values per `models/enums.md`.
- **Idempotent subscribe mechanism:** set `Reference` to a deterministic per-user-per-plan value (exact format is the app's call, e.g. derived from user id + product handle). Flow: `FindSubscription(reference)` → 404 via `TryGetNoContent` → `CreateSubscription` with the same `Reference`; a 200 from `FindSubscription` means "already subscribed" — return the existing subscription rather than erroring. Consequence the app must weigh: two concurrent requests can both observe the 404 before either create lands — the map documents no server-side uniqueness rule for subscription `reference`, so whether the double-create window needs closing (per-user lock, etc.) is **YOUR CALL — not in the map**. There is no documented "duplicate subscription for same customer/product" rejection to catch — the 422 case (`ErrorListResponse1.Errors` string list) covers validation failures, not duplicates.
- Confirmation fields, read one level down: `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` (nullable — null-check), then from `Subscription` (`records-3-Of-Su.md`): `Id (id): int?` · `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?` · `ProductPriceInCents (product_price_in_cents): long?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` and `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (both present; either can serve as "next billing date") · `Reference (reference): string?` · `PaymentCollectionMethod` · `Currency (currency): string?` · nested `Product (product): MaxioAdvancedBilling.Models.Product?` (plan name/handle/price) · nested `Customer (customer): MaxioAdvancedBilling.Models.Customer?`.
- `SubscriptionState` values (`models/enums.md`): `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` — `StringEnum<T>` records, not C# enums.

**My subscriptions (Step 6, `GET /api/my-subscriptions`)** — `operations/Customers.md`

| Operation | Signature (verbatim) | Returns | Error case | Pagination |
|---|---|---|---|---|
| List a customer's subscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` — same envelope and `Subscription` fields as above (state, nested `Product` handle/id/price, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Reference`) | **Case B** `SdkException<RawError>` (404 → unknown customer id) | **none** — the endpoint returns all of the customer's subscriptions |

- Customer id comes from the Step-4 lookup (`Customer.Id`), or is cached app-side keyed on the user. `ReadCustomerByReference` re-anchors a stale cached id.
- The site-wide `ListSubscriptions` has **no customer filter** in its 14-parameter signature (verified: `state`, `product`, coupon, date, metadata, direction, sort, include, page, perPage) — it is not usable for per-customer listing; `ListCustomerSubscriptions` is the operation for this route.

**Metered usage — informational only, no implementation now** — `operations/Components.md`, `operations/SubscriptionComponents.md`, `models/unions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md`

- Resolve the component handle: `client.Components.FindComponent(string handle, CancellationToken ct = default)` → `ComponentResponse` (Case B; pass `"api-call"`). From the response's `Component` take `Id (id): int?`.
- Record usage: `client.SubscriptionComponents.CreateUsage(MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference subscriptionIdOrReference, MaxioAdvancedBilling.Models.AnyOf.ComponentIdModel componentId, MaxioAdvancedBilling.Models.CreateUsageRequest? body, CancellationToken ct = default)` → `UsageResponse`, **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateUsageError>`. The `AnyOf` params are built with the factories `SubscriptionIdOrReference.Int(...)` / `.String(...)` and `ComponentIdModel.Int(...)` / `.String(...)` (namespace `MaxioAdvancedBilling.Models.AnyOf`), not with `new`. `CreateUsageRequest` wraps `Usage (usage): MaxioAdvancedBilling.Models.CreateUsage !req`; `CreateUsage` fields: `Quantity (quantity): double?`, `Memo (memo): string?`, `PricePointId (price_point_id): string?`, … Negative quantity deducts usage (per the operation's Notes).

### 2.3 Error handling summary (per-operation detail above)

| Situation | What is thrown | How to read it |
|---|---|---|
| Any non-2xx on a Case B op (`ReadCustomerByReference`, `ListProductFamilies`, `ListCustomerSubscriptions`, `FindComponent`) | `SdkException<RawError>` | `ex.Error.StatusCode` (e.g. 401 invalid API key, 404 not found), `ex.Error.ReadAsString()` for the body |
| 4xx with typed shape on a Case A op (`CreateCustomer`, `CreateSubscription`, `FindSubscription`, `ListProductsForProductFamily`) | `SdkException<{Operation}Error>` | The row's `TryGet…(out …)` accessor for the documented status; `TryGetRawError(out RawError)` for everything else, including **401** |
| There is no base `ApiException` and no no-throw variant | — | Every operation is throw-only; every catch ladder bottoms out at the two `SdkException<T>` shapes above |

---

## 3. Trap notes (attached to the step where each bites)

- ⚠ Step 1 (client registration) — the SDK client wraps an `HttpClient` the caller supplies; how that `HttpClient` is owned and how long the client lives in DI decides whether sockets/handlers leak. **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ Step 1 (credentials) — credentials must be set on the options **before** the client is built, and the API key must come from configuration, never a literal; getting the Username/Password roles backwards yields a 401 that looks like a bad key. **MUST load `dotnet-authentication`**.
- ⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs get retried on which trigger decides whether a non-idempotent write can execute twice. **MUST load `dotnet-configuration-resilience`** before wiring the client, and again before touching `BaseUrl`/pagination behavior.
- ⚠ Steps 2–6 (every call) — list/search operations take a long run of nullable parameters with **no C# defaults**; a positional call silently mis-binds them. Call with named arguments and pass `null` explicitly for the skipped filters. **MUST load `dotnet-calling-endpoints`** before the first SDK call.
- ⚠ Steps 3–6 (models) — enums are `StringEnum<T>` records (write `CollectionMethod.Automatic`, never a raw string or C# enum), response envelopes wrap their payload one level down, `required` members must appear in the initializer, and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** before constructing any request payload or mapping a response.
- ⚠ Step 7 (error boundary) — the map's error-accessor rows are per-operation and two-shaped (Case A/B); a catch ladder built on memory of one shape misreads the other. **MUST load `dotnet-error-handling`** before writing any `try/catch` around an SDK call.
- ⚠ Steps 1–7 (tests) — the `HttpClient` constructor argument is the test seam; the SDK surface is not designed to be mocked member-by-member. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load before implementation starts)

The sheet deliberately does not carry these skills' contents — each names a hazard above and resolves it in the skill itself.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, options shape, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic credentials (Username = API key, Password = `"x"`), config-sourced secrets, 401 diagnosis |
| `dotnet-calling-endpoints` | Steps 2–6 — named-argument calling, must-pass-explicitly parameters, envelope unwrapping |
| `dotnet-models` | Steps 3–6 — request records, `required` members, `StringEnum<T>` enums, `AnyOf` factories, wire names |
| `dotnet-error-handling` | Step 7 — the Case A/B catch ladder, `RawError` reads, and the JSON-malformed-body hazards below |
| `dotnet-configuration-resilience` | Steps 1–3 — retry/timeout semantics, verbatim `BaseUrl` override, manual pagination |
| `dotnet-testing` | Test seam — faking at the `HttpClient` constructor boundary, covering error paths |

Two hazard rows that reach the boundary from opposite directions (they need opposite handling; part of `dotnet-error-handling`'s territory):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**

1. Caller identity (the eShopOnWeb user id / email from the JWT) is resolved by the PublicApi project's existing auth conventions; the sheet consumes it but does not specify how it is extracted.
2. The eShop user id is used as the Maxio customer `reference`, and a deterministic per-user-per-plan string as the subscription `reference` — the exact value formats are the app's call (both fields are optional `string?` on the create models).
3. Two plans and one metered component exist under family `eshop-subscribe` as described; handles are stable, so the code keys on `Handle`, never on cached numeric ids (family id is resolved live and cached, products are matched by handle).

**Blockers**

None remaining. Former Blocker 1 — the `CreateSubscription` 422 `"No payment method was on file for the $299.00 balance"` (site `the sandbox site`, plan `eshop-pro`) — is **resolved, live-verified on `the sandbox site`**: the 422 fires only with the default collection method (`Automatic`) on products carrying `request_credit_card=true` / `require_credit_card=false`. The fix is a one-field code change: set `PaymentCollectionMethod = CollectionMethod.Remittance` on the `CreateSubscription` payload (everything else unchanged: `ProductHandle`, `CustomerId`, `Reference`, no payment fields) — live result: subscription created in state `active`, correct product price, `next_assessment_at` populated, and `FindSubscription`-by-reference then returns it (idempotent double-click returns the existing subscription). Step-5 rows in §2.2 updated accordingly.

**Explicitly UNVERIFIED (live-traffic-only facts; defensive coding directives, not certainties)**

- The exact wire shape of a `CreateCustomer` 422 body: the generated `CustomerErrorResponse1`/`Errors` pair does not model attribute-keyed errors. Directive: extract best-effort from the typed accessor, fall back to `TryGetRawError` + `ReadAsString()` for the message shown to the caller.
- Whether the live wire always populates `Subscription.NextAssessmentAt` vs `Subscription.CurrentPeriodEndsAt` for a no-trial, no-card subscription. Directive: display whichever is non-null, preferring `NextAssessmentAt`; treat both as nullable.
