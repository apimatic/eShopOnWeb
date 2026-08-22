# eShopOnWeb SMS-notification integration — Twilio .NET SDK plan

NuGet: `AsadAli.TwilioSdk` (version-less). Root namespace: `TwilioSdk` (not `Twilio`). Client: `TwilioSdk.TwilioSdkClient`. Map: `sdk-map.md` (source commit `51fdf48`).

## Scope & sequence

1. **Client + auth + `Twilio:BaseUrl`** — construct one `TwilioSdkClient`; set `AccountSidAuthToken`; apply `Twilio:BaseUrl` only to the messaging server node (`Server.Default`), never Lookup (`Server.Default4`).
2. **Flow 1 — register number** — `client.LookupsV1PhoneNumberApi` is **not** the usability check (untyped `Carrier`). Use `client.LookupsV2PhoneNumber.FetchPhoneNumber3`. Reject before store; persist `LookupResponse.PhoneNumber` (E.164).
3. **Flow 2 — immediate SMS** (placed / dispatched / cancelled) — `client.Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`. Capture `Sid` + `Status` (+ `ErrorCode` / `ErrorMessage`). Catch send failure so the order operation still succeeds.
4. **Flow 2 — schedule follow-up** (dispatch) — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` (enum docs: Messaging Services only). Messaging API → `Twilio:BaseUrl` applies. Persist returned `Sid`; scheduled identity is `Status == Scheduled`.
5. **Flow 2 — cancel follow-up** (order cancelled) — `UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled` if the follow-up has not gone out.
6. **Fetch delivery outcome (no webhooks)** — `FetchMessage` by provider `Sid`.
7. **Flow 3 — reconciliation** — `ListMessage` with `from` = `Twilio:FromNumber` (provider-side filter, not list-all-then-filter) and `DateSent>` / `DateSent<` for the inclusive `from`/`to` range. Walk pages. Messaging API → `Twilio:BaseUrl` applies.
8. **Flow 3 — resend + idempotency key** — `CreateMessage` again. **SDK gap:** caller-supplied idempotency is not exposable (see CONTRACT SHEET + Blockers).
9. **Flow 3 — dispose/redact body** — `UpdateMessage` with `body` (do **not** `DeleteMessage` — that removes the resource). Confirm via `FetchMessage` that `Body` is gone; `Sid`/`Status`/`ErrorCode` survive.
10. **Error boundary** around every SDK call (all in-scope ops are Case B `SdkException<RawError>`).

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

No-throw `…Result` variants: **absent** on every operation below.

### Client construction, servers, auth

| Fact | Value | Cite |
|---|---|---|
| Client | `TwilioSdk.TwilioSdkClient` | `sdk-map.md` · `TwilioSdkClient.cs` |
| Ctor | `TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` — SDK does **not** own `httpClient` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` registers a **singleton** client via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options (`TwilioSdk.TwilioSdkClientOptions`) | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). `Default()` → `Production`. **Only member.** | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Auth credentials | `new BasicAuthCredentials { Username = <AccountSid or API key SID>, Password = <AuthToken or API key secret> }` — both members `required`. XML: API key as username + secret as password; Account SID + auth token also accepted (docs: limit SID/token to local testing). Product uses AccountSid + AuthToken → `Username` / `Password`. | `sdk-map.md` Servers & auth · `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `ServerOptions` (`TwilioSdk`) | `Default`, `Default1` … `Default14` — each a `*Options` with nested `Production.BaseUrl` | `ServerOptions.cs` |
| Messaging API host (Messages create/fetch/list/update) | HTTP server **Default (api)**. Path prefix `/2010-04-01/Accounts/{AccountSid}/…`. Override: **`options.Server.Default.Production.BaseUrl`** (default `"https://api.twilio.com"`). **`Twilio:BaseUrl` goes here, verbatim.** There is **no** per-request base-URL on `RequestOptions`. | `operations/Api20100401Message.md` · `Servers/DefaultOptions.cs` · `Api/Api20100401Message.cs` (`_server.Default(...)`) |
| Lookup host | HTTP server **Default4 (lookups)**. Override: `options.Server.Default4.Production.BaseUrl` (default `"https://lookups.twilio.com"`). **Do not set this from `Twilio:BaseUrl`.** One client is enough: override only `Default`. | `operations/LookupsV2PhoneNumber.md` · `Servers/Default4Options.cs` · `Api/LookupsV2PhoneNumber.cs` (`_server.Default4(...)`) |
| Per-request options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No headers, no idempotency, no base-URL. | `Core/RequestOptions.cs` |
| Retry options (all `required`; or `RetryOptions.Default()` / `Disabled()`) | `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` | `sdk-map.md` · `Core/Configuration/RetryOptions.cs` |

`Twilio:AccountSid` (config) is also the path param `accountSid` on every Messages operation (`AC…`).

---

### 1. Phone-number lookup / usability — `FetchPhoneNumber3`

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, **no default** → pass `null` to skip |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · server **Default4 (lookups)** · **`Twilio:BaseUrl` does not apply** |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity_match / reassigned_number / pre_fill extras unused here) |
| Returns | `TwilioSdk.Models.LookupResponse` — **no envelope wrapper** (the record *is* the body) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` · `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `operations/LookupsV2PhoneNumber.md` · `records-4-Li-Me.md` · `Api/LookupsV2PhoneNumber.cs` |

**`fields`:** C# type is `string?` (comma-separated), **not** `IReadOnlyList<Field>`. XML possible values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. For SMS usability pass e.g. `"line_type_intelligence,line_status"` (add `sms_pumping_risk` if blocking pumped numbers). `phoneNumber`: E.164 or national; default country +1. Optional `countryCode` if national format.

**`LookupResponse` fields this flow reads** (`TwilioSdk.Models`, `records-4-Li-Me.md` · `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical E.164** to store (not the caller-typed string). XML: `+` + country code + subscriber number |
| `Valid (valid)` | `bool?` | XML: number is in a valid range freely assignable by a carrier. **Primary reject gate:** treat `Valid != true` (false or null) as not usable |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Invalid reasons — reject if non-empty |
| `NationalFormat (national_format)` | `string?` | Display only |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status` |
| `SmsPumpingRisk (sms_pumping_risk)` | `TwilioSdk.Models.SmsPumpingRiskInfo?` | Optional extra signal |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, **`Type (type): string?`**, `ErrorCode (error_code): int?`. **`Type` is `string?`, not `LineType`.** `TwilioSdk.Models.Enums.LineType` is a *different* type (XML: “new line type to override the original line type”) — do not use it as this field’s C# type.

**`LineStatusInfo`:** `Status (status): string?`, `ErrorCode (error_code): int?`.

**`SmsPumpingRiskInfo`** (`records-6-Sh-V2.md`): `NumberBlocked (number_blocked): bool?`, `SmsPumpingRiskScore (sms_pumping_risk_score): int?`, `ErrorCode (error_code): int?`, …

**Usable SMS destination — what the SDK actually models:** there is **no** `sms_capable` / `sms` boolean. Closest documented signals: `Valid` + `ValidationErrors` + (if requested) `LineTypeIntelligence.Type` and `LineStatus.Status`. `Type` allow-list is **UNVERIFIED** (not in map/source). Defensive: reject on `Valid != true` or any `ValidationErrors`; if `LineTypeIntelligence.ErrorCode` is set, treat as package failure (do not store); do not invent a Type allow-list from memory.

**Invalid / not found / not SMS-capable errors:** operation is Case B only — no typed `{Op}Error`, no `TryGet…`. HTTP statuses for 404/invalid are **UNVERIFIED**. Defensive: any `SdkException<RawError>` from this call → reject (do not store); read `ex.Error.StatusCode` + `ex.Error.ReadAsString()` (not `ex.ToString()`).

**Do not use** `LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber` (`Carrier (carrier): object?`) for this flow.

---

### 2. Send SMS (immediate) — `CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Method | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — pass `null` to skip |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default (api)** · **`Twilio:BaseUrl` applies** · body `application/x-www-form-urlencoded` |
| Wire ← C# (form) | `To`←`to`, `From`←`from`, `MessagingServiceSid`←`messagingServiceSid`, `Body`←`body`, `ScheduleType`←`scheduleType`, `SendAt`←`sendAt`, `StatusCallback`←`statusCallback`, `FallbackFrom`←`fallbackFrom`, … (full list on the operations page) |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **no envelope wrapper** |
| Error | **Case B** `SdkException<RawError>` · `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Cite | `operations/Api20100401Message.md` · `records-1-Ac-Ca.md` · `Api/Api20100401Message.cs` |

**From vs MessagingServiceSid:** both are independent optional form fields (`from` / `From`, `messagingServiceSid` / `MessagingServiceSid`). SDK does **not** require either at the C# level (`to` + `accountSid` are the non-nullable args). Immediate lifecycle SMS: pass `from: Twilio:FromNumber`, `body: <text>`, `to: <stored E.164>`, `scheduleType: null`, `sendAt: null`. Pass `messagingServiceSid: Twilio:MessagingServiceSid` only if this send should go through the Messaging Service; otherwise `null`. Whether the provider accepts both together, or neither, is **UNVERIFIED**.

**Capture for later act/report:**

| C# (wire) | Type | Use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message id (`SM…` / `MM…`) |
| `Status (status)` | `MessageEnumStatus?` | Current delivery outcome |
| `ErrorCode (error_code)` | `int?` | Set when status is failed/undelivered |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code` |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT |
| `To (to)` / `From (from)` | `string?` | E.164 |
| `Body (body)` | `string?` | Text (later redacted) |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | `MG…` if used |

