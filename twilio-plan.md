# Twilio .NET SDK — eShopOnWeb SMS notifications — plan + contract sheet

Package `AsadAli.TwilioSdk` (install version-less). Root namespace **`TwilioSdk`** (map identity; not `Twilio`). Client `TwilioSdkClient`. No `…Result` variants exist — every operation is throw-only. Provenance: `sdk-map.md` commit `51fdf48`.

---

## Scope & sequence

1. **Client, auth, messaging BaseUrl** — construct `TwilioSdkClient` from `Twilio:AccountSid` / `Twilio:AuthToken` / optional `Twilio:BaseUrl` (messaging host only). Ops: none.
2. **Lookup / validate on `POST /api/contact-numbers`** — `LookupsV2PhoneNumber.FetchPhoneNumber3`. Store `LookupResponse.PhoneNumber` (E.164). Reject non-usable destinations before persist.
3. **Send SMS on order placed / dispatched / cancelled** — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`. Persist `Sid` + `Status`.
4. **Queue follow-up on dispatch (provider-side, not an app timer)** — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` = `Twilio:MessagingServiceSid`. Persist scheduled `Sid` + `Status`.
5. **Cancel unsent follow-up on order cancel** — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
6. **Read delivery outcome (no webhooks)** — `Api20100401Message.FetchMessage` by persisted `Sid`; read `Status` / `ErrorCode` / `ErrorMessage`.
7. **Operator resend `POST /api/notifications/{id}/resend`** — `CreateMessage` again. SDK does **not** accept a caller idempotency key (see CONTRACT SHEET + Assumptions). Enforce “same key must not send twice” in the app.
8. **Redact provider body `DELETE /api/notifications/{id}/content`** — `Api20100401Message.UpdateMessage` with `body` (redact). **Do not** call `DeleteMessage` (that deletes the resource).
9. **Reconciliation `GET /api/notifications/reconciliation?from=&to=`** — `Api20100401Message.ListMessage` constrained at the provider with `from` = `Twilio:FromNumber` and the DateSent inequality params. Paginate manually.
10. **Error boundary** — every SDK call above is Case B `SdkException<RawError>`. Order mutations must not fail because a message send failed.

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

Null form/query values are omitted on the wire (a `null` argument is not sent as an empty field). Every nullable Create/List/Update argument still **must be passed explicitly** (`null` to skip).

### Client construction / auth / messaging BaseUrl

