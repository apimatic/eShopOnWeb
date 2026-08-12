# Twilio SMS Integration — CONTRACT SHEET (src/PublicApi, .NET 8)

Scope: send SMS order notifications, schedule/cancel scheduled messages, fetch/list/redact
messages, and validate destination numbers via Lookup. Grounded strictly in the bundled SDK map
(`sdk-map.md`, `map/operations/*`, `map/models/*`) plus the SDK server source where the map was
silent. **Do not write application code from memory of the Twilio REST API — use only the rows
below.** SDK package: `AsadAli.TwilioSdk` (install version-less). Map source commit `51fdf48`.

## 1. Scope & sequence

1. **Client & DI setup** — register `TwilioSdkClient` with `IHttpClientFactory`; build
   `TwilioSdkClientOptions` (credentials + messaging base-URL override).
2. **Auth** — set `options.AccountSidAuthToken` (Basic auth).
3. **Base-URL override for messaging** — set `options.Server.Default.Production.BaseUrl` from
   `Twilio:BaseUrl` when present.
4. **Send SMS** — `client.Api20100401Message.CreateMessage(...)`.
5. **Schedule message** — same `CreateMessage`, with `scheduleType` + `sendAt` (+ messaging service).
6. **Cancel scheduled** — `client.Api20100401Message.UpdateMessage(...)` with `status = Canceled`.
7. **Fetch one** — `client.Api20100401Message.FetchMessage(...)`.
8. **Redact body** — `UpdateMessage(...)` with empty `body`.
9. **List for reconciliation** — `client.Api20100401Message.ListMessage(...)` with From + DateSent range.
10. **Lookup / validate destination** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3(...)`.
11. **Error boundary** — catch `SdkException<RawError>` (+ `JsonException`, see Required Reading).

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C#
> identifier. The cancellation-token parameter really is named `ct`: in named arguments write
> `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each
> one from that type's own map row, never from where a neighbouring type sits. Enums, unions,
> auth, server and client-config types are spread across different child namespaces, and two
> types configured side by side in the same options object routinely live in different ones.
> Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and
> the build breaks.

### Namespaces (`using` directives) — one per type kind

| Type | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `AddTwilioSdkClient` (DI extension on `IServiceCollection`) | `TwilioSdk` |
| `ServerEnvironment`, `DefaultOptions` (and its `ProductionOptions`) | `TwilioSdk.Servers` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| Controllers `Api20100401Message`, `LookupsV2PhoneNumber` | `TwilioSdk.Api` |
| Records `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` |
| Enums `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumContentRetention`, `MessageEnumAddressRetention` | `TwilioSdk.Models.Enums` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |

### Client construction, auth & server override (client-config facts)

Source: `sdk-map.md` *Getting a client* / *Servers & auth*; `ServerOptions.cs`,
`Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`,
`Core/Authentication/Basic/BasicAuthCredentials.cs` (server files read from source — the map does
not carry the `ServerOptions`/base-URL shape).

- **Constructor:** `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
  The `HttpClient` is the first (positional) argument — this is the test seam.
- **DI:** `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)`
  (extension on `IServiceCollection`, namespace `TwilioSdk`).
- **`TwilioSdkClientOptions` properties:** `Environment: ServerEnvironment` ·
  `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` ·
  `AccountSidAuthToken: BasicAuthCredentials?`.
- **Credentials (Basic auth):** `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <API key SID or Account SID>, Password = <API key secret or Auth Token> }`.
  `BasicAuthCredentials` is a `sealed class` with **`required`** `Username: string` and
  `Password: string` (object-initializer, not a constructor). Per the source XML doc: prefer an
  API key SID as username + its secret as password; Account SID + Auth Token is "local testing
  only".
- **Environment:** `options.Environment` is `ServerEnvironment`; only member is
  `ServerEnvironment.Production` (`"production"`). `ServerEnvironment.Default()` returns
  `Production`.
- **`ServerOptions` shape (`options.Server`):** it holds **15 independent per-service server
  nodes** — `Default`, `Default1` … `Default14`, each a distinct `Default{N}Options` type. Each
  node exposes `Production.BaseUrl` (a `string`). This is how one host is overridden without
  touching the others.
- **Default base URLs (per node, per environment):**
  - **Messaging / 2010-04-01 REST (`Api20100401Message`) → the `Default` node** (operation rows
    tag it `Default (api)`): `options.Server.Default.Production.BaseUrl`, default
    **`https://api.twilio.com`**.
  - **Lookup v2 (`LookupsV2PhoneNumber`) → the `Default4` node** (rows tag it `Default4
    (lookups)`): `options.Server.Default4.Production.BaseUrl`, default
    **`https://lookups.twilio.com`**.
