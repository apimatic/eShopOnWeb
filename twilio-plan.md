# Twilio .NET SDK — Order-Notification-by-SMS Integration Plan (eShopOnWeb)

SDK: `AsadAli.TwilioSdk` (NuGet, install version-less: `dotnet add package AsadAli.TwilioSdk`) ·
root namespace `TwilioSdk` · client `TwilioSdkClient` · options `TwilioSdkClientOptions`.
Map source commit stamp: `51fdf48`. Every row below cites the map page it came from; a handful of
client-config facts the map does not carry are cited to the named SDK source file.

This SDK is **throw-based** and every messaging/lookup operation in scope is **Case B**
(`SdkException<RawError>`) — there are **no typed error classes** and **no no-throw `…Result`
variants** anywhere in the SDK. Read status + provider error off `RawError` (see CONTRACT SHEET §E).

---

## 1. Scope & sequence

| # | Capability | Operation(s) | Controller |
|---|---|---|---|
| 0 | Client construction + DI + auth + base-URL binding | (config) | `TwilioSdkClient` / `AddTwilioSdkClient` |
| 1 | Send SMS immediately (FromNumber **or** MessagingServiceSid) | `CreateMessage` | `client.Api20100401Message` |
| 2 | Validate / canonicalize destination number **before storing** | `FetchPhoneNumber3` (Lookup v2) | `client.LookupsV2PhoneNumber` — **DIFFERENT HOST** |
| 3 | Schedule the follow-up message a few days out | `CreateMessage` (`scheduleType=Fixed`, `sendAt`) | `client.Api20100401Message` |
| 4 | Cancel a scheduled message before it sends | `UpdateMessage` (`status=Canceled`) | `client.Api20100401Message` |
| 5 | Fetch one message's delivery outcome by SID | `FetchMessage` | `client.Api20100401Message` |
| 6 | List messages for reconciliation (From + date range) | `ListMessage` | `client.Api20100401Message` |
| 7 | Redact message body at the provider (keep the sent-fact) | `UpdateMessage` (`body=""`) | `client.Api20100401Message` |

Sequence for a notification: **(2) Lookup-validate the number → store E.164 → (1) send now →
(3) schedule follow-up → later (5) fetch/reconcile or (6) list → (4) cancel follow-up on order
cancel → (7) redact on shopper request.**

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types are spread across different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones.

### Namespaces to `using` (each type below carries its own — C# does NOT import child namespaces transitively)

| Type(s) | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| Controllers are properties on the client (`client.Api20100401Message`, `client.LookupsV2PhoneNumber`) | `TwilioSdk.Api` (only if you type a controller variable) |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`, `ValidationError` | `TwilioSdk.Models` |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection` (+ other `MessageEnum*`) | `TwilioSdk.Models.Enums` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` (source: `Core/Authentication/Basic/BasicAuthCredentials.cs`) |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` (source: `Servers/*.cs`) |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` (source: `Core/Exceptions/SdkException.cs`) |
| `RawError`, `ApiError` | `TwilioSdk.Core.ErrorResponse` (source: `Core/ErrorResponse/*.cs`) |

---

### §A — Client construction, auth, base URL, environment  *(source: `sdk-map.md` §Getting a client / §Servers & auth; `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`)*

Constructor: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
DI: `services.AddTwilioSdkClient(o => { /* set o.* here */ });`

`TwilioSdkClientOptions` properties (source: `TwilioSdkClientOptions.cs`): `Environment: ServerEnvironment`,
`Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?`.

**Auth (AccountSid/AuthToken basic auth).** Set the single credentials property:
```csharp
o.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> };
```
`BasicAuthCredentials` has `required string Username` + `required string Password` (both `init`).
Username = AccountSid, Password = AuthToken. (The SDK doc-note also allows API-key SID + secret; for
this app use AccountSid/AuthToken from the `Twilio:` config section.)

**Environment.** `o.Environment = ServerEnvironment.Production;` — `Production` is the **only** member
(`ServerEnvironment` is a `StringEnum`, wire value `"production"`, source `Servers/ServerEnvironment.cs`).

**Base URL — this is the load-bearing config fact.** `ServerOptions` exposes one property per server the
API defines (`Default` … `Default14`). Each is a `{Server}Options` with a nested `ProductionOptions`
carrying a settable `BaseUrl`. The two servers in scope:

| Server | C# path to set BaseUrl | SDK default | Which ops use it |
|---|---|---|---|
| **api** (messaging) | `o.Server.Default.Production.BaseUrl` | `https://api.twilio.com` | ALL `Api20100401Message` ops (send/read/update/list/delete) — labelled "Default (api)" in the map |
| **lookups** | `o.Server.Default4.Production.BaseUrl` | `https://lookups.twilio.com` | `LookupsV2PhoneNumber.FetchPhoneNumber3` — labelled "Default4 (lookups)" |

