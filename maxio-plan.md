# Maxio Advanced Billing — integration plan & contract sheet (eShopOnWeb `src/PublicApi`)

Grounded against the bundled SDK map (`sdk-map.md`, `map/operations/*`, `map/models/*`) and, where the
map left a real gap, against the SDK source at tag `v1.0.2` (the ref the map was generated from). Every
row cites where it came from. Nothing below is from memory of this API.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 0 | Add the NuGet package; add the `Maxio:*` configuration keys. | — |
| 1 | Register the SDK client in DI (auth, subdomain / base-URL override, retry options). | — |
| 2 | `GET /api/subscription-plans` — resolve the product family by **handle**, list its products, project to a plan DTO; read the site currency once. | `client.ProductFamilies.ListProductFamilies`, `client.ProductFamilies.ListProductsForProductFamily`, `client.Sites.ReadSite` |
| 3 | `POST /api/subscriptions` — idempotently ensure the Maxio customer for the JWT caller, then idempotently create the subscription by product **handle**, with the architecture-correct `PaymentCollectionMethod` (§2.7). | `client.Sites.ReadSite`, `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer`, `client.Subscriptions.FindSubscription`, `client.Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — resolve the caller's customer, list their subscriptions, project state/price/dates. | `client.Customers.ReadCustomerByReference`, `client.Customers.ListCustomerSubscriptions` |
| 5 | Write the SDK→HTTP error boundary (shared by all three endpoints). | — |
| 6 | Tests against the `HttpClient` seam. | — |

Step 2 note — **there is no "read product family by handle" operation.** `ReadProductFamily` takes
`int id`, so the handle cannot be passed to it (see §2.2). The grounded path is: list families, match
`ProductFamily.Handle` against `Maxio:ProductFamilyHandle` in memory, then pass that family's `Id` (as a
string) to `ListProductsForProductFamily`. No numeric ID is hard-coded — the ID is discovered per call
(cache it in memory if you wish; that is a §5 `YOUR CALL` item).

Step 2 note — **`Product` carries no currency field** (see §2.4). Currency comes from
`client.Sites.ReadSite` → `SiteResponse.Site.Currency`.

Step 3 note — **a no-card signup requires `PaymentCollectionMethod` to be set explicitly** (settled by
live traffic; see **§2.7**). The value depends on the site's architecture, so the **same** O4 `ReadSite`
call that step 2 makes for `Currency` must also read `Site.RelationshipInvoicingEnabled` and
`Site.DefaultPaymentCollectionMethod`. Sequence step 3 accordingly: O4 is a prerequisite of step 3, not
only of step 2 — resolve it once and share it (caching it is `YOUR CALL — not in the map`). Rejecting
plans whose `Product.RequireCreditCard == true` up front remains correct, but it is a **necessary, not
sufficient** filter — see the `RequireCreditCard` row in §2.4.

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

### 2.1 Package, client construction, auth, servers

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (install by this id; it differs from the namespace) | `sdk-map.md` |
| Package version | Map stamp is source tag `v1.0.2` / commit `15db14b`; the `.csproj` **at that tag** declares `<Version>1.0.0</Version>` — the two disagree, so the published version number is not settleable from map or source. Pin an explicit `Version=` in the `.csproj` (never a floating range) and treat the compiler as arbiter if a symbol below does not resolve. | `sdk-map.md`; `MaxioAdvancedBilling.csproj` — **UNVERIFIED** (exact published version) |
| Transitive deps this adds | `Polly` 8.6.5, `Microsoft.Extensions.Http` 10.0.8, `System.Net.Http.Json` 10.0.8, `System.Net.ServerSentEvents` 10.0.8, `PolySharp` 1.15.0 (build-only). Installing the SDK therefore lifts `Microsoft.Extensions.Http` to 10.0.8 transitively — expect a version bump in the PublicApi restore graph. | `MaxioAdvancedBilling.csproj` |
| SDK target framework | `netstandard2.0`, `LangVersion 14`, `Nullable enable` | `sdk-map.md`; `MaxioAdvancedBilling.csproj` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — `sealed` | `MaxioAdvancedBillingClient.cs` |
| **There is no builder type.** The only constructor is | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md`; `MaxioAdvancedBillingClient.cs` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — plain settable class, no builder methods | `MaxioAdvancedBillingClientOptions.cs` |

`MaxioAdvancedBillingClientOptions` members (namespace `MaxioAdvancedBilling`), with the defaults the
source declares:

| Property | Type (fully qualified) | Default | Source |
|---|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` ⇒ `ServerEnvironment.Us` | `MaxioAdvancedBillingClientOptions.cs` |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` | `MaxioAdvancedBillingClientOptions.cs` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new ServerOptions()` | `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs` |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` | `MaxioAdvancedBillingClientOptions.cs` |

**Auth.** `BasicAuthCredentials` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`) is `sealed`
with two `required string` init-only members: `Username`, `Password`. **`Username` = the Maxio API key,
`Password` = the literal `"x"`.** Both are `required`, so the object initializer must set both.
Source: `sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs`.

```csharp
options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
{
    Username = /* Maxio:ApiKey */, Password = "x"
};
```

**Servers — the subdomain and the verbatim base-URL override both exist. This is not a blocker.**

| Type | Namespace | Members | Source |
|---|---|---|---|
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `static readonly ServerEnvironment Us` (wire `"US"`), `Eu` (wire `"EU"`), `static ServerEnvironment Default() => Us`. It is a `StringEnum<ServerEnvironment>` record, **not** a C# enum — the private ctor means `Us`/`Eu` are the only constructible values. | `Servers/ServerEnvironment.cs` |
| `ServerOptions` | `MaxioAdvancedBilling` (**root**, not `.Servers`) | `ProductionOptions Production { get; set; }`, `EbbOptions Ebb { get; set; }` — both pre-initialized | `ServerOptions.cs` |
| `ProductionOptions` | `MaxioAdvancedBilling.Servers` | `UsOptions Us { get; set; }`, `EuOptions Eu { get; set; }` — both pre-initialized | `Servers/ProductionOptions.cs` |
| `ProductionOptions.UsOptions` (nested) | `MaxioAdvancedBilling.Servers` | `string BaseUrl` default `"https://{site}.chargify.com"`; `string Site` default **`"subdomain"`** | `Servers/ProductionOptions.cs` |
| `ProductionOptions.EuOptions` (nested) | `MaxioAdvancedBilling.Servers` | `string BaseUrl` default `"https://{site}.ebilling.maxio.com"`; `string Site` default `"subdomain"` | `Servers/ProductionOptions.cs` |

- **Subdomain:** `options.Server.Production.Us.Site = "your-site";`
- **Verbatim base URL:** `options.Server.Production.Us.BaseUrl = "https://your-site.chargify.com";` — the
  URL is assembled by `template.Replace("{site}", value)` and then
  `baseUrl.TrimEnd('/') + "/" + path.TrimStart('/')`, so a `BaseUrl` **containing no `{site}` token is
  used verbatim** and the `Site` value is simply never substituted. A trailing slash is harmless.
  Source: `Core/TemplateParamsFactory.cs`, `Core/UriFactory.cs`.
- **Only the branch matching `options.Environment` is read** (`ProductionOptions.Resolve` matches
  `Us`/`Eu`), so setting `Production.Us.*` while `Environment = ServerEnvironment.Eu` silently has no
  effect. Source: `Servers/ProductionOptions.cs`.
- `Site`'s default is the **literal string `"subdomain"`**, not null and not validated — forgetting to set
  it produces requests to `https://subdomain.chargify.com` rather than an exception. Source:
  `Servers/ProductionOptions.cs`.
