# Twilio SMS order notifications — plan + contract sheet

NuGet: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace: `TwilioSdk` (map; the brief’s “Twilio” is not the generated namespace). Client: `TwilioSdk.TwilioSdkClient`.

---

## Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Bind `Twilio:*` config; construct/register `TwilioSdkClient` with Account SID + Auth Token; optionally override **only** the Messages (Default/api) base URL | client construction — `sdk-map.md` *Getting a client* / *Servers & auth* |
| 2 | Validate a shopper number on `POST /api/contact-numbers`; store the provider canonical E.164, reject if not a usable destination | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send an immediate SMS (order placed / dispatched / cancelled / operator resend) from `Twilio:FromNumber`; persist SID + initial `Status` | `Api20100401Message.CreateMessage` (no schedule) |
| 4 | After dispatch, **schedule** a follow-up SMS with Twilio for a few days later (app chooses `sendAt`; do not hold the send in-process) | `Api20100401Message.CreateMessage` (`scheduleType` + `sendAt` + `messagingServiceSid`) |
| 5 | On order cancel, cancel a not-yet-sent follow-up by SID so it never delivers | `Api20100401Message.UpdateMessage` (`status: Canceled`) |
| 6 | Poll delivery outcome by SID (no webhook URL exists) | `Api20100401Message.FetchMessage` |
| 7 | Shopper content disposal: redact body at the provider; keep the Message resource (SID/status survive) | `Api20100401Message.UpdateMessage` (`body: ""`) — **not** `DeleteMessage` |
| 8 | Reconciliation `GET /api/notifications/reconciliation?from=&to=` — list messages **filtered at the provider** by this app’s From number + DateSent range; page manually | `Api20100401Message.ListMessage` |
| 9 | Error boundary: lookup/send/fetch/list/cancel/redact; send failures are **notification outcomes** and must not fail the order operation | Case B `SdkException<RawError>` on every in-scope op |
| 10 | Tests for the integration seam | — |

Do **not** call `DeleteMessage` for content disposal — it removes the Message resource (`Api/Api20100401Message.cs`). Redact with `UpdateMessage`.

---

## CONTRACT SHEET

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

### Client construction, auth, servers

| Fact | Value | Cite |
|---|---|---|
| Client | `TwilioSdk.TwilioSdkClient` | `sdk-map.md` |
| Ctor | `TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdkClientOptions options)` | `sdk-map.md` |
| Options | `TwilioSdk.TwilioSdkClientOptions` | `TwilioSdkClientOptions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` → `Production`); `Retry: TwilioSdk.Core.Configuration.RetryOptions`; `Logging: LoggingOptions`; `Server: TwilioSdk.ServerOptions`; `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Auth property | `AccountSidAuthToken` | `sdk-map.md` *Servers & auth* |
| Credentials type | `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials` — `required string Username { get; init; }`, `required string Password { get; init; }` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Credential mapping | `Username` = `Twilio:AccountSid` (`TWILIO_ACCOUNT_SID`); `Password` = `Twilio:AuthToken` (`TWILIO_AUTH_TOKEN`) | map + this integration’s config |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `"production"`). Only member. | `Servers/ServerEnvironment.cs` |
| DI helper | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Messages host (Default) | `TwilioSdk.Servers.DefaultOptions.Production.BaseUrl` default `"https://api.twilio.com"` | `Servers/DefaultOptions.cs` |
| Lookup host (Default4) | `TwilioSdk.Servers.Default4Options.Production.BaseUrl` default `"https://lookups.twilio.com"` | `Servers/Default4Options.cs` |
| Other host (Default1) | `"https://messaging.twilio.com"` — **not** used by any operation in this plan | `Servers/Default1Options.cs` |
| `Twilio:BaseUrl` | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` only. Do **not** set `Default4` (Lookup) or `Default1`. Unset → leave constructor default. | `ServerOptions.cs`, `Server.cs` (`Default` vs `Default4`) |
| `ServerOptions` (root ns) | `TwilioSdk.ServerOptions` with `Default`, `Default1` … `Default14` each a `TwilioSdk.Servers.DefaultNOptions` | `ServerOptions.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel { get; init; }`. No header bag. | `Core/RequestOptions.cs` |

