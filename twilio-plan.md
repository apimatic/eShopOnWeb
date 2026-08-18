# Twilio .NET SDK — Integration Plan & Contract Sheet

**Feature:** SMS order-notification for eShopOnWeb (ASP.NET Core / .NET 8).
**SDK:** `AsadAli.TwilioSdk` (APIMatic-generated), root namespace `TwilioSdk`, client `TwilioSdkClient`.
Install version-less: `dotnet add package AsadAli.TwilioSdk`. Source commit stamp `51fdf48`.

All facts below are grounded in the bundled SDK map (pages cited per row); the server/base-URL
shape is grounded in the SDK source files named in that section.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `TwilioSdkClient` in the service container, options bound from
   config (Account SID / Auth Token, `Twilio:BaseUrl`). Ops used: none (wiring).
2. **Send SMS immediately** — `Api20100401Message.CreateMessage` (sender = `from` OR
   `messagingServiceSid`).
3. **Schedule SMS** — `Api20100401Message.CreateMessage` with `scheduleType` + `sendAt` +
   `messagingServiceSid`.
4. **Cancel scheduled SMS** — `Api20100401Message.UpdateMessage` with `status = Canceled`.
5. **Fetch one message** — `Api20100401Message.FetchMessage` (read `Status`, `ErrorCode`,
   `ErrorMessage`).
6. **List / reconcile** — `Api20100401Message.ListMessage` (server-side `From` + `DateSent`
   range, manual paging).
7. **Redact / delete provider content** — `UpdateMessage` (redact body) and/or `DeleteMessage`
   (remove record).
8. **Phone-number lookup / validation** — `LookupsV2PhoneNumber.FetchPhoneNumber3` (separate
   host).
9. **Persistence** — store `Sid` (provider identifier) + `Status` + `ErrorCode`/`ErrorMessage`
   (current delivery outcome) per message.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C#
> identifier. The cancellation-token parameter really is named `ct`: in named arguments write
> `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map/source gives it** — take
> each one from that type's own row, never from where a neighbouring type sits. Enums, unions,
> auth, server and client-config types live in different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build
> breaks.

### 2a. Namespaces (`using` directives) — one per type kind

| Type | Namespace | Source of fact |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` | sdk-map *Getting a client* + `ServerOptions.cs` |
| Controllers (`Api20100401Message`, `LookupsV2PhoneNumber`, `LookupsV1PhoneNumberApi`) | `TwilioSdk.Api` (accessed as `client.<Name>` properties) | sdk-map *Namespaces* table |
| Records (`ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`, `LookupsV1PhoneNumber`) | `TwilioSdk.Models` | sdk-map *Namespaces* table |
| Enums (`MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumContentRetention`, `MessageEnumAddressRetention`, `MessageEnumDirection`) | `TwilioSdk.Models.Enums` | sdk-map *Namespaces* table |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` | source `Core/Exceptions/SdkException.cs` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | source `Core/ErrorResponse/RawError.cs` |
| `RequestOptions` | `TwilioSdk.Core` | source `Core/RequestOptions.cs` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | source `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` | source `Core/Configuration/RetryOptions.cs` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` | sdk-map *Getting a client* + `Servers/*.cs` |

### 2b. Operations — `client.Api20100401Message` (map: `operations/Api20100401Message.md`)

Every operation on this controller is **Case B** (`SdkException<RawError>` — no typed accessors)
and **throw-only** (no `…Result` no-throw variant). Error accessors on `ex.Error`:
`StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`,
`ReadAsBytes(): ReadOnlyMemory<byte>`.

| Op | Signature (params in order — all nullable-no-default params **must be passed explicitly**, pass `null` to skip) | Returns |
|---|---|---|
| **CreateMessage** | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` |
| **FetchMessage** | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` |
| **UpdateMessage** | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` |
| **DeleteMessage** | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (`Task`) |
| **ListMessage** | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ListMessageResponse` |

All are `async` — call as `await client.Api20100401Message.<Op>(...)`; pass your
`CancellationToken` as `ct:`.

**Per-capability mapping (from the map, resolved inline):**

