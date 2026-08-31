# Twilio .NET SDK integration plan — eShopOnWeb SMS order notifications

SDK: `AsadAli.TwilioSdk` (NuGet) · Root namespace `TwilioSdk` · Client `TwilioSdkClient` · Options `TwilioSdkClientOptions`
Map provenance: source commit `51fdf48` ("Publish v2.0.0 SDK"). NuGet publishes exactly one version, **2.0.0**, which is that commit — the map and the installed package agree. (Repo `main` has moved on to an unpublished regen with different names; ignore it — the package is 2.0.0.)

**Version mechanism:** install version-less (`dotnet add package AsadAli.TwilioSdk`) so it floats to latest; the only published version today is 2.0.0. If the repo uses central package management (`Directory.Packages.props`), add the `AsadAli.TwilioSdk` version there per the repo's existing convention (repo recon is the implementer's job).

## 1. Scope & sequence

1. **Client & DI setup** — construct/register `TwilioSdkClient` with AccountSid/AuthToken basic auth; apply `Twilio:BaseUrl` override to the messaging server node only. (No API operation; client construction.)
2. **Register contact number with validation** — `LookupsV2PhoneNumber.FetchPhoneNumber3` → store canonical E.164 from the response.
3. **Send SMS immediately** — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`.
4. **Schedule follow-up (~3 days)** — `Api20100401Message.CreateMessage` with `messagingServiceSid` = `Twilio:MessagingServiceSid`, `scheduleType` = Fixed, `sendAt` ≈ now+3d.
5. **Cancel scheduled message** — `Api20100401Message.UpdateMessage` with `status` = Canceled.
6. **Fetch delivery outcome** — `Api20100401Message.FetchMessage` (poll by Sid; no callback URL).
7. **Redact message body at provider** — `Api20100401Message.UpdateMessage` with `body` = `""` (NOT `DeleteMessage` — see §2 row).
8. **Reconciliation report** — `Api20100401Message.ListMessage` with `from` + date-sent range filters; manual paging.
9. **Error boundary** — every operation in scope is throw-only, Case B (`SdkException<RawError>`); see §2 error rows and §3/§4.

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

Namespaces needed (`sdk-map.md` *Namespaces* table + source files named below):

| Types | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`, `ServiceCollectionExtensions.AddTwilioSdkClient` | `TwilioSdk` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| `RetryOptions`, `LoggingOptions` | `TwilioSdk.Core.Configuration` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `TwilioSdk.Core.ErrorResponse` |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection`, `ValidationError` | `TwilioSdk.Models.Enums` |

### Operations

**OP-1 · Validate + canonicalize a phone number** (`map/operations/LookupsV2PhoneNumber.md`; record `map/models/records-4-Li-Me.md`)

- Controller property: `client.LookupsV2PhoneNumber` · HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` · server node **Default4 (lookups)** — host `https://lookups.twilio.com`; the `Twilio:BaseUrl` override does **not** govern this operation.
- Signature:
  `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — the 15 params `fields`…`partnerSubId` are nullable with **no C# default: pass each explicitly** (`null` to skip). For plain validation pass `phoneNumber` (E.164, or national format + `countryCode`), everything else `null`. (`fields` requests extra paid packages — `validation` data is in the base response; leave `fields: null`.)
- Returns `LookupResponse` — fields the integration reads:
  `PhoneNumber (phone_number): string?` ← **canonical E.164 form — store this** · `Valid (valid): bool?` ← usability verdict · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` ← reasons when invalid · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?` · `Url (url): string?`. (Remaining fields — `CallerName`, `SimSwap`, `CallForwarding`, `LineTypeIntelligence`, `LineStatus`, `IdentityMatch`, `ReassignedNumber`, `SmsPumpingRisk`, `PhoneNumberQualityScore`, `PreFill` — are null unless requested via `fields`; ignore.)
- Unusable number: `Valid == false` (inspect `ValidationErrors`). Whether the provider *additionally* returns a non-2xx (e.g. 404) for some malformed numbers is not determinable from the SDK surface — **UNVERIFIED** → defensive directive: also treat `SdkException<RawError>` from this call as "number not usable", reading `ex.Error.StatusCode`/`ReadAsString()` for logging.
- Error: **Case B** — `SdkException<RawError>`; accessors `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`. No-throw variant: absent. Pagination: none.

**OP-2 · Send SMS immediately** (`map/operations/Api20100401Message.md`; record `map/models/records-1-Ac-Ca.md`)

- Controller property: `client.Api20100401Message` · HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server node **Default (api)** — `https://api.twilio.com`; governed by the `Twilio:BaseUrl` override.
- Signature:
  `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — the 24 params `statusCallback`…`contentSid` are nullable with **no C# default: pass each explicitly** (`null` to skip). Use named arguments. Wire names: `To`←`to`, `From`←`from`, `Body`←`body`, `MessagingServiceSid`←`messagingServiceSid`, `ScheduleType`←`scheduleType`, `SendAt`←`sendAt` (full 24-name wire map on the operation page).
- Immediate send: `accountSid` = `Twilio:AccountSid`, `to` = canonical E.164 from OP-1, `from: ` = `Twilio:FromNumber`, `body: ` = text, `messagingServiceSid: null`, `scheduleType: null`, `sendAt: null`, all other optionals `null`.
- Returns `ApiV2010AccountMessage` (full field table below) — read `Sid (sid): string?` and `Status (status): MessageEnumStatus?` (expect `Queued`/`Accepted`/`Sent` on success — exact immediate value is provider behavior, **UNVERIFIED**; do not branch on one specific value).
- Error: **Case B** — `SdkException<RawError>` (accessors as OP-1). No-throw variant: absent. Pagination: none.

**OP-3 · Schedule a message (~3 days) with the provider** (same page/signature as OP-2)

- Same `CreateMessage`, but: `from: null`, `messagingServiceSid: ` = `Twilio:MessagingServiceSid` (scheduling requires a Messaging Service), `scheduleType: MessageEnumScheduleType.Fixed` (wire `fixed` — the enum's only value), `sendAt: ` a `DateTimeOffset` ≈ `DateTimeOffset.UtcNow.AddDays(3)`.
- `SendAt` format/window: the SDK applies **no client-side validation** — `sendAt` is passed straight through as the `SendAt` form parameter (source: `Api/Api20100401Message.cs` request-builder list). The provider's min/max scheduling window is not visible anywhere in the SDK — **UNVERIFIED** → defensive directive: keep `sendAt` comfortably in the future (the ~3-day use case qualifies), always send UTC, and treat a `SdkException<RawError>` with 400 from create as a rejected schedule (surface `ReadAsString()` to logs).
- Returns `ApiV2010AccountMessage`; persist `Sid` and expect `Status` = `Scheduled` (`scheduled`). Store the Sid — OP-4 needs it.

**OP-4 · Cancel a scheduled message** (`map/operations/Api20100401Message.md`)

- Controller property: `client.Api20100401Message` · HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · server node **Default (api)**.
- Signature: `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` are nullable with **no default: pass both explicitly**. Wire: `Body`←`body`, `Status`←`status`.
- Cancel: `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` (wire `canceled` — the enum's only value). Operation doc: "used to redact Message `body` text and to cancel not-yet-sent messages".
- Which current statuses the provider allows cancellation from is not in the SDK surface — **UNVERIFIED** → defensive directive: only attempt when the app's own state says the message is scheduled/not-yet-sent; on `SdkException<RawError>` (e.g. 400/404) treat as "too late to cancel", log `StatusCode` + `ReadAsString()`, and do not retry.
- Returns `ApiV2010AccountMessage` (expect `Status` = `Canceled`). Error: **Case B** `SdkException<RawError>`. No-throw variant: absent.

**OP-5 · Fetch current delivery outcome** (`map/operations/Api20100401Message.md`)

- Controller property: `client.Api20100401Message` · HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · server node **Default (api)**.
- Signature: `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`.
- Returns `ApiV2010AccountMessage` — read `Status` (terminal outcomes: `Delivered`, `Undelivered`, `Failed`, `Canceled`; non-terminal: `Queued`, `Sending`, `Sent`, `Accepted`, `Scheduled`) plus `ErrorCode (error_code): int?` and `ErrorMessage (error_message): string?` for the failure reason.
- Error: **Case B** `SdkException<RawError>` — a 404 here means the provider has no such message (e.g. deleted). No-throw variant: absent.

**OP-6 · Redact message content at the provider** (`map/operations/Api20100401Message.md`)

- Use **`UpdateMessage(accountSid, sid, body: "", status: null)`** — the operation's documented redaction path ("used to redact Message `body` text"). Afterwards the record (Sid, Status, dates, ErrorCode) survives with an empty `Body`; a later `FetchMessage` returns `Body` = `""`.
- Do **not** use `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` (`DELETE …/Messages/{Sid}.json`, returns `void`/Task): it deletes the whole Message resource, which would destroy the sent-fact/outcome the requirement says must survive.
- Error: **Case B** `SdkException<RawError>` for both. No-throw variants: absent.

**OP-7 · List/reconcile messages (From + date range)** (`map/operations/Api20100401Message.md`; records `map/models/records-4-Li-Me.md`, `records-1-Ac-Ca.md`)

- Controller property: `client.Api20100401Message` · HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · server node **Default (api)**.
- Signature:
  `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — the 8 params `to`…`pageToken` are nullable with **no default: pass each explicitly**. Wire map (verbatim, note the generated names): `To`←`to` · `From`←`from` · `DateSent`←`dateSent` (exact day) · **`DateSent<`←`dateSentQuery` (sent before)** · **`DateSent>`←`dateSentQueryQuery` (sent after)** · `PageSize`←`pageSize` · `Page`←`page` · `PageToken`←`pageToken`.
