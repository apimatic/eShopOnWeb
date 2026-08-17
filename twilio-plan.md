# Twilio .NET SDK integration — SMS order notifications (eShopOnWeb PublicApi)

Package: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`).
Root namespace `TwilioSdk`; client `TwilioSdkClient`; options `TwilioSdkClientOptions`.
SDK map source commit stamp: `51fdf48`. Every row below cites its map page.

This sheet has **no open lookups** — every signature, wire name, envelope field, enum value and
error accessor for all seven capabilities is resolved inline. Two items that only live Twilio
traffic can confirm are labelled **UNVERIFIED** with a defensive-coding directive; they are *not*
open lookups (the SDK contract itself is fully pinned).

---

## 1. Scope & sequence

1. **Client & DI registration** — register one long-lived `TwilioSdkClient` (via `AddTwilioSdkClient`)
   with Basic auth + the messaging base-URL override. Uses: none (setup).
2. **Validate + canonicalize a phone number at registration** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`.
3. **Send an SMS immediately** (order placed / dispatched / cancelled) — `client.Api20100401Message.CreateMessage`.
4. **Schedule a message for later, queued at Twilio** — `client.Api20100401Message.CreateMessage` with `scheduleType` + `sendAt`.
5. **Cancel a scheduled message** — `client.Api20100401Message.UpdateMessage` with `status = Canceled`.
6. **Fetch a single message's delivery status** — `client.Api20100401Message.FetchMessage`.
7. **Redact a message body at Twilio** — `client.Api20100401Message.UpdateMessage` with `body = ""`.
8. **Reconciliation: list Twilio's own messages by From + date range** — `client.Api20100401Message.ListMessage`.
9. **Error boundary** around every call (all Case B — see CONTRACT SHEET + REQUIRED READING).

---

## 2. Configuration facts — base URL, servers & auth

### Base-URL override is PER-SERVER-NODE, not global (this is the answer to the config question)

The SDK does **not** have a single global base address. `TwilioSdkClientOptions.Server` is a
`ServerOptions` (source `ServerOptions.cs`) holding **one property per server node**, each with its
own `Production.BaseUrl`. The two nodes this integration touches:

| Node property | Governs | Default BaseUrl | Set from |
|---|---|---|---|
| `options.Server.Default.Production.BaseUrl` | **All `Api20100401Message` calls** (send/read/reconcile/update/delete) — every message op's HTTP line is tagged `(Default (api))` | `https://api.twilio.com` | `Twilio:BaseUrl` when set (verbatim), else leave default |
| `options.Server.Default4.Production.BaseUrl` | **Lookup V2** (`FetchPhoneNumber3`) — its HTTP line is tagged `(Default4 (lookups))` | `https://lookups.twilio.com` | never touched by `Twilio:BaseUrl` |

Cited: `sdk-map.md` *Servers & auth* + `ServerOptions.cs` / `DefaultOptions.cs` / `Default4Options.cs`
(source, on a real map gap — the map named `options.Server`/`Servers/` but not the node structure).

**Directive for `Twilio:BaseUrl`:** when the key is present, set
`options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl value>` **verbatim** (do not append/trim).
Do **NOT** set `Default4` from it. Because the base URL is a per-node property, Lookup and messaging
**coexist in one client automatically**: messaging follows the (possibly overridden) `Default` node,
Lookup always follows `Default4` (`lookups.twilio.com`) unless you separately override it. There is
exactly **one** `ServerEnvironment` member — `ServerEnvironment.Production` (`Servers/ServerEnvironment.cs`);
the override lives on `.Production.BaseUrl` of each node. When `Twilio:BaseUrl` is absent, set nothing
and both nodes keep their defaults.

### Auth (both hosts share one credential)

`TwilioSdkClientOptions.AccountSidAuthToken` is `BasicAuthCredentials?`
(`TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials`, source confirmed). Shape:

```
BasicAuthCredentials { required string Username; required string Password; }
```

Map `Twilio:AccountSid` → `Username`, `Twilio:AuthToken` → `Password` (Twilio Basic auth: SID as
username, auth token as password; an API key SID/secret pair is the production-preferred alternative).
The same credential authenticates both the messaging (`Default`) and lookups (`Default4`) hosts — one
`TwilioSdkClient` covers both. Cited: `sdk-map.md` *Servers & auth*.

### Client construction

- Constructor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
- DI: `services.AddTwilioSdkClient(o => { /* set o.AccountSidAuthToken, o.Server... */ });`
  (`ServiceCollectionExtensions.cs`).
- Every API group is a property on the client: `client.Api20100401Message`, `client.LookupsV2PhoneNumber`.
Cited: `sdk-map.md` *Getting a client*.

---