- **(1) Send now** — pass `to` (positional, required, E.164 destination) + `body` + exactly one
  sender: `from:` (the `Twilio:FromNumber`) **or** `messagingServiceSid:` (the
  `Twilio:MessagingServiceSid`). Both are separate nullable params; the SDK does not enforce
  "exactly one" — your app must. Wire names: `To`←`to`, `Body`←`body`, `From`←`from`,
  `MessagingServiceSid`←`messagingServiceSid`.
- **(2) Schedule** — set `scheduleType: TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed`
  and `sendAt: <DateTimeOffset>` (wire `ScheduleType`, `SendAt`). The `MessageEnumScheduleType`
  value list has **only** `Fixed (fixed)` (see enums below); its source doc states scheduling is
  *"For Messaging Services only"* — so a scheduled send must supply `messagingServiceSid:`, not
  `from:`. The SDK types do not enforce this pairing; it is a **provider-side** requirement
  (a scheduled send without a Messaging Service SID is rejected by Twilio at the wire — see
  Assumptions & Blockers, `UNVERIFIED` exact rejection).
- **(3) Cancel scheduled** — `UpdateMessage(accountSid, sid, body: null, status:
  TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled)`. `MessageEnumUpdateStatus` has
  **only** `Canceled (canceled)`. Wire `Status`←`status`. If the message has already left
  `scheduled`/`accepted` (already sent), Twilio rejects the update: the call throws
  `SdkException<RawError>` — read `ex.Error.StatusCode` and `ex.Error.ReadAsString()`. The exact
  HTTP status/code for "already sent" is `UNVERIFIED` (live-wire only); code defensively — treat
  any non-2xx on cancel as "could not cancel, re-fetch current status" rather than keying on a
  specific code.
- **(4) Fetch** — `FetchMessage(accountSid, sid)`; read `Status`, `ErrorCode`, `ErrorMessage`
  off the returned `ApiV2010AccountMessage` (fields below).