So bind **`Twilio:BaseUrl` (when set) into `o.Server.Default.Production.BaseUrl` only** — that governs
every messaging-API call as required. It does **NOT** and MUST NOT touch `o.Server.Default4` (Lookup) —
see §B for how the Lookup host is handled separately. A literal URL with no `{placeholders}` is used
as-is. **Set the server before constructing the client** and set `BaseUrl` on the **Production** node
(the environment you select is the only one read). When `Twilio:BaseUrl` is unset, leave the default.

---

### §B — Capability rows

| # | Method (params in order) → Returns | Request fields (C# ← wire) | Response accessors used | Errors / pagination | Map page |
|---|---|---|---|---|---|
| **1 SEND** | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` | `accountSid` (path, required) · `to` (`To`, **required**, non-nullable) · `body` (`Body`) · **From option A:** `from` (`From`) · **From option B:** `messagingServiceSid` (`MessagingServiceSid`). All 24 nullable params between `statusCallback`…`contentSid` have **no default → must pass explicitly** (pass `null` to skip). Use named args. | `Sid` (`sid`): `string?` · `Status` (`status`): `MessageEnumStatus?` · `ErrorCode` (`error_code`): `int?` · also `ErrorMessage` (`error_message`): `string?` | Case B `SdkException<RawError>`; no pagination. **A send that the provider ACCEPTS returns 2xx with `Status`=queued/accepted/scheduled and `ErrorCode`=null** — undeliverable US numbers surface later as `Status`=undelivered/failed + an `ErrorCode`, NOT as an exception. | `operations/Api20100401Message.md` (CreateMessage); response `records-1-Ac-Ca.md` (`ApiV2010AccountMessage`) |
| **2 VALIDATE** | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `LookupResponse` | `phoneNumber` (path, required — the number to validate, raw or E.164) · pass `null` for all 15 optional params to skip (basic validation needs no `fields`). | `Valid` (`valid`): `bool?` — reject when not `true` · `PhoneNumber` (`phone_number`): `string?` — **the provider's canonical E.164 form to store** · `ValidationErrors` (`validation_errors`): `IReadOnlyList<ValidationError>?` · `NationalFormat` (`national_format`): `string?` · `CountryCode` (`country_code`): `string?` | Case B `SdkException<RawError>` (a 404 means "number not found/parseable" → treat as invalid); no pagination. | `operations/LookupsV2PhoneNumber.md`; response `records-4-Li-Me.md` (`LookupResponse`) |
| **3 SCHEDULE** | Same as **1** `CreateMessage`, with: `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset>` (a few days out), `messagingServiceSid: <sid>`, `from: null`. | `scheduleType` (`ScheduleType`) = `Fixed` · `sendAt` (`SendAt`): `DateTimeOffset?` = the future send time · `messagingServiceSid` (`MessagingServiceSid`) **required for scheduling** (see enum note) | `Sid` + `Status` (=`Scheduled` on success) — same accessors as **1** | Case B. **`scheduleType` has exactly one value `Fixed` and its doc says it is "For Messaging Services only" used "in conjunction with" `sendAt`** → scheduling REQUIRES `MessagingServiceSid` (not a bare `From`). Min/max lead-time is a provider-enforced runtime constraint, **not** in the SDK map — see Assumptions. | `operations/Api20100401Message.md` (CreateMessage); `enums.md` (`MessageEnumScheduleType`) |
| **4 CANCEL** | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` | `accountSid` (path) · `sid` (path, the scheduled message SID) · `status` (`Status`) = `MessageEnumUpdateStatus.Canceled` · `body` (`Body`) = `null` (pass explicitly to skip) | `Status` (`status`): `MessageEnumStatus?` = `Canceled` on success | Case B `SdkException<RawError>`. If it is already sent / past cancel window the provider returns **non-2xx** → caught as `SdkException<RawError>`; read `StatusCode` + provider code via `ReadAsJson`/`ReadAsString` (§E). The exact provider error code is a runtime fact, not in the map. | `operations/Api20100401Message.md` (UpdateMessage); `enums.md` (`MessageEnumUpdateStatus`) |
| **5 FETCH** | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` | `accountSid` (path) · `sid` (path) | `Status` (`status`): `MessageEnumStatus?` · `ErrorCode` (`error_code`): `int?` · `ErrorMessage` (`error_message`): `string?` · `To`/`From`/`Body`/`DateSent` as needed | Case B; no pagination. | `operations/Api20100401Message.md` (FetchMessage); `records-1-Ac-Ca.md` |
| **6 LIST** | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ListMessageResponse` | `from` (`From`) = configured FromNumber (provider-side filter) · **range lower bound** `dateSentQueryQuery` → wire **`DateSent>`** · **range upper bound** `dateSentQuery` → wire **`DateSent<`** · (`dateSent` → exact `DateSent`, leave `null` for a range) · `pageSize` (`PageSize`) · `page` (`Page`) · `pageToken` (`PageToken`). Pass the rest `null`. **Use named args — the param order pairs are easy to swap.** | `Messages` (`messages`): `IReadOnlyList<ApiV2010AccountMessage>?` · paging: `Page` (`page`): `int?` · `PageSize` (`page_size`): `int?` · `NextPageUri` (`next_page_uri`): `string?` · `FirstPageUri`, `PreviousPageUri`, `Uri`, `Start`, `End` | Case B. **Pagination: NO auto-paging enumerable and NO `perPage`** — the map marks pagination "none (only `page`, no `perPage`)". Enumerate the whole range yourself: loop incrementing `page` (and/or follow `NextPageUri`/`pageToken`) until `Messages` is empty or `NextPageUri` is null. | `operations/Api20100401Message.md` (ListMessage); `records-4-Li-Me.md` (`ListMessageResponse`) |
| **7 REDACT** | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, ...)` → `ApiV2010AccountMessage` | `sid` (path, the message to redact) · `body` (`Body`) = **`""` (empty string)** to redact the text · `status` (`Status`) = `null` | Returns the message with `Body` now empty; SID / `Status` / `DateSent` / `ErrorCode` **survive** (the sent-fact + outcome are preserved). | Case B. **Redaction keeps the record** — `UpdateMessage(body:"")` is the map's documented redact path ("used to redact Message `body` text"). `DeleteMessage` (below) is the destructive alternative that removes the WHOLE record. | `operations/Api20100401Message.md` (UpdateMessage note) |

`DeleteMessage(string accountSid, string sid, ...)` → `void` (Case B) exists but **deletes the entire
Message resource**, losing the sent-fact and outcome. Use **7 (redact via `body:""`)** for the shopper
"dispose of the text" request; reserve `DeleteMessage` for full-record removal only. *(map: `operations/Api20100401Message.md` DeleteMessage.)*

---

### §C — Enum value tables (literal C# member ← wire)  *(source: `map/models/enums.md`)*

**`MessageEnumStatus`** (delivery status on `ApiV2010AccountMessage.Status`, namespace `TwilioSdk.Models.Enums`):
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`,
`Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`,
`Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.
(Build via `MessageEnumStatus.FromValue("delivered")` or the static member `MessageEnumStatus.Delivered`
— it is a `StringEnum`, not a C# enum.)

**`MessageEnumScheduleType`**: `Fixed (fixed)` — only value. (Messaging Services only; pair with `sendAt`.)

**`MessageEnumUpdateStatus`**: `Canceled (canceled)` — only value.

**`MessageEnumDirection`** (`ApiV2010AccountMessage.Direction`): `Inbound (inbound)`, `OutboundApi (outbound-api)`,
`OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

