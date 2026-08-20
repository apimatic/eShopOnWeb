# Twilio SMS order notifications — plan + contract sheet

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdk.TwilioSdkClient`. Additive on `src/PublicApi`. No webhooks; poll via fetch/list.

## Scope & sequence

1. **Client + config** — bind `Twilio:*` into `TwilioSdkClientOptions` (auth, messaging-only base URL, `FromNumber`, `MessagingServiceSid`); register `TwilioSdkClient`.
2. **Lookup / register number** — `LookupsV2PhoneNumber.FetchPhoneNumber3`; store canonical E.164 from the lookup; reject unusable destinations at `POST /api/contact-numbers`.
3. **Immediate SMS** — `Api20100401Message.CreateMessage` on place-order, dispatch, cancel (swallow send failures so the order operation still succeeds).
4. **Schedule follow-up** — same `CreateMessage` with `scheduleType` + `sendAt` on dispatch (provider-side queue, not an in-app timer).
5. **Cancel scheduled follow-up** — `Api20100401Message.UpdateMessage` with `status: Canceled` on order cancel.
6. **Fetch delivery outcome** — `Api20100401Message.FetchMessage` (poll; no `StatusCallback`).
7. **Resend** — `CreateMessage` again; see idempotency blocker below.
8. **Redact provider body** — `Api20100401Message.UpdateMessage` with empty `body` (`DELETE /api/notifications/{id}/content`). Do **not** use `DeleteMessage` (that removes the resource).
9. **Reconciliation** — `Api20100401Message.ListMessage` with provider-side `From` + `DateSent>` / `DateSent<` (`GET /api/notifications/reconciliation`).
10. **Error boundary** — Case B `SdkException<RawError>` on every in-scope op; lookup failures fail registration; send/schedule/cancel/fetch failures must not fail the order.

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

### Client construction, auth, servers

| Fact | Value | Cite |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` | `ServiceCollectionExtensions.cs` |
| Options type | `TwilioSdk.TwilioSdkClientOptions` | `sdk-map.md` / `TwilioSdkClientOptions.cs` |
| `Environment` | `TwilioSdk.Servers.ServerEnvironment` — member `Production` (wire `production`); `ServerEnvironment.Default()` → `Production` | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Auth property | `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Credentials shape | `BasicAuthCredentials` `{ required string Username, required string Password }` — Account SID → `Username`, Auth Token → `Password` (XML also allows API key / secret) | `TwilioSdkClientOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `Retry` | `TwilioSdk.Core.Configuration.RetryOptions` — all members `required`; `RetryOptions.Default()` / `RetryOptions.Disabled()` | `sdk-map.md`, `Core/Configuration/RetryOptions.cs` |
| `Server` | `TwilioSdk.ServerOptions` (repo-root type, namespace `TwilioSdk`) | `TwilioSdkClientOptions.cs`, `ServerOptions.cs` |
| Messaging host | Message ops use `_server.Default(...)` → `options.Server.Default.Production.BaseUrl` default **`https://api.twilio.com`**. Type: `TwilioSdk.Servers.DefaultOptions` / nested `ProductionOptions.BaseUrl: string` | `Api/Api20100401Message.cs`, `Servers/DefaultOptions.cs` |
| Lookups host | Lookup ops use `_server.Default4(...)` → `options.Server.Default4.Production.BaseUrl` default **`https://lookups.twilio.com`**. Type: `TwilioSdk.Servers.Default4Options` | `Api/LookupsV2PhoneNumber.cs`, `Servers/Default4Options.cs` |
| `Twilio:BaseUrl` | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` only. Do **not** write it onto `Default4` (or Default1–3, 5–14). | brief + `ServerOptions.cs` |
| Config keys | `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (optional, messaging API only). Values from env; never hard-code. | brief |
| Per-call options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel`. No extra headers, no idempotency slot. | `Core/RequestOptions.cs` |

`TwilioSdkClientOptions` members (`sdk-map.md`): `Environment`, `Retry`, `Logging`, `Server`, `AccountSidAuthToken`.

### Operations

| Step | Controller | Method (verbatim) | HTTP | Returns | Error | Pagination |
|---|---|---|---|---|---|---|
| 2 | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 15 params `fields`…`partnerSubId` nullable, **must pass explicitly** (`null` to skip) | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 lookups) | `TwilioSdk.Models.LookupResponse` (no extra envelope) | Case B `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | none |
| 3,4,7 | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 params `statusCallback`…`contentSid` nullable, **must pass explicitly**. Body is **form-urlencoded**, not JSON. | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default api) | `TwilioSdk.Models.ApiV2010AccountMessage` (no extra envelope) | Case B `SdkException<RawError>` (same accessors) | none |
| 5,8 | same | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` nullable, **must pass explicitly** | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` | `ApiV2010AccountMessage` | Case B `SdkException<RawError>` | none |
| 6 | same | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` | `ApiV2010AccountMessage` | Case B `SdkException<RawError>` | none |
| 9 | same | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `to`…`pageToken` nullable, **must pass explicitly** | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` | `TwilioSdk.Models.ListMessageResponse` | Case B `SdkException<RawError>` | **SDK does not auto-page.** Request: `PageSize`/`Page`/`PageToken`. XML: default page size 50, max 1000; `page` is client state; `pageToken` is API-provided. |
| — | same | `DeleteMessage(string accountSid, string sid, …)` — **out of scope** for content disposal (deletes the resource) | `DELETE …/Messages/{Sid}.json` | `void` | Case B | none |

