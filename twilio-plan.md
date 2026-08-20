# Twilio .NET SDK plan — eShopOnWeb order SMS notifications

NuGet: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace: `TwilioSdk`. Client: `TwilioSdk.TwilioSdkClient`. Map provenance: source commit `51fdf48`.

Nothing is pre-seeded on the Twilio account. There is no publicly reachable URL — pass `statusCallback: null` on every create. Delivery outcome is obtained only by fetch/list.

---

## Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Register `TwilioSdkClient` in ASP.NET Core DI from config `Twilio:` (AccountSid, AuthToken, FromNumber, MessagingServiceSid, optional BaseUrl). Override **messaging** host only. | client construction — no API call |
| 2 | `POST /api/contact-numbers` — look up the typed number; reject unusable destinations; store the provider E.164 form. | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Order placed / dispatched / cancelled / resend — send outbound SMS. Catch send failures at the notification boundary so the order operation still succeeds. Persist `Sid` + `Status`. | `Api20100401Message.CreateMessage` (immediate) |
| 4 | On dispatch — queue a follow-up SMS with the provider for a few days later. Persist the scheduled message `Sid`. | `Api20100401Message.CreateMessage` (`scheduleType` + `sendAt` + `messagingServiceSid`) |
| 5 | On order cancel — cancel a follow-up that has not gone out. | `Api20100401Message.UpdateMessage` (`status: Canceled`) |
| 6 | Poll current delivery outcome by stored SID (no webhooks). | `Api20100401Message.FetchMessage` |
| 7 | `DELETE /api/notifications/{id}/content` — clear body at the provider; keep the resource (SID/status). **Do not** call `DeleteMessage`. | `Api20100401Message.UpdateMessage` (`body: ""`) |
| 8 | `GET /api/notifications/reconciliation?from=&to=` — list this app’s From-number messages over the ISO-8601 range, all pages. | `Api20100401Message.ListMessage` |
| 9 | `POST /api/notifications/{id}/resend` — idempotency is **application-side** (see CONTRACT SHEET §9). Send via `CreateMessage`. | `Api20100401Message.CreateMessage` |

`DeleteMessage` (`DELETE …/Messages/{Sid}.json`) exists on the same controller and **removes the resource**. It is out of scope for redaction because the fact of the send would not survive.

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

All in-scope operations are **throw-only** (no `…Result` variant). All are **Case B**: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`.

`TwilioSdk.Core.RequestOptions` has a single member `LogLevel` (`Microsoft.Extensions.Logging.LogLevel?`). It cannot carry headers, idempotency keys, or If-Match. (`Core/RequestOptions.cs`)

---

### Client construction, auth, servers

| Fact | Value | Cite |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers the client as **singleton**, builds options, calls `IHttpClientFactory.CreateClient()` (unnamed/default client) | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). `ServerEnvironment.Default()` returns `Production`. Only environment. | `Servers/ServerEnvironment.cs` |
| Auth scheme | Basic. `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = …, Password = … }` (`Username` and `Password` are `required string`). XML on the options property: API key as username + API key secret as password, **or** Account SID + auth token (limit SID/token to local testing). This app’s config is `Twilio:AccountSid` / `Twilio:AuthToken` → Username / Password. | `sdk-map.md` *Servers & auth*, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Messaging host (Default / api) | Message create/fetch/list/update use `_server.Default(...)`. Default BaseUrl: `https://api.twilio.com`. Override: `options.Server.Default.Production.BaseUrl` (`TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl: string`). When `Twilio:BaseUrl` is set, assign it **verbatim** here. When unset, leave the default. | `Api/Api20100401Message.cs`, `Servers/DefaultOptions.cs`, `ServerOptions.cs` |
| Lookup host (Default4 / lookups) | Lookup uses `_server.Default4(...)`. Default BaseUrl: `https://lookups.twilio.com`. Property: `options.Server.Default4.Production.BaseUrl`. **Do not** point this at `Twilio:BaseUrl` — that setting governs messaging only. | `Api/LookupsV2PhoneNumber.cs`, `Servers/Default4Options.cs` |
| `TwilioSdk.ServerOptions` | Root namespace (`ServerOptions.cs`). Properties: `Default`, `Default1` … `Default14` (each a `*Options` type under `TwilioSdk.Servers`). Only `Default` (messaging API) and `Default4` (lookups) are in scope. | `ServerOptions.cs` |
| Path Account SID | Every Message operation’s first param `accountSid` is the path `{AccountSid}`. Use `Twilio:AccountSid`. | `operations/Api20100401Message.md` |
| From / Messaging Service | `Twilio:FromNumber` → `CreateMessage` `from` / `ListMessage` `from`. `Twilio:MessagingServiceSid` → `CreateMessage` `messagingServiceSid` (required for scheduling — see CreateMessage row). | config + operation rows below |

