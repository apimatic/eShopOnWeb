# Twilio .NET SDK — eShopOnWeb integration plan + contract sheet

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: **`TwilioSdk`** (not `Twilio`). Client: `TwilioSdk.TwilioSdkClient`. Map provenance: commit `51fdf48`.

## Scope & sequence

1. **Client construction** — bind `Twilio:` settings; construct `TwilioSdkClient` with AccountSid+AuthToken; apply optional messaging-only `BaseUrl` on server **Default**.
2. **Phone validation / canonicalization** — `LookupsV2PhoneNumber.FetchPhoneNumber3` before `POST /api/contact-numbers` persist.
3. **Send SMS** (placed / dispatched / cancelled / resend) — `Api20100401Message.CreateMessage` (`from` + `body` + `to`); catch send failures so the order operation still succeeds.
4. **Schedule follow-up SMS** (dispatch) — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`.
5. **Cancel scheduled follow-up** (cancel-order) — `Api20100401Message.UpdateMessage` with `status: Canceled`.
6. **Fetch delivery outcome** — `Api20100401Message.FetchMessage` by SID (no webhooks).
7. **Operator resend + caller idempotency key** — `CreateMessage` again; see Blockers (SDK does not accept a caller-supplied idempotency key).
8. **Redact content** — `Api20100401Message.UpdateMessage` with empty `body` (do **not** `DeleteMessage`).
9. **Reconciliation list** — `Api20100401Message.ListMessage` with `from` = `Twilio:FromNumber` and date-range filters; page the whole range.
10. **Error boundary** — Case B `SdkException<RawError>` on every in-scope call, plus the `JsonException` paths in REQUIRED READING.

Do **not** use `DeleteMessage` for cancel or redact (it deletes the Message resource). Do **not** use Lookup v1 (`FetchPhoneNumber2`) — v2 returns typed `Valid` + E.164 `PhoneNumber`.

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

No-throw `…Result` variants: **absent** on every operation below. All are throw-only.

---

### Client construction, auth, servers, HttpClient

| Item | Contract | Cite |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` — `httpClient` is required | `sdk-map.md` *Getting a client* |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdk.TwilioSdkClientOptions>? configure = null)` — registers `TwilioSdkClient` as singleton; calls `services.AddHttpClient()` and `IHttpClientFactory.CreateClient()` (unnamed) | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment`; `Retry: TwilioSdk.Core.Configuration.RetryOptions`; `Logging: TwilioSdk.Core.Configuration.LoggingOptions`; `Server: TwilioSdk.ServerOptions`; `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` / `TwilioSdkClientOptions.cs` |
| Environment | Only `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). Default is Production. | `sdk-map.md` *Servers & auth*; `Servers/ServerEnvironment.cs` |
| Auth credentials | `new BasicAuthCredentials { Username = accountSid, Password = authToken }` on `options.AccountSidAuthToken`. Scheme is HTTP Basic (`Authorization: Basic …`). Map XML: AccountSid+AuthToken **or** API key as username + secret as password. | `sdk-map.md` *Servers & auth*; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Bind `Twilio:` | `AccountSid` → credentials `Username` **and** every operation’s `accountSid` path param; `AuthToken` → credentials `Password`; `FromNumber` → Create `from` + List `from`; `MessagingServiceSid` → Create `messagingServiceSid` (required for schedule); `BaseUrl` → **only** `options.Server.Default.Production.BaseUrl` | this sheet |
| Messaging base URL | In-scope send/fetch/update/list use server **Default**. Default production URL: `"https://api.twilio.com"`. Override: `options.Server.Default.Production.BaseUrl = twilioBaseUrl` **verbatim** when `Twilio:BaseUrl` is set. Joiner: `{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}`. | `Servers/DefaultOptions.cs`; `Server.cs`; `Api/Api20100401Message.cs` (`_server.Default(...)`) |
| Lookup host **not** governed | Lookup v2 uses server **Default4**, default `"https://lookups.twilio.com"`. Do **not** write `Twilio:BaseUrl` onto `Server.Default4`. | `Servers/Default4Options.cs`; `Api/LookupsV2PhoneNumber.cs` (`_server.Default4(...)`) |
| Other hosts (unused here) | `Server.Default1.Production.BaseUrl` defaults to `"https://messaging.twilio.com"` (Messaging Services REST, **not** the 2010 Messages resource this integration calls). Leave it alone. | `Servers/Default1Options.cs` |
| Per-request URL | `TwilioSdk.Core.RequestOptions` has **only** `LogLevel?: Microsoft.Extensions.Logging.LogLevel?`. No base-URL, no header bag. | `Core/RequestOptions.cs` |
| HttpClient `BaseAddress` | The SDK builds an **absolute** `Uri` from `ServerOptions` + path. `HttpClient.BaseAddress` does not select Default vs Default4. Do not use `BaseAddress` as the `Twilio:BaseUrl` mechanism. | `Core/UriFactory.cs`; `Core/TemplateParamsFactory.cs` |
| HttpClient ownership | Ctor takes an externally supplied `HttpClient`; DI uses `IHttpClientFactory`. | `sdk-map.md`; `ServiceCollectionExtensions.cs` |
| Logging knobs | `TwilioSdk.Core.Configuration.LoggingOptions`: `LoggerFactory`, `LogRequestHeaders`, `LogResponseHeaders`, `LogRequestBody`, `BodySizeLimit`, `LoggableContentTypes`, `RedactedHeaders`, `RedactedKeys`, `UnmaskHeaders`, `RedactionPlaceholder`. Constraint: never log auth token or shopper phone numbers. | `Core/Configuration/LoggingOptions.cs` |
| Retry type | `TwilioSdk.Core.Configuration.RetryOptions` — all members `required`; or `RetryOptions.Default()` / `RetryOptions.Disabled()`. | `sdk-map.md`; `Core/Configuration/RetryOptions.cs` |

⚠ Step 1 (client registration) — the constructor’s `HttpClient` argument does not say which object is long-lived, which is per-request, or whether the wrapper is safe as a singleton; getting this wrong exhausts sockets or shares handlers unsafely. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (authentication) — setting `AccountSidAuthToken` in the wrong place relative to construction, or hardcoding the token, leaks secrets and yields 401s that look like “the SDK is broken”. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (base URL / retries / logging) — `Retry`/`Timeout` on options are not the timeout on the `HttpClient` you register and do not bound “one business send”; Create is HTTP POST; request-body/header logging can emit `Authorization` and shopper `To`/`From`. **MUST load `dotnet-configuration-resilience`** before registering the client or turning logging on.

---

### 1. FetchPhoneNumber3 — validate + canonicalize (Lookup v2)

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · server **Default4** (`https://lookups.twilio.com`) · **`Twilio:BaseUrl` does not apply** |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 15 params `fields` … `partnerSubId` are nullable with **no C# default** — pass `null` to skip |
| Path | `phoneNumber` — caller-typed number; XML: E.164 or national; default country +1 |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity/reassigned/pre_fill params (pass `null`) |
| `fields` values (XML) | comma-separated: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill` |
| Returns | `TwilioSdk.Models.LookupResponse` — **no wrapper field** |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `map/operations/LookupsV2PhoneNumber.md`; `Api/LookupsV2PhoneNumber.cs`; `map/models/records-4-Li-Me.md` |

**`LookupResponse` fields this integration reads** (`CSharp (wire): type`):

| Field | Role |
|---|---|
| `PhoneNumber (phone_number): string?` | Provider canonical form — E.164 (`+` + country + subscriber). **Store this**, not the caller-typed input. |
| `Valid (valid): bool?` | XML: whether the number is in a valid range a carrier can assign to a user. Reject registration when `Valid` is not `true`. |
| `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid (see enum table). |
| `NationalFormat (national_format): string?` | National display form (do not persist as canonical). |
| `CountryCode (country_code): string?` | ISO 3166-1 alpha-2. |
| `CallingCountryCode (calling_country_code): string?` | International prefix. |
| `LineTypeIntelligence (line_type_intelligence): LineTypeIntelligenceInfo?` | Only populated if `fields` includes `line_type_intelligence`. `Type (type): string?` (plain string, **not** `LineType`). Also `CarrierName`, `MobileCountryCode`, `MobileNetworkCode`, `ErrorCode`. |
| `LineStatus (line_status): LineStatusInfo?` | Only if `fields` includes `line_status`. `Status (status): string?`, `ErrorCode (error_code): int?`. |

