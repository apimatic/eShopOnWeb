# Maxio Advanced Billing integration plan — eShopOnWeb PublicApi

Recurring-subscription billing for eShopOnWeb with Maxio Advanced Billing as billing system of
record, exposed as JWT-authenticated endpoints on `src/PublicApi`:

1. `GET /api/subscription-plans` — list plans (products) in the configured product family.
2. `POST /api/subscriptions` — idempotently subscribe the caller to a plan by product handle.
3. `GET /api/my-subscriptions` — list the caller's subscriptions.

## 1. Scope & sequence

| Step | Work | Operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` (pin **1.0.2** — the ref this sheet was grounded against) to `src/PublicApi`. `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` | — |
| 2 | Bind config: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional) | — |
| 3 | Register the SDK client in DI (long-lived `HttpClient` via factory; see trap notes) | — |
| 4 | `GET /api/subscription-plans`: resolve family handle → id (cache it; it changes never/rarely), then list products in the family | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions`: find-or-create customer by reference, check for an existing live subscription to the same product, else create | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions`: find customer by reference, list their subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Error-translation layer: SDK exceptions → HTTP problem responses | (all of the above) |
| 8 | Tests for the integration layer (SDK faked at the `HttpClient` seam) | — |

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

### 2.0 SDK identity, client construction, auth, servers

| Fact | Value | Map page |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (≠ root namespace) — pin `1.0.2` | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` (`MaxioAdvancedBillingClient.cs`) |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment` (`ServerEnvironment`), `Server` (`ServerOptions`), `Retry` (`RetryOptions`), `BasicAuth` (`BasicAuthCredentials?`) | `sdk-map.md` (`MaxioAdvancedBillingClientOptions.cs`) |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` extension (`ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| Auth | HTTP Basic: `o.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` — password is the literal string `"x"`. Type `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` | `sdk-map.md` (`Core/Authentication/Basic/BasicAuthCredentials.cs`) |
| Environment | `o.Environment = ServerEnvironment.Us` (default; `https://{site}.chargify.com`) or `ServerEnvironment.Eu` (`https://{site}.ebilling.maxio.com`). Type `MaxioAdvancedBilling.Servers.ServerEnvironment`. **There is no "sandbox" environment value** — sandbox vs live is which *site subdomain* you point at; use the sandbox subdomain (e.g. `cp-exp-1`) with `Us` hosting (see Assumptions) | `sdk-map.md` (`Servers/ServerEnvironment.cs`) |
| Site subdomain | `o.Server.Production.Us.Site = <Maxio:Subdomain>` (`{site}` template slot; defaults to the literal `"subdomain"`) | `sdk-map.md` (`Server.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`) |
| Base-URL override | When `Maxio:BaseUrl` is set, use it verbatim **instead**: `o.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` (then `Site` is irrelevant). `ServerOptions` is root-namespace (`MaxioAdvancedBilling`); `ProductionOptions` is `MaxioAdvancedBilling.Servers` — the property chain `o.Server.Production.Us.*` needs no extra `using` beyond what construction already requires | `sdk-map.md` |
| Retry options | `o.Retry` — type `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; **all 9 members are C# `required`** — start from `RetryOptions.Default()` and mutate, or set every member | `sdk-map.md` (`Core/Configuration/RetryOptions.cs`) |
| Controller accessors | `client.ProductFamilies`, `client.Products`, `client.Customers`, `client.Subscriptions` (controllers live in `MaxioAdvancedBilling.Api`; reached via client properties) | `sdk-map.md` |

All operations are `async` and return `Task<T>` for the listed payload type `T` (`Task` where the
map says `void`). No operation has a no-throw `…Result` variant — every call is throw-only.

### 2.1 Step 4 — list plans

**4a. Resolve family handle → numeric id** (the list-by-family operation takes an id; there is no
list-products-by-family-handle operation, and `ReadProductFamily(int id)` cannot express the
`handle:…` form its doc text mentions — its C# parameter is `int`. So: list families, match the
handle, cache the id).

| | |
|---|---|
| Call | `client.ProductFamilies.ListProductFamilies(dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct)` |
| Signature | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — **all 5 filter params are nullable with no default → must pass explicitly (pass `null`)** |
| Returns | `Task<IReadOnlyList<ProductFamilyResponse>>` — no pagination |
| Error | **Case B**: `SdkException<RawError>` — `Error.StatusCode`, `Error.ReadAsString()` |
| Match | `resp.ProductFamily?.Handle == <Maxio:ProductFamilyHandle>` → take `resp.ProductFamily.Id` (`int?`) |

`ProductFamilyResponse` (envelope): `ProductFamily (product_family): ProductFamily?` — nullable, null-check.
`ProductFamily` fields read: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`.
Map pages: `operations/ProductFamilies.md`, `records-3-Of-Su.md`.

**4b. List products in the family**

| | |
|---|---|
| Call | `client.ProductFamilies.ListProductsForProductFamily(productFamilyId: familyId.ToString(), dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null, page: page, perPage: 200, ct: ct)` |
| Signature | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **8 params (`dateField`…`include`) must be passed explicitly (pass `null`)**; note `productFamilyId` is a `string` |
| Returns | `Task<IReadOnlyList<ProductResponse>>` — **manual pagination**: loop `page` until a page returns fewer than `perPage` items |
| Error | **Case A**: `SdkException<ListProductsForProductFamilyError>` — `Error.TryGetString(out string)` [404, family not found] · `Error.TryGetRawError(out RawError)` [fallback] |

`ProductResponse` (envelope): `Product (product): Product !req` — required, one level down.
`Product` fields read for the plan DTO: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?` (**cents** — divide by 100 for display), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` (non-null ⇒ archived ⇒ exclude even if `includeArchived: false` is relied on).
Enum `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (StringEnum, not a C# enum): `IntervalUnit.Day` (`day`), `IntervalUnit.Month` (`month`).
Map pages: `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `enums.md`.

### 2.2 Step 5/6 — find-or-create customer (idempotency anchor)

Stable reference: the eShopOnWeb caller's identity/username (e.g. the JWT `sub` or username claim),
used as the Maxio customer `reference`. Server-side uniqueness is guaranteed per the
`CreateCustomer` doc notes: only one customer may exist for a given `reference` value.

**5a. Find by reference**

| | |
|---|---|
| Call | `client.Customers.ReadCustomerByReference(reference: userRef, ct: ct)` |
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query param `reference`) |
| Returns | `Task<CustomerResponse>` |
| Error | **Case B**: `SdkException<RawError>` — **"customer not found" = `Error.StatusCode == HttpStatusCode.NotFound`**; that 404 is the find-or-create branch signal, not a failure |

