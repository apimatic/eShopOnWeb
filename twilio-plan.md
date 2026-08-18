# Twilio .NET SDK — Contract Sheet & Integration Plan (eShopOnWeb `src/PublicApi`)

SDK: `AsadAli.TwilioSdk` (root namespace `TwilioSdk`), client `TwilioSdkClient`. Every fact below is
grounded in the bundled SDK map (source commit `51fdf48`); where the map was silent the SDK source was
consulted and the specific finding is cited inline. Install version-less: `dotnet add package AsadAli.TwilioSdk`.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `TwilioSdkClient` via `AddTwilioSdkClient(...)` (or construct with an
   `IHttpClientFactory`-owned `HttpClient`). Bind `Twilio:` config; set auth + the messaging base-URL override.
2. **Auth** — set `options.AccountSidAuthToken` from `Twilio:AccountSid` (username) + `Twilio:AuthToken` (password).
3. **Base-URL override** — if `Twilio:BaseUrl` is set, apply it to the **messaging** server node ONLY
   (`options.Server.Default.Production.BaseUrl`); leave the **Lookup** node (`options.Server.Default4`) at its default.
4. **Validate destination at registration** (cap 2) — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (Lookup host).
5. **Send SMS** (cap 1) — `client.Api20100401Message.CreateMessage` (via From, or via Messaging Service SID).
6. **Schedule send** (cap 3) — same `CreateMessage` with `scheduleType` + `sendAt` + messaging service.
7. **Cancel scheduled** (cap 4) — `client.Api20100401Message.UpdateMessage` with cancel status.
8. **Fetch status** (cap 5) — `client.Api20100401Message.FetchMessage`.
9. **Redact body** (cap 6) — `client.Api20100401Message.UpdateMessage` with empty body.
10. **List/reconcile** (cap 7) — `client.Api20100401Message.ListMessage` with From + DateSent range, paged.
11. **Idempotency** (cap 8) — implement in your own store; the SDK has no mechanism (see contract note).
12. **Error boundary** — one Case-B (`SdkException<RawError>`) catch layer over all calls (see Trap notes + Required reading).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from
> that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and
> client-config types live in different child namespaces; two types configured side by side in the same
> options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the
> implementer guess the wrong `using`, and the build breaks.

### 2a. Namespaces (add a `using` per type kind — child namespaces are NOT imported transitively)

| Type | Fully-qualified namespace | Source of fact |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` | sdk-map.md (client), `ServerOptions.cs` (source) |
| `AddTwilioSdkClient` (DI ext) | `TwilioSdk` (on `IServiceCollection`) | sdk-map.md |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` (`.Production.BaseUrl`) | `TwilioSdk.Servers` | sdk-map.md; `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs` (source) |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` | records-1-Ac-Ca.md, records-4-Li-Me.md |
| `MessageEnumStatus`, `MessageEnumUpdateStatus`, `MessageEnumScheduleType`, `ValidationError` | `TwilioSdk.Models.Enums` | enums.md |
| `RequestOptions` | `TwilioSdk.Core` | `Core/RequestOptions.cs` (source) |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` | `Core/Exceptions/SdkException.cs` (source) |
| `RawError`, `ApiError` | `TwilioSdk.Core.ErrorResponse` | `Core/ErrorResponse/RawError.cs` (source) |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | `Core/Authentication/Basic/BasicAuthCredentials.cs` (source) |

### 2b. Operations

