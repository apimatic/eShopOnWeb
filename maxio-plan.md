# Maxio Advanced Billing — Subscription Billing Plan (PublicApi)

Additive capability in `src/PublicApi`. Site: `cp-exp-1`. Product family handle:
`eshop-subscribe`. Plans: `eshop-pro` ($299.00/mo), `basic-plan` ($29.00/mo). Both plans have
`payment method NOT required` — no payment profile / card is sent on subscription creation.

## 1. Scope & sequence

1. **Client & DI registration** — register `MaxioAdvancedBillingClient` in `Program.cs`/DI,
   bound from configuration section `Maxio:` (`Maxio:ApiKey`, `Maxio:Subdomain`,
   `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`). No SDK operations yet.
2. **`GET /api/subscription-plans`** — uses `client.ProductFamilies.ListProductsForProductFamily`
   (products of the configured family) and `client.Sites.ReadSite` (site's default currency).
3. **`POST /api/subscriptions`** (hero flow) — uses:
   a. `client.Customers.ReadCustomerByReference` then, if absent, `client.Customers.CreateCustomer`
      (find-or-create-by-reference; reference = the eShopOnWeb user's stable identity).
   b. `client.Customers.ListCustomerSubscriptions` (pre-create existing-subscription check —
      app-level, see Blockers) then `client.Subscriptions.CreateSubscription` (no payment
      profile fields set).
   c. Response mapped from the returned `SubscriptionResponse`.
4. **`GET /api/my-subscriptions`** — `client.Customers.ReadCustomerByReference` (resolve the
   caller's Maxio customer id) then `client.Customers.ListCustomerSubscriptions`.

A capability the map does not provide (native idempotency key on subscription creation, native
upsert-by-reference on customers) is called out as a **Blocker** in §5, not invented.

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

### 2.1 Client construction / configuration / auth (all root namespace `MaxioAdvancedBilling` unless noted)

| Fact | Detail | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | sdk-map.md |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — members: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | sdk-map.md |
| Auth | `options.BasicAuth = new BasicAuthCredentials { Username = "<Maxio:ApiKey value>", Password = "x" }` — Basic auth, password is the literal string `"x"`, not a secret | sdk-map.md §Servers & auth |
| Subdomain-derived host (`Maxio:BaseUrl` NOT set) | `options.Server.Production.Us.Site = "<Maxio:Subdomain value>"` — leaves `options.Server.Production.Us.BaseUrl` at its SDK default `"https://{site}.chargify.com"` (namespace of `ServerOptions`/`ProductionOptions.UsOptions`: `ServerOptions` is `MaxioAdvancedBilling.ServerOptions` (root ns, file `ServerOptions.cs` at repo root); `ProductionOptions` and its nested `UsOptions`/`EuOptions` are `MaxioAdvancedBilling.Servers.ProductionOptions` (file `Servers/ProductionOptions.cs`)) | `ServerOptions.cs`, `Servers/ProductionOptions.cs` (confirmed in cloned source this session) |
| Explicit base-URL override (`Maxio:BaseUrl` set) | Set `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl value>"` **verbatim** — this replaces the whole templated base URL (the sdk-map's own example uses a plain host with no `{site}` placeholder, e.g. `"http://localhost:8080"`), so when this key is present do **not** also rely on `{site}` substitution — set `Us.Site` too only if the override URL still contains a `{site}` token, which is an application decision, not an SDK requirement | sdk-map.md §Servers & auth; `Servers/ProductionOptions.cs` |
| Environment | `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default; `.Eu` exists for EU-hosted sites — not used here since only `Us` fields are referenced above) | sdk-map.md |
| DI registration | Extension method `MaxioAdvancedBilling.ServiceCollectionExtensions` (root ns) — `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`. Internally it calls `services.AddHttpClient()` then registers the constructed `MaxioAdvancedBillingClient` as a **Singleton** built once from `IHttpClientFactory.CreateClient()` inside the registration lambda (not built per-request, not `IOptions<T>`-driven — the `configure` callback runs exactly once, at registration time) | `ServiceCollectionExtensions.cs` (confirmed in cloned source this session) |
| `ProductFamilyHandle` config value usage | Passed as `productFamilyId` (a plain `string`) to `ListProductsForProductFamily`, in the literal form `"handle:eshop-subscribe"` (see 2.2) — the `Maxio:ProductFamilyHandle` config value is the bare handle (`eshop-subscribe`); the `"handle:"` prefix is a call-site convention the integration must add, not part of the stored config value (**YOUR CALL** whether to store the bare handle or the prefixed form in config — either is fine as long as the prefix is added before the call) | `Api/ProductFamilies.cs` doc-comment (confirmed in cloned source this session) |

### 2.2 `GET /api/subscription-plans`

| Op | Signature | Request | Response envelope | Error | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `productFamilyId` = `"handle:" + <Maxio:ProductFamilyHandle>` (e.g. `"handle:eshop-subscribe"`) confirmed to accept either the numeric id or `handle:`-prefixed handle; all other params `null` unless filtering is wanted | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`, each `.Product` (`MaxioAdvancedBilling.Models.Product`, **not** `!req` — nullable, null-check before use) with: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?` (`Day`/`Month`), `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` | `SdkException<ListProductsForProductFamilyError>` — Case A. Accessors: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage`, defaults page=1/perPage=20 | `map/operations/ProductFamilies.md`; `map/models/records-3-Of-Su.md` (`Product`, `ProductResponse`) |
| `client.Sites.ReadSite` | `ReadSite(CancellationToken ct = default)` | none | `MaxioAdvancedBilling.Models.SiteResponse.Site` (`!req`, non-null) → `Currency (currency): string?` — this is the site's single default/primary currency for prices reported in cents on `Product`/`ProductPricePoint` (there is **no** `Currency` field on `Product` itself; multi-currency prices live only in `ProductPricePoint.CurrencyPrices`, which this plan does not use) | `SdkException<RawError>` — Case B | none | `map/operations/Sites.md`; `map/models/records-3-Of-Su.md` (`Site`, `SiteResponse`) |

Endpoint response shape (name, price, currency/interval): `name` = `Product.Name`, `price` =
`Product.PriceInCents / 100.0` formatted per `Site.Currency`, `interval`/`intervalUnit` =
`Product.Interval`/`Product.IntervalUnit`, `handle`/`id` = `Product.Handle`/`Product.Id`.

### 2.3 `POST /api/subscriptions` — customer ensure step (2a)

| Op | Signature | Request | Response | Error | Source |
|---|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `reference` = the stable string derived from the eShopOnWeb user's identity (**YOUR CALL — not in the map**: which claim/value to use and how to format it as the reference string) | `MaxioAdvancedBilling.Models.CustomerResponse.Customer` (`!req`) → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName`/`LastName (first_name/last_name): string?` | `SdkException<RawError>` — Case B (`ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`) | `map/operations/Customers.md`; `map/models/records-2-Cr-Ne.md` (`Customer`, `CustomerResponse`) |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest.Customer (customer): CreateCustomer !req` wrapping `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set to the same stable reference used above) | `CustomerResponse.Customer !req` → same `Customer` shape as above, read `.Id` for the new numeric customer id | `SdkException<CreateCustomerError>` — Case A. Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `map/operations/Customers.md`; `map/models/records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`); `map/models/records-2-Cr-Ne.md` (`CustomerErrorResponse1`) |

**Idempotent find-or-create mechanism — the exact, real one (no invention):** Maxio has **no
atomic upsert-by-reference** endpoint. The only two primitives are `ReadCustomerByReference`
(exact single-match lookup, per its Notes) and `CreateCustomer`, whose own Notes state: *"you may
only create one customer for a given reference value... the `reference` value must be
unique."* The correct sequence is:
1. Call `ReadCustomerByReference(reference)`. If it returns a `CustomerResponse`, use `.Customer.Id` — done.
2. If it throws `SdkException<RawError>` for a **not-found** miss, call `CreateCustomer` with
   that same `reference`.
3. If step 2 itself throws `SdkException<CreateCustomerError>` with the 422 case (meaning a
   concurrent duplicate call won the race between steps 1 and 2), **do not treat this as a hard
   failure** — re-call `ReadCustomerByReference(reference)` once more and use the customer it
   returns. This closes the double-click race without ever creating two customers for the same
   reference.

`UNVERIFIED`: the exact HTTP status `ReadCustomerByReference` returns on a lookup miss is not
stated in the map or in the SDK source (it is Case B/`RawError` with no per-status typed
accessor — confirmed by reading `Api/Customers.cs` this session, which carries no status-code
doc-comment). Defensive directive: treat `ex.Error.StatusCode == HttpStatusCode.NotFound` as
"customer does not exist yet, proceed to create"; treat any other status as a genuine failure to
surface/rethrow, not to swallow into the create branch.

`UNVERIFIED` / suspicious shared model (confirmed by reading `Models/CustomerErrorResponse1.cs`
and `Models/Errors.cs` in the cloned source this session): the 422 typed payload
`CustomerErrorResponse1.Errors` (type `MaxioAdvancedBilling.Models.Errors`) declares **only**
`PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`
— fields that have nothing to do with a customer or a `reference` conflict. This is a generic
error-shape record the generator reused across unrelated operations; do **not** branch program
logic on `CustomerErrorResponse1.Errors.PerPage`/`.PricePoint` to detect a duplicate-reference
conflict — it will not carry one. Defensive directive: on 422 from `CreateCustomer`, extract a
best-effort message via `TryGetRawError(out var raw)` → `raw.ReadAsString()` (or
`ReadAsJson<System.Text.Json.JsonElement>()`) for logging/diagnostics, but drive the actual
retry-by-reference-lookup behavior purely off "422 happened", not off any parsed field.

### 2.4 `POST /api/subscriptions` — subscription creation step (2b, 2c, 2d)

| Op | Signature | Request | Response envelope | Error | Source |
|---|---|---|---|---|---|
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` = the id resolved in 2.3 | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`, each `.Subscription` (nullable — null-check) | `SdkException<RawError>` — Case B | `map/operations/Customers.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req` wrapping `MaxioAdvancedBilling.Models.CreateSubscription`. **No field on this record is marked `!req` in the generated type** — the compiler will not stop you from omitting the fields this call actually needs. Set explicitly: `ProductHandle (product_handle): string?` = the chosen plan handle (`eshop-pro` or `basic-plan`) **or** `ProductId (product_id): int?` if calling by numeric id; `CustomerId (customer_id): int?` = the id from 2.3 **or** `CustomerReference (customer_reference): string?` = the same stable reference (either is a valid way to identify the customer per the op's own Notes — do not set both to conflicting values). Leave `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` all `null` — the seeded plans have payment method not required, and the op's Notes confirm payment info is conditional on the product's own options | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` — **nullable**, null-check before reading. Fields to read for the response payload: `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Product (product): MaxioAdvancedBilling.Models.Product?` → `.Name`, `.PriceInCents`, `.Handle`; `Customer (customer): MaxioAdvancedBilling.Models.Customer?` | `SdkException<CreateSubscriptionError>` — Case A. Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (shape: `Errors (errors): IReadOnlyList<string> !req` — a plain string list, unlike the mismatched `CustomerErrorResponse1` above; safe to surface these strings directly) · `TryGetRawError(out RawError)` [fallback] | `map/operations/Subscriptions.md`; `map/models/records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`, `ErrorListResponse1`); `map/models/records-4-Su-We.md` (`SubscriptionResponse`); `map/models/records-3-Of-Su.md` (`Subscription`, `Product`) |

**Subscription-level idempotency (2c) — the exact, real mechanism (no invention):**
`CreateSubscription`'s request model (`CreateSubscription`, full field list read this session)
has **no idempotency-key parameter of any kind**, and its Notes make no mention of the API
rejecting or coalescing a second `CreateSubscription` call for a customer who already has an
active subscription to the same product — it will simply create another one. There is **no
native dedup to rely on.** See Blocker in §5 — the application must implement its own
check-then-create using `ListCustomerSubscriptions` (2.4 row 1), filtering the returned list for
an existing subscription whose `.Subscription.Product.Handle` matches the requested plan and
whose `.Subscription.State` is a live state (map's `SubscriptionState` enum: `Active`,
`Trialing`, `PastDue`, `Suspended`, `OnHold`, `AwaitingSignup`, `SoftFailure`, `Unpaid` — treat
`Canceled`/`Expired`/`FailedToCreate` as not blocking a new signup), before calling
`CreateSubscription`. This still leaves a race window on true concurrent double-clicks — the map
provides no server-side lock to close it (§5).

### 2.5 `GET /api/my-subscriptions`

Same two ops as 2.3 row 1 (`ReadCustomerByReference`) and 2.4 row 1 (`ListCustomerSubscriptions`)
— resolve the caller's customer id from the stable reference, then list. If
`ReadCustomerByReference` misses (no Maxio customer yet for this user — they have never hit
`POST /api/subscriptions`), return an empty list rather than treating it as an error (**YOUR
CALL — not in the map**: this is an application response-shaping decision, not an SDK fact).

### 2.6 Enum value tables actually needed

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`map/models/enums.md`) — full value list:
`Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`,
`Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`,
`PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`,
`Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`,
`AwaitingSignup (awaiting_signup)`.

`MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`map/models/enums.md`): `Day (day)`,
`Month (month)`.

---

## 3. Trap notes

⚠ Step 1 (client & DI registration) — `AddMaxioAdvancedBillingClient` builds the
`MaxioAdvancedBillingClient` **once, eagerly, inside its own registration lambda** as a
Singleton, from an `IHttpClientFactory`-created `HttpClient`. Whether/how this composes with
ASP.NET Core's standard named/typed `HttpClient` configuration (timeouts, handlers,
`IOptionsMonitor`-driven config reload) is not something the extension method's signature
shows you. **MUST load `dotnet-client-initialization`** before wiring this into `Program.cs`.

⚠ Step 1 (auth) — the `Password = "x"` literal looks like a placeholder that needs replacing; it
does not. Getting this wrong (e.g. binding it to a second secret) breaks every call with an auth
error that looks unrelated to the real cause. **MUST load `dotnet-authentication`** before
wiring `Maxio:ApiKey` into `BasicAuthCredentials`.

⚠ Steps 2–4 (every call) — `ListProductsForProductFamily`, `ListSubscriptions`-family, and
`ListCustomerSubscriptions` all have many nullable-with-no-default parameters that "must pass
explicitly"; calling them positionally is exactly how a value silently binds to the wrong
parameter. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ Step 2 (`GET /api/subscription-plans`) and Step 3d (`POST /api/subscriptions` response
mapping) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>`, not C# enums; equality,
serialization, and switch-style branching on them do not behave like a normal enum. **MUST load
`dotnet-models`** before writing the response-mapping/filtering code that reads `.State` or
`.IntervalUnit`.

⚠ Step 3 (customer-ensure + subscription-create sequence) — every call in this sequence is
throw-only (no `…Result` variant exists anywhere in this SDK), and the two operations in the
sequence carry **different** error cases (`ReadCustomerByReference`/`ListCustomerSubscriptions`
are Case B `RawError`; `CreateCustomer`/`CreateSubscription` are Case A typed). A single catch
block that assumes one shape for both will miss the other's accessor entirely. **MUST load
`dotnet-error-handling`** before writing this boundary (see also the two mandatory rows in §4).

⚠ Step 1 (resilience) — the default `RetryOptions` may resend a `CreateCustomer`/
`CreateSubscription` `POST` on a transport-level failure (not just a retried status code),
independent of anything this plan's own check-then-create logic does at the application layer —
the two retry mechanisms (SDK-level, app-level) are not the same thing and don't know about each
other. **MUST load `dotnet-configuration-resilience`** before finalizing retry/timeout
configuration for the registered client.

⚠ Steps 3–4 tests — stubbing `ReadCustomerByReference`'s not-found miss and `CreateCustomer`'s
422-race branch both require faking specific HTTP status/body combinations at the transport seam,
not just faking a return value — the UNVERIFIED not-found status noted in §2.3 must be encoded as
a test assumption, not left implicit. **MUST load `dotnet-testing`** before writing tests for the
find-or-create logic.

---

## 4. REQUIRED READING

Load every skill below **before implementation starts** — this sheet deliberately does not
carry their contents.

- `dotnet-client-initialization` — governs Step 1 (client construction, DI registration, `HttpClient` lifetime/ownership).
- `dotnet-authentication` — governs Step 1 (Basic auth credential wiring, where/when to set it).
- `dotnet-calling-endpoints` — governs Steps 2–4 (named-argument calling convention for every operation in §2).
- `dotnet-models` — governs Steps 2–4 (building `CreateCustomer`/`CreateSubscription` request objects, reading `StringEnum<T>` values, JSON wire names).
- `dotnet-error-handling` — governs Step 3 and the shared error boundary across all four endpoints. **Mandatory two hazard rows, verbatim:**
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the `JsonException`
    **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
    maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and
    a caller that retries 5xx retries something that can never succeed.
- `dotnet-configuration-resilience` — governs Step 1 (retry/timeout tuning, and the
  transport-failure-retries-every-verb hazard noted in §3).
- `dotnet-testing` — governs test coverage for Steps 3–4 (the find-or-create and
  check-then-create branches, including the UNVERIFIED not-found status).

---

## 5. Assumptions & Blockers

**Blockers:**

- **No native idempotent upsert-by-reference for customers.** Confirmed from `Customers.md`'s
  own Notes and the `CreateCustomer`/`ReadCustomerByReference` signatures: the only supported
  mechanism is application-level lookup-then-create-with-422-fallback, spelled out exactly in
  §2.3. This is real, not invented — but it is inherently not fully atomic; a true simultaneous
  double-click still relies on the 422-then-relookup fallback closing the gap, not on a
  database-style unique-constraint guarantee the app can lean on for anything beyond customers.
- **No native idempotency key or dedup for subscription creation.** Confirmed from the full
  `CreateSubscription` request-field list (§2.4) and its Notes: there is no idempotency-key
  field and no documented server-side rejection of a duplicate active subscription for the same
  customer+product. The app must implement its own check-then-create via
  `ListCustomerSubscriptions` (§2.4), and a genuine concurrent double-click race is **not**
  closable purely from this SDK's surface — closing it fully requires an application-level lock
  or dedup key that is out of this plan's scope to design (the application's own concurrency
  design is not this plan's to set).

**Assumptions:**

- The eShopOnWeb user's JWT claim used to derive the Maxio customer `reference` (e.g. user id vs.
  email vs. username) and its exact string format is **YOUR CALL — not in the map** (§2.3);
  this plan assumes it is stable and unique per user, which is a requirement on whichever claim
  is chosen, not an SDK fact.
- Currency for the plan-listing endpoint is taken from `client.Sites.ReadSite().Site.Currency`
  (§2.2) rather than any field on `Product`/`ProductPricePoint`, because no such field exists on
  those records (confirmed this session). Fetching/caching this value once (vs. per-request) is
  an application performance decision (**YOUR CALL**).
- `Maxio:ProductFamilyHandle` is assumed to be stored as the bare handle (`eshop-subscribe`);
  the integration code prefixes it with `"handle:"` at the `ListProductsForProductFamily` call
  site per §2.1's last row.