There is **no** `sms_capable` / SMS-capability boolean on `LookupResponse`. Usable-destination decision = `Valid == true` (and optionally inspect `LineTypeIntelligence.Type` / `LineStatus.Status` if those packages are requested). `TwilioSdk.Models.Enums.LineType` exists but is a **different** type (porting override); do not assign it to `LineTypeIntelligenceInfo.Type`.

**Invalid / not-found:** a 2xx body with `Valid == false` is a successful RPC — do not look for an exception. Non-2xx (including not-found) is Case B `SdkException<RawError>`. Which HTTP status the provider uses for a garbage number vs an unbillable Lookup package is **UNVERIFIED** — read `ex.Error.StatusCode` + `ReadAsString()`; fall back to a generic rejection message if JSON does not parse.

**v1 (out of scope):** `client.LookupsV1PhoneNumberApi.FetchPhoneNumber2` returns `LookupsV1PhoneNumber` with `Carrier: object?` (untyped) and **no** `Valid`. Do not use it.

Cite: `map/operations/LookupsV2PhoneNumber.md`, `map/models/records-4-Li-Me.md`, `map/models/records-3-Fl-Li.md` (`LineTypeIntelligenceInfo`, `LineStatusInfo`), `map/models/enums.md`.

---

### 2. CreateMessage — send SMS and schedule follow-up

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default** (`https://api.twilio.com`) · **`Twilio:BaseUrl` applies** |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 24 params `statusCallback` … `contentSid` — nullable, **no default** → pass `null` to skip. Only `requestOptions` defaults to null. |
| Form body (wire ← C#) | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid`. Null values are **omitted** from the form. |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **no wrapper field** |
| Error | **Case B** `SdkException<RawError>` (invalid number, 4xx, 5xx, 401/403 — all the same type) |
| Accessors | `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Cite | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs` |

**Immediate send (placed / dispatched notice / cancelled / resend):**

- `accountSid`: `Twilio:AccountSid`
- `to`: shopper canonical E.164 from Lookup
- `from`: `Twilio:FromNumber`
- `body`: notification text
- `messagingServiceSid`: `null` for immediate send **unless** you also send through the messaging service (see schedule row)
- all other optional params: `null`

**From vs MessagingServiceSid:** both are optional form fields (`from` / `messagingServiceSid`). Immediate send can use `from` alone. Scheduled send: `MessageEnumScheduleType` XML says it is **for Messaging Services only** — pass `messagingServiceSid: Twilio:MessagingServiceSid` together with `scheduleType` + `sendAt`. Passing `from` in addition is allowed by the signature (nulls omitted); whether the provider rejects a specific From+Service combination is **UNVERIFIED** — treat as Case B.

**Schedule (dispatch follow-up, queued at the provider):**

| Param | Value |
|---|---|
| `scheduleType` | `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) — only member |
| `sendAt` | `DateTimeOffset?` form field `SendAt`. Flattened via JSON-normalize (STJ default DateTimeOffset string), **not** the List filter `ToIso8601()` helper. Pass UTC. |
| `messagingServiceSid` | **Required by the schedule-type contract** (`MessageEnumScheduleType` XML: Messaging Services only; mentions `send_time` in prose — the generated field is `SendAt` / `sendAt`) |
| `to`, `body`, `accountSid` | same as immediate send |
| `from` | `Twilio:FromNumber` if the service should send from that number |

**Schedule window (min/max) and timezone rules beyond UTC `DateTimeOffset`:** not in the map row and not in CreateMessage XML. **UNVERIFIED.** An out-of-window request surfaces as Case B; extract status/body best-effort, generic message on parse failure.

**Response fields to persist:**

| Field | Wire | Type | Use |
|---|---|---|---|
| `Sid` | `sid` | `string?` | Provider message id (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`) |
| `Status` | `status` | `MessageEnumStatus?` | Immediate: typically queued/accepted; scheduled: `Scheduled` (`scheduled`) |
| `ErrorCode` / `ErrorMessage` | `error_code` / `error_message` | `int?` / `string?` | On failed/undelivered **after** send — XML: do not use programmatically as a stable API |
| `MessagingServiceSid` | `messaging_service_sid` | `string?` | Echo of service |
| `From` / `To` / `Body` | `from` / `to` / `body` | `string?` | |

