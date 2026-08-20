# Twilio .NET SDK — eShopOnWeb SMS order-notification CONTRACT SHEET

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdk.TwilioSdkClient`. Map stamp: source commit `51fdf48`.

---

## Scope & sequence

| Step | Capability | Operations |
| ---: | --- | --- |
| 1 | Client construction, auth, messaging-only base URL | `TwilioSdkClient` / `TwilioSdkClientOptions` / `AddTwilioSdkClient` |
| 2 | Validate / lookup shopper mobile at `POST /api/contact-numbers` | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS on place / dispatch / cancel / operator resend | `Api20100401Message.CreateMessage` |
| 4 | Schedule follow-up SMS after dispatch (provider-queued) | `Api20100401Message.CreateMessage` with `scheduleType` + `sendAt` |
| 5 | Cancel unsent follow-up on order cancel | `Api20100401Message.UpdateMessage` (`status`) |
| 6 | Read current delivery outcome (no webhooks) | `Api20100401Message.FetchMessage` |
| 7 | Operator resend with caller-supplied idempotency key | **see Blockers** — `CreateMessage` has no caller key |
| 8 | Dispose message text at the provider | `Api20100401Message.UpdateMessage` (`body`) — **not** `DeleteMessage` |
| 9 | Reconciliation listing scoped to sending number | `Api20100401Message.ListMessage` (`from` + date range) |
| 10 | Isolate send/create/cancel/fetch/list/redact/lookup failures from place/dispatch/cancel | Case B `SdkException<RawError>` on every in-scope op |

Do **not** call `Api20100401Message.DeleteMessage` for content disposal — it deletes the Message resource. Do **not** call `LookupsV1PhoneNumberApi.FetchPhoneNumber2` — v1 has no `Valid` / `ValidationErrors`. Do **not** set `statusCallback` (no webhooks).

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
| --- | --- | --- |
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers `TwilioSdkClient` via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` only (`"production"`). `Default()` → Production | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Auth property | `options.AccountSidAuthToken` | `sdk-map.md` Servers & auth |
| Credentials shape | `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials` — `required string Username { get; init; }` · `required string Password { get; init; }` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Config mapping (keys only, never hardcode values) | `Username` ← `Twilio:AccountSid` · `Password` ← `Twilio:AuthToken` | this sheet |
| Messaging API host (Default / api) | `options.Server.Default.Production.BaseUrl` — type `TwilioSdk.Servers.DefaultOptions` / nested `ProductionOptions.BaseUrl: string`, default `"https://api.twilio.com"` | `ServerOptions.cs` · `Servers/DefaultOptions.cs` · Create/Fetch/List/Update Message all call `_server.Default(...)` (`Api/Api20100401Message.cs`) |
| Lookup API host (Default4 / lookups) | `options.Server.Default4.Production.BaseUrl` — type `TwilioSdk.Servers.Default4Options` / nested `ProductionOptions.BaseUrl: string`, default `"https://lookups.twilio.com"` | `Servers/Default4Options.cs` · `LookupsV2PhoneNumber.FetchPhoneNumber3` calls `_server.Default4(...)` (`Api/LookupsV2PhoneNumber.cs`) |
| `Twilio:BaseUrl` | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` only. Do **not** write it onto `Default4` (or Default1–3, Default5–14). Lookup stays on the lookups host. When unset, leave the Default production default. | this sheet + `ServerOptions.cs` |
| Same client | One `TwilioSdkClient` serves both messaging (`Default`) and lookup (`Default4`). Do not construct a second client for Lookup. | `TwilioSdkClient.cs` |
| Per-call options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel?: Microsoft.Extensions.Logging.LogLevel`. No headers dictionary. | `Core/RequestOptions.cs` |
| App config used as send args, not client options | `Twilio:FromNumber` → `CreateMessage`/`ListMessage` `from` · `Twilio:MessagingServiceSid` → `CreateMessage` `messagingServiceSid` · `Twilio:AccountSid` also → `accountSid` path param on every Message op | this sheet |

Which sender arg is required:

