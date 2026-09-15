# Twilio .NET SDK — eShopOnWeb SMS order-notification contract sheet

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk` (not `Twilio`). Client: `TwilioSdk.TwilioSdkClient`. Provenance: `sdk-map.md` stamp `51fdf48`.

## Scope & sequence

1. **Client, auth, environments, messaging-only BaseUrl** — construct `TwilioSdkClient` with AccountSid+AuthToken; override only `Server.Default` when `Twilio:BaseUrl` is set.
2. **Lookup / store canonical number** (`POST /api/contact-numbers`) — `LookupsV2PhoneNumber.FetchPhoneNumber3`; reject non-usable destinations; persist `LookupResponse.PhoneNumber`.
3. **Send SMS** (order placed / dispatched / cancelled / operator resend) — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`.
4. **Schedule dispatch follow-up at the provider** — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`.
5. **Cancel a not-yet-sent follow-up** (order cancelled) — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
6. **Fetch delivery outcome** (no public webhook) — `Api20100401Message.FetchMessage` by Sid.
7. **Redact message text at the provider** (`DELETE /api/notifications/{id}/content`) — `UpdateMessage` with `body` (not `DeleteMessage`).
8. **Reconciliation list** (`GET /api/notifications/reconciliation?from=&to=`) — `Api20100401Message.ListMessage` with `from` = `Twilio:FromNumber` and date-range params; page until exhausted.
9. **Resend idempotency** — SDK does **not** accept a caller idempotency key; implement in the application.
10. **Error boundary + tests** around every SDK call.

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

| Fact | Value | Source |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` — registers a singleton client; internally `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options | `TwilioSdk.TwilioSdkClientOptions`: `Environment` (`TwilioSdk.Servers.ServerEnvironment`), `Retry` (`TwilioSdk.Core.Configuration.RetryOptions`), `Logging` (`TwilioSdk.Core.Configuration.LoggingOptions`), `Server` (`TwilioSdk.ServerOptions`), `AccountSidAuthToken` (`TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Auth | `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required string` | `sdk-map.md` Servers & auth, `BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `"production"`). Only member. Default = Production. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Messaging host (create/fetch/list/update/delete message) | Operations use `_server.Default(...)` → `options.Server.Default.Production.BaseUrl`, default `"https://api.twilio.com"` | `Api/Api20100401Message.cs`, `Servers/DefaultOptions.cs` |
| Lookup host | Operations use `_server.Default4(...)` → `options.Server.Default4.Production.BaseUrl`, default `"https://lookups.twilio.com"` | `Api/LookupsV2PhoneNumber.cs`, `Servers/Default4Options.cs` |
| `Twilio:BaseUrl` (messaging only) | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl`. Do **not** set `Default4` (or Default1–3, 5–14). Nested type: `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl` (`string`) | `ServerOptions.cs` (root ns `TwilioSdk`), `Servers/DefaultOptions.cs` |
| `RequestOptions` | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel`. No headers, no idempotency, no per-call base URL. | `Core/RequestOptions.cs` |

`TwilioSdk.ServerOptions` members (root namespace; `ServerOptions.cs`): `Default`, `Default1` … `Default14` — each a `TwilioSdk.Servers.DefaultNOptions` with nested `Production.BaseUrl`.

Config keys → SDK:

| Setting | SDK sink |
|---|---|
| `Twilio:AccountSid` | `BasicAuthCredentials.Username` **and** `CreateMessage`/`FetchMessage`/`ListMessage`/`UpdateMessage`/`DeleteMessage` param `accountSid` |
| `Twilio:AuthToken` | `BasicAuthCredentials.Password` |
| `Twilio:FromNumber` | `CreateMessage` param `from`; `ListMessage` param `from` (provider-side sender filter) |
| `Twilio:MessagingServiceSid` | `CreateMessage` param `messagingServiceSid` — **required for scheduled send** (see CreateMessage / schedule) |
| `Twilio:BaseUrl` | `options.Server.Default.Production.BaseUrl` only |

---

### 1. Phone number lookup / validation — `FetchPhoneNumber3`

Use **Lookups v2**, not v1. v1 (`LookupsV1PhoneNumberApi.FetchPhoneNumber2`) returns `LookupsV1PhoneNumber` with `Carrier` as `object?` and **no** `Valid` / `ValidationErrors` (`records-4-Li-Me.md`).

| | |
|---|---|
| Controller | `client.LookupsV1PhoneNumberApi` is **not** the integration op. Use `client.LookupsV2PhoneNumber` |
| Method | `FetchPhoneNumber3` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` on **Default4 (lookups)** |
| Signature | `Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` are nullable with **no C# default** — pass `null` to skip |
| Required | `phoneNumber` (`string`) — E.164 or national; XML: default country code is +1 |
| `fields` (wire `Fields`) | `string?` — comma-separated. XML possible values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. Pass `"line_type_intelligence,line_status"` (and `validation` if desired). This param is **not** `TwilioSdk.Models.Enums.Field` (that enum is for batch lookup models and lacks `validation`). |
| `countryCode` (wire `CountryCode`) | ISO 3166-1 alpha-2 when `phoneNumber` is national format |
| Returns | `TwilioSdk.Models.LookupResponse` — **flat record, no envelope wrapper** |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Pagination | none |
| Map | `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md` |

