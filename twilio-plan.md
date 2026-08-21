# Twilio .NET SDK — eShopOnWeb order SMS notifications

Package: `AsadAli.TwilioSdk` (version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient`.

## Scope & sequence

1. **Install + client** — construct `TwilioSdkClient` with AccountSid + AuthToken; optional messaging-only `Twilio:BaseUrl` on server node `Default` (not Lookup).
2. **Lookup / register** — `LookupsV2PhoneNumber.FetchPhoneNumber3`: reject unusable destinations at `POST /api/contact-numbers`; store canonical E.164 from the response.
3. **Send immediate SMS** — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber` (place, dispatch, cancel, resend). Catch send failures so the order operation still succeeds.
4. **Schedule follow-up** — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` = `Twilio:MessagingServiceSid`; persist returned `Sid` for later cancel.
5. **Cancel scheduled** — `Api20100401Message.UpdateMessage` with `status` = canceled, using the stored Sid.
6. **Fetch outcome** — `Api20100401Message.FetchMessage` by Sid (no webhooks).
7. **Reconcile** — `Api20100401Message.ListMessage` filtered by `from` = `Twilio:FromNumber` and the date range; page until exhausted.
8. **Resend** — `CreateMessage` again. **No caller-supplied idempotency parameter exists** (see Blockers).
9. **Redact body** — `Api20100401Message.UpdateMessage` with empty `body` (do **not** `DeleteMessage`; that removes the resource).
10. **Error boundary** — Case B `SdkException<RawError>` on every in-scope op, plus `JsonException` (see REQUIRED READING).

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

### Client construction & auth

| Fact | Value | Source |
|---|---|---|
| Client | `TwilioSdk.TwilioSdkClient` | `sdk-map.md` |
| Constructor | `TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.Action<TwilioSdk.TwilioSdkClientOptions>? configure = null)` — registers `TwilioSdkClient` via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options | `TwilioSdk.TwilioSdkClientOptions`: `Environment` (`TwilioSdk.Servers.ServerEnvironment`), `Retry` (`TwilioSdk.Core.Configuration.RetryOptions`), `Logging` (`TwilioSdk.Core.Configuration.LoggingOptions`), `Server` (`TwilioSdk.ServerOptions`), `AccountSidAuthToken` (`TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`); `ServerEnvironment.Default()` → Production | `Servers/ServerEnvironment.cs` |
| Auth credentials | `new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required` | `Core/Authentication/Basic/BasicAuthCredentials.cs`; `sdk-map.md` *Servers & auth* |
| Auth property | `options.AccountSidAuthToken` | `sdk-map.md` |
| Messaging base URL | All `Api20100401Message` ops use server node **Default (api)**. Override: `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl verbatim>`. Default when unset: `"https://api.twilio.com"`. Types: `TwilioSdk.ServerOptions.Default` → `TwilioSdk.Servers.DefaultOptions.Production` → `ProductionOptions.BaseUrl` | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Api/Api20100401Message.cs` (`_server.Default(...)`) |
| Lookup base URL | `LookupsV2PhoneNumber.FetchPhoneNumber3` uses server node **Default4 (lookups)**. Default: `"https://lookups.twilio.com"`. **Do not** apply `Twilio:BaseUrl` to `options.Server.Default4`. | `Servers/Default4Options.cs`, `Api/LookupsV2PhoneNumber.cs` (`_server.Default4(...)`) |
| `RequestOptions` | `TwilioSdk.Core.RequestOptions` — only member `LogLevel` (`Microsoft.Extensions.Logging.LogLevel?`). No header bag, no idempotency field. | `Core/RequestOptions.cs` |
| No-throw variants | Absent on every in-scope operation | `sdk-map.md` |

Config keys → SDK: `Twilio:AccountSid` / `Twilio:AuthToken` → `AccountSidAuthToken`; `Twilio:FromNumber` → `CreateMessage`/`ListMessage` `from`; `Twilio:MessagingServiceSid` → scheduled `CreateMessage` `messagingServiceSid`; `Twilio:BaseUrl` → `Server.Default.Production.BaseUrl` only.

