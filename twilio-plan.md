# Twilio .NET SDK — integration plan & contract sheet (eShopOnWeb SMS order notifications)

SDK: `AsadAli.TwilioSdk` (NuGet, install version-less: `dotnet add package AsadAli.TwilioSdk`) · root namespace `TwilioSdk` · client `TwilioSdkClient` · options `TwilioSdkClientOptions` · map stamp: source commit `51fdf48`.

## 1. Scope & sequence

| Step | Work | Operation(s) |
|---|---|---|
| 1 | Add NuGet package `AsadAli.TwilioSdk` to the Infrastructure project | — |
| 2 | Register `TwilioSdkClient` in DI; wire auth (`Twilio:AccountSid`/`Twilio:AuthToken`) and optional `Twilio:BaseUrl` override | client construction (§3.4) |
| 3 | Validate registration phone number → canonical E.164 | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 4 | Send immediate SMS (order confirmation) | `Api20100401Message.CreateMessage` |
| 5 | Schedule follow-up SMS (queued with Twilio) | `Api20100401Message.CreateMessage` + `scheduleType`/`sendAt` |
| 6 | Cancel a scheduled follow-up | `Api20100401Message.UpdateMessage` (`status`) |
| 7 | Poll message state (no webhooks available) | `Api20100401Message.FetchMessage` |
| 8 | Redact message body (privacy request) | `Api20100401Message.UpdateMessage` (`body`) |
| 9 | Reconciliation list over date range, filtered by sender | `Api20100401Message.ListMessage` |
| 10 | Resend = new `CreateMessage` (no dedicated op — see §3.3 row 8) | `Api20100401Message.CreateMessage` |
| 11 | Error boundary + tests | all of the above |

## 2. Need → operation map (the 8 asks)

| # | Need | SDK capability |
|---|---|---|
| 1 | Validate number | `LookupsV2PhoneNumber.FetchPhoneNumber3` — response carries `Valid` + `ValidationErrors` + canonical `PhoneNumber`. (v1 `LookupsV1PhoneNumberApi.FetchPhoneNumber2` exists but its response has **no** `Valid` field — do not use it for validation.) |
| 2 | Send now | `Api20100401Message.CreateMessage` |
| 3 | Schedule | Same `CreateMessage` with `scheduleType: MessageEnumScheduleType.Fixed` + `sendAt` + `messagingServiceSid` (scheduling is Messaging-Services-only — see §3.3 row 3) |
| 4 | Cancel scheduled | `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled` |
| 5 | Fetch state | `Api20100401Message.FetchMessage` |
| 6 | Delete content | `Api20100401Message.UpdateMessage` with `body: ""` (redact; record survives). `DeleteMessage` exists but destroys the whole record — **not** the requirement. |
| 7 | List by range + sender | `Api20100401Message.ListMessage` with `from`, `dateSent*`, paged manually |
| 8 | Resend | **No dedicated resend operation exists** — the controller exposes exactly Create/Delete/Fetch/List/Update. Resend = a new `CreateMessage` with the same `to`/`body`. |

## 3. CONTRACT SHEET

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

### 3.1 Namespaces (using-directives)

| Types | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `TwilioSdk.Core.ErrorResponse` |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection`, `ValidationError` (+ other `MessageEnum*`) | `TwilioSdk.Models.Enums` |

### 3.2 Operations

**Row 1 — Validate number** (`operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md`)

- Controller: `client.LookupsV2PhoneNumber` · `GET /v2/PhoneNumbers/{PhoneNumber}` · server group **Default4 (lookups)** — NOT governed by the messaging base-URL override.
- Signature: `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 15 params `fields…partnerSubId` are nullable with no default → **must be passed explicitly** (pass `null`). For plain validation pass `fields: null` (or `"validation"`), `countryCode` only when the input is national-format (default country is +1 North America).
- Returns `LookupResponse` (flat record, no envelope). Fields the integration reads: `Valid (valid): bool?` · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` · `PhoneNumber (phone_number): string?` ← canonical E.164 form · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?`.
- Error: **Case B** — `SdkException<RawError>`. **UNVERIFIED** (only live traffic settles it): an invalid number may come back as 200 with `Valid = false` + `ValidationErrors`, or as an error status. Defensive directive: treat `Valid != true` as invalid AND catch `SdkException<RawError>` mapping 4xx to "invalid number" via `ex.Error.StatusCode` / `ex.Error.ReadAsString()`.