**Create does not throw for later carrier refusal.** A 2xx `CreateMessage` with later `undelivered`/`failed` on Fetch is the expected US live-account outcome — not a Create exception.

**Idempotency-Key (capability 7):** CreateMessage **always** sends header `Idempotency-Key` with `Guid.NewGuid()` (new value every invocation). There is **no** method parameter for a caller key. `RequestOptions` cannot set headers. See Assumptions & Blockers. What Twilio would return on a true replay of the **same** key (same SID vs error) is **UNVERIFIED** and unreachable through this public surface.

⚠ Step 3 (CreateMessage) — 24 optional parameters have no C# default; a positional call binds the wrong arguments. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Step 3 (CreateMessage POST) — a transport failure can cause the write to run more than once even when status-code retries exclude POST; a duplicate SMS vs a lost send is the cost. **MUST load `dotnet-configuration-resilience`**.

---

### 3. UpdateMessage — cancel scheduled + redact body

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · server **Default** · **`Twilio:BaseUrl` applies** |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `body` and `status` — nullable, no default → pass `null` to skip that field (omitted from form) |
| Form (wire ← C#) | `Body` ← `body`, `Status` ← `status` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — no wrapper |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs` |

**Cancel not-yet-sent follow-up:** `sid` = stored provider SID; `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`); `body: null`. Identify only by SID (no other lookup key on this operation). Canceled delivery outcome on later Fetch: `MessageEnumStatus.Canceled` (wire `canceled`).