No-throw `…Result` variants: **absent** on every operation (`sdk-map.md`).

Cites: `map/operations/LookupsV2PhoneNumber.md`, `map/operations/Api20100401Message.md`.

#### CreateMessage / ListMessage / UpdateMessage wire names

CreateMessage form fields (`wire ← csharp`): `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid`. Path: `AccountSid` ← `accountSid`. Null params are **omitted** (not sent as empty).

ListMessage query (`wire ← csharp`): `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, `DateSent<` ← `dateSentQuery`, `DateSent>` ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken`. Date values are encoded with `ToIso8601()` → **`yyyy-MM-ddTHH:mm:ss.fffZ`** (UTC). The inequality lives in the **parameter name**, not in the value.

UpdateMessage form: `Body` ← `body`, `Status` ← `status`. Path: `AccountSid`, `Sid`.

FetchPhoneNumber3 query: `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity-match/pre-fill fields unused here. Path: `PhoneNumber` ← `phoneNumber`.

### Request/response fields the integration reads

**`TwilioSdk.Models.LookupResponse`** (`records-4-Li-Me.md`, `Models/LookupResponse.cs`) — no wrapper; the return value **is** the payload.

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber` (`phone_number`) | `string?` | Canonical **E.164** (`+` + country code + subscriber). **This is what to store.** XML: E.164. |
| `Valid` (`valid`) | `bool?` | Provider validity: “in a valid range that can be freely assigned by a carrier to a user.” Reject registration when this is not `true`. |
| `ValidationErrors` (`validation_errors`) | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid. |
| `NationalFormat` (`national_format`) | `string?` | Display only; do not store as canonical. |
| `CountryCode` (`country_code`) | `string?` | ISO 3166-1 alpha-2. |
| `CallingCountryCode` (`calling_country_code`) | `string?` | E.164 prefix. |
| `LineTypeIntelligence` (`line_type_intelligence`) | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated only if `fields` includes `line_type_intelligence`. |
| `LineStatus` (`line_status`) | `TwilioSdk.Models.LineStatusInfo?` | Populated only if `fields` includes `line_status`. |

**`TwilioSdk.Models.LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode` (`mobile_country_code`): `string?`, `MobileNetworkCode` (`mobile_network_code`): `string?`, `CarrierName` (`carrier_name`): `string?`, **`Type` (`type`): `string?`** (not an enum on this model), `ErrorCode` (`error_code`): `int?`.