| Fact | Value | Source |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers a singleton client via `IHttpClientFactory.CreateClient()` (unnamed) | `ServiceCollectionExtensions.cs` |
| Options type | `TwilioSdk.TwilioSdkClientOptions` — `Environment` (`TwilioSdk.Servers.ServerEnvironment`), `Retry` (`TwilioSdk.Core.Configuration.RetryOptions`), `Logging` (`TwilioSdk.Core.Configuration.LoggingOptions`), `Server` (`TwilioSdk.ServerOptions`), `AccountSidAuthToken` (`TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). `Default()` → Production | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Auth property | `options.AccountSidAuthToken` | `sdk-map.md` *Servers & auth* |
| Credentials shape | `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials` — `required string Username`, `required string Password` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Config mapping | `Username` ← `Twilio:AccountSid`; `Password` ← `Twilio:AuthToken`. Never hard-code. Map XML also allows API-key SID + secret in the same two fields | `sdk-map.md` *Servers & auth* |
| Null credentials | `BasicAuthScheme.Create(null)` installs `NoneAuthScheme` — calls go out unauthenticated | `AuthSchemes.cs` |
| Messaging host (Default / api) | Create/Fetch/List/Update/Delete Message all call `_server.Default(...)`. Override: `options.Server.Default.Production.BaseUrl` (type `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl: string`). Default `"https://api.twilio.com"`. When `Twilio:BaseUrl` is set, assign it **verbatim** here. Do **not** set this from lookup | `Api/Api20100401Message.cs` · `Server.cs` · `Servers/DefaultOptions.cs` |
| Lookup host (Default4 / lookups) | `FetchPhoneNumber3` calls `_server.Default4(...)`. `options.Server.Default4.Production.BaseUrl` default `"https://lookups.twilio.com"`. **Leave this alone** when applying `Twilio:BaseUrl` | `Api/LookupsV2PhoneNumber.cs` · `Servers/Default4Options.cs` |
| `Twilio:FromNumber` / `Twilio:MessagingServiceSid` | Application config, not SDK options. Passed as `CreateMessage`/`ListMessage` arguments | — |
| Per-call options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel { get; init; }`. No header bag. Cannot inject a caller `Idempotency-Key` | `Core/RequestOptions.cs` |

`RetryOptions` members (all `required`; start from `RetryOptions.Default()` or `Disabled()`): `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` — `TwilioSdk.Core.Configuration.RetryOptions` (`sdk-map.md`).

---

### 1. Phone number lookup / validation

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Operation | `FetchPhoneNumber3` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` on **Default4 (lookups)** |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 nullable params `fields` … `partnerSubId` |
| Returns | `TwilioSdk.Models.LookupResponse` (the payload **is** the record — no wrapper field) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `ex.Error.StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsBytes()` · `ReadAsJson<T>()` |
| Pagination | none |
| Map | `operations/LookupsV2PhoneNumber.md` · `records-4-Li-Me.md` (`LookupResponse`) |

**`fields` (wire `Fields`)**: comma-separated. Documented values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. For this integration pass at least `validation,line_type_intelligence` (optionally add `line_status`). Source: `Api/LookupsV2PhoneNumber.cs` XML.

**`LookupResponse` fields this integration reads** (`TwilioSdk.Models`, all optional/`?`; `records-4-Li-Me.md` · `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical form to persist** — E.164 (`+` + country code + subscriber) |
| `NationalFormat (national_format)` | `string?` | National display; do not store as the canonical key |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2 |
| `Valid (valid)` | `bool?` | “in a valid range that can be freely assigned by a carrier to a user” — **not** by itself “usable mobile SMS destination” |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status` |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?`, `ErrorCode (error_code): int?`. **`Type` is an untyped string — the SDK map and source do not enumerate line-type wire values** (no “mobile”/“landline” enum). **`LineStatusInfo`**: `Status (status): string?`, `ErrorCode (error_code): int?` — also untyped.

**`ValidationError`** (`TwilioSdk.Models.Enums.ValidationError`, `StringEnum`, `enums.md`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

**Reject now (not a usable destination) when any of:**
- the call throws `SdkException<RawError>` with a 4xx `StatusCode` (invalid / not found / not permitted) — exact 4xx set is **UNVERIFIED**; treat 401/403 as auth, other 4xx as “not usable”, 5xx/transport as lookup failure (do not persist);
- `Valid` is not `true`;
- `ValidationErrors` is non-empty;
- `LineTypeIntelligence.ErrorCode` is non-null (package error, not a usable typed line).

**`Type` allow-list is a GAP** — do not invent values from memory. Compare `Type` only as an opaque provider string; if the product later names allowed types, they are not SDK-contract constants.

Do **not** use `LookupsV1PhoneNumberApi.FetchPhoneNumber2` — v1 returns `LookupsV1PhoneNumber` with `Carrier: object?` and no `Valid` / `ValidationErrors` / `LineTypeIntelligenceInfo`.

---

### 2–3. Create message (immediate send + provider-scheduled follow-up)

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Operation | `CreateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)** — form-urlencoded |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required (non-nullable) | `accountSid`, `to` |
| Must-pass-explicitly | 24 nullable params `statusCallback` … `contentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (no wrapper) |
| Error | **Case B** `SdkException<RawError>` — accessors as above |
| Pagination | none |
| Map | `operations/Api20100401Message.md` |

**Wire names (form):** `To`←`to`, `StatusCallback`←`statusCallback`, `ApplicationSid`←`applicationSid`, `MaxPrice`←`maxPrice`, `ProvideFeedback`←`provideFeedback`, `Attempt`←`attempt`, `ValidityPeriod`←`validityPeriod`, `ForceDelivery`←`forceDelivery`, `ContentRetention`←`contentRetention`, `AddressRetention`←`addressRetention`, `SmartEncoded`←`smartEncoded`, `PersistentAction`←`persistentAction`, `TrafficType`←`trafficType`, `ShortenUrls`←`shortenUrls`, `ScheduleType`←`scheduleType`, `SendAt`←`sendAt`, `SendAsMms`←`sendAsMms`, `ContentVariables`←`contentVariables`, `RiskCheck`←`riskCheck`, `From`←`from`, `FallbackFrom`←`fallbackFrom`, `MessagingServiceSid`←`messagingServiceSid`, `Body`←`body`, `MediaUrl`←`mediaUrl`, `ContentSid`←`contentSid`. Path `{AccountSid}` ← `accountSid` (`Twilio:AccountSid`).

**Immediate SMS (placed / dispatched / cancelled):** `to` = stored canonical E.164, `from` = `Twilio:FromNumber`, `body` = text, `messagingServiceSid: null`, `scheduleType: null`, `sendAt: null`, all other optionals `null`. `statusCallback` stays `null` (no public webhook URL).

**Scheduled follow-up (dispatch, a few days later, provider-side):** same `to` / `body` / `from` = `Twilio:FromNumber`, plus `messagingServiceSid` = `Twilio:MessagingServiceSid`, `scheduleType` = `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`), `sendAt` = target `DateTimeOffset`. Enum XML: **“For Messaging Services only”** — scheduling without `messagingServiceSid` is outside the documented contract. `SendAt` is serialized as ISO-8601 UTC `yyyy-MM-ddTHH:mm:ss.fffZ` (`DateTimeOffsetExtensions.ToIso8601`). **Allowed timing window is not in the map or source (UNVERIFIED).** Do not invent a 5-minute/7-day client check; if the provider rejects `sendAt`, that surfaces as `SdkException<RawError>` — record it, do not fail the order.

**From vs Messaging Service:** Immediate send is pinned to `Twilio:FromNumber` via `from`. Scheduled send also passes `from` so reconciliation can still query that number, **and** must pass `messagingServiceSid` because `MessageEnumScheduleType` is Messaging-Service-only. `Twilio:MessagingServiceSid` is not used on immediate sends.

**Idempotency (capability 6):** `CreateMessage` has **no** `idempotencyKey` parameter (unlike `CreatePayments` / `CreateUserDefinedMessage` on other controllers). The generated body always sends header `Idempotency-Key: Guid.NewGuid()` — a **new** value on every invocation. `RequestOptions` cannot override headers. **A caller-supplied resend key cannot be forwarded to Twilio.** Repeating `CreateMessage` always looks like a new provider write. Enforce “same HTTP idempotency key ⇒ do not call CreateMessage again” in application storage. Source: `Api/Api20100401Message.cs` (header line on `CreateMessage`).

**Response `ApiV2010AccountMessage`** (`records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`) — persist at least:

| C# (wire) | Type | Role |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message id (`SM…` / `MM…`) |
| `Status (status)` | `MessageEnumStatus?` | Current delivery / schedule state |
| `From (from)` / `To (to)` | `string?` | Sender / destination |
| `Body (body)` | `string?` | Text (later redactable) |
| `DateCreated (date_created)` / `DateSent (date_sent)` / `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT timestamps (strings, not `DateTimeOffset`) |
| `ErrorCode (error_code)` | `int?` | Set when status is `failed` or `undelivered`; otherwise null |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code`; XML: do not consume these two fields as a stable programmatic contract |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | Service used, if any |
| `NumSegments (num_segments)` | `string?` | XML: Messaging Service messages may show `0` until a sender is assigned |
| `Direction (direction)` | `MessageEnumDirection?` | Outbound API → `OutboundApi (outbound-api)` |

A 2xx create with later `undelivered` (e.g. US numbers on this account) is an **expected delivery outcome**, not a create failure. Poll via `FetchMessage` — no webhooks.

---

### 4. Cancel a scheduled follow-up

| | |
|---|---|
| Operation | `UpdateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` |
| Wire | `Body`←`body`, `Status`←`status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |
| Notes | XML: “used to redact Message `body` text **and** to cancel not-yet-sent messages” |

**Cancel:** `sid` = persisted follow-up Sid, `status` = `TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`), `body: null`. Success: returned `Status` is `MessageEnumStatus.Canceled`.

**Already sent / not cancellable:** map and source do **not** name the HTTP status or error body. **UNVERIFIED.** Catch `SdkException<RawError>`, read `StatusCode` + `ReadAsString()`; if the call succeeds, trust returned `Status`. If the message already left `scheduled`/`queued`, cancel cannot recall it — then `FetchMessage` and persist the real `Status` (`sent` / `delivered` / `undelivered` / …). Do not invent a status-code table.

`UpdateMessage` / `CreateMessage` / `DeleteMessage` also inject a random `Idempotency-Key` Guid; caller cannot supply one.

---

### 5. Fetch message by Sid (delivery outcome)

| | |
|---|---|
| Operation | `FetchMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` (missing Sid → 4xx **UNVERIFIED** exact code; read `StatusCode`) |
| Map | `operations/Api20100401Message.md` |

Read `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `From`, `To`, `Body`. This is the only delivery channel (no webhooks).

---

### 6. Resend

Same `CreateMessage` row as §2. Caller idempotency key is **application-owned**. SDK header `Idempotency-Key` is always a fresh `Guid` and is not a parameter.

---

### 7. Dispose of message content at the provider (redact)

| | |
|---|---|
| Operation | `UpdateMessage` (same signature as §4) |
| Intent | Redact `body` so a later `FetchMessage` does not return the original text; **keep** the Message resource (`Sid`, `Status`, `ErrorCode`, …) |
| Not this | `DeleteMessage(string accountSid, string sid, …)` → `DELETE …/Messages/{Sid}.json`, returns `void` — **deletes the resource**, which drops provider-side outcome. Out of scope for this capability |

**Redact:** `body` = the redaction payload, `status: null`. The map/source **do not document the sentinel** that means “redact” versus “overwrite with new text”. **UNVERIFIED.** After the call, `FetchMessage` and confirm `Sid`/`Status`/`ErrorCode` still exist; treat leftover original `Body` as “not redacted”. Do not call `DeleteMessage`.

Optional create-time flag (not a substitute for later redact): `contentRetention` = `MessageEnumContentRetention.Discard` (wire `discard`) vs `Retain (retain)`.

---

### 8. Reconciliation listing (From + date range, provider-constrained)

| | |
|---|---|
| Operation | `ListMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `to` … `pageToken` (8 params) |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **No auto-pager.** Map: “none (only `page`, no `perPage`)”. XML: `pageSize` default 50, max 1000; `page` is client state; `pageToken` “provided by the API” |
| Map | `operations/Api20100401Message.md` · `records-4-Li-Me.md` (`ListMessageResponse`) |

**Query wire:** `To`←`to`, `From`←`from`, `DateSent`←`dateSent`, `DateSent<`←`dateSentQuery`, `DateSent>`←`dateSentQueryQuery`, `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`. DateTimeOffset values are sent as ISO-8601 UTC (`ToIso8601`), **not** as `YYYY-MM-DD` (XML describes the latter; the generated client sends the former).

**Provider-side constraints for this app:** `from` = `Twilio:FromNumber` (do **not** list the whole account then filter). `to` (recipient) = `null`. Date range: API `from` → `dateSentQueryQuery` (wire `DateSent>`); API `to` → `dateSentQuery` (wire `DateSent<`); `dateSent: null`. Inclusive/exclusive semantics of `>` / `<` are **UNVERIFIED** — pass the request’s ISO-8601 instants as those two arguments; do not invent extra inequalities. There is **no DateCreated filter** on this operation; scheduled messages with a null `date_sent` may be absent from a DateSent window (**limitation**, not a workaround).

**`ListMessageResponse` envelope** (`TwilioSdk.Models`): `End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`. Items: same `ApiV2010AccountMessage` (`Sid`, `From`, `To`, `Status`, `DateSent`/`DateCreated`, `Body`, …). **No `PageToken` field on the response** — only `NextPageUri`. Continue while `NextPageUri` is non-null using `pageToken` / `page` as the list params allow; how to derive `pageToken` from `NextPageUri` is **UNVERIFIED**.

---

### Enums in scope (`TwilioSdk.Models.Enums`, `map/models/enums.md`)

All are `StringEnum<T>` (not C# enums). Construct with the static member or `FromValue("wire")`. Compare via the static members. `.Value` is the wire string.

| Type | Members (C# = wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — only legal update status |
| `MessageEnumScheduleType` | `Fixed (fixed)` — Messaging Services only, with `sendAt` (enum XML says `send_time`; the **parameter is `sendAt` / wire `SendAt`**) |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` |
| `MessageEnumTrafficType` | `Free (free)` |
| `ValidationError` | see §1 |

Scheduled create typically returns `Status = Scheduled`. Immediate create typically `Queued` / `Accepted` / `Sending` (exact initial value **UNVERIFIED** — persist whatever is returned). Terminal-ish: `Delivered`, `Undelivered`, `Failed`, `Canceled`. `Read` is WhatsApp-only per XML.

---

### Error types that reach catch blocks (all in-scope ops)

Every listed operation is **Case B**. Catch:

| Type | When |
|---|---|
| `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` | Non-2xx from lookup / create / fetch / list / update / delete-message |
| `System.Net.Http.HttpRequestException` (and related transport) | Connectivity; not an `SdkException` |
| `System.Text.Json.JsonException` | See REQUIRED READING — two directions, opposite handling |

`SdkException<TError>` exposes **only** `required TError Error` (`Core/Exceptions/SdkException.cs`) — **no** `StatusCode` on the exception itself. For Case B: `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. There are no `TryGet…` accessors (those exist only on Case A `{Op}Error : ApiError`).

**Classification (best-effort from `RawError.StatusCode` + body string; body JSON shape is not a generated `{Op}Error`):**
- **401 / 403** — auth (`AccountSidAuthToken` missing/wrong, or `NoneAuthScheme`).
- **4xx on lookup** — treat as “not a usable destination” (or malformed request); do not persist the typed number.
- **4xx on create/update** — message not accepted / not cancellable / not redactable; **do not fail the order** on send/cancel paths.
- **5xx** — provider outage.
- Transport (`HttpRequestException`) — outage / network.
- **2xx create + later `undelivered`/`failed` on FetchMessage** — expected carrier outcome (e.g. US destinations on this account), not a catch-block error.

Do not parse `Exception.ToString()` when `ReadAsString()` exists. **UNVERIFIED:** live Twilio error JSON keys (`code`, `message`, `status`) vs `ReadAsJson<T>()` — extract best-effort, fall back to `ReadAsString()` / generic message.

---

## Trap notes

⚠ Step 1 (client registration) — `TwilioSdkClient` takes an `HttpClient` whose ownership and lifetime are not visible from the constructor; a per-request client vs a long-lived factory client has different connection and DNS costs. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — credentials live on `AccountSidAuthToken` as `BasicAuthCredentials` (`Username`/`Password`), must be set before the client is used, and a null property silently installs no auth. **MUST load `dotnet-authentication`** before wiring `Twilio:AccountSid` / `Twilio:AuthToken`.

⚠ Step 1 (BaseUrl / retries) — `Twilio:BaseUrl` is a **Default (api)** override only; lookup uses **Default4**. Retry/timeout options on `TwilioSdkClientOptions.Retry` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs retry on transport vs status is not the options type’s obvious reading. A non-idempotent `CreateMessage` POST plus SDK-injected random `Idempotency-Key` makes “whether a failed write can be re-sent” a real cost. **MUST load `dotnet-configuration-resilience`** before setting `Server.Default.Production.BaseUrl` or `Retry`.

⚠ Steps 2–9 (every call) — 24 / 15 / 8 nullable parameters have **no C# default**; a positional call mis-binds and `cancellationToken:` will not compile. **MUST load `dotnet-calling-endpoints`** before the first `client.Api20100401Message` / `LookupsV2PhoneNumber` call.

⚠ Steps 2–9 (models) — statuses, schedule type, update status, and validation errors are `StringEnum<T>` records, not C# enums; date fields on `ApiV2010AccountMessage` are `string?`; unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`** before mapping `LookupResponse` / `ApiV2010AccountMessage` / enum members.

⚠ Step 10 (error boundary) — all in-scope ops are Case B (`SdkException<RawError>`, no `TryGet…`); `TryGetRawError` is not on `RawError`; a 2xx missing `required` member is **not** this exception. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Tests — the constructor `HttpClient` argument is the test seam; do not fake SDK internals. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client ctor, DI, HttpClient lifetime
- `dotnet-authentication` — Step 1 `AccountSidAuthToken` / `BasicAuthCredentials`
- `dotnet-calling-endpoints` — Steps 2–9 named arguments, `ct:`, must-pass nullables
- `dotnet-models` — Steps 2–9 records, `StringEnum<T>`, dropped JSON
- `dotnet-configuration-resilience` — Step 1 BaseUrl (Default vs Default4), retries/timeouts, ListMessage pagination
- `dotnet-testing` — tests for the integration layer
- `dotnet-error-handling` — Step 10 boundary (always; every integration writes one)

**Both of these hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**
- Lookups **v2** (`FetchPhoneNumber3`) is the lookup in scope (v1 lacks `Valid` / `ValidationErrors` / typed line-type intelligence).
- Immediate SMS uses `from` = `Twilio:FromNumber` and does not send `messagingServiceSid`. Scheduled SMS uses `Twilio:MessagingServiceSid` + `MessageEnumScheduleType.Fixed` + `sendAt`, and still passes `from` = `Twilio:FromNumber` so `ListMessage(from:)` can constrain at the provider.
- `Twilio:BaseUrl` is applied only to `options.Server.Default.Production.BaseUrl` (messaging). Lookup stays on `lookups.twilio.com` unless that host is separately overridden (out of scope).
- Resend idempotency is enforced in the eShopOnWeb app (store the caller key, skip a second `CreateMessage`). The SDK will not do it.
- Send/cancel/schedule failures are logged and persisted as notification state; they must not throw out of the order placed / dispatched / cancelled path.
- Live undeliverable destinations (US numbers on this account) are represented by a successful create plus later `undelivered`/`failed` on fetch — not by a create exception.

**Blockers / gaps (not invented around)**
- **Caller-supplied idempotency key on create-message: not in the SDK surface.** No parameter; `RequestOptions` has only `LogLevel`; generated code always sends `Idempotency-Key: new Guid`. App-side dedupe is required; there is no SDK header to set.
- **`LineTypeIntelligenceInfo.Type` / `LineStatusInfo.Status` have no enum in the map or source.** The sheet cannot name “mobile” (or any other line type) as a contract constant. Usable-destination rejection is therefore `Valid` / `ValidationErrors` / lookup 4xx / `LineTypeIntelligence.ErrorCode` as specified above; a Type allow-list would be product policy, not SDK contract.
- **`sendAt` allowed window:** absent from map and source. **UNVERIFIED.** Provider rejection is the only SDK-visible signal (`SdkException<RawError>`).
- **Redact sentinel for `UpdateMessage.body`:** absent from map and source. **UNVERIFIED.** Do not use `DeleteMessage` to “redact”.
- **Cancel-already-sent error status:** absent from map and source. **UNVERIFIED.** Read `RawError.StatusCode` + body; confirm with `FetchMessage`.
- **`ListMessage` has no DateCreated filter** and no auto-pager; `ListMessageResponse` has `NextPageUri` but no `PageToken` field. Token continuation from that URI is **UNVERIFIED**. Scheduled-but-unsent rows may not appear in a DateSent range.
