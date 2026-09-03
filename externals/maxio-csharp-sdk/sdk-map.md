<!-- Generated file — do not edit; regenerated with the SDK. -->

# SDK map — Maxio (.NET)

> A generated table of contents for this SDK. Consult this map and its sub-pages to learn signatures, error types, and server/auth wiring **by lookup**. Model shapes and enum values are *not* duplicated here — the map names the file declaring each type; read the shape there. The compiler is the backstop: a wrong name fails to build.

|  |  |
| --- | --- |
| SDK display name | Maxio |
| Root namespace | `Maxio` |
| Target framework | `netstandard2.0` (C# `LangVersion 14`, `Nullable enable`) |
| API spec version | `1.0` |
| Generator | APIMatic |

Staleness check: the API spec version above changes when the SDK is regenerated from a new spec. If a lookup here fails to compile, trust the compiler and re-read the source file named in the row.

All `Source` paths on this map and its sub-pages are relative to the **SDK root** — the directory holding this file and `Maxio.csproj` — never to the page that carries them. Open them as-is from the SDK root, from any page; if the SDK sits under a subdirectory of a larger repo, prefix that subdirectory.

---

## Getting a client

```csharp
var httpClient = new HttpClient();
// TODO: configure more client options here
var options =
    new MaxioClientOptions
    {
        BasicAuth = new BasicAuthCredentials
        {
            Username = "YOUR_USERNAME",
            Password = "YOUR_PASSWORD",
        },
        BearerAuth = "YOUR_BEARER_TOKEN",
        Environment = ServerEnvironment.Us,
    };
var client = new MaxioClient(httpClient, options);
```

DI alternative (`services.AddMaxioClient`):

```csharp
services.AddMaxioClient(options =>
    {
        options.BasicAuth =
            new BasicAuthCredentials
            {
                Username = "YOUR_USERNAME",
                Password = "YOUR_PASSWORD",
            };
        options.BearerAuth = "YOUR_BEARER_TOKEN";
        options.Environment = ServerEnvironment.Us;
        // TODO: configure more client options here
    });
```

Every API group is a property on the client (e.g. `client.ApiExports`). Source: `MaxioClient.cs`. The only constructor is `MaxioClient(HttpClient httpClient, MaxioClientOptions options)`.

All `MaxioClientOptions` properties (source: `MaxioClientOptions.cs`):

| Property | Type |
| --- | --- |
| `Environment` | `ServerEnvironment` |
| `Retry` | `RetryOptions` |
| `Logging` | `LoggingOptions` |
| `Server` | `ServerOptions` |
| `Hooks` | `IReadOnlyList<SdkHook>` |
| `BasicAuth` | `BasicAuthCredentials?` |
| `BearerAuth` | `string?` |

`RetryOptions` members (namespace `Maxio.Core.Configuration` — add `using Maxio.Core.Configuration;`; source: `Core/Configuration/RetryOptions.cs`; all members are `required`, so build a full instance or start from `RetryOptions.Default()`):

| Member | Type |
| --- | --- |
| `StatusCodesToRetry` | `IReadOnlyList<HttpStatusCode>` |
| `HttpMethodsToRetry` | `IReadOnlyList<HttpMethod>` |
| `MaxRetries` | `int` |
| `Delay` | `TimeSpan` |
| `Timeout` | `TimeSpan?` |
| `BackOffFactor` | `int` |
| `UseExponentialBackoff` | `bool` |
| `MaxJitter` | `TimeSpan` |
| `OnRetry` | `Action<RetryAttempt>?` |

---

## Error-handling model (read once — applies to every operation)

Operations are **throw-based**. On an error status the SDK throws `SdkException<TError>` (`Core/Exceptions/SdkException.cs`) exposing `.Error` of type `TError`. There are two cases:

- **Case A — typed error.** `TError` is a generated `…Error : ApiError` class with status-specific `TryGet…(out …)` accessors (each returns `true` when that shape is present) plus the inherited `TryGetRawError(out RawError)` fallback. The operation blocks name the exact `TryGet…` methods and the HTTP status each maps to.
- **Case B — raw error.** `TError` is `RawError` (`Core/ErrorResponse/RawError.cs`): `StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?`.

Core error types (`Core/ErrorResponse/`) — public members with their **declared types**, verbatim from source:

| Type | Public members | Source |
| --- | --- | --- |
| `ApiError` — abstract base of the 166 typed error classes in `Errors/` | `TryGetRawError(out RawError error): bool` | `Core/ErrorResponse/ApiError.cs` |
| `RawError` | `StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` | `Core/ErrorResponse/RawError.cs` |

Typed-error payload shapes (the `out` types in each operation page's error-accessor cells) are ordinary records/unions — no special handling. The operation's **Type sources** table gives the file that declares each one; read field names, declared types, and JSON wire names there, as for any other model.

```csharp
try
{
    var response = await client.ApiExports.ExportInvoices();
}
catch (SdkException<ExportInvoicesError> ex)
{
    // Case A — typed error
    if (ex.Error.TryGetNoContent(out var error))
    {
        // Handle 404
    }
    else if (ex.Error.TryGetRawError(out var raw))
    {
        // Any other error status
    }
}
catch (SdkException<RawError> ex)
{
    // Case B — raw error
    // ex.Error.StatusCode, ex.Error.ReadAsString(), ex.Error.ReadAsJson<T>()
}
```

**No-throw (`…Result`) variants: absent across this SDK** — every operation is throw-only. Of **250 operations**, **166 are Case A (typed)** and **84 are Case B (raw)**.

---

## Operations — by controller (34 groups, 250 operations)

Each links to a sub-page with one row per operation: signature with must-pass-explicitly params and defaults, query-param wire names, return type, error Case A/B, and Case A's typed accessors with their statuses. Each operation also carries a **Type sources** table — every type it names, with the file that declares it — so resolving a body, return, or error payload to its source is a lookup, never a search. `RawError` is excluded there (its members and path are above); an operation with no table names nothing but primitives and `RawError`.

**Each row states what is specific to its operation. Everything below holds for EVERY operation unless that operation's row says otherwise, so a row silent on one of these points is telling you the default here applies — take it and move on rather than opening the source to confirm it.**

| Applies to every operation | Stated where | A row appears only when |
| --- | --- | --- |
| **Throw-only** — no `…Result`/no-throw variant exists anywhere in this SDK | this page, Error-handling model | a no-throw sibling exists (none do at this SDK version) |
| **No pagination** — the operation returns a single response, not a `Pageable` | here | pagination is offered — the block carries a **Pagination** bullet naming the posture (page-, offset-, cursor- or link-based, or the `page`-without-page-size case) |
| **Case B error accessors are always these four** — `StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` | the `RawError` row above | never — a `Case B` label always implies exactly these four; Case A rows list their own typed accessors |
| **Server group `Production`** — base URL per Servers & auth below | here | the operation is on another group — its block carries a **Server group** bullet |
| **Parameter names are literal** — signatures are generated code verbatim; in named arguments use the exact parameter names shown (the cancellation-token parameter is named `ct`) | here | never — it always holds |

**The HTTP verb and route live on the operation itself**, in the source file named at the top of its operations page. This map is method-first: the C# method is the interface you call. When something wire-level needs the route — reproducing a raw request, pointing the client at a mock, reading a provider-side log — read it from that file; do not reconstruct it from memory or infer it from the method name.

**The endpoint's behavioural prose lives there too**, as the XML `<remarks>` on the method. Rows here give you the contract — names, types, shapes, errors. Where an operation's *semantics* decide what you must pass — a parameter whose value changes server-side behaviour, an ordering or exclusivity rule between fields — that is what `<remarks>` settles; read it there rather than filling it in from memory.

| Controller (`client.X`) | Ops | Page |
| --- | --- | --- |
| `ApiExports` | 9 | [map/operations/ApiExports.md](map/operations/ApiExports.md) |
| `AdvanceInvoice` | 3 | [map/operations/AdvanceInvoice.md](map/operations/AdvanceInvoice.md) |
| `BillingPortal` | 4 | [map/operations/BillingPortal.md](map/operations/BillingPortal.md) |
| `ComponentPricePoints` | 12 | [map/operations/ComponentPricePoints.md](map/operations/ComponentPricePoints.md) |
| `Components` | 12 | [map/operations/Components.md](map/operations/Components.md) |
| `Coupons` | 14 | [map/operations/Coupons.md](map/operations/Coupons.md) |
| `CustomFields` | 9 | [map/operations/CustomFields.md](map/operations/CustomFields.md) |
| `Customers` | 7 | [map/operations/Customers.md](map/operations/Customers.md) |
| `Events` | 3 | [map/operations/Events.md](map/operations/Events.md) |
| `EventsBasedBillingSegments` | 6 | [map/operations/EventsBasedBillingSegments.md](map/operations/EventsBasedBillingSegments.md) |
| `Insights` | 4 | [map/operations/Insights.md](map/operations/Insights.md) |
| `Invoices` | 19 | [map/operations/Invoices.md](map/operations/Invoices.md) |
| `MaxioGateway` | 1 | [map/operations/MaxioGateway.md](map/operations/MaxioGateway.md) |
| `Offers` | 5 | [map/operations/Offers.md](map/operations/Offers.md) |
| `PaymentProfiles` | 12 | [map/operations/PaymentProfiles.md](map/operations/PaymentProfiles.md) |
| `ProductFamilies` | 4 | [map/operations/ProductFamilies.md](map/operations/ProductFamilies.md) |
| `ProductPricePoints` | 11 | [map/operations/ProductPricePoints.md](map/operations/ProductPricePoints.md) |
| `Products` | 6 | [map/operations/Products.md](map/operations/Products.md) |
| `ProformaInvoices` | 10 | [map/operations/ProformaInvoices.md](map/operations/ProformaInvoices.md) |
| `ReasonCodes` | 5 | [map/operations/ReasonCodes.md](map/operations/ReasonCodes.md) |
| `ReferralCodes` | 1 | [map/operations/ReferralCodes.md](map/operations/ReferralCodes.md) |
| `SalesCommissions` | 3 | [map/operations/SalesCommissions.md](map/operations/SalesCommissions.md) |
| `Sites` | 3 | [map/operations/Sites.md](map/operations/Sites.md) |
| `SubscriptionComponents` | 17 | [map/operations/SubscriptionComponents.md](map/operations/SubscriptionComponents.md) |
| `SubscriptionGroupInvoiceAccount` | 4 | [map/operations/SubscriptionGroupInvoiceAccount.md](map/operations/SubscriptionGroupInvoiceAccount.md) |
| `SubscriptionGroupStatus` | 4 | [map/operations/SubscriptionGroupStatus.md](map/operations/SubscriptionGroupStatus.md) |
| `SubscriptionGroups` | 9 | [map/operations/SubscriptionGroups.md](map/operations/SubscriptionGroups.md) |
| `SubscriptionInvoiceAccount` | 7 | [map/operations/SubscriptionInvoiceAccount.md](map/operations/SubscriptionInvoiceAccount.md) |
| `SubscriptionNotes` | 5 | [map/operations/SubscriptionNotes.md](map/operations/SubscriptionNotes.md) |
| `SubscriptionProducts` | 2 | [map/operations/SubscriptionProducts.md](map/operations/SubscriptionProducts.md) |
| `SubscriptionRenewals` | 11 | [map/operations/SubscriptionRenewals.md](map/operations/SubscriptionRenewals.md) |
| `SubscriptionStatus` | 10 | [map/operations/SubscriptionStatus.md](map/operations/SubscriptionStatus.md) |
| `Subscriptions` | 12 | [map/operations/Subscriptions.md](map/operations/Subscriptions.md) |
| `WebhooksApi` | 6 | [map/operations/WebhooksApi.md](map/operations/WebhooksApi.md) |

---

## Models — where they live, how to build them

**Shapes live only in the source.** Every file under `Models/` and `Errors/` declares exactly one public type, named after the file, and no two share a name — so a type name *is* its path. Take it from the operation's **Type sources** table, or build it from the kind's directory below. Never grep for a type.

| Group | Count | Directory (file = `<TypeName>.cs`) |
| --- | --- | --- |
| Records (plain `record` data models) | 563 | `Models/` |
| Unions (`OneOf`) — variant factories + `TryGet…` | 7 | `Models/OneOf/` |
| Unions (`AnyOf`) — variant factories + `TryGet…` | 83 | `Models/AnyOf/` |
| Enums (`StringEnum<T>` / `IntEnum<T>`) — C# member names + wire values | 101 | `Models/Enums/` |
| Typed error classes (`: ApiError`, one per Case A operation) | 166 | `Errors/` |

Conventions: records are immutable, `init`-only; `required` properties must be set in the object initializer; `T?` is optional. A field's wire name is its `[JsonPropertyName]` and often differs from the C# name (`AmountInCents` ↔ `amount_in_cents`) — read it off the property, don't derive it. `OneOf`/`AnyOf` unions wrap `Optional<T>` variants — build via static factory or implicit conversion, read via `TryGet…(out …)`; `AllOf` compositions are not unions — every constituent is a `required` property, so set them all, and those constituent properties carry no `[JsonPropertyName]` and have no wire name of their own, because the generated converter flattens each constituent's own fields directly into the one parent JSON object. Enums are **not** C# enums — build with `Type.FromValue("wire")` or the static members, whose names are PascalCase even when the wire value isn't (`CollectionMethod.Invoice`, not `.invoice`).

Namespaces by content type (add `using` accordingly):

| Contents | Namespace |
| --- | --- |
| Client & options (root) | `Maxio` |
| Operation controllers (`Api/`) | `Maxio.Api` |
| Records (`Models/`) | `Maxio.Models` |
| Enums (`Models/Enums/`) | `Maxio.Models.Enums` |
| OneOf unions (`Models/OneOf/`) | `Maxio.Models.OneOf` |
| AnyOf unions (`Models/AnyOf/`) | `Maxio.Models.AnyOf` |
| Error classes (`Errors/`) | `Maxio.Errors` |

---

## Servers & auth

**Basic auth.** Set `options.BasicAuth = new BasicAuthCredentials { Username = …, Password = … }`. The `username` is a Maxio Chargify API key and the `password` is `x`. Basic authentication works only with the US and EU environments, which connect to `chargify.com` directly. The Maxio API Gateway environment does not accept Basic authentication.

**Bearer token.** Set `options.BearerAuth = "<token>"`. A Maxio API Gateway connector token — the only authentication the gateway accepts. Use it with the Maxio API Gateway environment. This token is issued by your connector and is separate from your Chargify API key. Depending on how the connector was created, it is either a static connector API token you copy from your connector settings (long-lived, valid until you rotate it) or an access token you obtain by exchanging OAuth2 client credentials at `https://<connector>.api.maxio.com/oauth/token`.

**Environments.** `options.Environment` selects the target environment (`Servers/ServerEnvironment.cs`):

| Environment | Value | Hosting |
| --- | --- | --- |
| `ServerEnvironment.Us` *(default)* | `US` | Default Advanced Billing environment hosted in US. Valid for the majority of our customers. |
| `ServerEnvironment.Eu` | `EU` | Advanced Billing environment hosted in EU. Use only when you requested EU hosting for your AB account. |
| `ServerEnvironment.MaxioApiGateway` | `Maxio API Gateway` | Access Advanced Billing through a Maxio API Gateway connector. Authenticate with your connector Bearer token instead of Basic auth. Events-Based Billing ingestion does not go through the gateway and keeps its direct URL. |

**3 server groups.** Base-URL templates and override points (`options.Server.…`):

| Group | `Us` base URL | `Eu` base URL | `MaxioApiGateway` base URL | Override point |
| --- | --- | --- | --- | --- |
| `Production` | `https://{site}.chargify.com` | `https://{site}.ebilling.maxio.com` | `https://{connector}.api.maxio.com/api/v1/billing` | `options.Server.Production.Us.BaseUrl` (and the other environments) |
| `Ebb` | `https://events.chargify.com/{site}` | `https://events.chargify.com/{site}` | `https://events.chargify.com/{site}` | `options.Server.Ebb.Us.BaseUrl` (and the other environments) |
| `Oauth` | `https://{connector}.api.maxio.com` | `https://{connector}.api.maxio.com` | `https://{connector}.api.maxio.com` | `options.Server.Oauth.Us.BaseUrl` (and the other environments) |

`Production` · `Us` template variables: `{site}` defaults to `"subdomain"` — override `options.Server.Production.Us.Site`.

`Production` · `Eu` template variables: `{site}` defaults to `"subdomain"` — override `options.Server.Production.Eu.Site`.

`Production` · `MaxioApiGateway` template variables: `{connector}` defaults to `"connector"` — override `options.Server.Production.MaxioApiGateway.Connector`.

`Ebb` · `Us` template variables: `{site}` defaults to `"subdomain"` — override `options.Server.Ebb.Us.Site`.

`Ebb` · `Eu` template variables: `{site}` defaults to `"subdomain"` — override `options.Server.Ebb.Eu.Site`.

`Ebb` · `MaxioApiGateway` template variables: `{site}` defaults to `"subdomain"` — override `options.Server.Ebb.MaxioApiGateway.Site`.

`Oauth` · `Us` template variables: `{connector}` defaults to `"connector"` — override `options.Server.Oauth.Us.Connector`.

`Oauth` · `Eu` template variables: `{connector}` defaults to `"connector"` — override `options.Server.Oauth.Eu.Connector`.

`Oauth` · `MaxioApiGateway` template variables: `{connector}` defaults to `"connector"` — override `options.Server.Oauth.MaxioApiGateway.Connector`.

Retry/resilience is configurable via `options.Retry` (`RetryOptions`, backed by Polly).

