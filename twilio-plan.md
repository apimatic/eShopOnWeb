# Twilio .NET SDK — Order-Notification SMS integration plan (eShopOnWeb)

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace `TwilioSdk`. Client `TwilioSdkClient`, options `TwilioSdkClientOptions`. Map source commit `51fdf48`.

Every fact below is grounded in the bundled SDK map (page cited per row); the base-URL override shape and the auth/DI/exception namespaces were confirmed from the named SDK source files where the map does not carry the body.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `TwilioSdkClient` via `AddTwilioSdkClient(...)` (or construct `new TwilioSdkClient(httpClient, options)`), bind the `Twilio:` config section, set auth, and apply `Twilio:BaseUrl` to the messaging (`api`) server node only.
2. **Auth** — set `options.AccountSidAuthToken` (Basic auth) from `Twilio:AccountSid` / `Twilio:AuthToken`.
3. **Send SMS** (capability 1) — `client.Api20100401Message.CreateMessage(...)`, using `Twilio:FromNumber` and/or `Twilio:MessagingServiceSid`.
4. **Validate destination + canonical E.164** (capability 2) — `client.LookupsV2PhoneNumber.FetchPhoneNumber3(...)`. NOTE: different host — see §Base-URL below.
5. **Fetch delivery status** (capability 3) — `client.Api20100401Message.FetchMessage(...)`.
6. **Schedule follow-up** (capability 4) — `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`.
7. **Cancel scheduled** (capability 5) — `client.Api20100401Message.UpdateMessage(...)` with `status = Canceled`.
8. **Redact body** (capability 6) — `UpdateMessage(...)` with `body = ""`.
9. **Reconciliation list** (capability 7) — `client.Api20100401Message.ListMessage(...)` filtered by `from` + date range.
10. **Error boundary** — one `catch (SdkException<RawError>)` around every SDK call (all in-scope ops are Case B).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Namespaces (using-directives) for every type in scope

| Type | Namespace | Source path |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`, `Server` | `TwilioSdk` | repo root |
| `AddTwilioSdkClient` (extension on `IServiceCollection`) | `TwilioSdk` | `ServiceCollectionExtensions.cs` |
| Controllers `Api20100401Message`, `LookupsV2PhoneNumber` (property types) | `TwilioSdk.Api` | `Api/` (accessed via `client.X`, no `using` needed to call) |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` | `Models/` |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection` | `TwilioSdk.Models.Enums` | `Models/Enums/` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `SdkException<TError>` | `TwilioSdk.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | `Core/ErrorResponse/RawError.cs` |
| `ServerEnvironment` | `TwilioSdk.Servers` | `Servers/ServerEnvironment.cs` |
| `DefaultOptions.ProductionOptions` (reached via `options.Server.Default.Production`) | `TwilioSdk.Servers` | `Servers/DefaultOptions.cs` (no `using` needed — reached by member access) |
| `RetryOptions` | `TwilioSdk.Core.Configuration` | `Core/Configuration/RetryOptions.cs` |

### Operations

Map page for all Message rows: `operations/Api20100401Message.md`. Map page for Lookup: `operations/LookupsV2PhoneNumber.md`.

All in-scope operations are **async, throw-based, Case B** (`SdkException<RawError>` — no typed error accessors), return `Task<...>`, and take a trailing `RequestOptions? requestOptions = null, CancellationToken ct = default`. **No `…Result` no-throw variant exists on any of them.** `accountSid` = `Twilio:AccountSid`.

