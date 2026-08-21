# Twilio .NET SDK — eShopOnWeb order-lifecycle SMS

NuGet: `AsadAli.TwilioSdk` (version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient(HttpClient, TwilioSdkClientOptions)`. Source stamp: `51fdf48`.

Config keys (env only, never hard-code): `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (optional, **messaging host only**).

---

## Scope & sequence

| Step | App capability | SDK operations |
|---|---|---|
| 1 | Client, auth, messaging BaseUrl override, Lookup left on default host | `new TwilioSdkClient` / `AddTwilioSdkClient` |
| 2 | Register shopper number: validate + store canonical E.164 | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Immediate SMS (order placed / dispatched / cancelled) | `Api20100401Message.CreateMessage` (`from` = `Twilio:FromNumber`, `scheduleType`/`sendAt` null) |
| 4 | Provider-queued follow-up a few days after dispatch | `Api20100401Message.CreateMessage` (`messagingServiceSid` + `scheduleType` + `sendAt`) |
| 5 | Cancel a follow-up that has not gone out | `Api20100401Message.UpdateMessage` (`status: MessageEnumUpdateStatus.Canceled`) |
| 6 | Read delivery outcome (no webhooks) | `Api20100401Message.FetchMessage` |
| 7 | Operator resend (new SID) | `Api20100401Message.CreateMessage` again |
| 8 | Dispose message text at the provider; keep delivery facts | `Api20100401Message.UpdateMessage` (`body` redact; **not** `DeleteMessage`) |
| 9 | Reconciliation for **this app’s From number only** | `Api20100401Message.ListMessage` with provider-side `from` + `DateSent>` / `DateSent<` |
| 10 | Swallow Twilio failures on order paths; classify errors on contact/reconciliation paths | Case B `SdkException<RawError>` on every in-scope op |

Out of scope: Voice, Conversations, Verify, TaskRouter, inbound webhooks/`statusCallback`, buying numbers, Copilot, `DeleteMessage`, Lookups v1.

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

| Fact | Value | Cite |
|---|---|---|
| Client | `TwilioSdk.TwilioSdkClient` | `sdk-map.md` |
| Ctor | `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` | `sdk-map.md` |
| Options | `TwilioSdk.TwilioSdkClientOptions` | `TwilioSdkClientOptions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Environment | **Only** `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). `ServerEnvironment.Default()` returns `Production`. | `Servers/ServerEnvironment.cs` |
| Auth scheme | Basic. `options.AccountSidAuthToken = new BasicAuthCredentials { Username = AccountSid, Password = AuthToken }` — both members `required string`. | `sdk-map.md` Servers & auth, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` registers **`AddSingleton`** over the **unnamed** `IHttpClientFactory` client (`CreateClient()` with no name). | `ServiceCollectionExtensions.cs` |
| Per-call options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel`. No header bag. | `Core/RequestOptions.cs` |
| No-throw `…Result` | **Absent** on every operation in this SDK. | `sdk-map.md` |

### Messaging vs Lookup hosts (BaseUrl)

Messages use server **Default (api)**; Lookup uses **Default4 (lookups)**. They are different hosts. `Twilio:BaseUrl` overrides **Default only**.

| Server property | Type | Default `Production.BaseUrl` | Used by |
|---|---|---|---|
| `options.Server.Default` | `TwilioSdk.Servers.DefaultOptions` | `"https://api.twilio.com"` | All `Api20100401Message.*` (`_server.Default(...)`) |
| `options.Server.Default4` | `TwilioSdk.Servers.Default4Options` | `"https://lookups.twilio.com"` | `LookupsV2PhoneNumber.FetchPhoneNumber3` (`_server.Default4(...)`) |

Nested override (the only environment that exists):

- `options.Server.Default.Production.BaseUrl` — `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl` (`string`)
- `options.Server.Default4.Production.BaseUrl` — `TwilioSdk.Servers.Default4Options.ProductionOptions.BaseUrl` (`string`)

When `Twilio:BaseUrl` is set, assign that string **verbatim** to `options.Server.Default.Production.BaseUrl` before constructing the client. Do **not** write it onto `Default4` (or `Default1`–`Default3` / `Default5`–`Default14`). When unset, leave `Default.Production.BaseUrl` at its default.