**`LookupResponse` fields this integration reads** (`Models/LookupResponse.cs`, `records-4-Li-Me.md`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber` (`phone_number`) | `string?` | **Canonical E.164** (`+` + country code + subscriber). **This is what gets stored.** |
| `NationalFormat` (`national_format`) | `string?` | National format (do not store as canonical) |
| `CallingCountryCode` (`calling_country_code`) | `string?` | E.164 prefix |
| `CountryCode` (`country_code`) | `string?` | ISO country |
| `Valid` (`valid`) | `bool?` | XML: true iff the number is in a valid range a carrier can freely assign to a user |
| `ValidationErrors` (`validation_errors`) | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid |
| `LineTypeIntelligence` (`line_type_intelligence`) | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus` (`line_status`) | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status` |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode` (`mobile_country_code`): `string?`, `MobileNetworkCode` (`mobile_network_code`): `string?`, `CarrierName` (`carrier_name`): `string?`, `Type` (`type`): `string?`, `ErrorCode` (`error_code`): `int?`. **`Type` is an untyped string** — not `TwilioSdk.Models.Enums.LineType`.

**`LineStatusInfo`**: `Status` (`status`): `string?`, `ErrorCode` (`error_code`): `int?`. `Status` is an untyped string; SDK lists no values.

**Reject-now rule (what the SDK actually exposes):**

- Reject when `Valid` is not `true`, or `ValidationErrors` is non-empty, or `PhoneNumber` is null/blank.
- `ValidationError` members (`enums.md`): `TooShort` (`TOO_SHORT`), `TooLong` (`TOO_LONG`), `InvalidButPossible` (`INVALID_BUT_POSSIBLE`), `InvalidCountryCode` (`INVALID_COUNTRY_CODE`), `InvalidLength` (`INVALID_LENGTH`), `NotANumber` (`NOT_A_NUMBER`).
- `LineTypeIntelligence.Type` / `LineStatus.Status` have **no SDK value list**. Which Type strings are SMS-capable is **UNVERIFIED** — treat extra Type/Status checks as application policy, not as a generated contract. `LineType` enum (`Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)`) is a **different type** (`enums.md` describes it as an override line type) and is **not** the type of `LineTypeIntelligenceInfo.Type`.

Invalid/unknown numbers that the Lookups host refuses at HTTP level throw Case B (read `ex.Error.StatusCode` + body). Successful 2xx with `Valid == false` is the in-band rejection, not an exception.

---

### 2. Send SMS — `CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)** |
| Signature | `Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — nullable, **no C# default** — pass `null` to skip |
| Body encoding | `application/x-www-form-urlencoded` (not JSON) |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **flat record, no envelope** |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

**Wire names (form field ← C# param):** `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid`. Path `{AccountSid}` ← `accountSid`.

**This app's immediate-send argument set:**

| Param | Value |
|---|---|
| `accountSid` | `Twilio:AccountSid` |
| `to` | stored canonical E.164 |
| `body` | notification text |
| `from` | `Twilio:FromNumber` |
| `messagingServiceSid` | `null` for immediate FromNumber sends (see schedule row for when to set it) |
| `statusCallback` | `null` — this app has no publicly reachable URL |
| `scheduleType` / `sendAt` | `null` for immediate send |
| all other optionals | `null` |

**`from` vs `messagingServiceSid`:** both are optional `string?`. Immediate order SMS uses `from` = `Twilio:FromNumber`. Scheduled send is documented on `MessageEnumScheduleType` as **Messaging Services only** — then pass `messagingServiceSid` = `Twilio:MessagingServiceSid` (and still may pass `from`). There is no SDK rule that both cannot be set together.

**Null form fields are omitted** (`ParameterFlattener`: `if (value is null) return []`). Empty string **is** sent.

---

### 3. Schedule a follow-up at the provider — same `CreateMessage`

| Param | Value | Source |
|---|---|---|
| `scheduleType` | `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `"fixed"`). **Only enum member.** | `enums.md`, `Models/Enums/MessageEnumScheduleType.cs` |
| `sendAt` | `DateTimeOffset?` — wire `SendAt` | `operations/Api20100401Message.md` |
| `messagingServiceSid` | required for schedule per enum XML: “For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter”. C# param is `sendAt` (not `send_time`). | `MessageEnumScheduleType.cs` |
| `from` / `body` / `to` / `accountSid` | same as send | |

