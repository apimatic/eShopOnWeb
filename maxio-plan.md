# Maxio Advanced Billing — Subscription Billing Plan (eShopOnWeb PublicApi)

## 1. Scope & sequence

1. **Package + DI + config binding.** Add NuGet package `AsadAli.AdvancedBilling.Sdk` (root
   namespace `MaxioAdvancedBilling`) to the project that will host the Maxio client. Bind a
   `Maxio:` options object (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl?`) and register
   the client via `services.AddMaxioAdvancedBillingClient(...)`. No operations used yet.
2. **GET /api/subscription-plans** — `client.ProductFamilies.ListProductFamilies` (resolve the
   family by `Maxio:ProductFamilyHandle`) → `client.ProductFamilies.ListProductsForProductFamily`
   (the family's numeric id) → project each `Product` to a plan DTO (handle, name, price,
   interval).
3. **POST /api/subscriptions** —
   a. find-or-create customer: `client.Customers.ReadCustomerByReference` →
      `client.Customers.CreateCustomer` on 404.
   b. duplicate-prevention check: `client.Customers.ListCustomerSubscriptions` for an existing
      live subscription to the chosen product before creating a new one.
   c. enroll: `client.Subscriptions.CreateSubscription` (no payment fields — both seeded plans
      don't require a payment method).
   d. shape the response: plan name/handle, price, `Subscription.State`, `Subscription.NextAssessmentAt`.
4. **GET /api/my-subscriptions** — resolve the customer by reference (as in 3a; treat "no
   customer yet" as an empty list, not an error) → `client.Customers.ListCustomerSubscriptions`.
5. **Error boundary + tests** — one exception-translation layer used by all three endpoints;
   tests for the integration layer.

A capability the map doesn't offer (atomic server-side dedupe for subscription creation) is
recorded as a **Blocker** in §5 — not invented as a data path.

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

### 2.1 Client construction / DI (namespaces confirmed from source: `Server.cs`,
`ServerOptions.cs` → root `MaxioAdvancedBilling`; `Servers/ProductionOptions.cs`,
`Servers/ServerEnvironment.cs` → `MaxioAdvancedBilling.Servers`; `BasicAuthCredentials` →
`MaxioAdvancedBilling.Core.Authentication.Basic`)

| Config key | Bound to | Type / namespace | Notes | Source |
|---|---|---|---|---|
| `Maxio:ApiKey` | `options.BasicAuth = new BasicAuthCredentials { Username = <value>, Password = "x" }` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` (on `MaxioAdvancedBillingClientOptions`, root ns `MaxioAdvancedBilling`) | Password is the **literal string `"x"`**, not a secret. | sdk-map.md §"Servers & auth" |
| `Maxio:Subdomain` | `options.Server.Production.Us.Site = <value>` | `MaxioAdvancedBilling.ServerOptions` → `MaxioAdvancedBilling.Servers.ProductionOptions` → nested `UsOptions.Site: string` (default `"subdomain"`) | Only meaningful if `Us.BaseUrl` still contains the `{site}` token (see next row). | sdk-map.md §"Servers & auth"; confirmed in source `Servers/ProductionOptions.cs` |
| `Maxio:BaseUrl` (optional) | when present: `options.Server.Production.Us.BaseUrl = <value>` verbatim | `MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions.BaseUrl: string` (default `"https://{site}.chargify.com"`) | **Source-confirmed**: the default template contains a `{site}` placeholder that `UrlTemplate`/`TemplateParam.ForServer("site", Us.Site)` substitutes. If the override value has no `{site}` token, `Maxio:Subdomain`/`Us.Site` has **no effect** on the resolved host — the override is used exactly as given. | `Servers/ProductionOptions.cs` (source, on real gap — map's "Servers & auth" table names the override point but not the substitution mechanics) |
| `Maxio:ProductFamilyHandle` | NOT part of client construction — used at call time to resolve the family (see §2.2 row 1) | `string` | — | YOUR CALL — not in the map (this is app config, not an SDK construction fact) |
| `options.Environment` | leave at default | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) | Sandbox site `cp-exp-1` uses the `.chargify.com`-style production template implied by the `Maxio:BaseUrl` description — default `Us` is the documented default and is not overridden by anything in this brief. | sdk-map.md §"Servers & auth" |
| Registration | `services.AddMaxioAdvancedBillingClient(o => { ... })` | `MaxioAdvancedBilling.ServiceCollectionExtensions` (extension method on `IServiceCollection`) | Registers the client as a **Singleton** built from one `IHttpClientFactory.CreateClient()` call made at registration time (confirmed in source `ServiceCollectionExtensions.cs`) — what this means for the `HttpClient`'s lifetime/reuse is exactly the trap `dotnet-client-initialization` covers; do not resolve it here. | sdk-map.md §"Getting a client"; `ServiceCollectionExtensions.cs` |

### 2.2 Operations

| # | Controller property · method | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — **all 5 non-ct params have no default; pass `null` explicitly (named args) to skip them.** | none (query-only) | `IReadOnlyList<ProductFamilyResponse>`, each `.ProductFamily` (nullable) → `Id: int?`, `Name: string?`, `Handle: string?`. **Filter client-side**: find the entry whose `Handle == Maxio:ProductFamilyHandle`, take its `Id`. | Case B `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `map/operations/ProductFamilies.md`; `map/models/records-3-Of-Su.md` |
| 2 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` = the numeric `Id` from row 1 (`.ToString()`); the other 8 non-defaulted params must be passed explicitly (`null` to skip). | none (query-only) | `IReadOnlyList<ProductResponse>`, each `.Product` (`!req`) → `Id: int?`, `Name: string?`, `Handle: string?`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: IntervalUnit?`, `RequestCreditCard: bool?`, `RequireCreditCard: bool?`. Map each to the plan DTO: name/handle/price (`PriceInCents / 100.0`)/interval. `RequireCreditCard`/`RequestCreditCard` are the fields confirming payment is not mandatory. | Case A `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 20 covers the 2 seeded plans; loop if the family ever exceeds a page) | `map/operations/ProductFamilies.md`; `map/models/records-3-Of-Su.md` |
| 3 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` = the stable eShopOnWeb user id (see §5 for why reference, not email). | none | `CustomerResponse.Customer` (`!req`) → `Id: int?`, `FirstName/LastName/Email: string?`, `Reference: string?`. | Case B `SdkException<RawError>` — check `StatusCode == HttpStatusCode.NotFound` to mean "no customer yet"; anything else is a real error. | none | `map/operations/Customers.md`; `map/models/records-1-Ac-Cr.md` |
| 4 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body.Customer` = `CreateCustomer { FirstName !req, LastName !req, Email !req, Reference: <same stable id> }`. | `CreateCustomerRequest.Customer: CreateCustomer !req`; required: `FirstName`, `LastName`, `Email`; `Reference` optional but **must be set** to the idempotency key (see §5). Notes on this operation state: *"you may only create one customer for a given reference value... the `reference` value must be unique"* — this is the documented, server-enforced idempotency mechanism. | `CustomerResponse.Customer !req` → same fields as row 3. | Case A `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. **`CustomerErrorResponse1.Errors` is `Models/Errors.cs`, which only exposes `PerPage`/`PricePoint` string-array fields — unrelated to a customer/reference conflict.** Confirmed in source: this typed 422 payload cannot be used to detect "reference already taken" structurally. Defensive directive: on 422, re-call row 3 (`ReadCustomerByReference`) to fetch the customer a concurrent request just created, rather than parsing the typed error body; only fall back to `TryGetRawError().ReadAsString()` for logging. | none | `map/operations/Customers.md`; `map/models/records-1-Ac-Cr.md`; `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs` (source, real gap — map's field list already showed this but the *inference* that it's unusable for conflict detection needed the source read) |
| 5 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — used both for the duplicate-prevention pre-check (step 3b) and for `GET /api/my-subscriptions`. | none | `IReadOnlyList<SubscriptionResponse>`, each `.Subscription` (nullable) → `Id: int?`, `State: SubscriptionState?`, `Product: Product?` (→ `.Handle`, `.Name`, `.PriceInCents`, `.Interval`, `.IntervalUnit`), `NextAssessmentAt: DateTimeOffset?`, `CurrentPeriodEndsAt: DateTimeOffset?`, `Reference: string?`. | Case B `SdkException<RawError>` | none (returns the full list) | `map/operations/Customers.md`; `map/models/records-4-Su-We.md` |
| 6 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body.Subscription` = `CreateSubscription { ... }`. **CORRECTED (live sandbox test against `cp-exp-1`):** the earlier guidance to omit all payment fields because `RequireCreditCard`/`RequestCreditCard` = false was wrong in effect. Those two flags (row 2) only mean the API does **not require** payment-profile fields to be *present in the request* — they do **not** mean the product is charge-free at signup. A product with a non-zero price still triggers an **immediate assessment** at `CreateSubscription` time; with no payment profile on file that assessment fails as a 422 (`ErrorListResponse1.Errors` message text `"No payment method was on file for the $<amount> balance"`), confirmed live for `eshop-pro`. **Full field list** (`Models/CreateSubscription.cs`, none are C# `required`): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomPrice (custom_price): SubscriptionCustomPrice?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `NetTerms (net_terms): string?`, `CustomerId (customer_id): int?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false`, `StoredCredentialTransactionId (stored_credential_transaction_id): int?`, `SalesRepId (sales_rep_id): int?`, `PaymentProfileId (payment_profile_id): int?`, `Reference (reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?`, `BankAccountAttributes (bank_account_attributes): BankAccountAttributes?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `CalendarBilling (calendar_billing): CalendarBilling?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`, `CustomerReference (customer_reference): string?`, `Group (group): GroupSettings?`, `Ref (ref): string?`, `CancellationMessage (cancellation_message): string?`, `CancellationMethod (cancellation_method): string?`, `Currency (currency): string?`, `ExpiresAt (expires_at): DateTimeOffset?`, `ExpirationTracksNextBillingChange (expiration_tracks_next_billing_change): string?`, `AgreementTerms (agreement_terms): string?`, `AuthorizerFirstName (authorizer_first_name): string?`, `AuthorizerLastName (authorizer_last_name): string?`, `CalendarBillingFirstCharge (calendar_billing_first_charge): string?`, `ReasonCode (reason_code): string?`, `ProductChangeDelayed (product_change_delayed): bool?`, `OfferId (offer_id): OfferId?` (union), `PrepaidConfiguration (prepaid_configuration): UpsertPrepaidConfiguration?`, `PreviousBillingAt (previous_billing_at): DateTimeOffset?` (source: *"Can only be used if next_billing_at is also passed"*), `ImportMrr (import_mrr): bool?`, `CanceledAt (canceled_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `AgreementAcceptance (agreement_acceptance): AgreementAcceptance?`, `AchAgreement (ach_agreement): AchAgreement?`, `DunningCommunicationDelayEnabled (dunning_communication_delay_enabled): bool? = false`, `DunningCommunicationDelayTimeZone (dunning_communication_delay_time_zone): string?`, `SkipBillingManifestTaxes (skip_billing_manifest_taxes): bool?`. **Payment-deferral fields relevant to "no payment method on file" (source doc-comments, `Models/CreateSubscription.cs`):** • `NextBillingAt` — *"If you provide a next_billing_at timestamp that is in the future, no trial or initial charges will be applied when you create the subscription. In fact, no payment will be captured at all. The first payment will be captured... near the time specified by next_billing_at."* This is the **documented** way to create a subscription with zero immediate charge attempt, independent of whether a payment profile exists. • `InitialBillingAt` — defers the subscription into **Awaiting Signup** state until this future date; doc-comment: *"If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled"* — this defers *when* payment is attempted but, unlike `NextBillingAt`, the source does not state creation itself is charge-free. • `DeferSignup` (default `false`) — creates the subscription in **Awaiting Signup Date** state with an *unknown* first billing date (no date supplied); the doc-comment does not state a payment-at-creation guarantee either way. • `PaymentCollectionMethod` (`CollectionMethod?`: `Automatic`/`Remittance`/`Prepaid`/`Invoice`, `map/models/enums.md`) — `Invoice` = legacy Statements Architecture, `Remittance` = current Relationship Invoicing Architecture; both are non-card collection modes, but **neither the map's enum summary nor the source doc-comment on this field states whether it suppresses the immediate assessment** on a subscription with a non-zero price point — this is a real map/source gap, label `UNVERIFIED` if relied on for that purpose (see §5). | `CreateSubscriptionRequest.Subscription: CreateSubscription !req`. Identify the product via `ProductHandle: string?` **or** `ProductId: int?`, and the customer via `CustomerId: int?` **or** `CustomerReference: string?`. `Reference: string?` is the subscription's own reference (source doc-comment: *"The reference value (provided by your app) for the subscription itself"* — no uniqueness statement, unlike `CreateCustomer.Reference`; see §5 UNVERIFIED). | `SubscriptionResponse.Subscription?` → `Id`, `State: SubscriptionState?`, `NextAssessmentAt: DateTimeOffset?`, `Product: Product?` (name/handle/price/interval). | Case A `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`ErrorListResponse1.Errors: IReadOnlyList<string>` — flat message strings, no structured field; confirmed live to carry `"No payment method was on file for the $<amount> balance"` for an unpaid immediate-assessment failure), `TryGetRawError(out RawError)` [fallback]. Defensive directive (UNVERIFIED, see §5): best-effort scan the message strings for a reference/duplicate-subscription hint **or** a "no payment method"/balance-due hint; otherwise surface the generic list to the caller. | none | `map/operations/Subscriptions.md`; `map/models/records-2-Cr-Ne.md`; `map/models/enums.md`; `Models/CreateSubscription.cs` (source, confirms no uniqueness doc on `Reference` and the `NextBillingAt`/`InitialBillingAt`/`DeferSignup`/`PaymentCollectionMethod` doc-comments above) |
| 7 | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — optional, only if you want a direct reference-based re-lookup instead of re-scanning `ListCustomerSubscriptions`. | none | `SubscriptionResponse.Subscription?` (same shape as row 6) | Case A `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404], `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md` |

### 2.3 Enum values used

| Enum | Namespace | Values (C# member (wire)) | Source |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `map/models/enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` | `map/models/enums.md` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `map/models/enums.md` |

Not a C# `enum` — built via `Type.FromValue("wire")` or the static members (`SubscriptionState.Active`, not `SubscriptionState.active`). Do not hardcode which state a fresh no-payment-method subscription lands in — read `State` from the response (see §5, UNVERIFIED).

---

## 3. Trap notes

⚠ Step 1 (client registration) — `AddMaxioAdvancedBillingClient` registers the SDK client (and
the single `HttpClient` behind it) as a **Singleton** built once at startup, not per-request —
whether that's safe for this `HttpClient`'s lifetime (DNS reuse, socket exhaustion) needs
checking against how the rest of `PublicApi` handles outbound HTTP. **MUST load
`dotnet-client-initialization`** before wiring the client into DI.

⚠ Step 1 (auth) — `Password = "x"` is a fixed literal, not something to source from
configuration or treat as secret-shaped; getting this wrong (e.g. binding it to another config
key) breaks every call with a 401. **MUST load `dotnet-authentication`** before setting
`BasicAuth`.

⚠ Steps 2–4 (every list/lookup call) — several operations here take 5–9 nullable parameters
with no C# default (`ListProductFamilies`, `ListProductsForProductFamily`, `ListCustomers`) —
a positional call silently mis-binds them. **MUST load `dotnet-calling-endpoints`** before
writing the first call.

⚠ Steps 2–4 (every response) — every response type on this sheet wraps its payload one level
down (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`,
`ProductFamilyResponse.ProductFamily`) and several of those inner properties are themselves
nullable (`SubscriptionResponse.Subscription?`, `ProductFamilyResponse.ProductFamily?`) — a
direct read without a null-check throws where the map already shows it's optional. **MUST load
`dotnet-models`** before mapping any of these onto your own DTOs.