`CustomerResponse` (envelope): `Customer (customer): Customer !req`.
`Customer` fields read: `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`.
Map pages: `operations/Customers.md`, `records-2-Cr-Ne.md`.

**5b. Create (only on 404 above)**

| | |
|---|---|
| Call | `client.Customers.CreateCustomer(body: new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = …, LastName = …, Email = …, Reference = userRef } }, ct: ct)` |
| Signature | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Returns | `Task<CustomerResponse>` |
| Error | **Case A**: `SdkException<CreateCustomerError>` — `Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 validation] · `Error.TryGetRawError(out RawError)` [fallback] |

`CreateCustomerRequest` (envelope): `Customer (customer): CreateCustomer !req`.
`CreateCustomer` — **required**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. **Optional**: `Reference (reference): string?` (set it — it is the idempotency key), plus `Organization`, `Address`/`Address2`/`City`/`State`/`Zip`/`Country`, `Phone`, `CcEmails`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` (all nullable).
eShopOnWeb identities may not carry a first/last name — derive both from the username/claims (they are C#-`required`; the initializer will not compile without them).
⚠ **422 payload caveat**: `CustomerErrorResponse1.Errors (errors)` is typed as the shared record `Errors`, whose only modeled members are `PerPage (per_page)` and `PricePoint (price_point)` — a suspicious shared-model artifact for a customer-creation error. **Directive (`UNVERIFIED` — only live traffic can confirm the real 422 body):** treat the typed accessor as best-effort; for messages, always fall through to `TryGetRawError(out var raw)` → `raw.ReadAsString()` and log/forward that.
**Duplicate-reference race** (double-click while a first create is in flight): the loser gets a 422. On `TryGetCustomerErrorResponse1` returning true, re-call `ReadCustomerByReference` and use the now-existing customer.
Map pages: `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`.

### 2.3 Step 5 — create the subscription (payment-not-required products)

**Duplicate guard first**: `ListCustomerSubscriptions` (2.4) and, if any subscription has
`Subscription.Product?.Handle == productHandle` **and** `Subscription.State` not in
{`Canceled`, `Expired`, `TrialEnded`, `FailedToCreate`}, return that existing subscription instead
of creating. (State is a `StringEnum`; compare against the static members — see trap notes.)

| | |
|---|---|
| Call | `client.Subscriptions.CreateSubscription(body: new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = productHandle, CustomerId = customerId, Reference = $"{userRef}:{productHandle}" } }, ct: ct)` |
| Signature | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Returns | `Task<SubscriptionResponse>` |
| Error | **Case A**: `SdkException<CreateSubscriptionError>` — `Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `Error.TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` — the validation messages |

`CreateSubscriptionRequest` (envelope): `Subscription (subscription): CreateSubscription !req`.
`CreateSubscription` — **no C#-`required` members** (all optional); the wire contract needs a product and a customer, supplied here as:
- `ProductHandle (product_handle): string?` — the plan handle (`eshop-pro`). (Alternative: `ProductId (product_id): int?`.)
- `CustomerId (customer_id): int?` — the Maxio customer id from 2.2. (Alternative: `CustomerReference (customer_reference): string?` = the same reference string; using it would let you skip resolving the id, but the find-or-create above already yields it.)
- `Reference (reference): string?` — set a deterministic value (`{userRef}:{productHandle}`) for auditability and lookup via `Subscriptions.FindSubscription`.
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — **optional in the model; leave `null`** for the configured no-trial/no-setup-fee/payment-not-required products. Enum `MaxioAdvancedBilling.Models.Enums.CollectionMethod`: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` (enum doc: legacy Statements architecture — `invoice`/`automatic`; Relationship Invoicing — `remittance`/`automatic`/`prepaid`). If the site/product config ever demands one, the 422 `ErrorListResponse1.Errors` will name it.
- No payment profile fields (`PaymentProfileId`, `CreditCardAttributes`, …) are sent — consistent with payment-not-required products. Per the operation's doc notes, payment info is required only "depending on the options for the Product being subscribed".
Map pages: `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `enums.md`.

### 2.4 Steps 5/6 — list a customer's subscriptions + response accessors

| | |
|---|---|
| Call | `client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct)` |
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Returns | `Task<IReadOnlyList<SubscriptionResponse>>` — no pagination (returns all) |
| Error | **Case B**: `SdkException<RawError>` |

`SubscriptionResponse` (envelope): `Subscription (subscription): Subscription?` — **nullable; null-check before reading**.
`Subscription` fields read for the DTO:
- `Id (id): int?`
- `State (state): SubscriptionState?` — enum below
- `Product (product): Product?` → `.Name`, `.Handle` (nested `Product`, same record as 2.1)
- `ProductPriceInCents (product_price_in_cents): long?` — **cents**
- **Next billing date: there is no `NextBillingAt` on the read model** — `NextBillingAt` exists only on the create-request record. Read `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (the next assessment/billing date); `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` is the fallback display value.
- `Reference (reference): string?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CanceledAt (canceled_at): DateTimeOffset?` as needed.

