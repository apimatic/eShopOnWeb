# Maxio Advanced Billing — Subscribe capability plan (eShopOnWeb PublicApi)

## 1. Scope & sequence

1. **Client registration & auth** — register `MaxioAdvancedBillingClient` in DI, bind `Maxio:ApiKey`,
   `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`. No SDK operations here.
2. **`GET /api/subscription-plans`** — uses `client.ProductFamilies.ListProductFamilies` (resolve the
   configured family by handle) then `client.ProductFamilies.ListProductsForProductFamily` (the two
   catalog plans `eshop-pro` / `basic-plan` live here as `Product` rows).
3. **`POST /api/subscriptions`** — find-or-create the Maxio customer
   (`client.Customers.ReadCustomerByReference` → `client.Customers.CreateCustomer` on miss), then
   `client.Subscriptions.CreateSubscription`.
4. **`GET /api/my-subscriptions`** — `client.Customers.ReadCustomerByReference` (same reference keying
   as step 3) → `client.Customers.ListCustomerSubscriptions`.
5. **Error boundary** — one throw-only catch ladder per operation, Case A vs Case B per the sheet below.

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

### Operations

| Controller property | Method signature | Request model + fields | Response envelope + fields read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>> ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 non-ct params are nullable with **no C# default → pass `null` explicitly** to skip each | none (query-only) | `MaxioAdvancedBilling.Models.ProductFamilyResponse.ProductFamily` (`MaxioAdvancedBilling.Models.ProductFamily?` — **not required**, null-check before reading): read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` — filter the returned list client-side for `Handle == <configured ProductFamilyHandle>` | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B**: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none — call returns the full list in one shot | `map/operations/ProductFamilies.md` |
| `client.ProductFamilies` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass the family's **numeric** `Id.ToString()` (see note below) for `productFamilyId`; the 8 params `dateField`…`include` are nullable with no default → pass `null` explicitly | none (query-only) | Each `MaxioAdvancedBilling.Models.ProductResponse.Product` (`MaxioAdvancedBilling.Models.Product !req`): read `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?` — filter for `Handle` ∈ {`eshop-pro`, `basic-plan`} | `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — **Case A**: `TryGetString(out string)` [404] · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | manual `page`+`perPage`, default `perPage=20` — with only 2 catalog plans one page is enough, but code must not silently truncate if the family's product list grows past 20 | `map/operations/ProductFamilies.md` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` | none (`reference` is a query param) | `MaxioAdvancedBilling.Models.CustomerResponse.Customer` (`MaxioAdvancedBilling.Models.Customer !req`): read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B**: `StatusCode` — **UNVERIFIED**: the map does not state the exact status on a lookup miss; treat a `404 NotFound` as "no customer yet → create one", and re-throw/surface any other status as a genuine failure (do not swallow non-404 statuses) | none | `map/operations/Customers.md` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `MaxioAdvancedBilling.Models.CreateCustomerRequest { Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req }`; `CreateCustomer` fields actually needed: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` — **set `Reference` to the caller's stable identifier to get the server-enforced uniqueness described below** | `MaxioAdvancedBilling.Models.CustomerResponse.Customer` (`MaxioAdvancedBilling.Models.Customer !req`): read `Id (id): int?`, `Reference (reference): string?` | `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — **Case A**: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] — **see trap note, the 422 payload shape is suspect** | none | `map/operations/Customers.md`, `map/models/records-1-Ac-Cr.md` (`CreateCustomer`), `map/models/records-2-Cr-Ne.md` (`CreateCustomerRequest`, `CustomerResponse`, `Customer`, `CustomerErrorResponse1`) |
| `client.Customers` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | Each `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` (`MaxioAdvancedBilling.Models.Subscription?` — **not required, null-check**) — see `Subscription` fields below | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B** | none — full list in one call | `map/operations/Customers.md` |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest { Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req }`; `CreateSubscription` has **no `!req` fields at all** — fields actually needed: `ProductHandle (product_handle): string?` (the chosen plan's handle), `CustomerReference (customer_reference): string?` (the same reference used for the customer — identifies an existing customer without resolving a numeric id), `Reference (reference): string?` (optional subscription-level reference — no documented uniqueness constraint, see trap note). **Confirmed**: `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` are all nullable/optional — the SDK model does **not** require any payment-method fields, matching the "no card required" catalog config | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` (`MaxioAdvancedBilling.Models.Subscription?` — **not required, null-check**): `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (the actual "next billing" timestamp per Subscriptions.md `UpdateSubscription` notes — `current_period_ends_at` is the period boundary, `next_assessment_at` is when the charge runs), `Product (product): MaxioAdvancedBilling.Models.Product?`, `Currency (currency): string?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` | `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — **Case A**: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] (`ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req`) · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | none | `map/operations/Subscriptions.md`, `map/models/records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`, `ErrorListResponse1`), `map/models/records-3-Of-Su.md` (`Subscription`, `SubscriptionResponse`, `Product`) |

