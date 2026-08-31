# Twilio SMS integration — plan (eShopOnWeb PublicApi, .NET 8)

## 1. Scope & sequence

1. **Client setup & DI** — register `TwilioSdkClient` with AccountSid/AuthToken basic auth; apply optional messaging-only BaseUrl override. *(no operations)*
2. **Register shopper phone** — validate + canonicalize via `LookupsV2PhoneNumber.FetchPhoneNumber3`; store `LookupResponse.PhoneNumber` (E.164), never the caller-typed string.
3. **Immediate SMS** (order placed / dispatched / cancelled) — `Api20100401Message.CreateMessage` with `from` + `body`.
4. **Scheduled SMS** (dispatch follow-up) — `Api20100401Message.CreateMessage` with `messagingServiceSid` + `scheduleType` + `sendAt` (no `from`).
5. **Cancel scheduled SMS** — `Api20100401Message.UpdateMessage` with `status: Canceled`.
6. **Re-send** — plain `CreateMessage` again (step 3). Confirmed: the SDK has no resend operation; the controller exposes only Create/Delete/Fetch/List/Update.
7. **Redact message body** (GDPR-style) — `Api20100401Message.UpdateMessage` with `body: ""` (record + outcome survive). `DeleteMessage` removes the record entirely — NOT what this capability wants.
8. **Reconciliation list** — `Api20100401Message.ListMessage` with `from` + date-window filters, provider-side.
9. **Refresh delivery status** — `Api20100401Message.FetchMessage` by SID.
10. **Error boundary** — wrap all of the above; every in-scope operation is error Case B.

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

### Operations

| Capability | Controller property · signature (verbatim) | Returns | Error case |
|---|---|---|---|
| Validate/canonicalize number | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — all 15 params after `phoneNumber` are nullable-no-default → **pass explicitly** (`null` to skip). Server node `Default4 (lookups)`. | `TwilioSdk.Models.LookupResponse` | **B**: `SdkException<RawError>` |
| Send immediate SMS | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 nullable-no-default params → **pass explicitly**. Server node `Default (api)`. | `TwilioSdk.Models.ApiV2010AccountMessage` | **B**: `SdkException<RawError>` |
| Send scheduled SMS | same `CreateMessage` — pass `messagingServiceSid: <MessagingServiceSid>`, `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <dispatch time + N days>`, `from: null`. `MessageEnumScheduleType` doc: "For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter in order to schedule a Message." | `ApiV2010AccountMessage` with `Status` = `MessageEnumStatus.Scheduled` | **B** |
| Cancel scheduled SMS | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` nullable-no-default → pass explicitly. Cancel: `body: null, status: MessageEnumUpdateStatus.Canceled`. Op note: "used to redact Message `body` text and to cancel not-yet-sent messages". | `ApiV2010AccountMessage` | **B** |
| Redact body (keep record) | same `UpdateMessage` — `body: "", status: null`. Empties the text; the Message resource (Sid, status, outcome) survives. | `ApiV2010AccountMessage` | **B** |
| Delete record entirely | `client.Api20100401Message.DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — removes the Message resource; use only if the *record* must go, not for body redaction. | `void` (Task) | **B** |
| List for reconciliation | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 nullable-no-default params → pass explicitly, **named args only** (see trap notes). Wire names: `To`←`to`, `From`←`from`, `DateSent`←`dateSent` (exact day), `DateSent<`←`dateSentQuery` (**before**), `DateSent>`←`dateSentQueryQuery` (**after**), `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`. Filter `from: <FromNumber>` provider-side — do not filter client-side. | `TwilioSdk.Models.ListMessageResponse` | **B** |
| Fetch one message | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | **B** |

Every operation takes `accountSid` as its first parameter — the SDK does not inject it from credentials; pass the configured AccountSid on every call.

### Response envelopes / models (records pages: `records-1-Ac-Ca.md`, `records-4-Li-Me.md`; all namespace `TwilioSdk.Models`, immutable, `init`-only)

`ApiV2010AccountMessage` — **no envelope; the payload is the record itself**:
`Sid (sid): string?` · `Status (status): MessageEnumStatus?` · `To (to): string?` · `From (from): string?` · `Body (body): string?` · `DateSent (date_sent): string?` · `DateCreated (date_created): string?` · `DateUpdated (date_updated): string?` · `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` · `MessagingServiceSid (messaging_service_sid): string?` · `Direction (direction): MessageEnumDirection?` · `NumSegments (num_segments): string?` · `NumMedia (num_media): string?` · `Price (price): string?` · `PriceUnit (price_unit): string?` · `AccountSid (account_sid): string?` · `ApiVersion (api_version): string?` · `Uri (uri): string?` · `SubresourceUris (subresource_uris): object?`.
Note: the three date fields are `string?` (RFC-2822 on the wire), not `DateTimeOffset` — parse app-side for reporting.

