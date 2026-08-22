# eShopOnWeb PublicApi — Twilio SMS notifications (Twilio .NET SDK)

## Scope & sequence

1. **Package + client** — install `AsadAli.TwilioSdk` (version-less). Construct `TwilioSdk.TwilioSdkClient` with AccountSid/AuthToken from `Twilio:` config. When `Twilio:BaseUrl` is set, apply it only to the 2010 Messages host (`Server.Default`), never to Lookup (`Server.Default4`).
2. **Lookup / validate destination** — `client.LookupsV1PhoneNumberApi` is the older host; **use `client.LookupsV2PhoneNumber.FetchPhoneNumber3`**. Persist the canonical E.164 `PhoneNumber`. Reject unusable destinations at registration.
3. **Send SMS immediately** — `client.Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`, `messagingServiceSid: null`, `scheduleType: null`, `sendAt: null`. Store `Sid` + `Status`.
4. **Queue follow-up (provider-side schedule)** — same `CreateMessage` with `scheduleType` = `Fixed`, `sendAt` = dispatch+days, and **`messagingServiceSid` = `Twilio:MessagingServiceSid`** (required for scheduling per the schedule-type contract). Store scheduled `Sid` + `Status`.
5. **Cancel scheduled follow-up** — `UpdateMessage` with `status` = `Canceled` (not delete).
6. **Refresh delivery outcome** — `FetchMessage` by stored Sid (no webhooks; `statusCallback: null` on create).
7. **Reconcile by sending number + date range** — `ListMessage` with `from` = `Twilio:FromNumber` and the `DateSent>` / `DateSent<` filters; page manually until `next_page_uri` is absent (SDK does not auto-paginate this op).
8. **Redact body at provider** — `UpdateMessage` with `body` = `""` (empty string), `status: null`. Do **not** `DeleteMessage` (that removes the resource, including delivery outcome).
9. **Error boundary** around every call (all in-scope ops are Case B `SdkException<RawError>`).
10. **Tests** at the `HttpClient` seam.

Credentials (bind, never hard-code): `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, optional `Twilio:BaseUrl`.

**Package / identity** (`sdk-map.md`): NuGet `AsadAli.TwilioSdk`; **root namespace is `TwilioSdk`** (not `Twilio`); client `TwilioSdk.TwilioSdkClient`; options `TwilioSdk.TwilioSdkClientOptions`. Controllers live in `TwilioSdk.Api`.

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

| Item | Contract | Cite |
|---|---|---|
| NuGet | `dotnet add package AsadAli.TwilioSdk` (do not pin a version from memory) | `sdk-map.md` |
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers a singleton client; creates `HttpClient` via `IHttpClientFactory.CreateClient()` with no named client | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment`; `Retry: TwilioSdk.Core.Configuration.RetryOptions`; `Logging: LoggingOptions`; `Server: TwilioSdk.ServerOptions`; `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Auth scheme | Basic auth. `new BasicAuthCredentials { Username = accountSid, Password = authToken }` (`required` init). Applied as `Authorization: Basic …`. XML on the property: API key as username + secret as password is preferred; Account SID + auth token is accepted (local-testing note in the XML). This app’s config is AccountSid + AuthToken — map those onto `Username` / `Password`. | `sdk-map.md` *Servers & auth*, `BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `"production"`). `Default()` returns `Production`. Only member. | `Servers/ServerEnvironment.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel { get; init; }`. No header bag, no base-URL override, no idempotency field. | `Core/RequestOptions.cs` |

**Server nodes (BaseUrl is per-server, not client-wide, not per-request):** `TwilioSdk.ServerOptions` (root namespace, `ServerOptions.cs`) holds one property per host. Nested `*.Production.BaseUrl` is the override. In-scope hosts:

| `options.Server.{Name}` | Type | Default `Production.BaseUrl` | Used by in-scope ops |
|---|---|---|---|
| `Default` | `TwilioSdk.Servers.DefaultOptions` | `https://api.twilio.com` | **All** `Api20100401Message` ops (create/fetch/list/update/delete) |
| `Default4` | `TwilioSdk.Servers.Default4Options` | `https://lookups.twilio.com` | **All** Lookup v1/v2 ops |
| `Default1` | `TwilioSdk.Servers.Default1Options` | `https://messaging.twilio.com` | **Not used** by any in-scope op (Messaging Services REST host). Do not point `Twilio:BaseUrl` here just because the name says “messaging”. |

