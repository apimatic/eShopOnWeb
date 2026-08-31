# Twilio .NET SDK integration plan — eShopOnWeb order notifications (src/PublicApi)

Grounded in the bundled SDK map (`twilio-getting-started` skill, commit `51fdf48`) and, for the gaps the
map does not carry, the SDK source at that same commit (the exact commit NuGet `AsadAli.TwilioSdk` **2.0.0** —
the only published version — was built from; verified via the package nuspec). Map pages cited per row.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Install package; `TwilioOptions` config class; DI registration of the SDK client | — (client setup) |
| 2 | POST /api/contact-numbers — validate + canonicalize via Lookup v2, then store | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | GET /api/contact-numbers, DELETE /api/contact-numbers/{id} | — (app store only) |
| 4 | POST /api/orders — place order, send "placed" SMS | `Api20100401Message.CreateMessage` (immediate) |
| 5 | POST /api/orders/{id}/dispatch — "on its way" SMS + provider-scheduled follow-up | `CreateMessage` (immediate) + `CreateMessage` (`ScheduleType`/`SendAt`/`MessagingServiceSid`) |
| 6 | POST /api/orders/{id}/cancel — cancel SMS + cancel the not-yet-sent scheduled message at the provider | `CreateMessage` + `UpdateMessage(status: Canceled)` |
| 7 | GET /api/my-orders, GET /api/orders/{id}/notifications — refresh provider state by fetch (no webhooks) | `FetchMessage` per stored message SID |
| 8 | POST /api/notifications/{id}/resend — app-level idempotency check, then a fresh send | `CreateMessage` |
| 9 | DELETE /api/notifications/{id}/content — redact body at provider, keep the record | `UpdateMessage(body: "")` |
| 10 | GET /api/notifications/reconciliation — provider list vs app records | `ListMessage` (`from` + `DateSent>`/`DateSent<` + manual pagination) |
| 11 | Error boundary, resilience tuning, tests | — (all of the above) |

A failed/undeliverable SMS never fails the enclosing operation: every send is wrapped, the outcome
(incl. `SdkException<RawError>`) is recorded on the notification row, and the endpoint returns the
domain result. A shopper with no registered number is skipped before any SDK call.

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

### 2.1 SDK identity & install (map: `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.TwilioSdk` — install version-less (`dotnet add package AsadAli.TwilioSdk`); floats to **2.0.0** (only published version) |
| Package target framework | `netstandard2.0` ⇒ consumable from the net8.0 PublicApi project |
| Root namespace | `TwilioSdk` |
| Client class | `TwilioSdkClient` (`TwilioSdk`) — single ctor `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` |
| Options class | `TwilioSdkClientOptions` (`TwilioSdk`) — props: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?` |
| DI extension | `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — `ServiceCollectionExtensions.cs`, namespace `TwilioSdk` |

### 2.2 Client construction, auth, per-API base URL (map: `sdk-map.md` *Servers & auth*; source: `Servers/*.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`, `ServiceCollectionExtensions.cs`)

- Auth scheme: HTTP Basic. Set `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }`.
  `BasicAuthCredentials` (namespace `TwilioSdk.Core.Authentication.Basic`) is a sealed class with
  `required string Username { get; init; }` and `required string Password { get; init; }` — object
  initializer will not compile without both.
- Environment: `ServerEnvironment.Production` (namespace `TwilioSdk.Servers`) is the only member and the default.
- **Base-URL override is per-server.** `options.Server` (`ServerOptions`, namespace `TwilioSdk`) has
  properties `Default` … `Default14`, each with `.Production.BaseUrl`:
  - server **`Default`** = `https://api.twilio.com` — the **Messaging API** (all five `Api20100401Message` operations resolve through it);
  - server **`Default4`** = `https://lookups.twilio.com` — the **Lookup API** (`FetchPhoneNumber3` resolves through it).
  - Therefore `Twilio:BaseUrl` maps to exactly one assignment:
    `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` — messaging only; Lookup stays on its default host. Do not touch `Default4`.
- DI shape (from source): `AddTwilioSdkClient` builds one options instance, calls `services.AddHttpClient()`,
  and registers the `TwilioSdkClient` as a **singleton** backed by `IHttpClientFactory.CreateClient()`;
  it also fills `Logging.LoggerFactory` from DI if unset. Configuration callback runs once at registration.