Config keys (do not hard-code values): `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, optional `Twilio:BaseUrl`.

Exact construction shape (types/property names only):

```csharp
var options = new TwilioSdk.TwilioSdkClientOptions
{
    Environment = TwilioSdk.Servers.ServerEnvironment.Production,
    AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials
    {
        Username = accountSid,
        Password = authToken
    }
};
if (!string.IsNullOrWhiteSpace(messagingBaseUrl))
{
    options.Server.Default.Production.BaseUrl = messagingBaseUrl; // Twilio:BaseUrl — Messages API only
}
var client = new TwilioSdk.TwilioSdkClient(httpClient, options);
```

Controllers used: `client.LookupsV2PhoneNumber`, `client.Api20100401Message`.

---

### 1. Lookup / validate — `FetchPhoneNumber3`

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Source | `Api/LookupsV2PhoneNumber.cs` · `map/operations/LookupsV2PhoneNumber.md` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · server **Default4 (lookups)** |
| Signature | `Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 nullable params `fields` … `partnerSubId` — pass `null` to skip |
| Path | `phoneNumber` — E.164 or national; default country +1 (`LookupsV2PhoneNumber.cs` XML) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity-match fields unused here) |
| `fields` | `string?` comma-separated. XML: `validation, caller_name, sim_swap, call_forwarding, line_status, line_type_intelligence, identity_match, reassigned_number, sms_pumping_risk, phone_number_quality_score, pre_fill`. Basic `Valid` + `PhoneNumber` are on the default body — pass `fields: null` unless an extra package is required. |
| Returns | `TwilioSdk.Models.LookupResponse` — **no wrapper envelope** |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| No-throw variant | absent |

