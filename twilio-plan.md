# Twilio .NET SDK — eShopOnWeb messaging plan + contract sheet

Package `AsadAli.TwilioSdk` (install version-less). Root namespace **`TwilioSdk`** (not `Twilio`). Client `TwilioSdk.TwilioSdkClient`. Map: `sdk-map.md` (commit `51fdf48`). No webhooks: every `statusCallback` argument is `null`. No `…Result` variants exist — every operation throws.

## Scope & sequence

1. **Client construction** — `TwilioSdkClient` / `AddTwilioSdkClient`; `AccountSidAuthToken`; messaging-only `BaseUrl` on server `Default` (not `Default4`).
2. **Lookup / validate shopper number** — `client.LookupsV1PhoneNumberApi` is **not** the in-scope op (no `Valid` flag; `Carrier` is `object?`). Use **`client.LookupsV2PhoneNumber.FetchPhoneNumber3`**. Persist `LookupResponse.PhoneNumber` (E.164), not caller input.
3. **Send SMS** (order placed / dispatched / cancelled / resend) — `client.Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`, `body`, `to`; `statusCallback: null`. Persist `Sid` + `Status`.
4. **Schedule follow-up** — same `CreateMessage` with `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt`, and `messagingServiceSid` (enum docs: Messaging Services only).
5. **Cancel scheduled** — `UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled` (leave `body` null).
6. **Fetch delivery outcome** — `FetchMessage` by `sid`. No status callbacks.
7. **Reconcile** — `ListMessage` with **server-side** `from` = `Twilio:FromNumber` plus `DateSent>` / `DateSent<` range. Do not list the whole account then filter.
8. **Redact body** — `UpdateMessage` with `body: ""` (empty string), `status: null`. Do **not** call `DeleteMessage` (that removes the resource).
9. **Resend idempotency** — **SDK gap** (see CONTRACT SHEET + Assumptions). Implement idempotency in the application; `CreateMessage` does not accept a caller key.
10. **Error boundary** around every SDK call so a failed send never fails the order operation.

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

### Client construction / auth / servers

| Fact | Value | Cite |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` — registers the client as **singleton**, builds `HttpClient` via `IHttpClientFactory.CreateClient()` (unnamed) | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment`; `Retry: TwilioSdk.Core.Configuration.RetryOptions`; `Logging: TwilioSdk.Core.Configuration.LoggingOptions`; `Server: TwilioSdk.ServerOptions`; `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment | Only `TwilioSdk.Servers.ServerEnvironment.Production` (wire `"production"`). `Default()` → `Production`. | `Servers/ServerEnvironment.cs` |
| Auth scheme | `options.AccountSidAuthToken = new BasicAuthCredentials { Username = accountSid, Password = authToken }` — both members `required`. Sends HTTP Basic. Load SID/token from `TWILIO_ACCOUNT_SID` / `TWILIO_AUTH_TOKEN` or `Twilio:AccountSid` / `Twilio:AuthToken`. Never log `Password`. | `sdk-map.md` *Servers & auth* · `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Path `accountSid` | Same Account SID string as `Username` on every `Api20100401Message` call | operations path `{AccountSid}` |
| Messaging BaseUrl override | When `Twilio:BaseUrl` is set, assign it **verbatim** to `options.Server.Default.Production.BaseUrl` only. `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl` defaults to `"https://api.twilio.com"`. All five Message ops use `_server.Default(...)`. | `ServerOptions.cs` (root ns `TwilioSdk`) · `Servers/DefaultOptions.cs` · `Api/Api20100401Message.cs` |
| Lookup host (do **not** override from `Twilio:BaseUrl`) | `options.Server.Default4.Production.BaseUrl` defaults to `"https://lookups.twilio.com"`. `FetchPhoneNumber3` uses `_server.Default4(...)`. | `Servers/Default4Options.cs` · `Api/LookupsV2PhoneNumber.cs` |
| `TwilioSdk.ServerOptions` members in scope | `Default: TwilioSdk.Servers.DefaultOptions`; `Default4: TwilioSdk.Servers.Default4Options` (also `Default1`…`Default3`, `Default5`…`Default14` — leave untouched) | `ServerOptions.cs` |
| Per-call options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No header bag. | `Core/RequestOptions.cs` |
| App config (not SDK) | `Twilio:FromNumber`, `Twilio:MessagingServiceSid` — passed as `from` / `messagingServiceSid` on create | — |