`ListMessageResponse` — list envelope:
`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `NextPageUri (next_page_uri): string?` · `PreviousPageUri (previous_page_uri): string?` · `FirstPageUri (first_page_uri): string?` · `Page (page): int?` · `PageSize (page_size): int?` · `Start (start): int?` · `End (end): int?` · `Uri (uri): string?`.
Pagination: map row says "none (only `page`, no `perPage`)" — **no SDK auto-pager**; page manually via `page`/`pageToken`/`pageSize` or by following `NextPageUri` until null.

`LookupResponse` (Lookup v2) — **no envelope**:
`Valid (valid): bool?` ← usability verdict · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` ← why invalid · `PhoneNumber (phone_number): string?` ← **canonical E.164 form to store** · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?` · `Url (url): string?` · plus optional info records (`CallerName`, `SimSwap`, `CallForwarding`, `LineTypeIntelligence`, `LineStatus`, `IdentityMatch`, `ReassignedNumber`, `SmsPumpingRisk`) returned only when requested via `fields`.
Invalid/unusable = `Valid == false` (reasons in `ValidationErrors`). `UNVERIFIED`: whether a totally unparseable number throws `SdkException<RawError>` (e.g. 404/400) instead of returning `Valid == false` — only live traffic settles it. **Defensive directive: treat BOTH (`Valid != true` OR non-empty `ValidationErrors`) AND a thrown `SdkException<RawError>` as "not a usable SMS destination"; read the failure body via `ex.Error.ReadAsString()` for the log.**

### Enums (enums.md; namespace `TwilioSdk.Models.Enums`; `StringEnum<T>` — use the static members, not C# enum syntax)

- `MessageEnumStatus`: `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.
- `MessageEnumScheduleType`: `Fixed (fixed)` (only value).
- `MessageEnumUpdateStatus`: `Canceled (canceled)` (only value — this is how cancellation is expressed).
- `MessageEnumDirection`: `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.
- `MessageEnumContentRetention`: `Retain (retain)`, `Obfuscate (obfuscate)` · `MessageEnumAddressRetention`: `Retain (retain)`, `Obfuscate (obfuscate)` · `MessageEnumTrafficType`: `Free (free)` · `MessageEnumRiskCheck`: `Enable (enable)`, `Disable (disable)` (CreateMessage-only; not needed for the core flow).
- `ValidationError` (lookup reasons): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

Scheduling-window constraints (minimum lead time / maximum horizon for `sendAt`) are **not carried in the SDK surface** — `UNVERIFIED`. Defensive directive: always send a future-dated UTC `sendAt`, and surface a 400 body via `RawError` to the caller rather than swallowing it.

### Client construction / auth / server-node facts (sdk-map.md; `TwilioSdkClientOptions.cs`; `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `Servers/Default4Options.cs`; `Core/Authentication/Basic/BasicAuthCredentials.cs`)

- `TwilioSdk.TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` — the only constructor.
- DI: `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection services, Action<TwilioSdkClientOptions>? configure = null)` — registers the client as a **singleton** built from `IHttpClientFactory.CreateClient()`.
- `TwilioSdk.TwilioSdkClientOptions` properties: `Environment: ServerEnvironment` (default `ServerEnvironment.Default()`), `Retry: RetryOptions` (default `RetryOptions.Default()`), `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?`.
- Auth: `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { required string Username { get; init; } required string Password { get; init; } }`. Username = AccountSid (or API-key SID), Password = AuthToken (or API-key secret). Source XML doc: account SID + auth token should be limited to local testing; API keys preferred.
- `TwilioSdk.Servers.ServerEnvironment` — member `Production`.
- **BaseUrl override (messaging only)**: `TwilioSdk.ServerOptions` carries one property per server node (`Default`, `Default1` … `Default14`), each a `TwilioSdk.Servers.Default*NOptions` with `.Production.BaseUrl` (settable string). Messaging ops run on node `Default (api)` (default `https://api.twilio.com`); the lookup op runs on node `Default4 (lookups)` (default `https://lookups.twilio.com`). Therefore: `options.Server.Default.Production.BaseUrl = configuredBaseUrl;` re-points **every messaging-API call verbatim** and leaves Lookup untouched (override `Server.Default4.Production.BaseUrl` only if a separate lookups host is ever configured). There is no edge/region parameter and no per-call URL parameter — the per-node `Production.BaseUrl` is the mechanism.

