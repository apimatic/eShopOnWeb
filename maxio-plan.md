# Maxio Advanced Billing integration plan — eShopOnWeb (`src/PublicApi`, .NET 8)

## 1. Scope & sequence

| Step | Work | Operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` (v1.0.2) to `src/PublicApi`; root namespace is `MaxioAdvancedBilling` (package id ≠ namespace) | — |
| 2 | Register `MaxioAdvancedBillingClient` in DI; wire Basic auth, US environment, site from `Maxio:Subdomain`, optional verbatim base-URL override from `Maxio:BaseUrl` | — (client construction) |
| 3 | `GET /api/subscription-plans` — list products in family `handle:eshop-subscribe` | `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — find-or-create customer by reference, then idempotent subscribe by product handle | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve customer by reference, list their subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary translating `SdkException<T>` / `JsonException` to HTTP responses | (error model below) |

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

### 2.0 SDK identity & client construction (map: `sdk-map.md`; sources named there)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` version `1.0.2` — `dotnet add package AsadAli.AdvancedBilling.Sdk` |
| Root namespace | `MaxioAdvancedBilling` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — props: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| Auth | `o.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<api key>", Password = "x" }` — **password is the literal string `"x"`** |
| Environment | `o.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) → base template `https://{site}.chargify.com`. **There is no separate "sandbox" environment** — a sandbox site is just a subdomain on the same host |
| Site (subdomain) | `o.Server.Production.Us.Site = "<Maxio:Subdomain>"` (e.g. `"cp-exp-1"`). `ServerOptions` is in root namespace `MaxioAdvancedBilling`; `ProductionOptions`/`UsOptions` are in `MaxioAdvancedBilling.Servers` (source-verified: `ServerOptions.cs` declares `namespace MaxioAdvancedBilling`, `Servers/ProductionOptions.cs` declares `namespace MaxioAdvancedBilling.Servers` with nested `UsOptions { BaseUrl, Site }`) |
| Custom base URL | **SUPPORTED (no gap).** When `Maxio:BaseUrl` is set: `o.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` — used verbatim as the API base address, replacing the `{site}`-templated URL. Set `BaseUrl` *instead of* `Site` when the override is present |
| Retry options | `o.Retry` — `MaxioAdvancedBilling.Core.Configuration.RetryOptions`, all members `required`; start from `RetryOptions.Default()` |

Usings needed: `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Api` (only if referencing controller types), `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Errors`, `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Core.Configuration`, `MaxioAdvancedBilling.Core.ErrorResponse`, `MaxioAdvancedBilling.Core.Exceptions`, `MaxioAdvancedBilling.Servers`. Child namespaces are **not** imported transitively.

### 2.1 Error-handling model (map: `sdk-map.md` — applies to every call)

- All operations are **throw-only** (no `…Result` no-throw variants exist in this SDK).
- On error status: `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` with `.Error: TError`.
- **Case A (typed):** `TError` = generated `…Error : ApiError` with status-specific `TryGet…(out …)` accessors plus inherited `TryGetRawError(out RawError)`.
- **Case B (raw):** `TError` = `MaxioAdvancedBilling.Core.ErrorResponse.RawError` — members: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.

### 2.2 Operations