**Send failure vs order operation:** catch `SdkException<RawError>` (and the `JsonException` traps in REQUIRED READING) at the notification boundary — do not let it fail the order. Read `ex.Error.StatusCode` + `ex.Error.ReadAsString()`. No typed `TryGet…` accessors.

**Internal header (not caller-controlled):** generated code always sends `Idempotency-Key: Guid.NewGuid()` on create/update/delete. See §7.

---

### 3. Schedule follow-up SMS — same `CreateMessage`

Messaging API (`_server.Default`) → **`Twilio:BaseUrl` applies**.

| Param | Wire | Type | Contract |
|---|---|---|---|
| `scheduleType` | `ScheduleType` | `MessageEnumScheduleType?` | Only member: `MessageEnumScheduleType.Fixed` (wire `fixed`). XML: **Messaging Services only**; “in conjunction with the `send_time` parameter” — the **actual** C#/wire name is `sendAt` / `SendAt` (enum XML is stale wording; signature wins) |
| `sendAt` | `SendAt` | `DateTimeOffset?` | Schedule instant. Offset lives on the `DateTimeOffset`. Form-encoded via the SDK flattener (JSON-normalize of `DateTimeOffset`). **Min/max delay, timezone rules, and ISO format constraints are not in the map or XML.** GAP — do not invent 5-minute / 7-day windows |
| `messagingServiceSid` | `MessagingServiceSid` | `string?` | Required by the schedule-type XML (“Messaging Services only”) — pass `Twilio:MessagingServiceSid` |
| `from` | `From` | `string?` | Optional alongside the service; immediate-send `FromNumber` may still be passed. Combined semantics **UNVERIFIED** |
| `body` / `to` / `accountSid` | as send | | Same as immediate send |