---

### 1. Phone number lookup / validation — `FetchPhoneNumber3`

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` on **Default4 (lookups)** |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | 15 params `fields` … `partnerSubId` — nullable, no C# default; pass `null` to skip |
| Required | `phoneNumber` (non-nullable). XML: E.164 or national format; default country code +1 if national. Pass `countryCode` (ISO 3166-1 alpha-2) when the caller typed a national number. |
| `fields` | `string?` (comma-separated). XML possible values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. This integration needs SMS-usability: pass `"line_type_intelligence"` (add `,line_status` if line-active status is also required). This is **not** the `Field` enum — that enum is on a different request model (`LookupRequestWithCorId`). |
| Query wire ← C# | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity-match / reassigned / pre_fill params stay `null` here) |
| Returns | `TwilioSdk.Models.LookupResponse` (**no extra envelope wrapper**) |
| Error | Case B `SdkException<RawError>` |
| Pagination | none |
| Cite | `operations/LookupsV2PhoneNumber.md`, `Api/LookupsV2PhoneNumber.cs`, `records-4-Li-Me.md` |

**`LookupResponse` fields this step reads** (`TwilioSdk.Models`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber` (`phone_number`) | `string?` | Canonical **E.164** (`+` + country code + subscriber). **This is what gets stored**, not the caller’s typed input. |
| `NationalFormat` (`national_format`) | `string?` | National form (do not store as canonical). |
| `CallingCountryCode` (`calling_country_code`) | `string?` | E.164 prefix |
| `CountryCode` (`country_code`) | `string?` | ISO country code |
| `Valid` (`valid`) | `bool?` | XML: whether the number is in a range a carrier can freely assign. |
| `ValidationErrors` (`validation_errors`) | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid. |
| `LineTypeIntelligence` (`line_type_intelligence`) | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence`. |
| `LineStatus` (`line_status`) | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status`. |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`, `Models/LineTypeIntelligenceInfo.cs`): `MobileCountryCode` (`mobile_country_code`): `string?` · `MobileNetworkCode` (`mobile_network_code`): `string?` · `CarrierName` (`carrier_name`): `string?` · `Type` (`type`): `string?` · `ErrorCode` (`error_code`): `int?`.

There is **no** `sms` / `mms` boolean on `LookupResponse` or `LineTypeIntelligenceInfo`. Incoming-number `Capabilities.Sms` models are a different API and are not returned here.

**`LineStatusInfo`**: `Status` (`status`): `string?` · `ErrorCode` (`error_code`): `int?`. `Status` is an untyped string (no generated enum).

**Detect “not a usable SMS destination” vs other errors**

| Condition | How | Action |
|---|---|---|
| Invalid number (provider says not a real assignable number) | 2xx `LookupResponse` with `Valid == false` and/or non-empty `ValidationErrors` | Reject at registration. Not an `SdkException`. |
| Lookup package failed | `LineTypeIntelligence.ErrorCode` or `LineStatus.ErrorCode` is non-null (and `Type`/`Status` missing) | Cannot confirm SMS usability — reject or fail closed. |
| Unusable line type | `LineTypeIntelligence.Type` is a `string?` with **no generated enum on this model**. A separate enum `TwilioSdk.Models.Enums.LineType` (`mobile`, `landline`, `tollFree`, `fixedVoip`, `nonFixedVoip`, `personal`, `premium`, `voicemail`, `sharedCost`, `uan`, `pager`, `unknown`) belongs to `OverridesRequest`, **not** this field. Treat non-mobile `Type` strings as unusable as an **application rule**. **UNVERIFIED** that live Lookup `type` strings match `LineType` wire values. | Reject at registration when `Type` is present and not an SMS-capable line. |
| HTTP failure (auth, not found, 4xx/5xx) | `SdkException<RawError>` — `ex.Error.StatusCode`, body via `ReadAsString()` / `ReadAsJson<T>()` | 401/403 → auth config. Other statuses → registration error, not “invalid number” unless the body says so. |
| Malformed 2xx body | `System.Text.Json.JsonException` (not `SdkException`) | See trap notes. |

`ValidationError` members (`enums.md`, `Models/Enums/ValidationError.cs`): `TooShort` (`TOO_SHORT`), `TooLong` (`TOO_LONG`), `InvalidButPossible` (`INVALID_BUT_POSSIBLE`), `InvalidCountryCode` (`INVALID_COUNTRY_CODE`), `InvalidLength` (`INVALID_LENGTH`), `NotANumber` (`NOT_A_NUMBER`).

Lookups v1 `FetchPhoneNumber2` (`LookupsV1PhoneNumberApi`) returns `LookupsV1PhoneNumber` with `PhoneNumber` / `NationalFormat` / untyped `Carrier: object?` and **no** `Valid` / `LineTypeIntelligence`. It is **not** sufficient for this step.

---

### 2 & 3. Send SMS and schedule follow-up — `CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)** |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | 24 params `statusCallback` … `contentSid` — nullable, no C# default |
| Required non-nullable | `accountSid`, `to` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (**no extra envelope**) |
| Error | Case B `SdkException<RawError>` — **this is what the notification boundary catches** so a failed send does not fail the order |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs` |

**From vs MessagingServiceSid vs both**

| Param | Wire | Type | Immediate send (placed / dispatched / cancelled / resend) | Scheduled follow-up |
|---|---|---|---|---|
| `from` | `From` | `string?` | Pass `Twilio:FromNumber`. | Optional; scheduling docs are Messaging Service–scoped. |
| `messagingServiceSid` | `MessagingServiceSid` | `string?` | Optional. Pass `Twilio:MessagingServiceSid` if that is how this account sends; the SDK does not require it when `from` is set. | **Pass `Twilio:MessagingServiceSid`.** `MessageEnumScheduleType` XML: *“For Messaging Services only”*. |
| `fallbackFrom` | `FallbackFrom` | `string?` | `null` | `null` |

Neither `from` nor `messagingServiceSid` is required in the C# signature. The SDK will omit a param when its value is `null`. **UNVERIFIED** whether the live API accepts both together; passing both is allowed by the signature.

**Immediate send — named arguments to set (rest `null`)**

- `accountSid:` `Twilio:AccountSid`
- `to:` stored E.164
- `from:` `Twilio:FromNumber`
- `messagingServiceSid:` `Twilio:MessagingServiceSid` or `null` (see table)
- `body:` notification text
- `statusCallback:` `null` (no public URL)
- `scheduleType:` `null`, `sendAt:` `null`

**Scheduled follow-up — additional params**

| Param | Wire | Type | Value |
|---|---|---|---|
| `scheduleType` | `ScheduleType` | `TwilioSdk.Models.Enums.MessageEnumScheduleType?` | `MessageEnumScheduleType.Fixed` (only member; wire `fixed`) |
| `sendAt` | `SendAt` | `DateTimeOffset?` | Future instant. **Not** `DateTime`. Offset is preserved through form encoding (value is JSON-serialized then flattened to a string). Pass a UTC `DateTimeOffset`. |
| `messagingServiceSid` | `MessagingServiceSid` | `string?` | `Twilio:MessagingServiceSid` (see enum XML: Messaging Services only) |

`MessageEnumScheduleType` XML still says “in conjunction with the `send_time` parameter”; the generated parameter is **`sendAt` / wire `SendAt`**. There is no `send_time` parameter.

**Schedule window (min / max):** **not documented** in the map, the enum, or `CreateMessage` XML. Do not invent a window. If the provider rejects the time, it arrives as Case B `SdkException<RawError>` — catch at the notification boundary. (`enums.md` `MessageEnumScheduleType`, `Api/Api20100401Message.cs`)

**Status that means scheduled / not yet sent:** `TwilioSdk.Models.Enums.MessageEnumStatus.Scheduled` (wire `scheduled`). Capture `Sid` from the returned `ApiV2010AccountMessage` for later cancel/fetch.

**Internal header (not a caller parameter):** `CreateMessage` always sends `Idempotency-Key: Guid.NewGuid()` built inside the method. The caller cannot set or reuse this key. See §9.

---

### 4. Cancel scheduled follow-up — `UpdateMessage` (status)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` on **Default (api)** |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `body`, `status` |
| Cancel call | `body: null` (omitted on the wire — flattener drops nulls), `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (only member; wire `canceled`) |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — read `Status` (expect `MessageEnumStatus.Canceled` / wire `canceled`) and `Sid` |
| Error | Case B `SdkException<RawError>` |
| Cite | `operations/Api20100401Message.md`, `enums.md` `MessageEnumUpdateStatus` |

**Already sent / cannot cancel:** the SDK has no typed “already sent” error. Failure is Case B. Read `ex.Error.StatusCode` and `ex.Error.ReadAsString()`. Then `FetchMessage` and inspect `Status` — if it is `Sent` / `Delivered` / `Failed` / `Undelivered` (etc., not `Scheduled`), the follow-up already left the scheduled state. **UNVERIFIED** exact HTTP status the live API uses for cancel-after-send.

Same method also always sends a fresh internal `Idempotency-Key: Guid.NewGuid()`.

---

### 5. Fetch delivery outcome — `FetchMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` on **Default (api)** |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | Case B. Missing SID → `SdkException<RawError>` with `StatusCode` (typically not-found). **UNVERIFIED** exact status without live traffic — branch on `ex.Error.StatusCode`. |
| Cite | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md` |

