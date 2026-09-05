# Maxio Advanced Billing — subscription billing plan (eShopOnWeb `src/PublicApi`)

## 1. Scope & sequence

1. **Client & DI registration** — bind `Maxio:` config (`ApiKey`, `Subdomain`, `ProductFamilyHandle`,
   optional `BaseUrl`) and register `MaxioAdvancedBillingClient` via `AddMaxioAdvancedBillingClient`
   (singleton, `HttpClient` via `IHttpClientFactory`). No operations called yet.
2. **List subscription plans** — `client.ProductFamilies.ListProductsForProductFamily(...)` scoped to
   `Maxio:ProductFamilyHandle`, project each returned `Product` to plan/price/trial/setup-fee.
3. **Subscribe a user to a plan** —
   a. Ensure Maxio customer: `client.Customers.ReadCustomerByReference(reference)`; on a 404
      `SdkException<RawError>`, fall through to `client.Customers.CreateCustomer(...)`; on a 422 from
      that create, recover by re-calling `ReadCustomerByReference` (race-recovery — see §5).
   b. Resolve the site's billing architecture once (cacheable): `client.Sites.ReadSite()` →
      `SiteResponse.Site.RelationshipInvoicingEnabled`. This decides the **required**
      `PaymentCollectionMethod` value for a payment-method-not-required plan — see the corrected
      `CreateSubscription` row in §2.2 and §5 (a "no payment profile" subscribe on a non-zero-price,
      no-trial plan is rejected with a 422 under the default `Automatic` collection method; this is not
      an edge case, it is the documented default behavior).
   c. `client.Subscriptions.CreateSubscription(...)` with `CustomerId` from (a), `ProductHandle` from the
      chosen plan, **and `PaymentCollectionMethod` set per (b)** (plus `NetTerms` if invoicing); no
      payment-profile fields set.
   d. Project the resulting `Subscription` to the confirmation shape (plan/price/state/next billing).
4. **List my subscriptions** — same customer-resolution step as 3a (read-only, no create), then
   `client.Customers.ListCustomerSubscriptions(customerId)`, project each `Subscription` the same way
   as 3c.
5. **Error boundary** — one exception-translation layer wrapping every call above (see REQUIRED READING;
   the two `JsonException` hazard rows apply to all three capabilities).
6. **Tests** — around the new service/handler layer, faking the `HttpClient` seam (see REQUIRED READING).

None of the three capabilities need `SubscriptionComponents`/`api-call` (metered usage) — out of scope
per the brief.

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
> live in different ones.

### 2.1 Client construction / auth / server-node facts

| Fact | Detail | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor `(HttpClient, MaxioAdvancedBillingClientOptions)` | `sdk-map.md` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `MaxioAdvancedBillingClientOptions.cs` (source, confirmed) |
| Auth | `BasicAuth = new BasicAuthCredentials { Username = "<Maxio:ApiKey>", Password = "x" }` (literal `"x"`) | `sdk-map.md` §Servers & auth |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { ... })` — extension on `IServiceCollection` (namespace `MaxioAdvancedBilling`). Internally: calls `services.AddHttpClient()` (unnamed/default client), then registers `MaxioAdvancedBillingClient` as a **singleton** built once via `sp.GetRequiredService<IHttpClientFactory>().CreateClient()`. The `configure` callback runs **once**, at registration time — read `Maxio:` config synchronously inside it (e.g. from `builder.Configuration`), not per-request. | `ServiceCollectionExtensions.cs` (source, confirmed) |
| Base URL — default (no override) | `Environment = ServerEnvironment.Us` (default); site substitution: `Server.Production.Us.Site = "<Maxio:Subdomain>"` → resolves the built-in template `https://{site}.chargify.com` | `sdk-map.md` §Servers & auth; `Servers/ProductionOptions.cs` (source, confirmed) |
| Base URL — `Maxio:BaseUrl` override | Set `Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` **verbatim**. The template engine only substitutes `{site}` if that literal token is present in the string; an override with no `{site}` token (e.g. a plain `https://sandbox.example.com`) is used as-is and `Site` is simply unread — no need to also set `Site` in that case. | `Servers/ProductionOptions.cs`, `Server.cs` (source, confirmed) |
| `ServerOptions` namespace | `MaxioAdvancedBilling` (root) — declared at repo root, not under `Servers/` | `ServerOptions.cs` (source, confirmed) |
| `ProductionOptions` / nested `UsOptions` namespace | `MaxioAdvancedBilling.Servers` (the `Us`/`Eu` sub-options are nested classes: `ProductionOptions.UsOptions`) | `Servers/ProductionOptions.cs` (source, confirmed) |