⚠ Step 3 (create subscription / create customer error bodies) — this SDK's typed 422 payloads
are not uniformly trustworthy (row 4's `CustomerErrorResponse1` is a concrete, source-confirmed
example: it exposes fields that have nothing to do with the customer resource). Whether a given
typed error accessor actually carries the field you need must be checked per-operation before
you build conflict-detection logic on it, not assumed from the type name. **MUST load
`dotnet-error-handling`.**

⚠ Step 3 (resilience) — a transport-level failure during `CreateSubscription` (a genuinely
non-idempotent write) is retried by the SDK's own retry policy regardless of HTTP verb, and
nothing in `RetryOptions` disables that. Whether the duplicate-prevention check in step 5 of §2.2
is enough to make a retried `CreateSubscription` safe is exactly the kind of consequence this
skill spells out. **MUST load `dotnet-configuration-resilience`** before finalizing the retry
configuration used for this client.

⚠ Step 5 (tests) — the DI registration builds the SDK client around one captured `HttpClient`; the
test seam is the `HttpClient` handed to `MaxioAdvancedBillingClient`'s constructor, not something
deeper. **MUST load `dotnet-testing`** before writing tests for any of these three endpoints.

---

## 4. REQUIRED READING

Load all of these before implementation starts — this sheet deliberately does not carry their
contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client/`HttpClient` construction and DI registration lifetime |
| `dotnet-authentication` | Step 1 — Basic auth credential wiring |
| `dotnet-calling-endpoints` | Steps 2–4 — calling every list/lookup/create operation, named-argument discipline |
| `dotnet-models` | Steps 2–4 — response envelope unwrapping, nullability, enum construction |
| `dotnet-error-handling` | Steps 3–4 — the try/catch boundary around `CreateCustomer`/`CreateSubscription`/every list call |
| `dotnet-configuration-resilience` | Step 1/3 — retry/timeout tuning, what a retried non-idempotent write means here |
| `dotnet-testing` | Step 5 — testing the integration layer |