**`Twilio:BaseUrl` rule:** when set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` **before** constructing the client. Do **not** assign it to `Default4` (Lookup must keep `https://lookups.twilio.com` unless a separate lookup override exists — it does not). Do not assign it to `Default1`…`Default14`. Cite: `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Server.cs` (`_server.Default(...)` vs `_server.Default4(...)`).

---

### 1. Lookup / validate SMS destination

**Use Lookups v2** (`FetchPhoneNumber3`). v1 (`FetchPhoneNumber2`) has no `Valid` flag and types `Carrier` as `object?`. Host is `Default4` (`lookups.twilio.com`) — **`Twilio:BaseUrl` must not be applied.**

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `FetchPhoneNumber3` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 nullable params (`fields` … `partnerSubId`) — pass `null` to skip |
| Returns | `TwilioSdk.Models.LookupResponse` (no extra envelope — the record *is* the body) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `ex.Error.StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| Pagination | none |
| Cite | `operations/LookupsV2PhoneNumber.md`, `Api/LookupsV2PhoneNumber.cs` |

**Request (query wire ← C#):** `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity-match PII fields (pass `null` for SMS validation). Path: `{PhoneNumber}` = the number to look up (E.164 or national; XML: default country +1).

**`fields` value for this app:** comma-separated `"line_type_intelligence,line_status"`. XML allowed tokens include `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. Enum `TwilioSdk.Models.Enums.Field` members (if building the string from members): `LineTypeIntelligence` (`line_type_intelligence`), `LineStatus` (`line_status`). `Field` does **not** include `validation` — `Valid` is always a property on the response model.

**`LookupResponse` fields this integration reads** (`records-4-Li-Me.md`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical E.164** (`+` + country code + subscriber). Persist this as the destination. |
| `NationalFormat (national_format)` | `string?` | National display form (not the canonical store value). |
| `Valid (valid)` | `bool?` | XML: “in a valid range that can be freely assigned by a carrier to a user.” **Not** an SMS-capability flag. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<ValidationError>?` | Why invalid. |
| `LineTypeIntelligence (line_type_intelligence)` | `LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence`. |
| `LineStatus (line_status)` | `LineStatusInfo?` | Populated when `fields` includes `line_status`. |
| `CallingCountryCode (calling_country_code)` / `CountryCode (country_code)` | `string?` | Dial prefix / ISO country. |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, **`Type (type): string?`** (untyped string — **not** `LineType`), `ErrorCode (error_code): int?`.

**`LineStatusInfo`:** `Status (status): string?` (untyped — no enum on this model), `ErrorCode (error_code): int?`.

