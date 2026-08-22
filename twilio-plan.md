# Twilio .NET SDK — eShopOnWeb order-notification plan

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: **`TwilioSdk`** (map identity; not `Twilio`). Client: `TwilioSdk.TwilioSdkClient`. Controllers live on client properties. Map stamp: source commit `51fdf48` (`sdk-map.md`).

## Scope & sequence

1. **Client + config** — bind `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl`; construct `TwilioSdkClient` / `AddTwilioSdkClient`; set Account SID + Auth Token; when `Twilio:BaseUrl` is set, apply it **only** to the messaging (Default/api) server.
2. **Lookup on register** (`POST /api/contact-numbers`) — `LookupsV2PhoneNumber.FetchPhoneNumber3`. Reject when the provider does not treat the number as valid; persist `LookupResponse.PhoneNumber` (E.164), not the caller’s input. A shopper with no number is never messaged (app rule).
3. **Send on order events** — `Api20100401Message.CreateMessage`. Immediate: placed / dispatched / cancelled. Dispatched also schedules a follow-up via `scheduleType` + `sendAt` (provider-queued, not an app timer). Swallow send failures so the order operation still succeeds. No webhooks (`statusCallback: null`).
4. **Cancel a not-yet-sent follow-up** on order cancelled — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
5. **Persist provider state** — store `ApiV2010AccountMessage.Sid` and `Status`; refresh via `FetchMessage` (no status callbacks).
6. **Operator resend** (`POST /api/notifications/{notificationId}/resend`) — `CreateMessage` again. Caller idempotency key: see CONTRACT SHEET + Blockers (SDK does not accept a caller-supplied key).
7. **Dispose content** (`DELETE /api/notifications/{notificationId}/content`) — `UpdateMessage` with `body` (redact at the provider). Persist Sid/status; body must not remain retrievable from the provider.
8. **Reconciliation** (`GET /api/notifications/reconciliation?from=&to=`) — `ListMessage` filtered by `from` = `Twilio:FromNumber` and date range `dateSentQueryQuery` / `dateSentQuery`; walk pages until `NextPageUri` is absent.
9. **Error boundary** — every in-scope operation is Case B `SdkException<RawError>`; send failures must not fail order operations; lookup failure must fail registration.

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

No-throw `…Result` variants: **absent** on every operation below (`sdk-map.md`).

### Client construction / auth / servers

| Fact | Contract | Cite |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment`; `Retry: TwilioSdk.Core.Configuration.RetryOptions`; `Logging: LoggingOptions`; `Server: TwilioSdk.ServerOptions`; `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Environment | Only member: `TwilioSdk.Servers.ServerEnvironment.Production` (wire `"production"`). `Default()` → Production. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Auth scheme | `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required`. Basic auth. Do not hard-code values. | `sdk-map.md` *Servers & auth*, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Messaging base URL (`Twilio:BaseUrl`) | Messaging ops use server **Default (api)** (`_server.Default(...)`). Nested type: `TwilioSdk.Servers.DefaultOptions` / `DefaultOptions.ProductionOptions.BaseUrl`, default `"https://api.twilio.com"`. When `Twilio:BaseUrl` is set, assign it **verbatim** to `options.Server.Default.Production.BaseUrl`. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Api/Api20100401Message.cs` |
| Lookups host (do **not** apply `Twilio:BaseUrl`) | Lookup uses server **Default4 (lookups)** (`_server.Default4(...)`). `TwilioSdk.Servers.Default4Options.ProductionOptions.BaseUrl` default `"https://lookups.twilio.com"`. Leave unset when only messaging override is configured. | `Servers/Default4Options.cs`, `Api/LookupsV2PhoneNumber.cs` |
| Config keys (app) | `Twilio:AccountSid` → auth Username; `Twilio:AuthToken` → auth Password; `Twilio:FromNumber` → `CreateMessage.from` / `ListMessage.from`; `Twilio:MessagingServiceSid` → `CreateMessage.messagingServiceSid`; `Twilio:BaseUrl` → messaging `Server.Default.Production.BaseUrl` only. | this sheet |
| `TwilioSdk.Core.RequestOptions` | `LogLevel: Microsoft.Extensions.Logging.LogLevel?` only. No header bag, no idempotency member. | `Core/RequestOptions.cs` |

`RetryOptions` members (all `required` unless starting from `RetryOptions.Default()`): `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` — `sdk-map.md`, `Core/Configuration/RetryOptions.cs`.

### Operation: lookup / validate (`FetchPhoneNumber3`)

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, no default → pass `null` to skip |
| Path | `phoneNumber` — E.164 or national; default country +1 if national (`Api/LookupsV2PhoneNumber.cs`) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity/reassigned/pre_fill params (pass `null` for registration) |
| `fields` values (comma-separated) | `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill` (`Api/LookupsV2PhoneNumber.cs`) |
| Registration call | Named args: `phoneNumber:`, `fields:` at least `"line_type_intelligence"` (and `"line_status"` if you also read line status), all other optionals `null`, `ct:` |
| Returns | `TwilioSdk.Models.LookupResponse` — **not** wrapped in an extra envelope |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `map/operations/LookupsV2PhoneNumber.md`, `map/models/records-4-Li-Me.md`, `Api/LookupsV2PhoneNumber.cs` |

**`LookupResponse` fields this integration reads** (`TwilioSdk.Models`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | Canonical **E.164** (`+` country code + subscriber). **This is what to store.** |
| `NationalFormat (national_format)` | `string?` | National format (do not store as canonical) |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2 |
| `Valid (valid)` | `bool?` | “Boolean which indicates if the phone number is in a valid range that can be freely assigned by a carrier to a user.” **Reject registration when this is not `true`.** |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid, when `Valid` is false |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status` |

