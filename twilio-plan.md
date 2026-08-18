# Twilio .NET SDK — SMS Order Notifications: Plan & Contract Sheet

Integration: SMS order notifications into the eShopOnWeb ASP.NET Core app via the Twilio .NET SDK.
Every fact below is grounded in the bundled SDK map (source commit `51fdf48`); the two facts the
map does not carry (`RequestOptions` shape for idempotency, `ServerOptions`/`DefaultOptions` shape
for the base-URL override) were resolved from the SDK source and are cited as such.

## SDK identity (add to the calling project)

| | |
|---|---|
| NuGet package | `AsadAli.TwilioSdk` — install **version-less**: `dotnet add package AsadAli.TwilioSdk` (do **not** pin a version from memory) |
| Root namespace | `TwilioSdk` |
| Client class | `TwilioSdkClient` (ctor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`) |
| Options class | `TwilioSdkClientOptions` |
| DI extension | `services.AddTwilioSdkClient(o => { ... })` (namespace `TwilioSdk`) |
| Controller for all 7 message capabilities | `client.Api20100401Message` (namespace `TwilioSdk.Api`) |
| Target framework | `netstandard2.0` (compatible with the app's runtime) |

Map pages backing this sheet: `map/operations/Api20100401Message.md`, `map/models/records-1-Ac-Ca.md`
(`ApiV2010AccountMessage`), `map/models/records-4-Li-Me.md` (`ListMessageResponse`),
`map/models/enums.md`, `sdk-map.md` (§ Getting a client, § Error-handling model, § Servers & auth).

## Namespaces (using-directives) — each type from its own map row

C# does **not** import child namespaces transitively — add a `using` per kind:

| Type(s) | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`, `AddTwilioSdkClient` | `TwilioSdk` |
| `Api20100401Message`, `LookupsV2PhoneNumber` (controllers) | `TwilioSdk.Api` |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` (records) | `TwilioSdk.Models` |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection`, `ValidationError`, … | `TwilioSdk.Models.Enums` |
| `SdkException<TError>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| `RequestOptions`, `ServerEnvironment` (env) | `TwilioSdk.Core` / `TwilioSdk.Servers` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` |

---

## Scope & sequence

0. **Phone-number validation + canonicalization (Flow 1 — register a contact number)** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`. Reject an invalid destination at registration time; store the provider's canonical (E.164) form.
1. **Client & DI setup** — register `TwilioSdkClient` in the ASP.NET Core container; wire `HttpClient` via `IHttpClientFactory`. Uses no operation.
2. **Authentication** — set `options.AccountSidAuthToken` from `Twilio:AccountSid` + `Twilio:AuthToken` config.
3. **Base-URL override** — if `Twilio:BaseUrl` is set, apply it to the `Default` (api) server node.
4. **Send SMS** — `CreateMessage`.
5. **Fetch delivery status** — `FetchMessage`.
6. **Schedule future message** — `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`.
7. **Cancel scheduled message** — `UpdateMessage` with `status = Canceled`.
8. **Redact body at provider** — `UpdateMessage` with `body = ""`.
9. **List / reconcile by From + date range** — `ListMessage` (manual pagination loop).
10. **Idempotent send** — see GAP in Assumptions & Blockers; handled at the application layer.
11. **Error boundary + tests**.

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row (see the Namespaces table above), never from where a neighbouring
> type sits. Enums, error types, auth and server-config types live in different child namespaces:
> `MessageEnum*` are under `TwilioSdk.Models.Enums`, records under `TwilioSdk.Models`, `RawError`
> under `TwilioSdk.Core.ErrorResponse`, `SdkException<>` under `TwilioSdk.Core.Exceptions`.

### Operations (controller `client.Api20100401Message` · source `Api/Api20100401Message.cs`)

**Every operation is Case B: throws `SdkException<RawError>`.** No typed error class, no `TryGet…`
accessors, no no-throw `…Result` variant. Read status/body from `RawError` (see Error handling).

Every `CreateMessage`/`ListMessage` optional param below is **nullable with no C# default → you MUST
pass it explicitly** (pass `null` to skip). Call these with **named arguments** — positional calls
mis-bind. `requestOptions` and `ct` are the only params with defaults.

#### 1 & 3 & 10. CreateMessage — send / schedule
- **Signature**:
  `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **HTTP**: `POST /2010-04-01/Accounts/{AccountSid}/Messages.json`
- **Required**: `accountSid` (path), `to` (destination, E.164). Body text via `body`.
- **Sender selection (both `Twilio:FromNumber` and `Twilio:MessagingServiceSid` are configured):**
  the request model exposes BOTH `from` (wire `From`) and `messagingServiceSid` (wire
  `MessagingServiceSid`), each nullable. The SDK does **not** enforce which is set — the app picks
  exactly one per call and passes the other as `null`:
  - Send from a **number**: `from: "+1..."`, `messagingServiceSid: null`.
  - Send via a **Messaging Service**: `messagingServiceSid: "MG...."`, `from: null`.
- **Scheduling (capability 3)**: pass `scheduleType: TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed`,
  `sendAt: <DateTimeOffset>` (wire `SendAt`, serialized ISO-8601), and `messagingServiceSid: "MG..."`
  with `from: null`. The `MessageEnumScheduleType` enum has exactly one member, `Fixed` (wire `fixed`);
  its map description states **"For Messaging Services only"** — so a scheduled send goes through the
  Messaging Service SID, not `from`. (Whether the API rejects a schedule outside its allowed
  lead-time window is not encoded in the SDK surface — see Assumptions & Blockers.)
- **Returns**: `TwilioSdk.Models.ApiV2010AccountMessage` (fields below). Read `Sid` (the message SID)
  and `Status` (current `MessageEnumStatus`) off the returned object.
- **Idempotency**: the operation exposes **no** idempotency parameter or header — see GAP in
  Assumptions & Blockers.
- Query wire names (`wire ← C#`): `To ← to`, `From ← from`, `MessagingServiceSid ← messagingServiceSid`,
  `Body ← body`, `ScheduleType ← scheduleType`, `SendAt ← sendAt`, `MediaUrl ← mediaUrl`,
  `ContentSid ← contentSid`, `StatusCallback ← statusCallback`, `MaxPrice ← maxPrice`,
  `ValidityPeriod ← validityPeriod`, `ContentRetention ← contentRetention`,
  `AddressRetention ← addressRetention`, `RiskCheck ← riskCheck`, `TrafficType ← trafficType`,
  `ShortenUrls ← shortenUrls`, `SmartEncoded ← smartEncoded`, `SendAsMms ← sendAsMms`,
  `ContentVariables ← contentVariables`, `PersistentAction ← persistentAction`,
  `Attempt ← attempt`, `ProvideFeedback ← provideFeedback`, `ForceDelivery ← forceDelivery`,
  `ApplicationSid ← applicationSid`, `FallbackFrom ← fallbackFrom`.

#### 2. FetchMessage — read current delivery status by SID
- **Signature**: `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **HTTP**: `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`
- **Returns**: `ApiV2010AccountMessage` — read `Status` (`MessageEnumStatus`), and on a delivery
  failure `ErrorCode` (`error_code`, `int?`) + `ErrorMessage` (`error_message`, `string?`).

#### 4 & 6. UpdateMessage — cancel a scheduled message / redact body
- **Signature**: `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **HTTP**: `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`
- `body` and `status` are nullable-no-default → **pass both explicitly**.
- **Cancel a not-yet-sent message (capability 4)**: `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled`,
  `body: null`. `MessageEnumUpdateStatus` has exactly one member, `Canceled` (wire `canceled`).
  Cancellation only succeeds while the message is still `scheduled`/`accepted` (a non-cancellable
  state returns an error status — handle via the error boundary); the SDK itself imposes no
  client-side guard.
- **Redact body at the provider (capability 6)**: `body: ""` (empty string), `status: null`. This is
  the documented redaction path — the op's own map note says UpdateMessage is "used to redact Message
  `body` text". It is an **update, not a delete**: the message record and its final `Status` survive;
  only the `body` text becomes non-retrievable. (`DeleteMessage` also exists — `DELETE …/Messages/{Sid}.json`,
  returns `void` — but that removes the whole record and is **not** what disposal-with-audit needs;
  do not use it for redaction.)
- **Returns**: `ApiV2010AccountMessage`.
- Wire names: `Body ← body`, `Status ← status`.

#### 7. ListMessage — reconcile by From + date range (server-side filter + pagination)
- **Signature**: `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **HTTP**: `GET /2010-04-01/Accounts/{AccountSid}/Messages.json`
- **Server-side filters (wire ← C#)** — set these so filtering happens at Twilio, never client-side:
  - `From ← from` — pass the app's configured `Twilio:FromNumber`.
  - `DateSent> ← dateSentQueryQuery` — **lower bound** (messages sent **on/after** this instant).
  - `DateSent< ← dateSentQuery` — **upper bound** (messages sent **on/before** this instant).
  - `DateSent ← dateSent` — exact-day match; leave `null` when using a range.
  - Date-times are `DateTimeOffset?`; the SDK serializes them to the wire. To cover `[from, to]`,
    set `dateSentQueryQuery: from`, `dateSentQuery: to`, `dateSent: null`.
  - **Watch the parameter order**: `dateSent` (exact) comes first, then `dateSentQuery` (the `<`
    upper bound), then `dateSentQueryQuery` (the `>` lower bound). Use named arguments so the
    lower/upper bounds don't swap.
- **Returns**: `TwilioSdk.Models.ListMessageResponse` (map `records-4-Li-Me.md`):
  `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, plus `Page (page): int?`,
  `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`,
  `PreviousPageUri (previous_page_uri): string?`, `FirstPageUri (first_page_uri): string?`,
  `Start (start): int?`, `End (end): int?`, `Uri (uri): string?`.
- **Pagination is MANUAL — there is no auto-pagination and no `perPage`.** The map row states
  pagination = "none (only `page`, no `perPage`)". Cover the whole range by looping: request with
  `pageSize` set and `page`/`pageToken` = null, then continue paging (advance `page`, or follow the
  page token derived from `NextPageUri`) until `NextPageUri` is `null`. Accumulate `Messages` across
  pages. (The exact re-paging mechanism — incrementing `page` vs. extracting the token from
  `NextPageUri` for `pageToken` — is a resilience/pagination concern: **MUST load
  `dotnet-configuration-resilience`** before writing the loop.)
- **Fields read per listed message** (`ApiV2010AccountMessage`): `Sid`, `From`, `To`, `Status`
  (`MessageEnumStatus`), `DateSent` (**`string?`**, not a `DateTimeOffset`), `Body`.

### 0. Phone-number Lookup — validate + canonicalize (Flow 1) · controller `client.LookupsV2PhoneNumber` · source `Api/LookupsV2PhoneNumber.cs`

The SDK **does** expose phone-number Lookup/validation (no GAP). Use the **v2** operation.

- **Signature**:
  `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **HTTP**: `GET /v2/PhoneNumbers/{PhoneNumber}` — server node **`Default4 (lookups)`** (see base-URL scope note below).
- **Required**: `phoneNumber` (path segment `{PhoneNumber}` — the number to validate, as typed by the shopper).
  All 15 remaining params (`fields` … `partnerSubId`) are **nullable-no-default → pass explicitly** (pass
  `null` to skip). For basic validity + canonical form you do **not** need any of them — pass `fields: null`
  and the rest `null`. `fields` (wire `Fields`) selects optional data packages (line-type, caller-name,
  etc.); leave `null` for plain validation. `countryCode` (wire `CountryCode`) is an optional ISO country
  hint for interpreting a nationally-formatted input — pass a hint if the shopper may type a non-E.164
  local number; otherwise `null`.
- **Returns**: `TwilioSdk.Models.LookupResponse` (fields below).
- **Validity (requirement a)**: read `Valid (valid): bool?` — `true` ⇒ the provider considers it a usable
  destination; reject registration when it is not `true`. When invalid, `ValidationErrors (validation_errors):
  IReadOnlyList<ValidationError>?` carries the reasons (enum values below) for messaging back to the shopper.
- **Canonical form (requirement b)**: store `PhoneNumber (phone_number): string?` — the provider's own
  normalized (E.164) form of the number — **not** the caller's raw input. (`NationalFormat (national_format):
  string?` is the national format; do not store that as the canonical key.)
- **Error**: `SdkException<RawError>` — **Case B** (same pattern as every message op; confirmed). Read
  `ex.Error.StatusCode` and `ex.Error.ReadAsString()`/`ReadAsJson<T>()`. Note a subtlety for the boundary:
  a genuinely invalid-but-well-formed number typically returns **200 with `Valid == false`** (a normal
  result to branch on), whereas a malformed path/other failure surfaces as the `SdkException` — handle
  both (branch on `Valid`, catch the exception).
- **No-throw variant**: absent. **Pagination**: none.

**`LookupResponse` fields the integration reads** (map `records-4-Li-Me.md`, `Models/LookupResponse.cs`; all nullable, `init`-only):

| C# property (wire) | Type | Use |
|---|---|---|
| `Valid (valid)` | `bool?` | validity gate — reject unless `true` |
| `PhoneNumber (phone_number)` | `string?` | **canonical (E.164) number to store** |
| `NationalFormat (national_format)` | `string?` | national format (not the canonical key) |
| `CountryCode (country_code)` | `string?` | ISO country of the number |
| `CallingCountryCode (calling_country_code)` | `string?` | dialing country code |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<ValidationError>?` | reasons when `Valid == false` |
| `Url (url)` | `string?` | resource URL |

(The remaining `LookupResponse` members — `CallerName`, `SimSwap`, `CallForwarding`, `LineTypeIntelligence`,
`LineStatus`, `IdentityMatch`, `ReassignedNumber`, `SmsPumpingRisk`, `PhoneNumberQualityScore`, `PreFill` —
are optional data packages only populated when requested via `fields`; not needed for validate+canonicalize.)

**`ValidationError`** enum (`TwilioSdk.Models.Enums`, `Models/Enums/ValidationError.cs`; `StringEnum<T>`):
`TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`,
`InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

### Response record — `ApiV2010AccountMessage` (map `records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`)

All fields are nullable (`init`-only). Read-side envelope — the fields the integration reads:

| C# property (wire) | Type |
|---|---|
| `Sid (sid)` | `string?` — the message SID |
| `Status (status)` | `MessageEnumStatus?` — current delivery status |
| `From (from)` | `string?` |
| `To (to)` | `string?` |
| `Body (body)` | `string?` |
| `DateSent (date_sent)` | `string?` (string, not DateTimeOffset) |
| `DateCreated (date_created)` | `string?` |
| `DateUpdated (date_updated)` | `string?` |
| `MessagingServiceSid (messaging_service_sid)` | `string?` |
| `Direction (direction)` | `MessageEnumDirection?` |
| `ErrorCode (error_code)` | `int?` — provider delivery-failure code on a failed/undelivered message |
| `ErrorMessage (error_message)` | `string?` — provider delivery-failure message |
| `Price (price)` / `PriceUnit (price_unit)` | `string?` |
| `NumSegments (num_segments)` / `NumMedia (num_media)` | `string?` |
| `AccountSid (account_sid)` / `ApiVersion (api_version)` / `Uri (uri)` | `string?` |
| `SubresourceUris (subresource_uris)` | `object?` |

> Note: `ErrorCode`/`ErrorMessage` here are the **delivery-outcome** fields on the message record
> (why a `failed`/`undelivered` message failed). They are distinct from the HTTP-level provider error
> read off `RawError` when a *request* is rejected (see Error handling).

### Enum value tables (namespace `TwilioSdk.Models.Enums`; these are `StringEnum<T>`, **not** C# enums)

Build with a static member (`MessageEnumScheduleType.Fixed`) or `Type.FromValue("wire")`. Member name
is PascalCase; wire value in parentheses.

**`MessageEnumStatus`** (`Models/Enums/MessageEnumStatus.cs`) — send/fetch/list delivery status:
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`,
`Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`,
`Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumScheduleType`** (`Models/Enums/MessageEnumScheduleType.cs`): `Fixed (fixed)` — only member.

**`MessageEnumUpdateStatus`** (`Models/Enums/MessageEnumUpdateStatus.cs`): `Canceled (canceled)` — only member (the cancel value).

**`MessageEnumDirection`** (`Models/Enums/MessageEnumDirection.cs`): `Inbound (inbound)`,
`OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

**`MessageEnumContentRetention`** (`Models/Enums/MessageEnumContentRetention.cs`): `Retain (retain)`, `Discard (discard)`.

**`MessageEnumAddressRetention`** (`Models/Enums/MessageEnumAddressRetention.cs`): `Retain (retain)`, `Obfuscate (obfuscate)`.

**`MessageEnumRiskCheck`** (`Models/Enums/MessageEnumRiskCheck.cs`): `Enable (enable)`, `Disable (disable)`.

**`MessageEnumTrafficType`** (`Models/Enums/MessageEnumTrafficType.cs`): `Free (free)` — only member.

### Client construction, auth, base-URL, error handling (facts)

**Construction / DI** (`sdk-map.md` § Getting a client; `ServiceCollectionExtensions.cs`):
- Direct: `new TwilioSdkClient(httpClient, options)` where `options` is `TwilioSdkClientOptions`.
- ASP.NET Core: `services.AddTwilioSdkClient(o => { /* set credentials, server, retry on o */ });`
  — this registers `AddHttpClient()`, resolves an `HttpClient` from `IHttpClientFactory`, and
  registers `TwilioSdkClient` as a **singleton**.

**`TwilioSdkClientOptions` properties** (`TwilioSdkClientOptions.cs`):
`Environment: ServerEnvironment` (default `ServerEnvironment.Production` — only member) ·
`Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` ·
`AccountSidAuthToken: BasicAuthCredentials?`.

**Auth** (`sdk-map.md` § Servers & auth; source `Core/Authentication/Basic/BasicAuthCredentials.cs`):
Basic auth. Set `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <sid>, Password = <secret> }`.
`BasicAuthCredentials` has two **required** `init` props: `Username`, `Password`. For this app map
`Twilio:AccountSid` → `Username`, `Twilio:AuthToken` → `Password`. (Twilio also accepts an API-key
SID/secret in the same two slots; auth-token is fine for the app but see `dotnet-authentication`.)

**Base-URL override for the messaging API** (source: `ServerOptions.cs`, `Servers/DefaultOptions.cs`,
and `Api/Api20100401Message.cs`): all five message operations resolve their URL through the SDK's
**`Default` server node** (each calls `_server.Default("/2010-04-01/…")`). To honour `Twilio:BaseUrl`,
set:
```
options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>;   // default is "https://api.twilio.com"
```
Scope: this overrides the base address for **every operation on the `Default` (api) node** — which is
the whole `Api20100401*` family sharing `api.twilio.com`, and that covers all six messaging calls in
scope (send, read, schedule, cancel, redact, list). It does **NOT** affect other Twilio hosts
(Messaging v1/v2, Conversations, Verify, **Lookup**, etc.), which resolve through separate nodes
(`options.Server.Default1` … `Default14`). So the override is scoped to the api-host node, not global
across all Twilio hosts, and not a literal per-operation setting — apply it verbatim only when
`Twilio:BaseUrl` is present; otherwise leave the default.

**Confirmed — the messaging-only override leaves Lookup untouched.** The Lookup operation
`LookupsV2PhoneNumber.FetchPhoneNumber3` resolves through the **`Default4` (lookups)** server node
(`GET /v2/PhoneNumbers/{PhoneNumber}` is tagged `Default4 (lookups)` in the map), which is a **different**
node from the `Default` (api) node the five message operations use. Setting
`options.Server.Default.Production.BaseUrl` therefore does **not** change the Lookup host — Lookup's base
address lives under `options.Server.Default4`, which the app leaves at its default. The `Twilio:BaseUrl`
override is genuinely scoped to the messaging API and away from Lookup, as required.

**Error handling** — every operation is **Case B**, throwing
`TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Read via:
- HTTP status: `ex.Error.StatusCode` (`System.Net.HttpStatusCode`).
- Provider error code/message: no typed accessor exists (Case B). Read the body with
  `ex.Error.ReadAsJson<T>()` into a small DTO, or `ex.Error.ReadAsString()`. Extract Twilio's `code`
  and `message` **best-effort, falling back to the raw string** if the body does not match — the exact
  wire shape of the error body is not in the SDK surface (see Assumptions & Blockers, `UNVERIFIED`).