⚠ **`ListProductsForProductFamily`'s `productFamilyId` parameter is a plain `string` inserted verbatim
into the URL template — the SDK places no constraint on its content, but this operation's own map
Notes never state that a `handle:my-family` value is accepted there** (that claim appears only in
`ReadProductFamily`'s Notes, and `ReadProductFamily`'s own signature takes `int id`, not a string — a
direct contradiction between that operation's prose and its signature). Do **not** pass the configured
`Maxio:ProductFamilyHandle` string directly as `productFamilyId`; resolve the family's numeric `Id` via
`ListProductFamilies` first (filter the returned list by `Handle`), then pass `Id.ToString()`.

### Enum value tables

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`StringEnum`) — literal C# member (wire value):
`Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`,
`Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`,
`Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`,
`TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.
Live/billable states worth surfacing to the shopper as "active-ish": `Active`, `Trialing`; everything
else reads as not-currently-billing. Source: `map/models/enums.md`.

`MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`StringEnum`): `Day (day)`, `Month (month)`.
Source: `map/models/enums.md`.

### Client construction / auth / server-node facts

- Client type: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` — the only constructor. DI helper:
  `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this IServiceCollection, Action<MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions>)`.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` — literal password `"x"`, username is the API key. No other auth field exists on the options type.
- Environment: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) or `.Eu`.
- Subdomain / base URL (both types are `MaxioAdvancedBilling.ServerOptions` at the client-options root, nested `MaxioAdvancedBilling.Servers.ProductionOptions` with **nested classes** `ProductionOptions.UsOptions` / `ProductionOptions.EuOptions`, all still under namespace `MaxioAdvancedBilling.Servers`):
  - `options.Server.Production.Us.Site = <Maxio:Subdomain>` — default literal value is `"subdomain"` (a placeholder, not a real default host); it substitutes into the `BaseUrl` template's `{site}` token.
  - Only if `Maxio:BaseUrl` is configured, override `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` (default template `"https://{site}.chargify.com"`); leave unset otherwise so the `{site}` substitution above governs.
  - All four properties (`Environment`, `Retry`, `Server`, `BasicAuth`) live directly on `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`.
