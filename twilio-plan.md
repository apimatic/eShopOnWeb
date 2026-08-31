# Twilio .NET SDK integration plan — eShopOnWeb SMS order notifications (`src/PublicApi`, net8.0)

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Add NuGet package + register client & auth in DI (`Twilio:AccountSid`, `Twilio:AuthToken`, optional `Twilio:BaseUrl`, `Twilio:FromNumber` / `Twilio:MessagingServiceSid` config) | — (client construction) |
| 2 | Phone-number validation at registration; store canonical E.164 | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS on order events | `Api20100401Message.CreateMessage` |
| 4 | Scheduled follow-up SMS (provider-side) + cancel | `CreateMessage` (`scheduleType`/`sendAt`) · `Api20100401Message.UpdateMessage` (cancel) |
| 5 | Poll delivery outcome (no webhooks — app has no public URL) | `Api20100401Message.FetchMessage` |
| 6 | Redact message body (record survives) | `Api20100401Message.UpdateMessage` (redact) — **not** `DeleteMessage` |
| 7 | Reconciliation list by from-number + date range | `Api20100401Message.ListMessage` |
| 8 | Error boundary around all of the above | all six operations (all **Case B**) |

Install (into `src/PublicApi/PublicApi.csproj`): `dotnet add package AsadAli.TwilioSdk` — **version-less**, floats to latest; pulls runtime dependencies transitively. Version notes: this sheet is grounded in the SDK map pinned at source commit `51fdf48` ("Publish v2.0.0 SDK"). The SDK repo's `main` has since advanced to a regeneration whose surface renames the client/options types — if any name in this sheet fails to compile against the resolved package, trust the compiler, treat it as map/package drift, and re-ground from source before coding around it. Do not pin a version from memory.

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

**Every operation below is throw-only (no `…Result` variant exists anywhere in this SDK) and every one is Error Case B: it throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`.** Read failures via `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`. There are no typed `TryGet…` status accessors on any of these six operations. (sdk-map.md, *Error-handling model*)

### Step 2 — Phone validation: `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (map: `operations/LookupsV2PhoneNumber.md`)

- HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` on server node **Default4 (lookups)**.
- Signature: `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 15 params `fields … partnerSubId` are nullable with no C# default: **pass every one explicitly** (`null` to skip). For plain validation pass `fields: null, countryCode: <ISO 3166-1 alpha-2 or null>, ` and `null` for the rest.
- `phoneNumber` accepts E.164 or national format (default country +1); `countryCode` is used when national-format input is given. `fields` is a comma-separated list (`validation`, `caller_name`, …) — leave `null` for basic validation.
- Returns `TwilioSdk.Models.LookupResponse` (map: `records-4-Li-Me.md`). Fields the integration reads:
  - `Valid (valid): bool?` — **the validity verdict.** Treat anything other than `true` (including `null`) as not usable.
  - `PhoneNumber (phone_number): string?` — **canonical E.164 form; store this, not the caller's input.**
  - `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?`
  - `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` — reasons when invalid. Enum values (map: `enums.md`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.
- **Distinguishing "number invalid" from other failures:** invalid input is expected to come back as a 2xx `LookupResponse` with `Valid == false` (+ `ValidationErrors`); everything else surfaces as `SdkException<RawError>` where `ex.Error.StatusCode` carries the HTTP status (auth, rate-limit, etc.). `UNVERIFIED` (only live traffic can confirm): whether some invalid inputs instead surface as a non-2xx. Defensive directive: `Valid != true` ⇒ user-facing "invalid number"; `SdkException<RawError>` ⇒ provider failure — never show its body to the shopper, and do not conflate the two paths.
- V1 exists (`client.LookupsV1PhoneNumberApi.FetchPhoneNumber2`, returns `LookupsV1PhoneNumber`) but its response record has **no `Valid` field** (map: `records-4-Li-Me.md`) — V2 is the validation operation; do not use V1.

### Steps 3–7 — Messaging: `client.Api20100401Message` (map: `operations/Api20100401Message.md`; all five ops on server node **Default (api)**, form-url-encoded writes, JSON reads)

**CreateMessage** — `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (send + schedule):

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

- The 24 params `statusCallback … contentSid` are nullable with no default: **pass every one explicitly** (`null` to skip) — call with named arguments.
- Plain send: `accountSid`, `to` (E.164 — use the canonical form from step 2), `from`: configured from-number **or** `messagingServiceSid`: configured Messaging Service SID, `body`: text; `null` for everything else. Wire names: `To`, `From`, `MessagingServiceSid`, `Body`.
- **Scheduled send (step 4):** `messagingServiceSid` is **required** for scheduling — `MessageEnumScheduleType`'s doc: "For Messaging Services only" (map: `enums.md`). Set `scheduleType: MessageEnumScheduleType.Fixed` (only value; wire `fixed`), `sendAt:` the future `DateTimeOffset`, plus `messagingServiceSid`, `to`, `body`; `from: null`. (The enum doc's phrase "send_time parameter" refers to this SDK's `SendAt`/`sendAt` — the only scheduling-time parameter.) The provider holds the message; the app stores the returned SID and never re-sends itself.
- Returns `TwilioSdk.Models.ApiV2010AccountMessage` (map: `records-1-Ac-Ca.md`). Read: `Sid (sid): string?` — **provider message identifier (store it)**; `Status (status): MessageEnumStatus?` — **initial delivery status**; plus `To (to)`, `From (from)`, `Body (body)`, `DateCreated (date_created)`, `DateSent (date_sent): string?` (string, **not** `DateTimeOffset`), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `MessagingServiceSid (messaging_service_sid)`, `NumSegments (num_segments)`, `Price (price)`, `PriceUnit (price_unit)`, `Direction (direction): MessageEnumDirection?`, `DateUpdated (date_updated)`, `AccountSid (account_sid)`, `Uri (uri)`, `ApiVersion (api_version)`, `SubresourceUris (subresource_uris): object?`.

**FetchMessage** — `GET …/Messages/{Sid}.json` (poll status, step 5): `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage`. Poll `Status`; terminal failure shows up here (send itself already succeeded — see step 8).

**UpdateMessage** — `POST …/Messages/{Sid}.json` (cancel + redact, steps 4 & 6): `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` must both be passed explicitly. Wire: `Body`, `Status`. Doc: "used to redact Message `body` text and to cancel not-yet-sent messages".
- **Cancel scheduled:** `body: null, status: MessageEnumUpdateStatus.Canceled` (only value; wire `canceled`). Only valid while the message is still `scheduled`.
- **Redact body:** `body: "" (empty string), status: null`. The message record (sid, status, dates, outcome) survives; only the text is removed. `UNVERIFIED`: the exact post-redact representation of `Body` on later fetches (empty string vs null). Defensive directive: after redaction treat `Body` as null-or-empty; never assert a specific sentinel.
- Returns the updated `ApiV2010AccountMessage`.

**DeleteMessage** — `DELETE …/Messages/{Sid}.json`: `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void` (`Task`). **Full delete — the record does not survive. Not the redact mechanism;** included only so it is not chosen by mistake.

**ListMessage** — `GET …/Messages.json` (reconciliation, step 7):

```
ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent,
  DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize,
  int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- The 8 params `to … pageToken` must be passed explicitly. Server-side filters (wire ← C#): `To` ← `to`; **`From` ← `from`** — set to the configured sending number (E.164) so the account's other traffic is excluded server-side; `DateSent` ← `dateSent` (exact day); **`DateSent<` ← `dateSentQuery`** (sent before — range end); **`DateSent>` ← `dateSentQueryQuery`** (sent after — range start). The awkward generated names are literal: `dateSentQuery` = "before", `dateSentQueryQuery` = "after". Values serialize ISO-8601; the API documents GMT `YYYY-MM-DD` forms. `PageSize` ← `pageSize` (`long?`, default 50, max 1000), `Page` ← `page`, `PageToken` ← `pageToken`.
- Returns `TwilioSdk.Models.ListMessageResponse` (map: `records-4-Li-Me.md`): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (each item carries `Sid`, `To`, `From`, `Status`, `DateSent`, `Body` as above) plus pagination fields `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri)`, `FirstPageUri (first_page_uri)`, `Page (page): int?`, `PageSize (page_size): int?`, `Start (start)`, `End (end)`, `Uri (uri)`. **No built-in pager** (map row: "Pagination: none") — page manually via `NextPageUri`/`pageToken` (see trap note, step 7).

### Enum values needed (map: `models/enums.md`; all are `StringEnum<T>` in `TwilioSdk.Models.Enums` — static members, not C# enums)

| Enum | Members (wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` |
| `MessageEnumTrafficType` | `Free (free)` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

Delivery-outcome interpretation for polling: success = `Delivered`; carrier-refused/undeliverable = `Failed` or `Undelivered` (with `ErrorCode`/`ErrorMessage` on the record); in-flight = `Queued`, `Sending`, `Sent`, `Accepted`, `Scheduled`.

### Client construction, auth, servers (sdk-map.md *Getting a client* / *Servers & auth*; `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Server.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`)

- Client: `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` — the only constructor. DI alternative: `services.AddTwilioSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`).
- `TwilioSdkClientOptions` (namespace `TwilioSdk`) properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `AccountSidAuthToken: BasicAuthCredentials?`.
- Auth: `AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <Account SID or API-key SID>, Password = <Auth Token or API-key secret> }` — `Username`/`Password` are `required string` init-only. (The SDK's own doc: API key + secret preferred; account SID + auth token for local testing.)
- Environment: `TwilioSdk.Servers.ServerEnvironment` — single member `ServerEnvironment.Production`; default `ServerEnvironment.Default()`.
- **Base-URL override, scoped to messaging:** server selection is per-operation via named server nodes. All five messaging operations resolve through node **Default** (`options.Server.Default`), Lookup through **Default4** (`options.Server.Default4`). Each node type (`TwilioSdk.Servers.DefaultOptions` / `Default4Options`) exposes `.Production.BaseUrl` (defaults `https://api.twilio.com` and `https://lookups.twilio.com` respectively). So `Twilio:BaseUrl` maps verbatim to:
  `options.Server.Default.Production.BaseUrl = configuration["Twilio:BaseUrl"]` — this re-targets every messaging-API call and leaves Lookup on `lookups.twilio.com`. Precise scope: node Default serves **all** `Api20100401*` controllers, but messaging is this integration's only consumer of that node, so the effect here is messaging-only. Only set the property when the config value is present; otherwise leave the default.
- `TwilioSdk.Core.RequestOptions` (sealed record): `LogLevel? LogLevel` — per-request log-level override; pass `null` everywhere here.
- Async/cancellation: every operation returns `Task`/`Task<T>`; the last parameter is `CancellationToken ct = default` — flow the request's `HttpContext.RequestAborted` or a timeout token into `ct:`.
- `using` directives this integration needs: `TwilioSdk`, `TwilioSdk.Models`, `TwilioSdk.Models.Enums`, `TwilioSdk.Core` (RequestOptions), `TwilioSdk.Core.Authentication.Basic`, `TwilioSdk.Core.Exceptions` (SdkException), `TwilioSdk.Core.ErrorResponse` (RawError), `TwilioSdk.Servers` (only if naming `ServerEnvironment`). Controllers are reached via client properties (`client.Api20100401Message`, `client.LookupsV2PhoneNumber`); `TwilioSdk.Api` is not needed unless naming controller types.

---

## 3. Trap notes

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind `TwilioSdkClient` has lifetime requirements the constructor signature doesn't state; building it per request exhausts sockets. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient(...)` or the DI registration.
- ⚠ Step 1 (auth) — where credentials may come from (never hardcoded) and when in the client lifecycle they must be set is not visible from the property type. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.
- ⚠ Steps 3, 7 (calling operations) — `CreateMessage` has 24 and `ListMessage` 8 nullable parameters with **no C# defaults**; a positional call mis-binds silently. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–7 (models) — the enums above are `StringEnum<T>`, not C# enums, and unmodeled JSON fields are dropped on deserialize; assumptions carried from C# enum semantics break comparisons and pattern matching. **MUST load `dotnet-models`** before constructing or comparing enum values.
- ⚠ Step 8 (error boundary) — all six operations are Case B (`SdkException<RawError>`), so there are no typed status accessors; and `System.Text.Json.JsonException` reaches the boundary from two directions needing opposite handling (see REQUIRED READING). **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Steps 1, 3, 7 (resilience) — the SDK's retry options govern more verbs than their names suggest for a non-idempotent write like `CreateMessage` (whether a failed send can be re-executed), `Timeout` does not bound what its name suggests, there is no built-in logging hook, and list pagination is manual. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or paging `ListMessage`.
- ⚠ Tests — the SDK's test seam is a specific constructor argument, not an interface over the controllers. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs step 1 (client construction & DI lifetime).
- `dotnet-authentication` — governs step 1 (credentials wiring).
- `dotnet-calling-endpoints` — governs steps 2–7 (must-pass-explicitly params, named arguments).
- `dotnet-models` — governs steps 2–7 (StringEnum semantics, record immutability, dropped fields).
- `dotnet-error-handling` — governs step 8 (the exception boundary). An integration always writes an error boundary, so this skill always appears here — including these two hazards, verbatim:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — governs steps 1, 3, 7 (retries on writes, timeout semantics, manual pagination, logging).
- `dotnet-testing` — governs tests for the integration layer.

## 5. Assumptions & Blockers

**Assumptions**
- Lookup **V2** chosen over V1 because the V1 response record carries no `Valid` field (map-visible fact); V2's `LookupResponse.Valid` is the verdict and `LookupResponse.PhoneNumber` is the canonical E.164 form to store.
- Scheduling requires a Messaging Service SID (per `MessageEnumScheduleType`'s map doc, "For Messaging Services only"); the plan assumes the app configures one (`Twilio:MessagingServiceSid`). Plain immediate sends may use either `from` or `messagingServiceSid`.
- The same Account SID used for auth is passed as the `{AccountSid}` template parameter on every messaging call.
- Per the brief, US destinations are accepted at send time and refused by the carrier later: `CreateMessage` succeeding is **not** delivery; the integration polls `FetchMessage` and treats `Failed`/`Undelivered` (with `ErrorCode`/`ErrorMessage`) as the delivery failure path.
- `UNVERIFIED` (only live traffic can confirm; defensive directives given inline above): whether Lookup V2 ever returns non-2xx for an invalid number instead of 2xx + `valid: false`; the exact post-redact `Body` representation; the specific HTTP status codes returned for cancel/redact/delete in wrong states (all surface as `SdkException<RawError>` — branch on `StatusCode`, never on message text).

**Blockers** — none.