**`ValidationError`** (`enums.md`) — `StringEnum`; members / wire: `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

**How “usable SMS destination” is determined (settled):** the SDK has **no** `canReceiveSms` (or equivalent) boolean. Combine:

1. **Format/range:** reject unless `Valid == true`. If `ValidationErrors` is non-empty, reject (surface those members).
2. **Line type (SMS-relevance):** `Type` is a raw string. Compare to `TwilioSdk.Models.Enums.LineType` **wire** values (`mobile`, `landline`, `tollFree`, `fixedVoip`, `nonFixedVoip`, `personal`, `premium`, `voicemail`, `sharedCost`, `uan`, `pager`, `unknown`) — that enum is **not** the declared type of `Type`, so do not assign `LineType` into the property; compare strings / `LineType.FromValue`. **Accept `mobile` as a usable SMS destination.** Reject `landline`, `pager`, `voicemail`, `uan`, `premium`, `sharedCost`. `fixedVoip` / `nonFixedVoip` / `personal` / `tollFree` / `unknown` are **UNVERIFIED** for SMS — reject at registration (safer default) unless product later allows them.
3. **Package failure:** if `LineTypeIntelligence.ErrorCode` is non-null, the line-type package failed — do not treat the number as usable.
4. **HTTP failure:** any non-2xx is `SdkException<RawError>` (Case B). Treat as rejectable at registration. Exact live statuses for garbage vs well-formed-unassigned numbers are **UNVERIFIED**; read `ex.Error.StatusCode` + `ReadAsString()`.

v1 (do not use for this gate): `FetchPhoneNumber2(string phoneNumber, string? countryCode, IReadOnlyList<string>? type, IReadOnlyList<string>? addOns, object? addOnsData, …)` also on Default4; returns `LookupsV1PhoneNumber` with `PhoneNumber (phone_number)` canonical E.164 but **no `Valid`**. Cite: `operations/LookupsV1PhoneNumberApi.md`.

---

### 2. Send SMS immediately + 3. Schedule follow-up (same operation)

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Method | `CreateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 24 nullable params (`statusCallback` … `contentSid`) — pass `null` to skip |
| Body encoding | `application/x-www-form-urlencoded` (not JSON) |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (no wrapper envelope) |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs` |

**Form fields (wire ← C#):** `To` ← `to` (required `string`), `From` ← `from`, `Body` ← `body`, `MessagingServiceSid` ← `messagingServiceSid`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the other 18 skippable fields. **This app: `statusCallback: null`** (no publicly reachable URL).

**Immediate send (step 2) — exact args that differ:**

- `accountSid`: `Twilio:AccountSid`
- `to`: canonical E.164 from lookup
- `from`: `Twilio:FromNumber`
- `messagingServiceSid`: **`null`** (send with From only)
- `body`: order-status text
- `scheduleType`: `null`
- `sendAt`: `null`
- every other nullable: `null`

**Scheduled follow-up (step 3) — exact required params:**

The SDK does **not** C#-require `messagingServiceSid` (`string?`). **`MessageEnumScheduleType` XML states scheduling is “For Messaging Services only”** and must be sent as `fixed` together with the send-time parameter (the C# parameter is `sendAt` / wire `SendAt`; the enum XML says `send_time` — ignore that name, use `sendAt`).

Pass:

- `accountSid`, `to`, `body` as above
- `scheduleType`: `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (only member; wire `fixed`)
- `sendAt`: `DateTimeOffset` of the intended send (serialized as form field `SendAt`)
- `messagingServiceSid`: **`Twilio:MessagingServiceSid` (required for schedule)**
- `from`: `Twilio:FromNumber` or `null` (Messaging Service picks a sender if omitted). Passing both is allowed by the signature.
- all other nullables: `null`

**Schedule window:** the SDK encodes **no** min/max on `sendAt` (type `DateTimeOffset?` only). Out-of-window or missing-Messaging-Service rejects surface as Case B `SdkException<RawError>`. Live min/max (minutes vs days) is **UNVERIFIED** in this SDK — do not invent a client-side window; handle provider 4xx via `RawError.StatusCode` + body.

**`ApiV2010AccountMessage` fields to persist / later refresh** (`records-1-Ac-Ca.md`):

| C# (wire) | Type |
|---|---|
| `Sid (sid)` | `string?` — provider message id (`SM…` / `MM…`) |
| `Status (status)` | `MessageEnumStatus?` |
| `ErrorCode (error_code)` | `int?` |
| `ErrorMessage (error_message)` | `string?` |
| `DateCreated (date_created)` | `string?` (RFC 2822 GMT) |
| `DateSent (date_sent)` | `string?` (RFC 2822 GMT; null until sent) |
| `DateUpdated (date_updated)` | `string?` |
| `From (from)` / `To (to)` / `Body (body)` | `string?` |
| `MessagingServiceSid (messaging_service_sid)` | `string?` |
| `Direction (direction)` | `MessageEnumDirection?` |
| `NumSegments (num_segments)` / `NumMedia (num_media)` | `string?` |
| `AccountSid (account_sid)` | `string?` |

No extra envelope field — read these on the returned record directly.

**`MessageEnumStatus`** (`enums.md`) members / wire: `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, **`Scheduled (scheduled)`**, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`. Immediate create typically returns `queued`/`accepted`; scheduled create returns **`scheduled`**.

