# eShopOnWeb SMS order-notification — Twilio .NET SDK plan + contract sheet

Package: `AsadAli.TwilioSdk` (version-less: `dotnet add package AsadAli.TwilioSdk`) · Root namespace: `TwilioSdk` · Client: `TwilioSdkClient` · Source stamp: `51fdf48`

## Scope & sequence

1. **Client + config** — bind `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, optional `Twilio:BaseUrl`. Construct `TwilioSdkClient` with Account SID + Auth Token. If `Twilio:BaseUrl` is set, override **only** the messaging (Default / `api.twilio.com`) host; leave Lookup (`Default4` / `lookups.twilio.com`) unchanged.
2. **Flow 1 — contact numbers** — `LookupsV2PhoneNumber.FetchPhoneNumber3` to reject non-usable destinations at registration and persist the provider canonical E.164 (`LookupResponse.PhoneNumber`). GET/DELETE are local.
3. **Flow 2 — order SMS** — `Api20100401Message.CreateMessage` for placed / dispatched / cancelled (immediate). Same operation with `scheduleType` + `sendAt` to queue the dispatch follow-up at the provider. Persist `Sid` + `Status` (+ `ErrorCode`/`ErrorMessage` when present). Send failures must not fail the order operation; no number on file → skip send.
4. **Flow 2 — cancel follow-up** — `UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled` so a not-yet-sent scheduled message never goes out.
5. **Flow 2 — poll delivery** — `FetchMessage` by stored `Sid` (no webhook). Map `Status` into notification state.
6. **Flow 3 — resend** — another `CreateMessage`. The SDK does **not** accept a caller idempotency key; enforce the caller’s key in application storage (same key → do not call create again).
7. **Flow 3 — redact content** — `UpdateMessage` with empty `body` (leave the Message resource and status). Do **not** use `DeleteMessage` (that deletes the resource).
8. **Flow 3 — reconciliation** — `ListMessage` with `from: Twilio:FromNumber` and `DateSent>` / `DateSent<` for the ISO-8601 range; page until `NextPageUri` is null.

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

### Client construction / auth / messaging base-URL

| Fact | Value | Cite |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` — both required; no parameterless ctor | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI helper | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` — creates `HttpClient` via `IHttpClientFactory.CreateClient()` and registers the SDK client | `ServiceCollectionExtensions.cs` |
| Options type | `TwilioSdk.TwilioSdkClientOptions` | `TwilioSdkClientOptions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Auth credentials | `new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required string` | `sdk-map.md` Servers & auth · `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (only member; `Default()` → Production) | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Messaging host (Create/Fetch/List/Update/Delete Message) | `_server.Default(...)` → `options.Server.Default.Production.BaseUrl` default `"https://api.twilio.com"` | `Api/Api20100401Message.cs` · `Servers/DefaultOptions.cs` · `ServerOptions.cs` (repo root ⇒ `TwilioSdk`) |
| Lookup host | `_server.Default4(...)` → `options.Server.Default4.Production.BaseUrl` default `"https://lookups.twilio.com"` | `Api/LookupsV2PhoneNumber.cs` · `Servers/Default4Options.cs` |
| `Twilio:BaseUrl` override | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` only. Do **not** set `Default4` (or Default1–3, Default5–14). | `ServerOptions.cs` · `DefaultOptions.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` — sole public member `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No header bag. Caller **cannot** supply `Idempotency-Key`. | `Core/RequestOptions.cs` |
| Path AccountSid | Every Message operation’s `accountSid` argument is the same value as `Twilio:AccountSid` (URL template `{AccountSid}`). | `operations/Api20100401Message.md` |

`ServerOptions` (`TwilioSdk`, file at repo root): `Default`, `Default1` … `Default14` (`TwilioSdk.Servers.DefaultNOptions`). Messaging = `Default`; Lookup = `Default4`.

### 1. Phone number lookup / validation / canonical form

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `FetchPhoneNumber3` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · server **Default4 (lookups)** |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 15 nullable params (`fields` … `partnerSubId`) have **no C# default** — pass `null` to skip |
| Path | `{PhoneNumber}` ← `phoneNumber` (E.164 or national; default country +1) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity/reassigned/prefill params (unused here → `null`) |
| Returns | `TwilioSdk.Models.LookupResponse` — **no extra envelope field** |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` · accessors: `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` · no-throw variant: absent · pagination: none |
| Cite | `operations/LookupsV2PhoneNumber.md` · `records-4-Li-Me.md` · `Api/LookupsV2PhoneNumber.cs` |

