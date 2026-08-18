# Twilio .NET SDK integration plan — eShopOnWeb `src/PublicApi` (messaging)

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`) · root namespace `TwilioSdk` · client `TwilioSdkClient` · options `TwilioSdkClientOptions`. Map source commit `51fdf48`.

All eight requested capabilities are exposed by the SDK map — **no gaps**. Capabilities 1–6 live on the messaging (`api`) host under `client.Api20100401Message`; capability 7 (phone validation / Lookup) lives on a **different host** (`lookups`) under `client.LookupsV2PhoneNumber`; capability 8 (idempotency) has **no native SDK mechanism** and is an application-layer concern (a normal answer, not a gap).

---

## 1. Scope & sequence

1. **Client & DI registration** — register one long-lived `TwilioSdkClient` (via `AddTwilioSdkClient` or a factory over `IHttpClientFactory`); set Basic auth from `Twilio:AccountSid` + `Twilio:AuthToken`; when `Twilio:BaseUrl` is set, apply it as the base address for every **messaging** call. Ops: none (wiring).
2. **Send an SMS** — `client.Api20100401Message.CreateMessage(...)` with `to`, `body`, and `from` and/or `messagingServiceSid`. Read back `Sid` + `Status`.
3. **Schedule an SMS** — `CreateMessage(...)` with `scheduleType = MessageEnumScheduleType.Fixed`, `sendAt`, and a `messagingServiceSid`.
4. **Cancel a scheduled message** — `client.Api20100401Message.UpdateMessage(...)` with `status = MessageEnumUpdateStatus.Canceled`.
5. **Fetch one message** — `client.Api20100401Message.FetchMessage(...)`; read `Status` + `ErrorCode`.
6. **List by date range + From** — `client.Api20100401Message.ListMessage(...)` with `from` + the `DateSent<` / `DateSent>` range params; page to cover the whole range.
7. **Redact or delete a message body** — `UpdateMessage(...)` with `body = ""` (redact; record survives) and/or `DeleteMessage(...)` (delete resource).
8. **Validate a phone number (E.164)** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3(...)`; read `Valid` + `PhoneNumber`. **Lookup host, not messaging host** (see note below).
9. **Idempotency for resend** — application-layer dedupe; the SDK exposes no native idempotency key on message-create (see note below).

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

### Namespaces (using-directives)
`using TwilioSdk;` (client, `TwilioSdkClientOptions`) · `using TwilioSdk.Api;` (controllers — not needed if calling via `client.X`) · `using TwilioSdk.Models;` (records: `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`) · `using TwilioSdk.Models.Enums;` (all `MessageEnum*`) · `using TwilioSdk.Errors;` (`ApiError`) · `using TwilioSdk.Servers;` (`ServerEnvironment`). C# does **not** import child namespaces transitively — add each `using` separately.

### Operations

| # | Controller.Method (signature — params in order) | Request fields used (wire ← C#) | Response envelope + fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| 2/3 Send/Schedule | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path, required), `To ← to` (required), `Body ← body`, `From ← from`, `MessagingServiceSid ← messagingServiceSid`, `ScheduleType ← scheduleType` (`Fixed`), `SendAt ← sendAt`. **All 24 params `statusCallback`…`contentSid` are nullable with NO default → must pass explicitly; pass `null` to skip.** | Returns **`TwilioSdk.Models.ApiV2010AccountMessage`** (bare record, no wrapper). Read `Sid (sid): string?`, `Status (status): MessageEnumStatus?`. | `SdkException<RawError>` — **Case B**. `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()`. | none |
| 4 Cancel | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path), `sid` (path), `Status ← status` = `MessageEnumUpdateStatus.Canceled`, `Body ← body` = `null`. **`body` and `status` are nullable with no default → pass both explicitly.** | Returns `ApiV2010AccountMessage`. | `SdkException<RawError>` — **Case B**. Same accessors. | none |
| 5 Fetch | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path), `sid` (path). | Returns `ApiV2010AccountMessage`. Read `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`. | `SdkException<RawError>` — **Case B**. Same accessors. | none |
| 6 List | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path), `From ← from`, `DateSent ← dateSent` (exact day), `DateSent< ← dateSentQuery` (upper bound), `DateSent> ← dateSentQueryQuery` (lower bound), `PageSize ← pageSize`, `Page ← page`, `PageToken ← pageToken`. **8 params `to`…`pageToken` nullable, no default → pass explicitly.** | Returns **`TwilioSdk.Models.ListMessageResponse`** — this IS the envelope. Inner list: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`. Paging fields: `Page (page): int?`, `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `FirstPageUri`, `Start`, `End`, `Uri`. | `SdkException<RawError>` — **Case B**. Same accessors. | **none (no auto-pager)** — SDK returns one page; caller pages manually (see trap ⚠ pagination). |
| 6 Redact | `UpdateMessage(...)` (row above) with `body = ""`, `status = null` | `Body ← body` = `""` (empty string). | Returns `ApiV2010AccountMessage`; record + `Status` survive, `Body` is emptied at provider. | Case B. | none |
| 6 Delete | `client.Api20100401Message.DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path), `sid` (path). | Returns `void` (Task) — deletes the resource entirely. | `SdkException<RawError>` — **Case B**. Same accessors. | none |
| 7 Lookup/validate | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` (path, required — the number to validate). **15 params `fields`…`partnerSubId` nullable, no default → pass explicitly (`null` to skip).** | Returns **`TwilioSdk.Models.LookupResponse`** (bare record). Read `Valid (valid): bool?` (reject when not `true`), `PhoneNumber (phone_number): string?` (canonical **E.164** to store), `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`. | `SdkException<RawError>` — **Case B**. Same accessors. | none |

