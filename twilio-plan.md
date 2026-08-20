# Twilio SMS order notifications — contract sheet

Package: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient`. Map stamp: source commit `51fdf48`.

Config keys (values from config/env — never hard-code secrets): `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (optional). Env: `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_FROM_NUMBER`, `TWILIO_MESSAGING_SERVICE_SID`, `TWILIO_TEST_TO_NUMBER`, `TWILIO_UNREACHABLE_TO_NUMBER`.

No webhooks: every delivery outcome is obtained by fetch/list.

---

## Scope & sequence

1. **Client + DI** — construct `TwilioSdkClient` with Account SID + Auth Token; when `Twilio:BaseUrl` is set, override **only** the 2010 Messages host (`Server.Default`), never Lookup (`Server.Default4`).
2. **Lookup (Flow 1)** — `LookupsV2PhoneNumber.FetchPhoneNumber3`: reject non-usable destinations at `POST /api/contact-numbers`; persist provider canonical E.164 (`LookupResponse.PhoneNumber`).
3. **Send SMS (Flow 2 place/dispatch/cancel notices; Flow 3 resend)** — `Api20100401Message.CreateMessage` from `Twilio:FromNumber` to the stored E.164. Catch send failure without failing the order operation.
4. **Schedule follow-up (dispatch)** — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` (Messaging Service required). Persist returned `Sid` for later cancel.
5. **Cancel scheduled follow-up (order cancelled)** — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
6. **Poll delivery (no webhooks)** — `Api20100401Message.FetchMessage` by SID.
7. **Redact body at provider (Flow 3 DELETE content)** — `Api20100401Message.UpdateMessage` with empty `body` (do **not** `DeleteMessage` — that removes the resource, including delivery outcome).
8. **Reconcile (Flow 3)** — `Api20100401Message.ListMessage` with `from: Twilio:FromNumber` and `DateSent>` / `DateSent<` range; page until exhausted.
9. **Idempotent resend** — SDK does **not** accept a caller-supplied idempotency key on Create Message (see Assumptions & Blockers); enforce in the app layer.
10. **Error boundary** — Case B `SdkException<RawError>` on every in-scope op, plus `JsonException` (see trap notes).

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

### Client construction & servers (`sdk-map.md`, `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`, `ServiceCollectionExtensions.cs`)

| Piece | Type (namespace) | Fact |
|---|---|---|
| Client | `TwilioSdk.TwilioSdkClient` | `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` — only constructor |
| Options | `TwilioSdk.TwilioSdkClientOptions` | `Environment: TwilioSdk.Servers.ServerEnvironment` (member `Production`, wire `production`; `Default()` → `Production`); `Retry: TwilioSdk.Core.Configuration.RetryOptions`; `Logging: TwilioSdk.Core.Configuration.LoggingOptions`; `Server: TwilioSdk.ServerOptions`; `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` |
| Auth credentials | `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials` | `required string Username` · `required string Password`. Set `Username` = `Twilio:AccountSid`, `Password` = `Twilio:AuthToken`. |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient` | `IServiceCollection.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers a **singleton** `TwilioSdkClient`; internally `AddHttpClient()` + `IHttpClientFactory.CreateClient()` (unnamed). |
| Messaging host (send/read/list/update/delete) | `TwilioSdk.Servers.DefaultOptions` via `options.Server.Default` | Create/Fetch/List/Update/Delete Message all execute against `_server.Default(...)`. Production default BaseUrl = `https://api.twilio.com`. **When `Twilio:BaseUrl` is set, assign it verbatim to `options.Server.Default.Production.BaseUrl`.** Nested type: `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl`. |
| Lookup host | `TwilioSdk.Servers.Default4Options` via `options.Server.Default4` | Lookup V2 executes against `_server.Default4(...)`. Production default BaseUrl = `https://lookups.twilio.com`. **Do not write `Twilio:BaseUrl` onto Default4** (or Default1–3, 5–14). Lookup is a different host and is unaffected by `Twilio:BaseUrl`. |
| Other `ServerOptions` nodes | `TwilioSdk.ServerOptions` | `Default1` default BaseUrl is `https://messaging.twilio.com` (Messaging Services REST, **not** 2010 Messages). Overriding Default1 does **not** redirect send/fetch/list. Leave Default1–14 at defaults. |
| Per-call options | `TwilioSdk.Core.RequestOptions` | Only member: `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No header bag. No idempotency field. |

Do **not** set `HttpClient.BaseAddress` to `Twilio:BaseUrl` — that would hit Lookup and Messages with the same host.

⚠ Step 1 (client registration) — the SDK client constructor takes an `HttpClient` whose lifetime is not the same question as the wrapper's DI lifetime; registering the wrong one leaks handlers or multiplies clients. **MUST load `dotnet-client-initialization`** before `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — credentials live on a specific options property with `required` username/password; a 401/403 at runtime is an auth-wiring failure before it is an operation failure. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