- **Base URL is per-client, nested per-environment, and per-service-node.** It is set on the
  options object you pass to the constructor/DI callback (per-client), the value lives under the
  environment (`.Production`), and each service has its own node — so it is NOT a single
  global/per-environment address.
- **Requirement 1 (override MESSAGING only, verbatim):** when config `Twilio:BaseUrl` is set,
  assign it verbatim to **`options.Server.Default.Production.BaseUrl`** — that is the node every
  `Api20100401Message` call (Create/Fetch/List/Update/Delete) resolves against. Leaving
  `Default4` untouched keeps Lookup on `lookups.twilio.com`. Assign the string exactly as read
  (no trimming/normalisation) to honour "used verbatim".

### Operations

| # | Op & accessor | Signature (params in order; all nullable-no-default params MUST be passed explicitly, pass `null` to skip) | Request fields (wire ← C#) | Returns / fields read | Error | Map page |
|---|---|---|---|---|---|---|
| Send / Schedule | `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `To ← to` (required, positional), `From ← from`, `Body ← body`, `MessagingServiceSid ← messagingServiceSid`, `ScheduleType ← scheduleType`, `SendAt ← sendAt`. **No request record — flat params.** `accountSid` is the path Account SID (required). | `ApiV2010AccountMessage` (see record below) | `SdkException<RawError>` (Case B) | `operations/Api20100401Message.md` |
| Fetch one | `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `sid` = message SID (path, required) | `ApiV2010AccountMessage` | `SdkException<RawError>` (Case B) | `operations/Api20100401Message.md` |
| Cancel / Redact | `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `Body ← body`, `Status ← status`. Both nullable-no-default → **pass both explicitly.** | `ApiV2010AccountMessage` | `SdkException<RawError>` (Case B) | `operations/Api20100401Message.md` |
| List | `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `To ← to`, `From ← from`, `DateSent ← dateSent`, **`DateSent< ← dateSentQuery`** (upper bound), **`DateSent> ← dateSentQueryQuery`** (lower bound), `PageSize ← pageSize`, `Page ← page`, `PageToken ← pageToken` | `ListMessageResponse` (see record) | `SdkException<RawError>` (Case B) | `operations/Api20100401Message.md` |
| Delete (whole record) | `client.Api20100401Message.DeleteMessage` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `sid` (path, required) | `void` (Task) | `SdkException<RawError>` (Case B) | `operations/Api20100401Message.md` |
| Lookup / validate | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` = number to validate (path, required). `Fields ← fields`, `CountryCode ← countryCode` (rest optional). | `LookupResponse` (see record) | `SdkException<RawError>` (Case B) | `operations/LookupsV2PhoneNumber.md` |

**Per-capability notes (each maps to a numbered requirement):**

- **Req 2 (send SMS):** call `CreateMessage(accountSid, to, /* 20 nullable middle params = null */, from: <sender>, ..., messagingServiceSid: null, body: <text>, ...)`. Provide **either** `from` **or** `messagingServiceSid` (both nullable in the contract; which one is mandatory is a Twilio server rule, not enforced by the signature — see trap). The response SID field is **`Sid`**; delivery status field is **`Status`** of type `MessageEnumStatus?` (values below).
- **Req 3 (schedule):** set `scheduleType: MessageEnumScheduleType.Fixed` and `sendAt: <DateTimeOffset>`. **Type is `DateTimeOffset?`, not `DateTime?`** — convert accordingly. The generated doc on `MessageEnumScheduleType` states scheduling is **"For Messaging Services only"** and requires `fixed` together with the send time — so a `MessagingServiceSid` is effectively required for scheduling even though the parameter is nullable in the signature (see trap). (Doc text says `send_time`; the actual generated field is `SendAt ← sendAt`.)
- **Req 4 (cancel scheduled):** `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)`. `MessageEnumUpdateStatus` has exactly one member: `Canceled` (`"canceled"`).
- **Req 6 (redact body):** `UpdateMessage(accountSid, sid, body: "" /* empty */, status: null)`. The operation's map note explicitly says UpdateMessage is "used to redact Message `body` text". This redacts the text while the record/status survive; `DeleteMessage` (separate op) removes the whole record. Whether an empty-string body fully clears the stored text is server-side behaviour — verify against the fetched record's `Body` after the call (label: UNVERIFIED, server-confirmed only).
- **Req 5 (fetch status):** read `Status` (`MessageEnumStatus?`), `ErrorCode` (`int?`), `ErrorMessage` (`string?`) off `ApiV2010AccountMessage`.
- **Req 7 (list over a DateSent range):** filter with `from: <sender>`, `dateSentQueryQuery: <rangeStart>` (wire `DateSent>`), `dateSentQuery: <rangeEnd>` (wire `DateSent<`). **Watch the reversed naming:** `dateSentQuery` is the `<` (upper) bound and `dateSentQueryQuery` is the `>` (lower) bound. All three date filters are `DateTimeOffset?`. **No built-in pager** (map: "Pagination: none"): loop by incrementing `page` (with a fixed `pageSize`) or by following `NextPageUri` from the response until `Messages` is empty / `NextPageUri` is null. Per-message fields available on `ApiV2010AccountMessage` in the list: `Sid`, `To`, `From`, `Status`, `DateSent` (note: `string?`, not a date type), `Body`.
- **Req 8 (Lookup):** served from the **`Default4` (lookups)** host (`https://lookups.twilio.com`), independent of the messaging base-URL override — see server section. Validity flag is **`Valid` (`bool?`)**; canonical E.164 form is **`PhoneNumber` (`string?`)**; `NationalFormat` (`string?`) is the national form; `ValidationErrors` (`IReadOnlyList<ValidationError>?`) lists why an invalid number failed. Pass `fields` to request extra datasets; a bare validation needs only `phoneNumber` (rest `null`). **The SDK DOES expose phone-number lookup/validation — no gap to report.**