Enum `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (StringEnum): `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.
Map pages: `operations/Customers.md`, `records-4-Su-We.md`, `records-3-Of-Su.md`, `enums.md`.

### 2.5 Error-handling model (applies to every call)

- All errors throw `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`; the payload is `ex.Error`.
- **Case A** (typed): `TError` = a generated `{Operation}Error : ApiError` in namespace `MaxioAdvancedBilling.Errors`, with status-specific `TryGet…(out …)` accessors plus inherited `TryGetRawError(out RawError)`. In scope: `ListProductsForProductFamilyError` (`TryGetString` [404]), `CreateCustomerError` (`TryGetCustomerErrorResponse1` [422] — see the 2.2 caveat), `CreateSubscriptionError` (`TryGetErrorListResponse1` [422]).
- **Case B** (raw): `TError` = `MaxioAdvancedBilling.Core.ErrorResponse.RawError` — `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes()`. In scope: `ListProductFamilies`, `ReadCustomerByReference` (404 = not-found branch), `ListCustomerSubscriptions`.
- Catch order matters: `SdkException<CreateCustomerError>` and `SdkException<RawError>` are different generic instantiations — one `catch (SdkException<RawError>)` does **not** catch the typed ones.

## 3. Trap notes (hazards the signatures hide — load the named skill before coding that step)

