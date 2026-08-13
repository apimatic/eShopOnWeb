# Twilio .NET SDK integration plan — eShopOnWeb `src/PublicApi`

SDK: `AsadAli.TwilioSdk` (root namespace `TwilioSdk`), APIMatic-generated, map source commit `51fdf48`.
Client class `TwilioSdkClient`, options `TwilioSdkClientOptions`. Install version-less:
`dotnet add package AsadAli.TwilioSdk`.

Every fact below is grounded in the bundled SDK map (page cited per row); the two server-node
property names and the four core-type namespaces were confirmed from SDK source because the map
does not carry them (noted inline).

---

## 1. Scope & sequence

1. **Client registration & auth** — DI-register one long-lived `TwilioSdkClient` over an
   `IHttpClientFactory`-managed `HttpClient`; set Basic auth credentials from config; override the
   **messaging** base URL only. (Governs every step below.)
2. **Phone validation at registration** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` → gate on
   `Valid`, persist canonical `PhoneNumber`. (Capability 1. Different host — lookups node.)
3. **Send SMS** — `client.Api20100401Message.CreateMessage`. (Capability 2.)
4. **Schedule future SMS** — same `CreateMessage` with `scheduleType` + `sendAt` +
   `messagingServiceSid`. (Capability 3.)
5. **Cancel scheduled SMS** — `client.Api20100401Message.UpdateMessage` with `status=Canceled`.
   (Capability 4.)
6. **Fetch delivery outcome** — `client.Api20100401Message.FetchMessage`. (Capability 5.)
7. **Redact content** — `client.Api20100401Message.UpdateMessage` with `body=""`. (Capability 6.)
8. **List for reconciliation** — `client.Api20100401Message.ListMessage` filtered by `from` +
   date range, paged manually. (Capability 7.)
9. **Error boundary** — one translation layer over all SDK calls (all Case B). (Error handling.)

Every capability maps to a real SDK operation — **no gaps**.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — taken from
> that type's own map row / source path, never from a neighbour. Enums, records, client/server
> config, auth and error types live in different child namespaces; dropping one to the root or to
> `.Models` makes the implementer guess the wrong `using` and the build breaks.

### Namespaces (add a separate `using` per kind — child namespaces are NOT imported transitively)

| Type(s) | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `ServerEnvironment` | `TwilioSdk.Servers` |
| Controllers: `Api20100401Message`, `LookupsV2PhoneNumber` | `TwilioSdk.Api` |
| Records: `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` |
| Enums: `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection` | `TwilioSdk.Models.Enums` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` (confirmed from source) |
| `RawError` | `TwilioSdk.Core.ErrorResponse` (confirmed from source) |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` (confirmed from source) |

### Operations table

| Capability | Controller.Method (map page) | Request params (in order, types) | Response | Key fields / enums read | Error |
|---|---|---|---|---|---|
| 1. Validate + canonical E.164 | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (`operations/LookupsV2PhoneNumber.md`) | `(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 15 `string?` params after `phoneNumber` have **no default**, pass `null` to skip | `LookupResponse` | `Valid (valid): bool?` = validity gate; `PhoneNumber (phone_number): string?` = **canonical E.164** to persist; `NationalFormat` = national (do NOT store as canonical); `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` = reasons | `SdkException<RawError>` — Case B |
| 2/3. Create / schedule message | `client.Api20100401Message.CreateMessage` (`operations/Api20100401Message.md`) | `(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 params `statusCallback…contentSid` have **no default**, pass `null` for each you skip. **Call with NAMED arguments** (positional mis-binds). | `ApiV2010AccountMessage` | `Sid (sid): string?` = message SID to persist; `Status (status): MessageEnumStatus?` = delivery status; `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?` | `SdkException<RawError>` — Case B |
| 4. Cancel scheduled | `client.Api20100401Message.UpdateMessage` (`operations/Api20100401Message.md`) | `(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `body: null`, `status: MessageEnumUpdateStatus.Canceled` | `ApiV2010AccountMessage` | `Status` should become `Canceled` | `SdkException<RawError>` — Case B |
| 5. Fetch outcome | `client.Api20100401Message.FetchMessage` (`operations/Api20100401Message.md`) | `(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | `Status`, `ErrorCode`, `ErrorMessage`, `DateSent (date_sent): string?` | `SdkException<RawError>` — Case B |
| 6. Redact content | `client.Api20100401Message.UpdateMessage` (`operations/Api20100401Message.md`) | `(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, ...)` — pass `body: ""` (empty), `status: null` | `ApiV2010AccountMessage` | Body cleared at provider; `Sid`, `Status`, `DateSent` survive | `SdkException<RawError>` — Case B |
| 7. List for reconciliation | `client.Api20100401Message.ListMessage` (`operations/Api20100401Message.md`) | `(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `to…pageToken` no default, pass `null` to skip. **Named args.** | `ListMessageResponse` | see filter + pagination notes below | `SdkException<RawError>` — Case B |

### Capability-6 note (redaction): the SDK exposes exactly two disposal levers on `UpdateMessage`

- `body: ""` (empty string) redacts the **content** at the provider while the record (Sid, Status,
  DateSent, To/From) survives — this is the requested behavior.
- There is also `DeleteMessage(accountSid, sid)` (returns `void`) which removes the **whole**
  Message resource — do NOT use it here; it destroys the surviving record you must keep.

### Capability-7 filter + pagination detail (this is the whole answer to "cover the WHOLE range")

Query-param wiring for `ListMessage` (wire ← C#), from the map page:

| Intent | C# param | Wire param | Type |
|---|---|---|---|
| Sent FROM this number | `from` | `From` | `string?` (pass `Twilio:FromNumber`) |
| Sent AFTER (range start, inclusive lower bound) | `dateSentQueryQuery` | `DateSent>` | `DateTimeOffset?` |
| Sent BEFORE (range end, inclusive upper bound) | `dateSentQuery` | `DateSent<` | `DateTimeOffset?` |
| Exact-day equality (do NOT use for a range) | `dateSent` | `DateSent` | `DateTimeOffset?` |

Pass ISO-8601 date-times as `DateTimeOffset`; the SDK serializes them to the wire. The provider
filters server-side by From + range — this is a real filtered query, not post-filtering.

**Pagination (map row: "Pagination: none (only `page`, no `perPage`)").** There is **no** auto-iterator
and **no** `…Result`/streaming variant. Page manually: call with `page: 0` (then 1, 2, …) and a
`pageSize` (e.g. `long? pageSize = 1000`), holding `from`/`dateSentQueryQuery`/`dateSentQuery`
constant, until the page comes back empty. `ListMessageResponse` fields to drive the loop:
`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (the payload — read one level down,
this is the envelope), `NextPageUri (next_page_uri): string?` (null ⇒ last page), `Page (page): int?`,
`PageSize (page_size): int?`, `Start`, `End`, `Uri`, `FirstPageUri`, `PreviousPageUri`. Loop until
`Messages` is null/empty or `NextPageUri` is null to guarantee full range coverage. (map:
`records-4-Li-Me.md`)

### Response envelope shapes

- **`ApiV2010AccountMessage`** (map: `records-1-Ac-Ca.md`) — the message resource itself (NOT wrapped).
  Fields the integration reads: `Sid (sid): string?`, `Status (status): MessageEnumStatus?`,
  `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateSent (date_sent): string?`,
  `From (from): string?`, `To (to): string?`, `Body (body): string?`,
  `MessagingServiceSid (messaging_service_sid): string?`, `Direction (direction): MessageEnumDirection?`.
  Note `DateSent`/`DateCreated`/`DateUpdated` are `string?` (not `DateTimeOffset`) — parse if needed.
- **`ListMessageResponse`** (map: `records-4-Li-Me.md`) — envelope; payload is the `Messages` list (see above).
- **`LookupResponse`** (map: `records-4-Li-Me.md`) — flat; read `Valid` and `PhoneNumber` (see cap-1 row).

### Enum value tables (map: `models/enums.md`; enums are `StringEnum<T>`, build via `Type.FromValue("wire")` or static members `Type.Member`)

**`MessageEnumStatus`** (delivery status; wire in parens):
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`,
`Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`,
`Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.
→ Treat `Failed`/`Undelivered` (and `Canceled`) as terminal non-delivery OUTCOMES, not exceptions.

**`MessageEnumScheduleType`** (to schedule): only `Fixed (fixed)`. Set `scheduleType: MessageEnumScheduleType.Fixed`
together with `sendAt`.

**`MessageEnumUpdateStatus`** (for `UpdateMessage`): only `Canceled (canceled)`. Cancel via
`status: MessageEnumUpdateStatus.Canceled`. (Note the create/read status enum is the wider
`MessageEnumStatus`; the update-status enum is the narrower `MessageEnumUpdateStatus` — different types.)

**`MessageEnumDirection`** (read-only on response): `Inbound (inbound)`, `OutboundApi (outbound-api)`,
`OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

### Scheduling requirements (capability 3) — contract facts

- Scheduling requires **`messagingServiceSid`** set (from `Twilio:MessagingServiceSid`) AND
  `scheduleType: MessageEnumScheduleType.Fixed` AND `sendAt: <DateTimeOffset>`. The map's
  `MessageEnumScheduleType` doc states `fixed` is "For Messaging Services only" — so pass
  `messagingServiceSid` and leave `from: null` for scheduled sends. (map: `models/enums.md`)
- The allowed future window for `sendAt` (min/max lead time) is **provider-enforced**, surfaced as an
  API error, not a compile-time constraint — see Assumptions (`UNVERIFIED`); handle defensively.

### Client construction, auth, base-URL override

**Auth** (map: *Servers & auth*): Basic auth via `TwilioSdkClientOptions.AccountSidAuthToken` of type
`BasicAuthCredentials?` (namespace `TwilioSdk.Core.Authentication.Basic`). Use API key SID + secret,
or `Twilio:AccountSid` + `Twilio:AuthToken` as username/password (test use). Set BEFORE constructing
the client / in the DI callback. Environment: `options.Environment = ServerEnvironment.Production`
(only member; namespace `TwilioSdk.Servers`).

**Base-URL override — per-capability, NOT global (answers the config question directly).**
`options.Server` is a `ServerOptions` (namespace `TwilioSdk`) holding one node **per API host**, each
with a `.Production.BaseUrl` string. The map labels the message operations' host `Default (api)` and
the lookups host `Default4 (lookups)`; confirmed from SDK source (`ServerOptions.cs`,
`Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`):

| Capability / host label | Override property | Default value |
|---|---|---|
| **Messaging** (`CreateMessage`/`Fetch`/`Update`/`List` — host `Default (api)`) | `options.Server.Default.Production.BaseUrl` | `https://api.twilio.com` |
| **Lookup/validation** (`FetchPhoneNumber3` — host `Default4 (lookups)`) | `options.Server.Default4.Production.BaseUrl` | `https://lookups.twilio.com` |

→ To honor `Twilio:BaseUrl` for **messaging only**: when the config value is present, set
`options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl verbatim>` and **leave
`options.Server.Default4` at its default** so lookup calls still hit `https://lookups.twilio.com`.
When `Twilio:BaseUrl` is absent, set nothing (messaging keeps `https://api.twilio.com`). The URL is
re-resolved per request from the `Server` node, so these are the correct and only override points.
(See trap note for the mutation-timing gotcha.)

### Error model (all six operations are Case B)

Every operation above throws **`SdkException<RawError>`** (namespace `TwilioSdk.Core.Exceptions` /
`TwilioSdk.Core.ErrorResponse`). No typed `{Operation}Error`, no `TryGet…` accessors, no no-throw
variant. Read from `ex.Error` (a `RawError`):
- `StatusCode: HttpStatusCode` — the HTTP status.
- `ReadAsString(): string` — raw body.
- `ReadAsJson<T>(): T?` — deserialize the body.
- `ReadAsBytes(): ReadOnlyMemory<byte>`.

**Reading the Twilio error `code`/`message` safely (defensive directive — `UNVERIFIED` shape):** the
map has no generated model for the Twilio error body, so its wire shape (`code`/`message`/`more_info`/
`status`) cannot be confirmed from the map or source — only live traffic can. Extract **best-effort**:
call `ex.Error.ReadAsJson<T>()` into a small local DTO with nullable `code`/`message`; if it is null or
throws, **fall back** to `ex.Error.ReadAsString()` and then to a generic message. Never let this
extraction throw out of the boundary. Label this handling `UNVERIFIED` in code comments.

---

## 3. Trap notes (name the hazard; load the skill — do not implement from these lines)

⚠ Step 1 (client & DI) — whether the `HttpClient`/handler pipeline may be rebuilt per request or must
be long-lived and shared, and whether the SDK client wrapper is singleton or transient, is not shown by
the constructor signature. **MUST load `dotnet-client-initialization`** before wiring DI.

⚠ Step 1 (auth) — where and when credentials must be set relative to client construction, and how to
source them from configuration rather than hardcoding, is not shown by the property type. **MUST load
`dotnet-authentication`** before setting `AccountSidAuthToken`.

⚠ Step 1 (base-URL override) — whether editing `options.Server.Default.Production.BaseUrl` takes effect
after the client is constructed, and whether reassigning the parent object vs mutating the leaf behaves
differently, is a timing gotcha the property shape does not reveal; the SDK's retry/timeout options also
do **not** bound a whole call and are not the `HttpClient` timeout. **MUST load
`dotnet-configuration-resilience`** before wiring the client and the override.

⚠ Steps 2–8 (calling ops) — which optional params mis-bind in a positional call, and how cancellation
is threaded, is not shown by the signature. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–8 (models/enums) — how `StringEnum<T>` is constructed and compared, how required vs nullable
members behave, and that unmodeled JSON fields are dropped on deserialize, is not shown by the field
list. **MUST load `dotnet-models`** before building requests or mapping responses.

⚠ Step 3 (scheduling window) — whether a `sendAt` outside the provider's allowed lead-time window is
rejected, and how that surfaces, is provider behavior the signature cannot show. **MUST load
`dotnet-error-handling`** for how that rejection reaches your catch.

⚠ Step 9 (error boundary) — which exception types actually reach the catch, and how a failed send must
be prevented from failing the underlying order, is not shown by the return type. **MUST load
`dotnet-error-handling`** before writing any try/catch.

⚠ Step 8 (pagination resilience) — how a transport failure mid-pagination interacts with retry (and
whether a non-idempotent verb can re-execute) is not shown by the loop shape. **MUST load
`dotnet-configuration-resilience`** before finalizing the reconciliation loop.

---

## 4. REQUIRED READING — load BEFORE implementation starts

This sheet deliberately does **not** carry these skills' contents; load each before writing the code
for its step.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — setting `AccountSidAuthToken` / Basic credentials from config |
| `dotnet-configuration-resilience` | Step 1 & 8 — base-URL override timing, retries/timeouts, pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — named-argument calls, optional params, cancellation |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, required/nullable members, dropped-field behavior |
| `dotnet-error-handling` | Steps 3 & 9 — the exception boundary, reading status/error body safely |
| `dotnet-testing` | Tests — the HttpClient test seam |

**`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite
handling — carry both, before the boundary is written:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape
  the integration boundary;
- a **non-2xx** body that does not match its operation's generated error shape throws `JsonException`
  *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException`
  and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then
  reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that
  can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- `Twilio:AccountSid` is the `{AccountSid}` path parameter passed as the `accountSid` argument to every
  `Api20100401Message` operation (the map models it as an explicit method parameter, not client-global).
- Sends/notifications (capability 2, immediate) use `from: Twilio:FromNumber`; scheduled sends
  (capability 3) use `messagingServiceSid: Twilio:MessagingServiceSid` with `from: null`, because
  `scheduleType=fixed` is Messaging-Services-only per the enum doc. If immediate sends should also go
  through the Messaging Service, swap `from` for `messagingServiceSid` there too — confirm intent.
- "Usable destination at registration" is gated on `LookupResponse.Valid == true` (and empty
  `ValidationErrors`), storing `LookupResponse.PhoneNumber` as the canonical E.164 value.
- ISO-8601 reconciliation bounds are passed as `DateTimeOffset` to `dateSentQueryQuery` (after) and
  `dateSentQuery` (before).

**`UNVERIFIED` (only live traffic can confirm — handle defensively, do not assert):**
- Whether `LookupResponse.Valid == true` alone implies the number is a deliverable **SMS** destination,
  or whether line-type must be inspected (needs `fields: "line_type_intelligence"` on
  `FetchPhoneNumber3` and reading the line-type sub-object). Also whether an unusable/nonexistent number
  surfaces as `Valid == false` in a 200 body vs a 404 `SdkException<RawError>`. Handle BOTH: reject on
  `Valid != true` and reject on a 4xx lookup exception; carrier acceptance is ultimately only knowable
  at send time.
- The provider-enforced min/max lead-time window for scheduled `sendAt`, and the exact preconditions
  under which `UpdateMessage status=Canceled` succeeds (only while still `scheduled`) — both surface as
  API errors, not contract constraints. Treat the resulting `SdkException<RawError>` as an expected
  rejection, not a crash.
- The wire shape of the Twilio error body (`code`/`message`/…) behind `RawError` — extract best-effort
  via `ReadAsJson<T>()`, fall back to `ReadAsString()` then a generic message.

**Blockers**: none — every requested capability maps to a real SDK operation.
