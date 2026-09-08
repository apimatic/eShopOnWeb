# maxio-plan.md — Maxio Advanced Billing integration for eShopOnWeb (src/PublicApi)

SDK: NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · netstandard2.0 (runs on .NET 8) · APIMatic-generated, throw-only operations (no `…Result` no-throw variants).

---

## 1. Scope & sequence

Build order — each step names the SDK operations it uses:

1. **SDK wiring into `src/PublicApi`** — add the NuGet package; bind config section `"Maxio"` (keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional). No SDK values hard-coded.
2. **Client registration** — DI-register `MaxioAdvancedBillingClient` with Basic auth (username = API key, password = literal `"x"`), US environment, site subdomain, verbatim `BaseUrl` override when set. *(No operations — construction/config only.)*
3. **Error boundary** — one shared try/catch translation for every SDK call in the app (Case A/B ladder + `JsonException` handling per §4 hazards).
4. **Catalog resolution service** — resolve the product family **by handle** and list its products:
   `ProductFamilies.ListProductFamilies` (filter by `Handle` client-side) → `ProductFamilies.ListProductsForProductFamily` (by resolved family id). Single-plan detail: `Products.ReadProductByHandle`.
5. **User ↔ customer mapping store** — in-memory rows `(eshopUserId → maxioCustomerId)`; storage mechanism is the implementer's (in-memory DB acceptable; survives within a single run only).
6. **Idempotent ensure-customer** — `Customers.ReadCustomerByReference(eshopUserId)`; on 404 → `Customers.CreateCustomer(reference = eshopUserId)`. Cache the Maxio customer id in the mapping store.
7. **Idempotent ensure-subscription** — `Subscriptions.FindSubscription(reference = "{userId}:{planHandle}")`; on 404 → `Subscriptions.CreateSubscription(productHandle, customerReference, reference = "{userId}:{planHandle}")`. Cache the subscription id.
8. **HTTP endpoints** (JWT-authenticated; caller identity from the token):
   - `GET /api/subscription-plans` — step 4 ops.
   - `POST /api/subscriptions` — steps 6–7 ops; returns state/price/next-billing-date from the create response.
   - `GET /api/my-subscriptions` — map userId → Maxio customer id, then `Customers.ListCustomerSubscriptions(customerId)` (or `Subscriptions.ReadSubscription` per cached id).
9. **Build/run** — `DOTNET_ROLL_FORWARD=Major` (only .NET 10 SDK installed; `global.json` pins 8.0.x with `rollForward latestMajor`). In-memory database; no SQL Server.

Out of scope: the seeded metered component `api-call` ($0.01/unit) — no in-scope feature reads or allocates it. No usage-recording endpoint is planned.

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

⚠ **A request model may mark nothing required, and then `required?` selects nothing for you.**
`CreateSubscription` marks **nothing** required — the compiler catches nothing if you drop a
field. The fields below that the operation's Notes tie to acceptance are carried explicitly:
`product_id` **or** `product_handle` (product selection), `customer_id` **or**
`customer_reference` (customer selection), plus `reference` (our idempotency key).
Fields deliberately left out (all optional, not needed for card-less signup): `custom_price`,
`coupon_code(s)`, `payment_profile_id`, `credit_card_attributes`, `bank_account_attributes`,
`components`, `calendar_billing`, `metafields`, `next_billing_at`, `initial_billing_at`,
`defer_signup`, `currency`, and the remaining ~20 optional fields on the record.

### 2.1 Operations

| Use | Controller property · method signature | Request model + fields | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| Resolve family by handle | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — the 5 nullable params have **no default**, must pass explicitly (pass `null`; no filtering needed) | none | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily (product_family): ProductFamily?` → read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`; **match `Handle == Maxio:ProductFamilyHandle` client-side** | **B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` → `.StatusCode`, `.ReadAsString()` | none | `operations/ProductFamilies.md` |
| List plans in family | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` must pass explicitly (`null`); `productFamilyId` is a **string** — pass the resolved numeric id as string; `includeArchived: false` | none (query params only) | `IReadOnlyList<ProductResponse>` → `.Product (product): Product !req` → fields in §2.3 | **A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1/20 — 2 plans fit one page; loop-until-short-page if listing grows) | `operations/ProductFamilies.md` |
| Single plan by handle | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — pass plan handle (`eshop-pro`, `basic-plan`) | none | `ProductResponse` → `.Product` (§2.3) | **B** `RawError` — a missing handle is 404 on `.StatusCode` | none | `operations/Products.md` |
| Ensure-customer: lookup | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — pass the eShopOnWeb userId | none (query `reference`) | `CustomerResponse` → `.Customer (customer): Customer !req` → `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName` | **B** `RawError` — not-found is 404 on `.StatusCode` | none | `operations/Customers.md` |
| Ensure-customer: create | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` → `Customer (customer): CreateCustomer **required**` (§2.2) | `CustomerResponse` → `.Customer` → `Id`, `Reference` (echoes our userId) | **A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] — see drift note §2.5 | none | `operations/Customers.md` |
| Ensure-subscription: lookup | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable **no default**, must pass explicitly; pass `"{userId}:{planHandle}"` | none (query `reference`) | `SubscriptionResponse` → `.Subscription (subscription): Subscription?` (§2.3) | **A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] — a miss is 404 via `TryGetNoContent`, **not** the raw path | none | `operations/Subscriptions.md` |
| Subscribe | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription **required**` (§2.2) | `SubscriptionResponse` → `.Subscription` (§2.3) — state/price/next-billing-date come **from this response**, no extra read needed | **A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — `Errors (errors): IReadOnlyList<string> required` · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| Read one subscription | `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must pass explicitly; pass `null` | none | `SubscriptionResponse` → `.Subscription` (§2.3) | **B** `RawError` | none | `operations/Subscriptions.md` |
| List user's subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — pass the Maxio customer id from the mapping store | none | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` (§2.3) | **B** `RawError` | none | `operations/Customers.md` |

