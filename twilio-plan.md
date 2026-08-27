# eShopOnWeb SMS order-notification — Twilio .NET SDK plan

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient`. Live account. No webhooks.

## Scope & sequence

1. **Client + DI** — construct `TwilioSdkClient` from `Twilio:AccountSid`, `Twilio:AuthToken`, optional `Twilio:BaseUrl` (messaging host only). Config also carries `Twilio:FromNumber` and `Twilio:MessagingServiceSid` for later steps.
2. **Register contact** (`POST /api/contact-numbers`) — `LookupsV2PhoneNumber.FetchPhoneNumber3`. Reject non-usable destinations. Persist `LookupResponse.PhoneNumber` (E.164). Lookups host is **not** `Twilio:BaseUrl`.
3. **Send SMS now** (order placed / dispatched / cancelled / operator resend) — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`, `messagingServiceSid: null`, `statusCallback: null`.
4. **Schedule follow-up on dispatch** — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` = `Twilio:MessagingServiceSid` (`MessageEnumScheduleType` is Messaging Services only). Persist returned `Sid` + `Status`.
5. **Cancel scheduled follow-up on order cancel** — `FetchMessage` then `UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled` iff still `Scheduled`. Same `Sid` as create.
6. **Status refresh** — `FetchMessage` by persisted `Sid`. No inbound callbacks.
7. **Operator resend (idempotent)** — app-level key → `Sid` store. SDK `CreateMessage` does **not** accept a caller idempotency key (see CONTRACT SHEET §7).
8. **Redact body** (`DELETE /api/notifications/{id}/content`) — `UpdateMessage` with empty `body`. Do **not** call `DeleteMessage` (that removes the resource, including outcome).
9. **Reconciliation** (`GET /api/notifications/reconciliation?from=&to=`) — `ListMessage` with `from` = `Twilio:FromNumber` and `DateSent>` / `DateSent<` range; page until `NextPageUri` is null. Messaging host: `Twilio:BaseUrl` applies.
10. **Error boundary** — every in-scope op is Case B `SdkException<RawError>` plus `JsonException` (see REQUIRED READING).

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

### Client construction / auth / server nodes

| Fact | Value | Cite |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` | `sdk-map.md` · `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). `Default()` → Production. | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Auth | `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required`. XML on the property: API-key SID/secret **or** Account SID + Auth Token (SID+token limited to local testing per that doc). | `sdk-map.md` *Servers & auth* · `BasicAuthCredentials.cs` |
| Messaging host (Default / api) | Message create/fetch/list/update/delete use `_server.Default(...)`. Default production base: `https://api.twilio.com`. Override **only** this node from `Twilio:BaseUrl` when that key is set, verbatim: `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>`. Nested types: `TwilioSdk.Servers.DefaultOptions` / `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl`. | `operations/Api20100401Message.md` (HTTP “Default (api)”) · `Servers/DefaultOptions.cs` · `ServerOptions.cs` |
| Lookups host (Default4) | `FetchPhoneNumber3` uses `_server.Default4(...)`. Default production base: `https://lookups.twilio.com`. **Do not** assign `Twilio:BaseUrl` to `options.Server.Default4.Production.BaseUrl` (`TwilioSdk.Servers.Default4Options`). | `operations/LookupsV2PhoneNumber.md` (HTTP “Default4 (lookups)”) · `Servers/Default4Options.cs` |
| Other `ServerOptions` nodes | `Default1`…`Default3`, `Default5`…`Default14` exist. Leave them at SDK defaults. They are not messaging and not lookups. | `ServerOptions.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` — sole public member `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No headers dictionary. | `Core/RequestOptions.cs` |
| `accountSid` path param | Same config value as `Twilio:AccountSid` on every `Api20100401Message.*` call. | `operations/Api20100401Message.md` |

### Operations

| # | Controller property | Method signature (params in order) | Request / query (C# → wire) | Response envelope | Error | Pagination |
|---|---|---|---|---|---|---|
| 2 Lookup | `client.LookupsV1PhoneNumberApi` | **Do not use for this integration.** `FetchPhoneNumber2` returns `TwilioSdk.Models.LookupsV1PhoneNumber` with no `Valid` flag. Prefer v2 below. | — | — | Case B | none |
| 2 Lookup (use this) | `client.LookupsV2PhoneNumber` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` · 15 nullable params (`fields`…`partnerSubId`) have **no default → pass `null` to skip** | Path `PhoneNumber` ← `phoneNumber`. Query: `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity/reassigned/pre_fill params (pass `null`). XML: `phoneNumber` is E.164 or national; default country +1; `fields` is a comma-separated list: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. Pass `fields: "validation,line_type_intelligence,line_status"` (this is `string?`, **not** `TwilioSdk.Models.Enums.Field`). | **No wrapper.** `TwilioSdk.Models.LookupResponse` (records-4-Li-Me.md). Canonical form: `PhoneNumber (phone_number): string?` (E.164). Usable-range flag: `Valid (valid): bool?`. Invalid reasons: `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`. Also: `CallingCountryCode (calling_country_code)`, `CountryCode (country_code)`, `NationalFormat (national_format)`, `LineTypeIntelligence (line_type_intelligence): LineTypeIntelligenceInfo?` (`Type (type): string?` — **not** an enum; `ErrorCode (error_code): int?`), `LineStatus (line_status): LineStatusInfo?` (`Status (status): string?`, `ErrorCode (error_code): int?`). | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Accessors: `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()`. No `…Result` variant. | none |
| 3 Send now / 4 Schedule / 7 Resend | `client.Api20100401Message` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` · **24** params (`statusCallback`…`contentSid`) nullable with **no default → pass `null` to skip**. HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api). Form body, not JSON. | Wire ← C#: `To` ← `to` (**required**), `StatusCallback` ← `statusCallback` (**pass `null`** — app has no public URL), `From` ← `from`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`. Other form fields: pass `null`. **Immediate send:** `from` = `Twilio:FromNumber`, `messagingServiceSid: null`, `scheduleType: null`, `sendAt: null`. **Scheduled follow-up:** `messagingServiceSid` = `Twilio:MessagingServiceSid` (enum XML: schedule type is **Messaging Services only**), `scheduleType: MessageEnumScheduleType.Fixed` (wire `fixed`), `sendAt: DateTimeOffset?` (UTC), `from: null`. Do not pass both `from` and `messagingServiceSid` on the same call. | **No wrapper.** `TwilioSdk.Models.ApiV2010AccountMessage` (records-1-Ac-Ca.md). Persist: `Sid (sid): string?` (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`) — **same identifier** for scheduled and sent; `Status (status): MessageEnumStatus?`; `To (to)`, `From (from)`, `Body (body)`, `DateSent (date_sent): string?` (RFC 2822 GMT **string**, not `DateTimeOffset`), `DateCreated (date_created): string?`, `DateUpdated (date_updated): string?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `MessagingServiceSid (messaging_service_sid): string?`, `AccountSid (account_sid): string?`, `NumSegments (num_segments): string?`, `Direction (direction): MessageEnumDirection?`. Scheduled create typically returns `Status = Scheduled`. | **Case B** `SdkException<RawError>` — same accessors. No `…Result` variant. | none |
| 5 Cancel | `client.Api20100401Message` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` · `body` and `status` nullable **no default → pass explicitly**. HTTP `POST …/Messages/{Sid}.json`. Notes: “redact Message `body` text and cancel not-yet-sent messages”. | `Body` ← `body`, `Status` ← `status`. **Cancel:** `body: null`, `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`). Path `sid` = the create `Sid` (not a different id). | Same `ApiV2010AccountMessage`. After success, `Status` is `Canceled`. | **Case B** `SdkException<RawError>`. Already-sent / non-cancelable: no typed accessor — read `ex.Error.StatusCode` + `ReadAsString()` / `ReadAsJson<T>()` (**UNVERIFIED** live code). Detect “not yet sent” **before** calling: `FetchMessage` → `Status == MessageEnumStatus.Scheduled`. If status is `Sent` / `Delivered` / `Undelivered` / `Failed` / `Sending` / `Canceled` / etc., skip cancel. Whether `Queued`/`Accepted` are cancelable is **UNVERIFIED** — do not cancel those for this follow-up flow. | none |
| 6 Fetch | `client.Api20100401Message` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` · HTTP `GET …/Messages/{Sid}.json` | Path only: `AccountSid`, `Sid`. | Same `ApiV2010AccountMessage`. Read `Status`, `Body`, `ErrorCode`, `ErrorMessage`, `DateSent`. | **Case B** `SdkException<RawError>` (404 → unknown `Sid`). | none |
| 8 Redact body | `client.Api20100401Message` | Same `UpdateMessage` as cancel. | **Redact:** `body: ""` (empty string — omit/`null` drops the form field), `status: null`. **Do not** call `DeleteMessage` — that `DELETE`s the resource (`void`) and would drop outcome. | Same `ApiV2010AccountMessage`. **UNVERIFIED** whether post-redact `Body` is `""` or `null` on the wire — treat null-or-empty `Body` as redacted; `Status` and `Sid` survive. Confirm with `FetchMessage`. | **Case B** `SdkException<RawError>`. | none |
| 8 Do not use | `client.Api20100401Message` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` | Case B | none |
| 9 List (reconciliation) | `client.Api20100401Message` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` · 8 nullable params (`to`…`pageToken`) **no default → pass `null` to skip**. HTTP `GET …/Messages.json` (Default / api — **`Twilio:BaseUrl` applies**). | `To` ← `to` (pass `null` unless filtering recipient), `From` ← `from` (**set to `Twilio:FromNumber`** — provider-side filter, not post-filter), `DateSent` ← `dateSent` (exact; pass `null` for a range), `DateSent<` ← `dateSentQuery` (**range end** / before), `DateSent>` ← `dateSentQueryQuery` (**range start** / after), `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken`. List dates are converted with `ToIso8601()` → UTC `yyyy-MM-ddTHH:mm:ss.fff'Z'` (XML comment says GMT `YYYY-MM-DD`; the **code** sends full ISO-8601 UTC). XML: `pageSize` default 50, max 1000; `page` is client state; `pageToken` is provided by the API. | `TwilioSdk.Models.ListMessageResponse` (records-4-Li-Me.md): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus `End (end)`, `FirstPageUri (first_page_uri)`, `NextPageUri (next_page_uri)`, `Page (page)`, `PageSize (page_size)`, `PreviousPageUri (previous_page_uri)`, `Start (start)`, `Uri (uri)`. Item fields as in create/fetch (`Sid`, `From`, `To`, `Status`, `DateSent`, `Body`, …). | **Case B** `SdkException<RawError>`. | **None built-in** (map: “only `page`, no `perPage`”). Page the whole range: loop while `NextPageUri` is non-null; pass `pageToken` from the API on the next `ListMessage`. No auto-iterator. |

