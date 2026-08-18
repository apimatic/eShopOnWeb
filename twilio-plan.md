# Twilio .NET SDK integration plan — SMS order notifications (eShopOnWeb)

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`) · root namespace `TwilioSdk` · client `TwilioSdkClient` · APIMatic-generated · map source commit `51fdf48`.

All contract facts below are grounded in the bundled SDK map (pages cited per row) and, where the map only named a type, in the SDK source for that one type. Every operation in scope is **throw-based, Case B** (`SdkException<RawError>` — no typed accessors, no no-throw variant).

---

## 1. Scope & sequence

Configuration keys to bind (an options POCO, e.g. `TwilioOptions`): `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, optional `Twilio:BaseUrl` (messaging-API base-URL override ONLY).

| Step | Capability | Operation(s) | Host |
|---|---|---|---|
| 0 | Client construction, auth, DI, messaging base-URL override | `TwilioSdkClient` / `TwilioSdkClientOptions` | — |
| 1 | Validate destination + get E.164 canonical form (at registration) | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | **lookups host — NOT messaging** |
| 2 | Send SMS, get SID + delivery status | `client.Api20100401Message.CreateMessage` | messaging (api) |
| 3 | Schedule a follow-up message with the provider | `client.Api20100401Message.CreateMessage` (+ `scheduleType`/`sendAt`) | messaging (api) |
| 4 | Cancel a scheduled message before it sends | `client.Api20100401Message.UpdateMessage` (`status: Canceled`) | messaging (api) |
| 5 | Fetch a single message's current status by SID | `client.Api20100401Message.FetchMessage` | messaging (api) |
| 6 | Redact a message's body at the provider | `client.Api20100401Message.UpdateMessage` (`body: ""`) | messaging (api) |
| 7 | List messages from a From-number over a date range (paged) | `client.Api20100401Message.ListMessage` | messaging (api) |

**No capability in scope is a GAP** — all seven are exposed. (See Assumptions & Blockers for the one host-routing caveat and the scheduling-bounds caveat.)

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that
> type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and client-config
> types are spread across different child namespaces, and two types configured side by side in the same options
> object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

**Namespaces (add a `using` per kind — child namespaces are NOT imported transitively):**

| Type | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| Controllers (accessed as properties on `client`) | on the client — no separate `using` beyond `TwilioSdk` |
| Records: `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`, `ValidationError` | `TwilioSdk.Models` |
| Enums: `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection` | `TwilioSdk.Models.Enums` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |
| `RequestOptions` (optional param, default `null` — omit unless needed) | `TwilioSdk.Core` |
| `DefaultOptions` / `Default4Options` (server nodes; usually reached via `ServerOptions`) | `TwilioSdk.Servers` |

### 2a. Operations

Legend: params listed in order; `[E]` = nullable but has **no C# default → must pass explicitly** (pass `null` to skip); `ct` is the cancellation token (default). All in-scope ops throw `SdkException<RawError>` (**Case B**) — accessors: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. No typed error accessors, no `…Result` no-throw variant on any of them.