| Use | `from` (`Twilio:FromNumber`) | `messagingServiceSid` (`Twilio:MessagingServiceSid`) |
| --- | --- | --- |
| Immediate SMS (place / dispatch / cancel / resend) | Pass it (reconciliation lists by this number) | Optional (`null` to skip) |
| Scheduled follow-up | Optional (`null` to skip) | **Required** — `MessageEnumScheduleType` is documented “For Messaging Services only” |
| `ListMessage` scoped to this app | **Required** as `from` — there is **no** `messagingServiceSid` list filter | n/a |

(`operations/Api20100401Message.md`, `map/models/enums.md` `MessageEnumScheduleType`)

---

### 1. Lookup / validate number — `FetchPhoneNumber3`

| | |
| --- | --- |
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · server **Default4 (lookups)** |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, no C# default; pass `null` to skip |
| Wire query | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity_match / reassigned / pre_fill extras unused here — pass `null`) |
| Returns | `TwilioSdk.Models.LookupResponse` — **not** wrapped; fields are on the record itself |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` |
| Pagination | none |
| Map | `operations/LookupsV2PhoneNumber.md` · `records-4-Li-Me.md` · `Api/LookupsV2PhoneNumber.cs` · `Models/LookupResponse.cs` |

`fields` allowed values (comma-separated), from `Api/LookupsV2PhoneNumber.cs` XML: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`.

`LookupResponse` fields this step reads (`CSharpName (wire): type`):

| Field | Role |
| --- | --- |
| `PhoneNumber (phone_number): string?` | **Canonical E.164** (`+` + country code + subscriber). Store this, not the caller’s typed input. |
| `Valid (valid): bool?` | “Boolean which indicates if the phone number is in a valid range that can be freely assigned by a carrier to a user.” Reject registration when this is `false` (or missing after a successful 2xx — treat as not usable). |
| `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reasons when invalid. |
| `NationalFormat (national_format): string?` | Display-only; do not persist as the destination. |
| `LineStatus (line_status): TwilioSdk.Models.LineStatusInfo?` | Only populated if `fields` includes `line_status`. `Status (status): string?` · `ErrorCode (error_code): int?`. **No enum** for `Status` in the map. |
| `LineTypeIntelligence (line_type_intelligence): TwilioSdk.Models.LineTypeIntelligenceInfo?` | Only if `fields` includes `line_type_intelligence`. `Type (type): string?` — **no enum**. |

`ValidationError` (`TwilioSdk.Models.Enums.ValidationError`, `StringEnum`) members → wire:

| C# member | Wire |
| --- | --- |
| `TooShort` | `TOO_SHORT` |
| `TooLong` | `TOO_LONG` |
| `InvalidButPossible` | `INVALID_BUT_POSSIBLE` |
| `InvalidCountryCode` | `INVALID_COUNTRY_CODE` |
| `InvalidLength` | `INVALID_LENGTH` |
| `NotANumber` | `NOT_A_NUMBER` |

(`map/models/enums.md`)

Reject-at-registration rule grounded in this SDK: **`Valid == true` and store `PhoneNumber`**. A  non-2xx lookup is Case B (`SdkException<RawError>`) — reject registration, do not send later. `LineStatus.Status` / `LineTypeIntelligence.Type` strings are **not** enumerated by the SDK; do not invent allowed values. A number that Lookup accepts as `Valid` may still later `undelivered` (US destinations on this account) — that is a **delivery outcome**, not a lookup rejection.

---

### 2 / 3. Send SMS and schedule follow-up — `CreateMessage`

| | |
| --- | --- |
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default (api)** · form-urlencoded body |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid`, `to` |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — nullable, no C# default; pass `null` to skip |
| Wire form fields | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **not** wrapped |
| Error | **Case B** `SdkException<RawError>` |
| Accessors | `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| Pagination | none |
| Map | `operations/Api20100401Message.md` · `records-1-Ac-Ca.md` · `Api/Api20100401Message.cs` |

Immediate send (place / dispatch / cancel / resend) — pass explicitly:

- `accountSid`: `Twilio:AccountSid`
- `to`: stored E.164 from lookup
- `from`: `Twilio:FromNumber`
- `body`: notification text
- `statusCallback`: `null` (no webhooks)
- `scheduleType`: `null` · `sendAt`: `null` · `messagingServiceSid`: `null` unless also using the messaging service for immediate send
- every other optional: `null`

Scheduled follow-up (after dispatch) — same as above plus:

- `messagingServiceSid`: `Twilio:MessagingServiceSid` (**required** for schedule)
- `scheduleType`: `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) — **only** member
- `sendAt`: `DateTimeOffset` a few days later. CreateMessage passes this as form `SendAt` **without** `ToIso8601()` (unlike ListMessage dates).
- `from`: `null` or `Twilio:FromNumber` (both are optional on create; list-by-from still needs the sending number to appear on the resource)