**Min/max schedule window:** CreateMessage XML comments for `scheduleType` and `sendAt` are **empty**. The map and named source file do **not** document a numeric window. See Assumptions & Blockers.

**Scheduled create response:** same `ApiV2010AccountMessage`. Read `Sid` and `Status` — expect `MessageEnumStatus.Scheduled` (wire `"scheduled"`) when accepted as scheduled. Enum also has `Accepted`, `Queued`, etc. (`enums.md`).

---

### 4. Cancel a scheduled message — `UpdateMessage`

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default (api)) |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `Task<ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` |
| Wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

**Cancel call:** `body: null` (omitted on the wire), `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (only member; wire `"canceled"`).

**Status it moves to:** `MessageEnumStatus.Canceled` (wire `"canceled"`) on the returned resource when cancel succeeds.

**Already sent:** no typed error case. Throws Case B; HTTP status for “already sent” is **UNVERIFIED** — read `ex.Error.StatusCode` + `ReadAsString()`, best-effort extract, fall back to generic message. Do not treat a 2xx `CreateMessage` as delivery.

---

### 5. Fetch delivery outcome — `FetchMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default (api)) |
| Signature | `Task<ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

No webhook/`statusCallback` in this app — poll/fetch this resource.

---

### 6. Redact body (keep the fact of the send) — `UpdateMessage`, not `DeleteMessage`

| Op | Use? | Why |
|---|---|---|
| `UpdateMessage` with `body` set, `status: null` | **Yes** — this is the redact/cancel operation per method notes | Body text is what redact changes; resource remains |
| `DeleteMessage` | **No** | Notes: “Deletes a Message resource from your account” — wipes the resource, not just text |

`DeleteMessage` signature (do not call for this feature): `Task DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void`; Case B. HTTP `DELETE …/Messages/{Sid}.json`.

**Redact call:** `body: ""` (empty string — **must not pass `null`**, which omits `Body` entirely), `status: null`.

**What remains:** the Message resource is still fetchable (`Sid`, `Status`, `ErrorCode`, `From`, `To`, `DateSent`, …). Post-redact `Body` value is **UNVERIFIED** in the SDK (typical provider behavior is empty/`null`; do not code to a guessed string). `NumSegments` / `Status` / `ErrorCode` survive as fields on `ApiV2010AccountMessage`.

---

### 7. List messages for reconciliation — `ListMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default (api)) |
| Signature | `Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `to` … `pageToken` (8 params) |
| Error | **Case B** `SdkException<RawError>` |
| Map pagination cell | “none (only `page`, no `perPage`)” — SDK does **not** auto-iterate |
| Map | `operations/Api20100401Message.md`, `records-4-Li-Me.md` |

**C# names are not `DateSentBefore` / `DateSentAfter`.**

| C# param | Wire query | Meaning |
|---|---|---|
| `from` | `From` | **Sender filter** — pass `Twilio:FromNumber` so Twilio scopes to this app’s number |
| `to` | `To` | Recipient filter — pass `null` for reconciliation |
| `dateSent` | `DateSent` | Exact sent timestamp — pass `null` for a range |
| `dateSentQuery` | `DateSent<` | Upper bound (before) — pass the query’s `to` ISO-8601 DateTimeOffset |
| `dateSentQueryQuery` | `DateSent>` | Lower bound (after) — pass the query’s `from` ISO-8601 DateTimeOffset |
| `pageSize` | `PageSize` | XML: default 50, **maximum 1000** (`long?`) |
| `page` | `Page` | XML: page index, client state (`int?`) |
| `pageToken` | `PageToken` | XML: “provided by the API” (`string?`) |

Serialized via `DateTimeOffset.ToIso8601()` → `"yyyy-MM-ddTHH:mm:ss.fff'Z'"` UTC (`Core/Extensions/DateTimeOffsetExtensions.cs`). XML comments on the three date params describe REST `YYYY-MM-DD` / `<=` / `>=` **string** prefixes; the SDK does **not** add those prefixes — it sends three separate `DateTimeOffset` query params.

**This app’s list call:** `to: null`, `from: Twilio:FromNumber`, `dateSent: null`, `dateSentQuery: <range end>`, `dateSentQueryQuery: <range start>`, `pageSize: 1000` (or `null` for default 50), first page `page: null`, `pageToken: null`.

**`ListMessageResponse` envelope** (`records-4-Li-Me.md`):

| C# (wire) | Type |
|---|---|
| `Messages` (`messages`) | `IReadOnlyList<ApiV2010AccountMessage>?` — items |
| `NextPageUri` (`next_page_uri`) | `string?` — more pages when non-null |
| `PreviousPageUri` (`previous_page_uri`) | `string?` |
| `FirstPageUri` (`first_page_uri`) | `string?` |
| `Page` (`page`) | `int?` |
| `PageSize` (`page_size`) | `int?` |
| `Start` (`start`) / `End` (`end`) | `int?` |
| `Uri` (`uri`) | `string?` |

Walk the **whole** range by repeating `ListMessage` with the same filters and the next `pageToken` until `NextPageUri` is null. How to turn `NextPageUri` into `pageToken` is **not** a generated helper — **MUST load `dotnet-configuration-resilience`**.

---

### `ApiV2010AccountMessage` — fields the integration reads

(`records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`) — returned by Create/Fetch/Update; list items.

| C# (wire) | Type | Use |
|---|---|---|
| `Sid` (`sid`) | `string?` | Provider message id. Regex `^(SM\|MM)[0-9a-fA-F]{32}$`, length 34 |
| `Status` (`status`) | `MessageEnumStatus?` | Current delivery/schedule outcome |
| `ErrorCode` (`error_code`) | `int?` | Set when status is `failed` or `undelivered`; else null. XML: do not use programmatically as a stable contract |
| `ErrorMessage` (`error_message`) | `string?` | Description of `error_code` when failed/undelivered; else null. Same “do not use programmatically” XML |
| `From` (`from`) | `string?` | Sender |
| `To` (`to`) | `string?` | Recipient E.164 |
| `Body` (`body`) | `string?` | Text content |
| `DateSent` (`date_sent`) | `string?` | RFC 2822 GMT when sent (not `DateTimeOffset`) |
| `DateCreated` (`date_created`) | `string?` | RFC 2822 GMT |
| `DateUpdated` (`date_updated`) | `string?` | RFC 2822 GMT |
| `MessagingServiceSid` (`messaging_service_sid`) | `string?` | MG…; XML: unique default assigned if a Messaging Service is not used |
| `AccountSid` (`account_sid`) | `string?` | AC… |
| `Direction` (`direction`) | `MessageEnumDirection?` | Outbound API sends: `OutboundApi` (`outbound-api`) |
| `NumSegments` (`num_segments`) | `string?` | XML: `"0"` initially for Messaging Service until sender assigned |
| `NumMedia` (`num_media`) | `string?` | |
| `Price` (`price`) / `PriceUnit` (`price_unit`) | `string?` | After send |
| `Uri` (`uri`) | `string?` | Relative to `https://api.twilio.com` |
| `ApiVersion` (`api_version`) | `string?` | |
| `SubresourceUris` (`subresource_uris`) | `object?` | |