- Source for all of the above: `sdk-map.md` "Getting a client" / "Servers & auth" sections (member paths verified against `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `MaxioAdvancedBillingClientOptions.cs`).

## 3. Trap notes

⚠ Step 1 (client registration) — the `HttpClient` passed into `MaxioAdvancedBillingClient`'s constructor
must be long-lived/factory-managed, and the client wrapper's own DI lifetime is a separate decision from
the `HttpClient`'s. **MUST load `dotnet-client-initialization`** before writing the registration.

⚠ Step 1 (auth) — credentials must be set before/at construction, and where the API key is read from
(configuration binding key, not a hardcoded literal) has its own conventions. **MUST load
`dotnet-authentication`** before wiring `BasicAuth`.

⚠ Step 2 & 3 (calling `ListProductsForProductFamily` / `ListCustomerSubscriptions` / `CreateSubscription`)
— several params in these signatures are nullable with no C# default, so a positional call silently
mis-binds them; call every multi-param operation with named arguments. **MUST load
`dotnet-calling-endpoints`.**

⚠ Step 2 & 3 (models) — `SubscriptionState` and `IntervalUnit` are `StringEnum<T>`, not C# `enum`s;
building or comparing them the C#-enum way (`==`, `switch` on raw values) does not behave like it looks.
**MUST load `dotnet-models`** before mapping `Product`/`Subscription` fields onto response DTOs.

⚠ Step 3 (customer create/find race) — on a `CreateCustomer` 422, `CustomerErrorResponse1.Errors`
deserializes into `MaxioAdvancedBilling.Models.Errors { PerPage, PricePoint }` — a record whose only two
fields (`per_page`, `price_point`) have nothing to do with customer attributes. This looks like a
generator artifact (the wrong example schema was captured for this error type), so the typed accessor
cannot be trusted to surface a "reference already taken" message via named fields.
**Defensive-coding directive (label: UNVERIFIED except for the field-shape mismatch, which is
source-confirmed):** on any `CreateCustomerError` 422, do not attempt to parse `CustomerErrorResponse1`
for the conflict reason — instead re-run `ReadCustomerByReference` with the same reference; if it now
resolves, use that customer (the 422 was the race — a concurrent request created it first); if it still
404s, surface the original error (via `TryGetRawError().ReadAsString()` for diagnostics) rather than
retrying blindly.

⚠ Step 4 (`NextAssessmentAt` vs `CurrentPeriodEndsAt`) — both are plausible "next billing date"
candidates on `Subscription`; `Subscriptions.md`'s own `UpdateSubscription` notes distinguish them
(`current_period_ends_at` is the period boundary you re-read to confirm a billing-date change,
`next_assessment_at` is the actual next-charge timestamp) — pick the one that matches what the UI is
meant to show; do not assume they are interchangeable.

## 4. REQUIRED READING

Load before implementation starts — the sheet above deliberately does not carry these skills' contents:

- `dotnet-client-initialization` — governs step 1 (client/DI registration, `HttpClient` lifetime).
- `dotnet-authentication` — governs step 1 (Basic-auth credential wiring, credential source).
- `dotnet-calling-endpoints` — governs steps 2–4 (named-argument calling convention for every
  multi-param operation above).
- `dotnet-models` — governs steps 2–4 (StringEnum construction/comparison, wire-name mapping,
  reading nullable response envelopes safely).
- `dotnet-configuration-resilience` — governs step 1 tuning (retry/timeout/base-URL/pagination
  semantics) if/when the default `RetryOptions` are touched.
- `dotnet-testing` — governs the test layer once written (which seam to fake for `MaxioAdvancedBillingClient`).
- `dotnet-error-handling` — governs step 5, the error boundary for **every** operation above.
  **MUST load before writing that boundary.** Two hazards that must shape it from the first draft,
  not a later revision:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the `JsonException`
    **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
    maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
    and a caller that retries 5xx retries something that can never succeed.

## 5. Assumptions & Blockers

**Blockers (capability the SDK does not expose, per the map/source read this session):**

- **No atomic idempotency mechanism for subscription creation.** `CreateSubscription`'s request model
  (`CreateSubscription`) has no idempotency-key field, and — unlike `CreateCustomer`, whose Notes state
  a server-enforced uniqueness constraint on `reference` — `CreateSubscription`'s Notes make no
  uniqueness claim about the subscription-level `Reference` field or about one customer having only one
  subscription per product. The only available mitigation is a non-atomic check-then-act:
  `ListCustomerSubscriptions(customerId)` to look for an existing subscription whose `Product.Handle`
  matches the requested plan and whose `State` is `Active`/`Trialing` before calling `CreateSubscription`.
  This narrows the double-click/retry window but does **not** close it (two concurrent requests can both
  pass the check before either creates). Surface this limitation rather than presenting the check as a
  guarantee.
- **Plan-level currency is not exposed by the operations in scope.** `Product` (returned by
  `ListProductsForProductFamily`) carries `PriceInCents` but no plain currency-code field; a currency
  value only appears on `MaxioAdvancedBilling.Models.ProductPricePoint.CurrencyPrices`
  (`CurrencyPrice.Currency`), which is populated only for multi-currency-enabled price points and is
  reached through the `ProductPricePoints` controller — not one of the operations named in scope. Until
  that controller is added to scope, `GET /api/subscription-plans` can return `PriceInCents`/interval but
  not a reliable currency code.

**Assumptions:**

- `Reference` (not email) is the customer-lookup key. `Customers.md`'s `CreateCustomer` Notes state the
  *only* server-enforced uniqueness constraint is on `reference`; `email` is listed only as one of several
  fuzzy `ListCustomers` search fields (org, id, name, email, reference), not a unique key. The plan
  therefore keys the Maxio customer on the caller's stable per-user identifier (passed into `Reference`
  on create and used with `ReadCustomerByReference` for lookups), with `Email` carried as a plain
  attribute. Which of the JWT's claims supplies that stable identifier is the application's own identity
  path — `YOUR CALL — not in the map`.
- Which numeric/string values back `Maxio:ProductFamilyHandle`, `Maxio:Subdomain` etc. at deploy time, and
  the exact ASP.NET Core configuration/binding mechanics for reading them, are the application's own
  concern — `YOUR CALL — not in the map`.
- The 404-on-miss behavior of `ReadCustomerByReference` (used to decide "create a new customer") is
  **UNVERIFIED** — the map documents this operation as Case B (`RawError`, status + raw body only) but
  does not state the exact status returned when no customer matches. Code defensively: treat
  `HttpStatusCode.NotFound` as "absent, create one"; treat every other status as a real failure to
  surface, not to swallow.
