# Twilio .NET SDK — eShopOnWeb order-notification SMS

Package: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace: `TwilioSdk`. Client: `TwilioSdk.TwilioSdkClient`. Options: `TwilioSdk.TwilioSdkClientOptions`. Source stamp: `51fdf48`.

## Scope & sequence

1. **Client + auth + hosts** — construct one `TwilioSdkClient`; bind `Twilio:` config; override **only** the 2010 messaging host when `Twilio:BaseUrl` is set. Lookup stays on its own host.
2. **Lookup / validate** — `client.LookupsV1PhoneNumberApi` is **not** used (no `Valid` flag). Use `client.LookupsV2PhoneNumber.FetchPhoneNumber3`. Store `PhoneNumber` (E.164). Reject when `Valid == false` or the call throws.
3. **Send SMS immediately** — `client.Api20100401Message.CreateMessage` with `from: Twilio:FromNumber` (and `messagingServiceSid: null` unless a Messaging Service is the sender). Persist `Sid` + `Status`. Catch send failures; do not fail the business operation.
4. **Schedule follow-up** — same `CreateMessage` with `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt`, and `messagingServiceSid: Twilio:MessagingServiceSid` (schedule is Messaging-Services-only per the enum). Persist `Sid` + `Status` (`Scheduled` on success).
5. **Cancel scheduled** — `client.Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`. Persist resulting `Status` (`Canceled`).
6. **Fetch by SID (poll)** — `client.Api20100401Message.FetchMessage`. Read `Sid`, `Status`, `ErrorCode`, `ErrorMessage`. Carrier undeliverable is a **fetched status**, not a create-time exception.
7. **Redact body** — `UpdateMessage` with `body: ""` (empty string, **not** `null`) and `status: null`. Do **not** call `DeleteMessage` (that deletes the resource). Map/source document **no** extra companion field and **no** no-op condition; live 2xx can leave `Body` unchanged (see Blockers).
8. **Reconcile list** — `client.Api20100401Message.ListMessage` filtered `from: Twilio:FromNumber` plus `DateSent>` / `DateSent<`. Walk pages via `pageToken` until `NextPageUri` is absent.
9. **Idempotency on operator resend** — **SDK gap**: `CreateMessage` has no caller-supplied idempotency parameter; the SDK always sends header `Idempotency-Key` as a fresh `Guid`. Application-level idempotency (store the operator key, skip a second `CreateMessage`) is required. See Blockers.

No-throw `…Result` variants: **absent** on every operation below. All calls are throw-only.

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

### 0. Client construction, auth, hosts