⚠ Step 1 (base URL / retries) — `Twilio:BaseUrl` is a **server-node** override (`Server.Default.Production.BaseUrl`), not `HttpClient.BaseAddress`; the SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed **write** can be re-sent is decided by those options, not by the Message API. **MUST load `dotnet-configuration-resilience`** before wiring the client.

### 2. Lookup / validation — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (`operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md`)

Use **Lookup V2**, not V1: only V2's `LookupResponse` carries `Valid`, `ValidationErrors`, and canonical E.164 `PhoneNumber`. V1 (`LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber`) has `PhoneNumber` but **no** `Valid` / `ValidationErrors` (`operations/LookupsV1PhoneNumberApi.md`, `records-4-Li-Me.md`).

| | |
|---|---|
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · server **Default4 (lookups)** · **not** governed by `Twilio:BaseUrl` |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, no default → pass `null` to skip |
| Path | `phoneNumber` — E.164 or national; XML: default country code is +1 if national |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity_match / reassigned_number / pre_fill extras unused here) |
| `fields` values (XML on `LookupsV2PhoneNumber.cs`) | comma-separated: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill` |
| This flow | pass `fields: "validation"` (and optionally `line_type_intelligence` / `line_status`); all other optional params `null` |
| Returns | `TwilioSdk.Models.LookupResponse` — **no extra envelope**; fields are on the record itself |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` · accessors: `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |

**`LookupResponse` fields this flow reads** (`Models/LookupResponse.cs`, `records-4-Li-Me.md`):

| C# (wire) | Type | Use |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical E.164** (`+` + country code + subscriber). **Store this**, not the caller-typed value. |
| `Valid (valid)` | `bool?` | XML: whether the number is in a valid range a carrier can assign. **`false` → reject at registration.** |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reasons when invalid. Non-empty → reject. |
| `NationalFormat (national_format)` | `string?` | Display only; do not store as the destination. |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2. |
| `CallingCountryCode (calling_country_code)` | `string?` | International prefix. |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Only if requested via `fields`. `Type (type): string?` is **untyped string** (not `LineType` enum). `ErrorCode (error_code): int?` on this object is a package error, not a send error. |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Only if requested. `Status (status): string?` · `ErrorCode (error_code): int?`. |

**`ValidationError`** (`enums.md`, `Models/Enums/ValidationError.cs`) — `TwilioSdk.Models.Enums.ValidationError` (`StringEnum`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

**Reject as “not a usable SMS destination” at registration:**

- HTTP error on Lookup (**Case B** `SdkException<RawError>`) — number the provider will not resolve.
- `Valid == false` or any `ValidationErrors` entry.
- Missing `PhoneNumber` after a 2xx — cannot persist a canonical destination (treat as lookup failure).

Do **not** treat later carrier `undelivered` as a Lookup gap: US numbers can be `Valid` at registration and still land `Undelivered` after send (expected). `LineTypeIntelligence.Type` has **no documented value list** on that field — do not invent a landline reject rule from the unrelated `LineType` enum (`enums.md`: “new line type to override the original line type”).

⚠ Step 2 — `fields` and 15 must-pass-null params; a positional call mis-binds. **MUST load `dotnet-calling-endpoints`** before the first Lookup call.

⚠ Step 2 — `ValidationError` / later message statuses are `StringEnum<T>`, not C# enums; response members are nullable. **MUST load `dotnet-models`** before mapping `LookupResponse` onto domain types.

### 3. Create / send SMS — `client.Api20100401Message.CreateMessage` (`operations/Api20100401Message.md`, `records-1-Ac-Ca.md`)

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default (api)** · **`Twilio:BaseUrl` applies** |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid` (path; = `Twilio:AccountSid`), `to` (non-nullable `string`) |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — nullable, no default → pass `null` to skip |
| Body encoding | `application/x-www-form-urlencoded` (not JSON) |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **no extra envelope** |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |

**Wire names (form ← C#)** (`operations/Api20100401Message.md`): `To` ← `to`, `From` ← `from`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the unused optionals (`StatusCallback`, `ApplicationSid`, `MaxPrice`, `ProvideFeedback`, `Attempt`, `ValidityPeriod`, `ForceDelivery`, `ContentRetention`, `AddressRetention`, `SmartEncoded`, `PersistentAction`, `TrafficType`, `ShortenUrls`, `SendAsMms`, `ContentVariables`, `RiskCheck`, `FallbackFrom`, `MediaUrl`, `ContentSid`).

**From vs MessagingServiceSid**

| Call | `from` | `messagingServiceSid` | `scheduleType` / `sendAt` |
|---|---|---|---|
| Immediate SMS (placed / dispatched / cancelled notice / resend) | **`Twilio:FromNumber`** (so later `ListMessage(from:)` matches this app) | `null` unless a Messaging Service feature is required; both params are independently optional at the SDK (`string?`) | both `null` |
| Scheduled follow-up | pass `Twilio:FromNumber` as well so the eventual sender matches reconciliation | **required** — `MessageEnumScheduleType` XML: “For Messaging Services only” | `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset a few days out>` |

`to` = stored Lookup canonical E.164. `body` = notification text. `statusCallback: null` (no public URL). `accountSid` = `Twilio:AccountSid`. All other optionals `null`.

Null form params are **omitted** (flattener drops `null`). Empty string **is** sent (`Body=`).

**`ApiV2010AccountMessage` fields this integration reads** (`records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`):

| C# (wire) | Type | Use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message SID (`^(SM\|MM)[0-9a-fA-F]{32}$`). Persist for poll / cancel / redact. |
| `Status (status)` | `TwilioSdk.Models.Enums.MessageEnumStatus?` | Immediate send typically `queued`/`accepted`; scheduled → `scheduled`. |
| `From (from)` | `string?` | Sender E.164. |
| `To (to)` | `string?` | Recipient E.164. |
| `Body (body)` | `string?` | Text; empty after redact. |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT; may be unset until actually sent. |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT. |
| `ErrorCode (error_code)` | `int?` | Set when `failed` / `undelivered`; XML: do not branch programmatically on specific codes. |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code`; same caveat. |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | `^MG[0-9a-fA-F]{32}$`. |
| `NumSegments (num_segments)` | `string?` | XML: initially `0` for Messaging Service until a sender is assigned. |
| `Direction (direction)` | `MessageEnumDirection?` | Outbound API → `OutboundApi (outbound-api)`. |

**`MessageEnumStatus`** (`enums.md`): `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumScheduleType`** (`enums.md`): **only** `Fixed (fixed)`. XML on enum: include with value `fixed` “in conjuction with the `send_time` parameter” to schedule — the **C# parameter is `sendAt`**, wire name **`SendAt`** (there is no `send_time` / `sendTime` param on this signature).

**`MessageEnumUpdateStatus`** (`enums.md`): **only** `Canceled (canceled)`.

**`MessageEnumDirection`** (`enums.md`): `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

Send-time enums **not used** in this flow (pass `null`): `MessageEnumContentRetention` (`Retain`/`Discard`), `MessageEnumAddressRetention` (`Retain`/`Obfuscate`), `MessageEnumTrafficType` (`Free`), `MessageEnumRiskCheck` (`Enable`/`Disable`). Flow 3 redact is a later `UpdateMessage`, not send-time `contentRetention: Discard`.

**Idempotency on CreateMessage:** the generated method **always** attaches header `Idempotency-Key: Guid.NewGuid()` internally. There is **no** `idempotencyKey` / `uniqueName` parameter. `RequestOptions` cannot set headers. A caller-supplied key **cannot** be sent. Repeating an operator resend key will call CreateMessage again with a **new** GUID. See Assumptions & Blockers.

⚠ Step 3 — 24 must-pass-null parameters; positional calls mis-bind. **MUST load `dotnet-calling-endpoints`** before `CreateMessage`.

⚠ Step 3 — `CreateMessage` is a non-GET write whose form body is retryable at the HTTP layer; whether a timed-out send can execute more than once is not visible from the signature. **MUST load `dotnet-configuration-resilience`** before relying on retries around send.

⚠ Step 3 (order path) — a send `SdkException` must not fail the order operation; the catch types and how status/body are read are not the same as `ex.ToString()`. **MUST load `dotnet-error-handling`** before that catch.

### 4. Schedule follow-up (same `CreateMessage`)

| Param | Value |
|---|---|
| `accountSid` | `Twilio:AccountSid` |
| `to` | shopper canonical E.164 |
| `body` | “how was delivery?” copy |
| `from` | `Twilio:FromNumber` |
| `messagingServiceSid` | **`Twilio:MessagingServiceSid` (required for schedule)** |
| `scheduleType` | `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) |
| `sendAt` | `DateTimeOffset` for the follow-up instant (app chooses “a few days later”) |
| remaining optionals | `null` |

Identifier to persist for cancel: returned `ApiV2010AccountMessage.Sid`. Expected create-time status: `MessageEnumStatus.Scheduled`. After send-at elapses, poll via FetchMessage (`queued`/`sent`/`delivered`/…). After cancel: `Canceled`.

### 5. Cancel scheduled message — `client.Api20100401Message.UpdateMessage` (`operations/Api20100401Message.md`)

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · **Default (api)** · **`Twilio:BaseUrl` applies** |
| Notes (map + XML) | “Update a Message resource (used to **redact Message `body` text** and to **cancel not-yet-sent messages**)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` — nullable, no default |
| Wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |

**Cancel call:** `body: null` (omit Body), `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`), `sid` = persisted follow-up SID, `accountSid` = `Twilio:AccountSid`.