### 2.2 Operations

| Controller.Method | Signature | Request model + fields | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | none (query `reference` ← `reference`) | `MaxioAdvancedBilling.Models.CustomerResponse.Customer` (`customer`, `!req`) → `MaxioAdvancedBilling.Models.Customer`: `Id (id): int?`, `Reference (reference): string?` | Case B `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — no match ⇒ `StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, !req`. `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string, !req`; `LastName (last_name): string, !req`; `Email (email): string, !req`; `Reference (reference): string?` — set to the internal userId for idempotent lookup; all other fields (`Organization`, `Address`, …) optional/unused here | `CustomerResponse.Customer` → `Customer.Id`, `.Reference` | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback, **non-422 statuses only** — for a 422 the two accessors are mutually exclusive and neither exposes a usable message; see §5] | none | `operations/Customers.md`; `records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`); `records-2-Cr-Ne.md` (`CustomerErrorResponse1`, `Errors`) |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`, each `.Subscription` → `MaxioAdvancedBilling.Models.Subscription` (fields below) | Case B `SdkException<RawError>` | none (returns full list) | `operations/Customers.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 nullable params (`dateField`…`include`) must all be passed explicitly (`null` to skip) | `productFamilyId` = **`"handle:" + Maxio:ProductFamilyHandle`** — confirmed by the operation's own XML doc: *"Either the product family's id or its handle prefixed with `handle:`"*. Passing the bare handle (no prefix) is a different, wrong contract. Pass `includeArchived: false` explicitly (server-side default when omitted is undocumented in the map) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`, each `.Product` → `MaxioAdvancedBilling.Models.Product` (fields below) | Case A `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404, family handle/id not found] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage`, defaults page=1/perPage=20 — page through if a family ever has >20 products (only 2 seeded today) | `operations/ProductFamilies.md`; `Api/ProductFamilies.cs` (source, confirmed, for the `handle:` prefix) |
| `client.Sites.ReadSite` | `ReadSite(CancellationToken ct = default)` | none | `MaxioAdvancedBilling.Models.SiteResponse.Site` (`site`, `!req`) → `MaxioAdvancedBilling.Models.Site`: `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`, `DefaultPaymentCollectionMethod (default_payment_collection_method): string?` — read once (cacheable) to decide the legal `PaymentCollectionMethod` value for a no-payment-profile `CreateSubscription` call, see next row | Case B `SdkException<RawError>` | none | `operations/Sites.md`; `records-3-Of-Su.md` (`Site`, `SiteResponse`) |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, !req`. `MaxioAdvancedBilling.Models.CreateSubscription` marks **no field `!req`** — per the operation's Notes plus the field-level doc comments in `Models/CreateSubscription.cs` (source, confirmed), set: `ProductHandle (product_handle): string?` = chosen plan handle; `CustomerId (customer_id): int?` = the `Customer.Id` resolved/created in step 3a; **`PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?`** = `CollectionMethod.Invoice` if `Site.RelationshipInvoicingEnabled` is `false`/absent, else `CollectionMethod.Remittance` (both are collection methods that do **not** attempt an automatic card charge at signup — this field is **required** in practice for a `RequireCreditCard:false`, non-zero-price, no-trial plan; see §5, this is not optional for this scenario even though the record marks it nullable); optionally `NetTerms (net_terms): string?` = `"0"` to make the invoice due immediately rather than after a grace period. Leave `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` **all null** (payment profile not required/supplied). Leave `ProductPricePointHandle`/`ProductPricePointId` null to use the product's default price point | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` (`!req`) → `Subscription`: `Id (id): int?`, `State (state): SubscriptionState?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Product (product): Product?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422, `Errors (errors): IReadOnlyList<string>, !req` — plain message list, safe to surface directly; this is the shape the live "No payment method was on file for the $299.00 balance" rejection came back as] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`, `ErrorListResponse1`); `records-4-Su-We.md` (`SubscriptionResponse`); `Models/CreateSubscription.cs` (source, confirmed, for `PaymentCollectionMethod`/`NetTerms`/`NextBillingAt` doc comments) |