### Enums (`TwilioSdk.Models.Enums`) — build with `Type.FromValue("wire")` or the static member; these are `StringEnum<T>`, not C# enums

| Enum | Members (`CSharpMember (wire)`) | Used for |
|---|---|---|
| `MessageEnumStatus` (response `Status`) | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | delivery outcome (cap. 1, 4, 5); `Scheduled` distinguishes a scheduled message |
| `MessageEnumScheduleType` (request `scheduleType`) | `Fixed (fixed)` | schedule an SMS (cap. 2) — only value is `Fixed` |
| `MessageEnumUpdateStatus` (request `status` on Update) | `Canceled (canceled)` | cancel a scheduled message (cap. 3) — only value is `Canceled` |
| `MessageEnumDirection` (response `Direction`) | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | message direction (informational) |
| `MessageEnumRiskCheck` (request) | `Enable (enable)`, `Disable (disable)` | optional risk-check override |
| `MessageEnumTrafficType` (request) | `Free (free)` | optional |
| `MessageEnumContentRetention` (request) | `Retain (retain)`, `Discard (discard)` | optional content-retention control |
| `MessageEnumAddressRetention` (request) | `Retain (retain)`, `Obfuscate (obfuscate)` | optional address-retention control |

### Delivery error code location (capability 4)
On `ApiV2010AccountMessage`: the provider delivery error code is `ErrorCode (error_code): int?` and its text is `ErrorMessage (error_message): string?`. These are populated on the fetched message record — NOT read from an exception. (An exception's provider error is read separately via the Case-B `RawError` accessors below.)

### Date-range semantics (capability 5)
The three date params map to wire `DateSent` (exact), `DateSent<` (`dateSentQuery`, upper bound), `DateSent>` (`dateSentQueryQuery`, lower bound). To bound a range, set `dateSentQueryQuery` (lower) and `dateSentQuery` (upper) and leave `dateSent` null. The exact inclusive/exclusive boundary behaviour of `DateSent<` / `DateSent>` is enforced provider-side and is not settled by the map — treat both bounds defensively and reconcile on the returned `DateSent` per message; **UNVERIFIED** (only live traffic confirms boundary inclusivity).

### Client construction / auth / servers
- **Constructor**: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`. DI: `services.AddTwilioSdkClient(o => { ... })` (`ServiceCollectionExtensions.cs`).
- **Auth** (`TwilioSdkClientOptions.AccountSidAuthToken : BasicAuthCredentials?`): Basic auth. Set from `Twilio:AccountSid` (username) + `Twilio:AuthToken` (password). Set before constructing the client / inside the DI callback.
- **Options properties** (`TwilioSdkClientOptions`): `Environment : ServerEnvironment` (member `ServerEnvironment.Production`, namespace `TwilioSdk.Servers`), `Retry : RetryOptions`, `Logging : LoggingOptions`, `Server : ServerOptions`, `AccountSidAuthToken : BasicAuthCredentials?`.
- **Base URL / server**: base-URL templates and override points live under `Servers/` and `options.Server` (`ServerOptions`). The messaging operations use the `api` server node; Lookup uses a separate `lookups` node (see cap. 7 note). `Twilio:BaseUrl` must be applied to the messaging node only.
- **`accountSid` path parameter**: every `Api20100401Message` operation takes `accountSid` as its first argument — supply `Twilio:AccountSid` at each call site.

### Idempotency (capability 8) — SDK fact
`CreateMessage`'s generated signature exposes **no** idempotency-key parameter and **no** Idempotency-Key header hook; `RequestOptions` is the only per-call extension point and the map does not surface a native idempotency mechanism on it. Conclusion: **the SDK provides no native send-idempotency** — a caller-supplied key must be deduped at the application layer. (App-layer design is out of scope for this sheet.)

### Capability 7 — Lookup is on a DIFFERENT host (explicit)
`FetchPhoneNumber3` is `GET /v2/PhoneNumbers/{PhoneNumber}` on the **`lookups`** server node (`Default4 (lookups)`), whereas all `Api20100401Message` operations are on the **`api`** (messaging) node (`/2010-04-01/...`). `Twilio:BaseUrl` governs ONLY the messaging API per the brief — it must **not** be applied to the Lookup call. If a custom base URL is needed for Lookup it is a separate concern; do not point Lookup at the messaging `BaseUrl`.

---

## 3. Trap notes (load the named skill before writing that step — do not implement from the one-liner)

> ⚠ Step 1 (client & DI) — whether the `HttpClient`/handler pipeline may be rebuilt per request or must be long-lived and shared, and whether the SDK client wrapper is singleton or transient, is not shown by the constructor. Getting it wrong causes socket exhaustion or stale handlers. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

> ⚠ Step 1 (auth) — where and when `AccountSidAuthToken` must be set relative to client construction, and how to source the secret from configuration rather than hardcoding, is not shown by the property type. **MUST load `dotnet-authentication`** before setting credentials.

> ⚠ Step 1 (base URL / multi-host) — how `options.Server` / `ServerOptions` selects and overrides a base URL per server node, and how applying `Twilio:BaseUrl` to the messaging (`api`) node while leaving the `lookups` node untouched actually works, is not settled by the option names. Overriding the wrong node (or all nodes) would silently redirect Lookup traffic. **MUST load `dotnet-configuration-resilience`** before wiring the base URL.

> ⚠ Steps 2–3 (calling create with many nullable params) — the 24 must-pass-explicitly optionals mis-bind in a positional call; which arguments to name and how optional params with no C# default behave is not shown by the signature. **MUST load `dotnet-calling-endpoints`** before writing the first `CreateMessage` call.

> ⚠ Steps 2–6 (enums & models) — `MessageEnum*` are `StringEnum<T>`, not C# enums, and unmodeled JSON fields drop on deserialize; how to construct/compare enum values and read response records safely is not shown by the field list. **MUST load `dotnet-models`** before building request payloads or mapping responses.

> ⚠ Step 2 (send ret/timeout) & resend — whether a failed or timed-out `CreateMessage` (a non-idempotent POST) can be transparently re-sent by the retry layer, what `Retry.Timeout` actually bounds (per-attempt vs whole call), and which triggers cause a POST to replay, is not shown by the option names. This directly affects duplicate sends. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts.

> ⚠ Step 6 (pagination) — `ListMessage` returns a single `ListMessageResponse` with no auto-pager; how to iterate `page`/`pageToken` (or follow `NextPageUri`) to cover the WHOLE date range without gaps or overlaps is not shown by the signature. Stopping at page one silently truncates the reconciliation report. **MUST load `dotnet-configuration-resilience`** before writing the pagination loop.

> ⚠ All steps (error boundary) — every op here is **Case B** (`SdkException<RawError>`), throw-only, with no `…Result` no-throw variant; how to read the HTTP status and provider error body safely, and which JSON failures never surface as `SdkException`, is not shown by the accessor list. **MUST load `dotnet-error-handling`** before writing any try/catch (see REQUIRED READING for the two JsonException hazards).

---

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Client construction, `HttpClient` ownership/lifetime, DI registration (`AddTwilioSdkClient`) |
| `dotnet-authentication` | Setting `AccountSidAuthToken` Basic credentials from config |
| `dotnet-calling-endpoints` | Calling `CreateMessage`/`ListMessage` with named args; must-pass-explicitly optionals |
| `dotnet-models` | `StringEnum<T>` enums, building request records, reading response records |
| `dotnet-configuration-resilience` | Base-URL/server selection per host, retries/timeouts (duplicate-send risk), pagination |
| `dotnet-error-handling` | The Case-B `SdkException<RawError>` boundary; reading status + provider body |
| `dotnet-testing` | Faking the `HttpClient` seam when testing the integration |

**Error-boundary hazards (`System.Text.Json.JsonException` reaches the boundary from two directions — handle them oppositely):**
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. (These belong in this first sheet: the boundary is written early.)

---

## 5. Assumptions & Blockers

- **Assumption**: "sent from a configured From number (and/or MessagingServiceSid)" (cap. 1) means the implementer supplies `from` (from `Twilio:FromNumber`) and/or `messagingServiceSid` (from `Twilio:MessagingServiceSid`) on `CreateMessage`; the map does not encode which is mandatory, and Twilio requires at least one — validate at the app layer.
- **Assumption**: scheduling (cap. 2) uses a Messaging Service; the map documents `scheduleType`/`sendAt` but not the provider constraint that scheduling requires `messagingServiceSid` and a send-at window — treat `Twilio:MessagingServiceSid` as required for scheduled sends. The exact allowed send-at window (min/max lead time) is provider-enforced and **UNVERIFIED** from the map.
- **Assumption**: redaction (cap. 6) is achieved by `UpdateMessage` with `body=""` (record + status survive) per the operation note; deletion (`DeleteMessage`) removes the whole resource. Both are available — the implementer chooses redact vs delete per requirement.
- **No blockers.** All eight capabilities are grounded in the SDK map; capability 8 being app-layer is a normal answer, not a gap.
</content>
</invoke>