### Operations

| Step | Controller | Method (params in order) | Request / filters | Response envelope (fields the app reads) | Error | Pagination |
|---|---|---|---|---|---|---|
| 2 Lookup | `client.LookupsV1PhoneNumberApi` **out of scope for registration** | `FetchPhoneNumber2` — listed only to reject it: no `Valid`; `Carrier` is `object?` | — | `TwilioSdk.Models.LookupsV1PhoneNumber` | Case B | none |
| 2 Lookup | `client.LookupsV2PhoneNumber` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` · 15 params `fields`…`partnerSubId` nullable **must pass explicitly** | Path `PhoneNumber` ← `phoneNumber`. Query: `Fields` ← `fields` (comma-separated; XML values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`), `CountryCode` ← `countryCode`, remaining identity/reassigned/pre_fill params **pass `null`**. HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` on **Default4 (lookups)**. | **Unwrapped** `TwilioSdk.Models.LookupResponse` (not a wrapper). Canonical E.164: `PhoneNumber (phone_number): string?`. Validity: `Valid (valid): bool?` — “valid range that can be freely assigned by a carrier”. Reasons: `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`. Also: `CallingCountryCode (calling_country_code)`, `CountryCode (country_code)`, `NationalFormat (national_format)`. SMS-useful extras (only if requested via `fields`): `LineTypeIntelligence (line_type_intelligence): LineTypeIntelligenceInfo?` — `Type (type): string?` (not the `LineType` enum), `ErrorCode (error_code): int?`; `LineStatus (line_status): LineStatusInfo?` — `Status (status): string?`. **No `sms_capable` boolean exists.** Reject when `Valid` is not `true` or `ValidationErrors` is non-empty; store `PhoneNumber`, never caller input. If `Valid` is null, treat as unusable (defensive). `LineTypeIntelligence.Type` wire vs `LineType` enum: **UNVERIFIED** — compare the string; do not deserialize as `LineType`. | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` · `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` · no-throw absent | none | `operations/LookupsV2PhoneNumber.md` · `records-4-Li-Me.md` · `Api/LookupsV2PhoneNumber.cs` |
| 3 Send / 4 Schedule / 9 Resend | `client.Api20100401Message` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` · **24** params `statusCallback`…`contentSid` nullable **must pass explicitly** (`null` to skip). `accountSid` + `to` required non-nullable. | HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)**. Form-urlencoded body (map labels these “query”; generated method uses `FormUrlEncodedRequest`): `To`←`to`, `StatusCallback`←`statusCallback` (**always `null`** — no webhooks), `From`←`from` (`Twilio:FromNumber` for immediate send), `MessagingServiceSid`←`messagingServiceSid`, `Body`←`body`, `ScheduleType`←`scheduleType`, `SendAt`←`sendAt`. All other form fields `null` for this app. Immediate send: `from` set, `scheduleType`/`sendAt`/`messagingServiceSid` null unless a Messaging Service is also configured. **Schedule:** `scheduleType: MessageEnumScheduleType.Fixed` (wire `"fixed"`), `sendAt: DateTimeOffset` (UTC instant a few days out), `messagingServiceSid` set — enum XML: *“For Messaging Services only … in conjuction with the send_time parameter”*; the C#/wire name is **`sendAt` / `SendAt`**, not `send_time`. `sendAt` min/max window: **not in map or XML** — provider rejects via Case B. Null params omitted by the flattener. | **Unwrapped** `TwilioSdk.Models.ApiV2010AccountMessage`. Persist: `Sid (sid): string?` (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`); `Status (status): MessageEnumStatus?`; `To (to)`, `From (from)`, `Body (body)`, `DateCreated (date_created): string?` (RFC 2822 GMT), `DateSent (date_sent): string?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `MessagingServiceSid (messaging_service_sid)`, `Direction (direction): MessageEnumDirection?`, `AccountSid (account_sid)`. Create **2xx with `queued`/`accepted`/`scheduled` is success**; US carrier refusal later is `failed`/`undelivered` on a subsequent fetch — **status, not a missing API**. XML: do not branch programmatically on a specific `error_code` value (values may change). | **Case B** `SdkException<RawError>` — same accessors. No-throw absent. Create **throws** on non-2xx (invalid `to`, auth, etc.). Transport: `HttpRequestException` / `TaskCanceledException` (not `SdkException`). | none | `operations/Api20100401Message.md` · `records-1-Ac-Ca.md` · `Api/Api20100401Message.cs` |
| 5 Cancel scheduled | `client.Api20100401Message` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` · `body` and `status` nullable **must pass explicitly** | HTTP `POST …/Messages/{Sid}.json` (Default api). Form: `Body`←`body`, `Status`←`status`. **Cancel:** `status: MessageEnumUpdateStatus.Canceled` (wire `"canceled"`), `body: null`. Notes: *“cancel not-yet-sent messages”*. If already sent: Case B (typical provider code **not in map** — read `RawError.StatusCode` + body; **UNVERIFIED**). Fetch first; if `Status` is not `Scheduled`, skip cancel. | Same `ApiV2010AccountMessage`. Expect `Status == Canceled` on success. | **Case B** `SdkException<RawError>` | none | `operations/Api20100401Message.md` · `enums.md` |
| 8 Redact body | same `UpdateMessage` | same signature | **Redact:** `body: ""` (empty string), `status: null`. Notes: *“redact Message body text”*. Resource + outcome remain; body is no longer retrievable from the provider after a later `FetchMessage`. **Do not** use `DeleteMessage` (`DELETE …/Messages/{Sid}.json` → `void`) — that deletes the Message resource. | Same `ApiV2010AccountMessage`; `Body` empty; `Sid`/`Status`/`ErrorCode` survive. | **Case B** `SdkException<RawError>` | none | `operations/Api20100401Message.md` |
| 6 Fetch outcome | `client.Api20100401Message` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | HTTP `GET …/Messages/{Sid}.json` (Default api). Path `Sid` ← persisted provider id. | Same `ApiV2010AccountMessage`: `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `From`, `To`, `Body`, `DateSent`, `DateCreated`, `DateUpdated`, `Direction`. | **Case B** `SdkException<RawError>` (not-found is a non-2xx here, not a typed 404 model) | none | `operations/Api20100401Message.md` |
| 7 List / reconcile | `client.Api20100401Message` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` · 8 params `to`…`pageToken` nullable **must pass explicitly** | HTTP `GET …/Messages.json` (Default api). **Server-side** query: `From`←`from` (**must be `Twilio:FromNumber`** — do not omit), `To`←`to` (null for this endpoint), `DateSent`←`dateSent` (**null** for a range), `DateSent<`←`dateSentQuery` (range **end**), `DateSent>`←`dateSentQueryQuery` (range **start**). Generated code sends dates via `dateSent?.ToIso8601()` format `yyyy-MM-ddTHH:mm:ss.fff'Z'` (UTC). XML also describes `YYYY-MM-DD` / `<=` / `>=` date-only forms; the C# type is `DateTimeOffset?` so this SDK always emits the ISO-8601 datetime. `PageSize`←`pageSize` (XML: default 50, max 1000), `Page`←`page` (“client state”), `PageToken`←`pageToken` (“provided by the API”). | Envelope `TwilioSdk.Models.ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `Page`, `PageSize`, `Start`, `End`, `FirstPageUri`, `Uri`. **No `PageToken` field on the model** — next page’s `pageToken:` comes from the `PageToken` query value on `NextPageUri` (URI query key **UNVERIFIED** if it ever differs; request wire name is `PageToken`). | **Case B** `SdkException<RawError>` | Map: *“Pagination: none (only `page`, no `perPage`)”* — **no auto-paginator**. Loop while `NextPageUri` is non-null. Filters are query params on the provider request (server-side). | `operations/Api20100401Message.md` · `records-4-Li-Me.md` · `Api/Api20100401Message.cs` · `Core/Extensions/DateTimeOffsetExtensions.cs` |