Already-sent / already-delivered: XML only says the operation is used to cancel **not-yet-sent** messages. HTTP status / error code for “already sent” is **UNVERIFIED** — Case B; extract `StatusCode` + body best-effort. Whether a second cancel of an already-`canceled` SID is idempotent is **UNVERIFIED** (same Case B path).

**Redact content:** `body: ""` (empty string); `status: null`. Field cleared is `Body` / wire `body`. Returns the same `ApiV2010AccountMessage`; `Sid` and `Status` remain on the record so Fetch-by-SID still works. Whether post-redact `Body` is `""` vs `null` is **UNVERIFIED** — treat empty-or-null body as redacted. Do **not** call `DeleteMessage` (that deletes the resource, which removes the delivery record this app must keep).

UpdateMessage also auto-sends `Idempotency-Key: Guid.NewGuid()` (not caller-controlled).

---

### 4. FetchMessage — current delivery outcome

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · server **Default** · **`Twilio:BaseUrl` applies** |
| Signature | `FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — no wrapper |
| Error | **Case B** `SdkException<RawError>` (missing SID → this type; status **UNVERIFIED**, commonly 404) |
| Cite | `map/operations/Api20100401Message.md` |

**`ApiV2010AccountMessage` fields to read** (all optional on the record):

| C# | Wire | Type | Notes |
|---|---|---|---|
| `Sid` | `sid` | `string?` | Provider id |
| `Status` | `status` | `MessageEnumStatus?` | See enum table — `Undelivered`/`Failed` are expected live US outcomes |
| `ErrorCode` | `error_code` | `int?` | Set when status is failed/undelivered; XML: not a stable programmatic contract |
| `ErrorMessage` | `error_message` | `string?` | Same caveat |
| `Body` | `body` | `string?` | Content; empty/null after redact |
| `From` | `from` | `string?` | E.164 / sender |
| `To` | `to` | `string?` | E.164 destination |
| `DateCreated` | `date_created` | `string?` | RFC 2822 GMT **string**, not `DateTimeOffset` |
| `DateSent` | `date_sent` | `string?` | RFC 2822 GMT string; outgoing = when Twilio sent |
| `DateUpdated` | `date_updated` | `string?` | RFC 2822 GMT string |
| `Direction` | `direction` | `MessageEnumDirection?` | API sends → `OutboundApi` (`outbound-api`) |
| `MessagingServiceSid` | `messaging_service_sid` | `string?` | |
| `AccountSid` | `account_sid` | `string?` | |

**No `DateScheduled` / `SendAt` on the resource.** Scheduled time is create-time input only; until send, `Status` is `Scheduled`. No inbound webhook alternative is required — Fetch by SID is the outcome API.

---

### 5. ListMessage — reconciliation for this app’s FromNumber

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default** · **`Twilio:BaseUrl` applies** (messaging-API call) |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 8 params `to` … `pageToken` — nullable, no default |
| Query (wire ← C#) | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent` (**ISO-8601 via `ToIso8601()`**), `DateSent<` ← `dateSentQuery` (ISO-8601), `DateSent>` ← `dateSentQueryQuery` (ISO-8601), `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| ISO-8601 format (list filters only) | UTC `yyyy-MM-ddTHH:mm:ss.fff'Z'` (`DateTimeOffsetExtensions.ToIso8601`) |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| SDK auto-pager | **none** (`page` / `pageSize` / `pageToken` only; map: “Pagination: none (only `page`, no `perPage`)”) |
| Cite | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs`; `map/models/records-4-Li-Me.md` |