`LineTypeIntelligenceInfo` (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, **`Type (type): string?`**, `ErrorCode (error_code): int?`. **`Type` is an unconstrained `string?` — the map/enums page does not list allowed values.**

`LineStatusInfo`: `Status (status): string?`, `ErrorCode (error_code): int?` — `Status` likewise not an enum.

**Invalid number vs API error**

- **Invalid / unusable number on a 2xx:** `Valid != true` and/or non-empty `ValidationErrors`. Fail registration. Do not store.
- **API / transport error:** `SdkException<RawError>` (auth, 4xx/5xx, etc.). Read `ex.Error.StatusCode` and `ex.Error.ReadAsString()` (or `ReadAsJson<T>()`). Fail registration.
- Lookups v1 `FetchPhoneNumber2` (`LookupsV1PhoneNumberApi`) returns `LookupsV1PhoneNumber` with `Carrier (carrier): object?` and **no** `Valid` / `ValidationErrors` / typed E.164 flag — **do not use v1 for this requirement.**

### Operation: create / send / schedule (`CreateMessage`)

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) — **form-urlencoded**, not JSON |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid` (`Twilio:AccountSid`), `to` (stored E.164). All of `statusCallback` … `contentSid` (24 params) nullable, **no default → must pass explicitly** (`null` to skip; null form fields are omitted). |
| Form wire ← C# | `To` ← `to`, `StatusCallback` ← `statusCallback`, `From` ← `from`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the other listed params on the operations page |
| To / From / Body | `to:` destination E.164; `from:` `Twilio:FromNumber`; `body:` SMS text; `messagingServiceSid:` `Twilio:MessagingServiceSid` (optional on the signature; scheduling docs say Messaging Services only — see enum table) |
| Immediate send | `scheduleType: null`, `sendAt: null`, `statusCallback: null` (no public URL), `from:` and/or `messagingServiceSid:` from config, `body:` set |
| Schedule follow-up | `scheduleType: TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) **and** `sendAt: <DateTimeOffset a few days later>`. Enum XML: Messaging Services only, “in conjunction with the `send_time` parameter” — the **C# parameter is `sendAt`**, wire name **`SendAt`** (not `send_time`). |
| Companion status | Created scheduled messages surface as `Status == MessageEnumStatus.Scheduled` (wire `scheduled`) on the returned resource |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (no extra envelope) |
| Persist | `Sid (sid)` — provider message id; `Status (status)` — current delivery outcome |
| Error | **Case B** `SdkException<RawError>` — same accessors as lookup |
| Idempotency | **Not a method parameter.** Implementation always adds header `Idempotency-Key` with `Guid.NewGuid()` on each `CreateMessage` invocation (`Api/Api20100401Message.cs`). `RequestOptions` cannot override it. A second C# call always sends a **new** key. |
| Pagination | none |
| Cite | `map/operations/Api20100401Message.md`, `map/models/records-1-Ac-Ca.md`, `Api/Api20100401Message.cs` |