| # | Op (controller.method) | Full signature (params in order) | Request wire names | Returns / inner fields read | Error |
|---|---|---|---|---|---|
| 1,4 | `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 24 params `statusCallback`…`contentSid` are nullable-with-no-default and **must be passed explicitly** (pass `null` to skip; use named args). | `To`←`to`, `From`←`from`, `MessagingServiceSid`←`messagingServiceSid`, `Body`←`body`, `ScheduleType`←`scheduleType`, `SendAt`←`sendAt` (+ others) | `ApiV2010AccountMessage`: `.Sid` (message SID), `.Status` (`MessageEnumStatus?`) | Case B |
| 3,5 | `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path `{Sid}`←`sid` | `ApiV2010AccountMessage`: `.Status`, `.Sid`, `.DateSent`, `.From`, `.To`, `.Body` | Case B |
| 5,6 | `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` are nullable-no-default, **pass both explicitly**. | `Body`←`body`, `Status`←`status` | `ApiV2010AccountMessage` (updated resource) | Case B |
| 7 | `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 params `to`…`pageToken` are nullable-no-default, **pass explicitly** (named args). | see date-filter note below | `ListMessageResponse` envelope → `.Messages` (`IReadOnlyList<ApiV2010AccountMessage>?`) + paging fields | Case B |
| 2 | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 15 params `fields`…`partnerSubId` are nullable-no-default, **pass explicitly** (pass `null` to skip). | path `{PhoneNumber}`←`phoneNumber` | `LookupResponse`: `.PhoneNumber` (canonical **E.164**, wire `phone_number`), `.Valid` (`bool?`), `.ValidationErrors`, `.NationalFormat`, `.CountryCode` | Case B |

### Response model — `ApiV2010AccountMessage` (map: `records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`)

Exact accessors for the fields the integration reads (all fields are nullable / `init`-only):

| Concept | Accessor | Type | Wire name |
|---|---|---|---|
| Message SID | `.Sid` | `string?` | `sid` |
| Delivery status | `.Status` | `MessageEnumStatus?` | `status` |
| Date sent | `.DateSent` | `string?` (NOT a `DateTime` — raw RFC-2822 string) | `date_sent` |
| From | `.From` | `string?` | `from` |
| To | `.To` | `string?` | `to` |
| Body | `.Body` | `string?` | `body` |
| Direction | `.Direction` | `MessageEnumDirection?` | `direction` |
| Error code / message (provider) | `.ErrorCode` / `.ErrorMessage` | `int?` / `string?` | `error_code` / `error_message` |
| Messaging service SID | `.MessagingServiceSid` | `string?` | `messaging_service_sid` |

### Response envelope — `ListMessageResponse` (map: `records-4-Li-Me.md`, `Models/ListMessageResponse.cs`)

Payload is wrapped — the messages live one level down in `.Messages`:
`End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, **`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`**.

### Response model — `LookupResponse` (map: `records-4-Li-Me.md`, `Models/LookupResponse.cs`)