**`MessageEnumDirection`:** `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

Other create enums (pass `null` unless needed): `MessageEnumContentRetention`: `Retain (retain)`, `Discard (discard)`. `MessageEnumAddressRetention`: `Retain (retain)`, `Obfuscate (obfuscate)`. `MessageEnumTrafficType`: `Free (free)`. `MessageEnumRiskCheck`: `Enable (enable)`, `Disable (disable)`.

---

### 4. Cancel a scheduled message + 7. Redact body (same operation, different args)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| Method | `UpdateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` (nullable, no default) |
| Form wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Notes (XML) | “used to redact Message `body` text and to cancel not-yet-sent messages” |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs` |

**Cancel (step 4):** `body: null`, `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (only member; wire `canceled`). Empty string **is** serialized by the form flattener (`null` is omitted; `""` is sent) — so cancel **must** pass `body: null`, not `""`.

If the message already left `scheduled` (already sent/queued for send), the SDK still issues the POST; the provider rejects. That is Case B — read `ex.Error.StatusCode` + `ReadAsString()`. Exact live code for “already sent” is **UNVERIFIED**. After a throw, `FetchMessage` and inspect `Status` (e.g. `sent` / `delivered` / `failed`) to record “too late to cancel”.

**Redact (step 7):** `body: ""` (empty string — **not** null, not a `Redact` flag; there is no `Redact` parameter), `status: null`. Empty string is included in the form body. Response is `ApiV2010AccountMessage`; `Sid` / `Status` / `ErrorCode` remain on the resource (do not call `DeleteMessage`). Post-redaction `Body` value is **UNVERIFIED** (expect empty/null; persist whatever the record returns). Delivery outcome is refreshed from `Status` / `ErrorCode` / `ErrorMessage` on that same record or a later fetch.

**Do not use `DeleteMessage`** for redact or cancel: `DeleteMessage(string accountSid, string sid, …)` `DELETE …/Messages/{Sid}.json`, returns `void`, Case B — removes the resource.

---

### 5. Fetch a single message

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| Method | `FetchMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) |
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` (same fields as create) |
| Error | **Case B** `SdkException<RawError>` (unknown Sid → this exception; live 404 is **UNVERIFIED**, read `StatusCode`) |
| Cite | `operations/Api20100401Message.md` |

Read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `DateUpdated`, `Body` (null/empty after redact).

---

### 6. List messages for a date range FROM this app’s number

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| Method | `ListMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 nullable params (`to` … `pageToken`) |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Auto-pagination | **none** (not `IAsyncEnumerable`; map: “only `page`, no `perPage`”) |
| Cite | `operations/Api20100401Message.md`, `Api/Api20100401Message.cs` |

**Query wire ← C# (inequality is in the *key*, not the value):**

| C# param | Wire | Meaning |
|---|---|---|
| `from` | `From` | **Required for this app:** `Twilio:FromNumber` (provider-side filter; do not list unfiltered and filter locally) |
| `to` | `To` | `null` (not filtering by recipient) |
| `dateSent` | `DateSent` | exact sent instant; `null` when using a range |
| `dateSentQuery` | **`DateSent<`** | **range end** (strictly before) |
| `dateSentQueryQuery` | **`DateSent>`** | **range start** (strictly after) |
| `pageSize` | `PageSize` | XML: default 50, **maximum 1000**; type `long?` |
| `page` | `Page` | “client state” |
| `pageToken` | `PageToken` | “provided by the API” |

Values are `DateTimeOffset?.ToIso8601()` → **`yyyy-MM-ddTHH:mm:ss.fffZ`** (UTC). XML comments mention `YYYY-MM-DD`; the generated client sends the full ISO-8601 timestamp above (`DateTimeOffsetExtensions.ToIso8601`). Pass range bounds as UTC `DateTimeOffset`.

**`ListMessageResponse` envelope** (`records-4-Li-Me.md`): `End (end): int?`, `FirstPageUri (first_page_uri): string?`, **`NextPageUri (next_page_uri): string?`**, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, **`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`**. Items are the same `ApiV2010AccountMessage` record.