**Row 2 — Send now** (`operations/Api20100401Message.md`, `records-1-Ac-Ca.md`)

- Controller: `client.Api20100401Message` · `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server group **Default (api)**.
- Signature: `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 24 params `statusCallback…contentSid` are nullable with no default → **must all be passed explicitly** (pass `null`); call with named arguments only.
- Immediate send: `accountSid`, `to` (E.164 from step 1), `body`, and exactly ONE sender identity: `from: <Twilio:FromNumber>` **or** `messagingServiceSid: <Twilio:MessagingServiceSid>` — pass one, leave the other `null`. Recommendation: use `messagingServiceSid` everywhere for one code path (scheduling requires it); `from` alone is valid for immediate sends only.
- Returns `ApiV2010AccountMessage` (flat record, no envelope). Read: `Sid (sid): string?` · `Status (status): MessageEnumStatus?` (initial status, typically `Queued`/`Accepted`) · `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?`.
- Error: **Case B** — `SdkException<RawError>` (e.g. bad `to`/`from` surfaces here as 4xx; read `StatusCode` + `ReadAsString()`).

**Row 3 — Schedule** (`operations/Api20100401Message.md`, `enums.md`)

- Same `CreateMessage`. Scheduling adds: `scheduleType: MessageEnumScheduleType.Fixed` (wire `fixed`), `sendAt: <DateTimeOffset>`, and `messagingServiceSid` — the `MessageEnumScheduleType` map row states scheduling is **"For Messaging Services only"**, so `Twilio:MessagingServiceSid` is mandatory here; `from` does not substitute.
- A scheduled-but-unsent message carries status **`MessageEnumStatus.Scheduled`** (wire `scheduled`).
- **UNVERIFIED** (not in map/source): the provider's lead-time/horizon limits on `sendAt`. Defensive directive: catch `SdkException<RawError>` on create and surface the 4xx body (`ReadAsString()`) as the rejection reason rather than assuming the schedule was accepted.

**Row 4 — Cancel scheduled** (`operations/Api20100401Message.md`, `enums.md`)

- `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` are nullable, no default → **pass both explicitly**. Cancel: `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` (`Canceled` is the ONLY value of `MessageEnumUpdateStatus`, wire `canceled`).
- The operation's map note: used "to cancel not-yet-sent messages" — only a not-yet-sent (i.e. `scheduled`) message is cancellable; the enum having no other value confirms no other transition is exposed.
- Already-sent / too-late cancel: **Case B** `SdkException<RawError>`; exact status code **UNVERIFIED** — defensive directive: branch on `ex.Error.StatusCode` and keep the raw body; do not treat the exception type itself as "already sent".
- Returns the updated `ApiV2010AccountMessage` (expect `Status` = `Canceled`).

**Row 5 — Fetch state** (`operations/Api20100401Message.md`)

- `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage`. Poll `Status`; on terminal failure read `ErrorCode`/`ErrorMessage` from the same record.
- Error: **Case B** — `SdkException<RawError>`; unknown SID ⇒ check `StatusCode` for 404.
- Carrier-refused destinations are **outcomes, not exceptions**: the create succeeds, and a later fetch shows `Status` = `MessageEnumStatus.Failed` (wire `failed`) or `MessageEnumStatus.Undelivered` (wire `undelivered`) with `ErrorCode`/`ErrorMessage` populated. Record these as delivery outcomes.