- `RawError` members (`Core/ErrorResponse/RawError.cs`): `StatusCode: HttpStatusCode`,
  `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.

**`RequestOptions`** (`Core/RequestOptions.cs`): the per-call `requestOptions` argument is a record
with a **single** property, `LogLevel? LogLevel`. It carries **no** header collection and **no**
idempotency field — confirmed from source.

---

## Trap notes (load the named skill before coding that step — do not implement from the one-liner)

- ⚠ Step 1 (client & DI) — whether the `HttpClient`/handler pipeline may be rebuilt per request or
  must be long-lived and shared, and what lifetime the SDK client itself takes, is not visible in the
  ctor signature. **MUST load `dotnet-client-initialization`** before wiring the client into DI.
- ⚠ Step 2 (auth) — where and when credentials must be set relative to client construction, and how to
  source them from configuration rather than hardcoding, are not shown by the property type. **MUST
  load `dotnet-authentication`** before setting `AccountSidAuthToken`.
- ⚠ Steps 4–7 (all message calls) — which optional args mis-bind in a positional call and how the
  request body is actually assembled are not shown by the signature. **MUST load
  `dotnet-calling-endpoints`** before the first `client.Api20100401Message.*` call.
- ⚠ Steps 4/6/9 (enums, `body:""` redaction, response mapping) — `MessageEnum*` are `StringEnum<T>`,
  not C# enums; how they are constructed/compared, and how unmodeled JSON fields behave on
  deserialize, are not shown by the type name. **MUST load `dotnet-models`** before constructing
  request payloads or mapping the response.
- ⚠ Step 3 (client config / base URL / retries / pagination) — what `Retry.Timeout` actually bounds,
  which calls retry (and whether a non-idempotent `POST` send can execute more than once on a
  transport failure), and how to page `ListMessage` to completion, are not derivable from the option
  names. **MUST load `dotnet-configuration-resilience`** before tuning the client or writing the
  pagination loop.
- ⚠ Step 11 (error boundary) — which exception types actually reach the catch, and how a `RawError`
  body must be read safely, are not shown by the throw. **MUST load `dotnet-error-handling`** before
  writing the try/catch. (See the two mandatory `JsonException` hazards in REQUIRED READING.)
- ⚠ Step 11 (tests) — the correct fake seam (the `HttpClient`) is not obvious from the client API.
  **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING — load BEFORE implementation starts

This sheet deliberately does **not** carry these skills' contents; load each before its step.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 2 — setting `AccountSidAuthToken`, credential sourcing, 401/403 |
| `dotnet-calling-endpoints` | Steps 0 & 4–9 — named-argument calls, must-pass-null params (Lookup has 15), request body |
| `dotnet-models` | Steps 0 & 3–9 — `StringEnum<T>` enums (`ValidationError`), building/mapping records, `body:""` redaction |
| `dotnet-configuration-resilience` | Step 3 & 9 — retries/timeout, base-URL/server node, manual pagination loop |
| `dotnet-error-handling` | Step 11 — the `SdkException<RawError>` boundary and safe `RawError` reads |
| `dotnet-testing` | Step 11 — faking the `HttpClient` seam |

**Mandatory `System.Text.Json.JsonException` hazards for the error boundary** — it reaches the boundary
from two directions needing opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape
  the integration boundary;
- a **non-2xx** body that does not match its operation's generated error shape throws `JsonException`
  *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException`
  and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then
  reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that
  can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

