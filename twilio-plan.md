# Twilio .NET SDK — eShopOnWeb order SMS

NuGet: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdk.TwilioSdkClient`.

## 1. Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Install package; construct `TwilioSdkClient` with Account SID + Auth Token; optional messaging `BaseUrl` on **Default** only | client construction / auth / `ServerOptions` |
| 2 | Register shopper number: lookup typed input, store provider E.164, reject invalid | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS (order placed / dispatched / cancelled / resend) from `Twilio:FromNumber` | `Api20100401Message.CreateMessage` |
| 4 | On dispatch: schedule follow-up SMS with the provider (`sendAt` + `scheduleType`) | `Api20100401Message.CreateMessage` (same op; Messaging Service required by Notes) |
| 5 | Persist returned `Sid` as the provider identifier | create response `ApiV2010AccountMessage.Sid` |
| 6 | On order cancel: cancel a not-yet-sent scheduled follow-up | `Api20100401Message.UpdateMessage` (`status`) |
| 7 | Read delivery outcome (no webhooks) | `Api20100401Message.FetchMessage` |
| 8 | Redact message body at the provider; keep the resource | `Api20100401Message.UpdateMessage` (`body`) — **not** `DeleteMessage` |
| 9 | Reconciliation list, server-side `From` filter + date range | `Api20100401Message.ListMessage` |
| 10 | Error boundary around every SDK call | Case B `SdkException<RawError>` on all in-scope ops |

Out of scope as Twilio ops: app persistence, which order events fire which template, resend policy thresholds. Those are **YOUR CALL — not in the map**.

---

## 2. CONTRACT SHEET

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

### 2.1 Client, auth, servers

| Fact | Value | Source |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | sdk-map.md |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers `TwilioSdkClient` via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options members | `Environment`: `TwilioSdk.Servers.ServerEnvironment`; `Retry`: `TwilioSdk.Core.Configuration.RetryOptions`; `Logging`: `TwilioSdk.Core.Configuration.LoggingOptions`; `Server`: `TwilioSdk.ServerOptions`; `AccountSidAuthToken`: `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | sdk-map.md, `TwilioSdkClientOptions.cs` |
| Environment | Only `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). Default: `ServerEnvironment.Default()` → Production | sdk-map.md, `Servers/ServerEnvironment.cs` |
| Auth property | `options.AccountSidAuthToken` | sdk-map.md *Servers & auth* |
| Credentials type | `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials` — `Username` (`string`, `required`), `Password` (`string`, `required`) | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Auth scheme notes (XML) | Basic auth. API key as username + API key secret as password, **or** Account SID + Auth Token (docs limit SID+token to local testing). This task uses Account SID + Auth Token from `Twilio:AccountSid` / `Twilio:AuthToken` | sdk-map.md *Servers & auth* |
| Messaging host (Default) | Message create/fetch/list/update/delete use `_server.Default(...)` → `options.Server.Default.Production.BaseUrl`, default `"https://api.twilio.com"` | operations/Api20100401Message.md (Default (api)); `Servers/DefaultOptions.cs`; `Server.cs` |
| Lookup host (Default4) | Lookup uses `_server.Default4(...)` → `options.Server.Default4.Production.BaseUrl`, default `"https://lookups.twilio.com"` | operations/LookupsV2PhoneNumber.md (Default4 (lookups)); `Servers/Default4Options.cs` |
| `Twilio:BaseUrl` | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` only. Do **not** assign it to `Default4` (or Default1–3, Default5–14). Nested types: `TwilioSdk.Servers.DefaultOptions` / `DefaultOptions.ProductionOptions` (`BaseUrl`: `string`) | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs` |
| `TwilioSdk.ServerOptions` | `Default`, `Default1` … `Default14` (each a `*Options` class with `Production.BaseUrl`) | `ServerOptions.cs` (repo root ⇒ `TwilioSdk`) |
| Per-call options | `TwilioSdk.Core.RequestOptions` — member `LogLevel`: `Microsoft.Extensions.Logging.LogLevel?` only. **No header collection.** | `Core/RequestOptions.cs` |
| Binding keys | `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` | YOUR CALL — not in the map (task-named configuration keys) |

`accountSid` on every Messages path is the Account SID string (same value as `Twilio:AccountSid` when using SID+token auth).

### 2.2 Operations

#### Lookup (registration) — `client.LookupsV1PhoneNumberApi` is **not** the one to use

`LookupsV1PhoneNumberApi.FetchPhoneNumber2` returns `TwilioSdk.Models.LookupsV1PhoneNumber` with `PhoneNumber` / `NationalFormat` but **no** `Valid` flag and `Carrier` as `object?`. Capability 1 needs canonical form **and** a validity signal → **V2**.

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `FetchPhoneNumber3` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 15 params (`fields` … `partnerSubId`) are nullable with **no default** → pass `null` to skip. `phoneNumber` is required. |
| Path | `PhoneNumber` ← `phoneNumber` — XML: E.164 or national format; default country code +1 (North America) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity-match / reassigned / pre_fill params listed in the signature |
| Returns | `TwilioSdk.Models.LookupResponse` (fields below — **no extra envelope wrapper**) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` |
| Pagination | none |
| No-throw variant | absent |
| Source | operations/LookupsV2PhoneNumber.md, records-4-Li-Me.md (`LookupResponse`), `Api/LookupsV2PhoneNumber.cs` |

