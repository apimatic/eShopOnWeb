# Twilio integration plan — eShopOnWeb order SMS notifications

SDK: `TwilioSdk` (APIMatic-generated, root namespace `TwilioSdk`, netstandard2.0). Built from
source, vendored into the repo (see §Scope). All contract facts below come from the SDK map
(`sdk-map.md` + `map/operations/*`) and the map-named declaring source files, read this session.

---

## 1. Scope & sequence

The integration is additive to eShopOnWeb. It lives in three layers, matching the repo:
`ApplicationCore` (entities, interfaces, DTOs of the domain), `Infrastructure` (Twilio SDK
adapter, EF config), `PublicApi` (JWT endpoints).

Build order:

1. **Vendor the SDK.** The SDK is not on NuGet; the getting-started skill says build from source
   and reference the assembly. Copy the SDK source into `src/TwilioSdk/` (its own project, CPM
   opted out) and add a `ProjectReference` from `Infrastructure`. The temp read-only map clone is
   a *separate* thing and stays in temp.
2. **Domain + persistence.** `ContactNumber` and `OrderNotification` aggregate roots; EF
   configs; `CatalogContext` DbSets; reuse existing `IRepository<T>`.
3. **Twilio config + adapter.** `TwilioOptions` bound from `Twilio:` section with fail-fast;
   register `TwilioSdkClient` via `AddTwilioSdkClient`; `ISmsGateway` adapter wrapping the SDK
   operations below; `IOrderNotificationService` orchestrating domain + gateway.
4. **Endpoints.** All flows as `IEndpoint` minimal-API endpoints under `/api/`.
5. **Self-verify** end to end against the live account.

Operations used (all others out of scope):

| Step | Operation | Purpose |
| --- | --- | --- |
| Register number | `LookupsV2PhoneNumber.FetchPhoneNumber3` | reject unusable destination; store provider's canonical E.164 |
| Order placed / dispatched / cancelled notice / resend | `Api20100401Message.CreateMessage` (immediate) | send SMS from `Twilio:FromNumber` |
| Dispatch follow-up | `Api20100401Message.CreateMessage` (scheduled) | queue "how did delivery go" with provider, `ScheduleType=fixed`, `SendAt`=+3d, via `MessagingServiceSid` |
| Refresh outcome / notifications views | `Api20100401Message.FetchMessage` | read current delivery status + SID |
| Cancel follow-up | `Api20100401Message.UpdateMessage` (`status=canceled`) | call off not-yet-sent scheduled message |
| Content disposal | `Api20100401Message.UpdateMessage` (`body=""`) | redact body at provider; record + status survive |
| Reconciliation | `Api20100401Message.ListMessage` | provider's own record for a range, filtered by `From` = configured number |

No capability required by the task is missing from the map. `DeleteMessage` exists but is **not
used**: deleting the record would destroy the status the task requires to survive content
disposal; redaction via `UpdateMessage(body:"")` is the documented redact path (method summary:
"used to redact Message body text and to cancel not-yet-sent messages").

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C#
> identifier; in named arguments use exactly those names (the cancellation-token parameter is
> named `ct`, so write `ct:`). Nullable params with no default **must be passed explicitly** —
> pass `null` to skip.
> ⚠ **Every SDK type is written fully-qualified** with the namespace its source path implies,
> taken from the path the map gives for THAT type (`Models/` → `TwilioSdk.Models`,
> `Models/Enums/` → `TwilioSdk.Models.Enums`, `Core/Authentication/Basic/` →
> `TwilioSdk.Core.Authentication.Basic`, controllers `Api/` → `TwilioSdk.Api`).

### Operations