### Response records (fields the integration reads)

`ApiV2010AccountMessage` — `Models/ApiV2010AccountMessage.cs` (map `records-1-Ac-Ca.md`). All
fields nullable (`?`):
`Body (body): string`, `NumSegments (num_segments): string`, `Direction (direction): MessageEnumDirection`, `From (from): string`, `To (to): string`, `DateUpdated (date_updated): string`, `Price (price): string`, `ErrorMessage (error_message): string`, `Uri (uri): string`, `AccountSid (account_sid): string`, `NumMedia (num_media): string`, `Status (status): MessageEnumStatus`, `MessagingServiceSid (messaging_service_sid): string`, `Sid (sid): string`, `DateSent (date_sent): string`, `DateCreated (date_created): string`, `ErrorCode (error_code): int`, `PriceUnit (price_unit): string`, `ApiVersion (api_version): string`, `SubresourceUris (subresource_uris): object`.
→ **Message SID = `Sid`; delivery status = `Status` (`MessageEnumStatus?`); error info = `ErrorCode` (`int?`) + `ErrorMessage` (`string?`). `DateSent` is a `string?`, parse if you need a date.**

`ListMessageResponse` — `Models/ListMessageResponse.cs` (map `records-4-Li-Me.md`). All nullable:
`End (end): int`, `FirstPageUri (first_page_uri): string`, `NextPageUri (next_page_uri): string`, `Page (page): int`, `PageSize (page_size): int`, `PreviousPageUri (previous_page_uri): string`, `Start (start): int`, `Uri (uri): string`, `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>`.
→ Iterate `Messages`; drive paging off `NextPageUri` (null = done) or `Page`/`PageSize`.