---

### 6. Redact body — `UpdateMessage` (body)

Same signature as §4.

| | |
|---|---|
| Redact call | `body: ""` (**empty string, not `null`** — `null` is dropped and sends no `Body` field), `status: null` |
| Returns | `ApiV2010AccountMessage` |
| After redaction | Re-read `Body`. The map/XML do not specify the post-redaction string. **UNVERIFIED** live value (empty vs placeholder). Assert that stored notification text is no longer present in `Body`. SID, `Status`, `ErrorCode` remain on the resource. |
| Do not use | `DeleteMessage` — that deletes the resource (`void` / `Task`), which would drop the send record the product needs to keep. |

---

### 7. List for reconciliation — `ListMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)** |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | 8 params `to` … `pageToken` |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | Case B |
| Pagination | **Not** auto-`IAsyncEnumerable`. Map: “none (only `page`, no `perPage`)”. Drive `page` / `pageToken` / `pageSize` yourself. |
| Cite | `operations/Api20100401Message.md`, `records-4-Li-Me.md`, `Api/Api20100401Message.cs` |

**Provider-side filters (do not fetch wider and filter in-process)**

| C# param | Wire | Maps to reconciliation query |
|---|---|---|
| `from` | `From` | `Twilio:FromNumber` — **required for this report** |
| `to` | `To` | `null` (not the date `to`) |
| `dateSent` | `DateSent` | `null` (exact-day filter; unused) |
| `dateSentQuery` | `DateSent<` | range **end** (`to` ISO-8601) — sent-date **before** |
| `dateSentQueryQuery` | `DateSent>` | range **start** (`from` ISO-8601) — sent-date **after** |
| `pageSize` | `PageSize` | `long?`. XML: default **50**, maximum **1000** |
| `page` | `Page` | `int?` — “client state” |
| `pageToken` | `PageToken` | `string?` — “provided by the API” |