**Paging the whole range:** there is **no** SDK enumerator. Loop: first call with `from`, `dateSentQueryQuery` (start), `dateSentQuery` (end), `pageSize` (e.g. `1000`), `page: null`, `pageToken: null`. While `NextPageUri` is non-null/non-empty, call again with the same filters + `pageToken` parsed from that URI’s `PageToken` query (and optionally `page` from `Page`). Stop when `NextPageUri` is null **and** carry an independent page cap (provider-supplied “no next page” is not a bound). The SDK does not parse `next_page_uri` for you.

---

### 8. Idempotency for operator resend

| Fact | Detail | Cite |
|---|---|---|
| Header the SDK sends | **`Idempotency-Key`** (not `I-Twilio-Idempotency-Token`) | `Api/Api20100401Message.cs` |
| Caller parameter | **None.** `CreateMessage` has no idempotency argument. | `operations/Api20100401Message.md` |
| How the SDK fills it | `new HeaderParam("Idempotency-Key", Guid.NewGuid())` on **CreateMessage**, **UpdateMessage**, and **DeleteMessage**. Value is a new `Guid` per **invocation**. Flattened with `Guid.ToString()`. | `Api/Api20100401Message.cs`, `ParameterFlattener` |
| `RequestOptions` | Cannot set headers. Only `LogLevel`. | `Core/RequestOptions.cs` |
| Same invocation, HTTP retry | The `Guid` is created when building the header list, **before** `Execute`. Pipeline retries of that same call reuse the same key. | `Api20100401Message.cs` + `RawClient` |
| Operator resend (second `CreateMessage`) | A **new** Guid is generated. The public API **cannot** replay a caller-supplied key. A second call is a second message as far as this SDK is concerned. | same |
| Replay response shape | Not observable through this SDK’s public surface (caller cannot send the same key). Live provider replay behaviour is **UNVERIFIED**. | — |

**Settled for implementation:** do not look for `Idempotency-Key` / `I-Twilio-Idempotency-Token` on the method. To make operator resend safe, **do not call `CreateMessage` again** when a Sid already exists for that key — `FetchMessage` instead. A `DelegatingHandler` that rewrites `Idempotency-Key` is outside the SDK contract (the SDK will have already set a Guid on the `HttpRequestMessage`).

---

### 10. Error model (what actually reaches catch)

All eight in-scope operations are **throw-only** (no `…Result` variants) and **Case B**.

| Situation | Type that reaches `catch` | How to read it |
|---|---|---|
| Any non-2xx (401 auth, 4xx invalid number / bad Sid / cannot cancel, 5xx) | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` | `ex.Error.StatusCode`; body: `ex.Error.ReadAsString()` or `ReadAsJson<T>()` / `ReadAsBytes()`. **No** typed `TryGet…` accessors (those are Case A only). `SdkException<T>` itself has only `required TError Error` — status is on `RawError`, not on the exception. |
| 2xx body missing a `required` member / malformed JSON | `System.Text.Json.JsonException` — **not** `SdkException` | See REQUIRED READING. |
| Non-2xx body that fails while constructing the error object | `JsonException` **replacing** `SdkException` (HTTP status destroyed) | See REQUIRED READING. Case B uses `RawError` from raw bytes, so this path is less likely than Case A, but the boundary must still not map every `JsonException` to 5xx. |
| Transport failure | `HttpRequestException` (and derivatives) | Not an `SdkException`. |
| Caller/token timeout | `TaskCanceledException` / `OperationCanceledException` | Retry pipeline does not retry these. |

`HttpStatusPolicy` (empty allowlist): only HTTP 200–299 are success; everything else becomes `SdkException<RawError>`. Auth failure is 401 in that bag (`IsUnauthorized` exists internally; callers still see Case B).

Do not parse `ex.ToString()` for status or body.

---

### Enums actually needed (literal C# member names)

All are `TwilioSdk.Models.Enums.*` `StringEnum<T>` — construct with the static member or `Type.FromValue("wire")`, never a C# `enum`.

See status / schedule / update-status / validation / line-type / field tables above.

---

## Trap notes

⚠ Step 1 (client + DI) — `HttpClient` ownership and whether the SDK client is safe as singleton vs per-request is not visible from the constructor. A wrong lifetime duplicates handlers or disposes a shared client. **MUST load `dotnet-client-initialization`** before `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a credentials *object* with `required` `Username`/`Password`; setting it after the client is built, or swapping SID/token onto the wrong property, yields 401s that look like “the SDK is broken”. **MUST load `dotnet-authentication`** before wiring `Twilio:` config.