Success: returned `Status == MessageEnumStatus.Canceled`. The follow-up must not reach the shopper.

If the message **already sent**: Update with `canceled` is specified only for not-yet-sent. The failure is Case B (no typed accessor). Exact HTTP status of that rejection is **UNVERIFIED** (live wire). Defensive: catch `SdkException<RawError>`, `FetchMessage`, and if `Status` is already `Sent`/`Delivered`/`Undelivered`/`Failed`/`Canceled`, treat as “nothing left to cancel” without failing the order-cancel path.

### 6. Fetch by SID — `client.Api20100401Message.FetchMessage` (`operations/Api20100401Message.md`)

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · **Default (api)** · **`Twilio:BaseUrl` applies** |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (same fields as Create) |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |

Read: `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `From`, `To`, `Body`, `DateSent`. Map status members to queued/sent/delivered/undelivered/failed/canceled/scheduled as in the enum table. `undelivered` / `failed` **after a successful create** = carrier outcome, not a registration Lookup miss.

### 7. Redact / dispose content — `UpdateMessage` (not `DeleteMessage`)

| Goal | Operation | Params |
|---|---|---|
| Dispose **text** at the provider; keep the fact of send + delivery outcome | `UpdateMessage` | `body: ""` (empty string **is** serialized; `null` would omit Body and not redact), `status: null` |
| Remove the **entire** Message resource | `DeleteMessage(string accountSid, string sid, …)` → `void` · `DELETE …/Messages/{Sid}.json` · Case B | **Do not use for Flow 3** — delivery outcome would not survive at the provider |

After redact, persist/read: `Sid`, `Status`, `ErrorCode`, `From`, `To`, `DateSent` remain on `ApiV2010AccountMessage`; `Body` is the redacted text. Whether the live 2xx returns `Body` as `""` vs `null` is **UNVERIFIED** — treat either as “no longer retrievable”. Re-fetch to confirm the provider no longer returns the original text.

`DeleteMessage` error: Case B `SdkException<RawError>`. Unused in this flow.

### 8. List for reconciliation — `client.Api20100401Message.ListMessage` (`operations/Api20100401Message.md`, `records-4-Li-Me.md`)

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · **Default (api)** · **`Twilio:BaseUrl` applies** |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `to` … `pageToken` |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **SDK does not auto-page** (map: “none (only `page`, no `perPage`)”) |

**Filters — ask the provider, do not over-fetch:**

| C# | Wire | Type | This report |
|---|---|---|---|
| `from` | `From` | `string?` | **`Twilio:FromNumber`** — only this app’s sending number |
| `to` | `To` | `string?` | `null` (do not filter by shopper unless a future report needs it) |
| `dateSent` | `DateSent` | `DateTimeOffset?` | `null` (exact-day filter; not used for a range) |
| `dateSentQuery` | `DateSent<` | `DateTimeOffset?` | range **end** (on-or-before) |
| `dateSentQueryQuery` | `DateSent>` | `DateTimeOffset?` | range **start** (on-or-after) |
| `pageSize` | `PageSize` | `long?` | XML: default 50, **max 1000** |
| `page` | `Page` | `int?` | “client state” (XML) |
| `pageToken` | `PageToken` | `string?` | “provided by the API” (XML) |

SDK serializes those `DateTimeOffset?` values with `ToIso8601()` → UTC `yyyy-MM-ddTHH:mm:ss.fff'Z'` (`Core/Extensions/DateTimeOffsetExtensions.cs`). XML on the params also mentions GMT `YYYY-MM-DD`; the **generated client always emits the full ISO-8601 form**.

**`ListMessageResponse` envelope** (`records-4-Li-Me.md`): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `End (end): int?` · `Start (start): int?` · `Page (page): int?` · `PageSize (page_size): int?` · `FirstPageUri (first_page_uri): string?` · `NextPageUri (next_page_uri): string?` · `PreviousPageUri (previous_page_uri): string?` · `Uri (uri): string?`.

There is **no** `PageToken` property on the response record. Exhaust the range by repeating `ListMessage` while `NextPageUri` is present, passing `pageToken` / `page` on subsequent calls. How those URI query values bind onto `pageToken` is a pagination hazard — not a typed field.

⚠ Step 8 — the list operation does not return a complete range in one call; `NextPageUri` vs `pageToken`/`page` and max `pageSize` decide whether reconciliation silently drops pages. **MUST load `dotnet-configuration-resilience`** before writing the reconciliation loop.

### 9. Idempotent resend

| Mechanism | Present? |
|---|---|
| `CreateMessage` parameter `idempotencyKey` / `uniqueName` | **No** (`operations/Api20100401Message.md`) |
| `RequestOptions` header / idempotency member | **No** — only `LogLevel` (`Core/RequestOptions.cs`) |
| Header `Idempotency-Key` on the wire | Sent **internally** as `Guid.NewGuid()` on every `CreateMessage` / `UpdateMessage` / `DeleteMessage` call — **not caller-controllable** (`Api/Api20100401Message.cs`) |

Operator resend idempotency is **app-layer** (see Assumptions & Blockers).

### 10. Errors — every in-scope operation is Case B

All eight operations (Lookup V2, Create/Delete/Fetch/List/Update Message) throw:

`TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`

`SdkException<TError>` (`Core/Exceptions/SdkException.cs`): `required TError Error { get; init; }` (extends `Exception`).

`RawError` (`Core/ErrorResponse/RawError.cs`, `sdk-map.md`):

| Member | Type |
|---|---|
| `StatusCode` | `System.Net.HttpStatusCode` |
| `ReadAsBytes()` | `ReadOnlyMemory<byte>` |
| `ReadAsString()` | `string` |
| `ReadAsJson<T>()` | `T?` |

There is **no** generated `{Operation}Error` / `TryGet…` accessor on these calls (0 of 29 Case A ops). There is **no** Twilio REST-error record in the models map to `ReadAsJson` into. **UNVERIFIED** live error JSON shape — extract best-effort via `ReadAsString()` / `ReadAsJson<T>()` into an app DTO if needed; fall back to `ex.Message` + `StatusCode`. Do not parse `ex.ToString()` when `RawError` accessors exist.

**No-throw `…Result` variants: absent** (`sdk-map.md`).

**Distinguish:**

| Situation | Signal |
|---|---|
| Invalid number at **registration** | Lookup Case B **or** `Valid == false` / `ValidationErrors` |
| **Send rejected** (Twilio will not queue) | `CreateMessage` throws `SdkException<RawError>` (HTTP non-2xx). Order operation must still succeed. |
| **Carrier undeliverable** | `CreateMessage` **succeeds** with a SID; later `FetchMessage.Status` is `Undelivered` or `Failed` with `ErrorCode`/`ErrorMessage`. Expected for some US numbers. |
| Cancel after already sent | `UpdateMessage` Case B; confirm via FetchMessage |
| Missing message SID | Fetch/Update/Delete Case B (typically 404 — status from `RawError.StatusCode`) |

Other types that can reach a catch around these calls (not `SdkException`): `System.Text.Json.JsonException` (see trap notes), transport failures, cancellation. **MUST load `dotnet-error-handling`** for the full boundary — do not invent a catch list from this paragraph alone.

⚠ Every operation — Case B has no `TryGet…` payload accessors; `TryGetRawError` is on typed `ApiError`, **not** on `RawError`. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

(In-scope Message/Lookup models currently have **no** C# `required` members — 2xx `JsonException` is still possible from other deserialize failures; the non-2xx Case B path still constructs `RawError` from bytes, so the second hazard is about not treating every `JsonException` as an outage.)

⚠ Tests — the `HttpClient` constructor argument is the test seam; faking controller types or `RawError` internals will couple tests to generator output. **MUST load `dotnet-testing`** before stubbing.

---

## Trap notes (index)

⚠ Step 1 (client registration) — HttpClient vs wrapper lifetime. **MUST load `dotnet-client-initialization`**
⚠ Step 1 (auth) — credentials property / 401. **MUST load `dotnet-authentication`**
⚠ Step 1 (base URL / retries / timeouts) — server-node vs `HttpClient.BaseAddress`; retry/timeout do not bound the whole call or the registered `HttpClient`; whether a failed write can be re-sent. **MUST load `dotnet-configuration-resilience`**
⚠ Steps 2–8 (calls) — must-pass-null params and positional mis-bind. **MUST load `dotnet-calling-endpoints`**
⚠ Steps 2–8 (models) — `StringEnum<T>` vs C# enums; nullable records; form vs JSON. **MUST load `dotnet-models`**
⚠ Step 3 (send) — write retry / duplicate SMS. **MUST load `dotnet-configuration-resilience`**
⚠ Step 3 / 10 (order path) — catch types and safe status/body reads. **MUST load `dotnet-error-handling`**
⚠ Step 8 (list) — no auto-pagination; `NextPageUri` vs `pageToken`. **MUST load `dotnet-configuration-resilience`**
⚠ Error boundary — 2xx `JsonException` escapes an `SdkException`-only ladder. **MUST load `dotnet-error-handling`**
⚠ Error boundary — non-2xx `JsonException` replacing `SdkException` / destroying HTTP status. **MUST load `dotnet-error-handling`**
⚠ Tests — `HttpClient` is the seam. **MUST load `dotnet-testing`**

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `TwilioSdkClient` / `AddTwilioSdkClient` / `HttpClient` ownership |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–8 — first call to each operation; named vs positional; must-pass `null` |
| `dotnet-models` | Steps 2–8 — `LookupResponse` / `ApiV2010AccountMessage` / `StringEnum<T>` |
| `dotnet-error-handling` | Steps 3 & 10 and the integration boundary — Case B `RawError`, send-failure catch, **both** `JsonException` directions |
| `dotnet-configuration-resilience` | Step 1 server-node BaseUrl + retries/timeouts; Step 3 write retry; Step 8 list pagination |
| `dotnet-testing` | Tests that fake the SDK |

---

## Assumptions & Blockers

**Assumptions**

- Lookup **V2** (`FetchPhoneNumber3`) is the registration operation; V1 is not used because it has no `Valid` / `ValidationErrors`.
- Immediate SMS passes `from: Twilio:FromNumber` and leaves `messagingServiceSid` null; scheduled follow-up **also** passes `Twilio:MessagingServiceSid` because `MessageEnumScheduleType` is Messaging-Service-only.
- Follow-up delay (“a few days”) is an app `DateTimeOffset` for `sendAt`, not an SDK constant.
- Flow 3 content disposal uses `UpdateMessage(body: "")`, not `DeleteMessage`.
- Reconciliation uses provider `From` + `DateSent>`/`DateSent<` filters rather than client-side filtering of a wider list.
- No inbound webhooks/status callbacks (`statusCallback: null` on create).

**Blockers / limitations (not invented workarounds — these are map/source facts)**

- **Caller-supplied idempotency is not in the SDK surface for Create Message.** No param, and `RequestOptions` cannot set `Idempotency-Key`. The generated client always sends a fresh `Guid.NewGuid()`. Implement operator-resend idempotency **in the app layer** (brief: do this when the SDK/API does not expose a key).
- **`LineTypeIntelligenceInfo.Type` is an untyped `string?`** with no value list on that field. Usable-destination rejection is grounded on Lookup HTTP errors + `Valid` / `ValidationErrors` only.
- **Cancel-after-already-sent HTTP status** is Case B with no typed payload — exact status/body **UNVERIFIED**. Defensive: catch, FetchMessage, inspect `Status`.
- **`ListMessageResponse` has no `PageToken` field.** Exhausting the range depends on `NextPageUri` + the pagination skill; do not assume a single page.
- Scheduled create **requires** `Twilio:MessagingServiceSid` to be configured; if it is absent, provider-side scheduling cannot be called with this SDK contract.