## 3. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. A members table names the
> namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒
> `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server
> and client-config types are spread across different child namespaces, and two types configured
> side by side in the same options object routinely live in different ones. Dropping a type to the
> root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Namespaces to import
- `TwilioSdk` — client, options, `ServerOptions`.
- `TwilioSdk.Servers` — `ServerEnvironment`.
- `TwilioSdk.Core.Authentication.Basic` — `BasicAuthCredentials`.
- `TwilioSdk.Api` — the operation controllers.
- `TwilioSdk.Models` — records (`ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`).
- `TwilioSdk.Models.Enums` — `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumStatus`, `MessageEnumDirection`, `ValidationError`.
- `TwilioSdk.Core.Exceptions` — `SdkException<T>`.
- `TwilioSdk.Core.ErrorResponse` — `RawError`.

### 3.1 Operations table

All message ops are on **`client.Api20100401Message`** (page `operations/Api20100401Message.md`).
All are async → the listed return type is wrapped in `Task<>`; `await` them and pass `ct:`.
**Every operation below is error Case B** → `SdkException<RawError>`, accessors
`StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`.
No typed `{Operation}Error`, no `TryGet…` accessors, no no-throw `…Result` variant anywhere.

| # | Op | Method signature (params in order; all nullable-no-default params MUST be passed explicitly, `null` to skip) | Response envelope + fields read | Notes |
|---|---|---|---|---|
| 2 | **Validate/canonicalize** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (`operations/LookupsV2PhoneNumber.md`) | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass the number as `phoneNumber`, all 15 middle params `null` for a basic validate | Returns **`LookupResponse`** (NOT wrapped — read fields directly). Read `Valid (valid): bool?` for validity; read `PhoneNumber (phone_number): string?` for the **E.164 canonical** number to store; `NationalFormat (national_format): string?` is national form; `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` lists reasons. | Host = `Default4` (lookups), NOT `Twilio:BaseUrl`. See UNVERIFIED-A. |
| 3 | **Send now** — `CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 middle params nullable-no-default, pass `null` to skip. For an immediate SMS: set `to`, `body`, and EITHER `from:` (From number) OR `messagingServiceSid:` (Messaging Service). SDK supports **both** sender modes (both are `string?` params). `accountSid` = `Twilio:AccountSid`. | Returns **`ApiV2010AccountMessage`** (NOT wrapped). Read `Sid (sid): string?` (message SID) and `Status (status): MessageEnumStatus?`. Also available: `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `To/From/Body/DateSent`. | — |
| 4 | **Schedule later** — `CreateMessage` (same signature as #3) | Set `scheduleType: MessageEnumScheduleType.Fixed` **and** `sendAt: <DateTimeOffset>` **and** `messagingServiceSid:` (see constraint below). Leave `from:` null. Wire names: `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `MessagingServiceSid` ← `messagingServiceSid`. | Same `ApiV2010AccountMessage`. Expect `Status` = `Scheduled`. | `MessageEnumScheduleType` has ONE value `Fixed (fixed)`. Messaging-Service requirement + SendAt bounds = UNVERIFIED-B. |
| 5 | **Cancel scheduled** — `UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — set `sid:` = scheduled msg SID, `body: null`, `status: MessageEnumUpdateStatus.Canceled`. Wire: `Status` ← `status`. | Returns `ApiV2010AccountMessage`; `Status` should read `Canceled`. | Already-sent → provider error, UNVERIFIED-C. |
| 6 | **Fetch status** — `FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Returns `ApiV2010AccountMessage`; read `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateSent (date_sent): string?`. | — |
| 7 | **Redact body** — `UpdateMessage` (same signature as #5) | Set `sid:` = SID, `body: ""` (empty string → clears the stored text), `status: null`. Wire: `Body` ← `body`. | Returns `ApiV2010AccountMessage` — the RECORD SURVIVES: SID, Status, To/From/DateSent all remain; only `Body` is emptied. This is an **UPDATE**, not `DeleteMessage`. (`DeleteMessage` exists — `DELETE …/Messages/{Sid}.json`, returns `void` — but it removes the whole record; do NOT use it for redaction.) | Body-survives-as-record confirmed by op note "used to redact Message `body` text"; UNVERIFIED-D on whether the emptied body is truly unrecoverable at Twilio. |
| 8 | **Reconcile list** — `ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — filter server-side: set `from:` = the From number, `dateSentQueryQuery:` = **DateSent>= (lower bound)**, `dateSentQuery:` = **DateSent<= (upper bound)**. Pass `to: null`, `dateSent: null`. Page with `pageSize:` + `pageToken:`/`page:`. | Returns **`ListMessageResponse`** — **the payload is wrapped**: read the `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` field (one level down), NOT the response object itself. Paging fields: `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `Start`, `End`, `FirstPageUri`, `PreviousPageUri`, `Uri`. | **Wire-name trap, verified from source:** `From` ← `from`, `DateSent` ← `dateSent`, **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**. So the *upper* bound (`<=`) is the param named `dateSentQuery` and the *lower* bound (`>=`) is `dateSentQueryQuery` — the names are counter-intuitive; bind by NAME. See pagination note below. |

### 3.2 Pagination (capability #7 reconciliation)

`ListMessage` map row: **Pagination: none (only `page`, no `perPage`)** — there is **no auto-pager /
`AutoPagingEnumerable`**. Page the whole range manually: first call with `pageSize:` set and
`page: null`/`pageToken: null`; then read `ListMessageResponse.NextPageUri` — when non-null, more
pages remain. Follow-up pages are driven by `page:` and/or `pageToken:` (the `PageToken`/`Page` query
params). Stop when `NextPageUri` is null. Do the From + date filtering **server-side via the params
above** — do not fetch wide and filter in-app. Cited: `operations/Api20100401Message.md`,
`records-4-Li-Me.md`.

### 3.3 Enum value tables (literal C# member → wire value)

From `map/models/enums.md`. Enums are `StringEnum<T>`, **not** C# enums — reference the static member
(`MessageEnumStatus.Delivered`) or `Type.FromValue("wire")`; never `.delivered`.

**`MessageEnumStatus`** (response `Status`, source `Models/Enums/MessageEnumStatus.cs`):
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`,
`Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`,
`Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumUpdateStatus`** (request `status` on UpdateMessage): single value `Canceled (canceled)`.
(There is a sibling `SmsMessageEnumUpdateStatus` with the same single value — UpdateMessage's param
type is `MessageEnumUpdateStatus`, use that one.)

**`MessageEnumScheduleType`** (request `scheduleType`): single value `Fixed (fixed)` — note reads
"For Messaging Services only … in conjunction with the send_time parameter."

**`ValidationError`** (Lookup `ValidationErrors` items, `Models/Enums/ValidationError.cs`):
`TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`,
`InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

**`MessageEnumDirection`** (response `Direction`, if used): `Inbound (inbound)`, `OutboundApi (outbound-api)`,
`OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

### 3.4 Exceptions & reading status + Twilio error code (ALL ops here are Case B)

Every operation in this integration throws **`SdkException<RawError>`** on an error status (no typed
error class exists for any of them). Read from `ex.Error` (a `RawError`,
`TwilioSdk.Core.ErrorResponse.RawError`):
- HTTP status: `ex.Error.StatusCode` (`System.Net.HttpStatusCode`).
- Raw body: `ex.Error.ReadAsString()`.
- **Twilio's own `code` / `message` / `more_info` / `status`** live in the JSON body only — there is
  **no typed accessor**. Read them with `ex.Error.ReadAsJson<T>()` into a small local record
  (e.g. `int? code`, `string? message`, `string? more_info`) — best-effort, and fall back to
  `ReadAsString()` / the generic message if that JSON shape is absent. Cited: `sdk-map.md`
  *Error-handling model* + each op's Case B row.

There is **no** `…Result` no-throw variant on any of these — you MUST `try/catch`.
Async: all methods are awaitable `Task<…>`; pass the caller's `CancellationToken` as `ct:`.

---

## 4. UNVERIFIED items (SDK contract is pinned; only live Twilio traffic can confirm these)

These are defensive-coding directives, **not** open lookups — the code path is decided here.

- **UNVERIFIED-A (invalid-number signalling).** The Lookup V2 contract returns `Valid: bool?` +
  `ValidationErrors`. Whether Twilio signals an unusable number as **HTTP 200 with `valid == false`**
  vs a **non-2xx `SdkException<RawError>`** (e.g. 404 for a wholly unparseable string) is live-wire
  behaviour. **Directive:** treat a number as invalid/rejected if **either** `Valid != true` on a 200
  response **or** the call throws `SdkException<RawError>`; canonicalize/store `PhoneNumber` only when
  `Valid == true`. Extract `ValidationErrors` best-effort for the rejection reason, fall back to a
  generic "invalid phone number" message.

- **UNVERIFIED-B (schedule constraints).** The `scheduleType`/`sendAt` params exist and are typed, and
  the enum note says scheduling is "For Messaging Services only." Whether Twilio **rejects** a scheduled
  send that lacks `messagingServiceSid`, and the exact `sendAt` min/max window (Twilio documents ~15 min
  to ~7 days ahead, provider-enforced, not enforced by the SDK), can only be confirmed by live traffic.
  **Directive:** always pass `messagingServiceSid:` (from `Twilio:MessagingServiceSid`) — never `from:` —
  when scheduling; validate `sendAt` is in the future before the call; and wrap the send so a provider
  rejection surfaces via the Case B path (read Twilio `code`/`message`) rather than as an unhandled 5xx.

- **UNVERIFIED-C (cancel-after-sent).** Cancelling via `UpdateMessage status=Canceled` succeeds only
  while the message is still `scheduled`. If it has already sent, Twilio returns an error status
  (Case B `SdkException<RawError>`, HTTP 4xx). **Directive:** catch `SdkException<RawError>`, read the
  Twilio `code`, and treat "already sent / not cancelable" as a benign no-op (log + report), not a 5xx.

- **UNVERIFIED-D (redaction durability).** `UpdateMessage body=""` empties the stored body while the
  record + status survive (op note: "used to redact Message `body` text"). That the emptied text is
  genuinely unrecoverable at Twilio is a provider guarantee not visible in the SDK. **Directive:** rely
  on the returned `ApiV2010AccountMessage` (Sid + Status present, Body empty) as confirmation the record
  survived; treat unrecoverability as Twilio's documented behaviour, not something the code can assert.

---

## 5. Trap notes (load the named skill BEFORE writing that step)

> ⚠ Step 1 (client registration & lifetime) — the `HttpClient`/handler pipeline the SDK client wraps
> has lifetime and reuse rules a constructor signature can't show, and getting them wrong causes
> socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before writing
> `new TwilioSdkClient(...)` / `AddTwilioSdkClient`.

> ⚠ Step 1 (auth wiring) — where and when credentials must be set relative to client construction,
> and loading them from config vs hardcoding, is a wiring decision the property type doesn't reveal.
> **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

> ⚠ Step 1 (base URL / retries / timeouts / pagination) — the retry/timeout options do **not** bound
> a whole call the way the names suggest, the base-URL override interacts with retries and the
> `HttpClient` you register, and whether a failed **write** (a `CreateMessage`) can be silently
> re-sent depends on retry semantics the option names hide. This matters directly for non-idempotent
> sends. **MUST load `dotnet-configuration-resilience`** before tuning the client or wiring
> `Twilio:BaseUrl`.

> ⚠ Steps 2–8 (calling ops & building requests) — these operations have many optional params with no
> C# default that mis-bind in a positional call, and enums are `StringEnum<T>` not C# enums / unmodeled
> JSON is dropped on deserialize — consequences the signature can't show. **MUST load
> `dotnet-calling-endpoints`** (call style) and **`dotnet-models`** (enums, wire names, nullability)
> before writing the calls and payloads.

> ⚠ Step 9 (error boundary) — which exception types actually reach a catch, how to read status/body
> safely on a Case B `RawError`, and the catch-ladder shapes that are silently wrong are exactly what
> a signature cannot show. **MUST load `dotnet-error-handling`** before writing any try/catch. See the
> two mandatory `JsonException` hazards in REQUIRED READING.

> ⚠ Testing — the SDK's test seam is the `HttpClient` constructor argument, not an interface you'd
> guess; error/edge paths need explicit coverage. **MUST load `dotnet-testing`** before writing tests.

---

## 6. REQUIRED READING — load every skill below BEFORE implementation starts

This sheet deliberately does **not** carry these skills' contents (defaults, worked examples, the
parts a one-line note cannot hold). Load each before writing the step it governs:

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — setting `AccountSidAuthToken` / Basic credentials from config |
| `dotnet-configuration-resilience` | Step 1 — `Twilio:BaseUrl` override, retries, timeouts, resend-on-write semantics, pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — named-argument calling, async/`ct`, request/response envelopes |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>` enums, wire names, required/nullable members, dropped JSON |
| `dotnet-error-handling` | Step 9 — Case B `SdkException<RawError>`, reading status/Twilio code safely |
| `dotnet-testing` | Tests — faking the `HttpClient` seam, covering error/edge paths |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary — it reaches the
boundary from two directions that need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 7. Assumptions & Blockers

- **Assumption:** `Twilio:AccountSid` is used both as the Basic-auth username AND as the `accountSid`
  path parameter for every `Api20100401Message` op. (Both are the account SID; the config exposes no
  separate API-key pair.) If the deployment uses API-key auth instead, the username/password change but
  the `accountSid` path param stays the account SID.
- **Assumption:** scheduled sends (capability #3) use `Twilio:MessagingServiceSid` as the sender
  (required for scheduling per the enum note), while immediate sends (#2/dispatch/cancel notices) may
  use either `Twilio:FromNumber` or `Twilio:MessagingServiceSid` — the SDK supports both; pick per
  message type in implementation.
- **Assumption:** `Twilio:BaseUrl`, when set, targets only the messaging (`Default`) node; Lookup stays
  on `lookups.twilio.com`. This matches the stated requirement.
- **No capability gaps found.** All seven requested capabilities map to real SDK operations
  (Lookup V2 validate/canonicalize; CreateMessage send + schedule; UpdateMessage cancel + redact;
  FetchMessage status; ListMessage reconcile). Nothing required a workaround or is missing from the SDK.
- **Blockers:** none.
