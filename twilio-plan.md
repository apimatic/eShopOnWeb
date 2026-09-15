# Twilio .NET SDK — eShopOnWeb SMS notifications

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdk.TwilioSdkClient` / `TwilioSdk.TwilioSdkClientOptions`. Map stamp: source commit `51fdf48`.

## Scope & sequence

1. **Install + client** — `AddTwilioSdkClient` / `new TwilioSdkClient(httpClient, options)`. Auth: Account SID + Auth Token on `AccountSidAuthToken`. Optional `Twilio:BaseUrl` → **only** `options.Server.Default.Production.BaseUrl` (messaging API: create/fetch/list/update/delete). Never set `Default4` (Lookup). Also bind `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:AccountSid`.
2. **Flow 1 — register number** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`. Store `LookupResponse.PhoneNumber` (E.164). Reject when the provider does not treat the number as valid (`Valid` is not true, or `PhoneNumber` missing).
3. **Flow 2 — send immediately** (placed / dispatched / cancelled) — `client.Api20100401Message.CreateMessage` with `from: Twilio:FromNumber`, `to:` stored E.164, `body:`, `messagingServiceSid: null`, `scheduleType: null`, `sendAt: null`, `statusCallback: null`. Persist returned `Sid`.
4. **Flow 2 — schedule follow-up on dispatch** — same `CreateMessage` with `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt:` (provider-queued, days later), `messagingServiceSid: Twilio:MessagingServiceSid` (required by the schedule contract), `statusCallback: null`. Persist returned `Sid` for later cancel.
5. **Flow 2 — cancel scheduled follow-up** — `client.Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`, `body: null`.
6. **Status polling (no webhooks)** — `client.Api20100401Message.FetchMessage` by SID. Read `Status`, `ErrorCode`, `ErrorMessage`.
7. **Flow 3 — reconciliation** — `GET /api/notifications/reconciliation?from=&to=` → `client.Api20100401Message.ListMessage` with **server-side** `from: Twilio:FromNumber` and date-range params. Paginate manually.
8. **Flow 3 — resend** — `CreateMessage` again. **SDK does not accept a caller Idempotency-Key** (see CONTRACT + Assumptions). App-level idempotency store keyed by the caller’s key; skip a second `CreateMessage` on repeat.
9. **Flow 3 — dispose content at provider** — `UpdateMessage` with `body: ""` (empty string), `status: null`. Do **not** use `DeleteMessage` (that deletes the resource, not just the body).
10. **Error boundary** around every SDK call (all in-scope ops are Case B `SdkException<RawError>`). Delivery `failed`/`undelivered` after a successful create is an outcome on the Message resource, not a create exception.

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

### Client construction & servers

| Fact | Value | Cite |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` | `sdk-map.md` · `ServiceCollectionExtensions.cs` |
| Options | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment members | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`). Default: `Production`. | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Auth credentials | `new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required`. Map notes: account SID + auth token is accepted (also API key/secret). | `sdk-map.md` Servers & auth · `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Messaging base URL | Message create/fetch/list/update/delete all call `_server.Default(...)` (HTTP host **Default (api)**). Override: `options.Server.Default.Production.BaseUrl` (`TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl`, default `"https://api.twilio.com"`). When `Twilio:BaseUrl` is set, assign it **verbatim** here. | `Api/Api20100401Message.cs` · `ServerOptions.cs` · `Servers/DefaultOptions.cs` |
| Lookup host (do **not** override from Twilio:BaseUrl) | Lookup uses `_server.Default4(...)` (HTTP host **Default4 (lookups)**). `options.Server.Default4.Production.BaseUrl` default `"https://lookups.twilio.com"`. Leave untouched so Lookup stays on its own host. | `Api/LookupsV2PhoneNumber.cs` · `Servers/Default4Options.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel?: Microsoft.Extensions.Logging.LogLevel`. No header bag. | `Core/RequestOptions.cs` |
| Path AccountSid | Every Message op takes `string accountSid` as the first arg — pass `Twilio:AccountSid` (same value as auth Username). | `map/operations/Api20100401Message.md` |

### 1. Lookup / validation — `FetchPhoneNumber3`

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, no C# default → pass `null` to skip |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 lookups) |
| Wire query | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (see operations page) |
| Returns | `TwilioSdk.Models.LookupResponse` (no extra envelope) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `map/operations/LookupsV2PhoneNumber.md` · `Api/LookupsV2PhoneNumber.cs` · `map/models/records-4-Li-Me.md` |

`fields` XML (source): comma-separated; possible values `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. `phoneNumber` XML: E.164 or national; default country +1. Pass `countryCode` when the caller typed a national number.