**`MessageEnumStatus`** (`enums.md`) — C# member (wire): `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**Carrier later refuses (API accepted, then undeliverable):** this is **not** a `CreateMessage` exception. Create returns 2xx with an early status (`Queued` / `Accepted` / `Sending` / `Scheduled`). Later `FetchMessage` shows `Failed` or `Undelivered` with `ErrorCode` / `ErrorMessage` populated. Treat that as an expected delivery outcome.

**`MessageEnumDirection`:** `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

**Other create enums (pass `null` unless needed):** `MessageEnumContentRetention`: `Retain (retain)`, `Discard (discard)`. `MessageEnumAddressRetention`: `Retain (retain)`, `Obfuscate (obfuscate)`. `MessageEnumTrafficType`: `Free (free)`. `MessageEnumRiskCheck`: `Enable (enable)`, `Disable (disable)`.

---

### 8. Idempotency for resend — **not exposed to the caller**

| Question | Contract |
|---|---|
| Does `CreateMessage` take an idempotency key / unique-name param? | **No.** Signature has no such parameter (`operations/Api20100401Message.md`). |
| Does `RequestOptions` accept a header? | **No.** Only `LogLevel?` (`Core/RequestOptions.cs`). |
| Does the SDK send `Idempotency-Key`? | **Yes, internally** — `CreateMessage` (and `UpdateMessage` / `DeleteMessage`) always add `new HeaderParam("Idempotency-Key", Guid.NewGuid())`. A **new** GUID per method invocation. The caller cannot supply or reuse a key. |
| Conversations `idempotencyKey` params | Exist on other controllers (e.g. Conversations v2); **not** on Messages create. |

**Application must implement resend idempotency itself** (store the operator key → persisted provider `Sid`; replay returns the existing row and does not call `CreateMessage` again).

---

### 9. Messaging-only BaseUrl

See Client construction table. Messaging ops (`CreateMessage`, `FetchMessage`, `ListMessage`, `UpdateMessage`, `DeleteMessage`) resolve through `TwilioSdk.Server.Default` → `options.Server.Default.Production.BaseUrl`. Lookup stays on `Default4` (`https://lookups.twilio.com` unless someone separately changes it — **do not**).