### 2.2 Request records (wire names in parens)

`MaxioAdvancedBilling.Models.CreateCustomerRequest`
| Field | Type, required? |
|---|---|
| `Customer (customer)` | `CreateCustomer`, **required** |

`MaxioAdvancedBilling.Models.CreateCustomer` — fields this integration sets:
| Field | Type, required? |
|---|---|
| `FirstName (first_name)` | `string`, **required** |
| `LastName (last_name)` | `string`, **required** |
| `Email (email)` | `string`, **required** |
| `Reference (reference)` | `string?` — **set to the eShopOnWeb userId.** The provider's own contract (operation Notes): *only one customer may exist per `reference` value*, and `reference` is the documented way to look a customer up from your app (`ReadCustomerByReference`). This is the idempotency anchor. |

Left out (optional, not needed): `CcEmails`, `Organization`, address fields, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`.

`MaxioAdvancedBilling.Models.CreateSubscriptionRequest`
| Field | Type, required? |
|---|---|
| `Subscription (subscription)` | `CreateSubscription`, **required** |

`MaxioAdvancedBilling.Models.CreateSubscription` — fields this integration sets (record marks **nothing** required):
| Field | Type, required? |
|---|---|
| `ProductHandle (product_handle)` | `string?` — plan handle from the user's choice (`eshop-pro` default; `basic-plan`) |
| `CustomerReference (customer_reference)` | `string?` — the eShopOnWeb userId; the operation Notes name it as an alternative to `customer_id`, so the subscribe call does not depend on the mapping store having a Maxio id yet |
| `Reference (reference)` | `string?` — set `"{userId}:{planHandle}"`; the dedupe key `FindSubscription` looks up (its Notes: "Finds a subscription by its reference") |

Left out: `ProductId`, `ProductPricePointHandle/Id` (default price point applies), `CustomPrice`, `PaymentProfileId`, `PaymentCollectionMethod` (leave unset — default; both plans need no card per the seeded site), `Components`, `Metafields`, `NextBillingAt`, and the rest of the optional fields listed in the warning above.

### 2.3 Response records — fields the integration reads

`MaxioAdvancedBilling.Models.Product` (inside `ProductResponse.Product (product): Product !req`)
| Field (wire) | Type |
|---|---|
| `Id (id)` | `int?` |
| `Handle (handle)` | `string?` |
| `Name (name)` | `string?` |
| `PriceInCents (price_in_cents)` | `long?` — $299.00/mo ⇒ `29900`, $29.00/mo ⇒ `2900` |
| `Interval (interval)` / `IntervalUnit (interval_unit)` | `int?` / `IntervalUnit?` |
| `RequireCreditCard (require_credit_card)` / `RequestCreditCard (request_credit_card)` | `bool?` — confirm the seeded "no card" assumption at runtime |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` — filter archived plans out of the listing |
| `ProductFamily (product_family)` | `ProductFamily?` — nested; its `Handle` cross-checks the family filter |

`MaxioAdvancedBilling.Models.Subscription` (inside `SubscriptionResponse.Subscription (subscription): Subscription?`):
| Field (wire) | Type | Read for |
|---|---|---|
| `Id (id)` | `int?` | subscription id (cache in mapping store) |
| `State (state)` | `SubscriptionState?` (enum §2.4) | status shown to user |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | recurring price |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | **next billing date** |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | secondary next-billing signal |
| `Product (product)` | `Product?` | plan name/handle/price confirmation |
| `Customer (customer)` | `Customer?` | customer id/reference echo |
| `Reference (reference)` | `string?` | our `"{userId}:{planHandle}"` key echo |
| `CreatedAt (created_at)` | `DateTimeOffset?` | enrolled-at display |