**Row 6 — Redact body** (`operations/Api20100401Message.md`)

- `UpdateMessage(accountSid, sid, body: "", status: null)` — the operation's map note: "used to redact Message `body` text". Afterwards the record (SID, status/outcome, dates, parties) survives with an empty `Body`; the text is no longer retrievable.
- `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` returns `void` and deletes the whole Message resource — it removes the outcome record too, so it does **not** meet the "fact and outcome survive" requirement. Do not use it for this need.
- Error: **Case B** — `SdkException<RawError>`.

**Row 7 — Reconciliation list** (`operations/Api20100401Message.md`, `records-4-Li-Me.md`)

- `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 params `to…pageToken` must be passed explicitly (named args).
- Wire mapping: `To`←`to` · `From`←`from` · `DateSent`←`dateSent` (exact date) · `DateSent<`←`dateSentQuery` (strictly before) · `DateSent>`←`dateSentQueryQuery` (strictly after) · `PageSize`←`pageSize` · `Page`←`page` · `PageToken`←`pageToken`.
- Sender filter: pass `from: <Twilio:FromNumber>` — applied provider-side in the request, exactly as required. (Note: when sends go out via a Messaging Service, the visible `From` on each message record is the pool number that sent it; if the pool has several numbers, reconcile per sending number or by `to`.)
- Date semantics (source XML docs on the operation): GMT, date-granular (`YYYY-MM-DD`); the SDK exposes the three discrete filters above — `<`/`>` are strict per the wire names, so choose window boundaries accordingly (e.g. `dateSentQueryQuery: start.Date` inclusive-start ⇒ use the day before, or filter the boundary day client-side).
- Returns `ListMessageResponse`: payload one field down — `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` — plus paging fields `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `FirstPageUri`, `Page (page): int?`, `PageSize (page_size): int?`, `Start`, `End`, `Uri`.
- Pagination: no built-in pager (map: "none"). Manual loop: `pageSize` (default 50, max 1000 — source XML doc), then follow `NextPageUri` until null; `pageToken` is "provided by the API" (source XML doc) and `page` is client state only.
- Error: **Case B** — `SdkException<RawError>`.

**Row 8 — Resend**: no dedicated operation (controller has exactly Create/Delete/Fetch/List/Update — `operations/Api20100401Message.md`). Resend = new `CreateMessage` with the same `to`/`body`/sender; it yields a NEW message SID.

### 3.3 Enum values needed (`enums.md`)

| Enum (all `TwilioSdk.Models.Enums`, `StringEnum<T>`) | Values (`Member (wire)`) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

Outcome classification for the app: success terminal = `Delivered`; carrier-refused/undeliverable terminal = `Failed`, `Undelivered` (record `ErrorCode`/`ErrorMessage`); in-flight = `Accepted`, `Queued`, `Sending`, `Scheduled`; cancelled-by-us = `Canceled`.

### 3.4 Client construction, auth, base-URL override (`sdk-map.md`; source: `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`)

- Options properties: `Environment: ServerEnvironment` (`ServerEnvironment.Production`) · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `AccountSidAuthToken: BasicAuthCredentials?`.
- Constructor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`; DI: `services.AddTwilioSdkClient(o => { … })`.
- Auth: `o.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> };` — both members `required`. (Map auth note: Twilio prefers API-key-as-username in production; Account SID + Auth Token as given works — same property either way.)
- **Base-URL override (messaging API only)**: `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` — set verbatim, only when the setting is present. `ServerOptions` has one property per server group (`Default`, `Default1`…`Default14`); ALL `Api20100401Message` operations resolve through group **Default** (default `https://api.twilio.com`), so this override governs every send/fetch/update/list call and nothing else. Lookups (number validation) is group **Default4** (default `https://lookups.twilio.com`) and is deliberately left at its default — `Twilio:BaseUrl` does not affect it.
- `accountSid` operation parameter: pass the same `Twilio:AccountSid` value on every call.