**`LookupResponse` fields used (store / reject):**

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | Canonical **E.164** (`+` + country code + subscriber). **This is what gets stored.** |
| `Valid (valid)` | `bool?` | XML: “in a valid range that can be freely assigned by a carrier to a user.” Reject registration unless this is `true`. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reasons when invalid. |
| `NationalFormat (national_format)` | `string?` | Display only; do not store as the canonical form. |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix. |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2. |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Only if `fields` includes `line_type_intelligence`. Nested: `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?` (**untyped string — SDK lists no SMS-capability vocabulary**), `ErrorCode (error_code): int?`. |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Only if `fields` includes `line_status`. Nested: `Status (status): string?` (**untyped**), `ErrorCode (error_code): int?`. |

Cite: `map/models/records-4-Li-Me.md`, `records-3-Fl-Li.md`, `Models/LookupResponse.cs`.

**`ValidationError` (`TwilioSdk.Models.Enums.ValidationError`, StringEnum):** `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`. Cite: `map/models/enums.md`.

**How to tell “not a usable SMS destination” (what the SDK actually gives):**

- On **2xx**: reject if `Valid` is not `true`, or `PhoneNumber` is null/empty. `ValidationErrors` explains why.
- On **non-2xx**: `SdkException<RawError>` — treat as lookup failure (do not register). Read `ex.Error.StatusCode` + `ex.Error.ReadAsString()`.
- `LineTypeIntelligence.Type` / `LineStatus.Status` are unstructured `string?` — the SDK does **not** enumerate which strings mean “not SMS-capable”. Do not invent an allow/deny list. **UNVERIFIED** whether `Valid == true` without line-type packages is sufficient for every unusable destination; the in-scope test fixture (`TWILIO_UNREACHABLE_TO_NUMBER`, unassigned US) is **accepted by create** and fails later as **delivery outcome** — so registration uses Lookup validity, not post-send undeliverable.
- Do **not** use `LookupsV1PhoneNumberApi.FetchPhoneNumber2` for this flow: it has no `Valid` flag; `Carrier` is `object?`. (`map/operations/LookupsV1PhoneNumberApi.md`, `records-4-Li-Me.md`)

### 2–4. Create SMS / schedule — `CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Method | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — nullable, no default → **pass `null` to skip** |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default api) |
| Body encoding | **application/x-www-form-urlencoded** (not JSON). Wire names: `To`, `StatusCallback`, `ApplicationSid`, `MaxPrice`, `ProvideFeedback`, `Attempt`, `ValidityPeriod`, `ForceDelivery`, `ContentRetention`, `AddressRetention`, `SmartEncoded`, `PersistentAction`, `TrafficType`, `ShortenUrls`, `ScheduleType`, `SendAt`, `SendAsMms`, `ContentVariables`, `RiskCheck`, `From`, `FallbackFrom`, `MessagingServiceSid`, `Body`, `MediaUrl`, `ContentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (no extra envelope) |
| Error | **Case B** `SdkException<RawError>` |
| Accessors | `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `map/operations/Api20100401Message.md` · `Api/Api20100401Message.cs` |

**Immediate send (Flow 2 placed / dispatched / cancelled SMS):** named args — `accountSid:`, `to:` (stored E.164), `from: Twilio:FromNumber`, `body:`, and **null** for every other must-pass param including `messagingServiceSid`, `scheduleType`, `sendAt`, `statusCallback` (no public URL).

**Scheduled follow-up (Flow 2 dispatch):** same call with `scheduleType: TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`), `sendAt: <DateTimeOffset a few days later>`, `messagingServiceSid: Twilio:MessagingServiceSid`, `statusCallback: null`. `MessageEnumScheduleType` XML: **“For Messaging Services only”** — `messagingServiceSid` is required for this path; do not schedule with `From` alone. `from` may be `Twilio:FromNumber` or `null` (service assigns); reconciliation lists by `FromNumber`, so passing `from: Twilio:FromNumber` keeps list-by-from aligned when the service allows it. Enum XML mentions `send_time`; the **C# identifier and wire name are `sendAt` / `SendAt`** — use those.