### 2.4 Enum values needed (`MaxioAdvancedBilling.Models.Enums`, `StringEnum<T>` — not C# enums; build with the static members, e.g. `SubscriptionState.Active`)

`SubscriptionState` — the subscription-state values a display must handle:
`Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.
(Note from the enum page: `assessing` is transient/internal — do not base access decisions on it.)

`CollectionMethod` (referenced by `CreateSubscription.PaymentCollectionMethod`, which we leave unset): `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`.

`SubscriptionInclude` (ReadSubscription's `include` param — we pass `null`): `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)`.

### 2.5 Client construction, auth, base URL

```csharp
using MaxioAdvancedBilling;                                    // client + options
using MaxioAdvancedBilling.Core.Authentication.Basic;          // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                            // ServerEnvironment (Server options per folder ⇒ same namespace)

var options = new MaxioAdvancedBillingClientOptions
{
    Environment = ServerEnvironment.Us,                        // default; US sandbox
    BasicAuth  = new BasicAuthCredentials                      // HTTP Basic — Username = API key, Password = literal "x"
    {
        Username = apiKey,                                     // from Maxio:ApiKey — never hard-coded
        Password = "x",
    },
};
options.Server.Production.Us.Site    = subdomain;              // Maxio:Subdomain → https://{site}.chargify.com
if (!string.IsNullOrEmpty(baseUrlOverride))                    // Maxio:BaseUrl — use VERBATIM as the API base address
    options.Server.Production.Us.BaseUrl = baseUrlOverride;    // e.g. a mock/dev host; overrides the derived URL
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

- The **only** client constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`; every API group is a property (`client.Customers`, `client.Subscriptions`, `client.Products`, `client.ProductFamilies` — all in `MaxioAdvancedBilling.Api`).
- DI alternative: `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`).
- `MaxioAdvancedBillingClientOptions` properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`).
- `RetryOptions` members are all `required` (`MaxioAdvancedBilling.Core.Configuration`) — start from `RetryOptions.Default()` rather than hand-building.

### 2.6 Exceptions and error reading

Every operation is **throw-only**. On an error status the SDK throws `SdkException<TError>` (source `Core/Exceptions/SdkException.cs`):

```csharp
try { var resp = await client.Customers.CreateCustomer(body, ct); }
catch (SdkException<CreateCustomerError> ex)          // Case A (typed)
{
    if (ex.Error.TryGetCustomerErrorResponse1(out var e422)) { /* 422 shape — see drift note */ }
    else if (ex.Error.TryGetRawError(out var raw))         { /* other statuses */ }
}
catch (SdkException<RawError> ex)                     // Case B (raw)
{
    var status = ex.Error.StatusCode;                 // HttpStatusCode
    var body   = ex.Error.ReadAsString();
}
```