**Response fields this integration reads** (`map/models/records-4-Li-Me.md`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | Canonical E.164 (`+` + country code + subscriber). **Store this**, not the caller’s raw input. |
| `Valid (valid)` | `bool?` | `true` iff the number is in a valid range a carrier can assign. Reject registration unless `Valid == true`. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid (see enum table). |
| `NationalFormat (national_format)` | `string?` | Optional display |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2 |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix |

Do **not** use Lookups v1 (`FetchPhoneNumber2` / `LookupsV1PhoneNumber`) — that record has no `Valid` field (`map/models/records-4-Li-Me.md`).

**Reject vs later undeliverable:** a reserved unassigned US number can still be `Valid == true` (in a dialable range). That is expected. Send will be **accepted** then carrier-refused; that is a **delivery outcome** (`failed` / `undelivered` on fetch), not a registration or send-API gap.

Invalid lookup can surface as (handle both): HTTP error → Case B `SdkException<RawError>`; or HTTP 2xx with `Valid != true`.

---

### 2–4. Send immediate / schedule follow-up — `CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Source | `Api/Api20100401Message.cs` · `map/operations/Api20100401Message.md` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default (api)** |
| Signature | `Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 24 nullable params `statusCallback` … `contentSid` (pass `null` to skip). `accountSid` and `to` are required `string`. |
| Form body (wire ← C#) | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Null params | Omitted from the form (`ParameterFlattener`: null → no field). Empty string **is** sent. |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **no wrapper** |
| Error | **Case B** `SdkException<RawError>` |
| Accessors | `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| No-throw variant | absent |

**Immediate SMS** (placed / dispatched / cancelled / operator resend):

| Param | Value |
|---|---|
| `accountSid` | `Twilio:AccountSid` |
| `to` | stored canonical E.164 |
| `from` | `Twilio:FromNumber` |
| `body` | application-composed text |
| `messagingServiceSid` | `null` |
| `scheduleType` | `null` |
| `sendAt` | `null` |
| all other optionals | `null` |

**Scheduled follow-up** (after dispatch; provider holds the send):

| Param | Value |
|---|---|
| `accountSid` | `Twilio:AccountSid` |
| `to` | stored canonical E.164 |
| `body` | follow-up text |
| `from` | `null` (Messaging Service selects the sender) |
| `messagingServiceSid` | `Twilio:MessagingServiceSid` |
| `scheduleType` | `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) |
| `sendAt` | app-chosen `DateTimeOffset` a few days out |
| all other optionals | `null` |

`MessageEnumScheduleType` XML: **Messaging Services only** — `fixed` together with the send-at field (`map/models/enums.md`; C# param is `sendAt`, wire `SendAt`, not `send_time`).

**Response fields this integration reads** (`map/models/records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message id. Pattern `^(SM\|MM)[0-9a-fA-F]{32}$`. Persist this. |
| `Status (status)` | `MessageEnumStatus?` | Initial outcome (`queued` / `accepted` / `scheduled` / …). **Accepted is success of create**, not final delivery. |
| `ErrorCode (error_code)` | `int?` | Set when status is `failed` or `undelivered`; else null |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code` (do not branch on this string programmatically — XML) |
| `To (to)` / `From (from)` | `string?` | E.164 endpoints |
| `Body (body)` | `string?` | Text (empty after redact) |
| `DateSent (date_sent)` / `DateCreated (date_created)` | `string?` | RFC 2822 GMT timestamps |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | Present when a Messaging Service was used |

**Idempotency (operator resend):** `CreateMessage` has **no** `idempotencyKey` parameter (`map/operations/Api20100401Message.md`). The generated method **always** attaches HTTP header `Idempotency-Key` with `Guid.NewGuid()` — a **new value every call** (`Api/Api20100401Message.cs`). `RequestOptions` cannot add or override headers (`Core/RequestOptions.cs`: `LogLevel` only). A caller-supplied resend key **cannot** be forwarded to Twilio. Enforce “same key → do not send a second message” in the **application** (persist the key, skip `CreateMessage` on replay). Other SDK ops that *do* take `idempotencyKey` (Payments, Conversations, UserDefinedMessage) are out of scope.

A **create HTTP error** is a send failure (record as notification outcome; **do not fail the order**). A **2xx create** with later `undelivered`/`failed` (US carrier refuse) is a **delivery outcome**, obtained via `FetchMessage` — not a gap.

---

### 5. Cancel scheduled follow-up — `UpdateMessage` (status)

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `Task<ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` (nullable, no default) |
| Form (wire ← C#) | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `map/operations/Api20100401Message.md` |

**Cancel call:** `accountSid` = `Twilio:AccountSid`; `sid` = scheduled message SID; `body: null` (omit Body); `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`). Read `Status` on the returned resource (`canceled` expected).

---

### 6. Fetch by SID — `FetchMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) |
| Signature | `Task<ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` (same fields as create) |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Cite | `map/operations/Api20100401Message.md` |

Read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`. No statusCallback/webhook — this is the only delivery-outcome path.

---

### 7. Redact body — `UpdateMessage` (body)

Same signature as §5. **Redact call:** `body: ""` (empty string, **not** `null` — null omits the field); `status: null`. After success, `Body` is empty; `Sid` / `Status` remain. Do not use `DeleteMessage` (`DELETE …/Messages/{Sid}.json` → `Task` / void) — that deletes the resource.

---

### 8. List for reconciliation — `ListMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · Default (api) |
| Signature | `Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 nullable params `to` … `pageToken` |
| Query (wire ← C#) | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, `DateSent<` ← `dateSentQuery`, `DateSent>` ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date encoding | `dateSent` / `dateSentQuery` / `dateSentQueryQuery` sent as `ToIso8601()` → `yyyy-MM-ddTHH:mm:ss.fffZ` UTC (`Core/Extensions/DateTimeOffsetExtensions.cs`) |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **none in the SDK** (no auto-pager). Manual `page` / `pageSize` / `pageToken` only. XML: `pageSize` default 50, max 1000; `page` is client state; `pageToken` is provided by the API. |
| Cite | `map/operations/Api20100401Message.md` |

**This integration’s list args:**

| C# | Value |
|---|---|
| `accountSid` | `Twilio:AccountSid` |
| `to` | `null` (do not filter by recipient) |
| `from` | `Twilio:FromNumber` — provider-side From filter (not an app-side filter of a wider list) |
| `dateSent` | `null` (not an exact-day match) |
| `dateSentQueryQuery` | reconciliation `from` (range start) → wire `DateSent>` |
| `dateSentQuery` | reconciliation `to` (range end) → wire `DateSent<` |
| `pageSize` | e.g. `1000` (max per XML) or `null` for default 50 |
| `page` / `pageToken` | first page `null`; subsequent pages from the previous response |

**Envelope** (`map/models/records-4-Li-Me.md`, `Models/ListMessageResponse.cs`):

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` — the page payload |
| `NextPageUri (next_page_uri)` | `string?` — more pages when non-null |
| `PageToken` | not a response field; use `pageToken` query on the next `ListMessage` (API-provided) |
| `Page (page)` / `PageSize (page_size)` / `Start (start)` / `End (end)` | paging metadata |
| `FirstPageUri` / `PreviousPageUri` / `Uri` | `string?` |