**Scope to this app’s number at the API:** set `from: Twilio:FromNumber` (wire `From`). XML: “Filter by sender… retrieve a list of Message resources sent by {number}”. Pass `to: null`. Do not list the whole account and filter in-process.

**Date range for `GET /api/notifications/reconciliation?from=&to=`:**

| App bound | C# arg | Wire | Meaning in code |
|---|---|---|---|
| `from` (after) | `dateSentQueryQuery` | `DateSent>` | greater-than |
| `to` (before) | `dateSentQuery` | `DateSent<` | less-than |
| unused | `dateSent` | `DateSent` | equality on a single sent date — pass `null` for a range |

XML comments on all three date params are copy-pasted (`YYYY-MM-DD`, `<=`, `>=`) and **disagree** with the generated wire names (`DateSent<` / `DateSent>` without `=`) and with the serializer (full ISO-8601 UTC, not date-only). Trust the wire names + `ToIso8601()`. Whether the provider treats `<`/`>` as exclusive of the exact instant is **UNVERIFIED**.

**`ListMessageResponse` envelope:**

| C# | Wire | Type |
|---|---|---|
| `Messages` | `messages` | `IReadOnlyList<ApiV2010AccountMessage>?` — items have the same shape as Fetch |
| `NextPageUri` | `next_page_uri` | `string?` — stop when null |
| `PreviousPageUri` / `FirstPageUri` / `Uri` | `previous_page_uri` / `first_page_uri` / `uri` | `string?` |
| `Page` / `PageSize` / `Start` / `End` | `page` / `page_size` / `start` / `end` | `int?` |

**Paging the whole range:** XML: `pageSize` default 50, max 1000; `page` is “client state”; `pageToken` “is provided by the API”. Re-call `ListMessage` with the **same** `from` + date filters, advancing `pageToken` (and/or `page`) until `NextPageUri` is null. The envelope has no `PageToken` property — the token lives in the provider’s next-page URI. **MUST load `dotnet-configuration-resilience`** before writing that loop (dropping filters or stopping at page 0 under-counts the report).

---

### Enums in scope (`TwilioSdk.Models.Enums`, `StringEnum<T>` — not C# enums)

Build with static members or `Type.FromValue("wire")`. Compare via the member / `.Value`.