| Operation | Signature (verbatim) | Inputs the integration sets | Response fields read | Error | Source |
| --- | --- | --- | --- | --- | --- |
| `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` = caller-typed number; all others `null` (default validity-only lookup) | `Valid: bool?`, `PhoneNumber: string?` (canonical E.164 — this is what we store), `ValidationErrors` | `SdkException<RawError>` — Case B | map `LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | see **CreateMessage fields** below | `Sid`, `Status`, `To`, `From`, `ErrorCode`, `ErrorMessage`, `DateSent`, `DateCreated` | `SdkException<RawError>` — Case B | map `Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` | `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `To`, `From`, `Body`, `DateSent` | `SdkException<RawError>` — Case B | map `Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `from` = `Twilio:FromNumber` (server-side sender filter); `dateSentQueryQuery` = range **from** (wire `DateSent>`); `dateSentQuery` = range **to** (wire `DateSent<`); `pageSize` = 1000; page via `page`/`pageToken` from `NextPageUri` | `Messages: IReadOnlyList<ApiV2010AccountMessage>`, `NextPageUri` | `SdkException<RawError>` — Case B | map `Api20100401Message.md`; `Models/ListMessageResponse.cs` |
| `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | redact: `body=""`, `status=null`. cancel: `body=null`, `status=MessageEnumUpdateStatus.Canceled` | `Sid`, `Status`, `Body` | `SdkException<RawError>` — Case B | map `Api20100401Message.md`; `Api/Api20100401Message.cs` remarks |

**CreateMessage fields** (only these set; every other nullable param passed as `null` → provider default):

| Field | Immediate send | Scheduled follow-up | purpose |
| --- | --- | --- | --- |
| `to` | canonical E.164 of shopper | canonical E.164 of shopper | required destination |
| `body` | notice text | "how did delivery go" text | message content |
| `from` | `Twilio:FromNumber` | `null` | sender for immediate sends; **omit → use MessagingServiceSid instead** (scheduling requires a Messaging Service, not a From) |
| `messagingServiceSid` | `null` | `Twilio:MessagingServiceSid` | scheduling is Messaging-Service-only |
| `scheduleType` | `null` | `MessageEnumScheduleType.Fixed` | omit → immediate; `fixed` = scheduled |
| `sendAt` | `null` | now + 3 days (UTC) | when the follow-up goes out |

Wire facts confirmed from source (`Core/ParameterFlattener.cs`, `Core/Request/FormUrlEncodedRequest.cs`,
`Api/Api20100401Message.cs`): a `null` form Param is **omitted** from the body; an empty-string
Param (`body:""`) **is sent** as `Body=` — that is exactly what triggers provider-side redaction.
`ListMessage` serializes `DateSent</DateSent>` via `ToIso8601()` (full `yyyy-MM-ddTHH:mm:ss.fffZ`),
so the range filter is time-precise, not day-granular.

### Enums (only members used)

| Enum | Member → wire | Source |
| --- | --- | --- |
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `.Fixed` → `fixed` | `Models/Enums/MessageEnumScheduleType.cs` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `.Canceled` → `canceled` | `Models/Enums/MessageEnumUpdateStatus.cs` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` (read only) | `queued/sending/sent/delivered/undelivered/failed/scheduled/canceled/accepted/read/received/receiving/partially_delivered` | `Models/Enums/MessageEnumStatus.cs` |

Delivery-outcome interpretation (application decision): **reached** = `delivered`/`read`;
**in-flight** = `queued`/`sending`/`sent`/`accepted`/`scheduled`; **not reached** =
`failed`/`undelivered`/`canceled`. Resend is offered when the last known status is a *not reached*
state. Enum is `StringEnum<T>`; compare via `.Value` or the static members.

### Client construction / auth / server node

- DI: `services.AddTwilioSdkClient(options => { ... })` (`ServiceCollectionExtensions.cs`). It calls
  `AddHttpClient()` and registers a **singleton** `TwilioSdkClient` built from an options object
  captured **once at registration** (via `IHttpClientFactory`). Rotation ⇒ restart (see §5 row 2).
- Auth: `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials
  { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }`. `Username`/`Password` are
  `required`. Source: `AuthSchemes.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`.
- `accountSid` method argument (path template `/2010-04-01/Accounts/{AccountSid}/...`) =
  `Twilio:AccountSid`.
- Server node: messaging ops (`Api20100401Message`) resolve through group **`Default`**
  (`https://api.twilio.com`). When `Twilio:BaseUrl` is set, override **only**
  `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (source `ServerOptions.cs`,
  `Servers/DefaultOptions.cs`). Lookups resolve through group **`Default4`**
  (`https://lookups.twilio.com`) and are **not** governed by `Twilio:BaseUrl` — leave `Default4`
  at its default, matching the task ("this setting does not govern those").

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A message may only be sent to a number the provider accepted as a usable destination | `CreateMessage` ← `FetchPhoneNumber3` | application: only stored (Lookup-validated, canonical) `ContactNumber`s are ever messaged; after a `ContactNumber` is deleted, nothing is sent to it |
| Content disposal / resend / cancel act only on a message this app created | `UpdateMessage`/`FetchMessage` ← the `OrderNotification` whose `ProviderMessageSid` we stored from `CreateMessage` | application: operator endpoints resolve the SID from our own `OrderNotification` record, never a caller-supplied SID |
| A caller may act only on their own contact numbers and orders | all shopper endpoints | application: every shopper query is scoped by `BuyerId` = token identity name |