- Reconciliation call: `from: ` = `Twilio:FromNumber` (server-side sender filter), `dateSentQueryQuery: ` = range start (after), `dateSentQuery: ` = range end (before), `dateSent: null`, `to: null`, `pageSize: ` e.g. 1000 (doc: default 50, max 1000).
- Returns `ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus paging fields `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `FirstPageUri`, `Page (page): int?`, `PageSize (page_size): int?`, `Start`, `End`, `Uri`. **Pagination: no SDK pager** (map: "only `page`, no `perPage`") — loop manually: while `NextPageUri` is non-null, request the next page via `page`/`pageToken`. **MUST load `dotnet-configuration-resilience`** for the list-pagination pattern.
- Error: **Case B** `SdkException<RawError>`. No-throw variant: absent.

**`ApiV2010AccountMessage` — message resource fields** (`map/models/records-1-Ac-Ca.md`; `Models/ApiV2010AccountMessage.cs`; namespace `TwilioSdk.Models`). `CSharpName (wire_name): Type`:

| Field | Type | Notes |
|---|---|---|
| `Sid (sid)` | `string?` | message identifier — persist at create/schedule |
| `Status (status)` | `MessageEnumStatus?` | see enum table |
| `From (from)` / `To (to)` | `string?` | E.164 |
| `Body (body)` | `string?` | empty after OP-6 redaction |
| `ErrorCode (error_code)` | `int?` | provider error code on failed/undelivered |
| `ErrorMessage (error_message)` | `string?` | provider error text |
| `DateSent (date_sent)` | `string?` | **string, not DateTimeOffset** (list *filters* are `DateTimeOffset?`; the resource field is a string) |
| `DateCreated (date_created)` / `DateUpdated (date_updated)` | `string?` | same |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | set on scheduled sends |
| `Direction (direction)` | `MessageEnumDirection?` | |
| `NumSegments (num_segments)` / `NumMedia (num_media)` | `string?` | |
| `Price (price)` / `PriceUnit (price_unit)` | `string?` | |
| `AccountSid (account_sid)` / `Uri (uri)` / `ApiVersion (api_version)` | `string?` | |
| `SubresourceUris (subresource_uris)` | `object?` | unmodeled — ignore |