**Schedule window min/max:** not present on the operation row, param XML, or enum XML. **UNVERIFIED** in SDK. Do not hard-code invented min/max. If the provider rejects `sendAt`, that surfaces as `SdkException<RawError>` on create — read status + body.

**Identifier to persist:** `ApiV2010AccountMessage.Sid (sid): string?` — Twilio message SID (`^(SM|MM)[0-9a-fA-F]{32}$` on the model). Immediate: this SID is polled. Scheduled: this SID is cancelled later.

**Create-time status:** `Status (status): MessageEnumStatus?` — typically `queued` / `accepted` / `scheduled` on success. A **2xx create is not a delivery guarantee**.

#### `ApiV2010AccountMessage` fields the integration reads

| C# (wire) | Type | Use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider identifier |
| `Status (status)` | `MessageEnumStatus?` | Outcome / poll |
| `From (from)` | `string?` | Sender |
| `To (to)` | `string?` | Recipient |
| `Body (body)` | `string?` | Text (empty/null after redact) |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT created |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT sent |
| `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT updated |
| `ErrorCode (error_code)` | `int?` | Set when status is `failed` or `undelivered`; null otherwise. XML: do not branch programmatically on specific codes (values may change). |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code` when failed/undelivered |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | Service used |
| `AccountSid (account_sid)` | `string?` | Account |
| `Direction (direction)` | `MessageEnumDirection?` | `OutboundApi (outbound-api)` for REST creates |
| `NumSegments (num_segments)` | `string?` | Segments |
| `Price (price)` / `PriceUnit (price_unit)` | `string?` | Populated after send |

Cite: `map/models/records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`.

**Baked-in header (not a caller parameter):** `CreateMessage` always sends `Idempotency-Key: Guid.NewGuid()`. There is **no** `idempotencyKey` argument (unlike Payments / UserDefinedMessage). `RequestOptions` cannot set headers. A second `CreateMessage` from the app always gets a **new** key. Cite: `Api/Api20100401Message.cs`.

### 5. Cancel scheduled — `UpdateMessage`

| | |
|---|---|
| Method | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (form-urlencoded) |
| Wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Notes (map) | “Update a Message resource (**used to redact Message `body` text and to cancel not-yet-sent messages**)” |
| Cite | `map/operations/Api20100401Message.md` · `Api/Api20100401Message.cs` |

**Cancel:** `sid:` stored scheduled SID, `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`), `body: null`. Success: returned `Status` is `Canceled`. If the message **already sent**, the provider rejects — **Case B** (exact HTTP status **UNVERIFIED**; read `StatusCode` + `ReadAsString()`). Do not treat a later `FetchMessage` of `sent`/`delivered` as a successful cancel.

### 6. Fetch by SID — `FetchMessage`

| | |
|---|---|
| Method | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Returns | `ApiV2010AccountMessage` (fields in table above) |
| Error | **Case B** `SdkException<RawError>` (unknown SID → non-2xx; exact code **UNVERIFIED**) |
| Cite | `map/operations/Api20100401Message.md` |

Poll until a terminal status (`Delivered`, `Undelivered`, `Failed`, `Canceled`) or a deadline. `TWILIO_UNREACHABLE_TO_NUMBER`: create **succeeds**; poll `Status` → `Undelivered`/`Failed` with `ErrorCode`/`ErrorMessage` — **delivery outcome, not create failure**.

### 7. List for reconciliation — `ListMessage`

| | |
|---|---|
| Method | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `to` … `pageToken` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Wire query | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **No SDK auto-paginator.** `pageSize` XML: default 50, max 1000. Continue while `NextPageUri` is non-null. |
| Cite | `map/operations/Api20100401Message.md` · `Api/Api20100401Message.cs` · `map/models/records-4-Li-Me.md` |

**Server-side filters (do not list the whole account then filter in-app):**

- `from: Twilio:FromNumber` (XML: “Filter by sender”). `to: null`.
- Date range from query `from`/`to` (ISO-8601): pass **`dateSentQueryQuery` = range start** (`DateSent>`), **`dateSentQuery` = range end** (`DateSent<`), `dateSent: null` (exact-day `DateSent` unused). SDK serializes `DateTimeOffset` via `ToIso8601()`.

