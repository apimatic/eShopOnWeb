# eShopOnWeb — Twilio order-lifecycle SMS (contract sheet)

NuGet: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk` (not `Twilio`). Client: `TwilioSdkClient`. Map provenance: `sdk-map.md` stamp `51fdf48`.

## Scope & sequence

1. **Config + client** — bind `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, optional `Twilio:BaseUrl`. Construct `TwilioSdkClient` with AccountSid+AuthToken; if `Twilio:BaseUrl` is set, override **only** the messaging (Default) host.
2. **Flow 1 — contact numbers** — `LookupsV2PhoneNumber.FetchPhoneNumber3` to validate + canonicalize before persist. GET/DELETE are local (no Twilio call).
3. **Flow 2 — order SMS** — `Api20100401Message.CreateMessage` on place / dispatch / cancel (never fail the order on SMS error). Dispatch also `CreateMessage` with `scheduleType`+`sendAt` (provider-queued follow-up). Cancel also `UpdateMessage` with `status: Canceled` for any not-yet-sent follow-up SID. Persist provider `Sid` + `Status` on the notification record.
4. **Flow 2 — outcomes (no webhooks)** — `FetchMessage` by SID (and `ListMessage` where listing is needed) for current delivery outcome.
5. **Flow 3 — resend** — application-level idempotency, then `CreateMessage`. SDK does **not** accept a caller idempotency key.
6. **Flow 3 — content disposal** — `UpdateMessage` with a new `body` (redact at provider). Do **not** `DeleteMessage` (that removes the resource).
7. **Flow 3 — reconciliation** — `ListMessage` with `from: Twilio:FromNumber` and `DateSent>` / `DateSent<` for the ISO-8601 window; page until exhausted. Compare to local notification SIDs.

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
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` — `httpClient` is required | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` | `sdk-map.md` · `ServiceCollectionExtensions.cs` |
| Credentials property | `TwilioSdkClientOptions.AccountSidAuthToken` : `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` Servers & auth |
| Credentials shape | `BasicAuthCredentials` (`TwilioSdk.Core.Authentication.Basic`): `required string Username`, `required string Password` | `BasicAuthCredentials.cs` |
| Bind | `Username` = `Twilio:AccountSid`, `Password` = `Twilio:AuthToken`. XML docs also allow API key + secret as username/password; this integration uses Account SID + Auth Token. | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment | `options.Environment` : `TwilioSdk.Servers.ServerEnvironment` — member `Production` (wire `production`). `ServerEnvironment.Default()` → `Production`. | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Server overrides | `options.Server` : `TwilioSdk.ServerOptions` (root namespace) | `sdk-map.md` · `ServerOptions.cs` |
| Messaging host | Message ops use `_server.Default(...)`. Override: `options.Server.Default.Production.BaseUrl` (`TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl`, default `https://api.twilio.com`). When `Twilio:BaseUrl` is set, assign it **verbatim** here. Do **not** set any other `ServerOptions` node. | `Api/Api20100401Message.cs` · `Servers/DefaultOptions.cs` |
| Lookup host (must stay independent) | Lookup ops use `_server.Default4(...)`. Default `https://lookups.twilio.com` via `options.Server.Default4.Production.BaseUrl`. `Twilio:BaseUrl` must **not** change this. | `Api/LookupsV2PhoneNumber.cs` · `Servers/Default4Options.cs` |
| Other `TwilioSdkClientOptions` | `Retry` : `TwilioSdk.Core.Configuration.RetryOptions` (all members `required`, or `RetryOptions.Default()`); `Logging` : `LoggingOptions` | `sdk-map.md` |
| Per-call options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel`. No header bag. | `Core/RequestOptions.cs` |
| No-throw variants | Absent on every operation in this SDK | `sdk-map.md` |

`accountSid` path argument on every Message op = `Twilio:AccountSid`.

Never log `AccountSidAuthToken`, `Password`, or `BasicAuthCredentials.Encode()` output.

---

### 1. Phone-number lookup / validation (Flow 1)

**Controller:** `client.LookupsV1PhoneNumberApi` exists (`FetchPhoneNumber2` → `TwilioSdk.Models.LookupsV1PhoneNumber` with `Carrier (carrier): object?`) — **do not use** for this flow; SMS capability is untyped.

**Use:** `client.LookupsV2PhoneNumber.FetchPhoneNumber3` · HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) · Case B · `operations/LookupsV2PhoneNumber.md`

**Signature** (nullable params have no C# default — pass `null` to skip):

```
Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(
    string phoneNumber,
    string? fields,
    string? countryCode,
    string? firstName, string? lastName,
    string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode,
    string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId,
    TwilioSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

| Arg | This integration |
|---|---|
| `phoneNumber` | Caller-typed number (E.164 or national; default country +1) |
| `fields` | `"line_type_intelligence,line_status"` (comma-separated). XML allowed values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. `Valid` / `ValidationErrors` are on the response without a `validation` field request. |
| `countryCode` | ISO 3166-1 alpha-2 if the input is national format; else `null` |
| remaining identity/risk args | `null` |
| `requestOptions` | `null` |

**Response** `TwilioSdk.Models.LookupResponse` — **no extra envelope** (`records-4-Li-Me.md` · `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical E.164** (`+` + country code + subscriber). **This is what to store.** |
| `NationalFormat (national_format)` | `string?` | Display only |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix |
| `CountryCode (country_code)` | `string?` | ISO country |
| `Valid (valid)` | `bool?` | True iff the number is in a range a carrier can freely assign to a user |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status` |

`LineTypeIntelligenceInfo` (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, **`Type (type): string?`** (untyped — not `LineType`), `ErrorCode (error_code): int?`.

`LineStatusInfo`: `Status (status): string?` (untyped), `ErrorCode (error_code): int?`.

There is **no** `sms: bool` on `LookupResponse` (the `Capabilities.Sms` models apply to **owned** numbers, not Lookup).

**`ValidationError`** (`TwilioSdk.Models.Enums`, StringEnum) · `enums.md`:

| Member | Wire |
|---|---|
| `TooShort` | `TOO_SHORT` |
| `TooLong` | `TOO_LONG` |
| `InvalidButPossible` | `INVALID_BUT_POSSIBLE` |
| `InvalidCountryCode` | `INVALID_COUNTRY_CODE` |
| `InvalidLength` | `INVALID_LENGTH` |
| `NotANumber` | `NOT_A_NUMBER` |

**Closest typed line-type vocabulary** (used by number-override APIs, **not** bound to `LineTypeIntelligenceInfo.Type`) — `TwilioSdk.Models.Enums.LineType` · `enums.md`: `Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)`.

**Reject as “not a usable SMS destination” (do not persist):**

1. `SdkException<TwilioSdk.Core.ErrorResponse.RawError>` from this call — read `ex.Error.StatusCode` + `ReadAsString()`; treat as unusable/invalid.
2. `Valid` is not `true`.
3. `ValidationErrors` is non-null and non-empty.
4. `LineTypeIntelligence.Type` is present and, compared case-sensitively to LineType **wire** values, is `landline`, `pager`, `voicemail`, `uan`, `sharedCost`, or `premium`.
5. `LineTypeIntelligence` is null, or `Type` is null/empty after requesting `line_type_intelligence` — cannot confirm SMS capability; reject (Flow 1 requires rejection at register-time, not at send-time).
6. Accept `Type` wire `mobile` (and `nonFixedVoip` / `personal` / `fixedVoip` / `tollFree` only if product later widens this; default accept **`mobile` only**).

`LineStatus.Status` values are **not** enumerated on the model. If `LineStatus.ErrorCode` is set, the line-status package failed — do not treat that alone as “usable”; still apply rules 1–6. Live `Status` strings are **UNVERIFIED**.

**Error:** Case B `SdkException<RawError>` — accessors `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. No typed `{Op}Error`. Pagination: none.

---

### 2. Send SMS / schedule follow-up (Flow 2)

**Controller:** `client.Api20100401Message` · HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) · Case B · `operations/Api20100401Message.md`

**Signature** (24 nullable params `statusCallback`…`contentSid` have **no default** — pass `null` to skip). Body is **form-urlencoded**, not JSON:

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
    TwilioSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Wire names: `To`, `StatusCallback`, `ApplicationSid`, `MaxPrice`, `ProvideFeedback`, `Attempt`, `ValidityPeriod`, `ForceDelivery`, `ContentRetention`, `AddressRetention`, `SmartEncoded`, `PersistentAction`, `TrafficType`, `ShortenUrls`, `ScheduleType`, `SendAt`, `SendAsMms`, `ContentVariables`, `RiskCheck`, `From`, `FallbackFrom`, `MessagingServiceSid`, `Body`, `MediaUrl`, `ContentSid`.

**This integration — every send:**

| Param | Immediate (placed / dispatched / cancelled / resend) | Provider-queued follow-up (dispatch + N days) |
|---|---|---|
| `accountSid` | `Twilio:AccountSid` | same |
| `to` | stored canonical E.164 | same |
| `body` | SMS text | follow-up text |
| `from` | `Twilio:FromNumber` | `Twilio:FromNumber` (so list-by-From reconciliation still matches) |
| `messagingServiceSid` | `Twilio:MessagingServiceSid` when configured, else `null` | **required** — `Twilio:MessagingServiceSid` |
| `scheduleType` | `null` | `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) |
| `sendAt` | `null` | `DateTimeOffset` a few days later (application: **72 hours**) |
| `statusCallback` | **`null`** (no public URL; do not set webhooks) | `null` |
| all other optionals | `null` | `null` |

**From vs Messaging Service SID when both are configured:** pass **both** `from` and `messagingServiceSid` on every `CreateMessage`. Scheduling is **Messaging Services only** (`MessageEnumScheduleType` XML: value `fixed` in conjunction with send-time). Immediate sends still pass `from` so `ListMessage(from:)` can ask the provider for this app’s sending number. Never substitute `fallbackFrom` for `from`.

**Do not** use `statusCallback` / `applicationSid`.

**Response** `TwilioSdk.Models.ApiV2010AccountMessage` — **no extra envelope** (`records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`). Persist at least:

| C# (wire) | Type | Use |
|---|---|---|
| `Sid (sid)` | `string?` (SM\|MM + 32 hex) | **Provider identifier** on the notification record |
| `Status (status)` | `MessageEnumStatus?` | Initial delivery outcome |
| `To (to)` / `From (from)` | `string?` | E.164 endpoints |
| `Body (body)` | `string?` | Content (until redacted) |
| `DateCreated (date_created)` / `DateSent (date_sent)` / `DateUpdated (date_updated)` | `string?` (RFC 2822 GMT) | Audit |
| `ErrorCode (error_code)` / `ErrorMessage (error_message)` | `int?` / `string?` | Set when status is `failed` or `undelivered` (XML: do not branch programmatically on specific codes; they may change) |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | Echo of MG… SID |

A send failure **must not** fail the order/dispatch/cancel HTTP action — catch Case B / transport / `JsonException` at the notification boundary.

**Error:** Case B `SdkException<RawError>`. Pagination: none.

**Internal header (not a parameter):** the generated method always sends `Idempotency-Key: Guid.NewGuid()`. The caller cannot supply a key (see §8).

---

### 3. Fetch message by SID (outcomes, GET notifications)

```
Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(
    string accountSid, string sid,
    TwilioSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Case B · same response record as create.

Refresh the notification row from `Status`, `ErrorCode`, `ErrorMessage`, `Body`, `From`, `To`, `DateSent`, `DateUpdated`. No webhook path exists in this product.

**Not-found:** Case B — inspect `ex.Error.StatusCode` (typically 404; confirm via `StatusCode`, not string-matching `.ToString()`).

---

### 4. List messages for reconciliation (Flow 3)

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
    TwilioSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · Case B · `operations/Api20100401Message.md`

| C# param | Wire | This call |
|---|---|---|
| `to` | `To` | `null` (do not filter by shopper) |
| `from` | `From` | **`Twilio:FromNumber`** — provider-side filter: “messages sent by this number”. XML: e.g. `+15552229999` retrieves resources **sent by** that number. **Do not** list-all then filter. |
| `dateSent` | `DateSent` | `null` (exact-match; not a range) |
| `dateSentQuery` | `DateSent<` | reconciliation `to` (ISO-8601 → `DateTimeOffset`) |
| `dateSentQueryQuery` | `DateSent>` | reconciliation `from` |
| `pageSize` | `PageSize` | up to `1000` (XML default 50, max 1000) |
| `page` | `Page` | `null` unless driving client state |
| `pageToken` | `PageToken` | `null` on first page; then the token the API provided |

SDK serializes the three date args with `ToIso8601()` = `yyyy-MM-ddTHH:mm:ss.fff'Z'` (UTC). XML comments mention `YYYY-MM-DD`; the generated client still sends that ISO-8601 form.

**Response envelope** `TwilioSdk.Models.ListMessageResponse` (`records-4-Li-Me.md`):

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` — **inner list to reconcile** |
| `NextPageUri (next_page_uri)` | `string?` |
| `PreviousPageUri (previous_page_uri)` / `FirstPageUri (first_page_uri)` / `Uri (uri)` | `string?` |
| `Page (page)` / `PageSize (page_size)` / `Start (start)` / `End (end)` | `int?` |

Map pagination note: **no auto-paginator** (`page` only, no `perPage`). Loop while `NextPageUri` is present, passing `pageToken` until `Messages` is empty and `NextPageUri` is null so the **whole** `[from,to]` window is covered. Whether `DateSent>` / `DateSent<` are exclusive at the provider is **UNVERIFIED** — if a boundary SID is missing locally vs provider, widen the window only as a last resort after confirming via `FetchMessage`.

Count **only** this `from` result set (already provider-filtered). Compare SIDs to local notification records.

---

### 5. Cancel a scheduled / queued follow-up (Flow 2 cancel)

```
Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(
    string accountSid,
    string sid,
    string? body,
    TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status,
    TwilioSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

HTTP `POST …/Messages/{Sid}.json` · form `Body`, `Status` · Case B.

XML: **“Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)”**.

**Cancel:** `body: null` (must still pass), `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`). Only update-status member.

**Which statuses can be cancelled:** source/map say **not-yet-sent**. `MessageEnumStatus` values that are still pre-send: `accepted`, `scheduled`, `queued` (and possibly `sending` — treat cancel of `sending`/`sent`/`delivered`/… as already-sent). If the provider rejects (already sent), Case B — **do not fail** the order-cancel operation; record the error outcome.

Returns updated `ApiV2010AccountMessage` (`Status` should become `Canceled` on success).

---

### 6. Redact / dispose content at the provider (Flow 3)

Same `UpdateMessage`. **Do not** call `DeleteMessage` (`DELETE …/Messages/{Sid}.json` — XML: “Deletes a Message resource from your account”; the fact of the send would not survive at the provider).

**Redact:** `status: null`, `body:` empty string `""`. Operation exists specifically to redact `body` while the resource remains. Exact persisted `Body` after redact (empty vs whitespace) is **UNVERIFIED** — after the call, `FetchMessage` and store whatever `Body` remains; keep `Sid` + `Status` + error fields.

Remaining fields on `ApiV2010AccountMessage` (Sid, Status, From, To, dates, ErrorCode, ErrorMessage, MessagingServiceSid, …) are independent properties and are not removed by a body update.

---

### 7. Idempotency for resend (Flow 3)

`CreateMessage` has **no** `idempotencyKey` parameter (unlike Payments / UserDefinedMessage ops).

`RequestOptions` cannot set headers.

The generated `CreateMessage` **always** attaches `HeaderParam("Idempotency-Key", Guid.NewGuid())` — a **new** key every invocation (`Api/Api20100401Message.cs`). A caller-supplied key **cannot** be sent.

**Implement application-level idempotency** (store the operator key → existing notification / provider SID; same key must not call `CreateMessage` again). The SDK header does not provide this.

---

### 8. Enums used

**`TwilioSdk.Models.Enums.MessageEnumStatus`** (StringEnum) · `enums.md` · `Models/Enums/MessageEnumStatus.cs`:

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
| `Read` | `read` (WhatsApp only) |
| `PartiallyDelivered` | `partially_delivered` |
| `Canceled` | `canceled` |

Build with static members or `MessageEnumStatus.FromValue("queued")`. Compare as `StringEnum<T>`, not a C# `enum`.

**`MessageEnumScheduleType`:** `Fixed (fixed)` only.

**`MessageEnumUpdateStatus`:** `Canceled (canceled)` only.

**`MessageEnumDirection`** (on the message record): `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`. Outbound API sends are `outbound-api`.

Enums are `StringEnum<T>` in `TwilioSdk.Models.Enums` — `using TwilioSdk.Models` does **not** import them.

---

### 9. Errors (all in-scope ops are Case B)

Every operation above throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` on non-success HTTP (`sdk-map.md` error model · each `operations/*.md` row).

```
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;          // HttpStatusCode
    var body   = ex.Error.ReadAsString();      // or ReadAsJson<T>() / ReadAsBytes()
}
```

There are **no** `TryGet…` accessors on these ops (not Case A). `TryGetRawError` exists only on typed `ApiError` subclasses — **not** on `RawError`.

| Situation | How to detect (from this SDK) |
|---|---|
| Invalid / unusable number at **lookup** | Lookup Case B **or** `Valid != true` / `ValidationErrors` / line-type rules in §1 |
| Invalid destination at **send** | `CreateMessage` Case B; or 2xx with later `Status` `failed`/`undelivered` + `ErrorCode`/`ErrorMessage` |
| Message not found | `FetchMessage` / `UpdateMessage` / `DeleteMessage` Case B — use `StatusCode` |
| Cannot cancel (already sent) | `UpdateMessage` Case B — use `StatusCode` + body; do not fail the order op |
| Auth failure | Case B `StatusCode` 401/403 — check `AccountSidAuthToken` binding, not a host mix-up (`Twilio:BaseUrl` on Default only) |
| Transport | `HttpRequestException` / timeout — **not** `SdkException` |

Auth token must never be logged (including exception messages you format from options).

`error_code` / `error_message` on `ApiV2010AccountMessage` are for display; XML says values for a given cause may change — map delivery **outcome** from `Status`.

---

### 10. Messaging Service SID vs FromNumber (summary)

| Action | `from` (`Twilio:FromNumber`) | `messagingServiceSid` (`Twilio:MessagingServiceSid`) |
|---|---|---|
| Immediate SMS | **pass** | pass when configured |
| Scheduled follow-up | **pass** (reconciliation) | **required** (`ScheduleType.Fixed` is Messaging Services only) |
| `ListMessage` reconciliation | **query param `from`** — provider filter | do not use as the list filter |

---

## Trap notes

⚠ Step 1 (client registration) — `TwilioSdkClient` takes an `HttpClient` whose ownership and lifetime the constructor does not document; registering a per-request client vs a long-lived pipeline has connection-pool and DNS costs. **MUST load `dotnet-client-initialization`** before `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a nullable credentials object that must be populated from configuration (not literals); a 401/403 on any op is an auth-wiring failure, and credential values can leak through logs. **MUST load `dotnet-authentication`** before setting `Username`/`Password`.

⚠ Step 1 (BaseUrl / retries / list paging) — `Twilio:BaseUrl` maps only to `Server.Default`; `Retry`/`Timeout` on options are not the `HttpClient` timeout and do not bound “the whole call”; `CreateMessage` is POST; `ListMessage` has no auto-paginator (`NextPageUri` / `pageToken`). Wrong host, duplicate SMS, or a truncated reconciliation window are the costs. **MUST load `dotnet-configuration-resilience`** before wiring options or the list loop.

⚠ Steps 2–7 (calls) — up to 24 must-pass-explicitly nullables; mis-ordered positional args bind the wrong optional. Named arguments (`ct:` not `cancellationToken:`) are required for safety. **MUST load `dotnet-calling-endpoints`** before the first `client.Api20100401Message` / `LookupsV2PhoneNumber` call.

⚠ Steps 2–7 (models) — statuses/schedule/validation are `StringEnum<T>` not C# enums; `LookupResponse` / `ApiV2010AccountMessage` are the payload (list is wrapped in `Messages`); unmodeled JSON is dropped. **MUST load `dotnet-models`** before constructing enums or reading records.

⚠ Steps 2–7 (error boundary) — every in-scope op is Case B (`SdkException<RawError>` only). A status-only catch misses transport failures. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ A **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. (These ops are Case B / `RawError`, but the same construction-time `JsonException` replacement still applies.) **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (tests) — the `HttpClient` constructor argument is the test seam; live sandbox numbers cost real money. **MUST load `dotnet-testing`** before stubbing or writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `HttpClient` + `TwilioSdkClient` / `AddTwilioSdkClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 + reconciliation — `Server.Default` BaseUrl, retries/timeouts, `ListMessage` paging |
| `dotnet-calling-endpoints` | Steps 2–7 — named args, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 2–7 — `StringEnum<T>`, record fields, list envelope `Messages` |
| `dotnet-error-handling` | Steps 2–7 — Case B `RawError`, order-op isolation, **both** `JsonException` directions above |
| `dotnet-testing` | Tests / sandbox verification |

---

## Assumptions & Blockers

**Assumptions**

- Lookup API is **Lookups v2** `FetchPhoneNumber3` (v1 `Carrier` is `object?` and cannot type SMS capability).
- Usable SMS destination = `Valid == true`, no `ValidationErrors`, and `LineTypeIntelligence.Type` wire value `mobile`. Other `Type` strings and missing Type → reject at registration.
- Follow-up delay is **72 hours** from dispatch (`sendAt`).
- When both `Twilio:FromNumber` and `Twilio:MessagingServiceSid` are set, every `CreateMessage` passes both; scheduled sends additionally pass `scheduleType: Fixed` and `sendAt`.
- `statusCallback` is always `null`.
- Redact uses `UpdateMessage(body: "", status: null)`. Persisted body text after redact is **UNVERIFIED**.
- `DateSent>` / `DateSent<` inequality inclusivity is **UNVERIFIED**; first implementation sends the caller’s `from`/`to` as those two params.
- `LineStatus.Status` live values are **UNVERIFIED**; they do not override the Valid/Type reject rules.
- Application-level idempotency for resend (SDK key is not caller-controlled).
- Sandbox: register/message only `TWILIO_TEST_TO_NUMBER` and `TWILIO_UNREACHABLE_TO_NUMBER`. Unreachable/undeliverable is an outcome (`failed`/`undelivered`), not an integration gap. US registration-status undeliverable is likewise an outcome.
- SMS failure never fails place/dispatch/cancel; notification row still stored when a SID was returned.

**Blockers**

- None. Every required capability is on the map: lookup (v2), create/fetch/list/update message (cancel + redact), list-by-`From` + date range. Caller-supplied idempotency on `CreateMessage` is **absent** (not a blocker — application-level, documented in §7).