### Enums (`map/models/enums.md`; namespace `TwilioSdk.Models.Enums`; `StringEnum<T>` — use the static members shown, e.g. `MessageEnumScheduleType.Fixed`, or `Type.FromValue("wire")`; **not** C# enums)

| Enum | Members (wire values) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` — only value |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — only value |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

### Client construction, auth, server selection (`sdk-map.md` *Getting a client* / *Servers & auth*; sources `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Servers/ServerEnvironment.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`, `ServiceCollectionExtensions.cs`)

- Constructor: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
- `TwilioSdkClientOptions` properties (all): `Environment: ServerEnvironment` (default `ServerEnvironment.Default()`), `Retry: RetryOptions` (default `RetryOptions.Default()`), `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?`.
- Auth: `AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }` — `Username`/`Password` are `required string { get; init; }` (namespace `TwilioSdk.Core.Authentication.Basic`). The source XML doc recommends an API key/secret as username/password and says account SID + auth token is for local testing; the app's config carries AccountSid/AuthToken, so use those.
- Environment: `ServerEnvironment.Production` (wire `production`) is the **only** member (namespace `TwilioSdk.Servers`).
- Base-URL override, messaging only: `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` — `ServerOptions.Default: DefaultOptions` → `.Production: DefaultOptions.ProductionOptions` → `.BaseUrl: string` (default `https://api.twilio.com`). Lookup runs on a **separate node**: `options.Server.Default4.Production.BaseUrl` (default `https://lookups.twilio.com`) — leave it at default; the `Twilio:BaseUrl` override must not touch it. Apply the override only when `Twilio:BaseUrl` is non-empty.
- DI: `services.AddTwilioSdkClient(o => { /* set AccountSidAuthToken, Server.Default.Production.BaseUrl */ })` — extension in `TwilioSdk` namespace (`ServiceCollectionExtensions.cs`); registers the client as a **singleton** built from an `IHttpClientFactory`-created `HttpClient` and calls `services.AddHttpClient()`.