**Undeliverable US number (carrier later refuses):** `CreateMessage` throws only on **non-2xx**. A 2xx returns `ApiV2010AccountMessage` with an early `Status` (`queued` / `accepted` / `scheduled` / …). Later carrier refusal is **`FetchMessage`/`ListMessage` `Status` of `failed` or `undelivered`**, plus optional `ErrorCode` / `ErrorMessage` — not a create-time exception. Whether any given US number is accepted at create is **UNVERIFIED** (live traffic).

### Operation: fetch one message (`FetchMessage`)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Status access | `message.Status` → `TwilioSdk.Models.Enums.MessageEnumStatus?` (StringEnum, not a C# enum) |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `map/operations/Api20100401Message.md`, `records-1-Ac-Ca.md` |

### Operation: list messages / reconciliation (`ListMessage`)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `to` … `pageToken` (8 params) — pass `null` to skip |
| Filter **From** at the provider | `from:` = `Twilio:FromNumber` → query wire **`From`**. Do **not** list the whole account and filter locally. `to:` leave `null` unless also filtering recipient. |
| Date range (ISO-8601 `from`/`to` query) | There are **no** `DateSentAfter` / `DateSentBefore` C# names. Equivalents: **`dateSentQueryQuery` → wire `DateSent>`** (sent **after** / range start); **`dateSentQuery` → wire `DateSent<`** (sent **before** / range end). Exact-day `dateSent` → wire `DateSent` — pass `null` for a range. SDK serializes these three with `ToIso8601()` = `yyyy-MM-ddTHH:mm:ss.fffZ` (`Api/Api20100401Message.cs`, `Core/Extensions/DateTimeOffsetExtensions.cs`). XML docs also mention `YYYY-MM-DD` / `<=` / `>=` **inside the value**; the generated client instead puts the inequality **in the query key**. |
| Page size | `pageSize: long?` — XML: default **50**, maximum **1000** (`PageSize`) |
| Page index | `page: int?` — “client state” (`Page`) |
| Next-page token | `pageToken: string?` serializes to query **`PageToken`**. XML: “The page token. This is provided by the API.” |
| `page` | XML: “The page index. This value is simply for client state.” — **not** documented as the cursor. Incrementing `page` without `pageToken` is **not** a grounded walk. |
| Auto-iterator | **none** (`Pagination: none`). `ListMessage` does not use the SDK `LinkState` helper. |
| Walk pattern | First call: same filters (`from`, `dateSentQuery`/`dateSentQueryQuery`, `pageSize`), `page: null`, `pageToken: null`. While `NextPageUri` is non-empty: parse that string as a URI, read query name **`PageToken`**, pass it as `pageToken:` on the next call (keep filters + `pageSize`; `page: null`). Stop when `NextPageUri` is null/empty. `NextPageUri` is `[Format(FormatKind.Uri)]` which the SDK format check treats as **`UriKind.Absolute`** (`SchemaConstraintExtensions`); whether the live wire is always absolute and always contains `PageToken` is **UNVERIFIED** — parse `RelativeOrAbsolute` and if `PageToken` is missing, stop (do not invent a token). |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `map/operations/Api20100401Message.md`, `records-4-Li-Me.md` |

**`ListMessageResponse` envelope** (`Models/ListMessageResponse.cs`):

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` |
| `NextPageUri (next_page_uri)` | `string?` — continue while present |
| `FirstPageUri (first_page_uri)` | `string?` |
| `PreviousPageUri (previous_page_uri)` | `string?` |
| `Page (page)` | `int?` |
| `PageSize (page_size)` | `int?` |
| `Start (start)` / `End (end)` | `int?` |
| `Uri (uri)` | `string?` |

Each list item is `ApiV2010AccountMessage` (SID, from, to, body, status, date sent — see model table below).

### Operation: redact body / cancel scheduled (`UpdateMessage`)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) — form-urlencoded |
| Notes | “Update a Message resource (**used to redact Message `body` text** and to **cancel not-yet-sent messages**)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` |
| Form wire | `Body` ← `body`, `Status` ← `status` |
| Cancel scheduled | `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`), `body: null` (omitted) |
| Redact content | `body` XML on `UpdateMessage` is **empty**; `ApiV2010AccountMessage.Body` XML is only “The text content of the message”. **No constant/sentinel** in `Api/Api20100401Message.cs` or `Models/ApiV2010AccountMessage.cs` names the redact value. Null `body` is omitted; `""` is sent as form `Body` with empty value. Whether `""` (or any other string) causes the provider to drop content is **UNVERIFIED**. `status: null`. |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — same shape after update; read `Body` / `Status` |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `map/operations/Api20100401Message.md`, `Api/Api20100401Message.cs` |

