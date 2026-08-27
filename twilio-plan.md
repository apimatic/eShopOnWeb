# Twilio .NET SDK integration — eShopOnWeb `src/PublicApi`

Plan + contract sheet for Twilio messaging features in the PublicApi project (.NET 8, ASP.NET Core minimal APIs).
SDK: NuGet `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace `TwilioSdk`; client `TwilioSdkClient`; options `TwilioSdkClientOptions`. All in-scope operations are throw-based, **Case B** (`SdkException<RawError>`) — there are no typed error accessors anywhere in this scope.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Install package; register client + options in DI (`Twilio:BaseUrl` override applied here) | — (client construction) |
| 2 | Wire auth credentials from config | — (`AccountSidAuthToken`) |
| 3 | `NotificationService`: send immediate SMS (order placed/dispatched/cancelled) | `Api20100401Message.CreateMessage` |
| 4 | Registration: validate + canonicalize contact number | `LookupsV2PhoneNumber.FetchPhoneNumber3` (fallback `LookupsV1PhoneNumberApi.FetchPhoneNumber2`) |
| 5 | Dispatch: schedule follow-up message (provider-side) | `Api20100401Message.CreateMessage` with `scheduleType`/`sendAt`/`messagingServiceSid` |
| 6 | Order cancel: cancel still-scheduled follow-up | `Api20100401Message.UpdateMessage` (status) |
| 7 | Status polling (no webhook exists) | `Api20100401Message.FetchMessage` |
| 8 | Right-to-erasure: redact message body at provider | `Api20100401Message.UpdateMessage` (body) |
| 9 | Reconciliation job: list provider records by date range + From | `Api20100401Message.ListMessage` |
| 10 | Error boundary around all of the above (messaging failure must never fail an order op) | — (`SdkException<RawError>`) |
| 11 | Resilience tuning (retry/timeout) + tests | — |

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

### Client construction, auth, base-URL override (Step 1–2)

| Fact | Contract | Source |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` / `TwilioSdkClient.cs` |
| DI registration | `services.AddTwilioSdkClient(o => { … })` | `sdk-map.md` / `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` (member: `ServerEnvironment.Production`) · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` / `TwilioSdkClientOptions.cs` |
| Credentials | `BasicAuthCredentials` is `sealed class` with init-only `required string Username` and `required string Password`. Username = AccountSid (or API-key SID), Password = AuthToken (or API-key secret). | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| **Base-URL override — per-client, not per-request** | `TwilioSdk.ServerOptions` has one property per server node: `Default: TwilioSdk.Servers.DefaultOptions` (host `https://api.twilio.com` — **all `Api20100401Message` ops run on this node**) … `Default4: TwilioSdk.Servers.Default4Options` (host `https://lookups.twilio.com` — the Lookup ops). Override for the **messaging API only**: `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` — used verbatim as the base address. Leave `Default4` untouched so Lookup still hits the real lookups host. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` (the `requestOptions` param on every op) carries **only** `LogLevel: Microsoft.Extensions.Logging.LogLevel?` — there is **no** per-request base-URL override; the override is per-client only. | `Core/RequestOptions.cs` |

### `client.Api20100401Message` — messaging operations (map page `operations/Api20100401Message.md`)

| Operation | Signature (verbatim; all middle params nullable, no default → **must pass explicitly, `null` to skip**) | Returns | Error |
|---|---|---|---|
| CreateMessage | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | Case B `SdkException<RawError>` |
| FetchMessage | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | Case B |
| UpdateMessage | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` must be passed explicitly | `ApiV2010AccountMessage` | Case B |
| DeleteMessage | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` | Case B |
| ListMessage | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 nullable params must be passed explicitly | `ListMessageResponse` | Case B |

ListMessage wire names (filter at the provider): `To`←`to`, `From`←`from`, `DateSent`←`dateSent`, **`DateSent<`←`dateSentQuery`** (upper bound — pass the range **end** here), **`DateSent>`←`dateSentQueryQuery`** (lower bound — pass the range **start** here), `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`. Date params serialize via `ToIso8601()` as full UTC timestamps (`yyyy-MM-ddTHH:mm:ss.fff'Z'`); the operation's own XML doc says the API accepts GMT **date-only** (`YYYY-MM-DD`) values, and the SDK offers no date-only wire option through the typed signature — pass UTC-midnight `DateTimeOffset`s and treat provider acceptance of full timestamps as `UNVERIFIED` (live-traffic only). Swapping the two range operands guarantees an empty page.

