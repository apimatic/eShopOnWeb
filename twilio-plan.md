# Twilio .NET SDK integration plan — eShopOnWeb (`src/PublicApi` + `src/ApplicationCore` + `src/Infrastructure`)

SDK: `AsadAli.TwilioSdk` (NuGet) · root namespace `TwilioSdk` · client `TwilioSdkClient` · options `TwilioSdkClientOptions`.
Install **version-less** so it floats to the latest release (the map pins no version):

```bash
dotnet add package AsadAli.TwilioSdk
```

Add it to `src/Infrastructure` (the project whose code references SDK types). Keep SDK types out of `src/ApplicationCore` (abstractions/DTOs only) and out of the PublicApi contracts.

## 1. Scope & sequence

1. **Install** `AsadAli.TwilioSdk` into `src/Infrastructure`.
2. **Options** — `TwilioOptions` POCO bound from the `"Twilio:"` section (`AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`, `BaseUrl` optional), validated at startup.
3. **Client registration & auth** — register `TwilioSdkClient` with `AccountSidAuthToken` basic credentials; apply `BaseUrl` override to the **messaging API server slot only** (`Server.Default`); Lookups stays on its own default host. (Op group: client construction.)
4. **ApplicationCore abstraction** — e.g. `ISmsService` + plain DTOs (send result, validation result, message status, reconciliation item). No `TwilioSdk.*` types leak.
5. **Infrastructure implementation** — `TwilioSmsService` covering: send SMS (step uses `CreateMessage`), validate number (`FetchPhoneNumber3`), schedule (`CreateMessage` + `scheduleType`/`sendAt`), cancel scheduled (`UpdateMessage` status), read status (`FetchMessage`), re-send (fresh `CreateMessage`), redact body (`UpdateMessage` body), list for reconciliation (`ListMessage`).
6. **PublicApi endpoints** — JWT-authenticated minimal-API endpoints calling the abstraction.
7. **Error boundary** — one translation layer from SDK exceptions to app results/HTTP responses.
8. **Tests** — integration-layer tests via the SDK's test seam.

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

**All nullable parameters below have no C# default — they must be passed explicitly (pass `null` to skip). Use named arguments.**

### Operations