`DeleteMessage` **deletes the Message resource** (not “redact body, keep metadata”). Do **not** use it for the dispose-content requirement.

### Response model: `ApiV2010AccountMessage`

Namespace `TwilioSdk.Models` · `records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`. **No wrapper field** — create/fetch/update return this record directly.

| C# (wire) | Type | Integration use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message identifier (pattern `^(SM\|MM)[0-9a-fA-F]{32}$` in XML) |
| `Status (status)` | `MessageEnumStatus?` | Current delivery outcome |
| `From (from)` | `string?` | Sender |
| `To (to)` | `string?` | Recipient |
| `Body (body)` | `string?` | Text; after redact, must not remain retrievable from provider |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT when sent |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT created |
| `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT updated |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | MG… if a Messaging Service was used |
| `AccountSid (account_sid)` | `string?` | AC… |
| `ErrorCode (error_code)` | `int?` | When status is failed/undelivered; XML: do not use programmatically as a stable contract |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code`; same caveat |
| `Direction (direction)` | `MessageEnumDirection?` | Outbound API = `OutboundApi` / wire `outbound-api` |
| `NumSegments (num_segments)` | `string?` | |
| `NumMedia (num_media)` | `string?` | |
| `Price (price)` / `PriceUnit (price_unit)` | `string?` | |
| `Uri (uri)` | `string?` | Relative to `https://api.twilio.com` |
| `ApiVersion (api_version)` | `string?` | |
| `SubresourceUris (subresource_uris)` | `object?` | |

### Enums (StringEnum — static members + `FromValue("wire")`)

Namespace: **`TwilioSdk.Models.Enums`**. Cite `map/models/enums.md`.

**`MessageEnumStatus`** (delivery / lifecycle — persist and report this):

| C# member | Wire |
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

XML: WhatsApp-only for `read`. Failed/undelivered are expected **status** outcomes after a successful create.

**`MessageEnumScheduleType`:** only `Fixed` (`fixed`). XML: Messaging Services only, together with send time.

**`MessageEnumUpdateStatus`:** only `Canceled` (`canceled`).

**`MessageEnumDirection`:** `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

**`ValidationError`:** `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

Create-only enums you will usually pass as `null`: `MessageEnumContentRetention` (`Retain`/`Discard`), `MessageEnumAddressRetention` (`Retain`/`Obfuscate`), `MessageEnumTrafficType` (`Free`), `MessageEnumRiskCheck` (`Enable`/`Disable`).

### Error types (all in-scope ops)

| Operation | Catch | Read |
|---|---|---|
| `FetchPhoneNumber3` | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` | `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| `CreateMessage` | same | same |
| `FetchMessage` | same | same |
| `UpdateMessage` (redact + cancel) | same | same |
| `ListMessage` | same | same |

There is **no** typed `{Operation}Error` / `TryGet…` on these operations (Case B, not Case A). `RawError` is **not** an `ApiError` and has **no** `TryGetRawError`.

---

## Trap notes

⚠ Step 1 (client registration) — the `HttpClient` argument vs `AddTwilioSdkClient` registration does not document ownership, handler lifetime, or whether the SDK wrapper should be singleton/transient; getting this wrong surfaces as socket exhaustion or disposed-client failures at runtime. **MUST load `dotnet-client-initialization`** before constructing or DI-registering the client.

⚠ Step 1 (credentials) — `AccountSidAuthToken` is nullable on options; an unset scheme fails later as 401/403 rather than at `new TwilioSdkClient`, and rotating secrets has a defined options-object lifetime. **MUST load `dotnet-authentication`** before wiring `Twilio:AccountSid` / `Twilio:AuthToken`.

⚠ Step 1 (BaseUrl, retries, timeout, ListMessage paging, SDK logging) — `Retry`/`Timeout` on options are not the `HttpClient` timeout and do not document which failures retry on POST `CreateMessage`; `Server.Default` vs `Server.Default4` are separate nodes; `ListMessage` has no SDK iterator over `NextPageUri`; enabling `Logging` can capture request/response material. **MUST load `dotnet-configuration-resilience`** before setting `Twilio:BaseUrl`, retries, walking reconciliation pages, or turning on SDK logs. Do not log auth tokens or shopper numbers.