**`fields` XML** (comma-separated): `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`.

In-scope `fields` value: `"line_type_intelligence,line_status"` (and optionally `validation` per XML). Pass remaining identity/PII params as `null`. Enum `TwilioSdk.Models.Enums.Field` has members for the extra packages but **does not** include `validation`; the operation param is `string?`, not `Field`.

**`LookupResponse` fields this step reads** (`TwilioSdk.Models`, none `required`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber` (`phone_number`) | `string?` | Provider canonical form — XML: E.164 (`+` + country code + subscriber). **Store this**, not the caller-typed input |
| `Valid` (`valid`) | `bool?` | XML: whether the number is in a valid range that can be freely assigned by a carrier. **Reject registration when this is not `true`** |
| `ValidationErrors` (`validation_errors`) | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid (see enum table) |
| `NationalFormat` (`national_format`) | `string?` | National format (do not store as canonical) |
| `CountryCode` (`country_code`) | `string?` | ISO country code |
| `CallingCountryCode` (`calling_country_code`) | `string?` | E.164 prefix |
| `LineTypeIntelligence` (`line_type_intelligence`) | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Present when `fields` requested it. Nested: `Type` (`type`): `string?` — **no value list in map or source**; `CarrierName`, `MobileCountryCode`, `MobileNetworkCode`, `ErrorCode` |
| `LineStatus` (`line_status`) | `TwilioSdk.Models.LineStatusInfo?` | Nested: `Status` (`status`): `string?` — **no value list**; `ErrorCode` (`error_code`): `int?` |

SMS-specific “usable destination” beyond `Valid` is **UNVERIFIED** (`Type` / `Status` are untyped strings). Do not treat `TwilioSdk.Models.Enums.LineType` as this field’s type — that enum’s XML is “new line type to override the original line type”, a different model.

Left out of this call (pass `null`): `firstName` … `partnerSubId` (identity_match / reassigned_number / pre_fill / sms pumping). Notes do not tie those to acceptance of a plain lookup.

#### Create SMS / schedule SMS — `client.Api20100401Message.CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) |
| Notes | “Send a message” |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 24 params (`statusCallback` … `contentSid`) nullable, no default → **must pass explicitly** (`null` to skip). `accountSid`, `to` required. |
| Form wire ← C# | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (**no wrapper** — read fields on this record) |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| No-throw | absent |
| Source | operations/Api20100401Message.md, records-1-Ac-Ca.md, `Api/Api20100401Message.cs` |

**From vs MessagingServiceSid**

| Send kind | `from` | `messagingServiceSid` | `scheduleType` / `sendAt` |
|---|---|---|---|
| Immediate SMS (placed / dispatched notice / cancelled / resend) from this app’s number | `Twilio:FromNumber` | `null` | both `null` |
| Provider-queued follow-up | optional (`null` unless you also pass a From) | `Twilio:MessagingServiceSid` **required by Notes** | `scheduleType`: `MessageEnumScheduleType.Fixed` (wire `fixed`); `sendAt`: `DateTimeOffset` of send time |