**How the returned resource identifies a scheduled message:** same `ApiV2010AccountMessage`. `Sid` is the handle for cancel/fetch. `Status` member `MessageEnumStatus.Scheduled` (wire `scheduled`) is the scheduled state. `DateSent` may be empty until send.

---

### 4. Cancel a scheduled message — `UpdateMessage`

| | |
|---|---|
| Method | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · **Default (api)** · **`Twilio:BaseUrl` applies** |
| Wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Cite | `operations/Api20100401Message.md` · `Api/Api20100401Message.cs` |

**Cancel:** `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`), `body: null`. Success: returned `Status` should be `MessageEnumStatus.Canceled`.

**Already sent:** SDK XML does not define the outcome. **UNVERIFIED** status codes. Defensive: `FetchMessage` first; if `Status` is not `Scheduled` (and not `Queued`/`Accepted` if those appear pre-send), skip cancel. If `UpdateMessage` throws `SdkException<RawError>`, read `StatusCode`/`ReadAsString()` and treat as “could not cancel” (message may already have gone out) — do not fail the order cancel if that is product policy.

---

### 5. Fetch current delivery outcome — `FetchMessage`

| | |
|---|---|
| Method | `FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · **Default (api)** · **`Twilio:BaseUrl` applies** |
| Returns | `ApiV2010AccountMessage` (identifier `Sid`, outcome `Status`, `ErrorCode`, `ErrorMessage`, `Body`) |
| Error | **Case B** `SdkException<RawError>` (missing SID → read `StatusCode`; typically not-found is **UNVERIFIED**) |
| Cite | `operations/Api20100401Message.md` |

---

### 6. Reconciliation list — `ListMessage`

| | |
|---|---|
| Method | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `to` … `pageToken` (8 params) |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · **Default (api)** · **`Twilio:BaseUrl` applies** |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **none** (no auto-pager). XML: `pageSize` default **50**, max **1000**; `page` = client state; `pageToken` = “provided by the API” |
| Cite | `operations/Api20100401Message.md` · `records-4-Li-Me.md` · `Api/Api20100401Message.cs` |

**Filters (exact C# names — `DateSentAfter` / `DateSentBefore` do not exist):**

| C# param | Wire | Type | Use for `GET /api/notifications/reconciliation?from=&to=` |
|---|---|---|---|
| `from` | `From` | `string?` | **Must** pass `Twilio:FromNumber` — provider-side sent-FROM filter |
| `to` | `To` | `string?` | Recipient filter — pass `null` for reconciliation-by-sender |
| `dateSent` | `DateSent` | `DateTimeOffset?` | Exact sent timestamp — pass `null` for a range |
| `dateSentQuery` | `DateSent<` | `DateTimeOffset?` | **Range end (`to`)** — sent before this instant |
| `dateSentQueryQuery` | `DateSent>` | `DateTimeOffset?` | **Range start (`from`)** — sent after this instant |
| `pageSize` | `PageSize` | `long?` | Page size |
| `page` | `Page` | `int?` | Page index (client state) |
| `pageToken` | `PageToken` | `string?` | Cursor |

List date values are sent as `dateSent?.ToIso8601()` → UTC **`yyyy-MM-ddTHH:mm:ss.fffZ`**. XML copy-paste mentions `YYYY-MM-DD` / `<=` / `>=` string prefixes; those prefixes **cannot** be passed through the `DateTimeOffset?` parameters. Inclusivity of `DateSent>` / `DateSent<` vs the product’s inclusive `DateTimeOffset` from/to is **UNVERIFIED**. Defensive: pass the API `from` as `dateSentQueryQuery` and API `to` as `dateSentQuery`; if boundary messages are missing, that is a live-wire inclusivity issue, not a missing SDK param.

**Item envelope:** `TwilioSdk.Models.ListMessageResponse`:

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` — **items** |
| `NextPageUri (next_page_uri)` | `string?` |
| `PreviousPageUri (previous_page_uri)` | `string?` |
| `FirstPageUri (first_page_uri)` | `string?` |
| `Page (page)` / `PageSize (page_size)` / `Start (start)` / `End (end)` / `Uri (uri)` | paging metadata |