⚠ Steps 2–8 (every call) — 24 (create) / 15 (lookup) / 8 (list) positional optionals have **no C# default**; a positional call mis-binds and silently sends the wrong field. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–8 (models) — statuses and schedule type are `StringEnum<T>`, not C# enums; response dates are `string?` (RFC 2822), not `DateTimeOffset`; `LookupResponse.LineTypeIntelligence.Type` is `string?` not `LineType`. **MUST load `dotnet-models`** before mapping records.

⚠ Steps 2–8 (error boundary) — every in-scope op is Case B (`SdkException<RawError>`); a Case-A-shaped `TryGet…` ladder will not compile, and an SDK-exception-only catch lets other failures escape. **MUST load `dotnet-error-handling`** before writing `try/catch`.

⚠ Step 1 + 2 (retries / timeout / BaseUrl) — `Retry.Timeout` and `HttpClient.Timeout` are not a whole-call budget and are not interchangeable with `Server.Default.Production.BaseUrl`; create/update are POST form bodies whose transport-retry behaviour is not “writes never retry”. Setting `Twilio:BaseUrl` on the wrong `Server.*` node silently sends Lookup or Messages to the wrong host. **MUST load `dotnet-configuration-resilience`** before options, BaseUrl, or retries.

⚠ Step 6 (list paging) — `ListMessage` is a single-page call; `NextPageUri` / `PageToken` are provider-supplied stop conditions, not a bound. An unbounded page loop will not return. **MUST load `dotnet-configuration-resilience`** (pagination section) before the reconcile loop.

⚠ Step 10 (tests) — the test seam is the `HttpClient` argument, not a fake of generated controllers. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing `TwilioSdkClient`, `HttpClient` lifetime, `AddTwilioSdkClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, request/response records, wire names |
| `dotnet-error-handling` | Steps 2–8 + error boundary — Case B `RawError`, exception types that actually throw |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retries/timeouts; Step 6 hand-driven paging |
| `dotnet-testing` | Step 10 — `HttpClient` seam |

**`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Lookup uses **v2** `FetchPhoneNumber3` (has `Valid` + line-type intelligence). v1 is documented only as the rejected alternative.
- Immediate SMS uses **`from` only** (`messagingServiceSid: null`). Scheduled SMS **requires `messagingServiceSid`** plus `scheduleType: Fixed` and `sendAt`, per `MessageEnumScheduleType` XML (“Messaging Services only”).
- `Twilio:BaseUrl` overrides **`options.Server.Default.Production.BaseUrl`** (`https://api.twilio.com`, 2010 Messages). It is **not** applied to Lookup (`Default4` / `lookups.twilio.com`) and **not** to `Default1` (`messaging.twilio.com`).
- Canonical store value is Lookup v2 `PhoneNumber` (E.164). Usable SMS destination = `Valid == true` **and** `LineTypeIntelligence.Type` equals wire `mobile`; other non-mobile types rejected as above.
- No `StatusCallback` / inbound webhooks; status is always pull (`FetchMessage` / `ListMessage`).
- `Twilio:AccountSid` is both Basic-auth `Username` and the `accountSid` path argument on every Messages call.

**Blockers**

- **Caller-supplied idempotency key cannot be passed through this SDK.** `CreateMessage` always sends `Idempotency-Key: {new Guid}` and `RequestOptions` has no header override. Operator resend under the same app key will send a second message if it calls `CreateMessage` again. Mitigation is application-level (reuse stored Sid + `FetchMessage`), not an SDK parameter. Live replay-response body is UNVERIFIED because the public API cannot replay a key.
- **Schedule min/max window is not in the SDK.** Only provider 4xx via Case B can reject an out-of-range `sendAt`. Do not hard-code a window from memory.
- **`LineTypeIntelligence.Type` and `LineStatus.Status` are untyped strings**; SMS-capability is inferred, not a dedicated field. Voip/toll-free/`unknown` remain UNVERIFIED.