Both `from` and `messagingServiceSid` are optional on the signature. `MessageEnumScheduleType` XML: **“For Messaging Services only”** — include `fixed` **in conjunction with** the send-time parameter. The enum text says `send_time`; the **actual** C# / wire names are `sendAt` / `SendAt`. Use `sendAt`. Whether the provider accepts `from` **and** `messagingServiceSid` together is **UNVERIFIED**.

**Schedule time**

- C# type: `DateTimeOffset?`. Wire: form field `SendAt`.
- CreateMessage passes `sendAt` through form flattening (JSON-serialize then string), **not** through `ToIso8601()`. List filters (below) **do** use `ToIso8601()`.
- Identifier to store for later cancel: `ApiV2010AccountMessage.Sid` (XML: unique Twilio-provided string; pattern `^(SM\|MM)[0-9a-fA-F]{32}$`).
- How far in the future: **not stated** in the operation Notes or CreateMessage XML. A “few days later” is a `sendAt` value the app computes. Whether the provider rejects times outside an undocumented window is **UNVERIFIED** — a rejection is Case B `SdkException<RawError>`, not a missing operation.

**Immediate-send fields to pass; all other optionals `null`**

In: `accountSid`, `to` (stored E.164), `from` (`Twilio:FromNumber`), `body` (SMS text). Out (Notes do not tie them to acceptance for a body SMS): `statusCallback` (no webhooks), `applicationSid`, `maxPrice`, `provideFeedback`, `attempt`, `validityPeriod`, `forceDelivery`, `contentRetention`, `addressRetention`, `smartEncoded`, `persistentAction`, `trafficType`, `shortenUrls`, `scheduleType`, `sendAt`, `sendAsMms`, `contentVariables`, `riskCheck`, `fallbackFrom`, `messagingServiceSid`, `mediaUrl`, `contentSid`.

**Scheduled follow-up:** same as immediate **plus** `messagingServiceSid`, `scheduleType = MessageEnumScheduleType.Fixed`, `sendAt = <few days later>`. Pass `from: null` unless you choose to also set it (UNVERIFIED together).

**201 vs later carrier fail:** CreateMessage on HTTP success returns `ApiV2010AccountMessage` (includes `Sid` + `Status`). Non-2xx throws Case B. Later carrier refusal is **not** a create-time throw; it appears on a subsequent fetch as `Status` / `ErrorCode` / `ErrorMessage`. That a given US destination is accepted then fails at the carrier is an account/runtime fact — handle via FetchMessage status, not as a create gap. **UNVERIFIED** which create-time `Status` the live account returns on 2xx.

**Idempotency (capability 9) — see Blocker §5.** CreateMessage has **no** caller idempotency parameter. The generated body always sends header `Idempotency-Key` with `Guid.NewGuid()` per invocation (`Api/Api20100401Message.cs`). `RequestOptions` cannot set headers. A second `CreateMessage` call therefore cannot reuse a caller key.

#### Fetch status — `FetchMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` |
| Error | Case B `SdkException<RawError>` |
| Source | operations/Api20100401Message.md |

Read `Status`, `ErrorCode`, `ErrorMessage`, `Sid`, `From`, `To`, `DateSent`, `Body`. XML: `ErrorCode` / `ErrorMessage` are set when `status` is `failed` or `undelivered`; otherwise `null`. XML also: those two fields’ values for a given cause are subject to change; “Users should not use the `error_code` and `error_message` fields programmatically.”

#### Cancel scheduled / redact body — `UpdateMessage` (one operation, two uses)

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `body` and `status` nullable, no default → pass explicitly |
| Form wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | Case B `SdkException<RawError>` |
| Source | operations/Api20100401Message.md, `Api/Api20100401Message.cs` |

Form flattening **omits nulls**; a non-null string (including empty) **is** sent.

| Use | `body` | `status` | Ids |
|---|---|---|---|
| Cancel not-yet-sent follow-up | `null` (omit Body) | `TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`) | `accountSid` + message `Sid` from create |
| Redact content | non-null string sent as `Body` | `null` (omit Status) | same |

Do **not** use `DeleteMessage` for redact or cancel: Notes say it “Deletes a Message resource from your account” (the resource is removed). Redact must leave send/outcome facts retrievable.

**Already sent:** Notes only describe cancel of **not-yet-sent** messages. Outcome of `status=canceled` after the message has sent is **UNVERIFIED**. Catch Case B; `FetchMessage` to read current `Status`. If it already sent, the follow-up cannot be unsent — YOUR CALL how the order-cancel path reports that.