`MessageEnumScheduleType` XML (`Models/Enums/MessageEnumScheduleType.cs`): “For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter”. The **C# / wire names** are `sendAt` / `SendAt`, not `send_time`.

**When the message SID is assigned:** `CreateMessage` returns `ApiV2010AccountMessage` on 2xx. Persist `Sid` from that return (including scheduled creates). A scheduled create is the same operation; there is no separate schedule endpoint.

---

### 4. Cancel scheduled follow-up — `UpdateMessage` (`status`)

| | |
| --- | --- |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) · form-urlencoded |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` |
| Wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

Cancel: `body: null`, `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`) — **only** member of that enum.

The SDK does **not** enumerate which current `MessageEnumStatus` values the provider will accept for cancel. If the message is already sent, the call throws Case B `SdkException<RawError>` — catch it; do not fail order cancel. After a successful cancel, returned `Status` is `MessageEnumStatus.Canceled`.

---

### 5. Fetch delivery outcome — `FetchMessage`

| | |
| --- | --- |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

Same operation for scheduled and sent messages (lookup by `sid`). No webhook; poll/fetch this.

---

### 6. Caller-supplied idempotency key on resend

`CreateMessage` has **no** `idempotencyKey` parameter (unlike `CreatePayments` / `CreateUserDefinedMessage` / Conversations v2, which do).

`TwilioSdk.Core.RequestOptions` exposes **only** `LogLevel` — it cannot attach a caller header.

Inside `CreateMessage` (`Api/Api20100401Message.cs`) the SDK **always** sends header `Idempotency-Key` with `Guid.NewGuid()` — a new value on every invocation, not caller-controlled. `UpdateMessage` and `DeleteMessage` do the same; `FetchMessage` / `ListMessage` send no such header.

**Provider-honored idempotency under the operator’s key is not available in this SDK.** See Blockers. A fresh `CreateMessage` is always a new provider attempt.

---

### 7. Redact message body — `UpdateMessage` (`body`)

Same operation as cancel. Redact: `status: null`, `body:` replacement text (empty string is a legal `string` value). Do **not** use `DeleteMessage` — that removes the resource; the requirement is that the fact of the send and its outcome survive.

Returned `ApiV2010AccountMessage` still carries `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `From`, `To`, `DateSent`, `DateCreated`, `NumSegments`, `Price`, etc. Exact post-redact `Body` string the provider stores is **UNVERIFIED** (only live traffic confirms); persist `Sid` + `Status` (+ `ErrorCode`/`ErrorMessage`) from the returned record.

---

### 8. Reconciliation list — `ListMessage`

| | |
| --- | --- |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · Default (api) |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `to` … `pageToken` |
| Wire query | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, `DateSent<` ← `dateSentQuery`, `DateSent>` ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date encoding | `dateSent` / `dateSentQuery` / `dateSentQueryQuery` are sent as `value?.ToIso8601()` → UTC `"yyyy-MM-ddTHH:mm:ss.fff'Z'"` (`Api/Api20100401Message.cs`, `Core/Extensions/DateTimeOffsetExtensions.cs`). XML mentions `YYYY-MM-DD`; the **generated client** sends the Iso8601 form. |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **No SDK auto-pager** (map: “Pagination: none”). Manual `pageSize` / `page` / `pageToken`. XML: default page size 50, max 1000. |
| Map | `operations/Api20100401Message.md` · `records-4-Li-Me.md` |

Scope to this app’s sending number **at the provider** (do not list the whole account then filter):