Cite: `ServerOptions.cs` (root namespace `TwilioSdk`), `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Api/Api20100401Message.cs`, `Api/LookupsV2PhoneNumber.cs`.

### Idempotency header (native, not caller-controlled)

`CreateMessage`, `UpdateMessage`, and `DeleteMessage` each send header `Idempotency-Key` with value `Guid.NewGuid()` compiled into the method body. `RequestOptions` cannot override headers. A second app-level `CreateMessage` therefore always gets a **new** key and a **new** Message SID. App idempotency stays in eShopOnWeb.

Cite: `Api/Api20100401Message.cs` (`new HeaderParam("Idempotency-Key", Guid.NewGuid())`), `Core/RequestOptions.cs`.

---

### 1. Lookup — validate + canonical form

**Controller:** `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`)  
**Op:** `FetchPhoneNumber3`  
**HTTP:** `GET /v2/PhoneNumbers/{PhoneNumber}` on Default4 (lookups)  
**Cite:** `map/operations/LookupsV2PhoneNumber.md`, `Api/LookupsV2PhoneNumber.cs`

**Signature** (named args; 15 trailing nullables have **no** C# default — pass `null` to skip):

```
Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(
    string phoneNumber,
    string? fields,
    string? countryCode,
    string? firstName,
    string? lastName,
    string? addressLine1,
    string? addressLine2,
    string? city,
    string? state,
    string? postalCode,
    string? addressCountryCode,
    string? nationalId,
    string? dateOfBirth,
    string? lastVerifiedDate,
    string? verificationSid,
    string? partnerSubId,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

| Param | Wire | Pass |
|---|---|---|
| `phoneNumber` (path, required) | `{PhoneNumber}` | Shopper-typed number. XML: E.164 or national; default country +1. |
| `fields` | `Fields` | `"line_type_intelligence,line_status"` (comma-separated). XML also lists `validation` (not on `Field` enum). Identity-match fields stay `null`. |
| `countryCode` | `CountryCode` | ISO-3166-1 alpha-2 when the input is national format; `null` when already E.164. |
| remaining identity/reassigned/prefill params | as map | `null` |

**Return (no envelope wrapper):** `TwilioSdk.Models.LookupResponse` — `map/models/records-4-Li-Me.md`, `Models/LookupResponse.cs`

| C# (wire) | Type | Use |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical form to store.** XML: E.164 (`+` + country code + subscriber). |
| `Valid (valid)` | `bool?` | XML: true iff the number is in a valid range a carrier can assign. **Reject registration when this is not `true`.** |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reasons when invalid. Reject when non-empty. |
| `NationalFormat (national_format)` | `string?` | Display only; do not store as canonical. |
| `CountryCode (country_code)` | `string?` | ISO country. |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix. |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Optional extra; see gap below. |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Optional extra; `Status (status): string?`, `ErrorCode (error_code): int?` — `Status` is **not** an enum. |

`LineTypeIntelligenceInfo` (`map/models/records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, **`Type (type): string?`** (not `LineType`), `ErrorCode (error_code): int?`.

**`ValidationError`** (`TwilioSdk.Models.Enums`, `map/models/enums.md`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

**`Field`** (query helper enum, not the `fields` parameter type): `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)`.

**Error:** Case B `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Accessors: `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. No `TryGet…`. No `…Result` variant.

**Not a usable destination (what the SDK actually encodes):**
1. HTTP non-2xx → `SdkException<RawError>` (do not store).
2. HTTP 2xx with `Valid != true` and/or non-empty `ValidationErrors` → reject; do not store.
3. Store `PhoneNumber` (E.164), never the raw input.

`LineTypeIntelligenceInfo.Type` is an untyped `string?`. A sibling enum `TwilioSdk.Models.Enums.LineType` (`Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)`) is **not** this property’s type (XML: “override the original line type” on another resource). **UNVERIFIED** whether Lookup `type` strings match those wire values. Defensive: fail closed on `Valid != true`; if `LineTypeIntelligence.ErrorCode` is set, treat the package as failed and do not store; do not treat a null `Type` as proof of SMS capability.

Lookups v1 (`FetchPhoneNumber2` → `LookupsV1PhoneNumber`) has **no** `Valid` field — do not use it.

---

### 2–4, 6–7. Messages create / fetch / update / list

**Controller:** `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`)  
**Cite:** `map/operations/Api20100401Message.md`, `Api/Api20100401Message.cs`  
All message ops: path `{AccountSid}` = `Twilio:AccountSid`. POST/DELETE send form-urlencoded bodies (map “Query params” column is **wire names**; source uses `FormUrlEncodedRequest` for create/update). Host: Default (api).

#### CreateMessage — immediate send, schedule, resend

**HTTP:** `POST /2010-04-01/Accounts/{AccountSid}/Messages.json`  
**Returns:** `TwilioSdk.Models.ApiV2010AccountMessage` (no wrapper)  
**Error:** Case B `SdkException<RawError>`  
**Pagination:** none

```
Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(
    string accountSid,
    string to,
    string? statusCallback,
    string? applicationSid,
    double? maxPrice,
    bool? provideFeedback,
    int? attempt,
    int? validityPeriod,
    bool? forceDelivery,
    TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention,
    TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention,
    bool? smartEncoded,
    IReadOnlyList<string>? persistentAction,
    TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType,
    bool? shortenUrls,
    TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType,
    DateTimeOffset? sendAt,
    bool? sendAsMms,
    string? contentVariables,
    TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck,
    string? from,
    string? fallbackFrom,
    string? messagingServiceSid,
    string? body,
    IReadOnlyList<string>? mediaUrl,
    string? contentSid,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

24 params from `statusCallback` … `contentSid` are nullable **with no C# default** → must pass explicitly (`null` to skip).

| C# param | Wire | Immediate (placed/dispatched/cancelled + resend) | Scheduled follow-up |
|---|---|---|---|
| `accountSid` | path | `Twilio:AccountSid` | same |
| `to` | `To` | stored E.164 | same |
| `statusCallback` | `StatusCallback` | **`null`** (no public URL) | `null` |
| `from` | `From` | **`Twilio:FromNumber`** | `Twilio:FromNumber` or `null` (service picks sender) |
| `messagingServiceSid` | `MessagingServiceSid` | `null` (not required for send) | **`Twilio:MessagingServiceSid` required** — enum XML: scheduling is “For Messaging Services only” |
| `body` | `Body` | SMS text | follow-up text |
| `scheduleType` | `ScheduleType` | `null` | **`MessageEnumScheduleType.Fixed`** (wire `fixed`) |
| `sendAt` | `SendAt` | `null` | `DateTimeOffset` a few days ahead (app-chosen). Flattened via JSON serialize → string (not `ToIso8601()`). |
| all other optionals | as map | `null` | `null` |

`statusCallback` / `applicationSid` unused — this app cannot receive callbacks.

**Identifier to persist:** `ApiV2010AccountMessage.Sid (sid): string?` (pattern `^(SM|MM)[0-9a-fA-F]{32}$`) — this is `notificationId`.  
**Initial status:** `Status (status): MessageEnumStatus?` — immediate typically `queued`/`accepted`/`sending`; scheduled typically `scheduled`.  
**Messaging Service SID on response:** `MessagingServiceSid (messaging_service_sid): string?`.

**`MessageEnumScheduleType`:** only `Fixed (fixed)`. XML still says “in conjunction with the `send_time` parameter”; the C# parameter is **`sendAt`**. Cite: `map/models/enums.md`, `Models/Enums/MessageEnumScheduleType.cs`.

**How far ahead:** **not stated** in the operation XML or enum XML. **UNVERIFIED.** Defensive: pass the app’s few-days-later `DateTimeOffset`; if the provider rejects it, `SdkException<RawError>` is the authority — do not fail the order.

**Resend:** another `CreateMessage` (same `to`/`from`/`body` pattern as immediate). Return the **new** `Sid`. Native `Idempotency-Key` cannot carry the app key.

#### FetchMessage — status / delivery outcome

**HTTP:** `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`  
**Signature:** `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`  
**Returns:** `ApiV2010AccountMessage`  
**Error:** Case B `SdkException<RawError>`

Fields the app reads (`map/models/records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`):

| C# (wire) | Type | Notes |
|---|---|---|
| `Sid (sid)` | `string?` | Message SID |
| `Status (status)` | `MessageEnumStatus?` | see enum table |
| `ErrorCode (error_code)` | `int?` | set when status is `failed` / `undelivered`; XML: do not branch programmatically on code values |
| `ErrorMessage (error_message)` | `string?` | same caveat |
| `Body (body)` | `string?` | text (empty after redact) |
| `From (from)` | `string?` | E.164 sender |
| `To (to)` | `string?` | E.164 destination |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT **string**, not `DateTimeOffset` |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT string |
| `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT string |
| `Direction (direction)` | `MessageEnumDirection?` | outbound API → `OutboundApi (outbound-api)` |
| `AccountSid (account_sid)` | `string?` | |

US carrier refusal after accept: `Status` `Undelivered` / `Failed` with `ErrorCode`/`ErrorMessage` — **expected**, not a gap. Create returning 2xx then later Fetch showing undelivered is the documented path.

#### UpdateMessage — cancel scheduled **or** redact body

**HTTP:** `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`  
**XML:** “used to redact Message `body` text and to cancel not-yet-sent messages”  
**Signature:** `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)`  
`body` and `status` nullable, **no default** → pass explicitly.  
**Returns:** `ApiV2010AccountMessage`  
**Error:** Case B `SdkException<RawError>`  
**Wire:** `Body` ← `body`, `Status` ← `status`

| Use | `body` | `status` |
|---|---|---|
| Cancel follow-up | `null` | **`MessageEnumUpdateStatus.Canceled`** (wire `canceled`) — **only** member of this enum |
| Redact content | `""` (empty string) — **this is the value that puts `Body` on the wire** | `null` (`Status` omitted) |

**Form omit vs send (`ParameterFlattener` + `FormUrlEncodedRequest`):** `body: null` is omitted (no `Body` field — Twilio never sees `Body=`). `body: ""` is **not** omitted: Flatten keeps empty strings (`string str => [(key, str)]`; only `null` returns `[]`) and `FormUrlEncodedContent` encodes `Body` with an empty value (`Body=`). There is **no other C# Body value** in the map or UpdateMessage XML. Operation remarks say Update is “used to redact Message `body` text”; the `body` `<param>` is empty — no token besides sending `Body`. Live 2xx + Fetch still returning the original `Body` after `body: ""` is therefore **not** an SDK omit. The SDK cannot send a different documented redacting Body. Treat that Fetch as redact-failed; do not invent another payload. Cite: `Api/Api20100401Message.cs`, `Core/ParameterFlattener.cs`, `Core/Request/FormUrlEncodedRequest.cs`, `map/operations/Api20100401Message.md`.

**Already sent:** no typed “already sent” error. Defensive: catch `SdkException<RawError>`; `FetchMessage` and if `Status` is already `Sent`/`Delivered`/`Undelivered`/`Failed`/`Canceled`, stop — do not retry cancel. HTTP status that means “too late” is **UNVERIFIED**; extract best-effort from `ReadAsString()`, fall back to generic message.

**Do not `DeleteMessage`.** It “Deletes a Message resource from your account” (`void`) — delivery outcome would not survive.

#### ListMessage — reconciliation, provider-side From + date range

**HTTP:** `GET /2010-04-01/Accounts/{AccountSid}/Messages.json`  
**Signature:**

```
Task<TwilioSdk.Models.ListMessageResponse> ListMessage(
    string accountSid,
    string? to,
    string? from,
    DateTimeOffset? dateSent,
    DateTimeOffset? dateSentQuery,
    DateTimeOffset? dateSentQueryQuery,
    long? pageSize,
    int? page,
    string? pageToken,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

8 params `to` … `pageToken` nullable, **no default** → named arguments required.

| C# | Wire | Map from app |
|---|---|---|
| `accountSid` | path | `Twilio:AccountSid` |
| `from` | **`From`** (query, not form) | **`Twilio:FromNumber`** — provider-side. Source: `new Param("From", from)` on the GET query list. XML: “Filter by sender” / “sent **by**”. |
| `to` | `To` | `null` (reconciliation is by sender + dates). XML: “Filter by recipient” / “sent **to**”. |
| `dateSent` | `DateSent` | `null` (exact-day filter unused) |
| `dateSentQueryQuery` | **`DateSent>`** | app ISO-8601 **`from`** parsed to `DateTimeOffset` |
| `dateSentQuery` | **`DateSent<`** | app ISO-8601 **`to`** parsed to `DateTimeOffset` |
| `pageSize` | `PageSize` | XML: default 50, **max 1000**; type `long?` |
| `page` | `Page` | XML: “client state” |
| `pageToken` | `PageToken` | XML: “provided by the API” |

SDK serializes the three date params with `ToIso8601()` → `yyyy-MM-ddTHH:mm:ss.fff'Z'` (UTC). Inclusive vs exclusive on `DateSent>` / `DateSent<` is **UNVERIFIED** — pass the parsed offsets without shifting a day.

**`from` serialization:** query wire name is `From` (`Uri.EscapeDataString("From")=…`). `from: null` → Flatten returns `[]` → **no `From` query key** (filter dropped; list is account-wide, inbound included). `from: ""` → query `From=` (empty value) — the key is **still sent**, not dropped. Pass a non-empty E.164 `Twilio:FromNumber`; do not pass `null` or `""`. Cite: `map/operations/Api20100401Message.md`, `Api/Api20100401Message.cs`, `Core/QueryParameterFactory.cs`, `Core/ParameterFlattener.cs`.

**Inbound / `received`:** not expected when `From` is this app’s sending number. `MessageEnumStatus.Received` (`received`) is inbound; those resources have sender = the shopper, so they match `To` = the Twilio number, not `From` = `Twilio:FromNumber`. XML `from` = “sent by”. If `received` rows appear, the `From` query key was omitted (`from` was `null`) or the provider ignored it (**UNVERIFIED** vs XML). Do not post-filter a wider list as a substitute when `from` was actually sent.

**Do not swap `dateSentQuery` and `dateSentQueryQuery`.** That inverts the range.

**Return envelope:** `TwilioSdk.Models.ListMessageResponse` (`map/models/records-4-Li-Me.md`)

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` |
| `NextPageUri (next_page_uri)` | `string?` |
| `PreviousPageUri (previous_page_uri)` | `string?` |
| `FirstPageUri (first_page_uri)` | `string?` |
| `Page (page)` | `int?` |
| `PageSize (page_size)` | `int?` |
| `Start (start)` / `End (end)` | `int?` |
| `Uri (uri)` | `string?` |

**Pagination:** map row says **none** (no SDK auto-iterator; only `page`, no `perPage`). Drive the loop with `pageSize` + `pageToken` / `page`. Stop when `NextPageUri` is null or `Messages` is empty. **UNVERIFIED** that `PageToken` is a query argument on `NextPageUri` — defensive: if present, pass it as `pageToken` on the next call; cap the number of pages.

**Error:** Case B `SdkException<RawError>`.

---

### Response model — `TwilioSdk.Models.ApiV2010AccountMessage`

No payload wrapper. Cite: `map/models/records-1-Ac-Ca.md`.

Also: `NumSegments (num_segments): string?`, `Price (price): string?`, `PriceUnit (price_unit): string?`, `NumMedia (num_media): string?`, `ApiVersion (api_version): string?`, `Uri (uri): string?`, `SubresourceUris (subresource_uris): object?`.

### Enums in scope (`TwilioSdk.Models.Enums` — `StringEnum<T>`, not C# enums)

**`MessageEnumStatus`** (response `Status`; `map/models/enums.md`):

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

Scheduled vs sent: `Scheduled` vs `Sent` / `Delivered` / `Failed` / `Undelivered`. Cancelled follow-up: `Canceled`.

**`MessageEnumUpdateStatus`:** `Canceled (canceled)` only.  
**`MessageEnumDirection`:** `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.  
**`MessageEnumContentRetention`:** `Retain (retain)`, `Discard (discard)` — create-time privacy; **not** the post-send redact API.  
**`MessageEnumAddressRetention`:** `Retain (retain)`, `Obfuscate (obfuscate)`.  
**`MessageEnumRiskCheck`:** `Enable (enable)`, `Disable (disable)`.  
**`MessageEnumTrafficType`:** `Free (free)`.

Compare with `==` on the `StringEnum` record; read wire with `.Value`.

---

### Errors (every in-scope operation is Case B)

| Operation | Thrown type | Accessors |
|---|---|---|
| `FetchPhoneNumber3` | `SdkException<RawError>` | `ex.Error.StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| `CreateMessage` | same | same |
| `FetchMessage` | same | same |
| `UpdateMessage` | same | same |
| `ListMessage` | same | same |
| `DeleteMessage` (do not call) | same | same |

Namespaces: `TwilioSdk.Core.Exceptions.SdkException<T>` · `TwilioSdk.Core.ErrorResponse.RawError`. There is **no** `{Operation}Error` and **no** `TryGet…` on these ops.

`RawError.ReadAsJson<T>()` throws `System.Text.Json.JsonException` when the body is not JSON — prefer `ReadAsString()` unless the body is known JSON. **UNVERIFIED** JSON shape / numeric Twilio error codes for “invalid number” vs “already sent”. Defensive: best-effort parse; fall back to a generic message. Do **not** use `error_code` on the **message resource** as a programmatic classifier (XML forbids it).

Classification using **HTTP status on `RawError`** (plus Fetch `Status` after accept):

| Situation | Signal |
|---|---|
| Auth failure | `StatusCode` 401 / 403 on any op |
| Invalid number at **registration** | Lookup `Valid != true` / `ValidationErrors`, or Lookup `SdkException<RawError>` |
| Invalid destination at **Create** (rejected before accept) | `CreateMessage` throws `SdkException<RawError>` (4xx) — **UNVERIFIED** body |
| Undeliverable **after** accept (incl. US carrier refusal) | Create/Fetch 2xx with `Status` `Undelivered` / `Failed` |
| Cancel too late | Update throws `SdkException<RawError>` and/or Fetch `Status` already terminal |
| Transport / timeout | not `SdkException` — see trap notes |

**Order operations must not fail** because SMS failed: catch `SdkException<RawError>` (and the JsonException / transport catches required by `dotnet-error-handling`) on steps 3–5 and 7; log; continue the order. Contact registration (step 2) **should** fail the HTTP request when the number is not usable.

---

## Trap notes

⚠ Step 1 (client registration) — the `HttpClient` the ctor takes is not owned by the SDK; constructing one per call vs reusing factory/handler rotation changes DNS and socket lifetime. **MUST load `dotnet-client-initialization`** before wiring DI or `new TwilioSdkClient`.

⚠ Step 1 (DI lifetime) — `AddTwilioSdkClient`’s registered lifetime vs `IHttpClientFactory` handler rotation is not visible from the options type. **MUST load `dotnet-client-initialization`** before calling the extension.

⚠ Step 1 (auth) — credentials are a nullable options property that must be set before first call; 401/403 after a missed assignment is indistinguishable from a wrong secret at the catch site. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

⚠ Step 1 (BaseUrl) — `Environment` is captured at construct while `Server` is re-read per request; setting `Twilio:BaseUrl` on the wrong `{ServerName}.{Environment}` leaves Lookup or Messages on the default host. **MUST load `dotnet-configuration-resilience`** before assigning `options.Server`.

⚠ Step 1 (retries/timeouts) — `RetryOptions` / `Timeout` do **not** bound a whole call and are **not** `HttpClient.Timeout`; a failed `CreateMessage` POST can be executed more than once on the transport path. **MUST load `dotnet-configuration-resilience`** before accepting `RetryOptions.Default()`.

⚠ Steps 2–9 (calls) — list/create signatures have many leading nullables with **no** C# default; a positional call mis-binds `dateSentQuery` vs `dateSentQueryQuery` and `from` vs `to`. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ Steps 2–9 (models) — statuses, schedule type, and validation errors are `StringEnum<T>` records, not C# enums; treating them as `enum` or inventing `new` on unions will not compile. **MUST load `dotnet-models`** before mapping `MessageEnumStatus` / `ValidationError` / request enums.

⚠ Step 9 (list loop) — `ListMessage` has no SDK paginator; an unbounded `NextPageUri` loop has no built-in stop besides the provider. **MUST load `dotnet-configuration-resilience`** before writing the reconciliation loop.

⚠ Step 10 (error boundary) — every in-scope op is Case B (`SdkException<RawError>`); a Case A `TryGet…` ladder will not compile, and `ReadAsJson<T>()` is not a safe default read. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 10 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the test seam is the `HttpClient` argument, not a mock of controller types. **MUST load `dotnet-testing`** before stubbing Lookup/Messages.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, `HttpClient` lifetime, `AddTwilioSdkClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retries/timeouts; Step 9 pagination |
| `dotnet-calling-endpoints` | Steps 2–9 — named args, `ct:`, nullable-without-default params |
| `dotnet-models` | StringEnum statuses/errors, Lookup/Message records, wire names |
| `dotnet-error-handling` | Step 10 — Case B `RawError`, JsonException 2xx vs non-2xx, order-path swallow vs contact-path reject |
| `dotnet-testing` | Stub `HttpClient` for Lookup/Create/Fetch/Update/List |

---

## Assumptions & Blockers

**Assumptions**

- Immediate send uses `from` = `Twilio:FromNumber`; `Twilio:MessagingServiceSid` is required for **schedule** (enum XML), not for send/list/fetch/update/redact.
- `Twilio:BaseUrl` is applied only to `options.Server.Default.Production.BaseUrl`; Lookup stays on `https://lookups.twilio.com` unless separately changed (it must not be).
- Shopper input may be national format; then `countryCode` is passed. Canonical stored value is always `LookupResponse.PhoneNumber`.
- Follow-up delay (“a few days”) is chosen by the app as `sendAt`; the SDK does not encode a window.
- Cancel uses `UpdateMessage` with `status: Canceled` and `body: null`. Redact: `body: ""` **does** send form `Body=`; `body: null` omits `Body`. XML names no other redact token. Live Fetch still showing original `Body` after `""` is a provider non-redact, not an SDK omit — no alternative C# value exists in the map/source.
- App idempotency for resend is eShopOnWeb’s; the SDK’s `Idempotency-Key` is a fresh `Guid` per call and is not overridable.
- US undelivered/failed after 2xx create is an expected Fetch outcome, not a missing API.

**Blockers / gaps (no invented workaround)**

- **Scheduling window** (min/max lead time for `sendAt`) is absent from CreateMessage XML and `MessageEnumScheduleType` XML. **UNVERIFIED.** Provider rejection via `SdkException<RawError>` is the only encoded signal.
- **SMS-capable line type** is not a typed field: `LineTypeIntelligenceInfo.Type` is `string?`, not `LineType`. The SDK’s encoded “usable destination” check is `Valid` + `ValidationErrors`. Treating `Type` as SMS-capability is **UNVERIFIED**.
- **Redact:** SDK sends `Body=` for `body: ""` and omits `Body` only for `null`. UpdateMessage XML does not name a redacting token other than sending `Body`. Live 2xx + original `Body` on Fetch means the provider did not redact; the SDK has **no other documented Body value**. Do not invent a substitute payload. **`DeleteMessage` is still out of scope** (deletes the resource).
- **Cancel-already-sent** has no typed error payload (Case B only). **UNVERIFIED** HTTP status; Fetch `Status` after failure.
- **Create/Lookup error JSON** (invalid number vs auth vs quota) has no generated model. **UNVERIFIED** body shape; `ReadAsString()` + generic fallback.
- **`ListMessage` DateSent> / DateSent< inclusivity** and **`pageToken` extraction from `NextPageUri`** are **UNVERIFIED**. Map: no SDK paginator.
- **`RequestOptions` cannot set `Idempotency-Key`.** Native header exists but is not an integration knob.
- Lookups v1 cannot satisfy “provider canonical + not a usable destination” (`Valid` missing) — v2 only.
)