**CreateMessage / UpdateMessage / DeleteMessage header (not a parameter):** each call attaches `Idempotency-Key: Guid.NewGuid()` internally. `RequestOptions` cannot override it. Cite: `Api/Api20100401Message.cs`.

**`SendAt` encoding:** form-flattened via JSON then `Convert.ToString` (not `ToIso8601()`). Pass a UTC `DateTimeOffset`. How far ahead is allowed is **not** in the map or XML — **UNVERIFIED**. Treat a Case B 4xx on scheduled `CreateMessage` as a rejected schedule (including out-of-window); do not invent a min/max lead time.

### Enums in scope (`TwilioSdk.Models.Enums`, `StringEnum<T>` — use static members or `FromValue("wire")`)

| Type | C# member (wire) | Cite |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `map/models/enums.md` · `Models/Enums/MessageEnumStatus.cs` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — **only** value; this is what cancel sends | `enums.md` |
| `MessageEnumScheduleType` | `Fixed (fixed)` — **only** value. XML: Messaging Services only, together with SendAt (`send_time` in that comment; C# param is `sendAt`) | `enums.md` · `MessageEnumScheduleType.cs` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | `enums.md` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — pass `null` unless needed | `enums.md` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` — pass `null` | `enums.md` |
| `MessageEnumTrafficType` | `Free (free)` — pass `null` | `enums.md` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` — pass `null` | `enums.md` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | `enums.md` |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — **not** the type of `FetchPhoneNumber3`’s `fields` param (that is `string?`). Enum also **omits** XML value `validation`. | `enums.md` · `LookupsV2PhoneNumber.cs` XML |

`LineType` / `PhoneNumberEnumType` exist on **other** models. `LineTypeIntelligenceInfo.Type` is `string?`. Do not type that field as those enums. Which `Type` strings are SMS-capable is **UNVERIFIED** — reject when `Valid` is not `true`; if `LineTypeIntelligence.ErrorCode` is set, treat as not usable; do not invent a Type allow-list from training data.

### Usable SMS destination (step 2)

1. Call `FetchPhoneNumber3` with the caller-typed number; `countryCode` if national format; `fields: "validation,line_type_intelligence,line_status"`; remaining optionals `null`.
2. **Reject** if Case B (including 404) or if `Valid != true` or `PhoneNumber` is null/empty. Surface `ValidationErrors` when present.
3. **Store** `PhoneNumber` (E.164), not the input string.
4. `Valid` XML: “in a valid range that can be freely assigned by a carrier to a user” — not literally “SMS-capable”. Extra packages (`line_type_intelligence`, `line_status`) are requested; mapping their `string?` fields to SMS-capability is **UNVERIFIED**. If those objects are null, still accept on `Valid == true` + canonical `PhoneNumber` (packages may be unavailable). **UNVERIFIED** whether `Valid` populates without `fields` containing `validation` — always pass that field list.

### MessagingServiceSid (item 11)

| Operation | Uses `Twilio:MessagingServiceSid`? |
|---|---|
| Immediate `CreateMessage` | **No.** Use `from` = `Twilio:FromNumber`. Pass `messagingServiceSid: null`. |
| Scheduled `CreateMessage` | **Yes.** Param name `messagingServiceSid` (wire `MessagingServiceSid`). Required by `MessageEnumScheduleType` XML (Messaging Services only). Pass `from: null`. |
| Lookup, fetch, list, update (cancel/redact), delete | **Unused.** |

### Idempotency (item 7)

The SDK **does not** accept a caller-supplied idempotency key on message create.

| Mechanism | Reality | Cite |
|---|---|---|
| Create parameter | None | `operations/Api20100401Message.md` |
| `RequestOptions` | `LogLevel` only — no header map | `Core/RequestOptions.cs` |
| Header the SDK sends | `Idempotency-Key` = `Guid.NewGuid()` on **every** `CreateMessage` / `UpdateMessage` / `DeleteMessage` invocation | `Api/Api20100401Message.cs` |

Same operator key must not send twice: persist caller key → provider `Sid` in **app** storage and skip `CreateMessage` when the key exists. A fresh key is a new `CreateMessage` (the SDK will mint a new `Idempotency-Key` regardless).

### Errors (item 10) — all in-scope ops are Case B

Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Read `ex.Error.StatusCode`. Body: `ReadAsString()` or `ReadAsJson<T>()` — **not** `ex.ToString()`. Typed `{Operation}Error` / `TryGet…` accessors **do not exist** on these operations.

There is no generated payload type for Twilio REST error JSON on these ops. Provider numeric codes (invalid number, unreachable, etc.) are **UNVERIFIED** on the live wire — extract best-effort from the body, fall back to the generic message + HTTP status.

| HTTP (`RawError.StatusCode`) | Treat as |
|---|---|
| 400 | Invalid request (bad `To`/`From`/`SendAt`/cancel-not-allowed, etc.) |
| 401 / 403 | Auth / permission (`AccountSidAuthToken` or host) |
| 404 | Unknown message `Sid` or lookup number the API does not return |
| 429 | Rate limited |

`JsonException` is **not** an `SdkException` — see REQUIRED READING.

No-throw `…Result` variants: **absent** across this SDK (`sdk-map.md`).

---

## Trap notes

⚠ Step 1 (client registration) — constructor vs `AddTwilioSdkClient` disagree on who owns `HttpClient` lifetime; picking the wrong lifetime duplicates handlers or disposes a shared client. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient` or the DI callback.

⚠ Step 1 (auth) — credentials live on a typed options property, not a generic header; setting them after the client exists, or hardcoding secrets, fails at runtime. **MUST load `dotnet-authentication`** before assigning `AccountSidAuthToken`.

⚠ Step 1 (BaseUrl / retry / timeout) — `Retry` / `Timeout` on options are not the `HttpClient` timeout and do not bound a whole logical call; `HttpMethodsToRetry` is not the whole retry story, so a failed `CreateMessage` may execute more than once. Overriding the wrong `ServerOptions` node sends lookups (or other APIs) at the messaging base URL. **MUST load `dotnet-configuration-resilience`** before setting `Server.Default.Production.BaseUrl` or `Retry`.

⚠ Step 2–9 (calls) — `CreateMessage` / `FetchPhoneNumber3` / `ListMessage` have long nullable parameter lists with **no C# defaults**; positional calls bind the wrong arguments. The token parameter is `ct`. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ Step 2–9 (models) — statuses/schedule/validation errors are `StringEnum<T>` not C# enums; `ApiV2010AccountMessage` date fields are `string?` (RFC 2822 GMT), while `sendAt` / list filters are `DateTimeOffset?`; extra JSON is dropped on deserialize. **MUST load `dotnet-models`** before mapping `LookupResponse` or `ApiV2010AccountMessage`.

⚠ Step 9 (reconciliation paging) — `ListMessage` has **no** SDK auto-pager; stopping after page 1 under-counts the range. `NextPageUri` / `pageToken` mechanics and date-range completeness are not the signature. **MUST load `dotnet-configuration-resilience`** before writing the list loop.

⚠ Step 10 (error boundary) — every in-scope op is Case B (`SdkException<RawError>`); a catch ladder written for Case A `TryGet…` accessors never runs. `JsonException` also reaches this boundary from two directions (see REQUIRED READING). **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Tests — the `HttpClient` constructor argument is the seam; faking controllers or `TwilioSdkClient` internals couples tests to generated types. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

- `dotnet-client-initialization` — Step 1 client construction, `HttpClient` ownership, `AddTwilioSdkClient`.
- `dotnet-authentication` — Step 1 `AccountSidAuthToken` / `BasicAuthCredentials`.
- `dotnet-calling-endpoints` — Steps 2–9 named arguments, must-pass-null optionals, `ct:`.
- `dotnet-models` — Steps 2–9 `StringEnum<T>`, wire names, nullable records, string dates vs `DateTimeOffset`.
- `dotnet-configuration-resilience` — Step 1 retries/timeouts/`Twilio:BaseUrl` node selection; Step 9 list pagination.
- `dotnet-testing` — test seam for the integration layer.
- `dotnet-error-handling` — Step 10 boundary (always; every integration writes one).

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Lookups **v2** (`FetchPhoneNumber3`) is the registration validator; v1 is listed only to forbid it (no `Valid`).
- Immediate notifications send with `from` only; scheduled follow-ups send with `messagingServiceSid` only (`scheduleType` is Messaging Services only).
- `Twilio:AccountSid` is both `BasicAuthCredentials.Username` (SID+token mode) and the `accountSid` path argument.
- `Twilio:BaseUrl`, when set, is assigned verbatim to `options.Server.Default.Production.BaseUrl` and nowhere else.
- Content redaction uses `UpdateMessage(body: "")`, not `DeleteMessage`.
- Operator-resend idempotency is enforced in app storage because the SDK mints a new `Idempotency-Key` per call.

**Blockers**

- Caller-supplied idempotency key: **no SDK parameter, header setter, or `RequestOptions` member**. `CreateMessage` always sends `Idempotency-Key: Guid.NewGuid()`. Same-key semantics cannot be implemented through the SDK call itself.
- Schedule min/max lead time: **not** in the map or `CreateMessage` XML — **UNVERIFIED**. Handle via Case B on create.
- Live Twilio error-JSON `code` values (invalid number, unreachable, cancel-after-send): **UNVERIFIED**. Case B: HTTP status + best-effort body parse.
- `LineTypeIntelligenceInfo.Type` / `LineStatusInfo.Status` as SMS-capability: **UNVERIFIED** (untyped strings). Gate on `Valid` + canonical `PhoneNumber`.
- Post-redact `Body` exact value (`""` vs `null`): **UNVERIFIED**. Treat null-or-empty as redacted.
- Whether statuses other than `Scheduled` can be canceled: **UNVERIFIED**. Only cancel when `FetchMessage` returns `Scheduled`.