### Error handling (sdk-map.md error model; `Core/Exceptions/SdkException.cs`; `Core/ErrorResponse/RawError.cs`)

- **Every in-scope operation is Case B**: throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. No typed `{Operation}Error`, no `TryGet…` status accessors, and **no no-throw `…Result` variant exists anywhere in this SDK**.
- `RawError` members: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Twilio error bodies carry `code` / `message` / `more_info` / `status`; for Case B the SDK deserializes nothing — define an app-local DTO with those four wire names and read it via `ex.Error.ReadAsJson<YourDto>()`; fall back to `ReadAsString()` when the body doesn't match (defensive — body shape is `UNVERIFIED` per-status from the SDK surface alone).
- 400 (invalid number/body), 401 (bad auth), 404 (unknown SID) all arrive as the same `SdkException<RawError>` — branch on `ex.Error.StatusCode`.

## 3. Trap notes

> ⚠ Step 1 (client registration) — how the `HttpClient`/handler pair behind `TwilioSdkClient` is created and lived determines socket exhaustion vs. stale-DNS behavior, and the DI extension's shape (singleton over `IHttpClientFactory`) is not a decision to reverse-engineer from the constructor. **MUST load `dotnet-client-initialization`** before wiring the client.

> ⚠ Step 1 (auth) — when credentials must be set relative to client construction, and how secrets flow from configuration without hardcoding, are not visible from the options type. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

> ⚠ Steps 2–5, 7–9 (every call) — `CreateMessage` has 24 and `ListMessage` 8 nullable parameters with **no C# defaults**; a positional call mis-binds silently (e.g. `ListMessage`'s adjacent `dateSent`/`dateSentQuery`/`dateSentQueryQuery`, all `DateTimeOffset?`). What correct call discipline looks like (named arguments, explicit `null`s) is a skill topic. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–9 (models) — enums are `StringEnum<T>`, not C# enums: how you construct, compare, and switch on `MessageEnumStatus`/`ValidationError` differs from ordinary enum code, records are immutable with `init`-only setters, and unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** before touching response fields.

> ⚠ Step 10 (error boundary) — Case A/B mechanics: `TryGetRawError` is not a catch-all on typed errors, and which exception types actually reach a `catch` is not derivable from the signatures. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 1 (resilience) — whether a failed `CreateMessage` POST can be re-sent by the retry layer decides whether a transient fault can emit a **duplicate SMS**, and what `RetryOptions.Timeout` actually bounds decides whether your cancellation story holds. Neither is visible from the options list. **MUST load `dotnet-configuration-resilience`** before tuning `Retry` or relying on defaults.

> ⚠ Tests — the seam for faking the SDK without live calls is specific to this client shape. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — step 1 (client construction & DI registration).
- `dotnet-authentication` — step 1 (credentials wiring).
- `dotnet-calling-endpoints` — steps 2–9 (every operation call).
- `dotnet-models` — steps 2–9 (records, StringEnum enums, immutability).
- `dotnet-error-handling` — step 10 (the exception boundary).
- `dotnet-configuration-resilience` — step 1 (retries, timeouts, base-URL behavior).
- `dotnet-testing` — test seam for the integration layer.

Mandatory hazard rows for the error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumption (stated in brief):** Lookup **v2** (`FetchPhoneNumber3`) is used for registration-time validation — it is the only lookup returning `Valid`/`ValidationErrors`; v1 (`LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber`) carries no validity verdict and was rejected for this capability.
- **Assumption:** "canonical form" = `LookupResponse.PhoneNumber` (E.164). `NationalFormat` is display-only; do not store it as the destination.
- **Assumption:** scheduled-send window rules (min lead / max horizon for `sendAt`) are enforced provider-side and are not in the SDK surface; the plan validates future-dated UTC and surfaces 400s. Marked `UNVERIFIED` above.
- **Assumption:** redaction = `UpdateMessage` with empty `Body` (record survives), per the operation's own note; `DeleteMessage` is documented for the opposite outcome and is excluded from capability 6.
- **Blocker (version drift to verify at install time):** this sheet is grounded in the bundled SDK map pinned at source commit `51fdf48` ("Publish v2.0.0 SDK" — root namespace `TwilioSdk`, client `TwilioSdkClient`). The upstream repo's current `main` has since been regenerated under a different shape (root namespace `Twilio`, client `TwilioClient`), and the brief itself says "root namespace Twilio". `dotnet add package AsadAli.TwilioSdk` floats to the latest release, so **if the installed package's names disagree with this sheet, trust the compiler** and route the failing rows back to the twilio-sdk agent for re-grounding — do not patch from memory.