### Response/request models (records pages cited per row)

| Model | Fields the integration reads (`Name (wire_name): Type`) | Map page |
|---|---|---|
| `TwilioSdk.Models.ApiV2010AccountMessage` (no envelope — the payload **is** the response) | `Sid (sid): string?` · `Status (status): MessageEnumStatus?` · `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` · `From (from): string?` · `To (to): string?` · `Body (body): string?` · `DateSent (date_sent): string?` · `DateCreated (date_created): string?` · `DateUpdated (date_updated): string?` · `Direction (direction): MessageEnumDirection?` · `MessagingServiceSid (messaging_service_sid): string?` · `NumSegments (num_segments): string?` · `Price (price): string?` · `PriceUnit (price_unit): string?` | `records-1-Ac-Ca.md` |
| `TwilioSdk.Models.ListMessageResponse` | `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `NextPageUri (next_page_uri): string?` · `PreviousPageUri (previous_page_uri): string?` · `FirstPageUri (first_page_uri): string?` · `Page (page): int?` · `PageSize (page_size): int?` · `Start (start): int?` · `End (end): int?` · `Uri (uri): string?` | `records-4-Li-Me.md` |
| `TwilioSdk.Models.LookupResponse` (Lookup v2) | **`Valid (valid): bool?`** — validity flag · **`PhoneNumber (phone_number): string?`** — canonical (E.164) form to store · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?` · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` — reasons when invalid · (plus optional add-on fields: `CallerName`, `SimSwap`, `LineTypeIntelligence`, `SmsPumpingRisk`, … — not requested, not read) | `records-4-Li-Me.md` |
| `TwilioSdk.Models.LookupsV1PhoneNumber` (Lookup v1 fallback) | `PhoneNumber (phone_number): string?` · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `Url (url): string?` — **no `Valid` field**: validity is inferred from success vs. thrown `SdkException<RawError>` | `records-4-Li-Me.md` |

### Enums (`TwilioSdk.Models.Enums`; `StringEnum<T>` records, **not** C# enums — use the static members; map page `enums.md`)

| Enum | Members (wire values) | Used for |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, **`Scheduled (scheduled)`** — "scheduled but not yet sent", `Read (read)`, `PartiallyDelivered (partially_delivered)`, **`Canceled (canceled)`** | Steps 5, 6, 7, 9 |
| `MessageEnumScheduleType` | `Fixed (fixed)` — only value; map note: *"For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter in order to schedule a Message."* | Step 5 |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — only value | Step 6 |
| `MessageEnumContentRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` | optional, Step 3 |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` | optional, Step 3 |
| `MessageEnumTrafficType` | `Free (free)` | optional, Step 3 |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` | optional, Step 3 |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | Step 4 |

### Feature-by-feature binding

1. **Send immediate SMS** — `CreateMessage(accountSid: <Twilio:AccountSid>, to: <shopper>, …, from: <Twilio:FromNumber>, body: <text>, …)` with every other nullable param `null`. **Use `from:`, not `messagingServiceSid:`** for immediate sends: reconciliation (feature 7) filters `ListMessage` by `From`, and the provider record's `From (from)` field carries the actual sender — with a Messaging Service the provider picks the sender from the pool, so `From` on those records is not guaranteed to equal `Twilio:FromNumber` and the reconciliation query would miss them. (Map-grounded: `ListMessage.From` filter + `ApiV2010AccountMessage.From`; pool-selection behavior is `UNVERIFIED` — treat as the safe default either way.)
2. **Validate at registration** — `FetchPhoneNumber3(phoneNumber: <typed>, fields: null, countryCode: null, firstName: null, lastName: null, addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null, addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null, verificationSid: null, partnerSubId: null)`. Valid ⇔ `response.Valid == true`; store `response.PhoneNumber` (canonical), never the typed string; on `false`, surface `ValidationErrors`. Runs on server node `Default4` (`https://lookups.twilio.com`) — unaffected by the `Twilio:BaseUrl` messaging override. **Fallbacks:** (a) v2 unavailable/erroring → v1 `FetchPhoneNumber2(phoneNumber, countryCode: null, type: null, addOns: null, addOnsData: null)`; it has no `Valid` flag — treat a successful response as valid and a thrown `SdkException<RawError>` as invalid/unverifiable (best-effort; exact status semantics `UNVERIFIED` from map/source). (b) Account has no Lookup access at all → registration must not hard-fail: store the typed number flagged "unverified" and proceed (app-level decision — see Assumptions).
3. **Schedule follow-up** — `CreateMessage(…, scheduleType: MessageEnumScheduleType.Fixed, sendAt: <DateTimeOffset, a few days out>, …, messagingServiceSid: <Twilio:MessagingServiceSid>, body: <text>, …)`. Scheduling is **Messaging-Service-only** per the map's `MessageEnumScheduleType` note, so this one send path uses `messagingServiceSid:` (not `from:`). Persist the returned `Sid` on the order for later cancel/poll. "Scheduled but not yet sent" ⇔ `Status == MessageEnumStatus.Scheduled`. Minimum lead time / maximum scheduling window: not stated in the map or the SDK source — `UNVERIFIED`; directive: schedule days (not minutes) ahead, and treat a provider rejection of `sendAt` as a non-fatal, logged skip of the follow-up.
4. **Cancel scheduled** — `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` (map note on UpdateMessage: *"used to redact Message `body` text and to cancel not-yet-sent messages"*). The exact set of statuses the provider accepts cancellation for is not enumerated in map/source — `UNVERIFIED`; directive: attempt cancel only when the last polled status was `Scheduled` (or unknown), and treat a provider rejection here as "already sent / already terminal" — log and continue, never fail the order cancel.
5. **Fetch status** — `FetchMessage(accountSid, sid)` → read `Status` (enum table above), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?` directly off `ApiV2010AccountMessage` (no envelope). Not-found surfaces as thrown `SdkException<RawError>` — check `.Error.StatusCode == HttpStatusCode.NotFound`.
6. **Redact body (erasure)** — `UpdateMessage(accountSid, sid, body: "", status: null)` per the same map note ("redact Message `body` text"). The message record (`Sid`, `Status`, dates, `ErrorCode`, etc.) survives; only the body is cleared. Whether a subsequent fetch returns `Body` as `""` or `null` after redaction is `UNVERIFIED` — read `Body` defensively (null/empty both mean "redacted"). `DeleteMessage` exists but removes the whole record — do **not** use it for erasure; the outcome must survive.
7. **Reconciliation listing** — `ListMessage(accountSid, to: null, from: <Twilio:FromNumber>, dateSent: null, dateSentQuery: <rangeEndUtc>, dateSentQueryQuery: <rangeStartUtc>, pageSize: <e.g. 100>, page: <n>, pageToken: null)` — the `from` filter is applied **at the provider** (wire `From`, bare E.164, percent-encoded by the SDK). **Mind the inverted wire names:** `dateSentQuery` → `DateSent<` (upper bound = range **end**), `dateSentQueryQuery` → `DateSent>` (lower bound = range **start**) — passing start/end the other way round yields a guaranteed-empty page. Page through the range: loop while the returned `NextPageUri` is non-null, incrementing `page` (the map lists pagination as "only `page`, no `perPage`" — page mechanics are a resilience-skill concern, see trap notes). Caveat: scheduled sends (feature 3) go through the Messaging Service, so their records' `From` may be a pool number and can fall outside this filter — reconcile those by stored `Sid` + `FetchMessage` instead (defensive directive; pool behavior `UNVERIFIED`).
8. **Construction/auth/base URL** — see the client table above. Base-URL override is **per-client**: `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` when the config key is present; used verbatim for every messaging-API call; Lookup calls (node `Default4`) are unaffected.
9. **Error handling** — every in-scope operation throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` (Case B). Read: `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`. There are **no typed accessors** in this scope — the provider's error code/message live in the raw body; extract via `ReadAsJson<T>()` into an app-owned DTO best-effort and fall back to `ReadAsString()` (exact wire shape of the error body is `UNVERIFIED` from map/source). Invalid-number-at-send-time, message-not-found, and auth failure all arrive through this same single catch.