**`TwilioSdk.Models.LineStatusInfo`**: `Status` (`status`): `string?`, `ErrorCode` (`error_code`): `int?`.

Lookup `fields` is a **comma-separated `string?`**, not `Field`. XML list: `validation, caller_name, sim_swap, call_forwarding, line_status, line_type_intelligence, identity_match, reassigned_number, sms_pumping_risk, phone_number_quality_score, pre_fill`. `Valid` is on the resource even without a `validation` field token. Pass `fields: "line_type_intelligence"` (wire of `TwilioSdk.Models.Enums.Field.LineTypeIntelligence`). Optional: `"line_type_intelligence,line_status"`.

**Usable destination:** reject when `Valid != true` or `ValidationErrors` is non-empty, or when `FetchPhoneNumber3` throws `SdkException<RawError>`. `Type` is an untyped string — the SDK does **not** bind it to `LineType`. Request `line_type_intelligence`; if `LineTypeIntelligence` is null or `ErrorCode` is set, treat intelligence as unavailable (see Assumptions). `LookupsV1PhoneNumberApi.FetchPhoneNumber2` returns `LookupsV1PhoneNumber` with untyped `Carrier (carrier): object?` and **no** `Valid` — do not use V1 for this feature.

**`TwilioSdk.Models.ApiV2010AccountMessage`** (`records-1-Ac-Ca.md`) — return of create/fetch/update; list items.

| C# (wire) | Type | Role |
|---|---|---|
| `Sid` (`sid`) | `string?` | Provider message id (`SM…` / `MM…`). Persist this. |
| `Status` (`status`) | `MessageEnumStatus?` | Delivery / schedule outcome. |
| `To` (`to`) | `string?` | Destination. |
| `From` (`from`) | `string?` | Sender (E.164 / sender id). |
| `Body` (`body`) | `string?` | Text; after redact, expect empty or null (**UNVERIFIED** which). |
| `DateCreated` (`date_created`) | `string?` | RFC 2822 GMT. |
| `DateSent` (`date_sent`) | `string?` | RFC 2822 GMT; when Twilio sent. |
| `DateUpdated` (`date_updated`) | `string?` | RFC 2822 GMT. |
| `ErrorCode` (`error_code`) | `int?` | Set when status is `failed` / `undelivered`; else null. XML: do not key programmatic logic on a specific code’s stability. |
| `ErrorMessage` (`error_message`) | `string?` | Same caveat. |
| `MessagingServiceSid` (`messaging_service_sid`) | `string?` | MG… |
| `AccountSid` (`account_sid`) | `string?` | AC… |
| `Direction` (`direction`) | `MessageEnumDirection?` | Outbound API → `outbound-api`. |
| `NumSegments` (`num_segments`) | `string?` | XML: via Messaging Service this can be `"0"` until a sender is assigned. |
| `Price` (`price`) / `PriceUnit` (`price_unit`) | `string?` | After send. |
| `Uri` (`uri`) / `SubresourceUris` (`subresource_uris`) / `ApiVersion` (`api_version`) / `NumMedia` (`num_media`) | as mapped | unused unless needed |

**`TwilioSdk.Models.ListMessageResponse`** (`records-4-Li-Me.md`): `End` (`end`): `int?`, `FirstPageUri` (`first_page_uri`): `string?`, `NextPageUri` (`next_page_uri`): `string?`, `Page` (`page`): `int?`, `PageSize` (`page_size`): `int?`, `PreviousPageUri` (`previous_page_uri`): `string?`, `Start` (`start`): `int?`, `Uri` (`uri`): `string?`, **`Messages` (`messages`): `IReadOnlyList<ApiV2010AccountMessage>?`**. There is **no** `PageToken` field on the response. Page by passing `page` / `pageSize` / `pageToken` on the next `ListMessage` while `NextPageUri` is non-null (or `Messages` is empty).