### 2.3 Operations

**Case B errors throughout: every operation below throws `SdkException<RawError>` only**
(`TwilioSdk.Core.Exceptions.SdkException<TError>`, sealed; `RawError` in `TwilioSdk.Core.ErrorResponse`).
Read: `ex.Error.StatusCode` (`HttpStatusCode`) · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` ·
`ex.Error.ReadAsBytes()`. No `…Result` no-throw variant exists for any of them. (map: `sdk-map.md` error model)

#### OP-1 Validate/canonicalize a number — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (map: `operations/LookupsV2PhoneNumber.md`)

- HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` on server `Default4` (lookups.twilio.com — **not** governed by `Twilio:BaseUrl`).
- Signature: `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — all 15 nullable params (`fields` … `partnerSubId`) **must be passed explicitly** (pass `null`). For plain
  validation pass `fields: null` (the base response already carries `valid`/`validation_errors`; `fields`
  opts into paid add-on packages such as `line_type_intelligence` — do not request them) and
  `countryCode: null` (send the number in E.164-ish form; `countryCode` is only a national-format hint).
- Returns `LookupResponse` (`TwilioSdk.Models`; map: `records-4-Li-Me.md`) — fields the integration reads:
  `Valid (valid): bool?` · `PhoneNumber (phone_number): string?` (**provider-canonical E.164 — store this, not the caller's input**) ·
  `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` · `NationalFormat (national_format): string?` ·
  `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?`.
- Valid vs unusable: treat `Valid == true` as registrable; `Valid != true` (false/null, inspect
  `ValidationErrors`) as rejected. `ValidationError` enum (`TwilioSdk.Models.Enums`; map: `enums.md`):
  `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`,
  `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.
- `UNVERIFIED` (only live traffic settles it): whether some malformed numbers surface as a non-2xx
  `SdkException<RawError>` instead of 200 + `valid:false`. Defensive directive: registration rejects on
  **either** `Valid != true` **or** a caught `SdkException<RawError>` (log `StatusCode` + `ReadAsString()`),
  so both shapes are handled.

#### OP-2 Send immediately — `client.Api20100401Message.CreateMessage` (map: `operations/Api20100401Message.md`)

- HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on server `Default` (api.twilio.com — governed by `Twilio:BaseUrl`). Body is form-url-encoded.
- Signature: `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — 24 nullable params (`statusCallback` … `contentSid`) **must be passed explicitly**; use named arguments.
- Immediate send: `accountSid: <Twilio:AccountSid>`, `to: <canonical E.164>`, `from: <Twilio:FromNumber>`,
  `body: <text>`, `messagingServiceSid: null`, `scheduleType: null`, `sendAt: null`, everything else `null`.
- Sender identity is `from` XOR `messagingServiceSid` (the SDK does not enforce this; a request with both or
  neither is rejected provider-side as a synchronous `SdkException<RawError>`).
- **`Idempotency-Key` is not caller-settable** (source: `Api/Api20100401Message.cs`): the SDK stamps every
  `CreateMessage` call with `Idempotency-Key: Guid.NewGuid()`. Consequence: resend idempotency (Flow 3) must
  live in the app — persist the caller's key and dedupe **before** invoking the SDK.
- Returns `ApiV2010AccountMessage` (`TwilioSdk.Models`; map: `records-1-Ac-Ca.md`) — fields the integration reads:
  `Sid (sid): string?` (provider message id — persist) · `Status (status): MessageEnumStatus?` (persist) ·
  `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` · `To (to)` / `From (from)` /
  `Body (body)` · `DateCreated/DateUpdated/DateSent (…): string?` (dates arrive as **strings**, parse app-side if needed) ·
  `MessagingServiceSid (messaging_service_sid): string?` · `Price (price)` / `PriceUnit (price_unit)` /
  `NumSegments (num_segments)` · `Direction (direction): MessageEnumDirection?` · `Uri (uri)` · `AccountSid (account_sid)`.
- Error: Case B `SdkException<RawError>` (synchronous rejection, e.g. bad `To`). Pagination: none.

#### OP-3 Schedule for later (provider-side) — same `CreateMessage`

- `scheduleType: MessageEnumScheduleType.Fixed` (only value; wire `fixed`), `sendAt: <future DateTimeOffset>`,
  `messagingServiceSid: <Twilio:MessagingServiceSid>`, `from: null` (scheduling is Messaging-Service-only —
  map: `enums.md` ScheduleType note), `to`/`body` as usual.
- Wire format of `SendAt`: serialized as ISO-8601 (source: form-param serialization via default STJ); pass a
  UTC `DateTimeOffset`. `UNVERIFIED`: whether the provider accepts the `+00:00` offset form vs trailing `Z` —
  defensive directive: always pass UTC and treat a synchronous 400 (`SdkException<RawError>`) as a scheduling
  rejection to surface, not retry blindly.
- Lead-time constraints (min/max scheduling window): **not expressed anywhere in the SDK surface** —
  `UNVERIFIED`; enforce the current provider-documented window app-side before calling, and handle a 400 via
  `RawError` as the provider's rejection.
- A scheduled message comes back with `Status == MessageEnumStatus.Scheduled (scheduled)` and already has its
  `Sid` — persist both; the Sid is the handle for OP-4.

#### OP-4 Cancel a scheduled message — `client.Api20100401Message.UpdateMessage` (map: `operations/Api20100401Message.md`)

- HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (server `Default`).
- Signature: `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` **must be passed explicitly**.
- Cancel: `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` — the enum's
  only value (wire `canceled`). Only works while the message is still `scheduled`; provider rejects otherwise
  (Case B). Returns the updated `ApiV2010AccountMessage` (expect `Status == Canceled`).

#### OP-5 Redact body, keep the record — same `UpdateMessage`

- Redact: `UpdateMessage(accountSid, sid, body: "", status: null)` — empty string wipes the body at the
  provider; the Message **record** (Sid, status, error fields, dates) survives and stays fetchable — this is
  the documented purpose of the operation (map row notes: "used to redact Message `body` text and to cancel
  not-yet-sent messages"). After redaction, `FetchMessage` returns the record with an empty `Body`.
- Do **not** use `DeleteMessage` for this flow: it deletes the whole resource (returns `void`/`Task`, Case B),
  so the fact-and-outcome trail the app must keep would be destroyed at the provider.

#### OP-6 Fetch current state — `client.Api20100401Message.FetchMessage` (map: `operations/Api20100401Message.md`)

- HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (server `Default`).
- Signature: `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`.
- Returns `ApiV2010AccountMessage` (same record as OP-2). This is the delivery-outcome poll (no webhooks):
  read `Status`, `ErrorCode`, `ErrorMessage`. Case B errors (a 404 here means the SID is gone — e.g. after a
  provider-side delete).

#### OP-7 Reconciliation list — `client.Api20100401Message.ListMessage` (map: `operations/Api20100401Message.md`; source: `Api/Api20100401Message.cs`)

- HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` (server `Default`).
- Signature: `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 nullable params **must be passed explicitly**.
- Server-side filters (wire ← C#): `To` ← `to` · **`From` ← `from`** (pass `Twilio:FromNumber` — this is the
  server-side sender filter the brief requires) · `DateSent` ← `dateSent` (exact day) ·
  **`DateSent<` ← `dateSentQuery`** (sent before — pass the range `to`) · **`DateSent>` ← `dateSentQueryQuery`**
  (sent after — pass the range `from`). The generated names are counter-intuitive: `dateSentQuery` = `<`,
  `dateSentQueryQuery` = `>` — bind by name, never positionally.
- Date wire format: the SDK serializes these via `ToIso8601()` → `yyyy-MM-ddTHH:mm:ss.fffZ` in UTC (source:
  `Core/Extensions/DateTimeOffsetExtensions.cs`) — full date-times, so the ISO-8601 `from`/`to` query values
  map directly; convert to UTC before calling.
- `pageSize`: default 50, max 1000 (source XML doc).
- Returns `ListMessageResponse` (`TwilioSdk.Models`; map: `records-4-Li-Me.md`):
  `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `NextPageUri (next_page_uri): string?` ·
  `PreviousPageUri (previous_page_uri)` · `FirstPageUri (first_page_uri)` · `Page (page): int?` ·
  `PageSize (page_size): int?` · `Start (start)` · `End (end)` · `Uri (uri)`.