## 3. Trap notes

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind `TwilioSdkClient` has lifetime rules the constructor signature does not show; building it per request or disposing it wrong will degrade or break the app under load. **MUST load `dotnet-client-initialization`** before writing the DI registration.

> ⚠ Step 2 (auth) — when credentials must be set relative to client construction, and how secrets flow from configuration without hardcoding, is not visible from the options shape. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

> ⚠ Step 3+ (every call) — `CreateMessage`/`ListMessage`/`FetchPhoneNumber3` have long nullable parameter lists with no C# defaults; a positional call mis-binds silently. What the safe call pattern is, is a skill concern. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Step 3+ (models/enums) — `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError` are `StringEnum<T>` records, not C# enums; construction, comparison, and parsing each have traps the member list does not show, and unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** before touching enum values or mapping response fields.

> ⚠ Step 5 (scheduling) — provider-side scheduling constraints (minimum lead time, maximum window, Messaging-Service requirement enforcement) are only partially visible in the map; a violation surfaces as a Case B error at create time, and whether that failure can be safely retried is a resilience-skill question. **MUST load `dotnet-configuration-resilience`** and **`dotnet-error-handling`** before this step.

> ⚠ Step 9 (reconciliation paging) — the map lists pagination as "only `page`, no `perPage`"; how to page a list response to exhaustion without skipping or double-reading records is not derivable from the signature. **MUST load `dotnet-configuration-resilience`** before writing the paging loop.