---

### 10. Phone numbers vs messaging hosts

| Operation | Server node | Default base |
|---|---|---|
| `FetchPhoneNumber3` / `FetchPhoneNumber2` | Default4 (lookups) | `https://lookups.twilio.com` |
| All `Api20100401Message.*` | Default (api) | `https://api.twilio.com` |

`Twilio:BaseUrl` overrides **Default only**.

---

### 11. Errors — every in-scope operation is Case B

No `…Result` (no-throw) variants exist in this SDK (`sdk-map.md`).

| Operation | Thrown type |
|---|---|
| `FetchPhoneNumber3` | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| `FetchPhoneNumber2` (if used) | same |
| `CreateMessage` | same |
| `FetchMessage` | same |
| `ListMessage` | same |
| `UpdateMessage` | same |
| `DeleteMessage` | same |

`SdkException<TError>` (`Core/Exceptions/SdkException.cs`): `public required TError Error { get; init; }` — **no** HTTP status on the exception itself.

`RawError` (`Core/ErrorResponse/RawError.cs`): `StatusCode: System.Net.HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?`. **No** `TryGet…` accessors. There is **no** generated `{CreateMessage}Error` / Twilio error record for these ops.

**Reading provider error code/message from an HTTP failure:** `ex.Error.StatusCode`; body via `ReadAsString()` / `ReadAsJson<T>()`. JSON shape of that body is **UNVERIFIED** (no generated model) — extract best-effort (`code` / `message` if present), fall back to a generic message.