1. **GAP — Idempotency is NOT offered by the SDK (capability 5). Handle it at the application layer.**
   `CreateMessage` has no idempotency-key parameter, and the per-call `RequestOptions` record
   (`Core/RequestOptions.cs`) exposes only `LogLevel?` — no header collection, no idempotency field
   (confirmed from source). `TwilioSdkClientOptions` likewise exposes no default-header hook. There is
   therefore **no SDK-level mechanism** (no `Idempotency-Key` header or parameter) to pass an operator's
   idempotency key through the send. The app must de-duplicate re-sends itself (e.g. persist the
   idempotency key → resulting message SID and short-circuit a repeat under the same key before calling
   `CreateMessage`).

2. **UNVERIFIED — scheduling lead-time window.** The SDK does not encode any min/max lead time for
   `sendAt`; all scheduling params are nullable and unvalidated client-side. Whether a given `sendAt`
   is inside the provider's accepted scheduling window can only be confirmed by live traffic — treat a
   rejected schedule as a normal error-boundary outcome (read the `RawError` status/body), do not
   assume the SDK will pre-validate. Scheduling **does** require the Messaging Service SID path
   (`messagingServiceSid` set, `from` null) per the `MessageEnumScheduleType` "For Messaging Services
   only" note.

3. **UNVERIFIED — Twilio HTTP error body shape.** Reading the provider error `code`/`message` from
   `RawError` is Case B (no typed accessor). The exact wire shape of Twilio's error JSON is not in the
   SDK surface, so implement a **defensive extract**: `ReadAsJson<T>()` into a small DTO with the
   expected `code`/`message` fields, and **fall back to `ReadAsString()`** (and the HTTP `StatusCode`)
   whenever the body does not deserialize. Never let a malformed error body throw past the boundary
   (see the `JsonException` hazards above).