### Idempotency (capability 9) — **GAP, not an implementable SDK fact**

`CreateMessage` has **no** `idempotencyKey` parameter (unlike `Api20100401UserDefinedMessage`, which maps `IdempotencyKey` ← `idempotencyKey`). The generated method **always** sends header `Idempotency-Key: Guid.NewGuid()` and `TwilioSdk.Core.RequestOptions` cannot add/override headers (`LogLevel` only). A caller-supplied key therefore **cannot** reach Twilio; two `CreateMessage` calls always carry distinct keys. **Application-level idempotency is required** (store the operator key, short-circuit a second send). Cite: `operations/Api20100401Message.md` signature; `Api/Api20100401Message.cs` (`HeaderParam("Idempotency-Key", Guid.NewGuid())`); `Core/RequestOptions.cs`. `UpdateMessage` / `DeleteMessage` also stamp a fresh GUID; `FetchMessage` / `ListMessage` send no such header.

### Enums in scope (`TwilioSdk.Models.Enums` — `StringEnum<T>`, **not** C# enums)

Build with the static member or `FromValue("wire")`.

| Type | Members (C# → wire) | Cite |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `enums.md` · `Models/Enums/MessageEnumStatus.cs` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | `enums.md` |
| `MessageEnumScheduleType` | `Fixed (fixed)` only. XML: Messaging Services only. | `enums.md` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` only | `enums.md` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | `enums.md` |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — used by **batch** `LookupRequestWithCorId.Fields`, **not** by `FetchPhoneNumber3` (`fields` is `string?`). XML `fields` also lists `validation` / `phone_number_quality_score` / `pre_fill`, which are **absent** from this enum. Pass a comma-separated string, e.g. `"line_type_intelligence,line_status"`. | `enums.md` · `Api/LookupsV2PhoneNumber.cs` |
| `LineType` | `Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)` — **different resource** (line-type override). Do **not** type `LineTypeIntelligenceInfo.Type` as this enum (`Type` is `string?`). | `enums.md` |

Scheduled lifecycle: create → `Scheduled`; cancel → `Canceled`; after send, ordinary `queued`/`sent`/`delivered`/`undelivered`/`failed`.

### Errors (every in-scope op is Case B)

| Item | Fact | Cite |
|---|---|---|
| Thrown API type | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` — `public required TError Error { get; init; }`. `RawError` is **not** `ApiError`; **no** `TryGet*` on it. | `sdk-map.md` error-handling model · `Core/Exceptions/SdkException.cs` · `Core/ErrorResponse/RawError.cs` |
| HTTP status | `ex.Error.StatusCode` (`System.Net.HttpStatusCode`) | `RawError` |
| Body | `ReadAsString()`; or `ReadAsJson<T>()`. **No** generated `{Create,Fetch,List,Update,Delete}MessageError` / `FetchPhoneNumber3Error`. | map Case B |
| `code` / `message` / `more_info` | Message/Lookup ops do not ship a typed error record. A sibling envelope `TwilioSdk.Models.AccountsCallsRecordingsSidJson201041408Error` has `Code (code): int?`, `Message (message): string?`, `MoreInfo (more_info): string?`, `Status (status): int?`. **UNVERIFIED** that live Message/Lookup bodies match. Defensive: `ReadAsJson` into a **local** record with those four wire names; if null/missing, fall back to `ReadAsString()` + a generic message. Do **not** catch using the recordings error type. | `records-1-Ac-Ca.md` |
| Typical codes (invalid number, not found, already-sent) | **Not enumerated in the map.** Read status + body as above. Invalid lookup/create → Case B non-2xx. Unknown `sid` → Case B on fetch/update. Cancel after send → Case B. | gap |
| Connection / timeout | `HttpRequestException`, `TaskCanceledException` — **not** `SdkException`. | `dotnet-error-handling` (load it) |
| Send vs order | Catch the types above at the notification boundary so they never escape into the order command. | capability 3 |