Immediate send uses **`from` = `Twilio:FromNumber`** and **`messagingServiceSid: null`**. Scheduling uses **`messagingServiceSid` = `Twilio:MessagingServiceSid`** (see CreateMessage + `MessageEnumScheduleType`).

### Operations

#### 1. Lookup — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`

| | |
|---|---|
| Map | `map/operations/LookupsV2PhoneNumber.md` · source `Api/LookupsV2PhoneNumber.cs` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · **Default4 (lookups)** — BaseUrl does **not** apply |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 15 params `fields` … `partnerSubId` — nullable, no default → pass `null` to skip |
| Path | `phoneNumber` — E.164 or national; default country +1 (`Api/LookupsV2PhoneNumber.cs` XML) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity/reassigned/prefill params unused here: pass `null`) |
| `fields` for SMS usability | comma-separated wire values from `TwilioSdk.Models.Enums.Field`: `line_type_intelligence` (`Field.LineTypeIntelligence`), `line_status` (`Field.LineStatus`). Pass e.g. `"line_type_intelligence,line_status"`. |
| Returns | `TwilioSdk.Models.LookupResponse` (**not** wrapped) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Pagination | none |

**`LookupResponse` fields used** (`map/models/records-4-Li-Me.md`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber` (`phone_number`) | `string?` | Canonical **E.164** (`+` + country code + subscriber). **This is what to store.** |
| `NationalFormat` (`national_format`) | `string?` | National display form — do not store as canonical |
| `Valid` (`valid`) | `bool?` | `true` = number is in a range a carrier can assign; `false` = **not a usable destination** |
| `ValidationErrors` (`validation_errors`) | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid |
| `CallingCountryCode` (`calling_country_code`) | `string?` | E.164 prefix |
| `CountryCode` (`country_code`) | `string?` | ISO 3166-1 alpha-2 |
| `LineTypeIntelligence` (`line_type_intelligence`) | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | populated when `fields` includes `line_type_intelligence` |
| `LineStatus` (`line_status`) | `TwilioSdk.Models.LineStatusInfo?` | populated when `fields` includes `line_status` |

**`LineTypeIntelligenceInfo`** (`map/models/records-3-Fl-Li.md`): `MobileCountryCode` (`mobile_country_code`): `string?`, `MobileNetworkCode` (`mobile_network_code`): `string?`, `CarrierName` (`carrier_name`): `string?`, **`Type` (`type`): `string?`** (not the `LineType` enum), `ErrorCode` (`error_code`): `int?`.

**`LineStatusInfo`**: `Status` (`status`): `string?`, `ErrorCode` (`error_code`): `int?`. No SDK enum of status strings. **UNVERIFIED** live values — if `Status` is missing, decide from `Valid` + `Type`; if present and clearly not an active mobile line, reject.

**Reject at registration (usable SMS destination):**

- `SdkException<RawError>` with `Error.StatusCode == 404` (or other 4xx) → unusable / unknown number.
- `Valid == false` → unusable.
- `ValidationErrors` non-empty → unusable (see enum table).
- After requesting `line_type_intelligence`: compare `LineTypeIntelligence.Type` to `TwilioSdk.Models.Enums.LineType` **wire** values. Treat **`mobile`** as SMS-capable. Treat `landline`, `pager`, `voicemail`, `uan`, `unknown` as **not** SMS destinations. `tollFree` / `fixedVoip` / `nonFixedVoip` / `personal` / `premium` / `sharedCost` — **UNVERIFIED** as SMS; defensive: reject unless `Type` equals `mobile` (this account needs a mobile destination).
- `LineTypeIntelligence.ErrorCode` set / `Type` null after a successful lookup with that field requested → do not treat as confirmed mobile.

Do **not** use `LookupsV1PhoneNumberApi.FetchPhoneNumber2` for this flow (`Carrier` is `object?`, no `Valid` / E.164-documented `PhoneNumber` pair like V2).

---

#### 2. Send / schedule — `client.Api20100401Message.CreateMessage`

| | |
|---|---|
| Map | `map/operations/Api20100401Message.md` · source `Api/Api20100401Message.cs` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · **Default (api)** — `Twilio:BaseUrl` **applies** |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid` (path — `Twilio:AccountSid`), `to` (destination E.164) |
| Must-pass nullables | **24** params `statusCallback` … `contentSid` — no C# default → **must pass explicitly** (`null` to skip) |
| Form body (wire ← C#) | `To` ← `to`, `From` ← `from`, `Body` ← `body`, `MessagingServiceSid` ← `messagingServiceSid`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the other listed form fields |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (**not** wrapped) |
| Error | **Case B** `SdkException<RawError>` |
| Idempotency param | **None.** `CreateMessage` has no `idempotencyKey` (or any caller key) parameter. `RequestOptions` cannot set headers. Generated client always attaches header `Idempotency-Key` with a **new** `Guid.NewGuid()` on every Create/Update/Delete. Caller-supplied resend keys **cannot** be passed. |
| Pagination | none |

**Immediate SMS** (place / dispatch / cancel-notice / resend): named args with `from: <Twilio:FromNumber>`, `body: <text>`, `messagingServiceSid: null`, `scheduleType: null`, `sendAt: null`, all other optionals `null`. Do **not** send via Messaging Service for these.

**Scheduled follow-up** (dispatch + few days): `scheduleType: TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`), `sendAt: <DateTimeOffset>`, `messagingServiceSid: <Twilio:MessagingServiceSid>`, `body: <text>`, `from: null` (or `Twilio:FromNumber` if the service also binds a from — SDK allows both; enum XML: scheduling is **Messaging Services only**). Persist `Sid` from the return.

**Schedule constraints in the SDK:** only enum member is `Fixed`. `MessageEnumScheduleType` XML: *“For Messaging Services only: Include this parameter with a value of `fixed` in conjunction with the `send_time` parameter”* — the **actual** C#/wire name is `sendAt` / `SendAt`, not `send_time`. **Minimum schedule offset is not encoded in the SDK.** **UNVERIFIED** live minimum (Twilio typically rejects too-soon `SendAt`) — if create throws `SdkException<RawError>`, treat as schedule-failed (do not fail the order).

**Returned identifier / outcome (same model as fetch):** see `ApiV2010AccountMessage` below. Read `Sid`, `Status`, `ErrorCode`, `ErrorMessage`.

---

#### 3. Fetch by Sid — `client.Api20100401Message.FetchMessage`

| | |
|---|---|
| Map | `map/operations/Api20100401Message.md` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · **Default (api)** — BaseUrl **applies** |
| Signature | `FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` (404 → unknown Sid) |
| Pagination | none |

---

#### 4. List for reconciliation — `client.Api20100401Message.ListMessage`

| | |
|---|---|
| Map | `map/operations/Api20100401Message.md` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · **Default (api)** — BaseUrl **applies** |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 8 params `to` … `pageToken` |
| Query (wire ← C#) | `To` ← `to`, **`From` ← `from`**, `DateSent` ← `dateSent`, **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date serialization | SDK sends `dateTimeOffset.ToUniversalTime()` as **`yyyy-MM-ddTHH:mm:ss.fff'Z'`** (`Core/Extensions/DateTimeOffsetExtensions.cs`). XML also describes date-only `YYYY-MM-DD`; the generated client always emits the ISO-8601 form above. |
| Range mapping | `from:` API query `from`/`to` → `dateSentQueryQuery` = range **start** (`DateSent>`), `dateSentQuery` = range **end** (`DateSent<`). Pass `dateSent: null`. **`from:` SDK arg = `Twilio:FromNumber`** so the provider returns **this sending number’s** messages (do not list account-wide then filter). `to:` SDK arg = `null`. |
| Page size | XML: default **50**, maximum **1000** (`pageSize`) |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **No auto-pager.** Map: “Pagination: none (only `page`, no `perPage`)”. Cover the whole range by looping: while `NextPageUri` is non-null, call again with `pageToken` from the API (`PageToken` query on `NextPageUri`). |

**`ListMessageResponse`** (`map/models/records-4-Li-Me.md`): `End` (`end`): `int?`, `FirstPageUri` (`first_page_uri`): `string?`, **`NextPageUri` (`next_page_uri`): `string?`**, `Page` (`page`): `int?`, `PageSize` (`page_size`): `int?`, `PreviousPageUri` (`previous_page_uri`): `string?`, `Start` (`start`): `int?`, `Uri` (`uri`): `string?`, **`Messages` (`messages`): `IReadOnlyList<ApiV2010AccountMessage>?`**.

---

#### 5. Cancel scheduled / redact body — `client.Api20100401Message.UpdateMessage`

| | |
|---|---|
| Map | `map/operations/Api20100401Message.md` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · **Default (api)** — BaseUrl **applies** |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `body` and `status` — nullable, no default |
| Form (wire ← C#) | `Body` ← `body`, `Status` ← `status` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |

| Intent | Args | Expected `Status` on success |
|---|---|---|
| **Cancel** not-yet-sent follow-up | `body: null`, `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`) | `MessageEnumStatus.Canceled` |
| **Redact** text (`DELETE …/content`) | `body: ""` (empty string), `status: null` | resource remains; `Body` empty; Sid/status/error/from/to/dates survive |

Already-sent cancel: SDK does not type a distinct error. **UNVERIFIED** live status/body — catch `SdkException<RawError>`, read `StatusCode` + `ReadAsString()`/`ReadAsJson<T>()`; treat 4xx as “could not cancel (likely already sent)”.

**Do not** use `DeleteMessage` for content disposal: it **deletes the Message resource** (`DELETE …/Messages/{Sid}.json`), which removes the provider record of the send.

---

#### 6. `DeleteMessage` (out of scope for this product behavior)

`DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void`; Case B. Only if a future requirement needs full resource deletion.

---

### Response model — `TwilioSdk.Models.ApiV2010AccountMessage`

`map/models/records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs` — **no envelope wrapper**.

| C# (wire) | Type | Use |
|---|---|---|
| `Sid` (`sid`) | `string?` | Provider id (`SM`/`MM` + 32 hex). Persist this. |
| `Status` (`status`) | `MessageEnumStatus?` | Delivery / schedule state |
| `ErrorCode` (`error_code`) | `int?` | Set when `failed` / `undelivered` |
| `ErrorMessage` (`error_message`) | `string?` | Description of `error_code` (XML: do not treat as a stable programmatic contract) |
| `From` (`from`) | `string?` | Sender E.164 |
| `To` (`to`) | `string?` | Destination E.164 |
| `Body` (`body`) | `string?` | Text; empty after redact |
| `DateSent` (`date_sent`) | `string?` | RFC 2822 GMT (not `DateTimeOffset`) |
| `DateCreated` (`date_created`) | `string?` | RFC 2822 GMT |
| `DateUpdated` (`date_updated`) | `string?` | RFC 2822 GMT |
| `Direction` (`direction`) | `MessageEnumDirection?` | Outbound API sends → `outbound-api` |
| `MessagingServiceSid` (`messaging_service_sid`) | `string?` | Set when a Messaging Service was used |
| `AccountSid` (`account_sid`) | `string?` | |
| `NumSegments` (`num_segments`) | `string?` | |
| `NumMedia` (`num_media`) | `string?` | |
| `Price` (`price`) | `string?` | |
| `PriceUnit` (`price_unit`) | `string?` | |
| `Uri` (`uri`) | `string?` | |
| `ApiVersion` (`api_version`) | `string?` | |
| `SubresourceUris` (`subresource_uris`) | `object?` | |

### Enums (`TwilioSdk.Models.Enums` — `StringEnum<T>`, **not** C# enums)

Use members (`MessageEnumStatus.Queued`) or `Type.FromValue("wire")`. `.Value` is the wire string. `map/models/enums.md`.

**`MessageEnumStatus`** (`Models/Enums/MessageEnumStatus.cs`):

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

**`MessageEnumDirection`:** `Inbound` (`inbound`), `OutboundApi` (`outbound-api`), `OutboundCall` (`outbound-call`), `OutboundReply` (`outbound-reply`).

**`MessageEnumScheduleType`:** `Fixed` (`fixed`) only.

**`MessageEnumUpdateStatus`:** `Canceled` (`canceled`) only.

**`ValidationError`:** `TooShort` (`TOO_SHORT`), `TooLong` (`TOO_LONG`), `InvalidButPossible` (`INVALID_BUT_POSSIBLE`), `InvalidCountryCode` (`INVALID_COUNTRY_CODE`), `InvalidLength` (`INVALID_LENGTH`), `NotANumber` (`NOT_A_NUMBER`).

**`Field` (Lookup `fields` query):** `CallerName` (`caller_name`), `SimSwap` (`sim_swap`), `CallForwarding` (`call_forwarding`), `LineTypeIntelligence` (`line_type_intelligence`), `LineStatus` (`line_status`), `IdentityMatch` (`identity_match`), `ReassignedNumber` (`reassigned_number`), `SmsPumpingRisk` (`sms_pumping_risk`).

**`LineType`** (vocabulary for `LineTypeIntelligenceInfo.Type` **string**; the Lookup field is **not** typed as this enum): `Mobile` (`mobile`), `Landline` (`landline`), `TollFree` (`tollFree`), `FixedVoip` (`fixedVoip`), `NonFixedVoip` (`nonFixedVoip`), `Personal` (`personal`), `Premium` (`premium`), `Voicemail` (`voicemail`), `SharedCost` (`sharedCost`), `Uan` (`uan`), `Pager` (`pager`), `Unknown` (`unknown`).

Also on CreateMessage if ever passed: `MessageEnumContentRetention` `Retain`/`Discard`; `MessageEnumAddressRetention` `Retain`/`Obfuscate`; `MessageEnumTrafficType` `Free`; `MessageEnumRiskCheck` `Enable`/`Disable` — this integration passes `null`.

### Error handling (every in-scope op is Case B)

```csharp
catch (TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError> ex)
{
    var http = ex.Error.StatusCode;          // System.Net.HttpStatusCode
    var body = ex.Error.ReadAsString();      // UTF-8 body
    // ex.Error.ReadAsJson<T>() / ReadAsBytes()
}
```

`SdkException<TError>` (`Core/Exceptions/SdkException.cs`): `required TError Error { get; init; }` — **no** HTTP status on the exception itself; use `RawError.StatusCode`.

`RawError` (`Core/ErrorResponse/RawError.cs`): `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. **No generated Twilio error-body type** and **no** `TryGet…` accessors (Case B). **UNVERIFIED** live JSON keys — extract best-effort (`code` / `message` / `status` if present via `ReadAsJson`/`JsonDocument`), else fall back to `ReadAsString()`.

| Failure class | How to detect |
|---|---|
| Auth | `StatusCode` 401 / 403 on any op |
| Number unusable at **registration** | Lookup 4xx **or** `Valid == false` / `ValidationErrors` / non-mobile `Type` (see Lookup) |
| Number unusable at **send** (should be rare if registration used Lookup) | `CreateMessage` throws `SdkException<RawError>` (typically 4xx) |
| Accepted but later undeliverable | `CreateMessage` **succeeds**; later `FetchMessage` has `Status` `failed` / `undelivered` and `ErrorCode`/`ErrorMessage`. US carrier refuse is this path, not a gap. |
| Not found | `FetchMessage` / `UpdateMessage` `StatusCode` 404 |
| Cancel after send | `UpdateMessage` throws Case B — **UNVERIFIED** code; treat 4xx as failed cancel |
| Transport / timeout | not `SdkException` — see resilience skill |

**Order operations must not fail because SMS failed:** wrap **only** `CreateMessage` (immediate + schedule) in a catch of `SdkException<RawError>` **and** `System.Text.Json.JsonException` (see REQUIRED READING). Lookup **should** fail `POST /api/contact-numbers`. Fetch/list/redact/cancel are their own API paths.

There is **no** `…Result` / Try variant.

---

## Trap notes

⚠ Step 1 (client / DI) — constructor takes `HttpClient`; `AddTwilioSdkClient` also creates one internally. Wrong lifetime/ownership of that client vs the SDK wrapper shows up as socket exhaustion or disposed-handler failures. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a nullable options property; a missing or swapped Username/Password surfaces later as 401, not at `new TwilioSdkClient`. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (BaseUrl / retries) — `TwilioSdkClientOptions.Retry` / `Timeout` are **not** the `HttpClient` timeout and do **not** bound a whole logical operation; `HttpMethodsToRetry` is not the whole retry story (transport failures can retry **POST**, including `CreateMessage`). Setting `Server.Default4` by mistake sends Lookup at the messaging BaseUrl. **MUST load `dotnet-configuration-resilience`** before setting `Server`, `Retry`, or paging `ListMessage`.

⚠ Steps 2–9 (calls) — `CreateMessage` / `FetchPhoneNumber3` / `ListMessage` have long positional lists of required-but-nullable parameters; a positional call binds the wrong argument. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–9 (models) — statuses/schedule/lookup fields are `StringEnum<T>` records, not C# enums; `LineTypeIntelligenceInfo.Type` is a `string?`, not `LineType`; list/message dates on the resource are RFC 2822 **strings**. Wrong construction drops or mis-compares values. **MUST load `dotnet-models`** before mapping payloads.

⚠ Step 10 (errors) — every in-scope op is Case B (`SdkException<RawError>` only). A catch of a typed `{Op}Error` never runs. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ Step 10 (JsonException / 2xx) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (JsonException / non-2xx) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the seam; substituting internals of `TwilioSdkClient` will not hold. **MUST load `dotnet-testing`** before writing tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `new TwilioSdkClient` / `AddTwilioSdkClient` / `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–9 — named arguments, must-pass nullables, `ct:` |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, wire names, nullability |
| `dotnet-error-handling` | Step 10 — Case B accessors, **both** `JsonException` directions, what to catch around send |
| `dotnet-configuration-resilience` | Step 1 + Step 7 — `Server.Default` vs `Default4`, retries/timeouts, `ListMessage` paging |
| `dotnet-testing` | Tests — `HttpClient` seam |

---

## Assumptions & Blockers

**Assumptions**

- Live Twilio account; messages send and cost money. `TWILIO_UNREACHABLE_TO_NUMBER` / US undeliverable is a **FetchMessage status** (`undelivered`/`failed`), not an SDK gap.
- No webhooks / no publicly reachable URL — outcomes only via `FetchMessage` / `ListMessage`.
- Immediate SMS always uses `Twilio:FromNumber` as `from` and does not pass `messagingServiceSid`. Scheduled follow-up uses `Twilio:MessagingServiceSid` + `MessageEnumScheduleType.Fixed` + `sendAt` because the schedule-type enum is documented as Messaging Services only.
- Follow-up delay (“a few days”) is chosen by the app as a `DateTimeOffset` passed to `sendAt`.
- Canonical number to persist is `LookupResponse.PhoneNumber` (E.164).
- Content disposal is **redact via empty `Body`**, not `DeleteMessage`.
- Lookups **v2** (`FetchPhoneNumber3`) is the validation API; v1 is unused.

**Blockers**

- **Caller-supplied idempotency on resend is not supported by this SDK.** `CreateMessage` has **no** idempotency parameter. `RequestOptions` cannot set `Idempotency-Key`. The generated client always sends `Idempotency-Key: <new Guid>` on each create. Repeating `POST /api/notifications/{id}/resend` with the same caller key **will still invoke a new provider create** if the application calls `CreateMessage` again. Do not invent an SDK workaround in application code beyond what product/policy allows; the contract gap is: **Message create has no idempotency parameter.**
- **Minimum `SendAt` offset** is not in the SDK surface. Treat provider rejection as `SdkException<RawError>` (**UNVERIFIED** exact status/body).
- **Live Lookup `line_status` strings** are not enumerated in the SDK (**UNVERIFIED**). Gate primarily on `Valid`, `ValidationErrors`, and `LineTypeIntelligence.Type == "mobile"`.
- **Twilio error JSON body** has no generated model (**UNVERIFIED** keys). Read via `RawError.ReadAsString()` / `ReadAsJson<T>()` best-effort.