> ⚠ Step 3 (client registration) — the `HttpClient`/handler pipeline behind the SDK client must be long-lived and shared (socket-exhaustion hazard if built per request); the SDK client wrapper's own lifetime differs from the pipeline's. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)` or the `AddMaxioAdvancedBillingClient` registration.

> ⚠ Step 3 (authentication) — credentials must be in place before the client is constructed / inside the DI callback, and the API key comes from configuration (`Maxio:ApiKey`), never code. **MUST load `dotnet-authentication`**.

> ⚠ Steps 4–6 (every call) — the list/read operations carry many optional parameters with **no C# default** (8 on `ListProductsForProductFamily`); a positional call mis-binds. Call with named arguments exactly as written in the contract sheet. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 4–6 (models) — SDK enums are `StringEnum<T>` records, **not C# enums**: no `switch` exhaustiveness, no bare `==` on wire strings; construct/compare via the static members (`SubscriptionState.Active`) or `FromValue`. Records are immutable with `init`-only setters and C#-`required` members that fail the build if omitted. Unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`**.

> ⚠ Step 7 (error boundary) — which operations are Case A vs Case B is per-operation (see 2.5); `TryGetRawError` on a typed error is a fallback, not a catch-all; and a 404 from `ReadCustomerByReference` is a *branch*, not a failure. **MUST load `dotnet-error-handling`**.

> ⚠ Step 3 (resilience) — whether a failed `CreateSubscription` POST can be transparently re-sent by the SDK's retry layer, and what the configured `Timeout` actually bounds, are not answerable from the option names; both bear directly on the idempotency design in 2.2/2.3. **MUST load `dotnet-configuration-resilience`** before wiring `o.Retry` or relying on any retry/timeout behavior.

> ⚠ Step 8 (testing) — the test seam is the `HttpClient` constructor argument, not mocking SDK types; match eShopOnWeb's existing test framework/assertion style. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING — load ALL of these before implementation starts

This sheet deliberately does not carry these skills' contents; loading them is part of the work.

- `dotnet-client-initialization` — governs step 3 (client construction & DI lifetime).
- `dotnet-authentication` — governs step 3 (Basic credentials from config).
- `dotnet-calling-endpoints` — governs steps 4–6 (named-argument calls, async/cancellation).
- `dotnet-models` — governs steps 4–6 (records, `StringEnum` enums, required members).
- `dotnet-error-handling` — governs step 7 (Case A/B mechanics, the error boundary). Always required — an integration always writes an error boundary.
- `dotnet-configuration-resilience` — governs step 3 (retry/timeout semantics vs. idempotent writes) and step 4 (manual pagination).
- `dotnet-testing` — governs step 8 (the `HttpClient` seam).

Two hazard rows that must shape the error boundary from the first draft:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Assumptions**
1. "Sandbox environment" is realized by pointing the client at the sandbox *site*: the SDK's
   `ServerEnvironment` selects US/EU **hosting region**, not sandbox-vs-live. Assumed US hosting
   (`ServerEnvironment.Us` + `Site = "cp-exp-1"`); if the account is EU-hosted, use
   `ServerEnvironment.Eu` + `.Eu.Site`. `Maxio:BaseUrl`, when set, replaces all of this verbatim.
2. The stable customer reference is the caller's username/identity claim from the JWT; the exact
   claim (`sub` vs `name`) is an eShopOnWeb decision. First/last name (C#-`required` on
   `CreateCustomer`) will be derived from that identity if no profile data exists.
3. The product-family id is resolved once and cached (config-handle → id lookup on first use);
   families are near-static.
4. "Price" is surfaced from `Product.PriceInCents` / `Subscription.ProductPriceInCents` (both
   cents, `long?`); formatting/currency is a presentation concern (`Subscription.Currency` exists
   if needed).
5. The duplicate-subscription guard treats `Canceled`, `Expired`, `TrialEnded`, `FailedToCreate`
   as non-blocking states; everything else blocks re-subscribe to the same product.
6. `UNVERIFIED` (only live traffic can confirm): the real 422 wire body for `CreateCustomer` —
   the generated `CustomerErrorResponse1` payload model looks like a shared-model artifact
   (2.2). The defensive directive there stands regardless.

**Blockers** — none.