⚠ Steps 2–8 (every call) — 8–24 leading parameters are nullable **without** C# defaults; a positional `CreateMessage`/`ListMessage`/`FetchPhoneNumber3` binds the wrong arguments. **MUST load `dotnet-calling-endpoints`** before the first SDK invocation.

⚠ Steps 2–8 (models) — `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError` are `StringEnum<T>` records, not C# enums; response records drop unmodeled JSON. **MUST load `dotnet-models`** before reading `Status`/`Valid` or building comparisons.

⚠ Step 9 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 9 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 9 (Case B) — every operation in this sheet throws `SdkException<RawError>`; a Case-A `TryGet…` ladder will not compile or will not run. Status codes live on `ex.Error.StatusCode`, not by parsing `ex.ToString()`. **MUST load `dotnet-error-handling`** before any `try/catch` around SDK calls.

⚠ Tests — the constructor `HttpClient` is the fakeable seam; asserting that a method was entered is not asserting send/lookup behaviour. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

- `dotnet-client-initialization` — Step 1 client constructor, `HttpClient`, `AddTwilioSdkClient`
- `dotnet-authentication` — Step 1 `AccountSidAuthToken` / Basic credentials from `Twilio:AccountSid` + `Twilio:AuthToken`
- `dotnet-configuration-resilience` — Step 1 messaging-only BaseUrl, retries/timeouts, ListMessage pagination, logging
- `dotnet-calling-endpoints` — Steps 2–8 named arguments and must-pass nullables
- `dotnet-models` — StringEnum status/validation, `LookupResponse` / `ApiV2010AccountMessage` records
- `dotnet-error-handling` — Step 9 Case B `RawError` boundary **and** both `JsonException` directions above (2xx deserialize miss vs non-2xx error-object construction destroying status)
- `dotnet-testing` — HttpClient handler seam for the notification/lookup layer

---

## Assumptions & Blockers

**Assumptions**

- Lookups **v2** `FetchPhoneNumber3` is the registration validator (v1 has no `Valid` / `ValidationErrors`).
- “A few days later” for the dispatch follow-up is computed by the app and passed as `sendAt`; the SDK only transports `DateTimeOffset?`.
- Immediate SMS uses `from: Twilio:FromNumber` and may also pass `messagingServiceSid: Twilio:MessagingServiceSid`. Scheduled send **must** set `messagingServiceSid` (`MessageEnumScheduleType` XML: “For Messaging Services only”) plus `scheduleType: Fixed` and `sendAt`. `CreateMessage` XML for `from` / `messagingServiceSid` is empty — source does **not** say to null `from` or to set both; whether both together are accepted is **UNVERIFIED**.
- `statusCallback` is always `null` (no publicly reachable URL).
- Order-path send/cancel/fetch failures are swallowed by the app after the SDK throws or returns; lookup failures reject `POST /api/contact-numbers`.
- Root namespace in this SDK is `TwilioSdk` (map), including types the brief called `Twilio`.

**Blockers (genuine map/SDK gaps — do not invent a workaround)**

1. **Caller-supplied idempotency on send is not exposable.** `CreateMessage` has no idempotency parameter. `RequestOptions` only has `LogLevel`. The generated method always sets header `Idempotency-Key` to a **new** `Guid` per invocation (`Api/Api20100401Message.cs`). Repeating an operator resend under the same app key **cannot** be made to reuse Twilio’s idempotency key through this SDK. App-local dedup is outside the SDK contract.
2. **`LineTypeIntelligenceInfo.Type` allowed values are not in the map or enums.** Usable-SMS checks beyond `Valid == true` cannot be grounded in a closed Type list. Do not invent `mobile`/`landline`/… constants.
3. **Redact sentinel not documented on `body`.** `UpdateMessage` is the redact operation; null `body` is omitted. Neither `Api/Api20100401Message.cs` nor `Models/ApiV2010AccountMessage.cs` names which `body` value drops content. After update, confirm via `FetchMessage.Body` — **UNVERIFIED**.
4. **`ListMessage` has no SDK page iterator.** Walk by parsing `PageToken` from `NextPageUri` (see Walk pattern). Live shape of `next_page_uri` (always absolute? always has `PageToken`?) is **UNVERIFIED**.