| Fact | Value | Cite |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers `IHttpClientFactory` then a **singleton** `TwilioSdkClient` | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment`, `Retry: TwilioSdk.Core.Configuration.RetryOptions`, `Logging: TwilioSdk.Core.Configuration.LoggingOptions`, `Server: TwilioSdk.ServerOptions`, `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` only (`"production"`). Default: `ServerEnvironment.Default()` → Production | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Auth scheme | `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required`. Map XML: account SID + auth token (or API key as username / key secret as password) | `sdk-map.md` Servers & auth, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Messaging host (2010 Messages) | HTTP server **Default (api)**. Default base: `https://api.twilio.com`. Override: `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl verbatim>` | `operations/Api20100401Message.md`, `Servers/DefaultOptions.cs`, `ServerOptions.cs` |
| Lookup host | HTTP server **Default4 (lookups)**. Default base: `https://lookups.twilio.com`. Property: `options.Server.Default4.Production.BaseUrl`. **Do not** apply `Twilio:BaseUrl` here | `operations/LookupsV2PhoneNumber.md`, `Servers/Default4Options.cs` |
| Other DefaultN hosts | `Default1` default `https://messaging.twilio.com` (Messaging Services product API — **not** 2010 Messages). Leave Default1–Default3, Default5–Default14 untouched | `Servers/Default1Options.cs`, `ServerOptions.cs` |
| Per-request URL / header override | `TwilioSdk.Core.RequestOptions` has **only** `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No base-URL, no header bag | `Core/RequestOptions.cs` |
| Separate clients? | **No.** One `TwilioSdkClient`; messaging vs lookup is `Server.Default` vs `Server.Default4` on the same options object | `Server.cs` |
| `accountSid` path param | Pass `Twilio:AccountSid` on every `Api20100401Message.*` call | `operations/Api20100401Message.md` |

`Twilio:FromNumber` and `Twilio:MessagingServiceSid` are **operation arguments**, not client options.

### 1. Phone number lookup / validation — `FetchPhoneNumber3`

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` (nullable, no default — pass `null` to skip) |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 lookups) |
| Path | `phoneNumber` — raw shopper input; XML: E.164 or national format; default country +1 |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity-match / reassigned / pre_fill params (pass `null` for this feature) |
| Country filter | `countryCode: string?` — ISO 3166-1 alpha-2 when the number is national format |
| Type filter (request) | **No typed type-filter param on v2.** Optional extras via `fields` (comma-separated). XML possible values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. Enum `TwilioSdk.Models.Enums.Field` covers a subset of those wire names (no `validation` member) |
| Returns | `TwilioSdk.Models.LookupResponse` — **not wrapped** |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md`, `LookupsV2PhoneNumber.cs` (XML for `fields` / `phoneNumber`) |

**`LookupResponse` fields this feature reads** (`TwilioSdk.Models`, `records-4-Li-Me.md`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical E.164** (`+` + country code + subscriber). Store this, not the caller’s typing |
| `Valid (valid)` | `bool?` | **Usable-range flag.** XML: true when the number is in a range a carrier can assign. Treat `false` as reject. `null` is not `false` — see models trap |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid, when `Valid` is false |
| `CountryCode (country_code)` | `string?` | ISO country |
| `NationalFormat (national_format)` | `string?` | National display form (do not store as canonical) |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Only if `fields` includes `line_type_intelligence`. `Type (type): string?` — **no enum of mobile/landline in the map** |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Only if `fields` includes `line_status`. `Status (status): string?` — **no enum** |

**Unusable number — two signals (both required):**

1. **Response flag (2xx):** `Valid == false` and/or non-empty `ValidationErrors`. This is **not** an exception.
2. **HTTP error:** `SdkException<RawError>` (malformed/unroutable/auth). Read `StatusCode` + `ReadAsString()`.

**`ValidationError`** (`TwilioSdk.Models.Enums`, `enums.md`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

Do **not** use `LookupsV1PhoneNumberApi.FetchPhoneNumber2` / `LookupsV1PhoneNumber` — no `Valid` field (`records-4-Li-Me.md`).

Recommended call shape (named args): `fields: "validation,line_type_intelligence,line_status"` if rejecting by line type/status; otherwise `fields: "validation"` plus `countryCode` when input is not E.164. Whether `Valid` is populated when `fields` is `null` is **UNVERIFIED** (live). Pass `fields` explicitly.

### 2. Send SMS immediately — `CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Method | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` (nullable, no default — pass `null` to skip) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default api) |
| Body | `application/x-www-form-urlencoded` (source). Map lists the same names as “query params”; the **source** is form fields. Null values are omitted |
| Wire ← C# | `To` ← `to`, `From` ← `from`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the other listed optionals |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **not wrapped** |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs` |

**From vs MessagingServiceSid (immediate send):**

| Param | Wire | Required by signature | This feature |
|---|---|---|---|
| `from` | `From` | no (`string?`) | **Yes** — `Twilio:FromNumber` (sending identity) |
| `messagingServiceSid` | `MessagingServiceSid` | no (`string?`) | **Pass `null` for immediate From-number sends.** Both may be sent; the signature does not forbid combining them |
| `to` | `To` | **yes** (`string`) | Canonical E.164 from lookup |
| `body` | `Body` | no | Message text |
| `scheduleType` / `sendAt` | `ScheduleType` / `SendAt` | no | **`null` for immediate** |