All in-scope operations are **throw-based, Case B**: on an error status they throw
`TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. There is **no** typed
`{Op}Error` and **no** no-throw `…Result` variant for any of them. Read status/body via the `RawError`
accessors (§2e). `accountSid` = `Twilio:AccountSid`.

| # | Call (`client.X.Method`) | Full signature (params in order) | Purpose / required inputs | Returns | Map page |
|---|---|---|---|---|---|
| 1,3 | `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Send/schedule. `to` (E.164) + `accountSid` non-nullable. Body text ← `body`. Sender: EITHER `from` (a From number) OR `messagingServiceSid` — pass one, leave the other `null`. Schedule: see cap 3 below. The 24 middle params are nullable-with-no-default → **must pass explicitly** (pass `null` to skip) — use named args. | `ApiV2010AccountMessage` (§2c) | operations/Api20100401Message.md |
| 4,6 | `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Cancel (cap 4): `status: MessageEnumUpdateStatus.Canceled`, `body: null`. Redact (cap 6): `body: ""` (empty string), `status: null`. `body` and `status` are nullable-no-default → **pass both explicitly**. | `ApiV2010AccountMessage` (§2c) | operations/Api20100401Message.md |
| 5 | `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Fetch one message by SID to read status/outcome. | `ApiV2010AccountMessage` (§2c) | operations/Api20100401Message.md |
| 7 | `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Server-side filter by From (`from`) + DateSent range. The 8 middle params (`to`…`pageToken`) are nullable-no-default → **pass explicitly**. Range mapping in §2d. | `ListMessageResponse` (§2c) | operations/Api20100401Message.md |
| 2 | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Validate a destination number + get canonical E.164 **before** sending. `phoneNumber` = the input number. 15 middle params nullable-no-default → **pass `null` explicitly**. Runs on the **Lookup host, NOT the messaging host** (see §2f). | `LookupResponse` (§2c) | operations/LookupsV2PhoneNumber.md |
| 1 (alt) | `client.Api20100401Message.DeleteMessage` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Hard-delete a message record (only if you need removal, not redaction — redaction keeps the record; see cap 6). Returns `void` (`Task`). | `void` | operations/Api20100401Message.md |

### 2c. Response envelopes & the fields the integration reads

**`ApiV2010AccountMessage`** (returned by CreateMessage / UpdateMessage / FetchMessage; also each element of the List response) — map: records-1-Ac-Ca.md. **Every field is nullable** (`string?` / enum? / `int?`); read null-safe.
- `Sid (sid): string?` — the created/looked-up message SID (read-back of the message identity).
- `Status (status): MessageEnumStatus?` — delivery status (enum values §2d).
- `Body (body): string?` · `To (to): string?` · `From (from): string?` · `MessagingServiceSid (messaging_service_sid): string?`
- `DateSent (date_sent): string?` · `DateCreated (date_created): string?` · `DateUpdated (date_updated): string?`
- `ErrorCode (error_code): int?` — Twilio numeric error code on a failed/undelivered message.
- `ErrorMessage (error_message): string?` — human-readable failure reason.
- `NumSegments`, `NumMedia`, `Price`, `PriceUnit`, `Direction (MessageEnumDirection?)`, `ApiVersion`, `AccountSid`, `Uri`, `SubresourceUris (object?)` — also present.
- **No response wrapper**: the message object is returned directly (not nested under a field).

**`ListMessageResponse`** (returned by ListMessage) — map: records-4-Li-Me.md.
- `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` — **the payload list lives one level down, under `Messages`**. Each element carries SID, To, From, Status, DateSent, Body per `ApiV2010AccountMessage` above.
- Paging fields: `Page (page): int?` · `PageSize (page_size): int?` · `NextPageUri (next_page_uri): string?` · `PreviousPageUri (previous_page_uri): string?` · `FirstPageUri (first_page_uri): string?` · `Uri (uri): string?` · `Start (start): int?` · `End (end): int?`.

**`LookupResponse`** (returned by FetchPhoneNumber3) — map: records-4-Li-Me.md.
- `PhoneNumber (phone_number): string?` — **the provider's canonical E.164 form** (read this as the normalized number).
- `Valid (valid): bool?` — whether the number is a valid/usable destination. Invalid is signalled by this **field**, not by an exception (a malformed/unfound lookup still 2xx's with `Valid=false`; only transport/auth/host errors throw — §2e).
- `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` — reasons a number is invalid (enum §2d).
- `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?` · plus optional add-on infos (`LineTypeIntelligence`, `SimSwap`, `CallerName`, etc.) and `Url`.

### 2d. Enums (map: enums.md — use the literal C# member, e.g. `MessageEnumStatus.Delivered`, or `Type.FromValue("wire")`; these are `StringEnum<T>`, not C# enums)

**`MessageEnumStatus`** (`ApiV2010AccountMessage.Status`) — 13 members:
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`,
`Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`,
`Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumUpdateStatus`** (`UpdateMessage` `status` param) — 1 member: `Canceled (canceled)` — this is the value that cancels a scheduled message.

**`MessageEnumScheduleType`** (`CreateMessage` `scheduleType` param) — 1 member: `Fixed (fixed)` — required (set to `Fixed`) to schedule; used together with `sendAt`.

**`ValidationError`** (`LookupResponse.ValidationErrors` element) — 6 members:
`TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`,
`InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

### 2e. Error / exception boundary (Case B — applies to every in-scope op)

- Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`.
- `ex.Error.StatusCode : System.Net.HttpStatusCode` — the HTTP status.
- `ex.Error.ReadAsString() : string` — raw error body.
- `ex.Error.ReadAsJson<T>() : T?` — deserialize the Twilio error body (`{ code, message, more_info, status }` shape) to read the Twilio error code + message. There is **no typed accessor** on these operations (Case B); do not expect `TryGet…`.
- `ex.Error.ReadAsBytes() : System.ReadOnlyMemory<byte>` — raw bytes.
- Source of Case-B mechanics: sdk-map.md *Error-handling model*; per-op rows in operations/Api20100401Message.md & operations/LookupsV2PhoneNumber.md.