**After redaction:** response type is still the full `ApiV2010AccountMessage` (`Sid`, `Status`, `ErrorCode`, `From`, `To`, `DateSent`, `Body`, …). Which `body` string the provider treats as redaction (vs a rewrite), and the exact post-redact `Body` value, are **UNVERIFIED** — after the call, if `Body` still equals the original text, treat redaction as failed.

#### Delete (not used for this product’s redact/cancel)

| | |
|---|---|
| `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `DELETE …/Messages/{Sid}.json`; returns `void`; Case B | operations/Api20100401Message.md |

#### List for reconciliation — `ListMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Notes | “Retrieve a list of Message resources associated with a Twilio Account” |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 8 params (`to` … `pageToken`) nullable, no default |
| Query wire ← C# | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, `DateSent<` ← `dateSentQuery`, `DateSent>` ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date encoding | SDK sends `dateSent` / `dateSentQuery` / `dateSentQueryQuery` via `ToIso8601()` = `yyyy-MM-ddTHH:mm:ss.fff'Z'` (UTC) | `Api/Api20100401Message.cs`, `Core/Extensions/DateTimeOffsetExtensions.cs` |
| XML on the three date params (identical copy) | Filter by Message `sent_date`. Accepts GMT dates: `YYYY-MM-DD` (specific day), `<=YYYY-MM-DD` (on and before), `>=YYYY-MM-DD` (on and after). The SDK does **not** prefix `<=`/`>=` on the value; inequalities are the query **names** `DateSent<` / `DateSent>` |
| Server-side From filter | `from` — XML: “Filter by sender. Set this parameter to `+15552229999` to retrieve a list of Message resources sent by `+15552229999`.” Pass `Twilio:FromNumber`. Pass `to: null` unless filtering recipients |
| Date range `[from, to]` | `dateSent: null`, `dateSentQueryQuery: from` (`DateSent>`), `dateSentQuery: to` (`DateSent<`). Inclusive vs exclusive on those inequalities: **UNVERIFIED** (XML describes prefixed `<=`/`>=` date-only forms; the SDK sends named `DateSent</>` + full ISO-8601) |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | Case B |
| Pagination | **No** SDK `Pageable`. Manual: `pageSize` XML default 50, max 1000; `page` “client state”; `pageToken` “provided by the API”. Map: “Pagination: none (only `page`, no `perPage`)” |
| Source | operations/Api20100401Message.md, records-4-Li-Me.md, `Api/Api20100401Message.cs` |

**`ListMessageResponse` envelope** (`TwilioSdk.Models`, none required):

| C# (wire) | Type |
|---|---|
| `Messages` (`messages`) | `IReadOnlyList<ApiV2010AccountMessage>?` — match eShop records by `Sid` |
| `End` (`end`), `Start` (`start`), `Page` (`page`), `PageSize` (`page_size`) | `int?` |
| `FirstPageUri` (`first_page_uri`), `NextPageUri` (`next_page_uri`), `PreviousPageUri` (`previous_page_uri`), `Uri` (`uri`) | `string?` |

Walk `NextPageUri` / `pageToken` until no next page. Filter is already `From=Twilio:FromNumber` on the provider — do not list the whole account then drop rows.

### 2.3 `ApiV2010AccountMessage` (create / fetch / update / list item)

Namespace `TwilioSdk.Models`. No field is `required`. Integration reads:

