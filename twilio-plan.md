# Twilio SMS order notifications — plan & contract sheet

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdk.TwilioSdkClient`. Map stamp: `51fdf48`.

## Scope & sequence

1. **Client + config bind** — construct `TwilioSdkClient` with `AccountSid` + `AuthToken`; optional `Twilio:BaseUrl` on the **2010 Messages** host only (`options.Server.Default`); bind `FromNumber` + `MessagingServiceSid`. No operations yet.
2. **Register mobile number** (`POST /api/contact-numbers`) — `LookupsV2PhoneNumber.FetchPhoneNumber3`; store `LookupResponse.PhoneNumber` (E.164); reject when not a usable destination.
3. **Send SMS now** — `Api20100401Message.CreateMessage` with `from` / `messagingServiceSid` / `body` / `to`; persist `Sid` + `Status`.
4. **Queue follow-up with the provider** — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`; persist scheduled `Sid`; confirm `Status == Scheduled`.
5. **Cancel follow-up on order cancel** — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
6. **Status refresh (no webhooks)** — `Api20100401Message.FetchMessage` by provider `Sid`; map `Status` / `ErrorCode` / `ErrorMessage`. US `undelivered`/`failed` after accept is an **expected** outcome, not a gap.
7. **Redact body at provider** (`DELETE /api/notifications/{id}/content`) — `Api20100401Message.UpdateMessage` with `body: ""`; subsequent `FetchMessage` for remaining `Body`.
8. **Reconciliation** (`GET /api/notifications/reconciliation?from=&to=`) — `Api20100401Message.ListMessage` with `from` = configured `Twilio:FromNumber` and the date-range params; paginate; align on `Sid`/`From`/`To`/`Status`/`DateSent`/`Body`.
9. **Error boundary** — every in-scope operation is Case B (`SdkException<RawError>`); classify invalid-number / not-found / cancel-refused / auth from `RawError.StatusCode` + body.

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

### Client construction / auth / messaging-only BaseUrl

| Fact | Value | Cite |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` — both required; SDK does **not** own `HttpClient` | `sdk-map.md` (*Getting a client*), `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`); `Default()` → `Production` | `sdk-map.md` (*Servers & auth*), `Servers/ServerEnvironment.cs` |
| Auth | `options.AccountSidAuthToken = new BasicAuthCredentials { Username = AccountSid, Password = AuthToken }` — both members `required string`. Scheme is HTTP Basic. | `sdk-map.md` (*Servers & auth*), `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Messaging host (send/read/reconcile) | `Api20100401Message` calls `_server.Default(...)` → `TwilioSdk.Servers.DefaultOptions.Production.BaseUrl`, default `"https://api.twilio.com"` | `operations/Api20100401Message.md` *(Default (api))*, `Servers/DefaultOptions.cs`, `Server.cs` |
| `Twilio:BaseUrl` | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` **before** constructing the client. Do **not** set `Default1`…`Default14`. Lookups stay on `Default4`. | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Lookups host | `LookupsV2PhoneNumber` / `LookupsV1PhoneNumberApi` use `_server.Default4(...)` → `TwilioSdk.Servers.Default4Options.Production.BaseUrl`, default `"https://lookups.twilio.com"` — **not** governed by `Twilio:BaseUrl` | `operations/LookupsV2PhoneNumber.md` *(Default4 (lookups))*, `Servers/Default4Options.cs` |
| Other hosts (do not touch) | e.g. `Default1` default `"https://messaging.twilio.com"` (Messaging Services admin API — **not** used by this integration’s send/read/reconcile) | `Servers/Default1Options.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No header bag, no idempotency property. | `Core/RequestOptions.cs` |

`TwilioSdk.ServerOptions` members (root namespace; `ServerOptions.cs`): `Default`, `Default1` … `Default14` — each is a `TwilioSdk.Servers.DefaultNOptions` with nested `Production.BaseUrl`.

Config keys (values from env, never hard-coded): `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (optional).

---

### Operations

#### 1. Phone lookup / canonicalization / usability — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`