(Other `CreateMessage` enum params — `MessageEnumContentRetention` {Retain, Discard},
`MessageEnumAddressRetention` {Retain, Obfuscate}, `MessageEnumRiskCheck` {Enable, Disable},
`MessageEnumTrafficType` {Free} — pass `null` unless a specific behaviour is needed.)

---

### §D — Response envelope shapes  *(source: `records-1-Ac-Ca.md`, `records-4-Li-Me.md`)*

`ApiV2010AccountMessage` (returned by CreateMessage / FetchMessage / UpdateMessage; element of ListMessage)
— all fields nullable: `Body (body)`, `NumSegments (num_segments)`, `Direction (direction): MessageEnumDirection?`,
`From (from)`, `To (to)`, `DateUpdated (date_updated)`, `Price (price)`, `ErrorMessage (error_message)`,
`Uri (uri)`, `AccountSid (account_sid)`, `NumMedia (num_media)`, `Status (status): MessageEnumStatus?`,
`MessagingServiceSid (messaging_service_sid)`, `Sid (sid)`, `DateSent (date_sent)`, `DateCreated (date_created)`,
`ErrorCode (error_code): int?`, `PriceUnit (price_unit)`, `ApiVersion (api_version)`, `SubresourceUris (subresource_uris): object?`.
Note `DateSent` is `string?` (not a DateTime) — parse if you need a timestamp.