| # | Feature | Controller property · signature (verbatim) | Request fields (wire ← C#) | Response envelope / fields read | Error case | Pagination |
|---|---|---|---|---|---|---|
| 1 | Send SMS | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `To`←`to` (required), `From`←`from`, `MessagingServiceSid`←`messagingServiceSid`, `Body`←`body`; all 24 nullable params pass explicitly | `ApiV2010AccountMessage` (flat record, **no wrapper**). Read: `Sid (sid): string?`, `Status (status): MessageEnumStatus?`, `From (from): string?`, `To (to): string?`, `MessagingServiceSid (messaging_service_sid): string?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateCreated/DateSent/DateUpdated: string?` | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none |
| 2 | Validate number | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path: `{PhoneNumber}`←`phoneNumber`; all 15 query params nullable — pass `null` for a plain validity lookup | `LookupResponse` (flat). Read: `Valid (valid): bool?`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`, `PhoneNumber (phone_number): string?` ← **canonical form to store**, `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?` | **Case B** `SdkException<RawError>` | none |
| 3 | Schedule message | Same `CreateMessage` as #1, with `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset>`, `messagingServiceSid: <MessagingServiceSid>` | `ScheduleType`←`scheduleType` (wire value `fixed`), `SendAt`←`sendAt` | Same `ApiV2010AccountMessage`; capture `Sid`; expected initial `Status` = `MessageEnumStatus.Scheduled` (wire `scheduled`) | **Case B** | none |
| 4 | Cancel scheduled | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` must be passed explicitly | `Status`←`status` = `MessageEnumUpdateStatus.Canceled` (wire `canceled`); pass `body: null` | `ApiV2010AccountMessage` | **Case B** | none |
| 5 | Read status | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path `{Sid}`←`sid` | `ApiV2010AccountMessage` — `Status`, `ErrorCode`, `ErrorMessage`, `DateSent` | **Case B** | none |
| 6 | Re-send | Fresh `CreateMessage` (same `to`/`body`/`from`) — new `Sid` returned; no idempotency-key parameter exists in the signature | as #1 | as #1 | **Case B** | none |
| 7 | Redact body | `UpdateMessage(accountSid, sid, body: "", status: null)` — empty string erases the body text; the record and its status survive. **Do NOT use `DeleteMessage`** — it deletes the whole message record | `Body`←`body` = `""` | `ApiV2010AccountMessage` | **Case B** | none |
| 8 | List for reconciliation | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `From`←`from` = configured FromNumber (**server-side sender filter**), `To`←`to`, `DateSent`←`dateSent` (exact match), `DateSent<`←`dateSentQuery` (strictly before), `DateSent>`←`dateSentQueryQuery` (strictly after), `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken` | `ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `Page (page): int?`, `PageSize (page_size): int?`, `FirstPageUri`, `Start`, `End`, `Uri` | **Case B** | **No built-in pager** (map: "none — only `page`, no `perPage`"). Loop pages manually: `pageSize` + incrementing `page` (or `pageToken`), continue while `NextPageUri` is non-null |

Map citations: `operations/Api20100401Message.md` (rows 1,3–8), `operations/LookupsV2PhoneNumber.md` (row 2), `records-1-Ac-Ca.md` (`ApiV2010AccountMessage`), `records-4-Li-Me.md` (`ListMessageResponse`, `LookupResponse`), `models/enums.md` (all enums below).

### Key contract decisions

- **From vs MessagingServiceSid (send):** `CreateMessage` carries both `from` and `messagingServiceSid` as independent nullable wire fields (`From`, `MessagingServiceSid`). For ordinary sends that must be attributable to the configured FromNumber, pass `from: <FromNumber>` and `messagingServiceSid: null` — reconciliation (#8) filters server-side on `From`, and the message record's `From (from)` field is what that filter matches. Whether the provider accepts or rejects a request carrying **both** is not stated in the map or SDK source (`UNVERIFIED`) → defensive rule: **pass exactly one sender identity per call, never both.**
- **Scheduling:** map-grounded via the `MessageEnumScheduleType` enum doc — *"For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter in order to schedule a Message."* So a scheduled send **requires `messagingServiceSid`** (a `from`-only send cannot be scheduled), plus `scheduleType: MessageEnumScheduleType.Fixed` and `sendAt` (`DateTimeOffset?`, wire `SendAt`). Consequence for reconciliation: a scheduled message's sender is chosen by the messaging service, so reconcile scheduled sends by the `Sid` captured at create time and by the record's actual `From` field — do not assume it equals the configured FromNumber. **Min/max lead-time constraints appear nowhere in the map or SDK source (`UNVERIFIED`)** — the SDK does no client-side window validation; the provider rejects out-of-window `sendAt` values through the Case-B error path. Defensive rule: validate the scheduling window app-side against current provider docs and surface the provider's error body (`RawError.ReadAsString()`) to the caller on rejection.
- **Redaction:** `UpdateMessage` with `body: ""`, `status: null`. The operation's own map note: *"used to redact Message `body` text and to cancel not-yet-sent messages."* `DeleteMessage(accountSid, sid)` exists but deletes the entire record — wrong tool for redaction.
- **Cancel:** `UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled` (the enum's only value), `body: null`. Per the map note this cancels "not-yet-sent" messages; cancelling an already-sent message is a provider rejection surfaced via Case B.
- **List filter semantics:** `From` is a server-side sender filter (map: *"Filter by sender"*). `DateSent` = exact match; `DateSent<` / `DateSent>` = strict inequalities per the wire operators (note the generated C# names: `dateSentQuery`→`DateSent<`, `dateSentQueryQuery`→`DateSent>`). All three are `DateTimeOffset?`. Whether the provider compares at day or sub-second granularity is not stated in map/source (`UNVERIFIED`) → defensive rule: bracket an inclusive calendar range as `dateSentQueryQuery: rangeStartUtc` (`DateSent>`) and `dateSentQuery: rangeEndUtcPlusOneDay` (`DateSent<`), and confirm boundary behavior against live traffic before relying on it.

### Enum values (verbatim from `models/enums.md`; enums are `StringEnum<T>`, **not** C# enums — use the static members, e.g. `MessageEnumStatus.Delivered`)

| Enum (namespace `TwilioSdk.Models.Enums`) | Members (wire value) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `MessageEnumDirection` (on the record; read-only here) | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |

**Status terminality:** the map/source carry the value list but no terminal/failure classification (`UNVERIFIED`). Working classification to encode behind one helper, confirmed against live behavior: terminal-failure = `Failed`, `Undelivered`, `Canceled`; terminal-success = `Delivered`, `Read`; non-terminal = `Accepted`, `Scheduled`, `Queued`, `Sending`, `Sent`, `Receiving`, `Received`, `PartiallyDelivered`. Defensive rule: match on the static members and treat any unrecognized status as **non-terminal** so a new provider value never gets misreported as a final failure.

### Client construction, auth, servers, DI

- Constructor: `TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdkClientOptions options)` (root namespace `TwilioSdk`).
- `TwilioSdkClientOptions` (`TwilioSdk`) properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?`.
- Auth: `AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }` — `BasicAuthCredentials` is `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials`, `Username`/`Password` are `required string` init-only. (Source XML doc: an API key + secret is the preferred username/password; account SID + auth token also works.)
- Environment: `ServerEnvironment.Production` (`TwilioSdk.Servers`) — the only member; it is the default.
- **BaseUrl override (messaging API only):** `options.Server` is `TwilioSdk.ServerOptions` with one slot per server (`Default` … `Default14`). The messaging API operations (`Api20100401Message.*`) resolve through slot **`Default`** → set `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (type `TwilioSdk.Servers.DefaultOptions`, default `"https://api.twilio.com"`). The Lookups API resolves through slot **`Default4`** (default `"https://lookups.twilio.com"`) — leave it untouched, so the override affects the messaging API verbatim and nothing else. Only set the property when `BaseUrl` is configured; otherwise keep the provider default.
- DI: `services.AddTwilioSdkClient(o => { /* credentials, environment, Server.Default override */ })` — extension in `TwilioSdk.ServiceCollectionExtensions` (root namespace). It registers the client as a **singleton** built on an `IHttpClientFactory`-created `HttpClient`; the `configure` callback runs once at registration time.
- Every in-scope operation's first parameter is `accountSid` (path `{AccountSid}`) — supply the configured AccountSid on every call.
- `RequestOptions` (last-but-one parameter, always optional) is `TwilioSdk.Core.RequestOptions` with a single member `LogLevel: LogLevel?` — per-call log-level override only; safe to omit.

### Error model (all in-scope operations)

- All 6 in-scope operations are **Case B**: they throw `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. There are **no typed `{Operation}Error` accessors** for these operations.
- Read failures via `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`.
- "Not a usable destination" (validation, #2) can surface two ways and the boundary must handle both: a **2xx** `LookupResponse` with `Valid == false` and/or non-empty `ValidationErrors`, **or** a thrown `SdkException<RawError>` (inspect `StatusCode`, e.g. 404). Which statuses the provider returns for which invalid-input shapes is not fixed by map/source (`UNVERIFIED`) → treat both paths as "not usable".
- No-throw `…Result` variants: **absent** across this SDK — every call is throw-only.

## 3. Trap notes

> ⚠ Step 3 (client registration) — the SDK's DI helper builds the client over an `IHttpClientFactory` `HttpClient`, and the lifetime split between the long-lived handler pipeline and the SDK client wrapper is not visible from any signature; getting it wrong socket-starves the app or strands config. **MUST load `dotnet-client-initialization`** before wiring registration.

> ⚠ Step 3 (auth) — credentials must be in place before the client is constructed (the DI `configure` callback runs once, at registration), and secrets must come from configuration, not code. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

> ⚠ Steps 5–6 (every call) — the nullable parameters have **no C# defaults**: positional calls mis-bind, and omitted arguments don't compile. Whether you pass explicit `null`s or named arguments changes the call shape. **MUST load `dotnet-calling-endpoints`** before writing the first call.

> ⚠ Step 5 (models) — the enums above are `StringEnum<T>`, not C# enums: construction, equality, and `switch` behave differently than expected, and unmodeled JSON fields are silently dropped on deserialize (a drifted wire payload loses data without error). **MUST load `dotnet-models`** before mapping `ApiV2010AccountMessage`/`LookupResponse` onto app DTOs.

> ⚠ Step 7 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`); there are no typed accessors here, and `TryGetRawError` is not a catch-all. What a transport failure vs. a provider error vs. a deserialization failure each throw is not uniform. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 5 (sends/retries) — `CreateMessage` is a non-idempotent `POST`: whether a failed send can be re-executed by the retry pipeline (a duplicate customer-facing SMS), and what `RetryOptions.Timeout` actually bounds, are not what the option names suggest. **MUST load `dotnet-configuration-resilience`** before tuning `Retry`/`Timeout` or relying on defaults for sends.

> ⚠ Step 8 (tests) — the SDK's test seam is a specific constructor argument, not an interface; faking the wrong seam couples tests to generated internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — step 3: client construction, HttpClient ownership, DI registration.
- `dotnet-authentication` — step 3: credentials wiring.
- `dotnet-calling-endpoints` — steps 5–6: calling every operation (named arguments, explicit nulls, `ct:`).
- `dotnet-models` — step 5: records, `StringEnum<T>` enums, wire-name mapping.
- `dotnet-error-handling` — step 7: the exception boundary (mandatory — see the two hazards below).
- `dotnet-configuration-resilience` — step 5: retries/timeouts for non-idempotent sends, base-URL, manual pagination.
- `dotnet-testing` — step 8: faking the SDK in tests.

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- Assumed eShopOnWeb layering per the brief: endpoints in `src/PublicApi`, abstractions/DTOs in `src/ApplicationCore`, SDK-touching implementation in `src/Infrastructure`. The repo was not surveyed (planning only).
- Assumed the validation feature uses Lookups **v2** (`FetchPhoneNumber3` → `LookupResponse.Valid`), which is the operation whose response models validity directly; a v1 lookup (`LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber`) also exists but returns no `Valid` flag.
- `UNVERIFIED` items (provider behavior not fixed by map/SDK source): scheduling lead-time window; whether `From` + `MessagingServiceSid` may coexist on one send; status terminality classification; `DateSent` comparison granularity; which invalid-input shapes return 404 vs 200-with-`Valid:false`. Each carries a defensive directive above.
- No blockers.