**`ListMessageResponse` envelope:** `End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, **`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`**. Cite: `records-4-Li-Me.md`.

**Next page:** SDK has no helper that consumes `NextPageUri`. **UNVERIFIED** exact extraction; defensive: when `NextPageUri` is non-null, pass its `PageToken` query value as `pageToken` (and/or `page` from `Page`); stop when `NextPageUri` is null.

### 8. Resend / idempotency — **SDK gap**

`CreateMessage` has **no** `idempotencyKey` parameter. `RequestOptions` cannot attach `Idempotency-Key`. The method **always** sends `Idempotency-Key: Guid.NewGuid()` internally — the caller cannot supply or reuse a key. Other SDK ops (e.g. `CreatePayments`, `CreateUserDefinedMessage`) *do* expose `idempotencyKey`; Message create does not. Cite: `map/operations/Api20100401Message.md` vs `Api20100401Payment.md` / `Api20100401UserDefinedMessage.md`; `Api/Api20100401Message.cs`.

**Implement resend with an app-level idempotency store** keyed by the caller-supplied key: first request calls `CreateMessage` and stores the returned `Sid`; repeats return the original result and **must not** call `CreateMessage` again.

### 9. Redact body at provider — `UpdateMessage` (not `DeleteMessage`)

| | |
|---|---|
| Call | `UpdateMessage(accountSid, sid, body: "", status: null, …)` — empty string, not null, so `Body` is sent |
| Returns | `ApiV2010AccountMessage` — same shape; `Sid`/`Status`/`ErrorCode` remain; `Body` should no longer carry the original text |
| After redaction | **UNVERIFIED** whether `Body` is `""` vs `null` on a later `FetchMessage`. Defensive: treat empty or null `Body` as redacted; never treat a leftover non-empty `Body` as success. |
| Cite | `map/operations/Api20100401Message.md` (notes: redact `body`) |

**`DeleteMessage(string accountSid, string sid, …)` → `void`:** “Deletes a Message resource from your account.” That removes the resource, which does **not** match “text gone, send fact + outcome survive.” Do not use it for Flow 3 content dispose. Cite: same operations page.

`CreateMessage` also has `contentRetention: MessageEnumContentRetention` (`Retain`/`Discard`) — create-time retention, **not** the post-facto DELETE-content flow.

### Enums in scope (`TwilioSdk.Models.Enums`, StringEnum — `Type.FromValue("wire")` or static members)

| Type | Members (C# (wire)) | Cite |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `map/models/enums.md` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` | `enums.md` |
| `MessageEnumScheduleType` | `Fixed (fixed)` | `enums.md` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | `enums.md` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` | `enums.md` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` | `enums.md` |
| `MessageEnumTrafficType` | `Free (free)` | `enums.md` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` | `enums.md` |
| `ValidationError` | see Lookup section | `enums.md` |

Do not confuse with `SmsMessageEnumStatus` / `LineType` — different types, not used by these operations.

### 10. Errors (all in-scope ops are Case B)

Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. There are **no** typed `{Operation}Error` / `TryGet…` accessors on these calls. Read:

- `ex.Error.StatusCode` (`System.Net.HttpStatusCode`)
- `ex.Error.ReadAsString()` (raw body)
- `ex.Error.ReadAsJson<T>()` if you have a DTO; otherwise string is enough

| Situation | What reaches the catch | Notes |
|---|---|---|
| Invalid number at **lookup** (2xx, `Valid != true`) | **No exception** — inspect `LookupResponse` | Reject at registration |
| Lookup HTTP failure / miss | `SdkException<RawError>` | **UNVERIFIED** exact status; read accessors |
| Message create failure (bad `To`/`From`, schedule window, auth, etc.) | `SdkException<RawError>` | |
| Cancel of already-sent message | `SdkException<RawError>` | **UNVERIFIED** exact status |
| Fetch unknown SID | `SdkException<RawError>` | **UNVERIFIED** exact status (typically not 2xx) |
| List errors | `SdkException<RawError>` | |
| Redact errors | `SdkException<RawError>` | |
| Auth / wrong host / timeout | `SdkException<RawError>` or transport exceptions | Check `AccountSidAuthToken` and `Server.Default` vs `Default4` |
| US unassigned number (`TWILIO_UNREACHABLE_TO_NUMBER`) | **Create succeeds** | Later `FetchMessage.Status` is `Undelivered`/`Failed` — **not** a create exception |
| Canadian reachable (`TWILIO_TEST_TO_NUMBER`) | Create succeeds | Poll toward `Delivered`/`Sent` |

`SdkException<TError>`: `public required TError Error { get; init; }` (`Core/Exceptions/SdkException.cs`).

---

## Trap notes

⚠ Step 1 (client / DI) — constructing `TwilioSdkClient` or calling `AddTwilioSdkClient` without the companion’s HttpClient/lifetime rules will leak sockets or share a mis-owned client across the app. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (auth) — putting SID/token in the wrong options member, or after the client is already built, yields 401s that look like “Twilio is down.” **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

⚠ Step 1 (BaseUrl / retries) — `Retry`/`Timeout` on options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure can replay a **POST** (`CreateMessage` / `UpdateMessage`) even though those writes are not caller-idempotent. Whether a failed write can be re-sent is decided here, not at the call site. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Steps 2–9 (calls) — 15–24 nullable parameters have **no C# defaults**; a positional call silently binds the wrong argument. **MUST load `dotnet-calling-endpoints`** before the first `CreateMessage` / `FetchPhoneNumber3` / `ListMessage`.

⚠ Steps 2–9 (models / enums) — `MessageEnum*` and `ValidationError` are `StringEnum<T>` records, not C# enums; response bodies drop unmodeled JSON; `LookupResponse`/`ApiV2010AccountMessage` members are nullable `init`. Comparing status/outcome incorrectly marks delivered traffic as failed. **MUST load `dotnet-models`** before mapping fields or switching on status.

⚠ Step 10 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`). A ladder written for Case A `TryGet…` never matches. Status and body live on `RawError`, not on exception `.ToString()`. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (list pagination) — `ListMessage` does not auto-page; stopping after the first `ListMessageResponse` under-counts the provider ledger. **MUST load `dotnet-configuration-resilience`** before writing the page loop.