---

## Trap notes

⚠ Step 1 (client / DI) — the `HttpClient` argument is not owned by the SDK; constructing a client per request vs reusing the factory-backed singleton changes socket and handler lifetime. **MUST load `dotnet-client-initialization`** before `AddTwilioSdkClient` / `new TwilioSdkClient`.

⚠ Step 1 (auth) — `AccountSidAuthToken` is nullable until set; a 401/403 after wiring SID+token is an auth-scheme/environment problem, not a retry problem. **MUST load `dotnet-authentication`** before assigning `BasicAuthCredentials`.

⚠ Step 1 (BaseUrl / retries / logging) — `Twilio:BaseUrl` maps onto **one** server-node `BaseUrl`, not `HttpClient.BaseAddress`; retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `CreateMessage`/`UpdateMessage` are POST and still sit on the retry pipeline; `LoggingOptions` can emit headers/bodies that carry Basic credentials. **MUST load `dotnet-configuration-resilience`** before setting `Server`, `Retry`, or `Logging`.

⚠ Steps 2–8 (calls) — `CreateMessage` and `ListMessage` have long leading nullable parameter lists with **no C# defaults**; a positional call mis-binds `dateSentQuery` vs `dateSentQueryQuery` and the 24 create optionals. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–8 (models) — statuses/schedule/validation are `StringEnum<T>` records; response dates on `ApiV2010AccountMessage` are `string?` (RFC 2822), not `DateTimeOffset`. **MUST load `dotnet-models`** before mapping onto domain types.

