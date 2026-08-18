# Twilio .NET SDK — SMS Order-Notifications Plan & Contract Sheet

Scope: eShopOnWeb (.NET 8, ASP.NET Core `PublicApi`). Add SMS order-notifications via the
APIMatic-generated Twilio .NET SDK (`AsadAli.TwilioSdk`, root namespace `TwilioSdk`). Every fact
below is grounded in the bundled SDK map (source commit `51fdf48`); the few facts the map did not
carry (server base-URL override members, auth-credential shape, error-boundary namespaces) were
resolved from the SDK source and are marked `(source)`.

## Package to reference

- `dotnet add package AsadAli.TwilioSdk` — install **version-less** (float to latest; do not pin).
- Root namespace / `using`: `TwilioSdk`. Client class `TwilioSdkClient`; options `TwilioSdkClientOptions`.
- The SDK splits types across child namespaces; C# does not import them transitively. `using` list
  for this integration (each type's namespace is taken from its own map row / source):
  - `TwilioSdk` — `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`
  - `TwilioSdk.Servers` — `ServerEnvironment`, `DefaultOptions`, `Default4Options`
  - `TwilioSdk.Models` — `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`
  - `TwilioSdk.Models.Enums` — `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumStatus`
  - `TwilioSdk.Core.Authentication.Basic` — `BasicAuthCredentials` *(source)*
  - `TwilioSdk.Core.Exceptions` — `SdkException<TError>` *(source)*
  - `TwilioSdk.Core.ErrorResponse` — `RawError` *(source)*
  - `TwilioSdk.Core` — `RequestOptions` *(source; the optional `requestOptions` param type)*

---

## Scope & sequence

1. **Client registration & auth** (DI in `PublicApi` startup) — build `TwilioSdkClientOptions`
   with Basic credentials + messaging base-URL override; register `TwilioSdkClient`.
   Uses no operation.
2. **Phone-number validation / canonical E.164** (Flow 1, contact registration) —
   `client.LookupsV2PhoneNumber.FetchPhoneNumber3`.
3. **Send order SMS** — `client.Api20100401Message.CreateMessage`.
4. **Send scheduled SMS** — `client.Api20100401Message.CreateMessage` (with `scheduleType` + `sendAt`).
5. **Fetch delivery status** — `client.Api20100401Message.FetchMessage`.
6. **Cancel a scheduled message** — `client.Api20100401Message.UpdateMessage` (status = canceled).
7. **Redact a sent message body** — `client.Api20100401Message.UpdateMessage` (body = "").
8. **List / reconcile messages** — `client.Api20100401Message.ListMessage`.

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each
> one from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types are spread across different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Client construction, auth & servers  (map: `sdk-map.md` §Getting a client, §Servers & auth)

| Item | Contract fact |
|---|---|
| Client ctor | `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` — `httpClient` is `System.Net.Http.HttpClient`. |
| DI helper | `services.AddTwilioSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`). |
| Controller access | Every API group is a property: `client.Api20100401Message`, `client.LookupsV2PhoneNumber`. |
| Auth property | `options.AccountSidAuthToken` of type `BasicAuthCredentials?` — Basic auth. |
| Auth credential shape *(source)* | `new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members are `required string` (`Core/Authentication/Basic/BasicAuthCredentials.cs`). Username = AccountSid (or an API key SID), Password = AuthToken (or API-key secret). Set **before** constructing the client / inside the DI callback. |
| Environment | `options.Environment = ServerEnvironment.Production;` (the only member; `Servers/ServerEnvironment.cs`). |
| Messaging base-URL override *(source)* | `options.Server.Default.Production.BaseUrl` — the `Api20100401Message` operations use server **"Default (api)"**, default `https://api.twilio.com`. Set this string verbatim to `Twilio:BaseUrl` **only when that key is present**; otherwise leave the default. `options.Server` is `ServerOptions` (root ns); `Server.Default` is `DefaultOptions` (`Servers/DefaultOptions.cs`). |
| Lookup base-URL (NOT overridden) *(source)* | Lookup uses server **"Default4 (lookups)"** = `options.Server.Default4.Production.BaseUrl`, default `https://lookups.twilio.com` (`Servers/Default4Options.cs`). The `Twilio:BaseUrl` override does **not** apply here — leave `Default4` at its default so lookup keeps hitting `lookups.twilio.com`. |
| `AccountSid` path param | The `string accountSid` first argument of every `Api20100401Message` operation is `Twilio:AccountSid` (path segment `Accounts/{AccountSid}`), independent of the auth username. |

