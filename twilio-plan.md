# eShopOnWeb SMS order-notification — Twilio .NET SDK plan + contract sheet

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: **`TwilioSdk`** (not `Twilio`). Client: `TwilioSdk.TwilioSdkClient`. Map stamp: source commit `51fdf48`.

---

## 1. Scope & sequence

| Step | Feature | Operation(s) |
|---|---|---|
| 1 | Client + DI + auth + per-server BaseUrl | `new TwilioSdkClient(httpClient, options)` / `AddTwilioSdkClient` |
| 2 | Phone-number lookup / validation at `POST /api/contact-numbers` | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS immediately (placed / dispatched / cancelled / resend) | `client.Api20100401Message.CreateMessage` |
| 4 | Schedule follow-up SMS with the provider | `client.Api20100401Message.CreateMessage` (`scheduleType` + `sendAt` + `messagingServiceSid`) |
| 5 | Cancel a not-yet-sent scheduled follow-up | `client.Api20100401Message.UpdateMessage` (`status`) |
| 6 | Fetch message by SID (delivery outcome; no StatusCallback) | `client.Api20100401Message.FetchMessage` |
| 7 | Resend (new CreateMessage; persist new SID as `notificationId`) | `client.Api20100401Message.CreateMessage` |
| 8 | Redact message body at the provider | `client.Api20100401Message.UpdateMessage` (`body`) — **not** `DeleteMessage` |
| 9 | Reconciliation list scoped to this app's From number | `client.Api20100401Message.ListMessage` (`from` + date range) |
| 10 | Error boundary around every call (send must never fail the order op) | Case B `SdkException<RawError>` on every in-scope op |

Do **not** use `DeleteMessage` for content redaction: it deletes the Message resource (`void`). Do **not** set `statusCallback` (app has no public URL). Do **not** apply `Twilio:BaseUrl` to Lookup.

---

## 2. CONTRACT SHEET

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

### 2.1 Client construction / auth / servers

| Fact | Value | Cite |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md`, `TwilioSdkClient.cs` |
| DI extension | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers **`AddSingleton`** over the **unnamed** `IHttpClientFactory` client (`CreateClient()` with no name) | `ServiceCollectionExtensions.cs` |
| Options class | `TwilioSdk.TwilioSdkClientOptions` | `TwilioSdkClientOptions.cs` |
| Options members | `Environment`: `TwilioSdk.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` → `Production`); `Retry`: `TwilioSdk.Core.Configuration.RetryOptions`; `Logging`: `LoggingOptions`; `Server`: `TwilioSdk.ServerOptions`; `AccountSidAuthToken`: `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Auth scheme | HTTP Basic. `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required string`. XML: API key as username + API-key secret as password, **or** Account SID + auth token (limit SID/token to local testing). Never log/return/commit `AuthToken`. | `sdk-map.md` *Servers & auth*, `BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment` (`StringEnum`): member `Production` (wire `"production"`). `Default()` returns `Production`. | `Servers/ServerEnvironment.cs` |
| Messaging host (Default) | Create/Fetch/Update/List/Delete Message use `_server.Default(...)` → **`https://api.twilio.com`**. Override **only this** from `Twilio:BaseUrl` when set: `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl verbatim>`. Types: `TwilioSdk.ServerOptions.Default` is `TwilioSdk.Servers.DefaultOptions`; nested `DefaultOptions.ProductionOptions.BaseUrl: string`. | `Api/Api20100401Message.cs`, `Servers/DefaultOptions.cs`, `ServerOptions.cs` |
| Lookup host (Default4) | Lookup uses `_server.Default4(...)` → **`https://lookups.twilio.com`**. Different host from messaging. **Never** assign `Twilio:BaseUrl` to `options.Server.Default4.Production.BaseUrl`. Type: `TwilioSdk.Servers.Default4Options.ProductionOptions.BaseUrl`. | `operations/LookupsV2PhoneNumber.md`, `Servers/Default4Options.cs` |
| `RequestOptions` | `TwilioSdk.Core.RequestOptions` (`record`): **only** `LogLevel? LogLevel { get; init; }`. No header bag. Cannot attach a caller idempotency key. | `Core/RequestOptions.cs` |
| HttpClient | SDK does **not** own the `HttpClient`; caller supplies it. | `sdk-map.md` |