### 2.3 Models used for the plan/subscription confirmation shape

`MaxioAdvancedBilling.Models.Product` fields used for capability 1 (list plans) — read straight off the
product returned by `ListProductsForProductFamily`, no separate price-point call needed for these
single-default-price-point plans (see §5 for the limits of that assumption):
`Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`,
`Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`,
`TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`,
`TrialIntervalUnit (trial_interval_unit): IntervalUnit?`, `TrialType (trial_type)` *(not on `Product`
itself — trial *type* lives on `ProductPricePoint`, not `Product`; `Product` only exposes
`TrialPriceInCents`/`TrialInterval`/`TrialIntervalUnit`, sufficient for "has a trial y/n" + cadence)*,
`InitialChargeInCents (initial_charge_in_cents): long?` (setup fee), `RequireCreditCard (require_credit_card): bool?`
(payment-method-required flag), `Taxable (taxable): bool?`, `ExpirationInterval (expiration_interval): int?` /
`ExpirationIntervalUnit (expiration_interval_unit): ExpirationIntervalUnit?` (both null/absent ⇒ never
expires — see §5).

Source: `records-3-Of-Su.md` (`Product`).

### 2.4 Enum value tables (only the ones this scope touches)

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`StringEnum<T>`, wire values in parens) —
source `enums.md` / `Models/Enums/SubscriptionState.cs`:

`Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`,
`Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`,
`Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`,
`TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.

`MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`StringEnum<T>`) — source `enums.md`:
`Day (day)`, `Month (month)`.

`MaxioAdvancedBilling.Models.Enums.ExpirationIntervalUnit` (`StringEnum<T>`) — source `enums.md`:
`Day (day)`, `Month (month)`, `Never (never)`.

These are `StringEnum<T>` records, **not** C# enums — compare via the static members
(`SubscriptionState.Active`) or `Type.FromValue("active")`, never a raw string `==`.

---

## 3. Trap notes

> ⚠ Step 1 (client registration) — `AddMaxioAdvancedBillingClient`'s `configure` callback runs **once**,
> at registration time, and its captured `MaxioAdvancedBillingClientOptions` is baked into a singleton
> client; whether/how the app needs to pick up a rotated `Maxio:ApiKey` or a config reload without an
> app restart is not handled by the extension method as written. **MUST load
> `dotnet-client-initialization`** before wiring the DI registration.

> ⚠ Step 1 (auth) — where exactly `BasicAuth` must be set relative to client construction, and how to
> load the key from configuration rather than hardcoding it, isn't obvious from the options shape alone.
> **MUST load `dotnet-authentication`**.

> ⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not**
> the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before
> wiring the client — this matters here because `CreateCustomer` and `CreateSubscription` are
> non-idempotent-shaped writes and a transport-level retry behaves differently from a status-code retry.

> ⚠ Step 2 (list plans) — `ListProductsForProductFamily` has 8 nullable params with no default that
> must all be passed explicitly by name, and paginates manually. Getting the argument list or the page
> loop wrong silently drops plans rather than erroring. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Step 3/4 (models) — enums are `StringEnum<T>` not C# enums, and unions/nullable reference handling
> have their own construction/read patterns; unmodeled or mismatched JSON fields are dropped silently on
> deserialize (directly relevant to the `CustomerErrorResponse1`/`Errors` mismatch in §5). **MUST load
> `dotnet-models`**.

> ⚠ Step 6 (tests) — the seam to fake is the `HttpClient` constructor argument, not the SDK's internal
> HTTP plumbing; match the project's existing test framework/assertion style rather than inventing a new
> one. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING

Load all of the following **before implementation starts** — this sheet deliberately does not carry
their contents:

- `dotnet-client-initialization` — governs Step 1 (client construction, DI registration, `HttpClient`
  ownership/lifetime).
- `dotnet-authentication` — governs Step 1 (Basic-auth credential wiring).
- `dotnet-configuration-resilience` — governs Step 1 (retry/timeout/base-URL semantics before tuning
  anything on the registered client).
- `dotnet-calling-endpoints` — governs Steps 2–4 (named-argument calling convention, pagination loop).
- `dotnet-models` — governs Steps 2–4 (building `CreateCustomer`/`CreateSubscription` request bodies,
  reading enums/response envelopes).