### Operations — `client.Api20100401Message`  (map: `operations/Api20100401Message.md`)

**`CreateMessage`** — send SMS (also scheduled). `POST /2010-04-01/Accounts/{AccountSid}/Messages.json`.
Full signature (all 24 middle params are nullable-with-no-default → **must be passed explicitly**,
pass `null` to skip):

```
CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid,
  double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery,
  MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention,
  bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType,
  bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms,
  string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom,
  string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid,
  RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- Key fields (wire ← C#): `To`←`to` (required, positional), `From`←`from`, `Body`←`body`,
  `MessagingServiceSid`←`messagingServiceSid`, `ScheduleType`←`scheduleType`, `SendAt`←`sendAt`.
- **Plain send:** pass `to`, `from` (= `Twilio:FromNumber`) **or** `messagingServiceSid`
  (= `Twilio:MessagingServiceSid`), `body`; everything else `null`.
- **Scheduled send (Step 4):** set `scheduleType: TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed`
  and `sendAt: <DateTimeOffset a few days out>`, and pass `messagingServiceSid:` (required for
  scheduling) with `from: null`. Constraint (map enum note, `MessageEnumScheduleType`): scheduling is
  **Messaging-Services-only** — `SendAt` requires `ScheduleType=fixed` used together with a
  `MessagingServiceSid`; do not also pass `From`.
- Returns `TwilioSdk.Models.ApiV2010AccountMessage` (see response record below).
- Error: **Case B** — `SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Accessors:
  `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`,
  `ReadAsBytes(): ReadOnlyMemory<byte>`. No typed accessors. No no-throw variant. No pagination.

**`FetchMessage`** — read current status. `GET …/Messages/{Sid}.json`.
`FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns `ApiV2010AccountMessage`. Path param `Sid`←`sid`. Error Case B (as above).

**`UpdateMessage`** — cancel scheduled + redact body. `POST …/Messages/{Sid}.json`.
`UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)`
(`body` and `status` are nullable-no-default → **pass both explicitly**). Wire: `Body`←`body`,
`Status`←`status`. Returns `ApiV2010AccountMessage`. Error Case B.
- **Cancel a not-yet-sent message (Step 6):** `body: null, status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled`.
  `MessageEnumUpdateStatus` has exactly one member `Canceled` (wire `canceled`). If the message
  already sent, the provider rejects the cancel — surfaces as Case B `SdkException<RawError>`
  (HTTP 4xx), **not** a success; read `StatusCode` + `ReadAsString()` for the Twilio error body.
- **Redact a sent message body (Step 7):** `body: "", status: null` — sets `Body` to empty string,
  which redacts the provider-side message content while the message record (SID, status, dates)
  survives. The map documents `UpdateMessage` as "used to redact Message `body` text"; the SDK
  exposes **no** separate redact endpoint — body-to-empty via `UpdateMessage` is the mechanism, and
  the record is not deleted (that is a distinct `DeleteMessage` operation). `UNVERIFIED` (live wire):
  that an empty-body update actually purges carrier-side content is a provider behaviour only live
  traffic can confirm — on the read-back in Step 5, extract `Body` best-effort and treat a non-empty
  value as "redaction not yet reflected", do not assume it.

**`ListMessage`** — reconciliation. `GET …/Messages.json`.
`ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)`
(the 8 filter/paging params are nullable-no-default → **pass explicitly**, `null` to skip).
- Wire ← C# (note the three date params map to different operators):
  `To`←`to`, `From`←`from`, `DateSent`←`dateSent` (exact match),
  **`DateSent<`←`dateSentQuery`** (on/before = range **upper** bound),
  **`DateSent>`←`dateSentQueryQuery`** (on/after = range **lower** bound),
  `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`.
- For "From = sending number, DateSent in [after, before]": `from: <number>`,
  `dateSent: null`, `dateSentQuery: <before>` (upper), `dateSentQueryQuery: <after>` (lower).
- Returns `TwilioSdk.Models.ListMessageResponse`. **Envelope:** the list items are in
  `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` — reads go one level down into
  `.Messages`. Paging fields: `Page (page): int?`, `PageSize (page_size): int?`,
  `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `FirstPageUri`, `Start`, `End`, `Uri`.