- `from`: `Twilio:FromNumber` (sender filter; XML: “Filter by sender”)
- `to` (recipient filter): `null`
- `dateSent`: `null` (exact-day filter unused)
- `dateSentQueryQuery`: app query `from` (lower bound) → wire `DateSent>`
- `dateSentQuery`: app query `to` (upper bound) → wire `DateSent<`
- Parse the ISO-8601 `from`/`to` query into `DateTimeOffset` and pass those values; the SDK encodes them.

`ListMessageResponse` envelope (`CSharpName (wire): type`):

| Field | Role |
| --- | --- |
| `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` | Page payload |
| `NextPageUri (next_page_uri): string?` | Next page present |
| `PageToken` is an **input** (`pageToken`); the envelope does not have a `page_token` field — XML: “The page token. This is provided by the API.” |
| `End (end): int?`, `Start (start): int?`, `Page (page): int?`, `PageSize (page_size): int?`, `FirstPageUri (first_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `Uri (uri): string?` | Paging metadata |

There is **no** list filter for Messaging Service SID.

---

### Response record used by create / fetch / update / list items

`TwilioSdk.Models.ApiV2010AccountMessage` (`records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`) — persist at least identifier + outcome:

| C# (wire) | Type | Use |
| --- | --- | --- |
| `Sid (sid): string?` | provider message SID (`SM…` / `MM…`) | persist as provider id |
| `Status (status): MessageEnumStatus?` | delivery / schedule state | persist as current outcome |
| `ErrorCode (error_code): int?` | set when status is `failed` / `undelivered` | persist; XML: do not branch programmatically on specific codes |
| `ErrorMessage (error_message): string?` | description of `error_code` | persist for operators |
| `To (to): string?` | E.164 destination | |
| `From (from): string?` | sender | |
| `MessagingServiceSid (messaging_service_sid): string?` | | |
| `Body (body): string?` | text (gone after redact) | |
| `DateSent (date_sent): string?` | RFC 2822 GMT | |
| `DateCreated (date_created): string?` | RFC 2822 GMT | |
| `DateUpdated (date_updated): string?` | RFC 2822 GMT | |
| `NumSegments (num_segments): string?` | | |
| `Price (price): string?` · `PriceUnit (price_unit): string?` | | |
| `Direction (direction): MessageEnumDirection?` | | |
| `AccountSid (account_sid): string?` · `Uri (uri): string?` · `ApiVersion (api_version): string?` · `NumMedia (num_media): string?` · `SubresourceUris (subresource_uris): object?` | | |

`MessageEnumStatus` (`TwilioSdk.Models.Enums.MessageEnumStatus`) member → wire:

| C# | Wire |
| --- | --- |
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

Scheduled create → expect `Scheduled`. Terminal delivery outcomes this app must treat as outcomes (not send failures of the order): `Delivered`, `Undelivered`, `Failed`, `Sent`, plus in-flight `Queued` / `Sending` / `Accepted`. US destinations that the API accepts and the carrier later refuses surface as `Undelivered`/`Failed` on fetch — expected.

`MessageEnumDirection`: `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

Enums are `StringEnum<T>`, not C# enums — use the static members (e.g. `MessageEnumStatus.Scheduled`), not `MessageEnumStatus.scheduled`.

---

### Errors — every in-scope operation

All of `CreateMessage`, `FetchMessage`, `ListMessage`, `UpdateMessage`, `FetchPhoneNumber3` throw **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. **No** typed `{Operation}Error`. **No** `…Result` / no-throw variant.

`TwilioSdk.Core.Exceptions.SdkException<TError>`: `required TError Error { get; init; }` (`Core/Exceptions/SdkException.cs`).