4. **Assumption — sender configuration.** Both `Twilio:FromNumber` and `Twilio:MessagingServiceSid`
   are configured; the app chooses one per call (number for immediate ad-hoc sends, Messaging Service
   SID for scheduled sends). The SDK does not enforce mutual exclusivity — the caller must pass exactly
   one and `null` for the other.

5. **Assumption — base-URL override scope is acceptable.** `Twilio:BaseUrl` maps to
   `options.Server.Default.Production.BaseUrl`, which covers all `Api20100401*` (api.twilio.com)
   operations — this includes every messaging call in scope and excludes other Twilio hosts. If the
   app ever calls a non-`Api20100401` host, that override will not apply to it (a separate `Default1…14`
   node governs it). Lookup lives on `Default4`, so the messaging override is confirmed scoped away from it.

6. **Note — Lookup config & metering (not a blocker).** `FetchPhoneNumber3` needs **no extra
   configuration** beyond the same `AccountSidAuthToken` (Account SID + Auth Token) basic-auth used for
   messaging — it is the same `TwilioSdkClient`, just a different controller/host node. Whether a Lookup
   call is metered/billed is a provider-account concern not encoded in the SDK surface (`UNVERIFIED` — only
   the account's live pricing confirms it); a plain validity/canonical lookup (`fields: null`) is Twilio's
   cheapest Lookup tier, while requesting optional `fields` packages can add per-package charges. Treat
   Lookup as a **paid, rate-limited network call**: perform it once at registration, cache/store the
   canonical E.164 result, and do not re-run it on every send.