---

## 3. Trap notes (name the hazard + the skill; do not resolve here)

- **Client & HttpClient lifetime / DI shape.** `AddTwilioSdkClient` builds options once and captures
  a singleton; getting the HttpClient ownership and the "rotated secret needs restart" consequence
  right is not visible from the signature. → **MUST load `twilio-platforms-team:dotnet-client-initialization`**.
- **Credential wiring / 401 vs unsent credential.** A credential never set is *skipped*, not thrown —
  a missing part can surface as a silent no-auth send. → **MUST load `twilio-platforms-team:dotnet-authentication`**.
- **Calling ops with 24 positional nullables.** Mis-binding a positional argument in `CreateMessage`
  is silent; named arguments and the injected-vs-real idempotency-key distinction matter. →
  **MUST load `twilio-platforms-team:dotnet-calling-endpoints`**.
- **Building request models / reading `StringEnum` + `AdditionalProperties`.** `MessageEnumStatus`
  is not a C# enum; response carries extension data. → **MUST load `twilio-platforms-team:dotnet-models`**.
- **Error boundary — two JsonException directions + Case B accessors.** A drifted 2xx body throws
  `JsonException` (not `SdkException`); `RawError` is Case B with `StatusCode`/`ReadAsString`. →
  **MUST load `twilio-platforms-team:dotnet-error-handling`**.
- **Timeout is per-attempt, retries, base-URL override, `LogRequestBody` unredacted, pagination.**
  A hung retryable GET costs a multiple of `Timeout`; the messaging base URL override and the
  SMS-body logging risk live here. → **MUST load `twilio-platforms-team:dotnet-configuration-resilience`**.
- **Faking the SDK seam for tests.** The `HttpClient` ctor arg is the seam. →
  **MUST load `twilio-platforms-team:dotnet-testing`** (only if adding SDK-level tests).

---

## 4. REQUIRED READING (load all before implementation; contents deliberately not restated here)

| Skill | Governs |
| --- | --- |
| `twilio-platforms-team:dotnet-client-initialization` | client construction + DI registration |
| `twilio-platforms-team:dotnet-authentication` | Basic-auth credential wiring |
| `twilio-platforms-team:dotnet-calling-endpoints` | every `client.*` call (named args) |
| `twilio-platforms-team:dotnet-models` | request bodies, `StringEnum`, response models |
| `twilio-platforms-team:dotnet-error-handling` | the try/catch around every SDK call + middleware |
| `twilio-platforms-team:dotnet-configuration-resilience` | timeout budget, base-URL override, logging posture, pagination |
| `twilio-platforms-team:dotnet-testing` | SDK-seam tests (if written) |