**`fields` (query `Fields`)** — `string?`, comma-separated. XML-documented values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. (`TwilioSdk.Models.Enums.Field` wire names match the extra packages: `line_type_intelligence`, `line_status`, … — but this parameter is still a `string?`, not `IReadOnlyList<Field>`.)

**`LookupResponse` fields this flow reads** (`TwilioSdk.Models`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical E.164** (`+` + country code + subscriber). Persist this, not the caller’s raw input. |
| `Valid (valid)` | `bool?` | Provider: number is in a range a carrier can freely assign. Reject registration when this is not `true`. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid (see enum table). Non-empty → reject. |
| `NationalFormat (national_format)` | `string?` | Display only |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2 |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Only populated if `fields` includes `line_type_intelligence` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Only populated if `fields` includes `line_status` |

**`LineTypeIntelligenceInfo`** (`Models/LineTypeIntelligenceInfo.cs`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, **`Type (type): string?`** (not an enum), `ErrorCode (error_code): int?`.

**`LineStatusInfo`** (`Models/LineStatusInfo.cs`): **`Status (status): string?`** (not an enum), `ErrorCode (error_code): int?`.

Do **not** bind `Type` to `TwilioSdk.Models.Enums.LineType` — that enum is a different model (“override the original line type”). Lookup `Type` / line `Status` vocabularies are unconstrained strings. **UNVERIFIED** live values. Defensive: reject when `Valid` is not `true` or `ValidationErrors` is non-empty; if `LineTypeIntelligence.ErrorCode` is set, treat extra-package data as missing rather than as a usable-destination proof.

V1 `LookupsV1PhoneNumberApi.FetchPhoneNumber2` returns `LookupsV1PhoneNumber` with **no** `Valid` field — do not use V1 for this registration gate.

### 2 / 3. Create / send message (immediate) and schedule (provider queue)

Same operation. Immediate: `scheduleType: null`, `sendAt: null`. Scheduled follow-up: `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset a few days later>`.

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Method | `CreateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default (api)** · body: `application/x-www-form-urlencoded` |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 24 params (`statusCallback` … `contentSid`) nullable, **no default** — pass `null` to skip |
| Form fields (wire ← C#) | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **direct**; no wrapper |
| Error | **Case B** `SdkException<RawError>` · same accessors · no-throw: absent · pagination: none |
| Cite | `operations/Api20100401Message.md` · `records-1-Ac-Ca.md` |

**From vs Messaging Service SID**

| Param | Wire | Required in C#? | When this integration passes it |
|---|---|---|---|
| `from` | `From` | no (`string?`) | Immediate and scheduled sends that must originate from **this app’s sending number** (`Twilio:FromNumber`) so ListMessage `From=` reconciliation matches. |
| `messagingServiceSid` | `MessagingServiceSid` | no (`string?`) | **Required for scheduling** — `MessageEnumScheduleType` XML: “For Messaging Services only … in conjunction with the `send_time` parameter”. Pass `Twilio:MessagingServiceSid`. Immediate send may pass it or `null`. |
| `fallbackFrom` | `FallbackFrom` | no | unused → `null` |

Null form/query values are omitted (not sent as empty). Empty string **is** sent.

**This flow’s CreateMessage arguments** (all other optionals `null`): `accountSid`, `to` (canonical E.164), `from` (`Twilio:FromNumber`), `messagingServiceSid` (SID or `null` per table), `body`, `scheduleType` / `sendAt` (scheduled only).

**Internal header (not caller-settable):** CreateMessage always attaches `Idempotency-Key: {Guid.NewGuid()}` at invoke time. A second `CreateMessage` call always gets a new key. (`Api/Api20100401Message.cs`)

### 4. Cancel a scheduled message

| | |
|---|---|
| Method | `UpdateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) · form-urlencoded |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `body` and `status` — nullable, no default |
| Form (wire ← C#) | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Cancel call | `body: null`, `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`) |
| Notes | XML: “used to redact Message `body` text and to cancel not-yet-sent messages”. Also attaches a fresh `Idempotency-Key` Guid (not caller-controlled). |
| Cite | `operations/Api20100401Message.md` · `enums.md` · `Api/Api20100401Message.cs` |

### 5. Fetch message status / outcome (poll)

| | |
|---|---|
| Method | `FetchMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `operations/Api20100401Message.md` |

Provider identifier = `Sid` (`sid`), pattern `^(SM\|MM)[0-9a-fA-F]{32}$`. Outcome = `Status`. When `failed` / `undelivered`, `ErrorCode` / `ErrorMessage` may be set; XML: those two values “are subject to change” and “Users should not use the `error_code` and `error_message` fields programmatically.” Store them for display; drive control flow from `Status`.

### 6. List messages by From + date range (server-side) + pagination

| | |
|---|---|
| Method | `ListMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · Default (api) |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 8 params (`to` … `pageToken`) nullable, no default |
| Query (wire ← C#) | `To` ← `to`, **`From` ← `from`**, `DateSent` ← `dateSent` (ISO-8601 via `ToIso8601()`), **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| SDK auto-pagination | **none** (map: “only `page`, no `perPage`”). `TwilioSdk.Core.Pagination.Pageable<,>` exists in the SDK but **this operation does not return it**. |
| Cite | `operations/Api20100401Message.md` · `records-4-Li-Me.md` · `Api/Api20100401Message.cs` |

**Reconciliation arguments:** `to: null`, **`from: Twilio:FromNumber`** (server-side From filter — do not list the whole account then filter), `dateSent: null`, **`dateSentQueryQuery: {from}`** (`DateSent>` = sent after range start), **`dateSentQuery: {to}`** (`DateSent<` = sent before range end), `pageSize:` up to **1000** (`long?`; XML default 50, max 1000), first page `page: null`, `pageToken: null`.

SDK date format for these three DateTimeOffset query params: `yyyy-MM-ddTHH:mm:ss.fff'Z'` (UTC). (`Core/Extensions/DateTimeOffsetExtensions.cs`)

**`ListMessageResponse` envelope** (`Models/ListMessageResponse.cs`):

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` — inner list to reconcile |
| `NextPageUri (next_page_uri)` | `string?` — stop when null |
| `Page (page)` / `PageSize (page_size)` / `End (end)` / `Start (start)` | `int?` |
| `FirstPageUri (first_page_uri)` / `PreviousPageUri (previous_page_uri)` / `Uri (uri)` | `string?` |

XML: `page` is “simply for client state”; `pageToken` “is provided by the API”. Response has **no** `page_token` field — only `next_page_uri`. **UNVERIFIED** that `NextPageUri` always contains a `PageToken` query value. Defensive: if `NextPageUri` is non-null, parse its `PageToken` query string and pass it as `pageToken` on the next `ListMessage`; if `NextPageUri` is set but no `PageToken` can be parsed, stop paging and treat remaining coverage as incomplete (do not invent a second listing strategy). Cover the whole range by repeating until `NextPageUri` is null.

### 7. Redact message body at the provider (keep record + status)

| | |
|---|---|
| Method | `UpdateMessage` (same signature as cancel) |
| Redact call | `body: ""` (empty string — **not** `null`, or the Body field is omitted), `status: null` |
| Returns | `ApiV2010AccountMessage` (resource remains; `Status` survives) |
| Do not use | `DeleteMessage(string accountSid, string sid, …)` → `void` — XML: “Deletes a Message resource from your account” |
| Cite | `operations/Api20100401Message.md` · `Api/Api20100401Message.cs` · ParameterFlattener omits `null`, sends empty string |

### 8. Idempotent create-message

**Absent from the public SDK surface.** `CreateMessage` has no idempotency parameter. `RequestOptions` cannot set headers. The generated method always sends `Idempotency-Key: {new Guid}` per invocation (`Api/Api20100401Message.cs`). Repeating `CreateMessage` is always a distinct provider create.

Application must persist the caller’s resend key → provider `Sid` and skip create on repeat. (Other Twilio ops such as Payment expose `idempotencyKey` as a form field; Message create does not.)

### 9. Message resource — identifier + delivery outcome

**`TwilioSdk.Models.ApiV2010AccountMessage`** (`records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`) — returned as-is by Create/Fetch/Update; listed under `ListMessageResponse.Messages`.

| C# (wire) | Type | Integration use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider id — persist; `SM…` / `MM…` |
| `Status (status)` | `MessageEnumStatus?` | Delivery outcome — persist + report + poll |
| `To (to)` / `From (from)` | `string?` | E.164 endpoints |
| `Body (body)` | `string?` | Content (empty after redact) |
| `DateCreated (date_created)` / `DateSent (date_sent)` / `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT timestamps (strings, not `DateTimeOffset`) |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | `MG…` |
| `ErrorCode (error_code)` | `int?` | Set when status is failed/undelivered |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code` |
| `Direction (direction)` | `MessageEnumDirection?` | Outbound API sends → `OutboundApi` (`outbound-api`) |
| `AccountSid (account_sid)` | `string?` | `AC…` |
| `NumSegments (num_segments)` / `NumMedia (num_media)` / `Price (price)` / `PriceUnit (price_unit)` / `Uri (uri)` / `ApiVersion (api_version)` / `SubresourceUris (subresource_uris)` | see map | unused except as needed for report payloads |

Create/Fetch/Update have **no** extra envelope — read `result.Sid`, `result.Status` directly.

---

### Enums in scope (`TwilioSdk.Models.Enums` — `StringEnum<T>`, **not** C# enums)

Build with the static member or `Type.FromValue("wire")`. Compare via the member / `.Value`. Cite: `map/models/enums.md`.

| Type | Members (C# = wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — **only** member; this is the cancel-scheduled value |
| `MessageEnumScheduleType` | `Fixed (fixed)` — **only** member; Messaging Services only |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — for documenting `fields` wire names; Create/Fetch lookup still takes `string?` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — CreateMessage unused → `null` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` — unused → `null` |
| `MessageEnumTrafficType` | `Free (free)` — unused → `null` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` — unused → `null` |

**Status vs product events (from generated descriptions, not live traffic):** create immediate typically lands in `queued`/`accepted`/`sending`; scheduled create → `scheduled`; cancel → `canceled`; carrier refusal after accept → `failed` / `undelivered` (sandbox US unreachable is this path — API accepted the message). `delivered` is success. Poll with `FetchMessage` until a terminal status (`delivered`, `undelivered`, `failed`, `canceled`) or the product’s poll budget.

---

### Errors (all in-scope operations)

Every operation above is **throw-only Case B**:

```
catch (TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError> ex)
```

| Read | How |
|---|---|
| HTTP status | `ex.Error.StatusCode` (`System.Net.HttpStatusCode`) |
| Body bytes | `ex.Error.ReadAsBytes()` |
| Body text | `ex.Error.ReadAsString()` |
| Body JSON | `ex.Error.ReadAsJson<T>()` → `T?` |

No typed `{Operation}Error`, no `TryGet…` accessors. **UNVERIFIED** live error JSON property names. Defensive: `ReadAsJson<T>` best-effort into a local DTO if you define one; on failure use `ReadAsString()`; if that is empty, generic message. Do **not** parse `ex.ToString()`.

**Registration (must fail the HTTP action):** Lookup `SdkException<RawError>` (e.g. 404 invalid number) **or** 2xx with `Valid != true` / non-empty `ValidationErrors` → reject POST `/api/contact-numbers`.

**Send / schedule / cancel / fetch / list / redact (must not fail the order operation):** catch `SdkException<RawError>` at the notification boundary, persist failure/outcome on the notification record, return success from the order command. Shopper with no number: skip Twilio entirely.

`DeleteMessage` / `UpdateMessage` / `CreateMessage` also attach a random `Idempotency-Key`; that does not change the exception type.

---

## Trap notes

⚠ Step 1 (client registration) — `TwilioSdkClient` requires an `HttpClient`; DI and manual construction disagree on who owns that handler and how long the SDK client lives. A wrong lifetime shows up as socket exhaustion or disposed-client failures on later sends. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — credentials are a nested `BasicAuthCredentials` on `AccountSidAuthToken`, not loose SID/token properties; setting them after the client exists (or leaving them null) yields 401s that look like send failures. **MUST load `dotnet-authentication`** before wiring `Twilio:AccountSid` / `Twilio:AuthToken`.

⚠ Step 1 (base URL / retries / timeouts) — `Twilio:BaseUrl` is a `Server.Default` host override, not `HttpClient.BaseAddress`; retry/timeout options do **not** bound a whole CreateMessage the way the `HttpClient` timeout does, and a failed write may still be executed more than once. **MUST load `dotnet-configuration-resilience`** before setting `Server`, `Retry`, or listing pages.

⚠ Steps 2–8 (calling) — CreateMessage has **24** must-pass-null optionals; ListMessage’s date filters are `dateSent` / `dateSentQuery` / `dateSentQueryQuery` mapping to `DateSent` / `DateSent<` / `DateSent>`; named-argument mistakes silently bind the wrong filter or skip From-scoping. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–8 (models) — statuses and schedule/cancel values are `StringEnum<T>` records; Lookup `LineTypeIntelligence.Type` is a plain `string?`; message timestamps are RFC 2822 **strings**. Treating any of these as C# enums or `DateTimeOffset` compiles wrong or drops wire values. **MUST load `dotnet-models`** before mapping SDK records onto notification entities.

⚠ Steps 2–8 (errors) — every in-scope op is Case B (`SdkException<RawError>` only). A catch ladder that looks for typed `TryGet…` destination errors never runs; send failures that must not fail the order operation then escape or are misclassified. **MUST load `dotnet-error-handling`** before writing the notification/order boundary.

⚠ Step 2 (errors) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 2 (errors) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the generated controllers are not the fake seam; substituting them leaves the real `HttpClient` pipeline in play. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1: constructing / DI-registering `TwilioSdkClient` and `HttpClient` ownership
- `dotnet-authentication` — Step 1: `AccountSidAuthToken` / `BasicAuthCredentials`
- `dotnet-configuration-resilience` — Step 1 + Step 8: `Server.Default` base URL, retries/timeouts, ListMessage paging
- `dotnet-calling-endpoints` — Steps 2–8: named arguments, must-pass-null params, `ct:`
- `dotnet-models` — Steps 2–8: `StringEnum<T>`, request/response records, wire names
- `dotnet-error-handling` — Steps 2–8: Case B `SdkException<RawError>`, both `JsonException` directions, order-vs-notification boundary
- `dotnet-testing` — tests: `HttpClient` seam

---

## Assumptions & Blockers

**Assumptions**

- Lookup **v2** `FetchPhoneNumber3` is the registration gate (v1 has no `Valid`). Extra packages `line_type_intelligence,line_status` are requested; the hard reject is `Valid != true` / non-empty `ValidationErrors`. Lookup `Type`/`Status` strings are **UNVERIFIED**.
- Immediate SMS always passes `from: Twilio:FromNumber` so reconciliation `ListMessage(from: that number)` is the provider-side scope. Scheduling additionally passes `messagingServiceSid: Twilio:MessagingServiceSid` and `scheduleType: Fixed` (enum is Messaging Services only).
- Caller-controlled create-message idempotency is **not** on the SDK; resend idempotency is application-local (key → `Sid`). The SDK still sends its own random `Idempotency-Key` per invoke.
- Redact = `UpdateMessage(body: "")`; cancel scheduled = `UpdateMessage(status: Canceled)`. `DeleteMessage` is out of scope (it removes the resource).
- ListMessage range: `dateSentQueryQuery` = range `from` (`DateSent>`), `dateSentQuery` = range `to` (`DateSent<`). Next-page token is taken from `NextPageUri` (**UNVERIFIED** query shape) as specified above.
- Case B error JSON property names are **UNVERIFIED**; extract best-effort via `ReadAsString` / `ReadAsJson<T>`, fall back to a generic message.
- `Twilio:BaseUrl` is the origin for **Default (api)** only; Lookup stays on `https://lookups.twilio.com` unless someone sets `Server.Default4` (they must not).

**Blockers**

- None. All nine required capabilities exist except **caller-supplied** create-message idempotency, which is absent from the public API and is replaced by application-level key handling (not a missing send/schedule/cancel/fetch/list/redact/lookup capability).