Loop while `NextPageUri` is present; do not list the whole account and filter From locally.

---

### Enums in scope (`map/models/enums.md`)

All are `TwilioSdk.Models.Enums.*` : `StringEnum<T>` — use static members (or `FromValue("wire")`). Compare via the member / `.Value`, not a C# `enum`.

| Type | Members (C# = wire) |
|---|---|
| `MessageEnumScheduleType` | `Fixed = "fixed"` |
| `MessageEnumUpdateStatus` | `Canceled = "canceled"` |
| `MessageEnumStatus` | `Queued = "queued"`, `Sending = "sending"`, `Sent = "sent"`, `Failed = "failed"`, `Delivered = "delivered"`, `Undelivered = "undelivered"`, `Receiving = "receiving"`, `Received = "received"`, `Accepted = "accepted"`, `Scheduled = "scheduled"`, `Read = "read"`, `PartiallyDelivered = "partially_delivered"`, `Canceled = "canceled"` |
| `ValidationError` | `TooShort = "TOO_SHORT"`, `TooLong = "TOO_LONG"`, `InvalidButPossible = "INVALID_BUT_POSSIBLE"`, `InvalidCountryCode = "INVALID_COUNTRY_CODE"`, `InvalidLength = "INVALID_LENGTH"`, `NotANumber = "NOT_A_NUMBER"` |
| `MessageEnumDirection` (on the resource; not passed in) | `Inbound = "inbound"`, `OutboundApi = "outbound-api"`, `OutboundCall = "outbound-call"`, `OutboundReply = "outbound-reply"` |

**How to read delivery status:** `ApiV2010AccountMessage.Status` is `MessageEnumStatus?`. Terminal-ish values for outbound SMS: `delivered`, `undelivered`, `failed`, `canceled`. In-flight: `accepted`, `queued`, `sending`, `sent`, `scheduled`. `undelivered` / `failed` after a successful create = carrier outcome (expected for `TWILIO_UNREACHABLE_TO_NUMBER`). `scheduled` → `canceled` after §5.

---

### Errors (every in-scope operation is Case B)

| Situation | What reaches `catch` | How to read |
|---|---|---|
| Lookup HTTP error | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` | `ex.Error.StatusCode`; body `ReadAsString()` or `ReadAsJson<T>()` |
| Lookup 2xx but unusable | **no exception** — `LookupResponse.Valid != true` (+ `ValidationErrors`) | application reject |
| Create/Fetch/List/Update HTTP error | same Case B `SdkException<RawError>` | same accessors |
| Create 2xx then carrier refuse | **no create exception** — later `FetchMessage` `Status` `undelivered`/`failed`, `ErrorCode` | delivery outcome |
| Transport / timeout | not `SdkException` — see `dotnet-error-handling` / `dotnet-configuration-resilience` | — |
| Drifted 2xx JSON / untyped error body | `System.Text.Json.JsonException` (see REQUIRED READING) | — |

`SdkException<TError>` (`Core/Exceptions/SdkException.cs`): `public required TError Error { get; init; }`. No `…Result` variants exist in this SDK (`sdk-map.md`).

There is **no** generated `{Operation}Error` / `TryGet…` on these operations — do not catch `SdkException<CreateMessageError>` etc.

---

## Trap notes

⚠ Step 1 (client registration) — `TwilioSdkClient` takes an `HttpClient` whose ownership, lifetime, and DI registration are not in the constructor; getting this wrong breaks pooling and every downstream call. **MUST load `dotnet-client-initialization`** before constructing or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — credentials live on `AccountSidAuthToken` as `BasicAuthCredentials`; when they must be set, how they are loaded, and what a 401 means are not in the property type. **MUST load `dotnet-authentication`** before wiring SID/token.

⚠ Steps 2–8 (calls) — `CreateMessage` / `FetchPhoneNumber3` / `ListMessage` have long must-pass-explicitly nullable parameter lists; a positional call silently mis-binds (`to` vs `from` vs date bounds is especially costly on `ListMessage`). **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ Steps 2–8 (models) — statuses, schedule type, update status, and lookup validation errors are `StringEnum<T>`, not C# enums; response records are `init`-only with JSON wire names that differ from C# names. **MUST load `dotnet-models`** before constructing requests or mapping `Sid`/`Status`/`Valid`/`PhoneNumber`.

⚠ Step 1 & 8 (resilience / paging / base URL) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `CreateMessage` is a POST whose retry behaviour interacts with application resend idempotency; `ListMessage` has no SDK auto-paginator so multi-page reconciliation is easy to under-bound. **MUST load `dotnet-configuration-resilience`** before registering the client or looping `NextPageUri`.

⚠ Step 9 (error boundary) — send/lookup/fetch failures and `JsonException` must be contained so an order place/dispatch/cancel still succeeds; a single-status `SdkException` catch lets other failures escape or mis-labels them. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (tests) — the test seam is not the generated controller type; faking the wrong layer tests the SDK, not the integration. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construct / DI-register `TwilioSdkClient` and `HttpClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–8 — named-argument calls, must-pass-explicitly nulls |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, wire names, `init` records |
| `dotnet-configuration-resilience` | Step 1 base URL / retry / timeout; Step 8 list pagination |
| `dotnet-error-handling` | Step 9 — exception boundary for every SDK call |
| `dotnet-testing` | Step 10 — integration test seam |

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Generated root namespace is `TwilioSdk` (map), not `Twilio`.
- Lookup v2 `FetchPhoneNumber3` is the validate+canonicalize API; v1 is unused.
- Registration rejects unless `LookupResponse.Valid == true`; stored number is `LookupResponse.PhoneNumber`.
- Immediate sends use `from` = `Twilio:FromNumber` and `messagingServiceSid: null`; scheduled sends use `messagingServiceSid` = `Twilio:MessagingServiceSid` with `scheduleType: Fixed` and `from: null`.
- Follow-up delay (“a few days”) is chosen by the application as `sendAt`.
- `Twilio:BaseUrl` overrides `Server.Default.Production.BaseUrl` (Messages on api.twilio.com), never Lookup (`Default4`) and never `Default1` (`messaging.twilio.com`).
- Content disposal = `UpdateMessage` with `body: ""`, not `DeleteMessage`.
- Operator-resend idempotency is enforced in the application because the SDK will not take the caller’s key (see Blockers).
- A failed or undeliverable SMS is recorded as a notification outcome; it must not fail place/dispatch/cancel.
- Live traffic: only `TWILIO_TEST_TO_NUMBER` (reachable CA) and `TWILIO_UNREACHABLE_TO_NUMBER` (reserved unassigned US) are registered/messaged. US undeliverable after accept is expected, not a gap.
- Nothing is pre-seeded; every Message is created dynamically.

**Blockers**

- **Caller-supplied idempotency key is not exposable on `CreateMessage`.** The map signature has no idempotency parameter; source always sends header `Idempotency-Key: Guid.NewGuid()`; `RequestOptions` cannot set headers. Provider-level “same key → same message” for `POST /api/notifications/{id}/resend` is **not available through this SDK**. Application-layer dedupe is required. This is not an invented Twilio REST claim — it is what the SDK actually sends.
- No other in-scope capability is missing from the map (lookup, send, schedule, cancel, fetch, redact, From+DateSent list, Case B errors).
