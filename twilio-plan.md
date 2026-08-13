# Twilio .NET SDK integration — SMS order-notifications (eShopOnWeb / src/PublicApi)

Package: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`).
Client: `TwilioSdkClient` · Options: `TwilioSdkClientOptions` · Root namespace: `TwilioSdk`.
SDK map provenance: source commit `51fdf48`. Every row below cites the map page it came from.

---

## 1. Scope & sequence

1. **Client registration + auth + base-URL wiring** (DI in `src/PublicApi`) — build `TwilioSdkClientOptions`, set Basic auth from `Twilio:AccountSid`/`Twilio:AuthToken`, set the messaging base URL from `Twilio:BaseUrl` (only on the messaging server node), register `TwilioSdkClient`.
2. **Validate + canonicalize a phone number at registration** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (lookups host — NOT governed by `Twilio:BaseUrl`).
3. **Send SMS immediately** (order placed / dispatched / cancelled) — `client.Api20100401Message.CreateMessage`.
4. **Schedule a follow-up message for later** — `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`.
5. **Cancel a scheduled message** — `client.Api20100401Message.UpdateMessage` (status = canceled).
6. **Resend with a caller idempotency key** — see BLOCKER B1; the SDK exposes no idempotency mechanism.
7. **Fetch a single message's delivery outcome** — `client.Api20100401Message.FetchMessage`.
8. **Redact a message's content** — `UpdateMessage` (body = empty). Full-record removal = `DeleteMessage`.
9. **List messages for reconciliation over a date range, filtered by From** — `client.Api20100401Message.ListMessage` (hand-driven paging).

---

## 2. Base-URL / server selection — how it works in THIS SDK

Source: `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Core/RequestOptions.cs`; map *Servers & auth* + `dotnet-configuration-resilience`.

- `options.Server` is a `ServerOptions` (namespace `TwilioSdk`) that holds **one property per named server node**. Each node has a per-environment options object with a settable `BaseUrl`. This SDK declares exactly one environment: `ServerEnvironment.Production` (namespace `TwilioSdk.Servers`).
- **Base URL is selected PER-CAPABILITY (per server node), not per call.** The two capabilities in scope resolve to **different** server nodes:
  - **Messaging API** (send / fetch / list / update-cancel / update-redact / delete) → node **`Default`** → `options.Server.Default.Production.BaseUrl` — SDK default `https://api.twilio.com`. Type `DefaultOptions` / nested `ProductionOptions` (namespace `TwilioSdk.Servers`).
  - **Lookups API** (phone-number validation) → node **`Default4`** → `options.Server.Default4.Production.BaseUrl` — SDK default `https://lookups.twilio.com`. Type `Default4Options` (namespace `TwilioSdk.Servers`).
- **To honor `Twilio:BaseUrl`:** when the config value is present, set `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` verbatim (a literal URL with no `{placeholders}` is used as-is). **Leave `options.Server.Default4` untouched** — the lookup call then keeps `https://lookups.twilio.com`. This satisfies the requirement that only messaging uses the override while lookup uses its own host.
- **There is NO per-call base-URL override.** `RequestOptions` (namespace `TwilioSdk.Core`) is `sealed record RequestOptions { LogLevel? LogLevel }` — it carries only a log level. Base URL cannot be varied per call; it is fixed per server node at/after construction.
- Lifecycle trap: `options.Environment` is captured once at construction, but the `ServerOptions` object is read live per request. Set `BaseUrl` on the node **before** constructing the client (see trap note, step 1).

---

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

### 3.1 Namespaces (`using` directives) for this integration