- **Pagination: none built-in** (map: "only `page`, no `perPage`"; no auto-pagination helper). Page
  manually: request successive pages via `page:`/`pageToken:`, stopping when `NextPageUri` is null.

**`DeleteMessage`** *(not in the 8 steps, listed for completeness)* —
`DeleteMessage(string accountSid, string sid, …)` → `void`; full provider-side delete (destroys the
record). Use `UpdateMessage` body-to-empty for redaction-that-keeps-the-record instead.

### Operation — `client.LookupsV2PhoneNumber`  (map: `operations/LookupsV2PhoneNumber.md`)

**`FetchPhoneNumber3`** — validate + canonical E.164. `GET /v2/PhoneNumbers/{PhoneNumber}` (lookups host).
`FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
(15 middle params nullable-no-default → **pass `null` to skip**; for basic validation pass just
`phoneNumber` and `null` for the rest).
- Path param `PhoneNumber`←`phoneNumber` (the raw/E.164 number to validate).
- Returns `TwilioSdk.Models.LookupResponse`. Fields used: `Valid (valid): bool?` (usable-destination
  flag), `PhoneNumber (phone_number): string?` (**canonical E.164 form**),
  `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`,
  `CallingCountryCode (calling_country_code): string?`,
  `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`.
- Error: **Case B** `SdkException<RawError>` (same accessors). No pagination. No no-throw variant.
- Host: lookup uses server **"Default4 (lookups)"** = `https://lookups.twilio.com` and is **NOT**
  affected by the `Twilio:BaseUrl` messaging override (see client table).

### Response record — `ApiV2010AccountMessage`  (map: `records-1-Ac-Ca.md`)

Returned by Create/Fetch/Update and as each list item. Fields the integration reads (`C# (wire): type`):
`Sid (sid): string?` · `Status (status): MessageEnumStatus?` · `ErrorCode (error_code): int?` ·
`ErrorMessage (error_message): string?` · `To (to): string?` · `From (from): string?` ·
`Body (body): string?` · `DateSent (date_sent): string?` · `DateCreated (date_created): string?` ·
`DateUpdated (date_updated): string?` · `MessagingServiceSid (messaging_service_sid): string?` ·
`Direction (direction): MessageEnumDirection?` · `Price (price): string?` · `NumSegments`, `NumMedia`,
`AccountSid`, `Uri`, `ApiVersion`, `PriceUnit`, `SubresourceUris (object?)`. (Note: `DateSent`/dates
are `string?`, not `DateTimeOffset`.)

### Enum value tables  (map: `models/enums.md`) — all are `StringEnum<T>`, not C# enums

| Enum (`TwilioSdk.Models.Enums`) | Members (C# `Member` = wire) |
|---|---|
| `MessageEnumStatus` (response `Status`) | `Queued`=queued, `Sending`=sending, `Sent`=sent, `Failed`=failed, `Delivered`=delivered, `Undelivered`=undelivered, `Receiving`=receiving, `Received`=received, `Accepted`=accepted, `Scheduled`=scheduled, `Read`=read, `PartiallyDelivered`=partially_delivered, `Canceled`=canceled |
| `MessageEnumScheduleType` (request `scheduleType`) | `Fixed`=fixed *(only member)* |
| `MessageEnumUpdateStatus` (update `status`) | `Canceled`=canceled *(only member)* |

Build via the static member (`MessageEnumScheduleType.Fixed`) or `MessageEnumScheduleType.FromValue("fixed")`.

---

## Trap notes (hazard + skill pointer — load the skill before writing that step)

⚠ Step 1 (client & DI registration) — the `HttpClient`/handler pipeline you hand `TwilioSdkClient`
has a lifetime and ownership contract the ctor signature does not show; getting it wrong causes
socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (resilience / base URL / retries) — the SDK's `RetryOptions` (`Timeout`, `HttpMethodsToRetry`,
`MaxRetries`, backoff) do not bound what their names suggest, and how a transport failure interacts
with a non-idempotent `POST /Messages` (a message send) determines whether an SMS can go out more
than once. The base-URL override and per-attempt-vs-total timeout also live here. **MUST load
`dotnet-configuration-resilience`** before tuning the client or relying on retry/timeout behaviour.