- `RawError` (`MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`.
- `ApiError` base: `TryGetRawError(out RawError)`.
- Case A per operation is in the table above. 404-shaped lookups to branch on: `ReadCustomerByReference` (Case B — check `StatusCode == HttpStatusCode.NotFound`), `FindSubscription` (Case A — `TryGetNoContent`), `ListProductsForProductFamily` (Case A — `TryGetString`).
- **Drift note (map-visible):** `CustomerErrorResponse1.Errors` is typed `Errors?` whose only fields are `PerPage (per_page)` and `PricePoint (price_point)` — a shape that cannot represent a "reference already taken" message list. Whether the live 422 body matches this generated model is **UNVERIFIED**. Defensive directive: on `CreateCustomer` 422, extract best-effort; if the typed shape is absent or deserialization fails, fall back to `TryGetRawError`/generic message — and treat a 422 after a race as "duplicate reference": re-run `ReadCustomerByReference` and continue with the winner.
- The two `JsonException` hazard rows below are part of this boundary.

### 2.7 Idempotency contract facts (design consequences, decisions are the implementer's)

- **Customer:** the provider itself guarantees one customer per `reference` (`CreateCustomer` Notes), and `ReadCustomerByReference` is the exact-match lookup. So `reference = eShopOnWeb userId` makes ensure-customer idempotent at the provider level; a lost local cache is recoverable by lookup alone.
- **Subscription:** `FindSubscription(reference)` + `CreateSubscription.Reference = "{userId}:{planHandle}"` gives the same property: one subscription per (user, plan) pair, recoverable after restart of the in-memory store.
- **Race consequence:** two concurrent requests can both miss a cache-only check before either creates; the reference-based lookups above are the contract-level dedupe, and a loser of the race recovers via lookup (§2.6 drift note). How the app serializes per-user work (locking, `SemaphoreSlim`, …) is the implementer's call.

| Decision | Value | Label |
|---|---|---|
| Customer field carrying the eShopOnWeb userId | `CreateCustomer.Reference` / `Customer.Reference` | grounded: `operations/Customers.md` Notes |
| Subscription idempotency key | `CreateSubscription.Reference = "{userId}:{planHandle}"` + `FindSubscription` | grounded: `operations/Subscriptions.md` Notes |
| Mapping store storage mechanism (in-memory per run) | implementer's choice | YOUR CALL — not in the map |
| Endpoint DTOs, route shapes, JWT identity extraction | implementer's choice | YOUR CALL — not in the map |
| Live 422 bodies matching the generated error models | see §2.6 drift note | UNVERIFIED |
| Max length/format limits on `reference` values | not stated in the map; provider rejects invalid values via the documented 422/404 paths | UNVERIFIED |

### 2.8 Mandatory JsonException hazard rows

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

---

## 3. Trap notes

> ⚠ Step 2 (client registration) — the SDK's retry/timeout options do **not** bound a whole
> call and are **not** the timeout on the `HttpClient` you register; a transport failure is
> retried on every verb, `POST` included, so a non-idempotent write can execute more than once
> and no setting fully disables that — whether a failed subscribe can be re-sent safely depends
> on the reference-dedupe in §2.7. **MUST load `dotnet-configuration-resilience`** before
> wiring the client.

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline must be long-lived and
> reused via `IHttpClientFactory`, not rebuilt per request; the SDK client wrapper over it may
> be transient. **MUST load `dotnet-client-initialization`** before writing the factory/DI
> registration.

> ⚠ Step 2 (credentials) — Basic auth is username = API key, password = literal `"x"`; set
> credentials before/at client construction and read the key from the `"Maxio"` config
> section, never hard-coded. **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Step 4 (first call) — call list/search ops with **named arguments**: many optional params
> have no C# default and mis-bind positionally (e.g. `ListProductFamilies`' five nullables).
> **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Operation}(…)`.

> ⚠ Steps 6–7 (models) — enums are `StringEnum<T>`, not C# enums; wire names differ from C#
> property names; unmodeled JSON fields are silently dropped on deserialize. **MUST load
> `dotnet-models`** before constructing request payloads or reading response models.

> ⚠ Step 3 (error boundary) — Case A vs Case B per operation (§2.1), `TryGetRawError` is not a
> catch-all on typed errors, and both `JsonException` directions from §2.8 must be handled.
> **MUST load `dotnet-error-handling`** before writing any try/catch around an SDK call.

> ⚠ Step 8 (tests, if written) — the `HttpClient` constructor argument is the test seam; match
> the project's existing framework and assertion style. **MUST load `dotnet-testing`** before
> stubbing the SDK.

---

## 4. REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry these skills' contents:

- `dotnet-client-initialization` — step 2: DI registration, client/options shape, `HttpClient` ownership and lifetime.
- `dotnet-authentication` — step 2: Basic credential wiring, per-environment config, key from configuration.
- `dotnet-calling-endpoints` — steps 4–8: named arguments, envelopes, async/cancellation on every SDK call.
- `dotnet-models` — steps 6–7: request models, `StringEnum<T>`, wire names, required members.
- `dotnet-error-handling` — step 3: the exception boundary, Case A/B mechanics, both `JsonException` directions.
- `dotnet-configuration-resilience` — steps 2 & 9: retries, what `Timeout` bounds, base-URL/server selection, pagination.
- `dotnet-testing` — step 8 (if tests are written): the fake seam, covering error paths.

---

## 5. Assumptions & Blockers

**Assumptions**
1. The sandbox catalog is seeded as stated: family handle `eshop-subscribe`; plans `eshop-pro` ($299.00/mo) and `basic-plan` ($29.00/mo); component `api-call` ($0.01/unit, out of scope). The integration resolves by handle and can verify prices at runtime via `Product.PriceInCents`.
2. Both plans accept signup without a payment profile / 3-DS (stated in the brief). Runtime confirmation: `Product.RequireCreditCard` / `RequestCreditCard` on the read models; a live rejection would surface through `CreateSubscription`'s Case-A 422 path.
3. The eShopOnWeb userId is a stable string suitable as a Maxio `reference` value; length/format limits are not stated in the map (UNVERIFIED — an over-long value would 422 through the documented error path).
4. The JWT-authenticated endpoints resolve the caller's identity from the token; the mapping store and its concurrency rules are the implementer's design (YOUR CALL — not in the map).
5. Build/run uses `DOTNET_ROLL_FORWARD=Major` per the environment constraint; persistence is in-memory and survives only within a single run.

**Blockers**
- None. Every operation, model, enum, and error type the three endpoints need exists in the SDK map and is documented in §2.