⚠ Steps 2–10 (error boundary) — every in-scope op is Case B (`SdkException<RawError>`); a Case A / `{Op}Error` catch will not compile or will not match. Connection failures never become `SdkException`. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ Step 3 (failed send must not fail the order) — a 2xx create with later `undelivered` is **not** an exception; swallowing only `SdkException<RawError>` still lets transport and deserialization failures fail the order. **MUST load `dotnet-error-handling`** before the send boundary.

⚠ Tests — the constructor `HttpClient` is the stub seam; do not fake controller types. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client ctor, `HttpClient` lifetime, `AddTwilioSdkClient`.
- `dotnet-authentication` — Step 1 `AccountSidAuthToken` / `BasicAuthCredentials`.
- `dotnet-calling-endpoints` — Steps 2–8 named arguments, must-pass nullables, `ct:`.
- `dotnet-models` — `StringEnum<T>`, record nullability, wire names.
- `dotnet-configuration-resilience` — Step 1 per-server `BaseUrl`, retries, timeouts, pagination loop, logging/redaction.
- `dotnet-testing` — HttpClient handler seam.
- `dotnet-error-handling` — Steps 2–10 catch ladder (always required). Also:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **Namespace:** the generated SDK root is `TwilioSdk` (`sdk-map.md`). The brief’s “root namespace Twilio” is not the generated identifier.
- **Idempotency-Key (capability 9) — GAP:** `CreateMessage` does not expose a caller idempotency parameter or header; it always sends `Idempotency-Key: Guid.NewGuid()`. Operator resend must be idempotent in **application** storage. This is a map+source fact, not a missing docs lookup.
- **No `sms_capable` flag:** usable destination = `LookupResponse.Valid == true` and empty `ValidationErrors`, optionally refined by `line_type_intelligence` / `line_status` strings. `Valid` means “in a valid assignable range”, not “will accept SMS”. Which `Type` strings are SMS-capable is **UNVERIFIED**.
- **Schedule requires a Messaging Service** per `MessageEnumScheduleType` XML. If `Twilio:MessagingServiceSid` is unset, the map does not document a From-number-only scheduled send. Immediate send uses `Twilio:FromNumber` without that SID.
- **`sendAt` window** (minimum offset / maximum delay) is not in the map or XML; out-of-range values surface as Case B on create.
- **Typical REST error codes** (invalid number, 404, cancel-after-send) are not in the map. Extract best-effort `code`/`message`/`more_info`; fall back to generic text. **UNVERIFIED** live envelope vs `AccountsCallsRecordingsSidJson201041408Error`.
- **Webhooks** are out of scope: always pass `statusCallback: null`. Poll with `FetchMessage` / `ListMessage`.
- **Live US undeliverable-after-accept** is `MessageEnumStatus.Undelivered` / `Failed` plus `ErrorCode`/`ErrorMessage` on fetch — expected, not a gap.
- **`DeleteMessage` is out of scope** for content disposal; redaction is empty `Body` on `UpdateMessage`.
- Lookups V1 (`FetchPhoneNumber2`) is not used for registration: no `Valid` / `ValidationErrors`.