| C# (wire) | Type | Notes |
|---|---|---|
| `Sid` (`sid`) | `string?` | Provider message id; XML length 34, `^(SM\|MM)[0-9a-fA-F]{32}$` |
| `Status` (`status`) | `TwilioSdk.Models.Enums.MessageEnumStatus?` | see enum table |
| `Body` (`body`) | `string?` | text content |
| `From` (`from`) | `string?` | XML: E.164 / sender ID / short code / channel |
| `To` (`to`) | `string?` | XML: E.164 or channel address |
| `ErrorCode` (`error_code`) | `int?` | when failed/undelivered |
| `ErrorMessage` (`error_message`) | `string?` | when failed/undelivered |
| `DateSent` (`date_sent`) | `string?` | XML: RFC 2822 GMT |
| `DateCreated` (`date_created`) | `string?` | RFC 2822 GMT |
| `DateUpdated` (`date_updated`) | `string?` | RFC 2822 GMT |
| `MessagingServiceSid` (`messaging_service_sid`) | `string?` | XML: MG… ; default assigned if unused |
| `AccountSid` (`account_sid`) | `string?` | AC… |
| `Direction` (`direction`) | `MessageEnumDirection?` | outbound API sends: `OutboundApi` / `outbound-api` |
| `NumSegments` (`num_segments`) | `string?` | XML: Messaging Service sends start at `0` until a sender is assigned |
| `NumMedia` (`num_media`) | `string?` | |
| `Price` (`price`), `PriceUnit` (`price_unit`) | `string?` | populated after send/receive |
| `Uri` (`uri`), `ApiVersion` (`api_version`) | `string?` | |
| `SubresourceUris` (`subresource_uris`) | `object?` | |

Source: records-1-Ac-Ca.md, `Models/ApiV2010AccountMessage.cs`.

### 2.4 Enums in scope (`TwilioSdk.Models.Enums` — `StringEnum<T>`, **not** C# enums)

Construct with the static member or `T.FromValue("wire")`.