| Type(s) | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` (+ nested `ProductionOptions`) | `TwilioSdk.Servers` |
| Controllers (`Api20100401Message`, `LookupsV2PhoneNumber`) — only if you store the accessor in a local | `TwilioSdk.Api` |
| Records: `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` |
| Enums: `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection`, `ValidationError` | `TwilioSdk.Models.Enums` |
| `RequestOptions` | `TwilioSdk.Core` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` |
| `SdkException<TError>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |

### 3.2 Client construction & auth

Source: map *Getting a client* + *Servers & auth*; `TwilioSdkClientOptions.cs`.

- Constructor: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
- DI extension: `services.AddTwilioSdkClient(o => { /* set credentials / environment / server on o */ })` (`ServiceCollectionExtensions.cs`).
- Auth property: `options.AccountSidAuthToken` of type `BasicAuthCredentials?` (Basic auth). Bind username/password from `Twilio:AccountSid` / `Twilio:AuthToken`. (The SDK doc-comment recommends an API key SID+secret in production, but AccountSid+AuthToken is accepted.)
- Environment: `options.Environment = ServerEnvironment.Production` (only member).
- `Twilio:FromNumber` and `Twilio:MessagingServiceSid` are **application config**, not client-options properties — pass them as request arguments (see per-operation rows).

### 3.3 Operations

**Legend:** params listed in signature order; every nullable-no-default param must be passed explicitly (pass `null` to skip). `ct` is the cancellation token (default `default`); all ops are async-returning `Task<...>`. No operation in this SDK has a no-throw `…Result` variant. Every messaging op below is **Case B** (`SdkException<RawError>`) — no typed accessors.

| # | Operation | Signature (params in order) | Request fields used | Response envelope → fields read | Error | Paging | Map page |
|---|---|---|---|---|---|---|---|
| 2 | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` (path, raw input); `countryCode` (ISO for national-format input); other 13 → pass `null` | Returns **`LookupResponse`** (no wrapper). Read: `Valid (valid): bool?` (validity), `PhoneNumber (phone_number): string?` (**E.164 canonical**), `NationalFormat (national_format): string?`, `CallingCountryCode`, `CountryCode`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` | `SdkException<RawError>` — Case B (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). A non-existent/garbage number surfaces as `Valid == false` **and/or** a 404; see validity note below | none | `operations/LookupsV2PhoneNumber.md`; `records-4-Li-Me.md` (LookupResponse) |
| 3 | `client.Api20100401Message.CreateMessage` (send) | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Required: `accountSid` (path = `Twilio:AccountSid`), `to`. Sender: **exactly one of** `from` (= `Twilio:FromNumber`) **or** `messagingServiceSid` (= `Twilio:MessagingServiceSid`). `body` = text. All other 20+ nullables → pass `null`. | Returns **`ApiV2010AccountMessage`** (no wrapper). Read: `Sid (sid): string?` (message SID), `Status (status): MessageEnumStatus?` (initial status, e.g. `queued`/`accepted`/`scheduled`), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?` | `SdkException<RawError>` — Case B | none | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` (ApiV2010AccountMessage) |
| 3s | `CreateMessage` (**scheduled** variant) | same signature as #3 | `scheduleType = MessageEnumScheduleType.Fixed`; `sendAt = <future DateTimeOffset>`; **`messagingServiceSid` (NOT `from`)** — see enum note; `to`, `body` as usual | `ApiV2010AccountMessage` → `Sid`, `Status` (expect `scheduled`) | Case B | none | same |
| 5 | `client.Api20100401Message.UpdateMessage` (**cancel**) | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid`; `body = null`; `status = MessageEnumUpdateStatus.Canceled` | `ApiV2010AccountMessage` → `Status` (expect `canceled`), `Sid` | Case B | none | `operations/Api20100401Message.md` |
| 7 | `UpdateMessage` (**redact**) | same signature | `accountSid`, `sid`; `body = ""` (empty string); `status = null` | `ApiV2010AccountMessage` → record survives (`Sid`, `Status`, `To`, `From`, dates, `ErrorCode`); `Body` now empty | Case B | none | same |
| 7d | `client.Api20100401Message.DeleteMessage` (full removal — alternative) | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` | returns `void` (Task) — removes the whole Message record | Case B | none | `operations/Api20100401Message.md` |
| 4/5 fetch | `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` | `ApiV2010AccountMessage` → `Status`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateSent`, `Price` | Case B | none | `operations/Api20100401Message.md` |
| 8 | `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`; `to = null`; **`from` = the specific From number** (provider-side filter → wire `From`); range: **`dateSentQueryQuery`** → wire **`DateSent>`** (lower bound / on-or-after start), **`dateSentQuery`** → wire **`DateSent<`** (upper bound / on-or-before end); `dateSent = null` (exact-date form); `pageSize`, `page`, `pageToken` for paging | Returns **`ListMessageResponse`** (single page wrapper). Read: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (items → `Sid`, `To`, `From`, `Status`, `DateSent`, `Body`); paging: `NextPageUri (next_page_uri): string?`, `Page`, `PageSize`, `Start`, `End`, `Uri` | `SdkException<RawError>` — Case B | **Not auto-paginated** — hand-driven (see paging note) | `operations/Api20100401Message.md`; `records-4-Li-Me.md` (ListMessageResponse) |