Use **Lookups v2**, not v1. v1 (`FetchPhoneNumber2` → `TwilioSdk.Models.LookupsV1PhoneNumber`) has canonical `PhoneNumber` but **no** `Valid` / `ValidationErrors` / typed line-type fields (`Carrier` is `object?`). v2 is the operation that can reject unusable destinations at registration. Cite: `operations/LookupsV2PhoneNumber.md`, `operations/LookupsV1PhoneNumberApi.md`, `records-4-Li-Me.md`.

| | |
|---|---|
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, no default → pass `null` to skip |
| Path | `phoneNumber` — E.164 or national; default country +1 if national (`LookupsV2PhoneNumber.cs` XML) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity/reassigned/prefill params (pass `null`) |
| `fields` | **`string?`**, comma-separated. XML possible values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. Pass `"line_type_intelligence"` (add `,line_status` if line-active check is wanted). This is **not** `TwilioSdk.Models.Enums.Field` — that enum is on a different request model. |
| Returns | `TwilioSdk.Models.LookupResponse` (no extra envelope) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |

**`LookupResponse` fields this step reads** (`records-4-Li-Me.md`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber` (`phone_number`) | `string?` | **Canonical E.164** (`+` + country + subscriber) — **this is what gets stored** |
| `NationalFormat` (`national_format`) | `string?` | National display form — do not store as the destination |
| `Valid` (`valid`) | `bool?` | `true` iff the number is in a range a carrier can assign to a user |
| `ValidationErrors` (`validation_errors`) | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid |
| `CallingCountryCode` (`calling_country_code`) | `string?` | E.164 prefix |
| `CountryCode` (`country_code`) | `string?` | ISO 3166-1 alpha-2 |
| `LineTypeIntelligence` (`line_type_intelligence`) | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus` (`line_status`) | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status` |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?`, `ErrorCode (error_code): int?`. **`Type` is an unconstrained `string?`** — not `TwilioSdk.Models.Enums.LineType` (that enum is `OverridesRequest.LineType`, a different resource).

**`LineStatusInfo`**: `Status (status): string?`, `ErrorCode (error_code): int?`. `Status` is an unconstrained `string?`. **UNVERIFIED** live values.

**Usable-destination decision (from these fields only):**
- Reject when `Valid` is not `true`, or `ValidationErrors` is non-empty.
- Reject when the call throws `SdkException<RawError>` (invalid/unusable at the HTTP layer — inspect `StatusCode`; **UNVERIFIED** whether a garbage number is 404 vs 2xx+`Valid=false`; extract best-effort from `ReadAsString()`/`ReadAsJson<T>()`, fall back to a generic rejection).
- When `line_type_intelligence` was requested: reject if `LineTypeIntelligence` is null, `ErrorCode` is set, or `Type` is null/empty — that is not a confirmed usable SMS destination. Mapping of `Type` strings onto “SMS-capable” is **UNVERIFIED** (do not bind `Type` to `LineType`).
- Store `PhoneNumber` (E.164), never the caller’s raw input.

**`ValidationError`** (`enums.md`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

---

#### 2. Send now / schedule follow-up — `client.Api20100401Message.CreateMessage`

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) — **form-urlencoded body**, not JSON |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required path/body | `accountSid` (`string`), `to` (`string`) |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — nullable, no default → pass `null` to skip. Null form fields are omitted on the wire. |
| Form fields (wire ← C#) | `To` ← `to`, `From` ← `from`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the other listed params |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (no extra envelope) |
| Error | **Case B** `SdkException<RawError>` — same accessors as above |
| Pagination | none |

**Immediate send:** `accountSid` = `Twilio:AccountSid`; `to` = stored E.164; `from` = `Twilio:FromNumber`; `messagingServiceSid` = `Twilio:MessagingServiceSid` (config key is required to be bound; SDK param is optional `string?`); `body` = text; all other optionals `null`. Both `from` and `messagingServiceSid` may be sent together (SDK emits any non-null field). **UNVERIFIED** provider precedence when both are present — still pass `from` so reconciliation `ListMessage(from:)` can request this app’s number.

**Schedule follow-up (provider-held, not app-queued):** same call with `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset a few days later>` (app computes the instant; SDK has no “delay duration” param). Enum XML: *“For Messaging Services only”* — scheduled send **must** pass `messagingServiceSid`. Identifier of the scheduled message = response `Sid`. Scheduled vs sent = response `Status`.

**Idempotency (caller-supplied key):** **not supported — see Blockers.** `CreateMessage` always attaches header `Idempotency-Key: Guid.NewGuid()` inside the generated method. `RequestOptions` cannot override it. Repeating a call always mints a new key.

---

#### 3. Cancel scheduled follow-up — `client.Api20100401Message.UpdateMessage`

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) — form-urlencoded |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` — pass `null` on the field you are not changing |
| Form (wire ← C#) | `Body` ← `body`, `Status` ← `status` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Cancel call | `body: null`, `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`) |

**Success vs already-sent:** success = method returns; read `Status`. Cancelled means `Status == MessageEnumStatus.Canceled`. Already sent / not cancellable = throws `SdkException<RawError>` — **UNVERIFIED** which `StatusCode` means already-sent vs other refusal. Defensive: on exception, read `StatusCode` + `ReadAsString()`/`ReadAsJson<T>()` best-effort; `NotFound` (404) = message not found; any other error status = cancel refused. Confirm with `FetchMessage`: if `Status` is no longer `Scheduled`, it has already gone out (or failed/undelivered). Do **not** use `DeleteMessage` to cancel — that deletes the resource.

`DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void` / Case B. Out of scope for cancel and for redaction (it removes the resource, not just the body). Cite: `operations/Api20100401Message.md`.

---

#### 4. Fetch by provider id — `client.Api20100401Message.FetchMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` (404 → not found — confirm via `StatusCode`) |

---

#### 5. Redact / dispose body — `client.Api20100401Message.UpdateMessage`

Same signature as cancel. Redact call: `body: ""` (empty string **is** sent; `null` omits the field), `status: null`.

Subsequent `FetchMessage`: read `Body`. **UNVERIFIED** whether the live wire returns `""` or `null` after redaction. Defensive: treat null **or** empty as “body no longer retrievable”. `Sid`, `Status`, `DateSent`, `From`, `To` remain on the resource (that is why this is Update, not Delete). Cite: `operations/Api20100401Message.md` notes, `ParameterFlattener` null-omit vs empty-string send.

---

#### 6. List for reconciliation — `client.Api20100401Message.ListMessage`

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `to` … `pageToken` |
| Query (wire ← C#) | `To` ← `to`, **`From` ← `from`**, `DateSent` ← `dateSent`, **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date serialization | SDK sends `DateTimeOffset` as ISO-8601 `yyyy-MM-ddTHH:mm:ss.fffZ` (UTC) via `ToIso8601()` | `Api20100401Message.cs`, `Core/Extensions/DateTimeOffsetExtensions.cs` |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **No SDK pager.** `page` / `pageSize` / `pageToken` only (`pageSize` default 50, max 1000 per XML). Walk `NextPageUri` / `pageToken` until `NextPageUri` is null. |

**Ask the provider for this app’s From number** — pass `from: Twilio:FromNumber` (do not list the whole account then filter). Date window: `dateSentQueryQuery` = range start (`DateSent>`), `dateSentQuery` = range end (`DateSent<`), `dateSent` = `null` (exact-day filter unused), `to` = `null`.

**`ListMessageResponse` envelope** (`records-4-Li-Me.md`): `End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, **`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`**.

---

### Response model — `TwilioSdk.Models.ApiV2010AccountMessage`

Cite: `records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`. All members optional (`T?`). No wrapper field — the record **is** the payload.

| C# (wire) | Type | Integration use |
|---|---|---|
| `Sid` (`sid`) | `string?` | Provider message id (`SM…` / `MM…`) |
| `Status` (`status`) | `MessageEnumStatus?` | Delivery / schedule outcome |
| `From` (`from`) | `string?` | Sender E.164 |
| `To` (`to`) | `string?` | Destination E.164 |
| `Body` (`body`) | `string?` | Text; empty/null after redact |
| `DateSent` (`date_sent`) | `string?` | RFC 2822 GMT **string**, not `DateTimeOffset` |
| `DateCreated` (`date_created`) | `string?` | RFC 2822 GMT string |
| `DateUpdated` (`date_updated`) | `string?` | RFC 2822 GMT string |
| `MessagingServiceSid` (`messaging_service_sid`) | `string?` | `MG…` |
| `AccountSid` (`account_sid`) | `string?` | `AC…` |
| `ErrorCode` (`error_code`) | `int?` | Set when `failed` / `undelivered` |
| `ErrorMessage` (`error_message`) | `string?` | Companion text; XML: do not use code/message programmatically |
| `Direction` (`direction`) | `MessageEnumDirection?` | Outbound API → `OutboundApi` (`outbound-api`) |
| `NumSegments` (`num_segments`) | `string?` | Messaging Service: initially `"0"` until sender assigned |
| `NumMedia` (`num_media`) | `string?` | |
| `Price` (`price`) / `PriceUnit` (`price_unit`) | `string?` | |
| `Uri` (`uri`) / `ApiVersion` (`api_version`) / `SubresourceUris` (`subresource_uris`) | | unused |

### Enums actually needed (`map/models/enums.md`)

Enums are `TwilioSdk.Models.Enums.*` : `StringEnum<T>` — compare with `==` against static members or `FromValue("wire")`. **Not** C# enums.

**`MessageEnumStatus`** — delivery / schedule:

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

US carrier-refuse-after-accept → expect `Undelivered` or `Failed` on a later `FetchMessage` (often after `Accepted`/`Queued`/`Sent`). That is an expected outcome.

**`MessageEnumScheduleType`:** `Fixed (fixed)` only.

**`MessageEnumUpdateStatus`:** `Canceled (canceled)` only.

**`MessageEnumDirection`:** `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

**`MessageEnumContentRetention`:** `Retain (retain)`, `Discard (discard)` — optional on create; redaction of an already-sent body is `UpdateMessage`, not this flag.

### MessagingServiceSid vs From

| | |
|---|---|
| Send param | `messagingServiceSid` (`string?`), wire `MessagingServiceSid` |
| From param | `from` (`string?`), wire `From` |
| Bind | Both config keys are required to be bound. Neither is required by the C# signature. |
| Immediate SMS | Pass `from` = `Twilio:FromNumber` (needed so list-by-From matches). Also pass bound `messagingServiceSid`. |
| Scheduled SMS | `MessageEnumScheduleType` XML: Messaging Services **only** — pass `messagingServiceSid` + `scheduleType: Fixed` + `sendAt`. |

### Errors (all in-scope ops are Case B)

Thrown type: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. `RawError` is **not** an `ApiError` — **no** `TryGet…` / `TryGetRawError`. Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. No-throw `…Result` variants: **absent**.

There is **no** generated per-operation error record for these calls. Wire error JSON shape is **UNVERIFIED**. Defensive: `ReadAsJson<T>()` best-effort into a local DTO if one is defined; on failure use `ReadAsString()`; if that is empty, a generic message. Do not parse `Exception.ToString()`.

| Situation | How to classify |
|---|---|
| Invalid / unusable number (lookup) | 2xx + `Valid != true` / non-empty `ValidationErrors`, **or** `SdkException<RawError>` from `FetchPhoneNumber3` |
| Invalid destination on send | `CreateMessage` throws `SdkException<RawError>` — inspect `StatusCode` + body (**UNVERIFIED** codes) |
| Message not found | `FetchMessage` / `UpdateMessage` / `DeleteMessage` → `StatusCode == HttpStatusCode.NotFound` |
| Already sent (cancel) | `UpdateMessage` throws; **UNVERIFIED** status; confirm with `FetchMessage.Status != Scheduled` |
| Auth failure | `StatusCode == HttpStatusCode.Unauthorized` (401) or `Forbidden` (403) on any op |
| US undeliverable | **Not an exception.** Later `FetchMessage`/`ListMessage` `Status` is `Undelivered`/`Failed` with `ErrorCode`/`ErrorMessage`. Expected. |

`JsonException` can still reach the boundary (see Trap notes) even though these payloads have no `required` members.

---

## Trap notes

⚠ Step 1 (client registration) — the SDK does not own the `HttpClient` you pass, and the DI extension’s lifetime decides whether handler rotation ever reaches this client. **MUST load `dotnet-client-initialization`** before constructing or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — credentials are a nullable options property that must be set before construct; loading secrets from configuration vs hard-coding, and what happens on 401/403, are not in the signature. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

⚠ Step 1 (BaseUrl / retries / timeouts) — `Twilio:BaseUrl` is nested per-server **and** per-environment; `Environment` vs `Server` are not read on the same schedule; SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed `CreateMessage`/`UpdateMessage` write can be re-sent is not visible from the method signature. **MUST load `dotnet-configuration-resilience`** before setting `Server.Default.Production.BaseUrl` or `Retry`.

⚠ Steps 2–8 (every call) — 8–24 nullable parameters have **no C# default** and mis-bind in a positional call; the token parameter is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `CreateMessage` / `FetchPhoneNumber3` / `ListMessage`.

⚠ Steps 2–8 (models / enums) — `MessageEnumStatus` and friends are `StringEnum<T>`, not C# enums; response `DateSent` is a `string?`; unmodeled JSON is dropped. **MUST load `dotnet-models`** before mapping `ApiV2010AccountMessage` / `LookupResponse`.

⚠ Step 9 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>` with no `TryGet…`); `TryGetRawError` is not a catch-all; a single-status catch ladder will mis-classify not-found vs auth vs cancel-refused. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ Step 9 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 9 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the constructor `HttpClient` is the test seam; matching the repo’s framework/assertions is not in the SDK surface. **MUST load `dotnet-testing`** before stubbing the client.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, `HttpClient` ownership/lifetime, `AddTwilioSdkClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `Server.Default.Production.BaseUrl`, retries, timeouts, list pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, `ct`, envelopes |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, wire names, nullability |
| `dotnet-error-handling` | Step 9 — Case B `RawError`, catch ladder, both `JsonException` paths |
| `dotnet-testing` | Tests — `HttpClient` seam |

---

## Assumptions & Blockers

**Assumptions**
- Lookups **v2** (`FetchPhoneNumber3`) is the registration lookup; v1 cannot decide usability (`Valid` / `ValidationErrors` / typed line-type are absent).
- Follow-up delay (“a few days later”) is computed by the application as `DateTimeOffset` and passed to `sendAt`; the SDK has no duration/delay parameter.
- Reconciliation `from`/`to` query params are ISO-8601 instants mapped to `dateSentQueryQuery` (`DateSent>`) and `dateSentQuery` (`DateSent<`), with `ListMessage.from` = `Twilio:FromNumber`.
- Redaction is `UpdateMessage` with empty `body`, not `DeleteMessage`.
- US undeliverability after accept is expected (`Undelivered`/`Failed` on fetch/list), not a planning gap.

**Blockers**
- **Caller-supplied idempotency key is not in this SDK.** `CreateMessage` always sends `Idempotency-Key: {Guid.NewGuid()}` (`Api/Api20100401Message.cs`). `TwilioSdk.Core.RequestOptions` exposes only `LogLevel`. There is no create parameter for a caller key. Repeating a send under an application key **cannot** be made to collapse to one provider message through this client. Do not invent a workaround (custom headers, wrapping HttpClient to rewrite the generated header, etc.). Immediate send without resend-idempotency remains available.