### 2f. Client construction, auth, base-URL override

**Auth (Account SID + Auth Token).** `TwilioSdkClientOptions.AccountSidAuthToken : BasicAuthCredentials?`
(namespace `TwilioSdk.Core.Authentication.Basic`). `BasicAuthCredentials` has two `required` members:
`Username` and `Password` (source: `BasicAuthCredentials.cs`). Set `Username = Twilio:AccountSid`,
`Password = Twilio:AuthToken`. (The SDK docs note an API key + secret is preferred over SID+token for
non-local use; the task specifies SID + Auth Token, so use those.)

**Construction / DI.** `services.AddTwilioSdkClient(o => { ... })`, or
`new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`. `options.Environment` =
`ServerEnvironment.Production` (only member). Source: sdk-map.md *Getting a client* / *Servers & auth*.

**Base-URL override (messaging host only).** The SDK exposes a **per-server-node base URL**
(`ServerOptions`, source `ServerOptions.cs`). The messaging API (`Api20100401Message.*`, tagged
`Default (api)`) resolves against node **`Default`**; the Lookup API (`LookupsV2PhoneNumber`, tagged
`Default4 (lookups)`) resolves against node **`Default4`** (source: operation `HTTP` lines + `Server.cs`).
Each node's URL is `Production.BaseUrl` (source `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`):

- Messaging default: `options.Server.Default.Production.BaseUrl` = `https://api.twilio.com`.
- Lookup default: `options.Server.Default4.Production.BaseUrl` = `https://lookups.twilio.com`.

Therefore, when `Twilio:BaseUrl` is set, assign it to `options.Server.Default.Production.BaseUrl` ONLY.
Because CreateMessage, FetchMessage, ListMessage, UpdateMessage, DeleteMessage all resolve against node
`Default` (all their map `HTTP` lines read `(Default (api))`), this single assignment routes every
messaging call (send, read, list, redact, delete, reconcile) to the override verbatim, and — because the
Lookup call resolves against the separate `Default4` node — leaves Lookup untouched. Do **not** touch
`options.Server.Default4`.

**Idempotent send (cap 8) — SDK offers NO native mechanism.** `CreateMessage` has no idempotency-key
parameter (see full signature §2b), and `RequestOptions` exposes only `LogLevel` — no custom-header seam
(source: `Core/RequestOptions.cs`). There is therefore no way to attach an idempotency key through the SDK.
**Idempotency must be implemented in your own store** (e.g. persist an operator-supplied key → resulting
message SID, and short-circuit on replay before calling `CreateMessage`).

**Async & cancellation.** Every operation is async (returns `Task`/`Task<T>`) and takes a trailing
`CancellationToken ct = default` — pass the request's token as `ct:`. Source: all operation signatures.

---

## 3. Trap notes (hazard + consequence + skill pointer — do NOT implement from these one-liners)