- **(5) List / reconcile** — server-side filters are real query params (**not** client-side):
  `from:` → wire `From` (messages sent FROM that number). Date range uses **two** params whose
  C# names are misleading — verify against the wire name, not the identifier:
  - `dateSentQuery:` → wire **`DateSent<`** (messages sent **before / less-than**),
  - `dateSentQueryQuery:` → wire **`DateSent>`** (messages sent **after / greater-than**),
  - `dateSent:` → wire `DateSent` (exact-day match — usually leave `null` for a range).
  To scan a whole range: pass `from:`, `dateSentQueryQuery:` (start), `dateSentQuery:` (end),
  `pageSize:`, and page with `page:`/`pageToken:`. **There is no auto-pager** (map: "Pagination:
  none (only `page`, no `perPage`)") — loop manually: read `NextPageUri` on the response; stop
  when it is `null` (fields below).
- **(6) Redact vs delete** — two distinct contracts, choose per design intent:
  - **Redact body, keep record + status:** `UpdateMessage(accountSid, sid, body: "", status:
    null)` — sets the message `Body` to empty at the provider (wire `Body`←`body`). The Message
    resource, its `Sid`, and `Status` survive; the text is no longer retrievable. (Map note on
    `UpdateMessage`: *"used to redact Message `body` text and to cancel not-yet-sent messages."*)
    Whether Twilio accepts empty vs whitespace body for redaction and whether it nulls vs blanks
    the field is `UNVERIFIED` (live-wire) — persist your own "redacted" flag rather than relying
    on reading the blanked field back.
  - **Delete the whole record:** `DeleteMessage(accountSid, sid)` — removes the Message resource
    from the account (returns `void`). After this the record itself is gone; do **not** use this
    if you must keep proof-of-send + status. (Also Case B: a delete of an undeletable/in-flight
    message throws `SdkException<RawError>`.)
  - **Design consequence:** to satisfy "text unretrievable but send-record + status survive," use
    **UpdateMessage-redact**, not DeleteMessage.

### 2c. Operation — phone-number lookup — `client.LookupsV2PhoneNumber` (map: `operations/LookupsV2PhoneNumber.md`)

**Separate host / server node** — this controller resolves against server node **Default4
(lookups)** (`https://lookups.twilio.com`), NOT the messaging node **Default (api)**
(`https://api.twilio.com`). See §2e — a `Twilio:BaseUrl` override applied to the messaging node
does **not** touch lookups.

| Op | Signature | Returns |
|---|---|---|
| **FetchPhoneNumber3** | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `LookupResponse` |

- Case **B** (`SdkException<RawError>`), throw-only, same accessors as above.
- `phoneNumber` is the path segment (the number to validate; pass raw/E.164). `fields:` selects
  optional data packages — for plain validity + canonical form, pass `null` for all optional
  params.
- **Response `LookupResponse` fields** (map: `records-4-Li-Me.md`): `Valid (valid): bool?`
  (validity flag), `PhoneNumber (phone_number): string?` (canonical **E.164** form to persist),
  `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`,
  `CallingCountryCode (calling_country_code): string?`, `ValidationErrors (validation_errors):
  IReadOnlyList<ValidationError>?`, plus optional data-package objects
  (`CallerName/LineTypeIntelligence/…`).
- **(7a) reject unusable destinations at registration:** an invalid number does **not** throw —
  the SDK returns `200` with `Valid == false` and populated `ValidationErrors`. Reject on
  `Valid != true`. A throw here (`SdkException<RawError>`) means a transport/auth/host problem,
  not "invalid number." (That `Valid==false` rather than an exception is the documented shape;
  confirm against live traffic that a malformed number returns `Valid=false` vs a 404 — label
  `UNVERIFIED`; code both: catch the exception AND check `Valid`.)
- **(7b) canonical E.164:** persist `LookupResponse.PhoneNumber`.
- **V1 alternative** exists (`client.LookupsV1PhoneNumberApi.FetchPhoneNumber2`, returns
  `LookupsV1PhoneNumber` with `PhoneNumber`/`NationalFormat` but **no `Valid` flag**) — prefer
  **V2** for the validity flag. V1 also lives on the lookups host.

### 2d. Response models — fields read by the integration

**`ApiV2010AccountMessage`** (map: `records-1-Ac-Ca.md`; returned by Create/Fetch/Update):
`Sid (sid): string?` (provider identifier — persist), `Status (status): MessageEnumStatus?`
(delivery outcome — persist), `ErrorCode (error_code): int?`, `ErrorMessage (error_message):
string?`, `Body (body): string?`, `From (from): string?`, `To (to): string?`,
`MessagingServiceSid (messaging_service_sid): string?`, `Direction (direction):
MessageEnumDirection?`, `NumSegments (num_segments): string?`, `NumMedia (num_media): string?`,
`Price (price): string?`, `PriceUnit (price_unit): string?`, `DateSent (date_sent): string?`,
`DateCreated (date_created): string?`, `DateUpdated (date_updated): string?`, `Uri (uri):
string?`, `AccountSid`, `ApiVersion`, `SubresourceUris (subresource_uris): object?`.
Note: dates are `string?` (not `DateTimeOffset`); `ErrorCode` is `int?`. All fields nullable.

**`ListMessageResponse`** (map: `records-4-Li-Me.md`; returned by ListMessage):
`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (**the payload — read one level
down into `.Messages`**), `NextPageUri (next_page_uri): string?` (paging cursor: loop until
`null`), `PreviousPageUri`, `FirstPageUri`, `Uri`, `Page (page): int?`, `PageSize (page_size):
int?`, `Start (start): int?`, `End (end): int?`.

### 2e. Client construction, auth, and base-URL / server override

**Construction** (map: *Getting a client*): `new TwilioSdkClient(HttpClient httpClient,
TwilioSdkClientOptions options)`. DI: `services.AddTwilioSdkClient(o => { ... })`
(`ServiceCollectionExtensions.cs`). Every API group is a property on the client
(`client.Api20100401Message`, `client.LookupsV2PhoneNumber`).

**Auth** (map: *Servers & auth*): set `options.AccountSidAuthToken` to a
`TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` — HTTP Basic. Per the source XML doc:
username = API key SID (or Account SID for local testing), password = API key secret (or Auth
Token). The user's "Account SID + Auth Token" maps to username = Account SID, password = Auth
Token. Load `dotnet-authentication` for the exact `BasicAuthCredentials` constructor/property
shape and where to set it.

**Base-URL / server override (grounded in source `ServerOptions.cs`, `Servers/DefaultOptions.cs`,
`Servers/Default4Options.cs`):**
- `options.Server` is a `TwilioSdk.ServerOptions` exposing **15 independent server-node
  properties** `Default … Default14`, each its own options object with a nested
  `Production.BaseUrl` string used **verbatim** as that node's base URL.
- **Messaging** (`Api20100401Message`, server node **Default**): default base
  `https://api.twilio.com`. Override with
  `options.Server.Default.Production.BaseUrl = "<Twilio:BaseUrl>";`
  This applies to **every** messaging-API call, since all `Api20100401Message` ops resolve
  through node `Default`.
- **Lookups** (`LookupsV2PhoneNumber`, server node **Default4**): default base
  `https://lookups.twilio.com`. It is a **different node** — the `Default` override above does
  **not** affect it. To point lookups elsewhere you would set
  `options.Server.Default4.Production.BaseUrl`. This confirms the user's note: `Twilio:BaseUrl`
  set on the messaging node leaves lookup on its own host.
- `options.Environment` is a `TwilioSdk.Servers.ServerEnvironment` with a single member
  `ServerEnvironment.Production`.

### 2f. Enums (map: `models/enums.md`) — `StringEnum<T>`, not C# enums

Build with `Type.FromValue("wire")` or the static member (`MessageEnumStatus.Delivered`) — never
`SomeEnum.some_member`.

| Enum | Members (`CSharpName (wire)`) |
|---|---|
| `MessageEnumStatus` (read on message `Status`) | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` (send `scheduleType`) | `Fixed (fixed)` — only value |
| `MessageEnumUpdateStatus` (cancel `status`) | `Canceled (canceled)` — only value |
| `MessageEnumDirection` (read `Direction`) | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumContentRetention` (send `contentRetention`) | `Retain (retain)`, `Discard (discard)` |
| `MessageEnumAddressRetention` (send `addressRetention`) | `Retain (retain)`, `Obfuscate (obfuscate)` |

Note: the message-resource status enum is `MessageEnumStatus` (used on `ApiV2010AccountMessage.
Status`). A separate `SmsMessageEnumStatus` exists but is not the type on this model — do not
mix them.

---

## 3. Trap notes (attached to the step where each bites — load the named skill; do not code from the one-liner)

⚠ **Step 1 (client & DI)** — the `HttpClient`/handler pipeline given to `TwilioSdkClient` has
lifetime rules the constructor signature does not reveal (long-lived/reused vs per-request), and
those rules decide whether you leak sockets or reuse a stale pipeline. **MUST load
`dotnet-client-initialization`** before writing `new TwilioSdkClient(...)` or
`AddTwilioSdkClient(...)`.

⚠ **Step 1/auth (auth)** — how and *when* `BasicAuthCredentials` must be set relative to client
construction, and API-key-vs-Account-SID choice, are not shown by the property type. **MUST load
`dotnet-authentication`** before wiring credentials.

⚠ **Steps 2–8 (every call)** — these operations have many optional params with **no C# default**
(must-pass-`null`); a positional call mis-binds silently, and named-argument discipline is
required. **MUST load `dotnet-calling-endpoints`** before the first `client.<Group>.<Op>(...)`
call.

⚠ **Steps 2–5, 8 (models/enums)** — `MessageEnumStatus`/`MessageEnumScheduleType`/etc. are
`StringEnum<T>`, not C# enums, and JSON fields the SDK does not model are dropped on deserialize
— which affects what you can actually read back off a message. **MUST load `dotnet-models`**
before constructing request enums or mapping responses onto domain types.

⚠ **Step 1 + Step 2 (resilience / idempotency interaction)** — the retry/timeout options do
**not** bound a whole call, are not the `HttpClient` timeout, and the retry trigger for
**transport failures** behaves differently from the status-code trigger across HTTP verbs —
which bears directly on whether a `CreateMessage` (POST) can be sent more than once. What
`Timeout` bounds and how base-URL/server selection interacts with the pipeline also live here.
**MUST load `dotnet-configuration-resilience`** before registering or tuning the client (this is
the same skill that governs your `Twilio:BaseUrl` wiring in §2e).

⚠ **Steps 2–8 (error boundary)** — every op here is Case B (`SdkException<RawError>`, no typed
accessors) and throw-only (no `…Result`); reading status/body safely and building a catch ladder
that does not silently swallow the wrong exception type is non-obvious. **MUST load
`dotnet-error-handling`** before writing any try/catch (see the two mandatory hazard rows in
Required Reading).

⚠ **Testing** — the SDK's test seam is the injected `HttpClient`, not the client wrapper, and
that choice determines whether your tests assert real behaviour. **MUST load `dotnet-testing`**
before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately does not carry their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials`, where/when to set credentials |
| `dotnet-calling-endpoints` | Steps 2–8 — named-argument calling, must-pass-null optionals, async/`ct` |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, building request enums, dropped-field pitfalls |
| `dotnet-configuration-resilience` | Step 1 + Step 2 — retries/timeouts, base-URL/server override, POST re-send hazard (idempotency) |
| `dotnet-error-handling` | Steps 2–8 — Case-B error reading, catch-ladder correctness, the JsonException rows below |
| `dotnet-testing` | Tests — HttpClient seam |

**Mandatory error-boundary hazards — `System.Text.Json.JsonException` reaches the boundary from
two directions and they need opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets
  it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated error shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps
  every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller
  that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- "Sender is either a From number or a Messaging Service SID" — the SDK models both as separate
  optional params and does **not** enforce mutual exclusivity; the app selects exactly one from
  config (`Twilio:FromNumber` / `Twilio:MessagingServiceSid`).
- `Twilio:BaseUrl` is intended for the **messaging** host only (server node `Default`); lookups
  keep their own host (`Default4`). Confirmed against source — matches the user's stated
  expectation.
- Persisted "provider identifier" = `ApiV2010AccountMessage.Sid`; "current delivery outcome" =
  `Status` (+ `ErrorCode`/`ErrorMessage`).

**Capability findings (not gaps — stated per the user's request)**
- **(8) Idempotency key on send — NOT SUPPORTED by the SDK.** `CreateMessage` exposes **no**
  idempotency-key parameter and no modeled idempotency header (see signature §2b). `RequestOptions`
  is the only per-call extension point; there is no first-class idempotency contract. **Handle
  idempotency at the app layer** (dedupe on the operator-supplied key before calling
  `CreateMessage`). Note the resilience trap (§3): a POST transport-failure retry can re-send, so
  app-layer dedupe must cover retries too.
- **(6) Redact vs delete — BOTH supported, different contracts.** `UpdateMessage` with empty
  `body` redacts content while keeping the record + `Status`; `DeleteMessage` removes the whole
  Message resource. Use redact to keep proof-of-send.
- **(7) Lookup/validation — SUPPORTED** via `LookupsV2PhoneNumber.FetchPhoneNumber3` on a
  **separate host** (`lookups.twilio.com`, server node `Default4`), returning `Valid` +
  canonical `PhoneNumber` (E.164).

**`UNVERIFIED` (only live traffic can confirm — code defensively, do not key logic on a specific
value):**
- Exact HTTP status/error code Twilio returns when cancelling an already-sent message
  (capability 3) — treat any non-2xx on cancel as "could not cancel."
- Whether an invalid number in lookup (capability 7) returns `200` + `Valid=false` vs an HTTP
  error — code both paths (catch `SdkException<RawError>` AND check `Valid != true`).
- Exact provider behaviour of the redact-by-empty-body call (capability 6) — persist your own
  "redacted" flag rather than reading the blanked field back.
- Whether Twilio rejects a scheduled send lacking a Messaging Service SID at the wire
  (capability 2) — the enum doc says schedule is Messaging-Services-only; always pass
  `messagingServiceSid` for scheduled sends and handle a non-2xx defensively.

**Blockers:** none — all required SDK capabilities are exposed; the only open items are the
live-wire behaviours above, all converted to defensive-coding directives.

---

*Every row cites its map page or the named SDK source file. Types are fully-qualified per §2a.*