> ⚠ Step 10 (error boundary) — all in-scope ops are Case B, but the boundary still has two `JsonException` hazards (see REQUIRED READING) and the question of whether a failed `CreateMessage` POST may have been executed more than once by the retry layer — that decides whether "the app never fails an order because a message failed" also needs duplicate-send protection. **MUST load `dotnet-error-handling`** and **`dotnet-configuration-resilience`** before writing the boundary.

> ⚠ Step 11 (tests) — the test seam for this SDK is specific (the `HttpClient` constructor argument); stubbing the wrong seam couples tests to SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load these **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 (client construction & DI registration).
- `dotnet-authentication` — Step 2 (credentials wiring).
- `dotnet-calling-endpoints` — Steps 3–9 (every operation call; named-argument discipline).
- `dotnet-models` — Steps 3–9 (`StringEnum<T>` enums, records, wire names).
- `dotnet-error-handling` — Step 10 (the exception boundary). Two hazard rows, verbatim:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — Steps 1, 5, 9, 10 (retry/timeout semantics, base-URL interaction, pagination).
- `dotnet-testing` — Step 11 (faking the SDK seam).

## 5. Assumptions & Blockers

**Assumptions**
- Config keys `Twilio:AccountSid` and `Twilio:AuthToken` exist alongside the named `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (the brief names only the latter three; every operation's first parameter `accountSid` is fed from `Twilio:AccountSid`).
- When Lookup access is absent on the account (feature 2 fallback (b)), registration stores the caller-typed number flagged "unverified" rather than failing signup — confirm with product; the alternative (hard-fail registration) is a one-line change at the boundary.
- The follow-up message in feature 3 is SMS (`body:`), not MMS/content-template — `mediaUrl`/`contentSid` stay `null`.
- Reconciliation date range is interpreted in UTC against `DateSent<`/`DateSent>`. The SDK serializes these as full UTC timestamps (`yyyy-MM-ddTHH:mm:ss.fff'Z'`, via `ToIso8601()` in `Core/Extensions/DateTimeOffsetExtensions.cs`) while the operation's XML doc documents date-only `YYYY-MM-DD` — whether the provider accepts full timestamps is `UNVERIFIED` against live traffic — defensive: pass UTC-midnight values, and log the first reconciliation response to confirm the filter took effect.

**Blockers**
- None.