## 3. Trap notes

- ⚠ Step 1 (client registration) — the SDK's DI extension registers the client as a singleton over a factory-created `HttpClient`; hand-rolling your own construction instead gets handler-lifetime and socket-exhaustion semantics wrong. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ Step 1 (auth) — credentials must be set on the options before the client is constructed, and secrets must come from configuration, not code. **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–8 (every call) — these operations take long parameter lists whose optional params have **no C# defaults**; a positional call mis-binds. Whether a skipped optional is passed as `null`, and how named arguments interact with the literal generated parameter names, is where calls go wrong. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–8 (models) — SDK enums are `StringEnum<T>` records, not C# enums: they don't appear in `switch` the way you expect, equality isn't `==` on a C# enum, and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** before mapping `ApiV2010AccountMessage`/`LookupResponse` onto app types.
- ⚠ Step 9 (error boundary) — every operation here is Case B (`SdkException<RawError>`): there are no typed `TryGet…` status accessors, so status/body extraction goes through `StatusCode`/`ReadAsString()`/`ReadAsJson<T>()`, and a catch ladder written for typed errors compiles but never matches. **MUST load `dotnet-error-handling`**.
- ⚠ Step 9 (delivery failures) — sending to an undeliverable destination (this account cannot deliver to US destinations): whether the provider rejects synchronously at create or accepts and later flips status to `undelivered`/`failed` is provider behavior the SDK surface cannot settle — **UNVERIFIED**. Defensive directive: treat `CreateMessage` success as *accepted, not delivered*; determine the outcome only via OP-5 polling of `Status`/`ErrorCode`/`ErrorMessage`; keep the catch ladder for transport/config failures (401, 5xx) separate from delivery outcomes.
- ⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs a failed write may be re-sent on is also not what the option names suggest — a non-idempotent `CreateMessage` can execute more than once. **MUST load `dotnet-configuration-resilience`** before tuning `Retry` or relying on any timeout.
- ⚠ Step 8 (pagination) — `ListMessage` has no SDK pager; whether you follow `NextPageUri` or `page`/`pageToken`, and what the filters actually match server-side, decides whether the reconciliation report silently truncates. **MUST load `dotnet-configuration-resilience`** (list pagination) before writing the loop.
- ⚠ Tests — the SDK seam you fake in tests is specific (one constructor argument), and faking at the wrong layer leaves the error boundary untested. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — Step 1 (client construction & DI registration).
- `dotnet-authentication` — Step 1 (basic-auth credentials wiring).
- `dotnet-calling-endpoints` — Steps 2–8 (explicit-null params, named arguments, literal parameter names incl. `ct:`).
- `dotnet-models` — Steps 2–8 (`StringEnum<T>` enums, record nullability, wire names, dropped unmodeled fields).
- `dotnet-error-handling` — Step 9 (Case B `RawError` accessors, the exception boundary).
- `dotnet-configuration-resilience` — Steps 1 & 8 (retry/timeout semantics, base-URL/server selection, list pagination).
- `dotnet-testing` — test seam for the integration layer.

Mandatory hazard rows (both directions of `System.Text.Json.JsonException` at the boundary — opposite handling):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Assumptions**

- Lookup validation uses the v2 Lookup API (`LookupsV2PhoneNumber.FetchPhoneNumber3`), whose base response carries `Valid`/`ValidationErrors`/canonical `PhoneNumber`; the v1 variant (`LookupsV1PhoneNumberApi.FetchPhoneNumber2`) has no `Valid` field in its model and was rejected for this use case.
- "Usable destination" = `Valid == true`; line-type/SMS-capability packages (`line_type_intelligence` etc.) are out of scope (paid add-ons via `fields`).
- Scheduled follow-up sends through the Messaging Service (`MessagingServiceSid`), so no `From` is passed on OP-3; immediate sends (OP-2) use `FromNumber` directly. If immediate sends should also route via the Messaging Service, pass `messagingServiceSid` instead of `from` — same signature.
- `Twilio:AccountSid` is passed as the `accountSid` argument on every `Api20100401Message` operation (it is also the basic-auth username).
- The project pins dependency versions centrally; the implementer will place the `AsadAli.TwilioSdk` version per the repo's existing mechanism (only published version: 2.0.0).

**Blockers** — none.

**UNVERIFIED items** (provider behavior not decidable from the SDK surface; each carries a defensive directive in §2/§3 — do not treat as settled): Lookup non-2xx-on-invalid behavior; immediate `Status` value returned by create; provider scheduling window for `SendAt`; statuses from which cancellation is accepted; sync-vs-async rejection of undeliverable destinations.