Both hazard rows below are mandatory in this first sheet (not deferred to a revision), because
the error boundary is written early:

- a drifted or malformed **2xx** body (a missing `required` member on any of `CustomerResponse`,
  `ProductResponse`, `ProductFamilyResponse`, `SubscriptionResponse`, or their nested `!req`
  fields like `CreateCustomerRequest.Customer`/`CreateSubscriptionRequest.Subscription`) surfaces
  as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — an
  SDK-exception-only catch ladder on any of the six operations in §2.2 lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps
  every `JsonException` to a 5xx then reports a deterministic rejection (e.g. a genuinely
  malformed 422 from `CreateSubscription`) as an outage, and a caller that retries 5xx retries
  something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions:**

- Caller identity → the stable Maxio customer `reference` is resolved from the app's own
  authenticated-user identity path (JWT claim / user id). Which claim, and its exact string form,
  is `YOUR CALL — not in the map`: this plan only requires that whatever value is chosen is
  stable and unique per eShopOnWeb user, because that value becomes the input to §2.2 rows 3–4.
- `Maxio:ProductFamilyHandle` resolution goes through `ListProductFamilies` + client-side filter
  (§2.2 row 1) rather than passing the handle string straight into `ListProductsForProductFamily`'s
  `productFamilyId` parameter. The map's only textual evidence about family lookup-by-handle
  (`ReadProductFamily`'s Notes: *"can be specified either with the id number, or with the
  `handle:my-family` format"*) is attached to an operation whose C# signature takes `int id` —
  i.e. that note cannot literally apply to that method's own parameter, so this plan does not
  extend that "`handle:`-prefix" convention to `ListProductsForProductFamily`'s `string
  productFamilyId` without evidence. Resolving the family id via `ListProductFamilies` first
  avoids needing that assumption at all.
- Package location: no opinion is asserted about existing `PublicApi`/`Infrastructure` project
  boundaries (this agent has not read that repo structure in plan mode). Generic guidance only:
  since this is called from JWT-authenticated HTTP endpoints and is additive, either (a) add the
  package directly to `src/PublicApi` if there's no existing external-integration project, or
  (b) if the solution already isolates outbound integrations in an `Infrastructure`-style
  project, put the Maxio client/wrapper there and reference it from `PublicApi`. The actual
  choice is `YOUR CALL — not in the map`.

**Blockers / gaps (stated plainly, not invented around):**

- **No documented atomic duplicate-prevention for subscription creation.** Unlike
  `CreateCustomer` (whose Notes explicitly state `reference` is enforced unique server-side),
  neither `CreateSubscription`'s operation Notes nor its `Reference` field's source doc-comment
  (`Models/CreateSubscription.cs`: *"The reference value (provided by your app) for the
  subscription itself"*) state that the subscription `reference` is enforced unique. The only
  duplicate-prevention this SDK/API documents for subscriptions is the **application-level**
  check in §2.2 row 5 (`ListCustomerSubscriptions`, scan for an existing live subscription to the
  same product before calling `CreateSubscription`) — which has a genuine race window between two
  concurrent POSTs. **UNVERIFIED**: whether the server additionally rejects a second
  `CreateSubscription` carrying a `Reference` that collides with an existing subscription's
  reference can only be confirmed by live traffic against `cp-exp-1`. Defensive-coding directive
  captured in §2.2 row 6: set `Reference` deterministically anyway (cheap insurance, enables
  `FindSubscription` reference lookups later), keep the `ListCustomerSubscriptions` pre-check as
  the primary guard, and treat a 422 from `CreateSubscription` as "possibly already exists" —
  re-run the `ListCustomerSubscriptions` check and return the existing subscription if now found,
  rather than assuming the 422 means outright failure. A true fix for the race itself (e.g. a
  request-level idempotency guard in the application, independent of Maxio) is `YOUR CALL — not
  in the map`.
- **Post-creation subscription state for a no-payment-method product is UNVERIFIED.** The map
  gives the full `SubscriptionState` value set (including `AwaitingSignup`, `Active`, `Trialing`)
  but nothing in the map or source asserts which one a fresh `CreateSubscription` call lands on
  when the target product has `RequireCreditCard`/`RequestCreditCard` = false. Defensive-coding
  directive: the endpoint must return whatever `State` the SDK actually reports, not assume
  `Active`.
- **`RequireCreditCard`/`RequestCreditCard` = false does NOT mean charge-free — confirmed live
  against `cp-exp-1`.** Live test: creating a subscription to `eshop-pro` (`RequireCreditCard` =
  `false`, `RequestCreditCard` = `false`, confirmed via row 2) with no payment fields threw
  `SdkException<CreateSubscriptionError>` / `TryGetErrorListResponse1` = `["No payment method was
  on file for the $299.00 balance"]`. Those two flags only govern whether the API *requires*
  payment-profile fields in the request body; they say nothing about whether the product's price
  point triggers an immediate assessment at signup — for a priced product it does, and that
  assessment fails hard with no payment profile on file. **This plan's step 3c must not create a
  subscription with all payment fields omitted for a priced product without a payment profile.**
  The map/source document exactly one field that defers the *entire* initial-assessment attempt
  regardless of payment-profile presence: `NextBillingAt` (`CreateSubscription.NextBillingAt`,
  `Models/CreateSubscription.cs`) — set to a future date/time and, per its doc-comment, "no trial
  or initial charges will be applied... no payment will be captured at all" at creation time; the
  first assessment instead happens near `NextBillingAt`. `InitialBillingAt` and `DeferSignup` also
  defer the subscription's *state* (Awaiting Signup) but their doc-comments do not make the same
  charge-free-at-creation guarantee — treat those as `UNVERIFIED` for this purpose.
  `PaymentCollectionMethod = CollectionMethod.Invoice`/`.Remittance` selects a non-card billing
  mode but neither the map nor the source states it suppresses the immediate assessment — also
  `UNVERIFIED`. **Decision needed:** whether eShopOnWeb's product catalog should (a) always pass a
  future `NextBillingAt` for products with no payment profile so signup succeeds with billing
  deferred to that date, or (b) require a payment profile up front for any priced product
  regardless of `RequireCreditCard`/`RequestCreditCard`, or (c) something else — this is an
  application-design choice and is `YOUR CALL — not in the map`.