`ListMessageResponse` — **the payload list is `Messages` (wire `messages`): `IReadOnlyList<ApiV2010AccountMessage>?`**
(reads go one level into `.Messages`), plus paging scalars `End`, `Start`, `Page`, `PageSize`,
`FirstPageUri`, `NextPageUri`, `PreviousPageUri`, `Uri`.

`LookupResponse` — `Valid (valid): bool?`, `PhoneNumber (phone_number): string?` (canonical E.164),
`NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode`,
`ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`, plus optional add-on blocks
(`CallerName`, `LineTypeIntelligence`, etc.) that are only populated when requested via `fields`.

---

### §E — Error-handling contract  *(source: `sdk-map.md` §Error-handling model; `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/RawError.cs`)*

Every in-scope op is **Case B**: catch **`SdkException<RawError>`** (namespace `TwilioSdk.Core.Exceptions`;
`RawError` is `TwilioSdk.Core.ErrorResponse`). There are NO typed `{Op}Error` classes and NO `TryGet…`
accessors for these ops. Read failures via:
- HTTP status: `ex.Error.StatusCode` (`System.Net.HttpStatusCode`).
- Provider error code + message: `ex.Error.ReadAsJson<T>()` (deserialize Twilio's `{code, message, more_info, status}` body) or `ex.Error.ReadAsString()` for the raw body; `ex.Error.ReadAsBytes()` also available.

**Send-failure policy (required by the brief):** a failed **send** must NOT fail the underlying order
operation — catch `SdkException<RawError>` around the send, log status + provider code, and continue.
An **undeliverable US destination is an expected OUTCOME, not an exception**: the create call returns 2xx
(`Status`=queued/accepted), and the failure appears later as `Status`=undelivered/failed +
`ErrorCode` on FetchMessage/ListMessage — reconcile via §B rows 5/6, do not treat it as a thrown error.

---

## 3. Trap notes (load the named skill before writing that step)

- ⚠ **Step 0 (client & DI)** — the `HttpClient`/handler pipeline lifetime and whether the SDK client is
  singleton/transient are not visible in the constructor signature; getting this wrong causes socket
  exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring `AddTwilioSdkClient`.
- ⚠ **Step 0 (auth)** — where/when credentials must be set relative to client construction, and loading
  secrets from config vs hardcoding, is a usage rule the property type does not show. **MUST load
  `dotnet-authentication`** before setting `AccountSidAuthToken`.
- ⚠ **Step 0 (base URL / server / retries)** — whether editing `o.Server.Default.Production.BaseUrl`
  after construction takes effect, what `RetryOptions.Timeout` actually bounds, and whether a failed
  **write** (CreateMessage POST) can be silently re-sent by the retry layer, are all governed by the
  resilience skill and are NOT inferable from the option names. This matters directly: an SMS send is a
  non-idempotent POST. **MUST load `dotnet-configuration-resilience`** before wiring the client/retries.
- ⚠ **Steps 1/3 (CreateMessage), 6 (ListMessage)** — these have many nullable-no-default params that
  mis-bind in positional calls; the correct calling convention (named args, which params are truly
  optional) is a usage rule. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ **Steps 1–7 (models)** — `MessageEnum*` are `StringEnum<T>`, NOT C# enums (build with a static member
  or `.FromValue("wire")`), and unmodeled JSON fields are dropped on deserialize. **MUST load
  `dotnet-models`** before constructing requests or mapping responses onto domain types.