### From vs MessagingServiceSid vs schedule

| Mode | Pass | Skip |
|---|---|---|
| Immediate SMS (place/dispatch/cancel) | `accountSid`, `to` (registered E.164), `body`, `from: Twilio:FromNumber`. `statusCallback: null` (no public URL). | Do not send `scheduleType` / `sendAt`. `messagingServiceSid` may be passed as well; **`from` is required for reconciliation**, which lists by this app’s sending number. |
| Scheduled follow-up (dispatch) | Same `to`/`body`/`from`, plus `messagingServiceSid: Twilio:MessagingServiceSid`, `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: DateTimeOffset` (a few days later). | `statusCallback: null`. Enum XML: scheduling is **Messaging Services only**, value `fixed`, “in conjunction with the `send_time` parameter” — SDK wire name is **`SendAt`**, C# `sendAt`. |
| Either | `from` and `messagingServiceSid` are independently optional in the signature. Null is omitted. | |

`sendAt` encoding: `CreateMessage` passes `DateTimeOffset?` through the form flattener (JSON then string), **not** `ToIso8601()`. Supply a `DateTimeOffset` with a correct offset (UTC recommended). Min/max schedule window is **not** in the map or the method XML (`<param name="sendAt"></param>` is empty) — **UNVERIFIED**; out-of-window rejects surface as Case B.

Immediate vs scheduled status on create: read `Sid` and `Status` (`queued` / `accepted` / `scheduled`, etc.).

### Cancel scheduled + redact body (`UpdateMessage`)

Remarks (map + XML): “used to redact Message `body` text and to cancel not-yet-sent messages.”

| Intent | Args | Notes |
|---|---|---|
| Cancel follow-up | `accountSid`, `sid` (provider SID), `body: null` (omit), `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`) | Only update-status enum member. Which current statuses are cancellable is **not** in the map/XML — **UNVERIFIED**. If already sent, expect Case B; do not fail the order cancel. Success: returned `Status` should be `MessageEnumStatus.Canceled`. |
| Redact content | `accountSid`, `sid`, `body: ""` (empty string; `null` omits the field and will not redact), `status: null` | Subsequent `FetchMessage`: `Body` empty or null (**UNVERIFIED**); `Status` / `Sid` / `ErrorCode` remain. **Do not** `DeleteMessage`. |

### Idempotency / resend (`CreateMessage`)

- **No first-class `idempotencyKey` parameter** on `CreateMessage` (unlike `CreatePayments` / `CreateUserDefinedMessage`).
- Source always sends header **`Idempotency-Key: Guid.NewGuid()`** on `CreateMessage`, `UpdateMessage`, and `DeleteMessage`. That Guid is computed **once per method invocation** (HTTP retries of that invocation reuse it). A second application call always gets a new Guid.
- `RequestOptions` cannot set headers.
- Repeating under a caller-supplied key **cannot** be forwarded to the provider through this SDK. Fetch + recreate with the same body is a **new** provider message. See Blockers.

### Reconciliation (`ListMessage`)

Provider-side filters (do **not** list the whole account then filter in app):

| PublicApi | ListMessage arg | Wire |
|---|---|---|
| this app’s sending number `Twilio:FromNumber` | `from:` | `From` |
| range start (ISO-8601) | `dateSentQueryQuery:` | `DateSent>` |
| range end (ISO-8601) | `dateSentQuery:` | `DateSent<` |
| unused equality | `dateSent: null`, `to: null` | omit `DateSent`, `To` |
| paging | `pageSize:` (≤ 1000), `page:` / `pageToken:` | `PageSize`, `Page`, `PageToken` |

XML still describes GMT `YYYY-MM-DD` / `<=` / `>=` **in the value**; the generated client instead puts inequalities in the query **name** and formats values as UTC ISO-8601 with millis. Pass the PublicApi `DateTimeOffset`s; the SDK formats them.

