# eShopOnWeb × Maxio Advanced Billing — recurring-subscription plan (PublicApi)

Additive, parallel capability on the existing JWT-authenticated `src/PublicApi` ASP.NET Core app:
`/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions`. All Maxio traffic is
sandbox (US-hosted `.chargify.com`). SDK: `AsadAli.AdvancedBilling.Sdk`, namespace
`MaxioAdvancedBilling`.

## 1. Scope & sequence

Implementation steps in order (each names the Maxio operations it uses — all signatures and
shapes in §2):

1. **Client registration + settings** in the PublicApi composition root. Bind `Maxio:` section
   (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl`) to a settings model;
   construct `MaxioAdvancedBillingClient` via DI. Uses **no** Maxio operation. (Client facts in
   §2.5.)
2. **Error boundary + Maxio facade interface.** One catch ladder shared by all three endpoints
   (Case A/B mechanics + the two JsonException directions — REQUIRED READING rows below).
3. **GET `/api/subscription-plans`** → `client.ProductFamilies.ListProductsForProductFamily`
   with the configured family handle, filter client-side to the plans to expose. Map
   `ProductResponse.Product` → plan DTO (handle, name, price amount from `PriceInCents`,
   `Interval`, `IntervalUnit`, recurring price = same interval amount — sandbox plans have no
   trial/setup-fee, see §5).
4. **POST `/api/subscriptions`** (find-or-create customer, then subscribe):
   `client.Customers.ReadCustomerByReference` → on 404 `client.Customers.CreateCustomer`
   (deterministic `Reference`) → `client.Subscriptions.CreateSubscription` with
   `CustomerId` + `ProductHandle`, **no payment-profile members**. Map the create response →
   subscribe DTO (plan, price, state, next billing date). Idempotency path: subscription
   double-submit guard is the app's (§2.4, §5).
5. **GET `/api/my-subscriptions`** → `client.Customers.ReadCustomerByReference` (404 ⇒ empty
   list) → `client.Customers.ListCustomerSubscriptions(customerId)`. Map each
   `SubscriptionResponse.Subscription` → subscription DTO.
6. (Supporting, optional) **FindSubscription** by a deterministic subscription `Reference` to
   short-circuit a repeat subscribe before calling CreateSubscription.
7. **Tests** for the facade (fake seam is the SDK's `HttpClient` ctor arg, per
   `dotnet-testing`).

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

**Namespaces in use:** controllers `MaxioAdvancedBilling.Api` · records
`MaxioAdvancedBilling.Models` · enums `MaxioAdvancedBilling.Models.Enums` · error classes
`MaxioAdvancedBilling.Errors` · `SdkException<TError>` `MaxioAdvancedBilling.Core.Exceptions` ·
`RawError` `MaxioAdvancedBilling.Core.ErrorResponse` · client + options (root)
`MaxioAdvancedBilling` · `ServerOptions` (root) `MaxioAdvancedBilling` · `ServerEnvironment`
`MaxioAdvancedBilling.Servers` · `ProductionOptions` `MaxioAdvancedBilling.Servers` ·
`BasicAuthCredentials` `MaxioAdvancedBilling.Core.Authentication.Basic`.

Records are `init`-only: build with the object initializer. `!req` = C# `required` (must be set
in the initializer). `Type?` = optional. All response fields you read are nullable unless marked
`!req` — guard before use. Field list is `CSharpName (wire_name)`.

### 2.1 Operations

| # | Controller property · method signature (call `await`, pass `ct:`) | Request model + fields we set | Response envelope → fields the app reads | Error case (catch type + accessors) | Pagination | Source |
|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` → `Task<IReadOnlyList<ProductResponse>>` | no body. `productFamilyId:` = `"handle:" + MaxioOptions.ProductFamilyHandle` (id **or** handle with `handle:` prefix — SDK param doc in `Api/ProductFamilies.cs`); pass `dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null`; `page`/`perPage` defaults fine (family has 2 products) | each `ProductResponse.Product` (`!req`): `Handle (handle): string?` · `Name (name): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `Taxable (taxable): bool?` · `RequireCreditCard (require_credit_card): bool?` · nested `ProductFamily (product_family): ProductFamily?` with `Handle (handle): string?` | **A** — `SdkException<ListProductsForProductFamilyError>` (`MaxioAdvancedBilling.Errors`): `TryGetString(out string)` [404 = bad family] · fallback `TryGetRawError(out RawError)` (401/other) | manual `page`/`perPage` (none needed) | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 2 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` → `Task<CustomerResponse>` | none (`reference` = the app's deterministic customer reference) | `CustomerResponse.Customer` (`!req`): `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName (first_name): string?` · `LastName (last_name): string?` | **B** — `SdkException<RawError>`: 404 = no such customer → check `ex.Error.StatusCode == HttpStatusCode.NotFound`; 401 = bad key → `StatusCode == Unauthorized`, body via `ex.Error.ReadAsString()` | none | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| 3 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` → `Task<CustomerResponse>` | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; on `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` — we set all four (reference = same deterministic value as #2). **Dedupe is server-enforced**: provider notes — only one customer per `reference` value; `reference` must be unique | `CustomerResponse.Customer` (`!req`) — same fields as #2 (`Id`, `Reference`, `Email`) | **A** — `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · fallback `TryGetRawError(out RawError)`. ⚠ 422 payload model `CustomerErrorResponse1.Errors` is typed `Errors` which only models `PerPage (per_page)`/`PricePoint (price_point)` — the real per-attribute message (incl. duplicate-reference) is **dropped** on deserialize, and on a 422 `TryGetRawError` returns `false` (dispatch is by status). Treat "422 on create" as *reference likely taken* → re-run #2. A double-click therefore cannot duplicate the customer: both 404, both create, server rejects the loser | none | `operations/Customers.md`; `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 4 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` → `Task<SubscriptionResponse>` | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`; on `CreateSubscription` set only: `CustomerId (customer_id): int?` (Maxio customer id from #2/#3), `ProductHandle (product_handle): string?` (plan handle, e.g. `eshop-pro`), optionally `Reference (reference): string?` (our subscription ref for #6). **No payment method**: omit `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, and leave `PaymentCollectionMethod` unset — provider notes: payment info "may be required … depending on the options for the Product", i.e. the product's card requirement governs; sandbox plans need none. Do **not** set `CustomPrice`, `ProductPricePointId/Handle` (no `product_handle` needed → product's **default price point** is used) | `SubscriptionResponse.Subscription` is **`Subscription?`** (nullable wrapper!) — null-guard. Read: `Id (id): int?` · `State (state): SubscriptionState?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date) · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `ProductPriceInCents (product_price_in_cents): long?` (recurring price actually billed) · `ProductPricePointId`/`ProductPricePointType` · nested `Product (product): Product?` (`Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`) · nested `Customer (customer): Customer?` | **A** — `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (validation messages, incl. payment-method/price-point errors) · fallback `TryGetRawError(out RawError)` (400/401/404/5xx; 401 = bad API key) | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`, `records-4-Su-We.md`, `records-3-Of-Su.md` |
| 5 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` → `Task<IReadOnlyList<SubscriptionResponse>>` | none (`customerId` = Maxio customer id from #2/#3) | each `SubscriptionResponse.Subscription` is **`Subscription?`** — skip/handle nulls. Per item read as #4's response list (`Id`, `State`, `NextAssessmentAt`, `ProductPriceInCents`, nested `Product.Handle/Name/…`, `PaymentCollectionMethod`) | **B** — `SdkException<RawError>` (404 for a deleted customer ⇒ treat as empty) | none | `operations/Customers.md`; `records-4-Su-We.md`, `records-3-Of-Su.md` |
| 6 | (supporting) `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` → `Task<SubscriptionResponse>` | none (`reference` = the deterministic subscription `Reference` we set at #4) | `SubscriptionResponse.Subscription` (`Subscription?`) — same fields as #4 | **A** — `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404 = no match] · fallback `TryGetRawError` | none | `operations/Subscriptions.md`; `records-4-Su-We.md` |

Notes tied to acceptance (provider prose — carry these, don't re-derive): **#1** lists the
family's products; archived excluded by `includeArchived: false`. **#4** identifies the product
by `product_handle` and the customer by `customer_id`; a specific price point would need
`product_price_point_handle`/`_id`, and a **custom** price point would need
`SubscriptionCustomPrice` — none are used here, so the **default price point** of the plan is
what subscribe charges, and that is the price the product listing (#1) surfaces
(`PriceInCents`/`Interval`/`IntervalUnit`).

### 2.2 Enums actually needed (`MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>` — load `dotnet-models` for equality/construction mechanics)

| Enum | C# members (wire values) | Where used |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `Subscription.State` (#4/#5/#6) — map to the API DTO string verbatim from the wire value |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `Product.IntervalUnit` (#1) — price cadence for the UI |
| (read-only context) `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `Subscription.PaymentCollectionMethod` if surfaced |

### 2.3 Idempotency / uniqueness facts

- **Customer uniqueness:** Maxio enforces *one customer per `reference`* (CreateCustomer Notes —
  "you may only create one customer for a given reference value… the reference value must be
  unique"). `ReadCustomerByReference` returns the single exact match (Notes: "single match").
  Finding by email/name is the fuzzy `ListCustomers(q:)` search — not needed. ⇒ Find-or-create
  on a **deterministic customer reference** is the safe key; a lost 404-vs-create race resolves
  itself as one 422 (see #3's error row).
- **Subscription uniqueness:** the map documents **no** server-side dedupe for subscriptions and
  no uniqueness guarantee for a subscription `Reference` (CreateSubscription Notes silent;
  `FindSubscription` merely looks a reference up). Multi-active-subscriptions-per-customer is
  normal, so Maxio will **not** stop a double-submit. Consequence: the "no duplicate
  subscriptions on double-click" guarantee must come from the app (§2.4).

### 2.4 Integration-layer shape (our code — the exact repo layout is YOUR CALL, §5)

- `MaxioOptions` bound from `Maxio:` — `ApiKey`, `Subdomain`, `ProductFamilyHandle`,
  `BaseUrl` (nullable). Never hard-code credentials.
- `IMaxioSubscriptionService` (implemented over the SDK client) with
  `Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct)`,
  `Task<SubscribeResult> SubscribeAsync(string planHandle, CancellationToken ct)`, and
  `Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(CancellationToken ct)`;
  caller identity (the JWT subject) is resolved by the app and passed in — no Maxio type leaks
  past this interface.
- DTO mapping: `SubscriptionPlan { Handle, Name, PriceAmount (decimal, `PriceInCents`/100),
  Interval, IntervalUnit }`; `SubscriptionInfo { SubscriptionId, PlanHandle, PlanName,
  PriceAmount, State, NextBillingDate (DateTimeOffset?) }`.
- **Idempotency:** (a) customer reference is deterministic from the authenticated user's stable
  id (e.g. `"eshop-" + <jwt subject>` — NOT the email, so an email change cannot fork the
  customer); (b) subscribe is guarded so one user + one plan yields one subscription — either a
  per-(user, plan) uniqueness/lock in the app's own store, or the optional
  deterministic-subscription-`Reference` + `FindSubscription` short-circuit (#6) plus serialized
  per-user execution. Which mechanism is the app's call; note a two-node race can still double-
  subscribe unless the app serializes, because Maxio does not dedupe.
- No Maxio numeric id is ever persisted/config-derived; ids returned by lookups are used within
  the same request flow only (ids are UNSTABLE across sites per the brief).

### 2.5 Client construction, base URL, auth

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = maxio.ApiKey, Password = "x" },
    // Environment = ServerEnvironment.Us;  // default (Us) — omit unless EU hosting
};
// Derived URL https://<subdomain>.chargify.com — the template fills {site}:
options.Server.Production.Us.Site = maxio.Subdomain;
// Optional verbatim override (when Maxio:BaseUrl is configured) — replaces the whole template:
// options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
var client = new MaxioAdvancedBillingClient(httpClient, options); // HttpClient ctor arg
```

Facts: `MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` is the only
ctor. Options members: `Environment` (`ServerEnvironment`, default Us), `Retry` (`RetryOptions`),
`Server` (`ServerOptions`), `BasicAuth`. `ServerOptions.Production` is `ProductionOptions`
(`MaxioAdvancedBilling.Servers`) with nested `Us`/`Eu`, each `{ BaseUrl, Site }`; US default
`BaseUrl = "https://{site}.chargify.com"`, `Site = "subdomain"`. Setting `BaseUrl` verbatim
replaces the template (no `{site}` remains ⇒ Site ignored). `BasicAuthCredentials { Username,
Password }` (`required` members). DI: `services.AddMaxioAdvancedBillingClient(o => …)` builds
options once and registers the client as a **singleton** over an `IHttpClientFactory` client.
Auth is HTTP Basic: username = API key, password = literal `"x"`. Source:
`MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`,
`ServiceCollectionExtensions.cs` (all map-named).

## 3. Trap notes

> ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call
> and are **not** the timeout on the `HttpClient` you register. **MUST load
> `dotnet-configuration-resilience`** before wiring the client.
>
> ⚠ Step 1 (client registration/DI) — the registered client is a singleton sharing one
> `HttpClient`; the options object is captured once at registration, and constructing the client
> per request (or per controller) rebuilds the whole pipeline. **MUST load
> `dotnet-client-initialization`** before writing the registration.
>
> ⚠ Step 1 (credentials) — auth is Basic with `Username` = API key and `Password` = literal
> `"x"`, set on the options **before** the client is built; a 401 surfaces per-operation as a
> raw error (Case B, or Case A's `TryGetRawError` fallback), not as an auth-specific exception.
> **MUST load `dotnet-authentication`** before wiring credentials.
>
> ⚠ Steps 3–6 (calls) — most in-scope calls take many nullable, default-less params (e.g. #1's
> eight) and list/find ops are Case B while create/find ops are Case A; positional calls mis-bind
> and only the map row says which `TryGet…` exists. **MUST load `dotnet-calling-endpoints`**
> before the first call.
>
> ⚠ Steps 3–5 (envelopes) — responses wrap their payload one level (`ProductResponse.Product`,
> `CustomerResponse.Customer`), but `SubscriptionResponse.Subscription` is **nullable** — reads
> go one level down and must null-guard; the request side is double-wrapped
> (`CreateSubscriptionRequest.Subscription`). **MUST load `dotnet-models`** before building
> payloads or mapping responses.
>
> ⚠ Steps 3–5 (state/price of a fresh subscription) — exactly which `SubscriptionState` a
> card-free, trial-free signup lands in (and whether `NextAssessmentAt` is already populated
> right after create) is not settled by the map — expose the returned values verbatim and treat
> a missing `NextAssessmentAt` as "not yet scheduled", never as a code fault. `UNVERIFIED`.
>
> ⚠ Steps 4 & 6 (idempotency) — SDK retry semantics can re-send a POST on a transport failure,
> so even a single call site can attempt a create twice; whether a failed write can be re-sent
> is governed by `dotnet-configuration-resilience`. **MUST load
> `dotnet-configuration-resilience`** before finalising the subscribe flow.
>
> ⚠ Step 7 (tests) — the SDK client is a concrete sealed type; the fake seam is the `HttpClient`
> constructor argument, and stubbing `SdkException`/`RawError` paths must match the Case A/B
> shapes above. **MUST load `dotnet-testing`** before writing tests for the facade.

## 4. REQUIRED READING

Load **before implementation starts** (the sheet deliberately does not carry their contents —
each row is the hazard the skill resolves for the step it governs):

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic credentials shape and 401 behaviour |
| `dotnet-configuration-resilience` | Steps 1, 4 — base-URL/server selection, retry/timeout semantics, POST re-send risk |
| `dotnet-calling-endpoints` | Steps 3–6 — controller access, required vs optional params, async/cancellation |
| `dotnet-models` | Steps 3–5 — request builders, envelopes, nullable members, `StringEnum` handling |
| `dotnet-error-handling` | Step 2 and every endpoint — the exception boundary |
| `dotnet-testing` | Step 7 — faking the SDK seam |

Always include, verbatim, **both** of these hazard rows — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the
FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives
afterwards arrives too late to shape it.

## 5. Assumptions & Blockers

**Assumptions**
- Caller identity for find-or-create is the JWT's stable subject (not the email, which can
  change) → deterministic Maxio customer `Reference`. The concrete claim path is the app's own
  (`YOUR CALL — not in the map`); the map only guarantees reference must be unique and is the
  exact-match lookup key.
- Plans list is small (2 plans) — default `page:1`/`perPage:20` suffices; if the family ever
  exceeds 20 products the loop over `page` is required (manual pagination).
- Where the Maxio integration layer lives (new `src/` project vs a folder in PublicApi) is the
  main agent's call. SDK-relevant constraints to honour either way: the project that owns the
  layer must reference the NuGet package; the SDK client is registered **once** at the
  composition root (singleton over `IHttpClientFactory`); keep Maxio types behind the facade so
  other layers never `using MaxioAdvancedBilling.*`; `dotnet add package
  AsadAli.AdvancedBilling.Sdk` (netstandard2.0 — fine on any current TFM in the repo).
- `Maxio:BaseUrl` when set is used verbatim and wins over the subdomain-derived
  `https://<subdomain>.chargify.com` (client fact §2.5). Sandbox is US-hosted ⇒ default
  `ServerEnvironment.Us`; `cp-exp-5` would resolve through `Site` without a BaseUrl override.
- Env→config mapping (`MAXIO_API_KEY` → `Maxio:ApiKey`, `MAXIO_SITE_SUBDOMAIN` →
  `Maxio:Subdomain`, `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`,
  optional `MAXIO_BASE_URL` → `Maxio:BaseUrl`) uses the app's existing config conventions
  (user-secrets/env providers); the exact provider wiring is the app's (`YOUR CALL`).
- Which exact `SubscriptionState` a successful card-free signup reports is `UNVERIFIED` (see
  trap note) — the DTO carries the returned state string verbatim.
- A double-click that arrives as two fully concurrent requests on two nodes can still create two
  subscriptions unless the app serialises per (user, plan); Maxio documents no subscription
  dedupe. The app's guard mechanism is `YOUR CALL`; the contract facts it must rely on are in
  §2.3/§2.4.

**Blockers**
- None that stop implementation.