**Persist from the return record** (`records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message id (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`) |
| `Status (status)` | `TwilioSdk.Models.Enums.MessageEnumStatus?` | Current delivery outcome at create time (often `queued` / `accepted`) |
| `ErrorCode (error_code)` | `int?` | Set when status is `failed` / `undelivered`; else null |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code`; XML: do not branch on these programmatically |
| `To (to)` / `From (from)` | `string?` | E.164 endpoints |
| `DateSent (date_sent)` / `DateCreated (date_created)` | `string?` | RFC 2822 GMT timestamps (**strings**, not `DateTimeOffset`) |
| `Body (body)` | `string?` | Text; empty after redaction |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | Echo of MS SID when used |

**Send failure vs carrier undeliverable:**

- HTTP-level send failure (4xx/5xx, including 401) → `SdkException<RawError>`. Catch at the integration boundary; **do not** fail the business operation.
- US carrier refusal after accept → **2xx create**, later `Status` = `Undelivered` / `Failed` with `ErrorCode`. Discover via **FetchMessage** (step 6). Not a gap.

### 3. Schedule follow-up SMS — same `CreateMessage`

| Param | Value | Cite |
|---|---|---|
| `scheduleType` | `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) — **only member** | `enums.md`, `Models/Enums/MessageEnumScheduleType.cs` |
| `sendAt` | `DateTimeOffset?` — wire `SendAt`. **Not** named `send_time` (enum XML says `send_time`; the C# identifier is `sendAt`) | `operations/Api20100401Message.md` |
| `sendAt` format | Passed as `DateTimeOffset` into form flattening (JSON-normalize then string). List filters use `ToIso8601()` (`yyyy-MM-ddTHH:mm:ss.fff'Z'` UTC). Create does **not** call `ToIso8601()`. Provide a UTC `DateTimeOffset` (offset zero) so the wire is unambiguous. Exact fractional-second / offset string vs provider parser: **UNVERIFIED** (live) | `Api20100401Message.cs`, `Core/Extensions/DateTimeOffsetExtensions.cs`, `Core/ParameterFlattener.cs` |
| Messaging Service | Enum XML: **“For Messaging Services only”**. Pass `messagingServiceSid: Twilio:MessagingServiceSid`. Immediate-send `from` may still be passed; schedule without an MS SID is not supported by this enum | `Models/Enums/MessageEnumScheduleType.cs` |
| `body` / `to` / `accountSid` | Same as immediate send | |

Success `Status` expected: `MessageEnumStatus.Scheduled` (wire `scheduled`). Confirm via the returned record / later fetch.

### 4. Cancel scheduled message — `UpdateMessage`

| | |
|---|---|
| Method | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` (nullable, no default) |
| HTTP | `POST …/Messages/{Sid}.json` (form body) |
| Wire | `Body` ← `body`, `Status` ← `status` |
| Cancel call | `body: null` (omitted), `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`) — **only update-status member** |
| Returns | `ApiV2010AccountMessage` |
| Terminal status | `MessageEnumStatus.Canceled` (wire `canceled`) |
| Already sent | Map/source do **not** name an HTTP status. Expect **Case B** `SdkException<RawError>`; read `StatusCode` + body. Do not guess 4xx vs 409 |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `operations/Api20100401Message.md` (notes: “cancel not-yet-sent messages”), `enums.md` |

### 5. Fetch message by SID — `FetchMessage`

| | |
|---|---|
| Method | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| HTTP | `GET …/Messages/{Sid}.json` |
| Returns | `ApiV2010AccountMessage` (same fields as create) |
| Error | **Case B** `SdkException<RawError>` — unknown SID: read `StatusCode` (map does not pin 404) |
| Cite | `operations/Api20100401Message.md` |

**`MessageEnumStatus`** (`TwilioSdk.Models.Enums`, `enums.md`) — compare with static members, not C# `enum`:

| Member | Wire |
|---|---|
| `Queued` | `queued` |
| `Sending` | `sending` |
| `Sent` | `sent` |
| `Failed` | `failed` |
| `Delivered` | `delivered` |
| `Undelivered` | `undelivered` |
| `Receiving` | `receiving` |
| `Received` | `received` |
| `Accepted` | `accepted` |
| `Scheduled` | `scheduled` |
| `Read` | `read` |
| `PartiallyDelivered` | `partially_delivered` |
| `Canceled` | `canceled` |

Poll until a terminal outcome the product cares about (`Delivered`, `Undelivered`, `Failed`, `Canceled`, `Sent` as needed). `ErrorCode` / `ErrorMessage` populate on `failed` / `undelivered`.

### 6. Redact body — `UpdateMessage` (not `DeleteMessage`)

| | |
|---|---|
| Call (wire confirmed `Body=`) | `await client.Api20100401Message.UpdateMessage(accountSid: accountSid, sid: providerSid, body: "", status: null, requestOptions: null, ct: ct);` |
| Flattener | `if (value is null) return [];` then `string str => [(key, str)]`. Empty string is **not** omitted. `FormUrlEncodedRequest` → `FormUrlEncodedContent` with no empty-skip |
| Q1 — empty-Body no-op conditions | **None documented.** Map notes only: “used to redact Message `body` text and to cancel not-yet-sent messages.” `body` XML `<param>` is empty. No mention of delivered/scheduled/missing header/account setting. `ContentRetention` / `AddressRetention` exist **only** on `CreateMessage`, not on `UpdateMessage`. `MessageEnumUpdateStatus` has only `Canceled` (cancel path, not redact). |
| Q2 — returned Body | SDK deserializes the 2xx JSON via `JsonResponse.Create<ApiV2010AccountMessage>()` — it does **not** copy the request `Body` onto the result. Model XML: “The text content of the message” (`body`). **No** note that the field echoes the request, and **no** note that 2xx with unchanged `Body` means redaction is queued. Live: 2xx `Body` length still 54; immediate `FetchMessage` same. Treat unchanged 2xx `Body` as **not redacted**. |
| Q3 — companion param/header | **None.** Update form fields are only `Body` ← `body` and `Status` ← `status`. Path: `AccountSid`, `Sid`. The only extra header is SDK-generated `Idempotency-Key: Guid.NewGuid()` (not caller-set; not documented as required for redact). `RequestOptions` is `LogLevel` only. |
| Q4 — other dispose-body ops | **None.** The five Message ops are Create / Delete / Fetch / List / Update. `DeleteMessage` “Deletes a Message resource from your account” (resource gone). `MessageEnumContentRetention.Discard` is Create-only (`contentRetention` on `CreateMessage`). |
| Survivors if provider did redact | `Sid`, `Status`, `ErrorCode`, dates; disposed field would be `Body` |
| Redaction HTTP failure | **Case B** `SdkException<RawError>` — 2xx with unchanged `Body` is **not** this case |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs`, `Models/ApiV2010AccountMessage.cs`, `Models/Enums/MessageEnumContentRetention.cs`, `Core/Response/JsonResponse.cs`, `Core/RequestOptions.cs`, `enums.md` |

### 7. List messages for reconciliation — `ListMessage`

| | |
|---|---|
| Method | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `to` … `pageToken` |
| HTTP | `GET …/Messages.json` |
| Query (wire ← C#) | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| SDK paginator | **None** (map: “Pagination: none (only `page`, no `perPage`)”) |
| Cite | `operations/Api20100401Message.md`, `records-4-Li-Me.md`, `Api20100401Message.cs` XML |

**This feature’s filters (named args — positional bind will swap the two date params):**

| Intent | Argument |
|---|---|
| Only this app’s sending number | `from: Twilio:FromNumber` (`to: null`) |
| Range start (after) | `dateSentQueryQuery: <from Instant>` → wire `DateSent>` |
| Range end (before) | `dateSentQuery: <to Instant>` → wire `DateSent<` |
| Exact day | `dateSent:` only if needed; otherwise leave `null` |
| Page size | `pageSize: long?` — XML default 50, **max 1000** |
| Next page | `pageToken: <token from previous page>` ; `page` is “client state” only |

XML for the three date params says GMT `YYYY-MM-DD` / `<=` / `>=`. The SDK actually sends `dateSent*.ToIso8601()` → UTC `yyyy-MM-ddTHH:mm:ss.fffZ`. Pass `DateTimeOffset` in UTC.

**`ListMessageResponse` envelope** (`records-4-Li-Me.md`):

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` — items: `Sid`, `To`, `From`, `Status`, `DateSent` (string RFC 2822 GMT), `Body` (empty if redacted) |
| `NextPageUri (next_page_uri)` | `string?` — **null/absent = last page** |
| `Page (page)` / `PageSize (page_size)` / `End` / `Start` / `Uri` / `FirstPageUri` / `PreviousPageUri` | paging metadata |

There is **no** `next_page_token` field. Continue by taking `PageToken` from `NextPageUri`’s query string and passing it as `pageToken`. No auto-iterator.

### 8. Idempotency on send

| Question | Map/source fact |
|---|---|
| `CreateMessage` parameter? | **None.** No `idempotencyKey` (that name exists on `CreateUserDefinedMessage` / Payments — **not** Messages) |
| `RequestOptions` header bag? | **No** — only `LogLevel` |
| Header the SDK sends anyway | `Idempotency-Key: Guid.NewGuid()` on **every** `CreateMessage`, `UpdateMessage`, and `DeleteMessage` invocation (`Api/Api20100401Message.cs`) |
| Caller-supplied key? | **Cannot be passed.** Each SDK call mints a new GUID, so two operator resends with the same app key still produce two different headers |
| How it behaves | Application **cannot** rely on this header for operator-resend idempotency. Store the caller key locally and skip a second `CreateMessage` |

Cite: `operations/Api20100401Message.md` (signature), `Api/Api20100401Message.cs` (header), `operations/Api20100401UserDefinedMessage.md` (contrast: `IdempotencyKey` **is** a param there).

### Error handling (all in-scope ops are Case B)

There is **no** typed `{Operation}Error` / `TryGet…` on Message or Lookup. Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`.

| Scenario | How it surfaces | What to read |
|---|---|---|
| Invalid / unusable destination (lookup 2xx) | No exception; `LookupResponse.Valid == false` + `ValidationErrors` | Response fields |
| Lookup HTTP failure / 401 | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| Send HTTP failure (incl. 401) | `SdkException<RawError>` on `CreateMessage` | same |
| Carrier undeliverable | **Not** an exception on create; later `FetchMessage` `Status` `undelivered`/`failed` + `ErrorCode`/`ErrorMessage` | Record fields |
| Cancel already-sent | `SdkException<RawError>` on `UpdateMessage` (status **UNVERIFIED** live) | `StatusCode` + body; best-effort parse, generic fallback |
| Fetch unknown SID | `SdkException<RawError>` (status **UNVERIFIED** live) | same |
| Redaction failure | `SdkException<RawError>` | same |
| Auth / 401 | `SdkException<RawError>` with `StatusCode == HttpStatusCode.Unauthorized` | `ReadAsString()` — **no** generated Twilio error record on these ops; JSON shape of the body is **UNVERIFIED**. Extract best-effort; fall back to a generic message |

`SdkException<TError>`: `public required TError Error { get; init; }` (`Core/Exceptions/SdkException.cs`). No-throw variants: absent.

---

## Trap notes

⚠ Step 0 (client registration) — the `HttpClient`/handler pipeline lifetime versus the SDK client wrapper lifetime is not visible from the constructor, and `AddTwilioSdkClient`’s registration shape is not a copy-paste recipe. **MUST load `dotnet-client-initialization`** before constructing or DI-registering the client.

⚠ Step 0 (auth) — `AccountSidAuthToken` is a `BasicAuthCredentials?` with `required` `Username`/`Password`; when those must be set relative to construction, and how rotating them interacts with a long-lived client, is not on the options type. **MUST load `dotnet-authentication`** before wiring `Twilio:AccountSid` / `Twilio:AuthToken`.

⚠ Step 0 (hosts / retries / timeouts) — `Twilio:BaseUrl`, `options.Environment`, `options.Server.Default*.Production.BaseUrl`, and `HttpClient.BaseAddress` are not interchangeable; `Retry.Timeout` / `HttpMethodsToRetry` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure on `CreateMessage` (POST) may not behave like a status-code retry. **MUST load `dotnet-configuration-resilience`** before registering the client or setting BaseUrl.

⚠ Step 1–8 (every call) — 8–24 nullable parameters have **no C# defaults** and mis-bind positionally (`dateSentQuery` vs `dateSentQueryQuery` especially). **MUST load `dotnet-calling-endpoints`** before the first `CreateMessage` / `ListMessage` / `FetchPhoneNumber3` call.

⚠ Step 1 (lookup models) — `Valid` is `bool?`; `ValidationError` / `MessageEnumStatus` / `MessageEnumScheduleType` / `MessageEnumUpdateStatus` / `Field` are `StringEnum<T>` records, not C# enums; `LineTypeIntelligenceInfo.Type` is an untyped `string?`. **MUST load `dotnet-models`** before mapping lookup/message records.

⚠ Step 2 (send) / Step 7 (list) — walking `NextPageUri` and whether a failed write can be re-issued are pagination/retry concerns the operation rows do not settle. **MUST load `dotnet-configuration-resilience`** before implementing reconciliation paging or send retries.

⚠ Error boundary (all steps) — every in-scope op is Case B (`SdkException<RawError>`); `TryGetRawError` is not a catch-all on typed errors (and these ops have no typed error). A status-only catch ladder misses deserialization failures. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Error boundary — a **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the test seam; matching the host framework and not faking SDK internals is not in the signatures. **MUST load `dotnet-testing`** before stubbing Twilio.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — `new TwilioSdkClient` / `AddTwilioSdkClient`, `HttpClient` lifetime |
| `dotnet-authentication` | Step 0 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 1–8 — named arguments, must-pass nulls, first operation call |
| `dotnet-models` | Steps 1–7 — `StringEnum<T>`, `bool?` flags, request/response records |
| `dotnet-error-handling` | All steps — Case B `SdkException<RawError>`, the two `JsonException` directions, 401/unknown-SID/cancel/redact/send failures |
| `dotnet-configuration-resilience` | Step 0 hosts/retries/timeouts; Step 2 send retry; Step 7 list pagination |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Lookups **v2** (`FetchPhoneNumber3`) is the lookup for POST `/api/contact-numbers`. v1 is unused.
- Immediate notifications use `from: Twilio:FromNumber` and do not require a Messaging Service. Scheduled follow-ups **do** require `Twilio:MessagingServiceSid` because `MessageEnumScheduleType` is documented as Messaging-Services-only.
- `Twilio:BaseUrl` maps to `options.Server.Default.Production.BaseUrl` only (2010 Messages: send / fetch / update / list). Lookup (`Default4`) is never overridden by it.
- Redaction **attempt** is `UpdateMessage` with empty `Body`, not `DeleteMessage`. Map documents no companion field and no no-op condition; success is **not** implied by HTTP 2xx alone (check returned `Body`).
- Operator-resend idempotency is enforced **in the application** (store the caller key; skip a second create).
- Reconciliation `from`/`to` query params are ISO-8601 instants mapped to `dateSentQueryQuery` (`DateSent>`) and `dateSentQuery` (`DateSent<`).
- “Usable destination” is at least `Valid == true`. Extra rejection on `LineTypeIntelligence.Type` / `LineStatus.Status` is product policy on untyped strings (no SDK enum).

**Blockers / gaps**

- **Caller-supplied idempotency on Message create is not exposed.** No parameter, no `RequestOptions` header API; the SDK always sends `Idempotency-Key: <new Guid>`. The operator-resend “same key must not send a second message” requirement cannot be met by the SDK header.
- **No generated error payload type** for these Case B operations; HTTP statuses for unknown SID, cancel-already-sent, and redact failure are not in the map. Handle via `RawError.StatusCode` + best-effort body; label live codes **UNVERIFIED**.
- **No line-type enum** for “mobile vs landline” filtering; `Type` is `string?`.
- **No list auto-paginator**; `NextPageUri` must be walked by the application.
- **`sendAt` wire string** is DateTimeOffset form-flattened, not `ToIso8601()`. Whether the live scheduler accepts that exact serialization is **UNVERIFIED** — send UTC `DateTimeOffset` and treat parser mismatch as a live-check item.
- Whether `LookupResponse.Valid` is present when `fields` is omitted is **UNVERIFIED** — pass `fields` including `validation`.
- **Empty-`Body` UpdateMessage does not clear stored text in live 2xx.** Map/source document no condition (status, header, ContentRetention, queued redact) under which `POST …/Messages/{Sid}` with `Body=` is a no-op or async. The SDK has no other dispose-body operation. Provider-side redaction despite 2xx + unchanged `Body` is **UNVERIFIED** and cannot be contracted from the map.