`LookupResponse` — `Models/LookupResponse.cs` (map `records-4-Li-Me.md`). Relevant nullable fields:
`CallingCountryCode (calling_country_code): string`, `CountryCode (country_code): string`, `PhoneNumber (phone_number): string`, `NationalFormat (national_format): string`, `Valid (valid): bool`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>`, plus optional datasets (`CallerName`, `LineTypeIntelligence`, `SimSwap`, `LineStatus`, `IdentityMatch`, `ReassignedNumber`, `SmsPumpingRisk`, `CallForwarding`, `PhoneNumberQualityScore`, `PreFill`), `Url (url): string`.
→ **Canonical E.164 = `PhoneNumber`; usability flag = `Valid`.**

### Enums (values needed) — namespace `TwilioSdk.Models.Enums`; build via `Type.FromValue("wire")` or the static member `Type.Member`

| Enum | C# member (wire value) | Used for |
|---|---|---|
| `MessageEnumStatus` (`status` on the message record) | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | reading delivery status (Req 2, 5) |
| `MessageEnumScheduleType` (`scheduleType` request param) | `Fixed (fixed)` — the only value | scheduling (Req 3) |
| `MessageEnumUpdateStatus` (`status` on UpdateMessage) | `Canceled (canceled)` — the only value | cancelling a scheduled message (Req 4) |
| `MessageEnumContentRetention` (`contentRetention` request param, optional) | `Retain (retain)`, `Discard (discard)` | privacy control at send time (optional) |
| `MessageEnumAddressRetention` (`addressRetention` request param, optional) | `Retain (retain)`, `Obfuscate (obfuscate)` | privacy control at send time (optional) |

These are `StringEnum<T>`, **not** C# enums (member is `MessageEnumStatus.Delivered`, never
`MessageEnumStatus.delivered`). Source: `map/models/enums.md`.

### Error handling (Req 9)

**Every operation in scope is Case B** — the only SDK exception that reaches your catch is
`SdkException<RawError>` (`TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`).
There are **no typed error accessors** and **no `TryGet…` shapes** for these ops. Read from
`ex.Error` (a `RawError`):

- HTTP status → `ex.Error.StatusCode` (`System.Net.HttpStatusCode`).
- Raw body → `ex.Error.ReadAsString()`.
- Twilio error code / message → `ex.Error.ReadAsJson<T>()` into your own record shaped to
  Twilio's error body (fields `code`, `message`, `more_info`, `status`). **This deserialize can
  itself throw `JsonException`** — see Required Reading. Extract best-effort and fall back to
  `ReadAsString()`/the generic message when the shape does not match (label: the exact
  error-body shape is UNVERIFIED — only live traffic confirms it; code defensively).

There is **no no-throw (`…Result`) variant** for any of these operations (map: absent across the
SDK) — the call throws, so a `try/catch` is mandatory.

---

## Trap notes (load the named skill before writing that step)

> ⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline lifetime and whether the SDK client
> wrapper is transient vs singleton are not shown by the constructor. **MUST load
> `dotnet-client-initialization`** before registering `AddTwilioSdkClient` / newing the client.

> ⚠ Step 2 (auth) — where and when credentials must be set relative to client construction, and
> loading the secret from configuration rather than hardcoding, are not shown by the property
> type. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

> ⚠ Step 3 (base-URL override / resilience) — the SDK's `Retry.Timeout` does **not** bound a whole
> call and is not the `HttpClient` timeout; and which calls actually retry (a `POST` create can
> re-execute on a transport failure regardless of `HttpMethodsToRetry`) is not visible in the
> option names. Whether a failed `CreateMessage` write can be silently re-sent matters for
> duplicate-SMS risk. **MUST load `dotnet-configuration-resilience`** before tuning the client
> or relying on the base-URL override behaviour.

> ⚠ Step 4/5 (calling / building the send) — the 20+ nullable-no-default middle parameters of
> `CreateMessage` have no C# defaults and mis-bind in a positional call; whether `From` or
> `MessagingServiceSid` is the mandatory sender, and whether scheduling truly needs a messaging
> service, are Twilio server rules the signature cannot show. **MUST load
> `dotnet-calling-endpoints`** (use named arguments) before writing the call.

> ⚠ Step 4/6 (models & enums) — `StringEnum<T>` is not a C# enum, and unmodeled JSON fields drop
> on deserialize; whether an empty-string `body` on `UpdateMessage` actually redacts is
> server-observable only. **MUST load `dotnet-models`** before constructing payloads / mapping
> the response.

> ⚠ Step 11 (error boundary) — which exception types actually reach the catch, and why an
> `SdkException`-only ladder is silently wrong, are not shown by any signature. **MUST load
> `dotnet-error-handling`** before writing the boundary (see mandatory rows below).

---

## REQUIRED READING — load BEFORE implementation starts

These `dotnet-*` skills are the usage layer; this sheet deliberately does not carry their
contents. Load each before writing its step:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 2 — setting Basic credentials, config-sourced secrets |
| `dotnet-configuration-resilience` | Step 3 — base-URL override, retries/timeouts, pagination |
| `dotnet-calling-endpoints` | Steps 4–10 — named-argument calls, required vs optional params |
| `dotnet-models` | Steps 4–8 — request/response models, `StringEnum<T>`, wire names |
| `dotnet-error-handling` | Step 11 — the exception boundary (always required) |
| `dotnet-testing` | tests — the `HttpClient` seam |

**Mandatory `dotnet-error-handling` hazard rows (write into the FIRST boundary, not later):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps
  every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller
  that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **Path:** the brief did not dictate a path for this file, so it was written to the repo-root
  default: `C:\claude-runs\t4-task4-plugin-opus48high-006\repo\twilio-plan.md`.
- **Sender identity (send/schedule):** the SDK marks both `from` and `messagingServiceSid`
  nullable, so it does not enforce which is required. Assumed you supply `From` (a specific
  sending number) for immediate sends per the brief, and a `MessagingServiceSid` for scheduled
  sends (the SDK's own `MessageEnumScheduleType` doc says scheduling is "For Messaging Services
  only"). Confirm which sender your Twilio account is provisioned for. (UNVERIFIED at the
  contract level — server-enforced.)
- **Redaction semantics:** that an empty-string `body` on `UpdateMessage` fully clears the stored
  text (vs. requiring `DeleteMessage`) is server-observable only; the map confirms UpdateMessage
  is *the* redaction mechanism, but verify the fetched `Body` post-call. (UNVERIFIED.)
- **Twilio error-body shape** consumed via `RawError.ReadAsJson<T>()` (`code`/`message`/
  `more_info`/`status`) is the documented Twilio error envelope but is not a generated model in
  this SDK — deserialize defensively and fall back to `ReadAsString()`. (UNVERIFIED — live only.)
- No blockers: every capability requested (send, schedule, cancel, fetch, redact, list, lookup,
  error handling, messaging-only base-URL override) is exposed by the SDK and grounded above.