`CallingCountryCode (calling_country_code): string?`, `CountryCode (country_code): string?`, **`PhoneNumber (phone_number): string?`** (provider's canonical E.164), `NationalFormat (national_format): string?`, **`Valid (valid): bool?`**, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`, plus optional enrichment objects (`CallerName`, `LineTypeIntelligence`, etc. — all nullable, not requested for send/read unless `fields` is set).

### ListMessage date-range filter — READ CAREFULLY (the C# param names are misleadingly ordered)

The three date params map to these wire names (map: `operations/Api20100401Message.md`):

| C# param | Wire name | Meaning |
|---|---|---|
| `dateSent` | `DateSent` | exact-day equality |
| `dateSentQuery` | `DateSent<` | on/before — the **upper** bound (range "to") |
| `dateSentQueryQuery` | `DateSent>` | on/after — the **lower** bound (range "from") |

So for a range **from ≤ DateSent ≤ to**: pass `dateSentQueryQuery: <from>` (lower / `DateSent>`) and `dateSentQuery: <to>` (upper / `DateSent<`), and `dateSent: null`. Filter `from:` = `Twilio:FromNumber`, `to: null`. All three are `DateTimeOffset?`. This filtering is **server-side** (the provider filters), satisfying the reconciliation requirement — do NOT fetch broad and filter client-side.

### Pagination for ListMessage

Map row: **Pagination: none (only `page`, no `perPage`)** — there is no auto-paging helper and no `…Result` iterator. Page manually: set `pageSize` (`long?`), then either increment `page` (`int?`) or follow `.NextPageUri` / pass `.NextPageToken` via `pageToken`. Loop until `.Messages` is empty or `.NextPageUri` is null. (See resilience trap note — pagination mechanics.)

### Enum value tables (map: `models/enums.md`)

**`MessageEnumStatus`** (`StringEnum`, wire values in parens) — read on send and on fetch:
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumScheduleType`** — `Fixed (fixed)` (the only member; required for scheduling).

**`MessageEnumUpdateStatus`** — `Canceled (canceled)` (the only member; used by `UpdateMessage` to cancel).

**`MessageEnumDirection`** — `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

> Enums are `StringEnum<T>`, NOT C# enums — build with the static member (`MessageEnumScheduleType.Fixed`, `MessageEnumUpdateStatus.Canceled`) or `Type.FromValue("wire")`, and compare with `==`. See `dotnet-models`.

### Per-capability construction notes (contract facts)

- **Cap 1 — Send:** `To`←`to` (destination). Sender is EITHER `from` (= `Twilio:FromNumber`) OR `messagingServiceSid` (= `Twilio:MessagingServiceSid`) — both are separate string params on `CreateMessage`; the SDK exposes both shapes. Text is `body`. Read back `.Sid` and `.Status`.
- **Cap 4 — Schedule:** set `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset in future>`, and `messagingServiceSid: Twilio:MessagingServiceSid`. `from` must be `null` when scheduling. (Whether the provider actually accepts the send-at window, and requires a Messaging Service rather than a plain From, is a provider-side rule the SDK types do not encode — see Assumptions/UNVERIFIED.)
- **Cap 5 — Cancel:** `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` — transitions `Status` to `canceled`. Only a not-yet-sent (scheduled) message can be canceled; a message already sent will be rejected by the provider (Case B error) — do not assume success.
- **Cap 6 — Redact:** `UpdateMessage(accountSid, sid, body: "", status: null)` — sends an empty `Body`, which redacts the stored content at the provider while the message record + status survive. Pass empty string `""`, not `null` (null skips the field).

### Client construction / auth / server override (confirmed from source)

**Construct / register.** DI extension `services.AddTwilioSdkClient(o => { ... })` (source `ServiceCollectionExtensions.cs`) registers `TwilioSdkClient` as a **singleton**, resolving the `HttpClient` from `IHttpClientFactory` (`services.AddHttpClient()` is called for you). Manual alternative: `new TwilioSdkClient(httpClient, options)` where `httpClient` is a long-lived `System.Net.Http.HttpClient`. HttpClient ownership: factory-managed and long-lived — do not create/dispose one per request. (See resilience trap note.)

**Auth (Basic).** `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> };` — `BasicAuthCredentials` has `required string Username` and `required string Password` (source `Core/Authentication/Basic/BasicAuthCredentials.cs`). Set before/at construction. (Per the source XML doc an API-key SID/secret may be used as username/password instead; account SID + auth token is fine here.)

**Environment.** `options.Environment` = `ServerEnvironment.Production` (the only member; source `Servers/ServerEnvironment.cs`). It is a `StringEnum`, not a C# enum.

**Base-URL override for `Twilio:BaseUrl` (confirmed from source `ServerOptions.cs` + `Servers/DefaultOptions.cs`).** The SDK holds a per-server-group base URL under `options.Server` (`ServerOptions`), one node per API group. The **messaging (2010-04-01) API — the `Default (api)` server — is `options.Server.Default`**, whose default base URL is `https://api.twilio.com`. Set it verbatim when `Twilio:BaseUrl` is present:

```
options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>;   // messaging: send/fetch/update/list
```

This one node governs CreateMessage, FetchMessage, UpdateMessage and ListMessage (all `Default (api)`), satisfying "used verbatim for EVERY messaging-API call."

**Lookup is a DIFFERENT host — do NOT apply `Twilio:BaseUrl` to it.** `FetchPhoneNumber3` runs on the `Default4 (lookups)` server = `options.Server.Default4`, whose base URL is `https://lookups.twilio.com` (source `Servers/Default4Options.cs`). Since `Twilio:BaseUrl` governs only the messaging API, leave `options.Server.Default4` at its default. If the integration ever needs to point Lookup elsewhere it is a separate knob (`options.Server.Default4.Production.BaseUrl`) — but that is out of scope for `Twilio:BaseUrl`.

### Error boundary shape (all in-scope ops are Case B)

```csharp
try { /* SDK call */ }
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;        // System.Net.HttpStatusCode
    var body   = ex.Error.ReadAsString();    // raw provider JSON body
    // provider error code/message live inside that JSON (Twilio: code/message/more_info) — extract best-effort, fall back to a generic message
}
```

There is **no typed error class and no `TryGet…` accessors** for these operations — the only way to the provider's error `code`/`message` is to read the raw body (`ReadAsString()` or `ReadAsJson<T>()`). `RawError` members: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.

---

## 3. Trap notes (load the named skill before writing that step — the note names the hazard, not the answer)

⚠ Step 1 (client & DI) — whether the SDK client may be a singleton, how the `HttpClient`/handler pipeline must be owned and reused, and whether the wrapper is safe to hold long-lived, is not decidable from the constructor signature alone. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ Step 1 (base URL & resilience) — the SDK's retry/timeout options do not bound a whole call the way they read, `HttpMethodsToRetry` does not gate every retry path, and pagination has no built-in helper; what `Timeout` actually bounds and which failures re-send a non-idempotent `CreateMessage` is not visible in the option names. **MUST load `dotnet-configuration-resilience`** before setting retries/timeouts/base URL or writing the ListMessage paging loop.

⚠ Step 2 (auth) — when credentials must be set relative to client construction, and how to source them from configuration rather than hardcoding, is a usage rule the property type does not show. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 3–9 (calling ops) — these ops have many nullable-no-default params that mis-bind in a positional call; the correct calling discipline (named args, which optionals to leave `null`) is not enforced by the compiler. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 3–9 (models & enums) — `StringEnum<T>` is not a C# enum, response fields are `init`-only and nullable, and unmodeled JSON is dropped on deserialize; how to build enum values and map these models safely onto domain types is not shown by the field list. **MUST load `dotnet-models`** before constructing requests or mapping responses.

⚠ Step 10 (error boundary) — which exception types actually reach the catch, and why an SDK-exception-only ladder is silently incomplete, is not derivable from the "Case B" label alone. **MUST load `dotnet-error-handling`** before writing the try/catch (see REQUIRED READING for the two `JsonException` hazards).

⚠ Step (tests) — the fake seam is the `HttpClient` constructor argument, and asserting real behaviour rather than execution is a technique the signatures do not reveal. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, DI registration, HttpClient ownership/lifetime |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, base-URL/server selection, ListMessage pagination |
| `dotnet-authentication` | Step 2 — Basic-auth credential wiring, sourcing secrets from config |
| `dotnet-calling-endpoints` | Steps 3–9 — named-arg calling, required vs optional params, async/cancellation |
| `dotnet-models` | Steps 3–9 — `StringEnum` values, required/nullable members, wire vs C# names |
| `dotnet-error-handling` | Step 10 — which exceptions reach the catch, reading status/body safely |
| `dotnet-testing` | Tests — faking the `HttpClient` seam, covering error/edge paths |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary (this SDK is APIMatic-generated; these reach the boundary from two directions and need opposite handling):**

- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- `Twilio:AccountSid` is used both as the Basic-auth username AND as the `accountSid` path argument to every messaging op. (The map's auth doc allows an API-key SID/secret instead; the config keys given imply account SID + auth token, so that pairing is assumed.)
- Send (cap 1) uses `Twilio:FromNumber` as `from` for immediate sends and `Twilio:MessagingServiceSid` for scheduled sends (cap 4), since the provider requires a Messaging Service to schedule. Both are supported SDK params; the integration chooses which to pass per call. If immediate sends should instead go through the Messaging Service, that is a one-line swap (`from: null, messagingServiceSid: ...`).
- Reconciliation (cap 7) date range is treated as `from ≤ DateSent ≤ to` inclusive, mapped to `dateSentQueryQuery` (lower) / `dateSentQuery` (upper) per the wire-name table.
- `Twilio:BaseUrl`, when set, is a full origin (scheme + host, e.g. `https://api.twilio.com`) suitable for `ProductionOptions.BaseUrl`; it is applied only to `options.Server.Default` (messaging).

**UNVERIFIED (only live traffic can confirm — code defensively, do not assert)**
- The scheduling window constraints (minimum lead time and maximum future horizon for `sendAt`, and the requirement that scheduling go through a Messaging Service) are **provider-enforced business rules not encoded in the SDK types**. Do not pre-validate against a hardcoded window from memory; instead send and treat a Case B rejection as "schedule refused" — extract the provider `message` best-effort from `ex.Error.ReadAsString()`, fall back to a generic message. `UNVERIFIED`.
- That the live wire JSON for an error body actually contains Twilio's `code`/`message`/`more_info` fields (used to surface a provider error code/message) is a live-traffic fact — parse best-effort from the raw body and fall back to the generic HTTP status message if absent. `UNVERIFIED`.
- Cancel (cap 5) preconditions — that only a not-yet-sent message transitions to `canceled` — are enforced by the provider, not the SDK; handle a Case B rejection rather than assuming the transition always succeeds. `UNVERIFIED`.

**Gaps / not exposed by the SDK**
- None blocking. All seven capabilities are exposed:
  - Phone validation + canonical E.164 (cap 2) IS exposed via `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (`LookupResponse.Valid` + `.PhoneNumber`), but on a **separate host** (`lookups.twilio.com`, `options.Server.Default4`) that `Twilio:BaseUrl` does NOT govern — flagged explicitly in the sheet.

**Blockers**
- None.

---

*Every row above cites its map page (`operations/Api20100401Message.md`, `operations/LookupsV2PhoneNumber.md`, `records-1-Ac-Ca.md`, `records-4-Li-Me.md`, `models/enums.md`) or the named SDK source file for the source-confirmed base-URL/auth/DI/exception facts.*