**Walk the whole range:** there is no `PageToken` **response** field. Loop: call `ListMessage` → yield `Messages` → if `NextPageUri` is null, stop; else re-call with a `pageToken`. Extracting `PageToken` from `NextPageUri` is **UNVERIFIED**. Defensive: parse `NextPageUri` query for `PageToken` and pass it as `pageToken`; keep `from` / date filters on every page request.

---

### 7. Resend + caller-supplied idempotency key

| | |
|---|---|
| Send op | Same `CreateMessage` as §2 (fresh `to`/`from`/`body`; new `Sid` on success) |
| Public idempotency API | **None.** `RequestOptions` has only `LogLevel`. `CreateMessage` has no idempotency parameter |
| What the SDK actually sends | Generated create/update/delete **always** attach header **`Idempotency-Key`** with **`Guid.NewGuid()`** — a new value every call, not the operator key |
| Cite | `Api/Api20100401Message.cs` · `Core/RequestOptions.cs` |

**BLOCKER:** same caller key **cannot** be applied through the public SDK surface; a second `CreateMessage` always gets a new `Idempotency-Key` and is a genuine second attempt at the HTTP layer. Do not invent a header API the types do not expose.

---

### 8. Dispose / redact body — `UpdateMessage` (not `DeleteMessage`)