List serialization of the three date params uses `DateTimeOffset.ToIso8601()` → UTC `yyyy-MM-ddTHH:mm:ss.fff'Z'`. XML still describes `YYYY-MM-DD` GMT forms; the **SDK sends the ISO-8601 timestamp above**. (`Api/Api20100401Message.cs`, `Core/Extensions/DateTimeOffsetExtensions.cs`)

**`ListMessageResponse` envelope** (`Models/ListMessageResponse.cs`):

| C# (wire) | Type |
|---|---|
| `Messages` (`messages`) | `IReadOnlyList<ApiV2010AccountMessage>?` — the page |
| `NextPageUri` (`next_page_uri`) | `string?` — stop when null/empty **and** apply an application page cap |
| `PageToken` is **not** a response field | Advance using `pageToken` on the next `ListMessage` call (from the API / URI) and/or `page` |
| `Page` (`page`), `PageSize` (`page_size`), `End` (`end`), `Start` (`start`), `FirstPageUri`, `PreviousPageUri`, `Uri` | paging metadata |

There is no SDK helper that GETs `NextPageUri` as a URL. Iterate by calling `ListMessage` again with the next `pageToken`/`page`.

---

### `ApiV2010AccountMessage` — fields the integration stores / reports

All nullable. Date fields are **`string?`**, RFC 2822 GMT per XML — not `DateTimeOffset`. (`records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`)