⚠ Step 1 (auth) — *when* and *where* the Basic credentials must be set relative to client
construction, and how to source them from configuration rather than hardcode, are not visible in the
property type. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 2–8 (every call) — these operations have long runs of nullable-no-default parameters that
must be passed explicitly and that mis-bind silently in a positional call; the async/`ct` usage and
request-body shaping also bite here. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–8 (models & enums) — `MessageEnumStatus`/`MessageEnumScheduleType`/`MessageEnumUpdateStatus`
are `StringEnum<T>`, not C# enums, and unmodeled JSON fields are dropped on deserialize, which affects
how you compare a returned status and how much of the payload you can trust. **MUST load
`dotnet-models`** before constructing payloads or mapping responses onto domain types.

⚠ Step 9 / all steps (error boundary) — which exception types actually reach a catch, how to read
the Twilio error code/message + HTTP status off a Case-B `SdkException<RawError>`, and the
`JsonException` traps that make a reasonable catch ladder silently wrong are all outside the signature.
A carrier-undeliverable outcome is **not** an exception — it comes back as a returned
`Status` of `Undelivered`/`Failed` with `ErrorCode`/`ErrorMessage` populated on
`ApiV2010AccountMessage`, so the delivery-status check (Step 5) must inspect the response, while
config/transport failures (401/host/timeout) throw. **MUST load `dotnet-error-handling`** before
writing the boundary.

⚠ Tests — the `HttpClient` constructor argument is the fake seam; match the project's existing test
framework/assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration. |
| `dotnet-authentication` | Step 1 — supplying Basic credentials, timing, sourcing from config. |
| `dotnet-configuration-resilience` | Step 1 — retry/timeout semantics, base-URL/server selection, non-idempotent-POST re-send, manual pagination. |
| `dotnet-calling-endpoints` | Steps 2–8 — required-vs-optional params, named args, async/`ct`, request bodies. |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>` handling, required/nullable members, dropped unmodeled fields. |
| `dotnet-error-handling` | Step 9 / all — exception boundary, reading status + Twilio error body, JsonException traps. |
| `dotnet-testing` | Tests — the HttpClient fake seam, real-behaviour assertions. |

Two hazard rows that MUST shape the error boundary from the first version (`System.Text.Json.JsonException`
reaches the boundary from two directions needing opposite handling):

- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — an
  SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException`
  *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException`
  and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then
  reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that
  can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **All 9 requested capabilities are covered by the SDK map — no gaps.** In particular, phone-number
  **Lookup (capability 8) is covered** via `client.LookupsV2PhoneNumber.FetchPhoneNumber3`
  (`LookupResponse.Valid` + `.PhoneNumber` canonical E.164), and it uses the `lookups.twilio.com`
  host independently of the `Twilio:BaseUrl` messaging override.
- **Assumption (scheduled send):** the request states "a few days later" — the plan passes a caller-chosen
  `DateTimeOffset` for `sendAt` with `scheduleType: Fixed` and a `messagingServiceSid`. Twilio's own
  min/max lead-time window for scheduling is a provider-side rule not expressed in the SDK contract;
  if `sendAt` is outside it the send is rejected as a Case-B `SdkException<RawError>` — handle it on
  the same error boundary. Labeled `UNVERIFIED` (only live traffic confirms the exact window).
- **Assumption (redaction semantics):** that an empty-body `UpdateMessage` purges carrier-side content
  is a provider behaviour the SDK cannot confirm — the plan directs best-effort read-back (Step 5) and
  does not assume purge. Labeled `UNVERIFIED`.
- **Assumption:** `Twilio:FromNumber` vs `Twilio:MessagingServiceSid` — a plain send may use either;
  a **scheduled** send must use `MessagingServiceSid` (and must not also set `From`). Caller decides
  which for non-scheduled sends.
- No blockers to implementation.