**Step 1 — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`** — map: `operations/LookupsV2PhoneNumber.md`
- Signature: `FetchPhoneNumber3(string phoneNumber, string? fields[E], string? countryCode[E], string? firstName[E], string? lastName[E], string? addressLine1[E], string? addressLine2[E], string? city[E], string? state[E], string? postalCode[E], string? addressCountryCode[E], string? nationalId[E], string? dateOfBirth[E], string? lastVerifiedDate[E], string? verificationSid[E], string? partnerSubId[E], RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Call: `phoneNumber` = raw number as typed by shopper; pass the 15 `[E]` params as `null` (or `countryCode` if you want to resolve a national-format number). HTTP `GET /v2/PhoneNumbers/{PhoneNumber}`.
- Returns: `LookupResponse` (map: `records-4-Li-Me.md`). Relevant fields:
  - `Valid (valid): bool?` — provider's validity verdict (the registration gate — see trap ⚠1).
  - `PhoneNumber (phone_number): string?` — **the E.164 canonical form to store.**
  - `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?`.
  - `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` — details when `Valid == false`.
- **Host: `lookups` server node (`Server.Default4`, base `https://lookups.twilio.com`) — the `Twilio:BaseUrl` override must NOT be applied to this call** (see Step 0 and trap ⚠2).

**Step 2 — `client.Api20100401Message.CreateMessage`** — map: `operations/Api20100401Message.md`
- Signature (25 params; 24 `[E]` between `statusCallback` and `contentSid`):
  `CreateMessage(string accountSid, string to, string? statusCallback[E], string? applicationSid[E], double? maxPrice[E], bool? provideFeedback[E], int? attempt[E], int? validityPeriod[E], bool? forceDelivery[E], MessageEnumContentRetention? contentRetention[E], MessageEnumAddressRetention? addressRetention[E], bool? smartEncoded[E], IReadOnlyList<string>? persistentAction[E], MessageEnumTrafficType? trafficType[E], bool? shortenUrls[E], MessageEnumScheduleType? scheduleType[E], DateTimeOffset? sendAt[E], bool? sendAsMms[E], string? contentVariables[E], MessageEnumRiskCheck? riskCheck[E], string? from[E], string? fallbackFrom[E], string? messagingServiceSid[E], string? body[E], IReadOnlyList<string>? mediaUrl[E], string? contentSid[E], RequestOptions? requestOptions = null, CancellationToken ct = default)`
- `accountSid` = `Twilio:AccountSid`. `to` = E.164 destination (positional required, non-null). Set exactly one sender: `from:` = `Twilio:FromNumber` **or** `messagingServiceSid:` = `Twilio:MessagingServiceSid` (leave the other `null`). `body:` = text. Everything else `[E]` → `null`. **Use named arguments** — 24 optional params with no defaults will mis-bind positionally.
- Wire (request query params): `To ← to`, `From ← from`, `MessagingServiceSid ← messagingServiceSid`, `Body ← body`, `ScheduleType ← scheduleType`, `SendAt ← sendAt`. HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json`.
- Returns: `ApiV2010AccountMessage` (map: `records-1-Ac-Ca.md`). Read:
  - `Sid (sid): string?` — **the message SID.**
  - `Status (status): MessageEnumStatus?` — **current delivery status** (enum values in §2b).
  - Also available: `To`, `From`, `Body`, `DateSent (date_sent): string?`, `DateCreated`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `MessagingServiceSid`.
  - Envelope note: the response is the message object directly — fields are top-level, **not** wrapped.

**Step 3 — Schedule a message** — same `CreateMessage` op, map: `operations/Api20100401Message.md` + `enums.md`
- Set `scheduleType:` = `MessageEnumScheduleType.Fixed` (wire `fixed` — the enum's ONLY member), `sendAt:` = `DateTimeOffset` of desired send time, and `messagingServiceSid:` = `Twilio:MessagingServiceSid` with `from: null`. The `MessageEnumScheduleType` map row states scheduling is **"For Messaging Services only"** — so a scheduled send MUST go through the messaging service, not a bare From number.
- The returned `ApiV2010AccountMessage.Status` for a scheduled message is `MessageEnumStatus.Scheduled` (wire `scheduled`); its `Sid` is what you persist to cancel later (Step 4).
- Delay bounds (min/max): **not encoded in the SDK** — see trap ⚠3 and Assumptions & Blockers.

**Step 4 — Cancel a scheduled message** — `client.Api20100401Message.UpdateMessage` — map: `operations/Api20100401Message.md`
- Signature: `UpdateMessage(string accountSid, string sid, string? body[E], MessageEnumUpdateStatus? status[E], RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Call: `accountSid` = `Twilio:AccountSid`, `sid` = the scheduled message SID, `body: null`, `status:` = `MessageEnumUpdateStatus.Canceled` (wire `canceled` — the enum's ONLY member). HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`.
- Returns `ApiV2010AccountMessage`; `Status` becomes `MessageEnumStatus.Canceled`.
- Note: `MessageEnumUpdateStatus` (used to cancel) is a distinct enum from `MessageEnumStatus` (read on responses). Do not cross them.

**Step 5 — Fetch a single message** — `client.Api20100401Message.FetchMessage` — map: `operations/Api20100401Message.md`
- Signature: `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`. HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`.
- `accountSid` = `Twilio:AccountSid`, `sid` = message SID.
- Returns `ApiV2010AccountMessage`; read `Status (status): MessageEnumStatus?` (and `ErrorCode`/`ErrorMessage` for failed deliveries).

**Step 6 — Redact a message's body at the provider** — `client.Api20100401Message.UpdateMessage` — map: `operations/Api20100401Message.md`
- Use `UpdateMessage(accountSid, sid, body: "", status: null, ...)` — pass an **empty string** body to redact the text at the provider. The map notes `UpdateMessage` is *"used to redact Message `body` text and to cancel not-yet-sent messages."*
- What survives: the message resource persists — `Sid`, `Status`, `To`, `From`, `DateSent`, `ErrorCode`, etc. remain retrievable; only `Body` is emptied. **Do NOT use `DeleteMessage`** for this: it removes the whole resource (`DELETE …/Messages/{Sid}.json`), destroying the outcome the requirement says must survive.
- Returns `ApiV2010AccountMessage` with `Body` now empty.

**Step 7 — List messages for reconciliation (From-filtered, date-ranged, paged)** — `client.Api20100401Message.ListMessage` — map: `operations/Api20100401Message.md`
- Signature: `ListMessage(string accountSid, string? to[E], string? from[E], DateTimeOffset? dateSent[E], DateTimeOffset? dateSentQuery[E], DateTimeOffset? dateSentQueryQuery[E], long? pageSize[E], int? page[E], string? pageToken[E], RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Server-side From filter:** pass `from:` = `Twilio:FromNumber` (wire `From`). This filters at the provider — do NOT list-then-filter client-side.
- **Date range (note the confusing param↔wire mapping):**
  - `dateSentQueryQuery:` → wire **`DateSent>`** = lower bound (messages sent **on/after** the range start).
  - `dateSentQuery:` → wire **`DateSent<`** = upper bound (messages sent **on/before** the range end).
  - `dateSent:` → wire `DateSent` = exact-day match (leave `null` for a range query).
- Both bounds are `DateTimeOffset?`; pass ISO-8601 date-times converted to `DateTimeOffset`.
- Returns: `ListMessageResponse` (map: `records-4-Li-Me.md`):
  - `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` — each item exposes `Sid`, `To`, `From`, `Status`, `DateSent`, `Body` (per Step 2's record).
  - Paging fields: `Page (page): int?`, `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `FirstPageUri`, `Start`, `End`, `Uri`.
- **Pagination:** the map marks this op `Pagination: none (only page, no perPage)` — there is **no auto-pager helper**. To cover the whole range you must page manually (advance `page`/`pageToken`, or follow `NextPageUri` until it is null). See trap ⚠4.

### 2b. Enums (map: `models/enums.md`) — `using TwilioSdk.Models.Enums;`

- `MessageEnumStatus` (on `ApiV2010AccountMessage.Status`; member `(wire)`):
  `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.
- `MessageEnumScheduleType` (on `CreateMessage.scheduleType`): `Fixed (fixed)` — only member.
- `MessageEnumUpdateStatus` (on `UpdateMessage.status`): `Canceled (canceled)` — only member.
- `MessageEnumDirection` (on `ApiV2010AccountMessage.Direction`): `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.
- These are `StringEnum<T>`, **not** C# enums — build with `MessageEnumScheduleType.Fixed` (static member) or `MessageEnumScheduleType.FromValue("fixed")`; compare via the member, and treat an unknown/unmodeled wire value as possible (see `dotnet-models`).

### 2c. Client construction, auth, base-URL override

**Auth** (map: `sdk-map.md` *Servers & auth*; type shape from `Core/Authentication/Basic/BasicAuthCredentials.cs`):
- `TwilioSdkClientOptions.AccountSidAuthToken` is a `BasicAuthCredentials?`. `BasicAuthCredentials` is an object-initializer type with two `required` members:
  - `Username` (`required string`) — API key SID, or `Twilio:AccountSid` for local/testing.
  - `Password` (`required string`) — API key secret, or `Twilio:AuthToken` for local/testing.
  - i.e. `options.AccountSidAuthToken = new BasicAuthCredentials { Username = cfg.AccountSid, Password = cfg.AuthToken };`
- Basic auth over HTTPS (the SDK Base64-encodes `Username:Password`). Load secrets from configuration, never hardcode.

**Environment**: `options.Environment` is a `ServerEnvironment` with the single member `ServerEnvironment.Production`.

**Messaging base-URL override** (`Twilio:BaseUrl`) — shape from `ServerOptions.cs` + `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`:
- `options.Server` is a `ServerOptions` holding one node per server. The **messaging (2010-04-01) API resolves against the `Default` node** (map rows show messaging ops as *"Default (api)"*; `DefaultOptions.ProductionOptions.BaseUrl` defaults to `https://api.twilio.com`).
- To apply the override, set ONLY the `Default` node's base URL:
  `options.Server.Default.Production.BaseUrl = cfg.BaseUrl;  // only when Twilio:BaseUrl is set`
- **The Lookup call resolves against the `Default4` node** (`Default4Options.ProductionOptions.BaseUrl` = `https://lookups.twilio.com` — map rows show Lookup as *"Default4 (lookups)"*). Leave `Server.Default4` untouched so Lookup keeps hitting the real lookups host. This is why the override is messaging-only: the two capabilities resolve against different `ServerOptions` nodes. (See trap ⚠2.)

**Construction / DI**: constructor is `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`; DI helper is `services.AddTwilioSdkClient(o => { … })`. HttpClient lifetime and the transient-vs-singleton wrapper question are governed by `dotnet-client-initialization` (trap ⚠5).

---

## 3. Trap notes (load the named skill before coding that step — the note names the hazard, not the fix)

- ⚠1 Step 1 (registration gate) — whether `LookupResponse.Valid == true` alone means the provider will actually accept the number as an SMS destination (vs. merely a well-formed number — landline vs mobile reachability lives behind the `fields`/`line_type_intelligence` request) is a semantic only live traffic/Twilio can confirm. Treat `Valid` as the gate and store `PhoneNumber` (E.164); decide separately whether landline rejection is required. `UNVERIFIED` — see Assumptions & Blockers.
- ⚠2 Step 0/1 (host routing) — the messaging `Twilio:BaseUrl` override and the Lookup call resolve against **different** `ServerOptions` nodes (`Default` vs `Default4`); setting the override too broadly (or on the wrong node) silently redirects Lookup traffic. **MUST load `dotnet-configuration-resilience`** before wiring base-URL/server selection.
- ⚠3 Step 3 (scheduling) — whether a chosen `sendAt` satisfies the provider's accepted scheduling window is not validated by the SDK; an out-of-window value is rejected at `CreateMessage`, not at compile time. Handle that rejection at the error boundary. **MUST load `dotnet-error-handling`** for reading the rejection. `UNVERIFIED` bounds — see Assumptions & Blockers.
- ⚠4 Step 7 (pagination) — this list op exposes no auto-pager; covering the whole date range without dropping or double-counting rows depends on how you drive `page`/`pageToken`/`NextPageUri`. **MUST load `dotnet-configuration-resilience`** (pagination section) before writing the loop.
- ⚠5 Step 0 (client & DI) — the `HttpClient`/handler pipeline lifetime and whether the SDK client wrapper is transient or singleton are not visible in the constructor signature. **MUST load `dotnet-client-initialization`** before registering the client.
- ⚠6 Step 0 (auth wiring) — when to set credentials relative to client construction, and secret loading, are not shown by the property type. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.
- ⚠7 Steps 2–4 (request models / enums) — `StringEnum<T>` is not a C# enum, and required members / null-vs-empty distinctions on request fields behave unlike plain properties. **MUST load `dotnet-models`** before building request payloads.
- ⚠8 All steps (calling) — the 24 no-default optional params on `CreateMessage` (and the 8 on `ListMessage`) mis-bind in a positional call. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠9 Provider error extraction — every in-scope op is Case B (`SdkException<RawError>`): there is **no typed accessor** for Twilio's `code`/`message`/`more_info` error fields. Extract them best-effort from `ex.Error.ReadAsString()` / `ReadAsJson<T>()`, and **fall back to a generic message** if the body does not parse. Whether the live wire body matches any assumed shape is `UNVERIFIED`. **MUST load `dotnet-error-handling`** before writing the boundary.

---

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, HttpClient ownership/lifetime, DI registration (`AddTwilioSdkClient`) |
| `dotnet-authentication` | Step 0 — setting `AccountSidAuthToken` / `BasicAuthCredentials`, secret loading |
| `dotnet-configuration-resilience` | Step 0 base-URL/server selection (`Twilio:BaseUrl` on `Server.Default`), Step 7 pagination, retries/timeouts |
| `dotnet-calling-endpoints` | Steps 1–7 — named-argument calling, the must-pass-explicitly optional params, async/`ct` |
| `dotnet-models` | Steps 2–4 — request models, `StringEnum<T>` enums, required/nullable members, wire-name mapping |
| `dotnet-error-handling` | The error boundary around every call — Case B `SdkException<RawError>`, reading status + provider error body |
| `dotnet-testing` | Testing the integration — the `HttpClient` seam, error/edge paths |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary** (`JsonException` reaches the boundary from two directions and they need opposite handling):
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **No GAPs** — all seven capabilities are exposed by the SDK plugin.
- **Assumption (auth):** the integration uses `Twilio:AccountSid` as `BasicAuthCredentials.Username` and `Twilio:AuthToken` as `Password` (the SDK's documented AccountSid/AuthToken basic-auth path). If the org instead uses an API key SID + secret, map those into the same two fields.
- **Assumption (sender selection):** for immediate sends (Step 2) either `Twilio:FromNumber` or `Twilio:MessagingServiceSid` may be used; for scheduled sends (Step 3) `Twilio:MessagingServiceSid` is REQUIRED (per the `MessageEnumScheduleType` map note "For Messaging Services only") — plan uses the messaging service for scheduling.
- **`UNVERIFIED` (registration usability, ⚠1):** the map/source expose `LookupResponse.Valid: bool?` and `PhoneNumber: string?` (E.164) but cannot confirm that `Valid == true` equals "the provider will accept this as an SMS destination." Directive: gate registration on `Valid == true`, store `PhoneNumber`; if landline/mobile filtering is required, request `fields = "line_type_intelligence"` and inspect `LineTypeIntelligence` — confirm the exact semantics against live Lookup responses.
- **`UNVERIFIED` (scheduling delay bounds, ⚠3):** the SDK does not encode a min/max `sendAt` window (the enum carries only `Fixed`); the map/source give no numeric bounds. Directive: do not hardcode a bound from memory — send the desired `sendAt` and treat a provider rejection at `CreateMessage` as a validation failure surfaced through the error boundary; confirm the accepted window against live traffic.
- **`UNVERIFIED` (provider error body shape, ⚠9):** Case B gives no typed error accessors; the exact JSON of Twilio's error body (`code`/`message`/`more_info`/`status`) can only be confirmed live. Directive: extract best-effort from `ReadAsString()`/`ReadAsJson<T>()`, fall back to a generic message.