- **Pagination is manual** — the SDK has no auto-pagination helper for this operation (map row: "Pagination:
  none"). Loop: call with `pageToken: null` first; while the response's `NextPageUri` is non-null, extract the
  page token the API embedded in that URI and pass it as `pageToken` on the next call (`page` is client-state
  only, per the SDK doc). The token-extraction mechanics are a named hazard → see Trap notes
  (`dotnet-configuration-resilience`).
- Case B errors.

### 2.4 Enum values needed (all `TwilioSdk.Models.Enums`, all `StringEnum<T>` — not C# enums; map: `enums.md`)

| Enum | Members (wire) | Used for |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | delivery outcome on every message record; terminal-failure = `Failed`/`Undelivered` (with `ErrorCode`/`ErrorMessage`); `Scheduled` = cancellable |
| `MessageEnumScheduleType` | `Fixed (fixed)` (only value) | OP-3 |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` (only value) | OP-4 |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | reconciliation sanity (`OutboundApi` = this app's sends) |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | OP-1 rejection reasons |
| `MessageEnumContentRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` | optional on CreateMessage — leave `null` (redaction is done via OP-5) |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` | optional — leave `null` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` | optional — leave `null` |
| `MessageEnumTrafficType` | `Free (free)` | optional — leave `null` |

### 2.5 Error model for this integration (map: `sdk-map.md` error model + each op row)

- Only `SdkException<RawError>` reaches a `catch` from these six operations (all Case B). There is no typed
  `{Operation}Error` with `TryGet…` accessors for any of them.
- Reading a failure: `ex.Error.StatusCode` for the HTTP status; `ex.Error.ReadAsString()` for the raw body;
  `ex.Error.ReadAsJson<T>()` into an **app-defined** DTO to try for the provider's structured error fields.
  `UNVERIFIED`: the provider error body's exact field names are not modeled by the SDK (Case B is raw) —
  defensive directive: attempt the typed read best-effort, fall back to the raw string, never let parsing the
  error throw inside the catch.
- Two failure shapes must be distinguished on every send:
  1. **Synchronous API rejection** — `CreateMessage` throws `SdkException<RawError>` (e.g. 400). The message
     was never accepted; no `Sid` exists.
  2. **Accepted-then-undeliverable** (the expected US-destination case) — `CreateMessage` returns normally
     with a `Sid` and an early `Status` (`queued`/`sent`); the carrier refusal appears **later** as
     `Status == failed|undelivered` with `ErrorCode`/`ErrorMessage` set, observable only via OP-6/OP-7 polls.
     Record both fields on each poll.

### 2.6 Recommended ASP.NET Core layout (app-side; adapt names to PublicApi conventions)

| File | Contents |
|---|---|
| `src/PublicApi/Twilio/TwilioOptions.cs` | `public class TwilioOptions { public const string SectionName = "Twilio"; public string AccountSid {get;set;} … AuthToken, FromNumber, MessagingServiceSid, string? BaseUrl }` — bound from the `Twilio:` section (env vars / user-secrets); validated non-empty except `BaseUrl` at startup |
| `src/PublicApi/Twilio/TwilioRegistration.cs` | `IServiceCollection` extension: binds/validates `TwilioOptions`, then `services.AddTwilioSdkClient(o => { o.AccountSidAuthToken = new BasicAuthCredentials { Username = opt.AccountSid, Password = opt.AuthToken }; if (opt.BaseUrl is not null) o.Server.Default.Production.BaseUrl = opt.BaseUrl; })` — `Default` only, never `Default4` |
| `src/PublicApi/Twilio/ITwilioMessaging.cs` + `TwilioMessaging.cs` | narrow seam over the six operations (validate-number, send, schedule, cancel-scheduled, fetch, redact, list-range) returning app DTOs; the only type that touches `TwilioSdk.*`; owns the Case-B error translation |
| `src/PublicApi/Notifications/` | notification entity (provider `Sid`, last polled `Status`/`ErrorCode`/`ErrorMessage`, kind, order id, resend idempotency-key store), domain service, endpoints |
| `src/PublicApi/ContactNumbers/` | contact-number entity (stores the **canonical** `PhoneNumber` from OP-1), endpoints |

`AuthToken` appears only in `TwilioOptions` and the registration callback — never in logs, responses, or source.

## 3. Trap notes

> ⚠ Step 1 (client registration) — the SDK's DI helper fixes a specific client/`HttpClient` lifetime
> arrangement; whether that arrangement is right for this app (and what you must wire yourself if you
> construct the client manually instead) is not visible from the signature. **MUST load
> `dotnet-client-initialization`** before registering.

> ⚠ Step 1 (auth) — where credentials may come from and when they must be set relative to client
> construction is a usage rule the options class does not show; getting it wrong fails every call with 401.
> **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

> ⚠ Steps 2–10 (every call) — the 24/15/8-nullable-parameter signatures mis-bind silently in positional
> calls, and several have no C# default. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–10 (models) — `MessageEnumStatus` & co. are `StringEnum<T>`, not C# enums: construction,
> comparison, and JSON round-trip follow rules a C# enum does not; and unmodeled JSON fields are dropped on
> deserialize. **MUST load `dotnet-models`** before reading `Status` or building enum values.

> ⚠ Step 11 (error boundary) — every operation here is Case B (`RawError`); the catch ladder, and what
> `ReadAsJson<T>` may itself do, differ from the typed-error case. **MUST load `dotnet-error-handling`**
> before writing any `try/catch`.

> ⚠ Step 11 (retries/timeouts) — whether a failed `CreateMessage` POST can be re-executed by the SDK's
> retry pipeline (a non-idempotent write sending twice), what `Timeout` actually bounds, and what the
> SDK's logging does with request data (the auth token must never appear in logs) are all governed by
> options you must set deliberately. **MUST load `dotnet-configuration-resilience`** before tuning.

> ⚠ Step 10 (reconciliation pagination) — `ListMessage` has no SDK pagination helper; how to walk
> `NextPageUri`/`pageToken` to cover the whole range without skipping or double-counting pages is a usage
> mechanic the signature does not show. **MUST load `dotnet-configuration-resilience`** before writing the loop.

> ⚠ Step 11 (tests) — the test seam for this SDK is specific (and it is not "mock the controllers");
> matching the repo's existing test framework matters. **MUST load `dotnet-testing`** before stubbing.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs Step 1 (client construction & DI lifetime).
- `dotnet-authentication` — governs Step 1 (credentials wiring).
- `dotnet-calling-endpoints` — governs Steps 2–10 (every operation call).
- `dotnet-models` — governs Steps 2–10 (records, `StringEnum<T>` enums, wire names).
- `dotnet-error-handling` — governs Step 11 (the catch ladder and error boundary).
- `dotnet-configuration-resilience` — governs Steps 10–11 (retries, timeouts, pagination, logging).
- `dotnet-testing` — governs Step 11 (faking the SDK seam).

Two hazards that shape the error boundary from day one, stated verbatim:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Assumptions**

1. Lookup **v2** (`FetchPhoneNumber3`) is the validation operation — it alone returns `Valid` +
   `ValidationErrors` + the canonical `PhoneNumber`; v1 (`LookupsV1PhoneNumberApi.FetchPhoneNumber2`,
   map: `operations/LookupsV1PhoneNumberApi.md`) has no validity field and was rejected for this use.
2. Order placement/cancel "shopper is told" sends use `From = Twilio:FromNumber`; the scheduled follow-up
   uses `Twilio:MessagingServiceSid` (scheduling is Messaging-Service-only per the SDK's own enum doc).
3. "A few days later" for the follow-up is an app-chosen `sendAt`; the provider's scheduling window is
   enforced app-side because the SDK expresses no constraint (see `UNVERIFIED` rows in OP-3).
4. Resend idempotency is enforced in the app's own store (the SDK auto-generates `Idempotency-Key` per call
   and gives the caller no way to set it — source-grounded, see OP-2).
5. Delivery state is refreshed lazily: `FetchMessage` per stored SID when my-orders/notifications endpoints
   are hit (no webhooks exist); reconciliation uses `ListMessage` only.
6. The PublicApi project's existing JWT auth, endpoint, and EF Core conventions are followed by the
   implementer; this plan covers only the Twilio-facing contract and layout.

**Blockers** — none.

**Drift notice (informational, not a blocker)** — the SDK repo's `main` branch has since been regenerated
under a different codegen (root namespace `Twilio`, client `TwilioClient`), but the only published NuGet
version (2.0.0) is built from commit `51fdf48`, which is exactly what this sheet and the bundled map
document. If a future `dotnet add package` ever resolves a newer package version, re-ground the sheet before
coding.