- ⚠ **All steps (error boundary)** — see REQUIRED READING; the Case-B `RawError` accessors and the
  `JsonException` traps below are governed by `dotnet-error-handling`.
- ⚠ **Tests** — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`**
  before stubbing the SDK.

---

## 4. REQUIRED READING — load BEFORE implementation starts

These carry defaults, worked examples, and gotchas this sheet deliberately does not restate. Load each
before writing the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, HttpClient lifetime, `AddTwilioSdkClient` DI registration |
| `dotnet-authentication` | Step 0 — setting `AccountSidAuthToken` (basic auth), secret loading |
| `dotnet-configuration-resilience` | Step 0 — base-URL/server selection, retries/timeouts, POST re-send risk, manual pagination |
| `dotnet-calling-endpoints` | Steps 1–7 — calling ops, named args, request/response envelope shapes |
| `dotnet-models` | Steps 1–7 — building requests, `StringEnum` handling, wire-name mapping |
| `dotnet-error-handling` | ALL steps — the Case-B error boundary (always required) |
| `dotnet-testing` | Test step — faking the `HttpClient` seam |

**`System.Text.Json.JsonException` reaches the error boundary from two directions and needs opposite
handling — both belong in the FIRST cut of the boundary, not a later revision:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated error shape throws `JsonException`
  *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and
  the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a
  deterministic rejection as an outage, and a caller that retries 5xx retries something that can never
  succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- `Twilio:BaseUrl`, when set, binds to `o.Server.Default.Production.BaseUrl` (the "api"/messaging server)
  and to that ONLY. When unset, the SDK default `https://api.twilio.com` stands. The Lookup call
  (capability 2) uses a **different server** `o.Server.Default4.Production.BaseUrl` (default
  `https://lookups.twilio.com`); `Twilio:BaseUrl` does not and must not affect it. If the deployment needs
  the Lookup host redirected too (e.g. a single mock gateway), that must be a **separate** config key —
  the brief scopes `Twilio:BaseUrl` to messaging only, so this plan leaves Lookup on its own default
  unless told otherwise.
- `AccountSid` is used as basic-auth `Username` and `AuthToken` as `Password` (the app supplies these
  directly rather than an API-key SID/secret pair).
- For scheduling (capability 3) the app will use `MessagingServiceSid` (required by `scheduleType=Fixed`),
  not a bare `FromNumber`. Immediate sends (capability 1) may use either `FromNumber` or
  `MessagingServiceSid` per the brief.
- `accountSid` passed as the path argument to each `Api20100401Message` op equals the configured
  `Twilio:AccountSid`.

**Blockers / GAPS**
- **No GAP on phone-number validation** — the SDK DOES expose it: Lookup v2
  (`client.LookupsV2PhoneNumber.FetchPhoneNumber3`) returns `Valid` + canonical `PhoneNumber` (E.164). It
  lives on a **different host** (`lookups.twilio.com` via `Server.Default4`), flagged above.
- `UNVERIFIED` (live-traffic only): the scheduling min/max lead-time (Twilio platform enforces roughly
  15 minutes to ~7/35 days) is **not** encoded in the SDK map or source — it is enforced server-side and
  returns a non-2xx `SdkException<RawError>` at send time. **Defensive-coding directive:** do not hard-code
  a lead-time rule from memory; compute `sendAt` well inside a few-days window, and on a rejected schedule
  extract the provider `code`/`message` best-effort via `RawError.ReadAsJson`/`ReadAsString`, falling back
  to the generic error message, rather than asserting a specific numeric bound.
- `UNVERIFIED` (live-traffic only): the exact provider error code returned when cancelling an
  already-sent/too-late message (capability 4) is not in the map/source. **Defensive-coding directive:**
  treat any non-2xx from `UpdateMessage(status:Canceled)` as "cancel failed / already sent", reading
  status + provider code best-effort from `RawError`, with a generic fallback.
- `DateSent` and the other timestamp fields on `ApiV2010AccountMessage` are typed `string?` in the SDK
  (not `DateTimeOffset`); the reconciliation code must parse them. The **request** filters
  (`dateSentQuery`/`dateSentQueryQuery`) ARE `DateTimeOffset?` and are serialized to the `DateSent<` /
  `DateSent>` ISO-8601 query params by the SDK.