`RetryOptions` members (all `required`; start from `RetryOptions.Default()` or `Disabled()`): `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. Source: `Core/Configuration/RetryOptions.cs`.

### 2.2 Operations

#### LookupsV2PhoneNumber.FetchPhoneNumber3 — contact-number registration

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| HTTP | `GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}` |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 nullable params `fields` … `partnerSubId` (pass `null` to skip) |
| Path | `phoneNumber` — typed-by-caller value; XML: E.164 or national; default country +1 |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity/reassigned/prefill params (pass `null`) |
| `fields` XML | Comma-separated. Possible values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. For this integration pass `"line_type_intelligence,line_status"` (validation fields are returned without a package). `TwilioSdk.Models.Enums.Field` exists for **batch** lookup bodies, **not** this `string? fields` param. |
| Returns | `TwilioSdk.Models.LookupResponse` (no envelope wrapper) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| No-throw/`…Result` | absent |
| Pagination | none |
| Cite | `operations/LookupsV2PhoneNumber.md`, `Api/LookupsV2PhoneNumber.cs`, `records-4-Li-Me.md` |

**`LookupResponse` fields used (C# (wire): type)** — `records-4-Li-Me.md`, `Models/LookupResponse.cs`:

| Field | Type | Notes |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Persist this** — E.164 (`+` + country code + subscriber). Canonical form. |
| `Valid (valid)` | `bool?` | XML: true if the number is in a valid range that a carrier can freely assign. Reject registration when `Valid == false`. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reasons when invalid. |
| `NationalFormat (national_format)` | `string?` | Do not persist as canonical. |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix. |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2. |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Only populated if requested in `fields`. Nested: `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?`, `ErrorCode (error_code): int?`. **`Type` is an untyped `string?` — the SDK does not enumerate SMS-capable values.** |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Nested: `Status (status): string?`, `ErrorCode (error_code): int?`. Status is an untyped `string?`. |

Do **not** use `LookupsV1PhoneNumberApi.FetchPhoneNumber2` (`LookupsV1PhoneNumber` has no `Valid` bool).

#### Api20100401Message.CreateMessage — immediate send + scheduled send + resend

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| HTTP | `POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json` (form-urlencoded body, **not** JSON) |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid` (`Twilio:AccountSid`), `to` (stored canonical E.164) |
| Must-pass-explicitly | 24 nullable params `statusCallback` … `contentSid` |
| Form fields (wire ← C#) | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Null params | Omitted from the form (`ParameterFlattener`: `if (value is null) return []`). Empty string `""` **is** sent. |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (no wrapper). Persist **`Sid`** as the provider message id / `notificationId`. |
| Error | **Case B** `SdkException<RawError>` |
| Accessors | `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| No-throw/`…Result` | absent |
| Pagination | none |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs`, `records-1-Ac-Ca.md` |

**Immediate SMS (placed / dispatched / cancelled / resend)** — named args:

- `accountSid:` `Twilio:AccountSid`
- `to:` stored canonical number
- `from:` `Twilio:FromNumber` (this app's sending number)
- `body:` SMS text
- `statusCallback:` `null` (no public URL; do not use webhooks)
- `messagingServiceSid:` `null`
- `scheduleType:` `null`, `sendAt:` `null`
- all other nullable params: `null`
- `ct:` caller token

**Scheduled follow-up (provider-side, not a local timer)** — named args:

- Same `accountSid`, `to`, `body`, `statusCallback: null`
- `messagingServiceSid:` `Twilio:MessagingServiceSid` — **required for scheduling**. `MessageEnumScheduleType` XML: *"For Messaging Services only: Include this parameter with a value of `fixed` in conjunction with the `send_time` parameter in order to schedule a Message."* The C# / wire field is **`sendAt` / `SendAt`**, not `send_time`.
- `scheduleType:` `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `"fixed"` — only member)
- `sendAt:` `DateTimeOffset` (a few days later)
- `from:` may be passed as `Twilio:FromNumber` or `null`; XML on CreateMessage `from`/`messagingServiceSid` is empty. Messaging Service is what the schedule-type enum requires.
- all other nullable params: `null`

**From vs MessagingServiceSid**

| Call | `from` | `messagingServiceSid` |
|---|---|---|
| Immediate SMS | `Twilio:FromNumber` | `null` |
| Scheduled SMS | optional (`null` or `Twilio:FromNumber`) | **`Twilio:MessagingServiceSid` (required)** |

**Identifier returned:** `ApiV2010AccountMessage.Sid` (wire `sid`) — unique Twilio string; XML regex `^(SM|MM)[0-9a-fA-F]{32}$`, length 34. Persist for later fetch / cancel / redact / resend-result `notificationId`.

**Idempotency (resend key) — SDK contract, not a caller parameter.** `CreateMessage` always attaches header `Idempotency-Key` with a **new** `Guid.NewGuid()` inside the generated method. `RequestOptions` cannot override headers. There is **no** CreateMessage parameter for a caller key. See **Blockers**.

#### Api20100401Message.FetchMessage — delivery outcome

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `operations/Api20100401Message.md` |

Read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `To`, `From`, `Body`, `Direction`, `Sid`. US carrier refusal after accept: CreateMessage succeeded; later Fetch shows `Status` `failed` / `undelivered` plus `ErrorCode` / `ErrorMessage`. That is a delivery outcome, not a create failure.

#### Api20100401Message.UpdateMessage — cancel scheduled OR redact body

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (form-urlencoded) |
| Notes | *"Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)"* |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` |
| Form (wire ← C#) | `Body` ← `body`, `Status` ← `status` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` (already-sent / not cancellable surfaces here — read `StatusCode` + `ReadAsString()`) |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs` |

**Cancel (order cancelled, follow-up not yet sent):** `body: null` (omitted), `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `"canceled"` — only member). Operation notes limit this to **not-yet-sent** messages. Which current `MessageEnumStatus` values accept cancel is **not** listed in the map or XML.

**Redact content (`DELETE /api/notifications/{id}/content`):** `body: ""` (empty string — null would omit `Body` and not redact), `status: null`. Response still carries `Sid`, `Status`, `ErrorCode`, etc.; `Body` is the redacted text at the provider. **Do not** call `DeleteMessage` (that deletes the resource).

#### Api20100401Message.ListMessage — reconciliation by From + date range

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 nullable params `to` … `pageToken` |
| Query (wire ← C#) | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, **`DateSent<`** ← `dateSentQuery`, **`DateSent>`** ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date encoding | SDK calls `dateSent?.ToIso8601()` / same for the two inequality params (`yyyy-MM-ddTHH:mm:ss.fff'Z'` UTC). XML also mentions `YYYY-MM-DD` / `<=YYYY-MM-DD` / `>=YYYY-MM-DD` for `DateSent`. |
| `pageSize` XML | Default 50, maximum 1000 |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **none** (no auto-paginator). Manual `page` / `pageToken`. Map: *"only `page`, no `perPage`"*. |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs`, `records-4-Li-Me.md` |

**eShop `?from={from}&to={to}` (ISO-8601 datetimes) vs Twilio `From`/`To` (phone numbers):**

| eShop query | Twilio ListMessage arg | Wire |
|---|---|---|
| (config, not the query) | `from:` `Twilio:FromNumber` | `From` — **ask the provider for this sending number's messages** |
| (unused) | `to:` `null` | omit `To` |
| `from` (range start) | `dateSentQueryQuery:` parsed `DateTimeOffset` | `DateSent>` |
| `to` (range end) | `dateSentQuery:` parsed `DateTimeOffset` | `DateSent<` |
| | `dateSent:` `null` | omit exact `DateSent` |

**`ListMessageResponse` (C# (wire): type)** — envelope, not a bare list:

| Field | Type |
|---|---|
| `End (end)` | `int?` |
| `FirstPageUri (first_page_uri)` | `string?` |
| `NextPageUri (next_page_uri)` | `string?` |
| `Page (page)` | `int?` |
| `PageSize (page_size)` | `int?` |
| `PreviousPageUri (previous_page_uri)` | `string?` |
| `Start (start)` | `int?` |
| `Uri (uri)` | `string?` |
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` |

Line up provider rows using each `ApiV2010AccountMessage`: `Sid`, `To`, `From`, `Status`, `DateSent`, `Body`, `ErrorCode`, `ErrorMessage`, `Direction`.

#### Api20100401Message.DeleteMessage — out of scope for redaction

| | |
|---|---|
| HTTP | `DELETE /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Signature | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `void` (`Task`) |
| Notes | *"Deletes a Message resource from your account"* — would remove the fact of the send. **Do not use** for `DELETE /api/notifications/{id}/content`. |
| Cite | `operations/Api20100401Message.md` |

### 2.3 `ApiV2010AccountMessage` — create / fetch / update / list item

Namespace `TwilioSdk.Models`. Cite: `records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`. No extra envelope field.

| C# (wire) | Type | Integration use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider id / `notificationId`. Regex `^(SM\|MM)[0-9a-fA-F]{32}$`, length 34. |
| `Status (status)` | `MessageEnumStatus?` | Delivery / schedule state. |
| `To (to)` | `string?` | Destination E.164. |
| `From (from)` | `string?` | Sender E.164 / sender ID. |
| `Body (body)` | `string?` | Text; empty after redact. |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT when sent (XML). |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT. |
| `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT. |
| `ErrorCode (error_code)` | `int?` | Set when `failed` / `undelivered`; else null. XML: do not treat code/message as a stable programmatic contract. |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code` when failed/undelivered. |
| `Direction (direction)` | `MessageEnumDirection?` | Outbound API sends: `OutboundApi`. |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | MG… length 34. |
| `AccountSid (account_sid)` | `string?` | AC… length 34. |
| `NumSegments (num_segments)` | `string?` | XML: initially `"0"` for Messaging Service until a sender is assigned. |
| `NumMedia (num_media)` | `string?` | |
| `Price (price)` | `string?` | |
| `PriceUnit (price_unit)` | `string?` | |
| `Uri (uri)` | `string?` | Relative to `https://api.twilio.com`. |
| `ApiVersion (api_version)` | `string?` | |
| `SubresourceUris (subresource_uris)` | `object?` | |

### 2.4 Enums in scope (`TwilioSdk.Models.Enums`, `StringEnum<T>` — not C# enums)

Use static members (or `FromValue("wire")` where public). `==` compares by value. Cite: `map/models/enums.md`.

**`MessageEnumStatus`** — `Models/Enums/MessageEnumStatus.cs`

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
| `Read` | `read` (WhatsApp only per XML) |
| `PartiallyDelivered` | `partially_delivered` |
| `Canceled` | `canceled` |

**`MessageEnumDirection`**

| Member | Wire |
|---|---|
| `Inbound` | `inbound` |
| `OutboundApi` | `outbound-api` |
| `OutboundCall` | `outbound-call` |
| `OutboundReply` | `outbound-reply` |

**`MessageEnumScheduleType`:** `Fixed` = `fixed` (only value). Messaging Services only, with `sendAt`.

**`MessageEnumUpdateStatus`:** `Canceled` = `canceled` (only value).

**`ValidationError`:** `TooShort` = `TOO_SHORT`, `TooLong` = `TOO_LONG`, `InvalidButPossible` = `INVALID_BUT_POSSIBLE`, `InvalidCountryCode` = `INVALID_COUNTRY_CODE`, `InvalidLength` = `INVALID_LENGTH`, `NotANumber` = `NOT_A_NUMBER`.

**`Field`** (batch lookup only): `CallerName` = `caller_name`, `SimSwap` = `sim_swap`, `CallForwarding` = `call_forwarding`, `LineTypeIntelligence` = `line_type_intelligence`, `LineStatus` = `line_status`, `IdentityMatch` = `identity_match`, `ReassignedNumber` = `reassigned_number`, `SmsPumpingRisk` = `sms_pumping_risk`.

**CreateMessage enums we pass as `null` (values if ever needed):** `MessageEnumContentRetention`: `Retain`/`retain`, `Discard`/`discard`. `MessageEnumAddressRetention`: `Retain`/`retain`, `Obfuscate`/`obfuscate`. `MessageEnumTrafficType`: `Free`/`free`. `MessageEnumRiskCheck`: `Enable`/`enable`, `Disable`/`disable`.

### 2.5 Error mapping for this product

Every in-scope operation is **Case B**. Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Read `ex.Error.StatusCode` and `ex.Error.ReadAsString()` (prefer string over `ReadAsJson<T>()` unless the body is known JSON). No `{Operation}Error` / `TryGet…` accessors. No `…Result` overloads.

| Situation | How it appears |
|---|---|
| Unusable number at **registration** | Lookup: `Valid == false` and/or `ValidationErrors`; or Lookup Case B (e.g. 404). Reject at `POST /api/contact-numbers`. |
| Unusable number at **create** | `CreateMessage` throws `SdkException<RawError>` (typically 4xx). Catch; **do not** fail the order operation; record outcome. |
| Message **accepted** then undeliverable (incl. US carrier refusal) | `CreateMessage` returns 2xx `ApiV2010AccountMessage` (`queued` / `accepted` / `sent` / …). Later `FetchMessage`: `Status` `failed` or `undelivered` + `ErrorCode` / `ErrorMessage`. Not a create failure. |
| Scheduled message already sent / not cancellable | `UpdateMessage` throws `SdkException<RawError>`. Read status + body. SDK does not list which statuses are cancellable. |
| Transport / timeout | `HttpRequestException` / `TaskCanceledException` — **not** `SdkException<T>`. |

---

## 3. Trap notes

⚠ Step 1 (client registration) — the SDK does not own `HttpClient`; `AddTwilioSdkClient` lifetime and the unnamed factory client decide whether handler rotation ever reaches this client. **MUST load `dotnet-client-initialization`** before writing the factory or DI registration.

⚠ Step 1 (client registration) — `RetryOptions.Timeout` and the `HttpClient.Timeout` you register are not the same knob and do not bound the same interval. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 1 (BaseUrl) — `Twilio:BaseUrl` must be applied only to the messaging server node (`options.Server.Default.Production.BaseUrl`); Lookup lives on a different server node (`Default4`). Environment vs `Server` are not read at the same time. **MUST load `dotnet-configuration-resilience`** before setting any `BaseUrl`.

⚠ Step 1 (auth) — credentials belong on `AccountSidAuthToken` before construct / in the DI callback; `AuthToken` is a secret. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 2–9 (calls) — every nullable Create/Lookup/List parameter has **no C# default** and mis-binds in a positional call; the cancellation token is `ct`. **MUST load `dotnet-calling-endpoints`** before the first SDK call.

⚠ Steps 2–9 (models) — statuses, schedule type, update status, and validation errors are `StringEnum<T>` records, not C# enums; wire names differ from member names. **MUST load `dotnet-models`** before mapping `Status` / `Valid` / schedule fields.

⚠ Step 3 (CreateMessage / retries) — a failed or timed-out **write** may still have reached the provider; `CreateMessage` is `POST` and the generated method always stamps a fresh `Idempotency-Key`. Whether that write can be re-sent, and what `HttpMethodsToRetry` actually gates, is not visible from the signature. **MUST load `dotnet-configuration-resilience`** before enabling retries around send/schedule/resend.

⚠ Step 9 (ListMessage) — there is no auto-paginator (`NextPageUri` / `pageToken` are manual). **MUST load `dotnet-configuration-resilience`** before writing reconciliation paging.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (error boundary) — Case B has no `TryGet…`; connection failures are not `SdkException<T>`; a send failure must be recorded without failing the order. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Tests — the constructor `HttpClient` is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, `HttpClient` ownership, `AddTwilioSdkClient` singleton / unnamed factory |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–9 — named arguments, `ct:`, reading envelopes |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, wire vs C# names, null vs empty body |
| `dotnet-configuration-resilience` | Step 1 BaseUrl per server; retries/timeouts; ListMessage pagination |
| `dotnet-error-handling` | Step 10 — Case B, JsonException both directions, transport vs SDK, status mapping |
| `dotnet-testing` | Tests — `HttpClient` / handler seam |

`dotnet-error-handling` always appears: every integration writes an error boundary. The two `JsonException` directions in Trap notes are load-bearing for that boundary.

---

## 5. Assumptions & Blockers

### Assumptions

- Lookups **v2** `FetchPhoneNumber3` is the registration lookup (v1 has no `Valid` / typed validation errors).
- Canonical stored number is `LookupResponse.PhoneNumber` (E.164), not the caller's raw input.
- Immediate outbound SMS uses `from` = `Twilio:FromNumber` and does **not** send `messagingServiceSid`.
- Scheduled follow-up uses `messagingServiceSid` = `Twilio:MessagingServiceSid` + `MessageEnumScheduleType.Fixed` + `sendAt`.
- `Twilio:AccountSid` → Basic `Username`; `Twilio:AuthToken` → Basic `Password`.
- `Twilio:BaseUrl`, when set, is assigned verbatim to **`options.Server.Default.Production.BaseUrl` only**.
- `statusCallback` is always `null`.
- Content redaction is `UpdateMessage` with `body: ""`, not `DeleteMessage`.
- Registration rejects when `Valid == false` (and/or Lookup throws). `LineTypeIntelligence.Type` is **not** treated as an SDK-defined SMS-capability enum.

### Blockers / gaps (not invented)

1. **Caller-supplied idempotency key (feature 6) is not exposable.** `CreateMessage` hardcodes `new HeaderParam("Idempotency-Key", Guid.NewGuid())`. `TwilioSdk.Core.RequestOptions` has only `LogLevel`. There is no CreateMessage parameter and no header override. Repeating `POST /api/notifications/{id}/resend` under the same app key **cannot** be made into a Twilio-level no-op via this SDK. Do not invent HttpClient header injection.

2. **Schedule window min/max is not in the SDK.** `CreateMessage` XML for `sendAt` / `scheduleType` is empty. `MessageEnumScheduleType` only states Messaging Services + `fixed`. No minimum (e.g. minutes from now) or maximum (e.g. days ahead) appears in the map or the named source files.

3. **Cancellable statuses are not listed.** Update notes say "cancel not-yet-sent messages"; `MessageEnumUpdateStatus` has only `Canceled`. The SDK does not name which `MessageEnumStatus` values accept that update, nor the error body when already sent.

4. **“Usable SMS destination” is not a typed SDK flag.** `Valid` is assignable-range validity. `LineTypeIntelligenceInfo.Type` and `LineStatusInfo.Status` are `string?` with **no enum value list** in the map or source. The SDK cannot name which line types are SMS-capable.

5. **Official Twilio docs’ `X-Twilio-Idempotency-Key` is not this SDK’s header.** The generated client sends `Idempotency-Key` (no `X-Twilio-` prefix), always random.