`TwilioSdk.Core.ErrorResponse.RawError`: `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. **No** `TryGet…` accessors (those exist only on Case A `ApiError` subclasses).

Place / dispatch / cancel must catch `SdkException<RawError>` (and the `JsonException` cases in Trap notes) around send/schedule/cancel/fetch so a provider failure does not fail the order. Registration lookup **should** fail the contact-number POST.

---

## Trap notes

⚠ Step 1 (client / DI) — constructing `TwilioSdkClient` and `AddTwilioSdkClient` hides HttpClient ownership and lifetime; getting that wrong leaks sockets or disposes a client still in use. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a nullable `BasicAuthCredentials` with two `required` init properties; a missing or swapped username/password surfaces only as later 401 Case B, not at construction. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (base URL / retries) — `Retry` / `Timeout` on `TwilioSdkClientOptions` are not the timeout of the `HttpClient` you pass in, and they do not bound a whole business operation; CreateMessage is POST with a retryable form body, so whether a failed write can be re-sent is not visible from the signature. **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Timeout`, or `Server.Default.Production.BaseUrl`.

⚠ Steps 2–9 (calls) — `CreateMessage`, `ListMessage`, `FetchPhoneNumber3`, and `UpdateMessage` have long runs of must-pass-null optionals with **no C# defaults**; a positional call binds the wrong argument. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–8 (models / enums) — statuses, schedule type, and validation errors are `StringEnum<T>` (static members / `FromValue`), not C# enums; `LookupResponse` / `ApiV2010AccountMessage` / `ListMessageResponse` drop unmodeled JSON on deserialize. **MUST load `dotnet-models`** before mapping fields or comparing status.

⚠ Steps 2–10 (errors) — every in-scope op is Case B (`SdkException<RawError>`); `TryGetRawError` is not on `RawError`, and there is no typed `{Op}Error`. A Case A-shaped catch ladder matches nothing. **MUST load `dotnet-error-handling`** before writing the boundary that keeps place/dispatch/cancel alive.

⚠ Step 8 (list paging) — `ListMessage` has no SDK pager; `page` / `pageSize` / `pageToken` plus `NextPageUri` are the only continuation, and a single call is not the full date range. **MUST load `dotnet-configuration-resilience`** before writing reconciliation paging.

⚠ Tests — the constructor `HttpClient` argument is the test seam; faking `TwilioSdkClient` internals is not. **MUST load `dotnet-testing`** before stubbing.

A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

A **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
| --- | --- |
| `dotnet-client-initialization` | Step 1 — `TwilioSdkClient` ctor, `AddTwilioSdkClient`, HttpClient lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–9 — first call to each operation; named args / must-pass nulls |
| `dotnet-models` | Steps 2–8 — `LookupResponse`, `ApiV2010AccountMessage`, `StringEnum<T>` |
| `dotnet-error-handling` | Steps 2–10 — Case B `SdkException<RawError>`, both `JsonException` directions, order-op isolation |
| `dotnet-configuration-resilience` | Step 1 Retry/Timeout/BaseUrl; Step 8 `ListMessage` paging |
| `dotnet-testing` | Test doubles for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Lookup v2 (`FetchPhoneNumber3`) is the registration validator; v1 is out of scope because it lacks `Valid` / `ValidationErrors`.
- Immediate notifications pass `from` = `Twilio:FromNumber` so `ListMessage(from:)` can ask the provider for this app’s traffic. Scheduled follow-up passes `messagingServiceSid` = `Twilio:MessagingServiceSid` with `scheduleType: Fixed` and `sendAt`.
- `statusCallback` is always `null` (no public URL / no webhooks). Delivery state is `FetchMessage` only.
- `Twilio:BaseUrl`, when present, overrides **only** `Server.Default.Production.BaseUrl` (api.twilio.com Message ops). Lookup remains on Default4 / lookups.twilio.com.
- Env vars `TWILIO_TEST_TO_NUMBER` / `TWILIO_UNREACHABLE_TO_NUMBER` are for later live tests; their values are not recorded here. US `undelivered` after a successful create is an expected delivery outcome.

**Blockers**

- **Caller-supplied Messaging idempotency key is not in this SDK.** `CreateMessage` has no idempotency parameter; `RequestOptions` cannot set headers; the client always sends `Idempotency-Key: Guid.NewGuid()`. Repeating `POST /api/notifications/{id}/resend` under the same caller key **cannot** be made a provider no-op via this SDK. Do not invent a header or wrap a second HTTP client to inject one. App-local de-dupe of that HTTP API is outside the SDK contract and is not specified here.