| | |
|---|---|
| Op | `UpdateMessage` · `body` set, `status: null` |
| Do **not** use | `DeleteMessage` — XML: “Deletes a Message resource from your account” (delivery record would not survive) |
| Confirm | Subsequent `FetchMessage`: `Body` empty/absent; `Sid`, `Status`, `ErrorCode`, `ErrorMessage` still present |
| Sentinel `body` value | **UNVERIFIED** (XML says the op redacts `body` but does not name the token). Defensive: pass empty string `""`; confirm with `FetchMessage`. If `Body` still holds the original text, redaction did not take |
| Cite | `operations/Api20100401Message.md` |

`MessageEnumContentRetention.Discard` is a **create-time** flag (`contentRetention` on `CreateMessage`), not later shopper-initiated dispose.

---

### Enums in scope (`TwilioSdk.Models.Enums` · `map/models/enums.md`)

All are `StringEnum<T>` — construct with static members or `FromValue("wire")`, not C# `enum`.

| Type | Members (C# = wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` |
| `MessageEnumTrafficType` | `Free (free)` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — **not** the type of `FetchPhoneNumber3`’s `fields` string |
| `LineType` | `Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)` — **not** the type of `LineTypeIntelligenceInfo.Type` |
| `ServerEnvironment` | `Production (production)` · namespace `TwilioSdk.Servers` |

---

### `ApiV2010AccountMessage` envelope (create / fetch / update / list items)

No wrapper field — read properties on the record itself (`records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`): `Sid (sid)`, `Status (status)`, `ErrorCode (error_code)`, `ErrorMessage (error_message)`, `Body (body)`, `From (from)`, `To (to)`, `DateSent (date_sent)`, `DateCreated (date_created)`, `DateUpdated (date_updated)`, `MessagingServiceSid (messaging_service_sid)`, `AccountSid (account_sid)`, `Direction (direction)`, `NumSegments (num_segments)`, `NumMedia (num_media)`, `Price (price)`, `PriceUnit (price_unit)`, `Uri (uri)`, `ApiVersion (api_version)`, `SubresourceUris (subresource_uris)`.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime versus the SDK wrapper’s lifetime, and whether `AddTwilioSdkClient` is the right registration for this host. **MUST load `dotnet-client-initialization`** before writing the factory or DI callback.

⚠ Step 1 (client registration / `Twilio:BaseUrl`) — base-URL override is nested **per server node**; putting `Twilio:BaseUrl` on the wrong node (or a single global URL) retargets Lookup or leaves messaging on the provider default. **MUST load `dotnet-configuration-resilience`** before wiring `options.Server`.

⚠ Step 1 (auth) — which credentials property to set, when it must be set relative to construction, and how secrets are loaded. **MUST load `dotnet-authentication`** before assigning `AccountSidAuthToken`.

⚠ Step 1 (retries/timeouts) — the SDK’s retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a failed **write** (`CreateMessage` / `UpdateMessage`) may or may not be re-sent. **MUST load `dotnet-configuration-resilience`** before copying `RetryOptions`.

⚠ Steps 2–9 (every call) — 8–24 optional parameters have **no C# default** and mis-bind in a positional call; the cancellation token is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `CreateMessage` / `FetchPhoneNumber3` / `ListMessage`.

⚠ Steps 2–9 (models/enums) — status, schedule, and validation values are `StringEnum<T>`, not C# enums; unmodeled JSON is dropped. **MUST load `dotnet-models`** before constructing `MessageEnumScheduleType` / comparing `MessageEnumStatus` / reading `LookupResponse`.

⚠ Steps 2–9 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`); there are no `TryGet…` accessors on these ops; parsing `Exception.ToString()` instead of `RawError` loses status/body. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

⚠ Step 7 (reconciliation paging) — the SDK does not auto-walk `NextPageUri` / `pageToken`; using `page` as if it were the cursor, or listing without `from`, drops or over-fetches rows. **MUST load `dotnet-configuration-resilience`** before implementing the date-range walk.

⚠ Tests — the `HttpClient` constructor argument is the test seam; faking controller types or internals will not match runtime. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, DI (`AddTwilioSdkClient`), `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `Twilio:BaseUrl` / `ServerOptions`, retries, timeouts; Step 7 — list pagination |
| `dotnet-calling-endpoints` | Steps 2–9 — named args, must-pass-null, `ct` |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, request/response records, wire names |
| `dotnet-error-handling` | All SDK calls — Case B `RawError`, catch ladder, **both** `JsonException` directions above |
| `dotnet-testing` | Tests of the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Live Account SID + Auth Token (not API keys) unless config later switches; both map onto `BasicAuthCredentials.Username` / `Password`.
- One `TwilioSdkClient` for the app; `Twilio:BaseUrl` is applied only to `options.Server.Default.Production.BaseUrl`.
- Flow 1 uses Lookups **v2** `FetchPhoneNumber3`, not v1.
- Immediate SMS uses `from` = `Twilio:FromNumber`; scheduled follow-up additionally sets `messagingServiceSid` + `scheduleType: Fixed` + `sendAt` because schedule-type XML is Messaging Services only.
- Reconciliation always passes `from: Twilio:FromNumber` into `ListMessage` (not a client-side filter).
- No inbound webhooks / `statusCallback` (product: no public URL). Delivery is pull via `FetchMessage` / `ListMessage`.
- `DeleteMessage` is out of scope for dispose.

**Blockers / gaps (do not invent)**

1. **Caller-supplied idempotency (Flow 3 resend)** — **not in the public SDK.** `RequestOptions` cannot carry headers; `CreateMessage` always sends `Idempotency-Key: Guid.NewGuid()`. Same operator key cannot suppress a second message through this SDK.
2. **Schedule min/max delay and timezone rules** — not in the map or `CreateMessage` XML. Only `sendAt: DateTimeOffset?` + `scheduleType: Fixed` + Messaging Service SID are documented.
3. **SMS-capable line-type allow-list** — `LineTypeIntelligenceInfo.Type` is an untyped `string?`. No `sms_capable` field. `Valid` is the only XML-documented usability boolean.
4. **Redaction sentinel** — `UpdateMessage` is the redaction op; the `body` value that clears provider text is **UNVERIFIED**.
5. **Cancel-already-sent behavior and lookup/list HTTP status codes** — Case B only; no typed error payloads. **UNVERIFIED** live status codes.
6. **List inclusivity and next-page token** — `DateSent>` / `DateSent<` inclusivity vs inclusive `DateTimeOffset` from/to, and how to populate `pageToken` from `NextPageUri`, are **UNVERIFIED**. Response has `NextPageUri` but no `PageToken` property.
7. **`DateSentAfter` / `DateSentBefore` names** — **do not exist**; use `dateSentQueryQuery` / `dateSentQuery`.