| C# (wire) | Type | Use |
|---|---|---|
| `Sid` (`sid`) | `string?` (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`) | Provider message id — persist |
| `Status` (`status`) | `MessageEnumStatus?` | Current outcome |
| `From` (`from`) | `string?` | Sender; reconciliation alignment |
| `To` (`to`) | `string?` | Destination E.164 |
| `Body` (`body`) | `string?` | Text; empty/absent after redact |
| `DateSent` (`date_sent`) | `string?` | RFC 2822 GMT when sent |
| `DateCreated` (`date_created`) | `string?` | RFC 2822 GMT created |
| `DateUpdated` (`date_updated`) | `string?` | RFC 2822 GMT updated |
| `ErrorCode` (`error_code`) | `int?` | Set when `failed` / `undelivered`; XML: do not treat code/message as a stable programmatic contract |
| `ErrorMessage` (`error_message`) | `string?` | Description of `error_code` |
| `MessagingServiceSid` (`messaging_service_sid`) | `string?` | MG… |
| `Direction` (`direction`) | `MessageEnumDirection?` | Outbound API → `OutboundApi` (`outbound-api`) |
| `AccountSid` (`account_sid`) | `string?` | AC… |
| `NumSegments` (`num_segments`) | `string?` | |
| `NumMedia` (`num_media`) | `string?` | |
| `Price` / `PriceUnit` / `Uri` / `ApiVersion` / `SubresourceUris` | as mapped | unused unless reporting needs them |

**Send accepted but later undeliverable:** `CreateMessage` returning 2xx with `Status` `Queued` / `Accepted` / `Sending` / `Sent` is **not** final. US numbers on this account that later fail are expected: `FetchMessage` / list will show `Undelivered` or `Failed` plus `ErrorCode` / `ErrorMessage`. That is not a lookup gap and not an `SdkException` on create.

---

### Enums in scope (`TwilioSdk.Models.Enums`, `enums.md`)

**`MessageEnumStatus`** (`StringEnum`; compare via static members, not C# `enum`):

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

Scheduled / not yet sent = `Scheduled`. Terminal-ish for this app: `Delivered`, `Undelivered`, `Failed`, `Canceled` (plus WhatsApp `Read` — unused).

**`MessageEnumDirection`:** `Inbound` (`inbound`), `OutboundApi` (`outbound-api`), `OutboundCall` (`outbound-call`), `OutboundReply` (`outbound-reply`).

**`MessageEnumScheduleType`:** `Fixed` (`fixed`) only.

**`MessageEnumUpdateStatus`:** `Canceled` (`canceled`) only.

**`Field`** (batch lookup only — do not pass this type into `FetchPhoneNumber3`): `CallerName` (`caller_name`), `SimSwap` (`sim_swap`), `CallForwarding` (`call_forwarding`), `LineTypeIntelligence` (`line_type_intelligence`), `LineStatus` (`line_status`), `IdentityMatch` (`identity_match`), `ReassignedNumber` (`reassigned_number`), `SmsPumpingRisk` (`sms_pumping_risk`).

---

### 9. Idempotent resend

`CreateMessage` has **no** caller-facing idempotency / If-Match / unique-name parameter. `RequestOptions` cannot add headers.

The generated method **always** attaches `HeaderParam("Idempotency-Key", Guid.NewGuid())` internally (`Api/Api20100401Message.cs`). A caller-supplied key is impossible; each `CreateMessage` invocation gets a new key.

**Implement idempotency in the application** (store the caller key → existing notification / provider SID; do not call `CreateMessage` again under a seen key).

The internal header may still make **SDK transport retries of the same in-flight request** share one key if the HTTP request object is reused; that does not satisfy the HTTP API’s resend idempotency requirement. ⚠ transport-retry hazard — **MUST load `dotnet-configuration-resilience`**.

---

### 10. Errors (every in-scope operation)

Every operation in this sheet throws **`TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`** (Case B). No `{Operation}Error` / `TryGet…` accessors.

| Read | How |
|---|---|
| HTTP status | `ex.Error.StatusCode` (`System.Net.HttpStatusCode`) |
| Raw body | `ex.Error.ReadAsString()` |
| Parsed body | `ex.Error.ReadAsJson<T>()` (define `T` locally if needed — the SDK has **no** generated rest-error record for these ops) |
| Bytes | `ex.Error.ReadAsBytes()` |

`SdkException<TError>` members: `Error` only (`Core/Exceptions/SdkException.cs`). `RawError` has **no** `TryGetRawError`.

| Scenario | Surface |
|---|---|
| Invalid / unusable destination at **lookup** | Prefer 2xx `Valid == false` / `ValidationErrors` / non-mobile `Type`. HTTP errors are Case B (auth, 4xx/5xx) — not the same as `Valid == false`. |
| Send rejected immediately | `CreateMessage` Case B — catch at notification boundary; order still succeeds. |
| Send accepted, later undeliverable | No exception on create. Later `FetchMessage` / list: `Status` `undelivered` / `failed`, `ErrorCode` / `ErrorMessage`. Expected for US numbers on this account. |
| Cancel already-sent | `UpdateMessage` Case B and/or fetch shows non-`scheduled` status. |
| Message not found | `FetchMessage` / `UpdateMessage` / `ListMessage` Case B — branch on `StatusCode`. |
| Auth 401/403 | Case B `StatusCode` `Unauthorized` / `Forbidden` on **any** op. Check `AccountSidAuthToken` Username/Password and that messaging vs lookup hosts are correct. |

No-throw variants: **absent**.

---

## Trap notes

⚠ Step 1 (client registration) — `AddTwilioSdkClient` and the `HttpClient` you pass into `TwilioSdkClient` have **lifetime and ownership rules the ctor does not state**; getting them wrong shares or churns handlers across the app. **MUST load `dotnet-client-initialization`** before wiring DI.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a nullable credentials object whose Username/Password mapping is not “just the config key names”; a 401/403 on every call is the cost of wiring it from memory. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 1 (BaseUrl) — `Twilio:BaseUrl` is a **per-server, per-environment** slot (`Server.Default.Production`), not a single client-wide host; setting the wrong server node leaves lookups or messaging on the default host (or the reverse). **MUST load `dotnet-configuration-resilience`** before assigning `BaseUrl`.

⚠ Step 1 (retries / timeout) — SDK retry and timeout options do **not** bound a whole notification action (send + schedule, or a page loop) and are **not** the timeout on the `HttpClient` you register; an order handler that swallows send failures can still burn the request budget. **MUST load `dotnet-configuration-resilience`** before registering the client.

⚠ Step 1 / 3 (CreateMessage is POST) — a transport failure on create can still be retried by the pipeline even when status-retries exclude POST; a duplicate SMS is the cost. **MUST load `dotnet-configuration-resilience`** before the first `CreateMessage`.

⚠ Steps 2–8 (every call) — 24 / 15 / 8 nullable parameters have **no C# default**; a positional call mis-binds. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ Steps 2–8 (models) — statuses, schedule type, validation errors, and direction are `StringEnum<T>` records with `FromValue` / static members, and several timestamps on `ApiV2010AccountMessage` are `string?` (RFC 2822), not `DateTimeOffset`. **MUST load `dotnet-models`** before mapping SDK records onto eShop types.

⚠ Steps 2–8 (error boundary) — every op is Case B `SdkException<RawError>`; catching a typed `{Op}Error` or calling `TryGetRawError` on `RawError` compiles wrong or misses the payload, and the order/notification boundary then lets provider failures escape (or, for send, fail the order). **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Steps 2–8 (JsonException, 2xx) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Steps 2–8 (JsonException, non-2xx) — a **non-2xx** body that does not match a generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 8 (reconciliation pages) — `ListMessage` does not auto-paginate; `NextPageUri` / `pageToken` are provider-supplied stop conditions. An unbounded page loop does not return. **MUST load `dotnet-configuration-resilience`** before writing the list loop.

⚠ Tests — the SDK ships no mocks; the `HttpClient` constructor argument is the seam, and tests that fake controller types couple to generated internals. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `TwilioSdkClient` ctor, `AddTwilioSdkClient`, `HttpClient` / `IHttpClientFactory` lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials`, 401/403 |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, must-pass-explicitly nullables, `ct:` |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, record nullability, wire names vs C# names |
| `dotnet-error-handling` | Steps 2–8 — Case B `RawError`, catch ladder, **both** `JsonException` directions below |
| `dotnet-configuration-resilience` | Step 1 BaseUrl / retries / timeouts; Step 3 POST transport retries; Step 8 pagination bounds |
| `dotnet-testing` | Test seam for the integration layer |

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Lookup uses **v2** `FetchPhoneNumber3`, not v1, because only v2 exposes `Valid`, E.164 `PhoneNumber`, and `LineTypeIntelligence`.
- Immediate SMS sets `from` from `Twilio:FromNumber`. Scheduled follow-up sets `messagingServiceSid` from `Twilio:MessagingServiceSid` because `MessageEnumScheduleType` is documented as Messaging Services only.
- `statusCallback` is always `null` (no public URL, no webhooks).
- Path `accountSid` and Basic `Username` both come from `Twilio:AccountSid`; Basic `Password` from `Twilio:AuthToken`.
- `Twilio:BaseUrl`, when set, is assigned only to `options.Server.Default.Production.BaseUrl` (messaging). Lookups stay on `https://lookups.twilio.com` unless separately changed (they must not be).
- Redaction is `UpdateMessage(body: "")`, not `DeleteMessage`.
- Resend idempotency is implemented in the application (store caller key).

**Blockers / gaps (do not invent a workaround)**

- **Schedule window:** the SDK/map does not document minimum or maximum `SendAt`. Out-of-window is only visible as Case B on create. **UNVERIFIED** live min/max.
- **SMS capability boolean:** Lookup does not return `Capabilities.Sms`. Usability is `Valid` + `LineTypeIntelligence.Type` (`string?`, no enum on that model). **UNVERIFIED** that live `type` strings match `LineType` wire values.
- **Caller idempotency key:** not exposed. Internal `Idempotency-Key` is a new GUID per `CreateMessage`/`UpdateMessage`/`DeleteMessage` invocation.
- **Post-redaction `Body`:** not specified beyond “redact Message body text”. **UNVERIFIED** exact string after a successful redact.
- **Cancel-already-sent / not-found HTTP statuses:** Case B only; exact codes **UNVERIFIED**. Branch on `StatusCode` and/or a follow-up `FetchMessage`.
- **`ListMessage` next-page token:** response has `NextPageUri` but no `PageToken` field. How the live URI encodes the next `pageToken` is **UNVERIFIED** — parse it from `NextPageUri` or use `page`; do not fetch an unfiltered account list.