- ⚠ **Step 1 (client & DI)** — how the `HttpClient`/handler pipeline must be owned and lifetime-scoped (and whether the SDK client wrapper is transient vs singleton) is not shown by the constructor; getting it wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`.**
- ⚠ **Step 2 (auth)** — where and when credentials must be set relative to client construction, and how to source them from configuration rather than hardcoding, is not implied by the property type. **MUST load `dotnet-authentication`.**
- ⚠ **Steps 4–10 (every call)** — `CreateMessage` (24), `ListMessage` (8) and `FetchPhoneNumber3` (15) have many nullable-no-default params that mis-bind in a positional call; the correct calling convention (named arguments) is a hazard the signature alone doesn't force. **MUST load `dotnet-calling-endpoints`.**
- ⚠ **Steps 4–10 (models/enums)** — `MessageEnum*`/`ValidationError` are `StringEnum<T>` (not C# enums), response fields are all-nullable, and unmodeled JSON is dropped on deserialize; how to build/read these safely is not visible in the field list. **MUST load `dotnet-models`.**
- ⚠ **Step 3 (base-URL / server selection) & Step 10 (pagination)** — what `Timeout` actually bounds, which calls retry (and whether a failed POST can be re-sent), and how to drive full-range pagination through `ListMessage` (there is no auto-pager; how `page`/`pageToken`/`NextPageUri` relate and terminate) are all decisions the option/field names do not settle. **MUST load `dotnet-configuration-resilience`.**
- ⚠ **Step 5 & 7 (write re-execution)** — because `CreateMessage` is a non-idempotent POST and the SDK has no idempotency key, whether a transport-level retry can send the SMS more than once is a hazard that interacts with cap 8's own-store design. **MUST load `dotnet-configuration-resilience`.**
- ⚠ **Step 12 (error boundary)** — which exception types actually reach the catch, why an SDK-exception-only ladder is silently incomplete, and how to read status/body without destroying it, are not conveyed by the Case-B accessor list. **MUST load `dotnet-error-handling`.**
- ⚠ **Testing** — the correct fake seam (the `HttpClient` constructor argument) and how to cover the error/edge paths are not obvious from the surface. **MUST load `dotnet-testing`.**

---

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 2 — setting `AccountSidAuthToken` credentials, config sourcing |
| `dotnet-calling-endpoints` | Steps 4–10 — named-argument calling convention, request/response shapes |
| `dotnet-models` | Steps 4–10 — `StringEnum<T>` enums, nullability, union/JSON-name handling |
| `dotnet-configuration-resilience` | Steps 3, 5, 7, 10 — base-URL/server selection, retries/timeouts, pagination |
| `dotnet-error-handling` | Step 12 — the exception boundary (mandatory for any integration) |
| `dotnet-testing` | Tests — faking the SDK seam, covering error paths |

**Two mandatory `System.Text.Json.JsonException` hazards — it reaches the error boundary from two directions that need opposite handling:**
- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- The messaging operations `CreateMessage`, `UpdateMessage`, `FetchMessage`, `ListMessage`, `DeleteMessage` all resolve against server node `Default (api)` (confirmed from each op's map `HTTP` line), so overriding `options.Server.Default.Production.BaseUrl` routes all of them; Lookup resolves against the distinct `Default4 (lookups)` node, so `Twilio:BaseUrl` correctly does not affect it.
- `Twilio:AccountSid` is used both as the `accountSid` path argument on every messaging call and as the Basic-auth username; `Twilio:AuthToken` is the Basic-auth password. (Task states auth = Account SID + Auth Token.)
- For cap 3 (scheduling), Twilio requires a Messaging Service to schedule: pass `messagingServiceSid` (from `Twilio:MessagingServiceSid`) together with `scheduleType: MessageEnumScheduleType.Fixed` and `sendAt`, and leave `from` null. The SDK exposes exactly these params; it does not itself enforce the combination (all are nullable), so the caller must supply them together.
- Cap 7 date range: map `sendAt`/DateSent params as — `dateSentQueryQuery` → wire `DateSent>` = lower bound (on/after "from"); `dateSentQuery` → wire `DateSent<` = upper bound (on/before "to"); `dateSent` = exact match (leave null for a range). Filter by From via the `from` param (server-side, per the task's requirement). Page by looping `page` (or following `NextPageUri`) until `Messages` is empty / `NextPageUri` is null — see the resilience skill for the correct driver.

**UNVERIFIED (only live provider traffic can confirm — code defensively as directed)**
- **Redaction value (cap 6):** the SDK sends whatever string you pass as `Body`; that an **empty string** `""` is the specific value Twilio interprets as "redact the body while keeping the record" is a provider-side behavior the SDK source cannot confirm. Directive: call `UpdateMessage(body: "", status: null)`, treat a 2xx as success, and best-effort re-fetch the message to confirm `Body` is cleared while `Sid`/`Status` survive; if the provider rejects it, surface the `RawError` status+message rather than assuming success.
- **DateSent range inclusivity (cap 7):** whether the wire `DateSent>` / `DateSent<` bounds are inclusive or exclusive of the boundary instant is provider-side and not visible in the SDK source. Directive: do not rely on exact-boundary inclusion for correctness — widen the range by the boundary granularity if a boundary-day message must be guaranteed captured, and de-duplicate by `Sid` when reconciling.
- **Response field population (all ops):** every `ApiV2010AccountMessage` / `LookupResponse` field is nullable in the generated model, so whether the live wire actually populates a given field (e.g. `ErrorCode`, `DateSent`, `PhoneNumber`) on a given response cannot be guaranteed from the contract. Directive: read every field null-safe; on Lookup, branch on `Valid == true` before trusting `PhoneNumber`, and fall back to the generic rejection path when a needed field is absent.

**Blockers**: none — every requested capability is exposed by the SDK.