Loop until `NextPageUri` is null. Align each `Messages[]` item (`Sid`, `Status`, `From`, `To`, `DateSent`, `Body`, `ErrorCode`) with eShop’s stored provider SIDs.

### Enums (in-scope)

Build with static members or `FromValue("wire")`. These are `StringEnum<T>`, **not** C# enums (`map/models/enums.md`).

| Type | Namespace | Members (C# = wire) |
|---|---|---|
| `MessageEnumStatus` | `TwilioSdk.Models.Enums` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | same | `Canceled (canceled)` |
| `MessageEnumScheduleType` | same | `Fixed (fixed)` |
| `MessageEnumDirection` | same | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumContentRetention` | same | `Retain (retain)`, `Discard (discard)` — pass `null` unless needed |
| `MessageEnumAddressRetention` | same | `Retain (retain)`, `Obfuscate (obfuscate)` — pass `null` unless needed |
| `MessageEnumTrafficType` | same | `Free (free)` — pass `null` |
| `MessageEnumRiskCheck` | same | `Enable (enable)`, `Disable (disable)` — pass `null` |
| `ValidationError` | same | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `Field` | same | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — use `.Value` inside the `fields` string |
| `LineType` | same | `Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)` — **different model** (“override the original line type”); **not** the type of `LineTypeIntelligenceInfo.Type` |
| `ServerEnvironment` | `TwilioSdk.Servers` | `Production (production)` |

Delivery outcomes used in-app: `queued` / `sending` / `sent` / `delivered` = in flight or succeeded; `failed` / `undelivered` = did not reach the shopper (carrier refusal after accept is expected for some US numbers — read from fetch, not from create throwing). `scheduled` / `canceled` = schedule lifecycle. `accepted` = accepted by Twilio. Create **accepting** a message is not a gap when the carrier later refuses.

### Errors (all in-scope ops = Case B)

Catch type: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` (`Core/Exceptions/SdkException.cs` — `required TError Error`).

`RawError` (`Core/ErrorResponse/RawError.cs`): `HttpStatusCode StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. **No** `TryGet…` typed accessors (those are Case A only). `TryGetRawError` is on `ApiError`, not on `RawError`.

| Situation | How it appears | App behavior |
|---|---|---|
| Invalid / unusable number at lookup | `Valid != true` / `ValidationErrors` **or** Case B (e.g. 4xx on lookup) | **Fail** `POST /api/contact-numbers` |
| Auth failure | Case B, `StatusCode` 401/403; body via `ReadAsString()` | Fail lookup registration; **do not** fail order ops — log and continue |
| Create/update/fetch/list send-path failure | Case B | Log; **do not** fail the order |
| Carrier undeliverable after accept | HTTP 2xx create; later `FetchMessage` `Status` `failed`/`undelivered` + `ErrorCode` | Expected; not an SDK gap |
| `JsonException` | See REQUIRED READING — not an `SdkException` | Error boundary must include it |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` vs `TwilioSdkClient` lifetime and `AddTwilioSdkClient` ownership are not obvious from the constructor. Getting this wrong leaks handlers or rebuilds the pipeline per request. **MUST load `dotnet-client-initialization`** before writing the factory/DI registration.

⚠ Step 1 (auth) — `AccountSidAuthToken` must be set on options before the client exists; SID/token are secrets and 401/403 follow from this property, not from a different scheme. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (BaseUrl / retries / paging) — `Retry` / `Timeout` on options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `Server.Default` and `Server.Default4` are different hosts; `ListMessage` has no SDK iterator. **MUST load `dotnet-configuration-resilience`** before registering the client, setting `Twilio:BaseUrl`, or paging reconciliation.

⚠ Steps 2–9 (calls) — `CreateMessage` / `FetchPhoneNumber3` / `ListMessage` have long nullable parameter lists with **no C# defaults**; a positional call mis-binds; the token is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 2–9 (models) — statuses and schedule type are `StringEnum<T>` (not C# enums); `LookupResponse` / `ApiV2010AccountMessage` members are nullable; unmodeled JSON is dropped. **MUST load `dotnet-models`** before mapping fields or comparing status.