| # | Operation (controller property · verbatim signature) | Request model | Response envelope → fields read | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable with **no C# default: pass `null` explicitly** | none (query only). **`productFamilyId` accepts `"handle:eshop-subscribe"`** — source-verified param doc: "Either the product family's id or its handle prefixed with `handle:`" | `IReadOnlyList<ProductResponse>` → per item `.Product` (`Product (product): Product !req`) → `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` (skip non-null), `RequireCreditCard (require_credit_card): bool?` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage`; loop until a short page | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 2 | `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query `reference`) | none | `CustomerResponse` → `.Customer` (`Customer (customer): Customer !req`) → `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName` | **Case B** `SdkException<RawError>` — "not found" = catch and test `ex.Error.StatusCode == HttpStatusCode.NotFound` (no typed 404 accessor on this op) | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| 3 | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest { Customer = new CreateCustomer { FirstName = …, LastName = …, Email = …, Reference = "<eShopOnWeb user id>" } }`. `CreateCustomer` **required**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; optional used here: `Reference (reference): string?` (unique per site — server enforces one customer per reference value) | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ payload `CustomerErrorResponse1.Errors` is typed `Errors?`, and `Errors` models only `PerPage (per_page)` / `PricePoint (price_point)` string lists — a suspicious shared model for a customer-422. **Directive (UNVERIFIED):** extract messages best-effort, fall back to `TryGetRawError` + `ReadAsString()` | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 4 | `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>` → per item `.Subscription` (`Subscription (subscription): Subscription?`) → fields in §2.3 | **Case B** `SdkException<RawError>` | none (returns all) | `operations/Customers.md`, `records-4-Su-We.md` |
| 5 | `client.Subscriptions` · `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed explicitly (pass the value, never null here) | none | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] = **no such subscription → safe to create** · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| 6 | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = "<product handle>", CustomerId = <customer id>, Reference = "<idempotency key, e.g. user id>" } }`. `CreateSubscription` fields used (all optional, no `required` members): `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?` (alternative to `CustomerId`), `Reference (reference): string?`. **Send no payment fields** — `PaymentProfileAttributes`/`CreditCardAttributes`/`BankAccountAttributes`/`PaymentProfileId` stay null; product config (no trial, no setup fee, `RequireCreditCard == false`) makes signup succeed without a card | `SubscriptionResponse` → `.Subscription` → fields in §2.3 | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `Errors (errors): IReadOnlyList<string> !req` · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |

**Idempotency design (GAP-driven):** the SDK/API exposes **no idempotency-key header or parameter** on `CreateSubscription` (source-verified: no idempotency support in the Subscriptions controller). Idempotency is built at the app level, in this order:
1. `FindSubscription(Reference)` — 404 (`TryGetNoContent`) → proceed; found → return existing.
2. `ListCustomerSubscriptions(customerId)` — if any subscription has `Product.Handle == <requested handle>` **and** `State` in the live set (`Active`, `Trialing`, `Assessing`, `Pending`, `AwaitingSignup`, `PastDue`, `Unpaid`, `OnHold`, `SoftFailure`, `Paused` — i.e. not `Canceled`, `Expired`, `FailedToCreate`, `TrialEnded`) → return that existing subscription instead of creating.
3. Only then `CreateSubscription`. On a 422 race (concurrent double-click), re-run steps 1–2 and return the winner.
Customer find-or-create is likewise idempotent: `Reference` is unique server-side, so a create race yields 422 → re-run `ReadCustomerByReference` and use the existing customer.

### 2.3 `Subscription` response fields read by `GET /api/my-subscriptions` (map: `records-3-Of-Su.md`)

| Field | Wire name | Type | Use |
|---|---|---|---|
| `Id` | `id` | `int?` | subscription id |
| `State` | `state` | `SubscriptionState?` | status display |
| `Product` | `product` | `Product?` | `.Name`, `.Handle` for plan name/handle |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | plan price (cents) |
| `CurrentBillingAmountInCents` | `current_billing_amount_in_cents` | `long?` | current amount actually billed (cents) — prefer for "price" display |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | next billing/assessment date |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | period end (fallback display) |
| `Reference` | `reference` | `string?` | correlation back to eShopOnWeb user |
| `Currency` | `currency` | `string?` | display |

### 2.4 Enums (map: `enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>` records, **not** C# enums — use static members or `Type.FromValue("wire")`)