- The `Ebb` group is only used by `SubscriptionComponents` event-ingest endpoints; nothing in this scope
  touches it. Source: `sdk-map.md`.

**Configuration keys → SDK options** (key names are the app's own; the SDK effect and defaults are map/source facts):

| Binding key | Maps to | If unset |
|---|---|---|
| `Maxio:ApiKey` | `options.BasicAuth.Username` (with `Password = "x"`) | `BasicAuth` stays `null` → requests go out unauthenticated → 401. Fail fast at startup instead. |
| `Maxio:Subdomain` | `options.Server.Production.Us.Site` | SDK default `"subdomain"` (§ above) — wrong host, so validate at startup. |
| `Maxio:BaseUrl` | `options.Server.Production.Us.BaseUrl` (verbatim) | SDK default `"https://{site}.chargify.com"` with `{site}` ← `Site`. Set **either** this **or** `Subdomain`; if both are set, `BaseUrl` without a `{site}` token wins outright. |
| `Maxio:ProductFamilyHandle` | Not an SDK option — the value matched against `ProductFamily.Handle` in step 2. | `YOUR CALL — not in the map` |

**DI registration.** `MaxioAdvancedBilling.ServiceCollectionExtensions` declares
`AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` returning
`IServiceCollection` (**not** `IHttpClientBuilder`, so you cannot chain `.ConfigureHttpClient(...)` /
`.AddHttpMessageHandler(...)` off it). What it does, verbatim from source: invokes `configure` **once at
registration time**, calls `services.AddHttpClient()`, and registers the client as a **singleton** whose
factory calls `IHttpClientFactory.CreateClient()` (the default, unnamed client) once. Consequences that
are facts, not opinions:

- The options object is built eagerly from the callback — it is **not** resolved from DI, so nothing
  inside the callback can inject services. Read configuration from the outer scope (e.g. `builder.Configuration`)
  at the call site. There is no `IOptionsMonitor` path and therefore no hot reload of the API key.
- The client is a singleton and holds one `HttpClient` for the process lifetime.
- It is declared inside a C# 14 `extension(IServiceCollection services)` block. If
  `services.AddMaxioAdvancedBillingClient(...)` does not resolve under the PublicApi project's language
  version, register manually instead — that fallback is fully specified and needs no further lookup:
  `services.AddHttpClient("maxio"); services.AddSingleton(sp => new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("maxio"), options));`
  Source: `ServiceCollectionExtensions.cs`.

**Thread-safety / lifetime.** `MaxioAdvancedBillingClient` is `sealed`, built once in its constructor from
immutable collaborators, and the SDK's own DI helper registers it as a **singleton** — singleton
registration is the shape the SDK ships. It exposes no `Dispose`; the `HttpClient` is owned by whoever
created it (with the SDK helper, by `IHttpClientFactory`). Source: `MaxioAdvancedBillingClient.cs`,
`ServiceCollectionExtensions.cs`.

**Retries/timeouts.** `options.Retry` is a `MaxioAdvancedBilling.Core.Configuration.RetryOptions` record
in which **every member is `required`** — you cannot build one member-by-member; start from
`RetryOptions.Default()` and use a `with` expression. Members: `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>`,
`HttpMethodsToRetry: IReadOnlyList<HttpMethod>`, `MaxRetries: int`, `Delay: TimeSpan`, `Timeout: TimeSpan?`,
`BackOffFactor: int`, `UseExponentialBackoff: bool`, `MaxJitter: TimeSpan`, `OnRetry: Action<RetryAttempt>?`.
Source: `sdk-map.md`; `Core/Configuration/RetryOptions.cs`. **What these actually bound and which calls
they actually resend is the trap in §3 — do not infer it from the member names.**

### 2.2 Operations

| # | Controller property | Method signature (verbatim, params in order) | Request model + fields | Response envelope + fields read | Error case / accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| O1 | `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters are nullable **with no C# default → must be passed explicitly** (pass `null`) | none (GET) | `IReadOnlyList<ProductFamilyResponse>`; read `.ProductFamily` (**nullable** `ProductFamily?`) → `.Handle`, `.Id` | **Case B** — `SdkException<RawError>`; `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none | `operations/ProductFamilies.md` |
| O2 | `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable **with no default → must be passed explicitly**; `page`/`perPage` default to 1/20 | none (GET). `productFamilyId` = the `Id` from O1, `.ToString()`. Pass `includeArchived: false`. | `IReadOnlyList<ProductResponse>`; `.Product` is `required` (non-null) | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 20 — loop until a short page) | `operations/ProductFamilies.md` |
| O3 | `client.Products` (`MaxioAdvancedBilling.Api.Products`) | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none (GET) | `ProductResponse`; `.Product` is `required` | **Case B** — `SdkException<RawError>` (a 404 for an unknown handle arrives here, `StatusCode == NotFound`) | none | `operations/Products.md` |
| O4 | `client.Sites` (`MaxioAdvancedBilling.Api.Sites`) | `ReadSite(CancellationToken ct = default)` | none (GET) | `SiteResponse`; `.Site` is `required` → `.Currency: string?`, `.Subdomain: string?`, `.Test: bool?`, **`.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`**, **`.DefaultPaymentCollectionMethod (default_payment_collection_method): string?`** — the last two drive §2.7 | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`; `records-3-Of-Su.md`; `Models/Site.cs` |
| O5 | `client.Customers` (`MaxioAdvancedBilling.Api.Customers`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` is a **query** param (`GET /customers/lookup.json?reference=…`) | none (GET) | `CustomerResponse`; `.Customer` is `required` → `.Id: int?`, `.Reference: string?`, `.Email: string?` | **Case B** — `SdkException<RawError>`; **"no such customer" arrives as `StatusCode == HttpStatusCode.NotFound`, not as a null result** | none | `operations/Customers.md` |
| O6 | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** | `CreateCustomerRequest { Customer: CreateCustomer !req }` → see §2.3 | `CustomerResponse`; `.Customer` is `required` → `.Id`, `.Reference` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [**422 only**] · `TryGetRawError(out RawError)` [every other status] | none | `operations/Customers.md`; `Errors/CreateCustomerError.cs` |
| O7 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (GET) | `IReadOnlyList<SubscriptionResponse>`; `.Subscription` is **nullable** | **Case B** — `SdkException<RawError>` | **none** — this endpoint takes no `page`/`perPage` and no state filter; filter by `Subscription.State` in memory | `operations/Customers.md` |
| O8 | `client.Subscriptions` (`MaxioAdvancedBilling.Api.Subscriptions`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }` → see §2.3 | `SubscriptionResponse`; `.Subscription` is **nullable** → `.Id`, `.State`, `.ProductPriceInCents`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt`, `.Product`, `.Currency` | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [**422 only**] · `TryGetRawError(out RawError)` [every other status] | none | `operations/Subscriptions.md`; `Errors/CreateSubscriptionError.cs` |
| O9 | `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **no default → must pass explicitly**; sent as query `reference` | none (GET) | `SubscriptionResponse`; `.Subscription` is **nullable** | **Case A** — `SdkException<FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| O10 | `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, **no default → must pass explicitly** (pass `null`) | none (GET) | `SubscriptionResponse`; `.Subscription` is **nullable** | **Case B** — `SdkException<RawError>` | none | `operations/Subscriptions.md` |

Not used, and why (so nobody reaches for them): `ReadProductFamily(int id, …)` cannot take a handle — its
parameter is `int` even though the endpoint prose mentions a `handle:my-family` form
(`operations/ProductFamilies.md`). `ListSubscriptions(...)` has 14 must-pass-explicitly params and
**no customer filter** at all, so O7 is the only customer-scoped listing (`operations/Subscriptions.md`).

**`handle:` shortcut (optional).** `ReadProductFamily`'s Notes say a family "can be specified either with
the id number, or with the `handle:my-family` format", and O2's `productFamilyId` is a `string` — so
`ListProductsForProductFamily("handle:your-family-handle", …)` would collapse O1+O2 into one call. O2's own
Notes do **not** state that, so this is cross-row inference. Treat it as **UNVERIFIED**: if you use it,
code it defensively — on any failure or empty result fall back to the O1→O2 path in §1 step 2, which is
fully grounded. Source: `operations/ProductFamilies.md` — **UNVERIFIED**.

### 2.3 Request models (namespace `MaxioAdvancedBilling.Models`)

`CreateCustomerRequest` — `Customer (customer): CreateCustomer` **`required`**. Source: `records-1-Ac-Cr.md`.

`CreateCustomer` — fields used here (`!req` = C# `required`, must be set in the initializer):

| C# member (wire name) | Type | Required? | Note |
|---|---|---|---|
| `FirstName (first_name)` | `string` | **`required`** | The compiler forces a value. A JWT that carries only an email cannot fill this — deriving a first/last name from the eShopOnWeb user is `YOUR CALL — not in the map`; the SDK simply will not compile without both. |
| `LastName (last_name)` | `string` | **`required`** | as above |
| `Email (email)` | `string` | **`required`** | the caller's email from the token |
| `Reference (reference)` | `string?` | optional | **This is the idempotency key.** CreateCustomer's Notes: "you may only create one customer for a given `reference` value… it represents a unique identifier for the customer from your own app". Uniqueness is enforced by Maxio, per those Notes. |
| `Organization (organization)`, `Locale (locale)`, `Address`…`Phone`, `TaxExempt`, `VatNumber`, `ParentId`, `SalesforceId`, `CcEmails`, `TaxExemptReason` | all nullable | optional | **Deliberately left out** — none is named by CreateCustomer's Notes as affecting acceptance. The Notes do constrain `country` (ISO-3166-1 alpha-2) and `state` (ISO-3166-2) **if you send them**, so send neither rather than sending an unvalidated value. |

Source: `records-1-Ac-Cr.md`; `Models/CreateCustomer.cs`.

`CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription` **`required`**.
Source: `records-2-Cr-Ne.md`.

`CreateSubscription` — **nothing on this record is C# `required`**, so `required?` selects no fields for
you. The fields that decide whether the call is *accepted* come from CreateSubscription's Notes and the
generated XML docs:

| C# member (wire name) | Type | Set it? | Why (from Notes / XML doc) |
|---|---|---|---|
| `ProductHandle (product_handle)` | `string?` | **yes** — `"eshop-pro"` / `"basic-plan"` | XML doc: "The API Handle of the product for which you are creating a subscription. **Required, unless a `product_id` is given instead.**" Using the handle is what keeps numeric IDs out of the app. |
| `ProductId (product_id)` | `int?` | no | mutually exclusive with `ProductHandle` |
| `CustomerId (customer_id)` | `int?` | **yes** (preferred) — `Customer.Id` from O5/O6 | XML doc: "The ID of an existing customer within Chargify. **Required, unless a `customer_reference` or a set of `customer_attributes` is given.**" |
| `CustomerReference (customer_reference)` | `string?` | alternative to `CustomerId` | XML doc: "The reference value (provided by your app) of an existing customer… **Required, unless a `customer_id` or a set of `customer_attributes` is given.**" Either one satisfies the contract; `CustomerId` is preferred here because step 3 has already read the customer and the id is unambiguous. |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | **no** | The third way to identify a customer — creates a new customer inline. Using it would bypass the reference-based idempotency in step 3, so do not set it. |
| `Reference (reference)` | `string?` | **yes** — the subscription idempotency key | XML doc: "The reference value (provided by your app) for the **subscription itself**." This is the value `FindSubscription(reference)` (O9) looks up. |
| `Ref (ref)` | `string?` | **NEVER** | XML doc: "A valid **referral code**… If supplied, must be valid, or else **subscription creation will fail**." Two similarly named fields; setting the wrong one breaks the hero flow. |
| `ProductPricePointHandle` / `ProductPricePointId` | `string?` / `int?` | no | omit → the product's default price point is used |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | **YES — required in practice for a no-card signup** | Omitting it applies the site default (`Site.DefaultPaymentCollectionMethod`, O4). **Live traffic settled this: omitting it produced a 422 `["No payment method was on file for the $299.00 balance"]` even though `eshop-pro` has `RequireCreditCard == false`.** Set it to the value that matches the site's architecture — see §2.7. |
| `NextBillingAt (next_billing_at)` | `DateTimeOffset?` | **conditionally — the doc-settled belt-and-braces lever** | XML doc, verbatim: "If you provide a next_billing_at timestamp that is **in the future**, no trial or initial charges will be applied when you create the subscription. **In fact, no payment will be captured at all.** The first payment will be captured, according to the prices defined by the product, near the time specified by next_billing_at. **If you do not provide a value for next_billing_at, any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created.**" That final sentence is the documented mechanism behind the observed 422. Cannot be combined with `CalendarBilling`. |
| `InitialBillingAt (initial_billing_at)` | `DateTimeOffset?` | **no** (for this flow) | XML doc: "Set this attribute to a **future** date/time to create a subscription in the **Awaiting Signup** state, rather than Active or Trialing… When the initial_billing_at date hits, the subscription will transition to the expected state… **If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled.**" It *defers* the charge; it does not waive it, and it changes the state you confirm back to the shopper. |
| `DeferSignup (defer_signup)` | `bool?`, declared default `false` | **no** | XML doc: "Set this attribute to true to create the subscription in the **Awaiting Signup Date** state. Use this when you want to create a subscription that has an **unknown first billing date**." Not this flow. **Serialization exception:** this is the one member of `CreateSubscription` with **no** `[JsonIgnore(WhenWritingNull)]`, so `defer_signup` is written to the body on every request (as `false` by default). Source: `Models/CreateSubscription.cs`. |
| `NetTerms (net_terms)` | `string?` | optional companion to a non-automatic method | XML doc: "(Optional) Default: null The number of days after renewal (**on invoice billing**) that a subscription is due. A value between 0 (due immediately) and 180." It only bites once collection is not automatic; it is not itself a way to avoid the signup charge. Note the type is `string?`, not `int?`. |
| `ReceivesInvoiceEmails (receives_invoice_emails)` | `string?` | optional | Relevant once the subscription is invoice/remittance-billed. **Type asymmetry:** `string?` on this request record but `bool?` on the `Subscription` response record — do not round-trip it without conversion. Source: `records-2-Cr-Ne.md`, `records-4-Su-We.md`. |
| `CustomPrice (custom_price)` | `SubscriptionCustomPrice?` | **no** | XML doc: "(Optional) Used in place of `product_price_point_id` to define a custom price point unique to the subscription." It could zero the signup balance, but that changes what the shopper is charged — a pricing decision, not a plumbing fix. `YOUR CALL — not in the map` if you ever want it. |
| `CalendarBillingFirstCharge (calendar_billing_first_charge)` | `string?` | **no** | XML doc: "(Optional) One of "prorated" (the default…), "immediate"…, or "**delayed**" (the full product price will be charged **with the first scheduled renewal**)." Only meaningful alongside `CalendarBilling`, which itself "Cannot be used when also specifying next_billing_at". A heavier alternative to `NextBillingAt`; not needed here. |
| `CreditCardAttributes`, `PaymentProfileAttributes`, `BankAccountAttributes`, `PaymentProfileId`, `AgreementAcceptance`, `AchAgreement` | all nullable | **no** | The product does not require a payment method, so no payment field is set. Nothing in the record is `required`, and the SDK sends no payment keys at all when these are left null. |
| `Currency (currency)` | `string?` | no | XML doc: "(Optional) **If Multi-Currency is enabled**… pass it at signup to create a subscription on a non-default currency." Leave unset to use the site's default currency. |
| `CouponCode`, `Components`, `CalendarBilling`, `Group`, `OfferId`, `ExpiresAt`, `Metafields`, `PrepaidConfiguration`, `SalesRepId`, `ReasonCode`, … | all nullable | no | out of scope; none is Notes-named as affecting acceptance for this flow |

**There is no per-subscription override of `require_credit_card`, and no "waive/zero the signup charge"
flag.** That is an enumeration, not an impression: `CreateSubscription` declares exactly 50 wire fields —
`product_handle`, `product_id`, `product_price_point_handle`, `product_price_point_id`, `custom_price`,
`coupon_code`, `coupon_codes`, `payment_collection_method`, `receives_invoice_emails`, `net_terms`,
`customer_id`, `next_billing_at`, `initial_billing_at`, `defer_signup`, `stored_credential_transaction_id`,
`sales_rep_id`, `payment_profile_id`, `reference`, `customer_attributes`, `payment_profile_attributes`,
`credit_card_attributes`, `bank_account_attributes`, `components`, `calendar_billing`, `metafields`,
`customer_reference`, `group`, `ref`, `cancellation_message`, `cancellation_method`, `currency`,
`expires_at`, `expiration_tracks_next_billing_change`, `agreement_terms`, `authorizer_first_name`,
`authorizer_last_name`, `calendar_billing_first_charge`, `reason_code`, `product_change_delayed`,
`offer_id`, `prepaid_configuration`, `previous_billing_at`, `import_mrr`, `canceled_at`, `activated_at`,
`agreement_acceptance`, `ach_agreement`, `dunning_communication_delay_enabled`,
`dunning_communication_delay_time_zone`, `skip_billing_manifest_taxes`. The only levers over the signup
charge in that list are the ones tabulated above. Source: `Models/CreateSubscription.cs`.

Source: `records-2-Cr-Ne.md`; `Models/CreateSubscription.cs`.

Serialization note (affects every request model above): each optional member carries
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, so **unset optionals are omitted from the
JSON body entirely** — they are not sent as `null`. Source: `Models/CreateSubscription.cs`,
`Models/CreateCustomer.cs`.

### 2.4 Response models (namespace `MaxioAdvancedBilling.Models`) — envelopes and the fields read

**Envelopes wrap their payload in exactly one field; every read goes one level down.** Their nullability
is *not* uniform — this is the main trap on the read path:

| Envelope | Payload member | Nullable? | Source |
|---|---|---|---|
| `ProductResponse` | `Product (product): Product` | **`required` — non-null** | `records-3-Of-Su.md` |
| `CustomerResponse` | `Customer (customer): Customer` | **`required` — non-null** | `records-1-Ac-Cr.md` |
| `SiteResponse` | `Site (site): Site` | **`required` — non-null** | `records-3-Of-Su.md` |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` | **NULLABLE** | `records-4-Su-We.md` |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` | **NULLABLE** | `records-3-Of-Su.md` |

`Product` — fields the plans endpoint projects (all nullable):

| C# member (wire name) | Type | Feeds |
|---|---|---|
| `Handle (handle)` | `string?` | plan handle — the stable key |
| `Name (name)` | `string?` | plan name |
| `Description (description)` | `string?` | plan description |
| `PriceInCents (price_in_cents)` | `long?` | **price is integer cents, not decimal dollars** — divide by 100 only at the presentation edge |
| `Interval (interval)` | `int?` | billing interval count |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | billing interval unit — enum, see §2.5 |
| `TrialPriceInCents (trial_price_in_cents)` | `long?` | trial info (cents) |
| `TrialInterval (trial_interval)` | `int?` | trial info |
| `TrialIntervalUnit (trial_interval_unit)` | `IntervalUnit?` | trial info |
| `RequireCreditCard (require_credit_card)` | `bool?` | the product's "payment method required" flag — surface it, **but it does NOT predict whether a no-card signup succeeds**. Live traffic: `eshop-pro` has `RequireCreditCard == false` and a no-card `CreateSubscription` was still rejected 422 for the $299.00 signup balance. Rejecting `RequireCreditCard == true` plans up front is still correct as a *necessary* filter; it is not a *sufficient* one. See §2.7. |
| `RequestCreditCard (request_credit_card)` | `bool?` | a *second*, similarly named flag on the same record; it is not the same thing as `require_credit_card`. Surface `RequireCreditCard`; do not treat the two as interchangeable. |
| `ExpirationInterval (expiration_interval)` / `ExpirationIntervalUnit (expiration_interval_unit)` | `int?` / `ExpirationIntervalUnit?` | "expires never" ⇒ expect `Never` / null — enum, see §2.5 |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | non-null ⇒ archived; belt-and-braces alongside `includeArchived: false` |
| `Id (id)`, `ProductFamily (product_family)`, `DefaultProductPricePointId`, `ProductPricePointHandle`, `Taxable`, `VersionNumber`, … | | present but out of scope |

**`Product` has no `currency` member** — the record simply does not declare one. Currency for the plans
endpoint comes from O4: `SiteResponse.Site.Currency: string?`. Source: `records-3-Of-Su.md` (`Product`,
`Site`).

`Subscription` — fields the subscribe/list endpoints project (all nullable):

| C# member (wire name) | Type | Feeds |
|---|---|---|
| `Id (id)` | `int?` | subscription id |
| `State (state)` | `SubscriptionState?` | state — enum, see §2.5 |
| `PreviousState (previous_state)` | `SubscriptionState?` | — |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | price at signup, **integer cents** |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | current amount, **integer cents** |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` | current period start |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | current period end |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **next billing date** |
| `Currency (currency)` | `string?` | per-subscription currency (unlike `Product`, this one exists) |
| `Product (product)` | `Product?` | **nullable** — plan name/handle for the list endpoint comes from here, so null-guard before `.Name` |
| `Customer (customer)` | `Customer?` | **nullable** |
| `Reference (reference)` | `string?` | your idempotency key, echoed back |
| `TrialStartedAt` / `TrialEndedAt` / `ActivatedAt` / `CanceledAt` / `ExpiresAt` / `CancelAtEndOfPeriod` | `DateTimeOffset?` / `bool?` | optional extras |

Source: `records-4-Su-We.md`.

`Customer` — `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`,
`FirstName`, `LastName`, `CreatedAt`, … all nullable. Note `Customer.Id` is `int?` even inside a
`required` envelope, so the subscribe flow must null-guard before assigning to
`CreateSubscription.CustomerId`. Source: `records-1-Ac-Cr.md`.

`ProductFamily` — `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`,
`Description (description): string?`, `ArchivedAt (archived_at): DateTimeOffset?` — all nullable, so the
handle match in step 2 must tolerate a null `Handle` and a null `Id`. Source: `records-3-Of-Su.md`.

### 2.5 Enums (namespace `MaxioAdvancedBilling.Models.Enums`)

These are **not** C# enums: they are `StringEnum<T>` records (base type
`MaxioAdvancedBilling.Core.Enum.StringEnum<T>`). Write the literal member name (`SubscriptionState.Active`,
not `SubscriptionState.active`), or build from a wire value with `Type.FromValue("wire")`. Every one
exposes `Value: string` (the wire value — use this when writing the API response), `IsKnownValue()`,
`static GetKnownValues()`, `static TryGetKnownValue(string, out T?)`, `ToString() => Value`, and record
equality. **Deserialization accepts any string, including values not listed below** — a `switch` over the
static members must have a default arm. Source: `enums.md`; `Core/Enum/StringEnum.cs`,
`Core/Enum/TypedEnum.cs`.

| Enum | Members (`CSharpName (wire_value)`) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `enums.md` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | `enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. **The enum's own XML summary splits these by site architecture, verbatim:** "The type of payment collection to be used in the subscription. For **legacy Statements Architecture** valid options are - `invoice`, `automatic`. For **current Relationship Invoicing Architecture** valid options are - `remittance`, `automatic`, `prepaid`." So `Invoice` and `Remittance` are **not interchangeable** — they are the same idea named differently per architecture, and only one of them is valid on any given site. Also exposes `CollectionMethod.FromValue(string)`. | `enums.md`; `Models/Enums/CollectionMethod.cs` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `enums.md` |
| `BasicDateField` (param type on O1/O2 — pass `null`) | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `enums.md` |
| `ListProductsInclude` (param type on O2 — pass `null`) | `PrepaidProductPricePoint (prepaid_product_price_point)` | `enums.md` |
| `SubscriptionInclude` (param type on O10 — pass `null`) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `enums.md` |
| `CancellationMethod` (on `Subscription`) | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` | `enums.md` |

`ListProductsFilter` (O2's `filter` param) is a **record**, not an enum:
`Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`,
`UseSiteExchangeRate (use_site_exchange_rate): bool?` — pass `null`. Source: `records-2-Cr-Ne.md`.

### 2.6 Errors

| Fact | Value | Source |
|---|---|---|
| Exception type | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` | `sdk-map.md`; `Core/Exceptions/SdkException.cs` |
| Base type | **`System.Exception` directly.** The class is `sealed`, generic, and has exactly one member: `public required TError Error { get; init; }`. | `Core/Exceptions/SdkException.cs` |
| **There is no non-generic `SdkException` base.** | So `catch (SdkException ex)` does not compile, and `catch (SdkException<ApiError>)` does **not** catch `SdkException<CreateSubscriptionError>` (class generics are invariant). Each operation needs a catch for **its own** closed generic type, plus `SdkException<RawError>` for the Case-B calls. | `Core/Exceptions/SdkException.cs` |
| HTTP status code | Not on the exception. Case B: `ex.Error.StatusCode` (`System.Net.HttpStatusCode`). Case A: `ex.Error.TryGetRawError(out var raw)` → `raw.StatusCode`. | `sdk-map.md`; `Core/ErrorResponse/RawError.cs`, `Core/ErrorResponse/ApiError.cs` |
| `RawError` members | `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` — namespace `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` |
| `ApiError` (base of every typed error, namespace `MaxioAdvancedBilling.Core.ErrorResponse`) | one public member: `TryGetRawError(out RawError error): bool` | `sdk-map.md` |
| No-throw / `…Result` variants | **absent across the whole SDK** — every operation throws. Ignore any Result-style guidance. | `sdk-map.md` |

**Which status lands where — read from the generated error classes, not assumed:**

`CreateSubscriptionError.Create` is `422 => FromJson<ErrorListResponse1>(…)`, `_ => FromRawBody(…)`.
`CreateCustomerError.Create` is `422 => FromJson<CustomerErrorResponse1>(…)`, `_ => FromRawBody(…)`.
So for **both** Case-A operations in scope:

| Status | Where it surfaces | How to read it |
|---|---|---|
| **422** | the typed accessor | `ex.Error.TryGetErrorListResponse1(out var e)` / `ex.Error.TryGetCustomerErrorResponse1(out var e)` → `true` |
| **401 / 403 / 404 / 429 / 5xx — every non-422 status** | the raw fallback | the typed accessor returns **`false`**; use `ex.Error.TryGetRawError(out var raw)` then `raw.StatusCode` to tell 401 from 403 from 404 |

Source: `Errors/CreateSubscriptionError.cs`, `Errors/CreateCustomerError.cs`, `operations/Subscriptions.md`,
`operations/Customers.md`. For the Case-B operations (O1, O3, O4, O5, O7, O10) **all** statuses —
401, 403, 404, 422, 5xx alike — arrive as `SdkException<RawError>` and are distinguished only by
`ex.Error.StatusCode`. In particular **O5's "customer does not exist" is a `NotFound` on `SdkException<RawError>`**;
that catch is the *normal* branch of the idempotency check, not an error path.

**Typed 422 payload shapes** (namespace `MaxioAdvancedBilling.Models`):

| Type | Fields | Source |
|---|---|---|
| `ErrorListResponse1` (subscription create, 422) | `Errors (errors): IReadOnlyList<string>` — **`required`** | `records-2-Cr-Ne.md` |
| `CustomerErrorResponse1` (customer create, 422) | `Errors (errors): Errors?` | `records-1-Ac-Cr.md` |
| `Errors` (the type of the field above) | `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` | `records-1-Ac-Cr.md` |

**Trust judgment on those two payloads — evidence is map/source-visible, the conclusion is not:**

- `CustomerErrorResponse1.Errors` is typed as the record `Errors`, whose only two members are `per_page`
  and `price_point` — pagination and price-point fields, which have no relation to customer validation.
  On the map's own evidence this generated shape does not model a customer-create validation error.
  `Errors`' members are all-optional, so an *object* body with other keys deserializes to an empty
  `Errors`; a body whose `errors` is an **array** cannot bind to an object at all. Either way,
  `TryGetCustomerErrorResponse1` is not a reliable source of a human-readable message. **Directive:**
  extract best-effort from `CustomerErrorResponse1` (`PerPage`/`PricePoint` joined if present), and fall
  back to the generic message plus `TryGetRawError(out raw)` → `raw.StatusCode` / `raw.ReadAsString()`.
  Never let the mapped message be the only thing logged. **UNVERIFIED** — only live 422 traffic can
  confirm the real body shape.
- `ErrorListResponse1.Errors` is `required`, and `ApiError.FromJson<T>` has **no `try`/`catch`** around the
  deserialize. A 422 body whose `errors` is not a JSON array of strings therefore throws
  `System.Text.Json.JsonException` *while the error object is being built*, and that exception
  **replaces** the `SdkException` — the 422 status is destroyed with it. See the two mandatory rows in §4.
  Source: `Core/ErrorResponse/ApiError.cs`, `records-2-Cr-Ne.md`. **Directive:** the boundary must handle
  `JsonException` explicitly and must **not** map it to 5xx by default. **UNVERIFIED** — whether the live
  422 body matches `ErrorListResponse1` can only be confirmed against real traffic.

### 2.7 The no-payment-profile signup — SETTLED BY LIVE TRAFFIC (supersedes the old §5 assumption)

**Observed:** `CreateSubscription` with only `ProductHandle`, `CustomerId`, `Reference` → HTTP 422,
`SdkException<CreateSubscriptionError>`, `TryGetErrorListResponse1` → `["No payment method was on file for
the $299.00 balance"]`, against a product whose `RequireCreditCard == false`. The earlier sheet claimed no
extra field was needed. **That was wrong; this section replaces it.**

**What the SDK docs say the mechanism is.** The coordinator's reading (site default is `automatic`, so
Maxio charges the signup balance immediately and fails with no profile) is *consistent* with the error, but
it is not the sentence the SDK actually documents. The documented mechanism is on `next_billing_at` —
XML doc, verbatim: *"If you do not provide a value for next_billing_at, any trial and/or initial charges
will be assessed and charged at the time of subscription creation. **If the card cannot be successfully
charged, the subscription will not be created.**"* That is a precise match for a 422 naming the $299.00
balance. Correction, therefore: the *collection method* decides **how** the balance is collected; the
absence of `next_billing_at` decides **when** — and it is the "when" that the docs tie to creation failing.
Both are real levers and they are independent. Source: `Models/CreateSubscription.cs`.

**Q1 — is `PaymentCollectionMethod` the grounded fix, and `Remittance` or `Invoice`?**
Setting a non-`Automatic` collection method is the right lever, **but do not hard-code `Remittance`.** The
enum's summary (quoted in full in §2.5) says `remittance` is valid only on the **Relationship Invoicing**
architecture and `invoice` only on the **legacy Statements** architecture. Choose at runtime from O4:

| `Site.RelationshipInvoicingEnabled` | Write |
|---|---|
| `true` | `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance` |
| `false` | `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice` |
| `null` | Do not guess. Fall back to `Site.DefaultPaymentCollectionMethod` (a `string?` — compare with `CollectionMethod.FromValue(...)`), and if that is also null or `"automatic"`, treat the plan as not self-serve-subscribable and reject with the same clear message you use for `RequireCreditCard == true`. |

**Important limit on this answer:** the map and the source state *which values are valid on which
architecture*. Neither states what `remittance` / `invoice` / `prepaid` **do** — there is no doc-comment
anywhere in the SDK saying "the customer is invoiced and remits payment". That behavioural claim is
**UNVERIFIED**; only live traffic can confirm the 422 clears. Directive: send the architecture-correct
value, and keep the 422 handler from §2.6 in place — extract the `ErrorListResponse1.Errors` strings
best-effort and fall back to `TryGetRawError` → `raw.ReadAsString()`, so a second, different rejection is
diagnosable rather than opaque. If the 422 persists, apply `NextBillingAt` (below) — that one **is**
doc-settled.

**Q2 — what else on `CreateSubscription` governs this.** Enumerated in §2.3 from all 50 declared wire
fields. In short: `NextBillingAt` (future timestamp ⇒ *"no payment will be captured at all"* at creation —
the only doc-settled way to guarantee no signup charge); `InitialBillingAt` and `DeferSignup` (defer, but
land the subscription in an **Awaiting Signup** state and, for `InitialBillingAt`, cancel it outright if
payment later fails); `CalendarBilling` + `CalendarBillingFirstCharge = "delayed"` (charge at first
renewal; mutually exclusive with `NextBillingAt`); `CustomPrice` (zero the price — a pricing decision);
`NetTerms` (due-date on invoice billing, not an avoidance lever). **No field overrides
`require_credit_card` per subscription, and no "no immediate charge" flag exists** — that is from
enumerating the whole record, not from absence of memory.

**Q3 — the site default.** `SiteResponse.Site.DefaultPaymentCollectionMethod`, wire
`default_payment_collection_method`, type **`string?` — NOT `CollectionMethod?`**. The request side is the
enum and the response side is a bare string, so compare via `CollectionMethod.FromValue(value)` or against
`CollectionMethod.Automatic.Value`; do not expect an enum instance. Read alongside
`Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` in the **same** O4 call the
plans endpoint already makes for `Currency`. Source: `records-3-Of-Su.md`; `Models/Site.cs`.

**Q4 — exact types.** Confirmed from source: `CollectionMethod` is a `sealed record` in namespace
**`MaxioAdvancedBilling.Models.Enums`** deriving from `StringEnum<CollectionMethod>`; the literal member
names are `Automatic`, `Remittance`, `Prepaid`, `Invoice` (so `CollectionMethod.Remittance` is correct);
and `CreateSubscription.PaymentCollectionMethod` is declared
`public CollectionMethod? PaymentCollectionMethod { get; init; }` with
`[JsonPropertyName("payment_collection_method")]`. Source: `Models/Enums/CollectionMethod.cs`,
`Models/CreateSubscription.cs`.

**Q5 — what this changes about the three fields you confirm back.** Flagged, because two of them can move:

- **`State`** — do **not** assume `active`. `SubscriptionState` includes `AwaitingSignup (awaiting_signup)`
  and `Pending (pending)` (§2.5), and `InitialBillingAt`/`DeferSignup` are documented to produce
  **Awaiting Signup** explicitly. Whether `Remittance`/`Invoice` alone (with no billing-date field) returns
  `active` is **UNVERIFIED** — no doc says. Corroborating map evidence that this state is reachable and
  normal on RI sites: `Subscriptions.ActivateSubscription` exists precisely to "activate **awaiting
  signup** and trialing subscriptions" and its Notes say it "is only available on the Relationship
  Invoicing architecture" (`operations/Subscriptions.md`). **Directive:** read `Subscription.State.Value`
  and render it; branch on the enum members with a **default arm** (`StringEnum` deserializes any string —
  §2.5). Do not display a hard-coded "Active". If the state comes back `awaiting_signup` and the demo needs
  it live, `ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)`
  → `SubscriptionResponse`, Case A `SdkException<ActivateSubscriptionError>` with
  `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [400] · `TryGetRawError` — that is the
  grounded remedy, already in the map, not a new lookup.
- **`NextAssessmentAt` / `CurrentPeriodEndsAt`** — both stay `DateTimeOffset?` and both can legitimately be
  **null** on a subscription that has not begun billing; the confirmation view must tolerate that rather
  than `.Value` them. **There is no `next_billing_at` on the response model** — `Subscription` declares no
  such member. The map states this outright in `UpdateSubscription`'s Notes: *"The server response will not
  return data under the key/value pair of `next_billing_at`. View the key/value pair of
  `current_period_ends_at` to verify that the `next_billing_at` date has been changed successfully."* So if
  you set `NextBillingAt` on the request, read it back as `CurrentPeriodEndsAt` (and/or
  `NextAssessmentAt`), never as a same-named response field. Sources: `records-4-Su-We.md`,
  `operations/Subscriptions.md`.

**Revised step-3 call shape** (field names verbatim; the collection-method value chosen per the table above):

```
CreateSubscriptionRequest { Subscription = new CreateSubscription {
    ProductHandle           = <plan handle>,
    CustomerId              = <Customer.Id>,
    Reference               = <app idempotency key>,
    PaymentCollectionMethod = <Remittance | Invoice, from Site.RelationshipInvoicingEnabled>,
    // NextBillingAt        = <future timestamp>   // add only if the 422 persists; doc-settled no-capture lever
} }
```

---

## 3. Trap notes

Each names a hazard and its consequence; the answer is in the skill, which you must load.

- ⚠ **Step 1 (client registration)** — the SDK's DI helper registers a **singleton** client holding one
  `IHttpClientFactory`-created `HttpClient` for the process lifetime; whether that lifetime is the right
  one, and what still has to be wired for handler rotation, is not visible from the registration call.
  **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ **Step 1 (retries/timeouts)** — `options.Retry`'s member names do **not** tell you what `Timeout`
  bounds, nor which of your calls can actually be re-sent. `POST /subscriptions.json` (O8) and
  `POST /customers.json` (O6) are non-idempotent writes and the hero flow's whole promise is "no duplicate
  customer, no duplicate subscription", so whether a failed write can be re-sent decides the design of
  step 3 — resolve it from the skill before you write the retry configuration or the idempotency check.
  **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ **Step 1 (credentials)** — where credentials must be set relative to client construction, and how the
  key should reach the options object, are not implied by the `BasicAuthCredentials` shape.
  **MUST load `dotnet-authentication`**.
- ⚠ **Steps 2–4 (every call)** — O1, O2, O5, O9, O10 all have nullable parameters with **no C# default**
  that must be passed explicitly; a positional call mis-binds silently rather than failing to compile.
  How to call these safely is the skill's subject. **MUST load `dotnet-calling-endpoints`** before the
  first `client.*` call.
- ⚠ **Steps 2–4 (models)** — the enums here are `StringEnum<T>` records rather than C# enums, unmodeled
  JSON has a defined fate on deserialize, and building request records with `required` members has rules
  the signature does not show. **MUST load `dotnet-models`** before constructing any request payload or
  mapping a response onto an eShopOnWeb DTO.
- ⚠ **Step 5 (error boundary)** — which exception types actually reach a catch block, and how a catch
  ladder over this SDK goes silently wrong, is exactly what the signatures hide (see also the two
  mandatory rows in §4). **MUST load `dotnet-error-handling`** before writing any `try`/`catch`.
- ⚠ **Step 6 (tests)** — the `HttpClient` constructor argument is the seam; how to fake it without
  binding tests to SDK internals is the skill's subject. **MUST load `dotnet-testing`** before stubbing
  the SDK.

---

## 4. REQUIRED READING — load **before implementation starts**

This sheet deliberately does **not** carry these skills' contents. Load each one at the step it governs.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, options shape, `HttpClient` ownership and lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying the Basic credentials, where and when |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, base-URL/server selection, pagination, logging |
| `dotnet-calling-endpoints` | Steps 2–4 — required vs optional params, named arguments, envelopes, cancellation |
| `dotnet-models` | Steps 2–4 — request models, `required` members, nullability, `StringEnum<T>`, wire names |
| `dotnet-error-handling` | Step 5 — the error/exception boundary |
| `dotnet-testing` | Step 6 — the test seam |

**Two hazard rows that must shape the boundary from the first version — `System.Text.Json.JsonException`
reaches it from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to
  a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries
  something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Concrete places these bite in this scope (all map/source-visible): `ProductResponse.Product`,
`CustomerResponse.Customer` and `SiteResponse.Site` are `required`, so a 2xx body missing `product` /
`customer` / `site` throws `JsonException` on O2/O3/O5/O6/O4 — direction one. `ErrorListResponse1.Errors`
is `required` and `ApiError.FromJson<T>` does not catch, so a 422 from O8 whose body is not
`{"errors":[…]}` throws `JsonException` instead of `SdkException<CreateSubscriptionError>` — direction
two, on the hero flow's own write. Sources: `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`,
`records-3-Of-Su.md`, `Core/ErrorResponse/ApiError.cs`.

---

## 5. Assumptions & Blockers

**Blockers:** none. In particular the verbatim base-URL question resolves in your favour —
`options.Server.Production.Us.BaseUrl` is an ordinary `string` used with `String.Replace("{site}", …)`, so
a URL with no `{site}` token is used verbatim (§2.1). Nothing in this scope requires an operation the map
lacks.

**Assumptions about intent** (each is a decision, not a map fact):

| Assumption | Detail |
|---|---|
| Product family resolution | `Maxio:ProductFamilyHandle` is matched against `ProductFamily.Handle` from O1, case-sensitively, and exactly one family matches. If zero match, the plans endpoint returns an empty list rather than an error — confirm that is the desired behaviour. |
| Customer reference key | The idempotency key is a stable string derived from the eShopOnWeb user identity. The SDK contract is only that `reference` must be unique per site (CreateCustomer Notes). **What that key is derived from — the email, the ASP.NET Identity user id, or something else — is `YOUR CALL — not in the map`**; the map cannot tell you which of the app's identifiers is stable across email changes. |
| Customer names | `CreateCustomer.FirstName`/`LastName` are C# `required` while a JWT may carry only an email. Supplying values for them is forced by the compiler; how they are derived is `YOUR CALL — not in the map`. |
| Subscription reference key | Step 3's second idempotency check uses `CreateSubscription.Reference` + `FindSubscription` (O9). The XML doc confirms `reference` is "the reference value (provided by your app) for the subscription itself" and O9 looks a subscription up by it — but **no Notes or doc-comment in the map or the source says Maxio enforces uniqueness on subscription `reference`** (unlike customer `reference`, where CreateCustomer's Notes say so explicitly). **UNVERIFIED.** Directive: do not rely on the provider rejecting a duplicate. Guard the write on the application's own side (the shape of that guard — a lock, a uniqueness constraint, an outbox — is `YOUR CALL — not in the map`), use `FindSubscription`/`ListCustomerSubscriptions` as a *pre*-check only, and treat a successful create whose returned `Subscription.Reference` does not echo your key as a signal to re-read rather than to retry. |
| ~~Payment-method-not-required creation~~ | **WITHDRAWN — this assumption was tested against live traffic and is wrong.** It claimed no extra field was needed for a no-payment-profile signup. A `CreateSubscription` carrying only `ProductHandle`/`CustomerId`/`Reference` returned 422 `["No payment method was on file for the $299.00 balance"]` on a product with `RequireCreditCard == false`. Superseded in full by **§2.7**, which is a settled contract requirement, not an assumption: `PaymentCollectionMethod` must be set, to the architecture-correct value read from `Site.RelationshipInvoicingEnabled`. The residual uncertainty (what `remittance`/`invoice` actually *do*, and which `State` comes back) is carried as **UNVERIFIED** inside §2.7 with defensive directives. |
| Plan pricing shown | Prices are taken from `Product.PriceInCents` (the product's default price point). If the site later attaches non-default price points, those are a separate controller (`ProductPricePoints`) and out of this scope. |
| `my-subscriptions` filtering | O7 returns **all** of the customer's subscriptions including `canceled`/`expired`; "current" is produced by filtering on `Subscription.State` in memory. Which states count as current is `YOUR CALL — not in the map`. |
| Caller identity | Resolved from the app's own JWT/identity path — `YOUR CALL — not in the map`. |
| Startup validation | Failing fast when `Maxio:ApiKey` is absent (rather than letting the SDK send unauthenticated requests that 401) is `YOUR CALL — not in the map`. |
