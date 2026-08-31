# Twilio .NET SDK integration plan — eShopOnWeb `src/PublicApi` SMS order notifications

**SDK**: NuGet `AsadAli.TwilioSdk` — install version-less (`dotnet add package AsadAli.TwilioSdk`), floats to latest. Root namespace `TwilioSdk`; SDK targets `netstandard2.0` (fine on .NET 8). Map provenance: source commit `51fdf48` (`sdk-map.md`).

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Add package `AsadAli.TwilioSdk` to `src/PublicApi`; bind a `TwilioOptions` POCO from the `Twilio:` config section (`AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`, `BaseUrl`) | — |
| 2 | Register the SDK client in DI (`AddTwilioSdkClient`), set basic-auth credentials, and apply the optional messaging-only base-URL override | — (client construction) |
| 3 | `POST /api/contact-numbers`: validate the shopper's number via Lookup v2, reject unusable numbers, persist Twilio's canonical E.164 form | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 4 | Send immediate SMS (order confirmation) from `FromNumber` or `MessagingServiceSid` | `Api20100401Message.CreateMessage` |
| 5 | On order dispatch: schedule the follow-up SMS (~3 days out) via `scheduleType` + `sendAt` (requires `messagingServiceSid`) | `Api20100401Message.CreateMessage` |
| 6 | On order cancellation: cancel the not-yet-sent scheduled message | `Api20100401Message.FetchMessage` (pre-check) + `Api20100401Message.UpdateMessage` |
| 7 | Delivery-status polling (no webhooks): fetch current status by message SID | `Api20100401Message.FetchMessage` |
| 8 | Redact message body after completion (keep the record + outcome); hard-delete only if the whole record must go | `Api20100401Message.UpdateMessage` (or `DeleteMessage`) |
| 9 | Reconciliation: list messages filtered by `from` = configured `Twilio:FromNumber` and a `DateSent` range, paging through the full range | `Api20100401Message.ListMessage` |
| 10 | Error boundary around all SDK calls; tests via the SDK's HTTP seam | all above |

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

**Namespaces needed** (`sdk-map.md` *Namespaces* + source rows below):

| Types | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `ServerEnvironment` (`Production`), `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` |
| Controllers (`client.Api20100401Message`, `client.LookupsV2PhoneNumber`) | `TwilioSdk.Api` (accessed via client properties — no direct construction) |
| Records (`ApiV2010AccountMessage`, `LookupResponse`, `ListMessageResponse`) | `TwilioSdk.Models` |
| Enums (`MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError`, …) | `TwilioSdk.Models.Enums` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |
| `RequestOptions` (optional trailing param everywhere; pass nothing) | `TwilioSdk.Core.Request` |

### Client construction, auth, servers (map: `sdk-map.md` *Getting a client* / *Servers & auth*; source rows: `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`)

- `TwilioSdkClientOptions` properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `AccountSidAuthToken: BasicAuthCredentials?`.
- Constructor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`. DI: `services.AddTwilioSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`; registers the client as a singleton over `IHttpClientFactory` and pulls `ILoggerFactory` from DI).
- Auth: `o.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> };` — both members are `required string` (init-only). (API-key-as-username is the provider-preferred scheme per the SDK's own docs; account SID + auth token works.)
- Environment: `o.Environment = ServerEnvironment.Production` (only member).
- **Messaging-only base-URL override** (the `Twilio:BaseUrl` requirement): every messaging-API operation below runs on server node **`Default` (api.twilio.com)**; Lookup runs on **`Default4` (lookups.twilio.com)**. `ServerOptions` has one property per node (`Default` … `Default14`), each with `.Production.BaseUrl`. So:
  `o.Server.Default.Production.BaseUrl = twilioOptions.BaseUrl;` (only when `BaseUrl` is set — used verbatim) — this retargets **only** the messaging API. `o.Server.Default4` is left untouched, so Lookup keeps hitting its default `https://lookups.twilio.com`.

### Operation rows

#### Step 3 — Lookup: `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (map: `operations/LookupsV2PhoneNumber.md`)

- HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` — server `Default4 (lookups)`.
- Signature: `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<LookupResponse>`.
  - The 15 params `fields` … `partnerSubId` are nullable with **no C# default — must be passed explicitly** (pass `null`). For basic validation pass all `null` (optionally `countryCode` as a hint when the shopper typed a national-format number).
  - `phoneNumber` is the path parameter — the number as typed.
- Response `LookupResponse` (`records-4-Li-Me.md`) — fields the integration reads:
  - `Valid (valid): bool?` — **the usability verdict**. Treat anything other than `true` (including `null`) as "not a usable destination" → reject registration.
  - `PhoneNumber (phone_number): string?` — **canonical E.164 form; persist this**, not the caller's input.
  - `NationalFormat (national_format): string?`, `CallingCountryCode (calling_country_code): string?`, `CountryCode (country_code): string?` — display/logging only.
  - `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` — reasons when invalid; enum `ValidationError` values: `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` (`models/enums.md`).
  - Rich add-on fields (`CallerName`, `SimSwap`, `LineTypeIntelligence`, …) exist on the record but are only populated when requested via `fields` — not needed here.
- Error: **Case B** — `SdkException<RawError>`; accessors `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`. No `…Result` no-throw variant (none exist in this SDK).

#### Steps 4 & 5 — Send (immediate & scheduled): `client.Api20100401Message.CreateMessage` (map: `operations/Api20100401Message.md`)

- HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` — server `Default (api)`.
- Signature: `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ApiV2010AccountMessage>`.
  - The 24 params `statusCallback` … `contentSid` are nullable with **no C# default — must be passed explicitly** (pass `null`). **Call with named arguments.**
  - Immediate send (step 4): `accountSid`, `to` (E.164 from step 3), `body`, and exactly one sender: `from: <Twilio:FromNumber>` **or** `messagingServiceSid: <Twilio:MessagingServiceSid>`; everything else `null`.
  - Scheduled send (step 5): same, but `messagingServiceSid` is **required** (scheduling is Messaging-Services-only per the enum's map note), plus `scheduleType: MessageEnumScheduleType.Fixed` and `sendAt: <dispatch time + 3 days>` as a `DateTimeOffset`.
  - **Scheduling window**: the SDK imposes no constraint — `sendAt` is a plain `DateTimeOffset?`. Any min/max window is enforced provider-side; a violation surfaces as the Case-B error below. `UNVERIFIED` (not visible in map or source): the exact provider window bounds. Defensive directive: on a scheduling rejection, surface `ex.Error.ReadAsString()` to the caller/log and do not retry unchanged.
- Response `ApiV2010AccountMessage` (`records-1-Ac-Ca.md`) — fields the integration reads:
  - `Sid (sid): string?` — persist; the handle for fetch/cancel/redact.
  - `Status (status): MessageEnumStatus?` — see enum table below (`scheduled` after a scheduled create, `queued`/`accepted` after an immediate one).
  - `To (to): string?`, `From (from): string?`, `MessagingServiceSid (messaging_service_sid): string?`, `Body (body): string?`, `NumSegments (num_segments): string?`, `DateCreated (date_created): string?`, `DateUpdated (date_updated): string?`, `DateSent (date_sent): string?` (dates are `string?`, not `DateTime`), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `Price (price): string?`, `PriceUnit (price_unit): string?`, `Direction (direction): MessageEnumDirection?`, `AccountSid (account_sid): string?`, `NumMedia (num_media): string?`, `Uri (uri): string?`, `ApiVersion (api_version): string?`, `SubresourceUris (subresource_uris): object?`.
  - Note: this record **is** the payload — no extra envelope level.
- **Undeliverable-destination behaviour**: the API accepting the POST and the carrier later rejecting are two different events. A synchronous rejection (bad number, bad sender config) throws the Case-B error below. A carrier rejection *after* acceptance does **not** throw — it appears later as `Status` = `Failed`/`Undelivered` with `ErrorCode`/`ErrorMessage` populated, observed via `FetchMessage` polling (step 7). Defensive directive: never treat a successful `CreateMessage` return as delivered; always reconcile via step 7.
- Error: **Case B** — `SdkException<RawError>`, same accessors as above.

#### Step 6 — Cancel scheduled: `client.Api20100401Message.UpdateMessage` (map: `operations/Api20100401Message.md`)

- HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` — server `Default (api)`.
- Signature: `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ApiV2010AccountMessage>`.
  - `body` and `status` are nullable with **no C# default — must be passed explicitly**. For cancel: `body: null, status: MessageEnumUpdateStatus.Canceled` (the enum's only value, wire `canceled`).
- Which messages can be cancelled: the map's operation note limits UpdateMessage's cancel use to **"not-yet-sent messages"** — i.e. `Status` = `MessageEnumStatus.Scheduled`. Cancelling anything further along is rejected provider-side as the Case-B error. Defensive directive: `FetchMessage` first, cancel only when `Status == MessageEnumStatus.Scheduled`, and still handle the Case-B rejection (race between check and cancel).
- Error: **Case B** — `SdkException<RawError>`.

#### Step 7 — Poll status: `client.Api20100401Message.FetchMessage` (map: `operations/Api20100401Message.md`)

- HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` — server `Default (api)`.
- Signature: `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ApiV2010AccountMessage>`.
- Read `Status` (`MessageEnumStatus?`), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?` off the same record as above.
- Error: **Case B** — `SdkException<RawError>` (a 404 here means unknown/deleted SID).

#### Step 8 — Redact / delete (map: `operations/Api20100401Message.md`)

- **Redact body, keep record + outcome (the requirement)**: `UpdateMessage(string accountSid, string sid, body: "", status: null)` → returns the updated `ApiV2010AccountMessage`; the message record, `Status`, and `ErrorCode` survive with an empty `Body`. (The operation's map note names body redaction as a supported use.)
- **Hard delete (only if the whole record must go — loses the outcome)**: `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task` (void).
- Both: **Case B** — `SdkException<RawError>`.

#### Step 9 — Reconcile: `client.Api20100401Message.ListMessage` (map: `operations/Api20100401Message.md`)

- HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` — server `Default (api)`.
- Signature: `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ListMessageResponse>`.
  - The 8 params `to` … `pageToken` are nullable with **no C# default — must be passed explicitly**. Use named arguments.
  - Filter mapping (wire ← C#): `From` ← `from` (set to `Twilio:FromNumber`), `DateSent` ← `dateSent` (exact day), **`DateSent<` ← `dateSentQuery`** (range end / before), **`DateSent>` ← `dateSentQueryQuery`** (range start / after) — the generated names are opaque; use named arguments and this mapping, never position. `PageSize` ← `pageSize` (`long?`), `Page` ← `page` (`int?`), `PageToken` ← `pageToken` (`string?`).
- Response `ListMessageResponse` (`records-4-Li-Me.md`): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus pagination fields `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `FirstPageUri (first_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `Start (start): int?`, `End (end): int?`, `Uri (uri): string?`.
- Pagination: the map marks this operation "none (only `page`, no `perPage`)" — there is no SDK pager; loop manually: request with `pageSize` + `page`, keep going while `NextPageUri` is non-null (increment `page`).
- Error: **Case B** — `SdkException<RawError>`.

### Enum value tables actually needed (`models/enums.md`; all are `StringEnum<T>` in `TwilioSdk.Models.Enums` — static members or `Type.FromValue("wire")`, **not** C# enums)

| Enum | Members (wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` |
| `MessageEnumTrafficType` | `Free (free)` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

## 3. Trap notes

> ⚠ Step 2 (client registration) — the SDK's `HttpClient`/handler pipeline has specific lifetime
> requirements (long-lived, factory-managed) that `new HttpClient()` per request violates, and the
> DI helper's registration shape decides what you may override. **MUST load `dotnet-client-initialization`**
> before wiring the client.

> ⚠ Step 2 (auth) — where and when credentials must be set relative to client construction, and how
> secrets should flow from configuration, are not visible from the property alone. **MUST load
> `dotnet-authentication`** before setting `AccountSidAuthToken`.

> ⚠ Steps 3–9 (every call) — `CreateMessage` has 24 and `ListMessage` has 8 nullable parameters with
> **no C# defaults** that mis-bind in positional calls; the cancellation token is named `ct`.
> **MUST load `dotnet-calling-endpoints`** before writing the first call.

> ⚠ Steps 3–9 (models) — SDK enums are `StringEnum<T>`, not C# enums (no `switch` exhaustiveness, no
> implicit string conversion); records are immutable with `init`-only setters; unmodeled JSON fields are
> silently dropped on deserialize. **MUST load `dotnet-models`** before mapping SDK records onto domain types.

> ⚠ Step 4/5 (send) — a transport failure on the `CreateMessage` POST can be retried by the SDK's retry
> layer even though the write is non-idempotent: whether a failed send can be re-executed (duplicate SMS)
> depends on retry configuration you have not seen yet. **MUST load `dotnet-configuration-resilience`**
> before wiring the client.

> ⚠ Steps 2, 7, 9 (resilience) — what the SDK's `Timeout` actually bounds, which triggers gate retries,
> and how list pagination is meant to be driven are not what the option names suggest. **MUST load
> `dotnet-configuration-resilience`** before tuning retries/timeouts or writing the reconciliation loop.

> ⚠ Step 10 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`): there are
> no typed `TryGet…` status accessors, so status-specific handling goes through `StatusCode` +
> `ReadAsString()`/`ReadAsJson<T>()`. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces
> as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
> SDK-exception-only catch ladder lets it escape the integration boundary.

> ⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated
> `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the
> `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary
> that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a
> caller that retries 5xx retries something that can never succeed.
> **MUST load `dotnet-error-handling`** before writing that boundary.

> ⚠ Step 10 (tests) — the test seam is a specific constructor argument, not an interface over the
> controllers. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load all of these **before implementation starts**. This sheet deliberately does not carry their
contents; the trap notes above name the hazards without resolving them.

- `dotnet-client-initialization` — step 2 (client construction & DI registration).
- `dotnet-authentication` — step 2 (basic-auth credentials wiring).
- `dotnet-calling-endpoints` — steps 3–9 (every operation call; named-argument discipline).
- `dotnet-models` — steps 3–9 (records, `StringEnum<T>` enums, nullability).
- `dotnet-error-handling` — step 10 (the Case-B boundary and both `JsonException` directions).
- `dotnet-configuration-resilience` — steps 2, 4/5, 7, 9 (retries incl. the duplicate-send hazard, timeouts, base-URL mechanics, pagination).
- `dotnet-testing` — step 10 (faking the SDK at the right seam).

## 5. Assumptions & Blockers

**Assumptions**
- Lookup **v2** (`FetchPhoneNumber3`) was chosen for registration-time validation — it returns the `Valid` verdict and E.164 `PhoneNumber` directly. (A v1 controller, `LookupsV1PhoneNumberApi`, also exists; not used.)
- "Not a usable destination" = `Valid` is not `true`; `ValidationErrors` then carries the reasons for the 400-style rejection the API returns to the shopper.
- Scheduled sends always use `Twilio:MessagingServiceSid` (scheduling requires it); immediate sends use `FromNumber` when no messaging service is configured, otherwise the service SID. Exactly one of `from` / `messagingServiceSid` is sent per message.
- Redaction (step 8) uses `UpdateMessage` with an empty body so the record and outcome survive, per the requirement; `DeleteMessage` is documented but not the default path.
- The `Twilio:BaseUrl` override applies to server node `Default` only; Lookup's node `Default4` keeps its default host, satisfying "Lookup is NOT governed by this setting".
- `accountSid` for every messaging call comes from `Twilio:AccountSid` (same value as the auth username).

**Blockers** — none.

**UNVERIFIED items** (provider-side behaviour not decidable from the SDK map or source; defensive directives given inline above)
- The exact min/max scheduling window enforced around `sendAt` (step 5).
- The full set of statuses the provider accepts for cancellation beyond "not-yet-sent" (step 6).