| Type | Members (C# = wire) | Source |
|---|---|---|
| `MessageEnumStatus` | `Queued` (`queued`), `Sending` (`sending`), `Sent` (`sent`), `Failed` (`failed`), `Delivered` (`delivered`), `Undelivered` (`undelivered`), `Receiving` (`receiving`), `Received` (`received`), `Accepted` (`accepted`), `Scheduled` (`scheduled`), `Read` (`read`), `PartiallyDelivered` (`partially_delivered`), `Canceled` (`canceled`) | enums.md, `Models/Enums/MessageEnumStatus.cs` |
| `MessageEnumUpdateStatus` | `Canceled` (`canceled`) | enums.md |
| `MessageEnumScheduleType` | `Fixed` (`fixed`) | enums.md |
| `MessageEnumDirection` | `Inbound` (`inbound`), `OutboundApi` (`outbound-api`), `OutboundCall` (`outbound-call`), `OutboundReply` (`outbound-reply`) | enums.md |
| `ValidationError` | `TooShort` (`TOO_SHORT`), `TooLong` (`TOO_LONG`), `InvalidButPossible` (`INVALID_BUT_POSSIBLE`), `InvalidCountryCode` (`INVALID_COUNTRY_CODE`), `InvalidLength` (`INVALID_LENGTH`), `NotANumber` (`NOT_A_NUMBER`) | enums.md |
| `Field` | `CallerName` (`caller_name`), `SimSwap` (`sim_swap`), `CallForwarding` (`call_forwarding`), `LineTypeIntelligence` (`line_type_intelligence`), `LineStatus` (`line_status`), `IdentityMatch` (`identity_match`), `ReassignedNumber` (`reassigned_number`), `SmsPumpingRisk` (`sms_pumping_risk`) | enums.md |
| `MessageEnumContentRetention` | `Retain` (`retain`), `Discard` (`discard`) | enums.md — pass `null` on create unless you opt in |
| `MessageEnumAddressRetention` | `Retain` (`retain`), `Obfuscate` (`obfuscate`) | enums.md — pass `null` |
| `MessageEnumTrafficType` | `Free` (`free`) | enums.md — pass `null` |
| `MessageEnumRiskCheck` | `Enable` (`enable`), `Disable` (`disable`) | enums.md — pass `null` |

**Status → “reached the shopper”:** the map/XML list values and point at external “detailed descriptions”; they do **not** define handset delivery. `ErrorCode`/`ErrorMessage` apply to `failed` / `undelivered`. Mapping to resend vs not is **YOUR CALL — not in the map**. `Read` is documented as WhatsApp only.

### 2.5 Errors (all in-scope operations)

| | |
|---|---|
| Throw type | `TwilioSdk.Core.Exceptions.SdkException<TError>` with `.Error` |
| In-scope TError | `TwilioSdk.Core.ErrorResponse.RawError` (Case B) on Lookup V2 and every Messages op |
| `RawError` | `StatusCode: System.Net.HttpStatusCode`; `ReadAsBytes(): ReadOnlyMemory<byte>`; `ReadAsString(): string`; `ReadAsJson<T>(): T?` |
| Typed `{Op}Error` / `TryGet…` | **none** on these operations |
| No-throw `…Result` | **absent SDK-wide** |
| Source | sdk-map.md error model; each operation row; `Core/Exceptions/SdkException.cs`; `Core/ErrorResponse/RawError.cs` |

A 2xx create that later fails at the carrier is **not** this exception; it is a message resource whose `Status` you poll with `FetchMessage`.

---

## 3. Trap notes

⚠ Step 1 (client / DI) — `HttpClient` lifetime versus the SDK wrapper is not visible from the constructor. A per-request client rebuild vs a long-lived pipeline changes socket and handler behaviour. **MUST load `dotnet-client-initialization`** before `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a credentials object on options, not ad-hoc headers; putting SID/token in source or logs is a secret leak. **MUST load `dotnet-authentication`** before wiring `Twilio:AccountSid` / `Twilio:AuthToken`.

⚠ Steps 2–9 (every call) — dozens of optional parameters have **no C# default** and mis-bind if passed positionally; the cancellation token is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `CreateMessage` / `FetchPhoneNumber3` / list call.

⚠ Steps 2–9 (models) — statuses, schedule type, and validation errors are `StringEnum<T>`, not C# enums; unmodeled JSON is dropped on deserialize; `LineTypeIntelligence.Type` is `string?` not `LineType`. **MUST load `dotnet-models`** before constructing enums or mapping `LookupResponse` / `ApiV2010AccountMessage`.

⚠ Step 10 (error boundary) — every in-scope op is Case B (`SdkException<RawError>`). A catch ladder built for typed `TryGet…` accessors will not compile or will miss the body. **MUST load `dotnet-error-handling`** before writing `try/catch`.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

⚠ Step 1 (resilience / BaseUrl) — the SDK’s retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `Twilio:BaseUrl` is a `Server.Default` override, not `HttpClient.BaseAddress`. **MUST load `dotnet-configuration-resilience`** before wiring retries, timeouts, or `ServerOptions`.

⚠ Step 3 (create SMS) — retry configuration can cause a non-idempotent write to execute more than once; CreateMessage cannot take a caller idempotency key (Blocker §5). **MUST load `dotnet-configuration-resilience`** before enabling retries on this client.

⚠ Tests — the constructor `HttpClient` argument is the test seam; do not stub SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `TwilioSdkClient` / `AddTwilioSdkClient` / `HttpClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` / secrets |
| `dotnet-calling-endpoints` | Steps 2–9 — named arguments, must-pass nulls, `ct` |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, request/response records, dropped JSON |
| `dotnet-error-handling` | Step 10 — Case B `RawError`, **and** both `JsonException` directions below |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, `Server.Default` BaseUrl, list paging |
| `dotnet-testing` | Tests of the integration layer |

`System.Text.Json.JsonException` reaches the boundary from two directions (opposite handling):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

### Assumptions

- Immediate SMS uses `from` = `Twilio:FromNumber` and does not send `messagingServiceSid`. Scheduled follow-up uses `messagingServiceSid` because `MessageEnumScheduleType` Notes say scheduling is for Messaging Services only.
- Lookup uses V2 `FetchPhoneNumber3`, not V1 `FetchPhoneNumber2` (V1 has no `Valid`).
- Registration stores `LookupResponse.PhoneNumber` and rejects when `Valid` is not `true`.
- `accountSid` path arguments use `Twilio:AccountSid`.
- No `statusCallback` (no public webhook URL).
- Redact = `UpdateMessage` `body`; cancel scheduled = `UpdateMessage` `status`; never `DeleteMessage` for those product actions.
- Follow-up delay (“a few days”) is computed by the app as `sendAt`; test destination numbers are not recorded here.
- Live traffic costs money — YOUR CALL how the app guards sends.

### Blockers

1. **Caller-supplied idempotency key on Messages create is not available.** `CreateMessage` has no idempotency parameter. The generated method always sets header `Idempotency-Key` to a new `Guid` per invocation. `TwilioSdk.Core.RequestOptions` only has `LogLevel` — it cannot override that header. Repeating `POST /api/notifications/{notificationId}/resend` under the same caller key therefore **cannot** be made into a single Twilio create via this SDK operation. Do not invent an app-side substitute as if it were a Twilio contract.