### 3.5 Errors that reach a catch block

Every operation in scope is **Case B**: `SdkException<RawError>` (`TwilioSdk.Core.Exceptions`) with `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()` (`TwilioSdk.Core.ErrorResponse`). There are no typed `{Operation}Error` accessors for any of the 8 needs, and no no-throw `…Result` variants exist anywhere in this SDK. Additionally, `System.Text.Json.JsonException` reaches the boundary from two directions — see the mandatory rows in §5.

## 4. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind `TwilioSdkClient` has specific lifetime requirements; building it per-request or disposing it with the client breaks sockets/DI. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient(...)` or `AddTwilioSdkClient`.

> ⚠ Step 2 (auth) — when in the construction sequence credentials must be set, and how secrets flow from configuration without hardcoding, is not visible from the options shape. **MUST load `dotnet-authentication`**.

> ⚠ Steps 2–9 (every call) — `CreateMessage` has 24 nullable no-default params and `ListMessage` 8; a positional call mis-binds silently. Whether a failed write can be safely re-sent, and how named-argument discipline is enforced, is governed by **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 3–9 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `init`-only/required members, and unmodeled JSON fields are dropped on deserialize — this affects how `Status` is compared and whether unexpected wire fields survive. **MUST load `dotnet-models`**.

> ⚠ Steps 3–9 (error boundary) — which exception types actually reach a catch block, and the traps that make a reasonable-looking catch ladder silently wrong (incl. the two `JsonException` rows in §5), are governed by **MUST load `dotnet-error-handling`** before any `try/catch` is written.

> ⚠ Step 2 (resilience) — the SDK's retry/timeout options interact with non-idempotent POSTs (a retried `CreateMessage` can mean a duplicate SMS), `Timeout` bounds something other than a whole call, and there is no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before wiring `RetryOptions` or tuning the client.

> ⚠ Step 11 (tests) — the test seam for SDK-calling code is specific to this client shape. **MUST load `dotnet-testing`** before stubbing.

## 5. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — governs step 2 (client construction & DI).
- `dotnet-authentication` — governs step 2 (credentials wiring).
- `dotnet-calling-endpoints` — governs steps 3–10 (every operation call).
- `dotnet-models` — governs steps 3–10 (records, `StringEnum<T>` enums, wire names).
- `dotnet-error-handling` — governs the whole error boundary (always required).
- `dotnet-configuration-resilience` — governs step 2 (retries, timeout, base URL, pagination, logging).
- `dotnet-testing` — governs step 11.

Mandatory hazard rows (verbatim — both belong in the FIRST sheet because the boundary is written early):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 6. Assumptions & Blockers

**Assumptions**

- Lookups **v2** chosen for validation (v1's response has no `Valid`/`ValidationErrors` fields — `records-4-Li-Me.md`).
- `Twilio:AccountSid` is passed both as the auth `Username` and as the `accountSid` parameter on every messaging call.
- `Twilio:BaseUrl` is intended for the messaging API host only (per brief); it maps to `Server.Default.Production.BaseUrl` and leaves Lookups (`Server.Default4`) untouched.
- Messaging Service SID is the single sender identity for all sends (required for scheduling; valid for immediate sends), with `Twilio:FromNumber` kept for the reconciliation `from` filter and as a fallback identity.
- `ApiV2010AccountMessage` date fields (`DateSent`, `DateCreated`, `DateUpdated`) are `string?`, not `DateTimeOffset` — parse defensively (`records-1-Ac-Ca.md`).

**Blockers** — none.

**UNVERIFIED items** (only live traffic can confirm; defensive directives given in the rows): invalid-number lookup result shape (200+`Valid=false` vs error status) · exact status code for cancel-after-send · provider lead-time/horizon limits on `sendAt`.