Mandatory hazard rows (verbatim, both directions of `System.Text.Json.JsonException`):
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it
  escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, **replacing** the `SdkException`
  and destroying the HTTP status with it. (All messaging ops here are Case B `RawError`, so the
  typed-shape mismatch is less likely, but the boundary must still catch `JsonException`.)

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `TwilioOptions` validated at startup by an `IValidateOptions`/explicit check that **refuses to start** if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is missing **or blank** (blank ≠ missing — every part checked). `BaseUrl` optional. Validation runs before the app serves traffic. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (loaded by `WebApplication.CreateBuilder`'s default config), bound from the `Twilio:` section; values never in any repo file. `AddTwilioSdkClient` captures options once at registration into the singleton client, so a rotated `AuthToken` takes effect only on **process restart** — acceptable for this app; documented, no hot-rotation path built. |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt**. Each endpoint passes the ASP.NET request `CancellationToken` (`ct`) into every SDK call so the whole call is bounded by the request lifetime; the notification path is best-effort and never blocks the order operation (see §Flow rules). Retries left at SDK default; a `CancellationToken` deadline is the only whole-call bound. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = `GET, HEAD, PUT, OPTIONS`. `CreateMessage`/`UpdateMessage` are **POST**, `DeleteMessage` DELETE — **never auto-resent by the SDK**, so no risk of a duplicate SMS from SDK retries. `FetchMessage`/`ListMessage` are GET (idempotent) — safe to retry. |
| 5 | Idempotency & ambiguous writes | The generator-injected `Idempotency-Key: Guid.NewGuid()` header is **not** a real key (fresh per call) — not relied upon. **Resend** carries a **caller-supplied idempotency key**: eShop stores it on the `OrderNotification` a resend produces and, before sending, looks up any existing notification created under the same key for that source notification; a repeat returns the existing `notificationId` and sends nothing, a fresh key sends. For the initial place/dispatch/cancel sends there is no provider key; the reconciliation report is the recovery path for an ambiguous send (SID recorded before we trust it; `ListMessage` by `From`+range surfaces provider messages eShop lost). |
| 6 | Observability | Structured `ILogger` at Information for lifecycle (order placed, message SID + status), Warning when a send fails as an outcome, Error only for unexpected faults. **Phone numbers and message bodies are never logged** (task mandate); logs carry order id, notification id, provider SID, status, `ErrorCode`. `LogRequestBody` stays **off**. The provider `ErrorCode`/`ErrorMessage` from `RawError`/message resource is captured onto the notification and logged as a code (not the body). |
| 7 | Sensitive data | In scope: **shopper phone number** (`to`) and **SMS body** — both sensitive. Posture: SDK `LogRequestBody` left `false`; `LoggingOptions.LoggerFactory` is **assigned explicitly** (the DI extension sets it from `ILoggerFactory`, non-null) so the `TWILIOCLIENT_LOG` env var cannot switch body logging on from outside code; `LogRequestHeaders`/`LogResponseHeaders` off. The app's own logs never echo `to`/`body`. Persisting `to`/`body` in the app store is not logging and is required to send/resend; content disposal clears the stored body too. |
| 8 | Environment selection | Groups touched: **`Default`** (messaging, `api.twilio.com`) and **`Default4`** (lookups, `lookups.twilio.com`). Production is the only declared environment (no sandbox in the SDK). Test traffic is kept off the live system by **only ever registering/messaging the two task-provided destinations** (`TWILIO_TEST_TO_NUMBER`, `TWILIO_UNREACHABLE_TO_NUMBER`); `Twilio:BaseUrl` overrides only `Default` and can point messaging at a mock without touching lookups. |

---

## 6. Assumptions & Blockers

- **No Blockers.** The map covers every capability the task needs.
- Assumption: `POST /api/orders` needs a ship-to address but the task's request body is only catalog
  item ids + quantities; `Order` requires an `Address`. Decision: construct a default placeholder
  `Address` (notifications, not fulfilment, are the feature). `YOUR CALL — not in the map`.
- Assumption: "a few days later" for the delivery follow-up = **+3 days** (well within the provider's
  scheduling window). `YOUR CALL — not in the map`.
- Assumption: a US destination that the carrier refuses (`TWILIO_UNREACHABLE_TO_NUMBER`) is an
  expected **outcome** (message `failed`/`undelivered`), recorded on the notification — not a gap.
- Assumption: buyer identity = JWT `ClaimTypes.Name` (the username/email), consistent with how
  eShop's Web storefront sets `Order.BuyerId`.