| Enum | Members (C# member ← wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` — monthly plans ⇒ `IntervalUnit.Month` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — only needed if you set `PaymentCollectionMethod`; default (omit) is fine for this scope |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` — only for `ListSubscriptions`, which this plan does **not** use (listed for completeness) |

---

## 3. Trap notes

> ⚠ Step 2 (client registration) — the SDK's retry/timeout options do **not** bound a whole
> call and are **not** the timeout on the `HttpClient` you register; and whether a failed
> `CreateSubscription` POST may have been re-sent by the retry layer decides how much of the
> idempotency design in §2.2 is load-bearing. **MUST load `dotnet-configuration-resilience`**
> before wiring the client.

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has
> specific ownership and lifetime rules (who disposes what, and why per-request construction
> breaks it). **MUST load `dotnet-client-initialization`** before writing the DI registration.

> ⚠ Step 2 (auth) — credentials must be in place before the client is constructed / in the DI
> callback, and the API key belongs in configuration, not code; a 401/403 shape has a specific
> diagnosis order. **MUST load `dotnet-authentication`**.

> ⚠ Steps 3–5 (calling endpoints) — list/search operations take many nullable parameters with
> **no C# defaults** (see §2.2: 8 must-pass params on `ListProductsForProductFamily`); positional
> calls mis-bind. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 3–5 (models) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>` records, not C#
> enums (no `switch` on values, no `Enum.Parse`); response enums arrive nullable; unmodeled JSON
> fields are silently dropped on deserialize. **MUST load `dotnet-models`**.

> ⚠ Step 6 (error boundary) — which of Case A / Case B each operation throws is per-operation
> (§2.2 rows), `TryGetRawError` is not a catch-all on typed errors, and `ReadCustomerByReference`
> signals "not found" only through `RawError.StatusCode`. **MUST load `dotnet-error-handling`**.

> ⚠ Step 6 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member)
> surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an
> `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

> ⚠ Step 6 (error boundary) — a **non-2xx** body that does not match its operation's generated
> `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*,
> so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with
> it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic
> rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

> ⚠ Testing — the SDK's test seam is the `HttpClient` constructor argument; stub at that layer,
> not by wrapping generated controllers. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs step 2 (client construction & DI registration).
- `dotnet-authentication` — governs step 2 (Basic credentials wiring).
- `dotnet-calling-endpoints` — governs steps 3–5 (every SDK call, esp. must-pass-null parameters).
- `dotnet-models` — governs steps 3–5 (records, `StringEnum<T>` enums, nullability).
- `dotnet-error-handling` — governs step 6 (the Case A/B boundary and both `JsonException` hazards above).
- `dotnet-configuration-resilience` — governs step 2 (retry/timeout semantics, base-URL override, pagination).
- `dotnet-testing` — governs the integration tests for steps 3–6.

---

## 5. Assumptions & Blockers

**Assumptions**
1. Sandbox site `cp-exp-1` is US-hosted ⇒ `ServerEnvironment.Us`. If the account is EU-hosted, switch to `ServerEnvironment.Eu` and set `o.Server.Production.Eu.Site` / `.Eu.BaseUrl` instead.
2. eShopOnWeb user id is used verbatim as both the customer `Reference` and the subscription `Reference`. If a user may hold several subscriptions to *different* plans simultaneously, make the subscription reference `"{userId}:{productHandle}"` instead — `FindSubscription` and the dedupe logic are unaffected.
3. "Active subscription" for dedupe = the live-state set in §2.2 (`Active`, `Trialing`, `Assessing`, `Pending`, `AwaitingSignup`, `PastDue`, `Unpaid`, `OnHold`, `SoftFailure`, `Paused`); `Canceled`/`Expired`/`FailedToCreate`/`TrialEnded` do not block re-subscribe.
4. Prices are surfaced in cents (`long?`); the API formats money as integer cents on these models, so no decimal parsing is needed.
5. JWT authentication of the eShopOnWeb endpoints themselves is application infrastructure, outside the SDK contract.
6. The metered component `api-call` is out of scope; no component operations appear in this plan.

**Gaps (explicit, no workaround invented)**
- **No SDK/API idempotency key** on `CreateSubscription` (source-verified). Mitigated app-side via `Reference` + `FindSubscription` + `ListCustomerSubscriptions` dedupe (§2.2) — this is a design, not an SDK feature.
- `ReadProductFamily(int id)` cannot resolve a family by handle (parameter is `int` despite the doc remark mentioning `handle:my-family`). Not needed: `ListProductsForProductFamily` accepts `"handle:…"` directly (source-verified), so family-handle → products is one call.
- **UNVERIFIED:** `CustomerErrorResponse1.Errors` is typed as a shared `Errors` record modeling only `per_page`/`price_point` messages — it likely does not match the live create-customer 422 body. Defensive directive in §2.2 row 3 (best-effort extract, fall back to raw body). Only live traffic can confirm.
- **UNVERIFIED:** whether the live `ListProductsForProductFamily` 404 body is a plain string matching `TryGetString(out string)` — extract best-effort, fall back to `TryGetRawError`.

**Blockers** — none.