**Reading delivery failure after a successful create:** not an exception — `ApiV2010AccountMessage.Status` + `ErrorCode` + `ErrorMessage` on Fetch/List.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime vs the SDK wrapper, and `AddTwilioSdkClient` vs `new TwilioSdkClient`, are not visible from the constructor signature. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (auth) — which credentials property to set, username vs password, and when they must be applied are not visible from `BasicAuthCredentials` alone. **MUST load `dotnet-authentication`** before constructing the client.

⚠ Step 1 (retries / timeout / messaging BaseUrl) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; they also do not tell you which verbs are retried on transport failure. **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Timeout`, or `Server.Default.Production.BaseUrl`.

⚠ Steps 2–8 (every call) — CreateMessage has 24 must-pass-explicitly nullables; ListMessage has 8; FetchPhoneNumber3 has 15. Positional calls mis-bind. Named arguments; cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first SDK call.

⚠ Steps 2–8 (models / enums) — `MessageEnumStatus` / `ValidationError` / `MessageEnumScheduleType` are `StringEnum<T>`, not C# enums; `LineTypeIntelligenceInfo.Type` is `string?` not `LineType`; date fields on the message resource are `string?` (RFC 2822), not `DateTimeOffset`. **MUST load `dotnet-models`** before mapping SDK records onto domain types.

⚠ Step 7 (reconciliation paging) — `ListMessage` does not auto-walk `NextPageUri`; `page` / `pageSize` / `pageToken` vs `NextPageUri` is a paging hazard, and a missed page silently under-counts. **MUST load `dotnet-configuration-resilience`** before looping the date range.

⚠ Step 10 (error boundary) — all in-scope ops are Case B (`SdkException<RawError>`), so a Case A `TryGet…` ladder will not compile; `RawError` has no `TryGetRawError`. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 11 (tests) — the `HttpClient` constructor argument is the test seam; do not stub SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `TwilioSdkClient` and `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, must-pass-explicitly nullables, `ct:` |
| `dotnet-models` | Steps 2–8 — records, `StringEnum<T>`, wire names, untyped `object?` / `string?` fields |
| `dotnet-error-handling` | Step 10 — Case B `SdkException<RawError>`, `JsonException` from 2xx **and** from failed error-object construction |
| `dotnet-configuration-resilience` | Step 1 retries/timeouts/BaseUrl; Step 7 list pagination |
| `dotnet-testing` | Step 11 — faking the SDK at the `HttpClient` seam |

---

## Assumptions & Blockers

- **Lookup API:** integration uses Lookups **v2** `FetchPhoneNumber3` because v1 has no `Valid` / `ValidationErrors` and types `Carrier` as `object?`.
- **Schedule window:** **BLOCKER for numeric min/max.** `CreateMessage` XML for `sendAt` / `scheduleType` is empty; `MessageEnumScheduleType` only documents Messaging Services + `fixed` + `send_time` (C# `sendAt`). Do not invent a window from outside the SDK.
- **Scheduling requires a Messaging Service:** enum XML says schedule is “For Messaging Services only”. If `Twilio:MessagingServiceSid` is unset, provider-side schedule cannot be expressed from this SDK’s documented contract. Immediate send still uses `Twilio:FromNumber`.
- **Idempotency:** not a missing operation — the Messages create API path **does** send `Idempotency-Key`, but the SDK always generates `Guid.NewGuid()` and exposes no caller parameter. Application-level idempotency is required.
- **`LineTypeIntelligenceInfo.Type` as SMS-capability:** **UNVERIFIED.** SDK type is `string?` with no value list. Numbering-plan rejection is `Valid != true`.
- **Cancel-after-sent HTTP status / error body JSON:** **UNVERIFIED.** Case B only; defensive extract from `ReadAsString()` / `ReadAsJson<T>()`, fall back to generic message.
- **Post-redact `Body`:** **UNVERIFIED.** Redact by sending `body: ""` (null omits the field). Do not call `DeleteMessage`.
- **`statusCallback`:** unused (`null`) because this app has no publicly reachable URL; delivery is via `FetchMessage`.
- **Root namespace** is `TwilioSdk` (package `AsadAli.TwilioSdk`), not `Twilio`.
- **List date filters:** there are no C# parameters named `DateSentBefore` / `DateSentAfter`; use `dateSentQuery` (`DateSent<`) and `dateSentQueryQuery` (`DateSent>`).