| Type | Members (C# = wire) | Cite |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `map/models/enums.md` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` only | same |
| `MessageEnumScheduleType` | `Fixed (fixed)` only | same |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | same |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — create-time retention; **not** the redact operation | same |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` | same |
| `MessageEnumTrafficType` | `Free (free)` | same |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` | same |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | same |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — Lookup **batch** model uses this; FetchPhoneNumber3 `fields` is a **`string?`** | same |

⚠ Step 2–9 (models) — these are `StringEnum<T>`, not C# enums; `new` is wrong; unmodeled JSON members are dropped on deserialize. **MUST load `dotnet-models`** before constructing requests or mapping responses.

---

### Errors (every in-scope operation is Case B)

Thrown type: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` (`sealed`, property `Error`).

`TwilioSdk.Core.ErrorResponse.RawError`:

| Member | Role |
|---|---|
| `StatusCode: HttpStatusCode` | HTTP status (401/403 auth, 4xx invalid, 404 missing SID, 5xx) |
| `ReadAsString(): string` | Raw body |
| `ReadAsJson<T>(): T?` | Deserialize if you supply `T` |
| `ReadAsBytes(): ReadOnlyMemory<byte>` | |

There is **no** `TryGet…` on `RawError` (`TryGetRawError` exists only on Case A `ApiError`). Do not catch `SdkException<CreateMessageError>` — that type does not exist.

**Auth (401/403):** same Case B; `StatusCode` is `Unauthorized` / `Forbidden`. 401 also trips the SDK’s unauthorized hook (revocable auth — unused for Basic). Missing `AccountSidAuthToken` sends no Basic header (`NoneAuthScheme`).

**Invalid phone on Create:** Case B (not Lookup’s `Valid` flag). **UNVERIFIED** exact Twilio `code` in the JSON body — `ReadAsJson<T>` into a local DTO if needed; on failure use `ReadAsString()` / generic message. Do not deserialize into unrelated generated types (e.g. `AccountsCallsRecordingsSidJson201041408Error`).

**Message not found (Fetch/Update):** Case B. Exact status **UNVERIFIED**.

**Cancel-already-sent:** Case B. Exact status **UNVERIFIED**.

**Create failure must not fail the order:** catch `SdkException<RawError>` (and the `JsonException` paths below) around Create/schedule only; persist a local failed-notification record from `StatusCode` + best-effort body.

Non-2xx is any status outside 200–299 (`HttpStatusPolicy` default).

⚠ Step 10 (error boundary) — Case B has no typed `TryGet…`; catching the wrong `SdkException<T>` compiles only if `T` exists and otherwise lets errors escape. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

### Trap notes (by step)

⚠ Step 1 (client registration) — HttpClient vs wrapper lifetime is not visible from the ctor. **MUST load `dotnet-client-initialization`**.

⚠ Step 1 (authentication) — credential timing and secret loading are not visible from `BasicAuthCredentials`. **MUST load `dotnet-authentication`**.

⚠ Step 1 (retry / timeout / BaseUrl / logging) — options timeouts are not the `HttpClient` timeout; POST Create can still be executed more than once on transport failure; body/header logs can emit tokens and shopper numbers. **MUST load `dotnet-configuration-resilience`**.

⚠ Steps 2–9 (calling) — long signatures with must-pass `null`s mis-bind positionally. **MUST load `dotnet-calling-endpoints`**.

⚠ Steps 2–9 (models) — `StringEnum<T>` construction/comparison; dropped unmodeled JSON. **MUST load `dotnet-models`**.

⚠ Step 9 (ListMessage paging) — there is no SDK enumerator over `NextPageUri`; a one-shot call under-counts the reconciliation range. **MUST load `dotnet-configuration-resilience`**.

⚠ Step 10 (errors + JsonException) — see the two `JsonException` rows above. **MUST load `dotnet-error-handling`**.

⚠ Tests — the `HttpClient` ctor argument is the seam; faking controller types or internals will not match runtime. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `TwilioSdkClient` ctor, `AddTwilioSdkClient`, HttpClient lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retry/timeout/logging; Step 3 POST retry; Step 9 list pagination |
| `dotnet-calling-endpoints` | Steps 2–9 — named arguments, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 2–9 — StringEnum, record nullability, wire names |
| `dotnet-error-handling` | Step 10 — Case B `RawError`, catch ladder, **both** `JsonException` directions |
| `dotnet-testing` | Tests against the integration layer |

---

## Assumptions & Blockers

**Blockers**

- **Caller-supplied idempotency for Create Message is not on the SDK surface.** `CreateMessage` always sets header `Idempotency-Key` to `Guid.NewGuid()`. There is no parameter for the operator key from `POST /api/notifications/{id}/resend`. `TwilioSdk.Core.RequestOptions` cannot add or override headers. Do not invent a `CreateMessage` argument or a DelegatingHandler as an “SDK feature”. Provider-side replay (same SID vs error) is therefore not available to this integration.

**Assumptions**

- `Twilio:BaseUrl` overrides **Default** (`api.twilio.com` / 2010 Messages), not Default4 (Lookup) and not Default1 (`messaging.twilio.com`).
- Contact-number “usable destination” is Lookup v2 `Valid == true` plus persist `PhoneNumber` (E.164). There is no SMS-capability flag in the model.
- Immediate SMS uses `from` = `Twilio:FromNumber`. Scheduled follow-up also sends `messagingServiceSid` = `Twilio:MessagingServiceSid` because `MessageEnumScheduleType` is Messaging-Services-only.
- Reconciliation uses List `from` = `Twilio:FromNumber` and `DateSent>` / `DateSent<` for the ISO-8601 range; equality `DateSent` is unused.
- Redact is `UpdateMessage` empty `body`; cancel is `UpdateMessage` `status: Canceled`; `DeleteMessage` is unused.
- A successful Create followed by Fetch `failed`/`undelivered` is an expected carrier outcome on a live US destination, not an SDK gap.
- `accountSid` on every 2010 call is the configured Account SID (same value as Basic `Username`).