⚠ Tests — the test seam is not the generated controllers. **MUST load `dotnet-testing`** before stubbing Twilio.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, DI, HttpClient ownership |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–9 — named args, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 2–9 — StringEnum, nullable records, wire vs C# names |
| `dotnet-error-handling` | Step 10 — Case B `RawError`, both `JsonException` directions, boundary |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retry/timeout; Step 7 pagination; POST replay hazard |
| `dotnet-testing` | Tests around the integration |

---

## Assumptions & Blockers

- **Assumption:** generated root namespace is `TwilioSdk` (map), not `Twilio`.
- **Assumption:** `Twilio:AccountSid` is both Basic-auth Username and the `{AccountSid}` path argument.
- **Assumption:** Lookup V2 (`FetchPhoneNumber3`) is the in-scope “Lookup or equivalent.” V1 lacks `Valid`.
- **Assumption:** registration rejects only when Lookup says the number is not valid (`Valid` not true / missing E.164 / lookup HTTP error). Unassigned-but-valid US numbers are **in-scope expected delivery failures**, not registration rejects.
- **Gap (real):** caller-supplied `Idempotency-Key` on `CreateMessage` is **not** in the SDK surface. Resend **must** use an app-level idempotency store. Do not invent a header the SDK will overwrite with `Guid.NewGuid()`.
- **Gap (real):** schedule min/max window is **not** documented on the operation or `sendAt` XML. Do not invent bounds.
- **Gap (real):** `LineTypeIntelligenceInfo.Type` / `LineStatusInfo.Status` have **no** SDK enum of SMS-capable values. Do not invent a type allowlist.
- **UNVERIFIED (live-only):** HTTP status codes for lookup miss, cancel-already-sent, fetch miss, redact of unknown SID; exact `Body` after redaction (`""` vs `null`); `PageToken` extraction from `NextPageUri`. Defensive: always read `RawError` accessors on non-2xx; treat empty/null body as redacted only after a 2xx `UpdateMessage`; stop listing when `NextPageUri` is null.
- **Blocker:** none that prevent coding the flows above once the app-level idempotency store is accepted for resend.
- Live account: creates **send and cost money**. No webhooks; poll `FetchMessage`.