- `dotnet-error-handling` — governs Step 5 (the exception-translation boundary). Always required:

  Both hazard rows below apply verbatim to this integration — a drifted 2xx body or a malformed non-2xx
  error body reach the boundary as the **same** exception type from **opposite** causes and need
  opposite handling:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
    `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
    the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
    `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
    retries 5xx retries something that can never succeed.

  This is doubly relevant here: §5 already identifies one operation (`CreateCustomer`'s 422) whose typed
  error payload is a source-confirmed mismatch with no raw-body fallback available for that status — a
  live case where "no readable message reaches the boundary" is a real, source-confirmed outcome, not a
  hypothetical.
- `dotnet-testing` — governs Step 6 (test seam, assertion style).

---

## 5. Assumptions & Blockers

**Blockers**

- **`CreateCustomer` requires `FirstName`, `LastName`, `Email` — all `!req` (non-nullable) in
  `MaxioAdvancedBilling.Models.CreateCustomer`** (`records-1-Ac-Cr.md` / `Models/CreateCustomer.cs`). A
  bare internal userId/GUID is not sufficient to create a Maxio customer. The eShopOnWeb user's first
  name, last name, and email must be resolved (from the validated JWT claims and/or the app's own user
  store) before step 3a's `CreateCustomer` call can be made. This is a data-availability blocker, not an
  SDK gap — the map cannot supply defaults for required fields.

**Assumptions / trust caveats**

- **The typed 422 payload for `CreateCustomer` cannot carry a duplicate-reference message, and there is
  no raw-body fallback for that same 422 — the two accessors are mutually exclusive on this type.**
  Source-confirmed from `Errors/CreateCustomerError.cs`: `CreateCustomerError.Create` switches on the
  HTTP status code — for `422` it builds the error via `AsCustomerErrorResponse1(...)`, which passes
  `default` (empty `Optional<RawError>`) as the `fallback` to the `ApiError` base constructor; for every
  *other* status it builds via `AsFallback(...)`, which populates the `RawError` and leaves the typed
  `CustomerErrorResponse1` slot empty. `ApiError.TryGetRawError` just reads that same `Optional<RawError>`
  field (`Core/ErrorResponse/ApiError.cs`). **Consequence: for a real 422, `TryGetCustomerErrorResponse1`
  returns `true` and `TryGetRawError` returns `false` on that same exception — there is no raw-body
  fallback to read for a 422.** (For any non-422 status — e.g. 401/500 — it is the reverse:
  `TryGetCustomerErrorResponse1` is `false` and `TryGetRawError` is `true`/safe to read, per the normal
  Case-A pattern.) Combined with the separate, also source-confirmed fact that `CustomerErrorResponse1`
  wraps `MaxioAdvancedBilling.Models.Errors`, whose **only** two fields are `PerPage (per_page)` and
  `PricePoint (price_point)` (`Models/Errors.cs`) — nothing customer- or reference-related — **a 422 from
  `CreateCustomer` has no readable caller-facing message anywhere on the exception.** Concrete
  defensive-coding directive (supersedes the earlier draft of this note): on a
  `SdkException<CreateCustomerError>` whose `TryGetCustomerErrorResponse1` returns `true`, do not attempt
  to read a message off either accessor — surface a generic rejection message (e.g. "billing customer
  request was rejected") to the caller. Because the operation's own Notes state that reference-uniqueness
  is the *only* validation restriction on `CreateCustomer`, treat **any** 422 from this call as a possible
  duplicate-reference race and recover by re-calling `ReadCustomerByReference` before surfacing that
  generic error; if the recheck still finds no customer, the generic message is what the caller gets. For
  any *non-422* status on this same call, `TryGetRawError(out var raw)` **is** populated — use
  `raw.ReadAsString()` there for a best-effort message. The exact wire shape of a real duplicate-reference
  422 body is **UNVERIFIED** — only live traffic against the sandbox would show it, but it no longer
  matters for the implementation since neither accessor exposes it regardless of shape.
- **Idempotent customer creation is lookup-first + create-on-miss + recover-on-409/422, not a single
  atomic SDK guarantee.** The SDK exposes no create-if-absent customer operation. The pattern above
  relies on Maxio's server-side reference-uniqueness enforcement (documented in the operation's Notes,
  not independently load-tested) to reject the losing side of a true concurrent double-submit. Whether
  eShopOnWeb additionally needs its own request-level de-duplication (idempotency key, DB lock, etc.) on
  top of this is an application design decision — **YOUR CALL — not in the map**.
- **`RequireCreditCard: false` on a product does NOT mean `CreateSubscription` can enroll it with zero
  payment method when the plan is non-zero-price and has no trial — confirmed live and matches the
  documented field semantics, not an SDK bug.** `MaxioAdvancedBilling.Models.CreateSubscription`'s
  default `PaymentCollectionMethod` behavior is `Automatic` (attempt to charge a card immediately at
  signup); per `Models/CreateSubscription.cs`'s own doc comment on `NextBillingAt`: *"If you do not
  provide a value for next_billing_at, any trial and/or initial charges will be assessed and charged at
  the time of subscription creation. If the card cannot be successfully charged, the subscription will
  not be created."* With no trial and a non-zero price, that immediate assessment has a non-zero balance
  due, and with no payment profile on file, `Automatic` collection has nothing to charge it to — this
  is the documented cause of the live 422 (`"No payment method was on file for the $299.00 balance"`),
  not a gap between `require_credit_card` and enrollment. **The documented, supported alternative is the
  `PaymentCollectionMethod` field itself** (`payment_collection_method`, enum
  `MaxioAdvancedBilling.Models.Enums.CollectionMethod`: `Automatic`, `Remittance`, `Prepaid`, `Invoice` —
  `enums.md` / `Models/Enums/CollectionMethod.cs`), whose own doc comment on
  `CreateSubscription.PaymentCollectionMethod` states: *"For legacy Statements Architecture valid options
  are - invoice, automatic. For current Relationship Invoicing Architecture valid options are -
  remittance, automatic, prepaid."* `Invoice`/`Remittance`/`Prepaid` do not attempt an automatic card
  charge at signup — they are Maxio's documented non-card billing methods, which is the "equivalent to
  invoicing instead of immediate card assessment" the coordinator asked for. §2.2's `CreateSubscription`
  row now requires this field for a `RequireCreditCard:false`, non-zero-price, no-trial plan. Which of
  `Invoice`/`Remittance` is legal is **site-specific**, not hardcodable: read
  `client.Sites.ReadSite().Site.RelationshipInvoicingEnabled` (added to §2.2 as a new row) — `false`/absent
  ⇒ legacy Statements Architecture ⇒ use `Invoice`; `true` ⇒ current Relationship Invoicing Architecture ⇒
  use `Remittance`. **Which one the live `cp-exp-1` sandbox actually is is UNVERIFIED from the map/source
  alone — call `ReadSite()` once to confirm before hardcoding either value.** `NetTerms` (`net_terms`,
  `"0"`–`"180"` as a string) optionally controls how many days after signup the resulting invoice is due;
  pass `"0"` for due-immediately. To directly answer the closing either/or in the question: neither (a)
  collecting a payment profile nor (b) restricting non-payment-required signups to \$0/trial plans is
  actually required by the API — (c) setting `PaymentCollectionMethod` to the site's non-`Automatic`
  invoicing method is the documented third path and is what this plan now specifies.
- Nothing in the map documents subscription-level de-duplication (e.g. rejecting a second subscription
  for the same customer/plan pair). If double-subscribing to the same plan must be prevented, that
  enforcement is an application decision — **YOUR CALL — not in the map**.
- `Product.PriceInCents`/`Interval`/`IntervalUnit`/`TrialPriceInCents`/`InitialChargeInCents` are read as
  the product's own (default-price-point-mirroring) fields, per `records-3-Of-Su.md`. This is sufficient
  for the two seeded single-price-point plans (`eshop-pro`, `basic-plan`) in scope. If a plan later needs
  multiple *non-default* price points surfaced, that requires a separate `ProductPricePoints` call — out
  of scope for this plan.
- "Never expires" is read as `Product.ExpirationInterval` being null/absent. The map does not state
  whether Maxio ever sets `ExpirationIntervalUnit.Never` explicitly instead of leaving both fields null;
  treat either as "never expires" defensively.
- `includeArchived: false` is passed explicitly to `ListProductsForProductFamily` rather than relying on
  an undocumented server-side default for the omitted case — **YOUR CALL — not in the map**, but a
  zero-risk direction (explicit beats implicit here).