### 3.4 Enum value tables (namespace `TwilioSdk.Models.Enums`; StringEnum — use members, not raw strings)

Source: `map/models/enums.md`. Build via the static member (e.g. `MessageEnumStatus.Delivered`) or `.FromValue("wire")`.

**`MessageEnumStatus`** (response `status` on `ApiV2010AccountMessage`) — member (wire):
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumUpdateStatus`** (write, on `UpdateMessage.status`): only `Canceled (canceled)`.

**`MessageEnumScheduleType`** (write, on `CreateMessage.scheduleType`): only `Fixed (fixed)`. Enum doc (source): *"For Messaging Services only … in conjunction with the send_time parameter in order to schedule a Message."* → **scheduling requires `messagingServiceSid`, not `from`** (contract note, evidence = the enum's own doc string).

**`MessageEnumDirection`** (response `direction`): `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

**`ValidationError`** (StringEnum in `LookupResponse.ValidationErrors`, source `Models/ValidationError.cs`, namespace `TwilioSdk.Models.Enums`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

### 3.5 Cross-cutting contract facts

- **Validity signalling (lookup, #2):** the map/source expose two channels — the `Valid (valid): bool?` field and `ValidationErrors`. Treat a number as usable only when `Valid == true`; read the canonical E.164 from `PhoneNumber (phone_number)`. Whether a truly malformed number returns `Valid == false` on a 200 vs. throws a 4xx `SdkException<RawError>` is **UNVERIFIED** (only live traffic confirms) — handle **both**: check `Valid` on success AND catch `SdkException<RawError>` and treat a 404/4xx as "not a usable destination." Store `PhoneNumber` (E.164) — never the raw caller input.
- **Cancel precondition (#5):** setting `status = Canceled` only succeeds for a message the provider still holds unsent (the op's own map note: *"cancel not-yet-sent messages"*; typically `status == scheduled`). The exact precondition the provider enforces is **UNVERIFIED** from the SDK surface — on cancel, extract the returned `Status` best-effort and, if the provider rejects (Case B `SdkException<RawError>`, e.g. the message already sent), surface the status and fall back to the generic message rather than assuming success.
- **List date range (#8):** to cover the WHOLE range in one query set both bounds — `dateSentQueryQuery` (wire `DateSent>`, start) and `dateSentQuery` (wire `DateSent<`, end). Inclusive-vs-exclusive boundary semantics are **UNVERIFIED** from the map; if exact-boundary messages matter, widen the range by one unit and filter the two edge timestamps in code.
- **Redact vs delete (#7):** `UpdateMessage` with `body = ""` empties the body text while the Message record (SID, status, To/From, dates, error code) survives and stays fetchable. `DeleteMessage` removes the whole record. Choose per requirement: the brief wants the record + status to survive → use redact (`UpdateMessage`, `body=""`).

---

## 4. Trap notes (attached to the step where each bites — load the named skill before coding that step)

> ⚠ Step 1 (client registration & HttpClient) — the `HttpClient`/handler pipeline lifetime and how the SDK client wraps it is not visible in the constructor signature, and getting it wrong is a socket-exhaustion / stale-DNS class of bug. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient(...)` or `AddTwilioSdkClient(...)`.

> ⚠ Step 1 (base-URL wiring) — WHEN the SDK reads `options.Environment` vs. the live `options.Server` object, and what the SDK's retry `Timeout` actually bounds, are not what the option names imply; set the messaging node's `BaseUrl` at the wrong moment and your `Twilio:BaseUrl` override is silently ignored. **MUST load `dotnet-configuration-resilience`** before wiring the server node, retries, or timeouts.

> ⚠ Step 1 (auth) — WHERE credentials must be set relative to client construction, and how to source them from configuration rather than hardcode, is not shown by the `AccountSidAuthToken` property alone. **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Steps 2–9 (every call) — these operations have many nullable-no-default parameters that bind positionally and mis-bind silently in a positional call; whether an optional argument may be omitted is not shown by the signature. **MUST load `dotnet-calling-endpoints`** before writing the first call.

> ⚠ Steps 2–9 (models & enums) — `MessageEnumStatus` / `MessageEnumScheduleType` / `ValidationError` are `StringEnum<T>`, not C# enums, and unmodeled JSON fields are dropped on deserialize; how to construct and compare them, and how `required`/nullable init works, is not shown by the field list. **MUST load `dotnet-models`** before constructing request payloads or mapping responses.

> ⚠ Step 8 (list paging) — `ListMessage` is **not** auto-paginated (returns a single `ListMessageResponse` page, not `IAsyncEnumerable`); "walk until the provider stops" and "fewer than pageSize" are provider-supplied stop conditions, not bounds, and an unbounded reconciliation loop is a hang-in-production defect. How to drive `page`/`pageSize`/`NextPageUri` safely and what bound to add is not on the operation row. **MUST load `dotnet-configuration-resilience`** (Pagination section) before writing the reconciliation loop.

> ⚠ Steps 2–9 (error boundary) — which exception types actually reach your catch, how to read the status code and provider error code/message off a Case-B `RawError`, and the traps that make a reasonable catch ladder silently wrong are not derivable from the "Case B" label. **MUST load `dotnet-error-handling`** before writing any try/catch or error middleware.

> ⚠ Test step — which seam to fake and how to assert real behaviour rather than execution is not obvious from the SDK surface. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## 5. REQUIRED READING — load BEFORE implementation starts

These `dotnet-*` companion skills must be loaded before writing the corresponding code. This sheet deliberately does **not** carry their contents (defaults, worked examples, boundary cases) — the trap notes name the hazard only.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, options/builder shape, HttpClient ownership/lifetime, ASP.NET Core DI registration. |
| `dotnet-authentication` | Step 1 — supplying `BasicAuthCredentials`, sourcing secrets from `Twilio:` config, where to set credentials relative to construction. |
| `dotnet-configuration-resilience` | Step 1 + Step 8 — base-URL/server-node selection (the `Twilio:BaseUrl` override), retries/timeouts, and `ListMessage` pagination bounds. |
| `dotnet-calling-endpoints` | Steps 2–9 — finding the controller, required vs optional params, named-argument calls, async/cancellation. |
| `dotnet-models` | Steps 2–9 — building request models, `required`/nullable init, `StringEnum` construction, JSON wire vs C# names. |
| `dotnet-error-handling` | Steps 2–9 — the integration error boundary; reading status + provider error code/message off `RawError` (Case B). Always required — an integration always writes an error boundary. |
| `dotnet-testing` | Test step — which seam to fake, covering error/edge paths. |

**Two mandatory error-boundary hazard rows (a `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling):**
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 6. Assumptions & Blockers

**Assumptions**
- `Twilio:AccountSid` + `Twilio:AuthToken` are used as the Basic-auth username/password (the SDK also accepts an API-key SID + secret in the same `AccountSidAuthToken` property; the config section names AccountSid/AuthToken, so those are assumed).
- Immediate sends (#3) use `Twilio:FromNumber` as `from`; scheduled sends (#3s) use `Twilio:MessagingServiceSid` as `messagingServiceSid` (required by the schedule feature — evidence: `MessageEnumScheduleType` doc string). Confirm which sender the non-scheduled sends should prefer if both config values are set.
- Redaction (#7) is done via `UpdateMessage(body="")` so the record + status survive (matches the brief); `DeleteMessage` is documented as the full-removal alternative but is not the chosen path.
- `Twilio:BaseUrl`, when present, is applied only to the messaging server node (`Default`); lookups (`Default4`) always use `https://lookups.twilio.com`.

**Blockers**
- **B1 — Idempotency key on send (#6) is NOT exposed by the SDK.** `CreateMessage` has no idempotency parameter; `RequestOptions` (namespace `TwilioSdk.Core`) is `sealed record { LogLevel? LogLevel }` — it carries **no** custom-header or idempotency-key facility; there is no client-level idempotency option on `TwilioSdkClientOptions`. There is therefore **no SDK-native way** to supply a caller-provided `Idempotency-Key` header (or any per-call header) on the send operation. The requirement "same key ⇒ no duplicate send; fresh key ⇒ new send" cannot be met through the SDK surface. Do not fake it with request parameters. (The only avenue is outside the SDK contract — a custom `HttpClient` `DelegatingHandler` injecting the header, which requires carrier-side idempotency support and caller-key plumbing via `AsyncLocal`; this is a build decision for the main agent, not an SDK feature, and is called out here rather than assumed.)
- **B2 — no per-call base-URL override (informational, not blocking the plan).** Base URL is per-server-node only; if any future requirement needs per-request host switching within the same capability, the SDK cannot do it (`RequestOptions` has no base-URL field). The stated per-capability requirement IS satisfiable (see §2).