⚠ Step 10 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`); Case A `TryGet…` accessors do not exist on these errors. A message send must not fail the order; a lookup failure must fail registration. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

⚠ Tests — the `HttpClient` constructor argument is the test seam; do not fake SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `TwilioSdkClient`, `HttpClient` ownership |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 / 9 — retries, timeouts, `Server.Default` vs `Default4`, `ListMessage` paging |
| `dotnet-calling-endpoints` | Steps 2–9 — named arguments, `ct`, must-pass nullables |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, request/response records, nullability |
| `dotnet-error-handling` | Step 10 — Case B `SdkException<RawError>`, status/body accessors, and both `JsonException` paths below |
| `dotnet-testing` | Tests — `HttpClient` seam |

`JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

### Assumptions

- Phone lookup uses **Lookups v2** `FetchPhoneNumber3`, not v1 (`FetchPhoneNumber2` has no typed `Valid` / E.164 validity flag).
- “Usable destination” at registration = lookup succeeds **and** `Valid == true` **and** `ValidationErrors` is empty. `LineTypeIntelligence.Type` is a raw `string?`; this sheet does not invent a Type allow-list. If intelligence is present with `ErrorCode` set, treat the number as not confirmed SMS-capable and reject. If intelligence is absent because `fields` was omitted, that is an implementation defect (always request `line_type_intelligence`).
- Immediate SMS always sets `from` to `Twilio:FromNumber` so `ListMessage(from:)` is the provider-side scope for reconciliation. Scheduled SMS also sets `messagingServiceSid` because `MessageEnumScheduleType` is documented as Messaging Services only.
- `Twilio:BaseUrl` overrides **only** the messaging API host (`Server.Default.Production.BaseUrl`). Lookups stay on `lookups.twilio.com` unless a future requirement says otherwise.
- `statusCallback` is always `null` (no publicly reachable URL).
- Reconciliation date range maps to `DateSent>` / `DateSent<` (messages with a send timestamp). Unsent `scheduled` rows may not appear in that filter — **UNVERIFIED**.
- After redaction, treat `Body` null or empty as “content gone at the provider”; `Status` is left unchanged because `status` is omitted.
- Live account; messages cost money; Auth Token is a secret; destinations are `TWILIO_TEST_TO_NUMBER` / `TWILIO_UNREACHABLE_TO_NUMBER` only in tests.

### Blockers / gaps

1. **Caller-supplied idempotency key (capability 6) — not exposed.** `CreateMessage` has no idempotency parameter. The SDK always injects `Idempotency-Key: Guid.NewGuid()`. `RequestOptions` cannot add or override headers. Provider-side “same key must not send a second message” **cannot** be implemented through this SDK. A fresh application call is always a new provider message. Do not invent a header workaround.
2. **Schedule min/max window — not in the SDK map or `CreateMessage` XML.** Parameter exists (`sendAt` / wire `SendAt`, timezone via `DateTimeOffset`). Bounds are **UNVERIFIED**. Out-of-range values: Case B `SdkException<RawError>`; extract `StatusCode` + `ReadAsString()` best-effort.
3. **Cancellable statuses — not enumerated.** Only `MessageEnumUpdateStatus.Canceled` exists. Remarks say “not-yet-sent.” Which `MessageEnumStatus` values the provider will cancel is **UNVERIFIED**; a refusal is Case B.
4. **`LineTypeIntelligenceInfo.Type` is `string?`, not `LineType`.** SMS-capable vs landline is not a generated enum on that field. `LineType` belongs to a different model. Do not cast `Type` to `LineType`.
5. **Post-redact `Body` null vs `""` on a later fetch — UNVERIFIED** (live wire). Defensive: treat null or empty as redacted; do not require a specific spelling.
