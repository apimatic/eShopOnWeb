# Twilio integration plan

## 1. Scope & sequence

| Step | Provider interaction | Application consequence | Source |
|---|---|---|---|
| 1. Install/configure | Add NuGet package `AsadAli.TwilioSdk` **version-less** (the plugin deliberately floats to the latest release). Build one long-lived `HttpClient`; construct `TwilioSdk.TwilioSdkClient` with credentials and server options below. | Bind only `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optional `Twilio:BaseUrl`; validate required values at startup; never log credentials or destination numbers. | `sdk-map.md`; `TwilioSdkClientOptions.cs`; `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `Servers/Default4Options.cs` |
| 2. Register contact | Call `client.LookupsV2PhoneNumber.FetchPhoneNumber3`; accept only `Valid == true` and nonblank `PhoneNumber`; store that provider-returned `PhoneNumber` as canonical. | Lookup rejection or an invalid/indeterminate response rejects registration. The provider's `Valid` flag is format/usability validation; it does not promise later carrier delivery, so a valid-but-unreachable fixture remains a valid registration. | `operations/LookupsV2PhoneNumber.md`; `records-4-Li-Me.md`; `enums.md` |
| 3. Send immediate notifications | Call `client.Api20100401Message.CreateMessage` for placed, dispatched, cancelled, and legitimate resend notifications; retain the returned provider `Sid`, status value, timestamps, error fields, from/to, and local notification identifier. | Provider failure or malformed response is recorded as a notification failure/unknown outcome and must not roll back the order transition. No contact means no provider call. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| 4. Schedule delivery follow-up | Call the same `CreateMessage` with `MessageEnumScheduleType.Fixed`, a future `sendAt`, and `Twilio:MessagingServiceSid`; pass `Twilio:FromNumber` so the configured sender remains the provider-side origin used for reconciliation. | Persist the scheduled message's provider `Sid`. Scheduling is done by the provider, never by an application send timer. | `operations/Api20100401Message.md`; `enums.md` |
| 5. Cancel queued follow-up | Call `UpdateMessage` with `body: null`, `status: MessageEnumUpdateStatus.Canceled` for every not-yet-sent follow-up when its order is cancelled. Use the same compensation when a contact number is deleted, because no already-scheduled message may later reach that removed number. | Persist the returned provider status. If cancellation is temporarily indeterminate, the application must retain a cancellation-needed state for compensation; the SDK exposes no atomic order-transition/provider-cancel operation. The application's persistence/retry design is **YOUR CALL — not in the map**. | `operations/Api20100401Message.md`; `enums.md` |
| 6. Refresh delivery state | Call `FetchMessage(accountSid, providerSid, ...)` from authenticated read/report paths and update local delivery status/error/timestamps best-effort. | With no callback URL, provider polling is the only SDK path for current status. A fetch failure returns the last locally known state plus staleness rather than erasing metadata. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| 7. Idempotent resend | Before `CreateMessage`, atomically claim the caller's `(original notification, idempotency key)` in application persistence; a repeat returns the prior result and makes no provider call. A fresh key may create one new notification. | The SDK method has no caller-supplied idempotency-key parameter; its generated method internally creates a new provider `Idempotency-Key` GUID per invocation. Therefore the endpoint's idempotency guarantee is application-owned. | `Api/Api20100401Message.cs` (source; indexed by `operations/Api20100401Message.md`) |
| 8. Dispose provider content | Call `UpdateMessage` with `body: ""`, `status: null`, then verify the returned/fetched `Body` is null/empty before clearing local content. Retain local SID/status/error/timestamps. | The map says `UpdateMessage` is the redaction operation, but does not document the exact redaction sentinel. Empty body is the concrete call to live-verify; if body remains retrievable, report disposal failure and do not claim success or clear the only recovery state. **UNVERIFIED — live response decides.** Do not use `DeleteMessage`, because that deletes the provider resource rather than preserving its status record. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| 9. Reconcile full range | Repeatedly call `ListMessage` with server-side `from: Twilio:FromNumber`, `dateSentQuery: to` (`DateSent<`), and `dateSentQueryQuery: from` (`DateSent>`). Follow `NextPageUri` until absent; extract its opaque `PageToken` and pass it to the next SDK call while preserving `from` and date bounds. | Compare the complete provider set and local set by provider SID, emitting provider-only and local-only rows. Never obtain a broader all-senders answer and filter locally. If a nonempty next URI has no usable page token, fail the report explicitly rather than returning a partial report. | `operations/Api20100401Message.md`; `records-4-Li-Me.md` |

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

### SDK identity, client, authentication, and servers

| Fact | Exact contract | Source |
|---|---|---|
| Package | `AsadAli.TwilioSdk`; run `dotnet add package AsadAli.TwilioSdk` without a version. The bundled contract was generated from source commit `51fdf48`; the map supplies no package semver and explicitly requires version-less install. | `sdk-map.md` |
| Client | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)`; controllers are properties including `Api20100401Message` and `LookupsV2PhoneNumber`. | `sdk-map.md`; `TwilioSdkClient.cs` |
| Auth | `TwilioSdk.TwilioSdkClientOptions.AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`; construct credentials with required init members `Username: string = Twilio:AccountSid`, `Password: string = Twilio:AuthToken`. | `sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.TwilioSdkClientOptions.Environment: TwilioSdk.Servers.ServerEnvironment`; use `TwilioSdk.Servers.ServerEnvironment.Production`. | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Messaging server | Every in-scope messaging operation is on `Default (api)`, whose default is `https://api.twilio.com`. When `Twilio:BaseUrl` is nonblank, assign it unchanged to `options.Server.Default.Production.BaseUrl`; do not trim, append a version path, or change any other server node. This covers create/fetch/list/update and would also cover delete because their operation rows all use `Default (api)`. | `operations/Api20100401Message.md`; `ServerOptions.cs`; `Servers/DefaultOptions.cs` |
| Lookup server | Lookup uses `Default4 (lookups)`, default `https://lookups.twilio.com`. Leave `options.Server.Default4.Production.BaseUrl` unchanged even when `Twilio:BaseUrl` is set. | `operations/LookupsV2PhoneNumber.md`; `ServerOptions.cs`; `Servers/Default4Options.cs` |
| Other options | `TwilioSdkClientOptions` also exposes `Retry: TwilioSdk.Core.Configuration.RetryOptions`, `Logging: TwilioSdk.Core.Configuration.LoggingOptions`, and `Server: TwilioSdk.ServerOptions`. Configure only after loading the resilience/client skills below. | `sdk-map.md`; `TwilioSdkClientOptions.cs` |

### Operations

All calls are throw-only; there are no `...Result` no-throw variants.

| Controller / operation | Exact generated signature | Parameters used and wire semantics | Response read by integration | Errors / pagination | Source |
|---|---|---|---|---|---|
| `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` is required path input. Pass all 15 nullable arguments (`fields` through `partnerSubId`) explicitly as `null`; no paid enrichment is needed. | Direct response, not an envelope: `PhoneNumber (phone_number): string?`, `Valid (valid): bool?`, `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`, plus optional `CallingCountryCode`, `CountryCode`, `NationalFormat`. Accept only literal `Valid == true` and nonblank `PhoneNumber`; persist `PhoneNumber`. | Case B: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; `RawError.StatusCode: HttpStatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. No pagination. | `operations/LookupsV2PhoneNumber.md`; `records-4-Li-Me.md`; `enums.md` |
| `client.Api20100401Message.CreateMessage` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Required: `accountSid = Twilio:AccountSid`, `to = stored canonical number`. Immediate SMS: `from = Twilio:FromNumber`, `messagingServiceSid = null`, `body = text`, `scheduleType/sendAt = null`. Scheduled SMS: `from = Twilio:FromNumber`, `messagingServiceSid = Twilio:MessagingServiceSid`, `scheduleType = MessageEnumScheduleType.Fixed`, `sendAt = selected future DateTimeOffset`, `body = text`. Pass `statusCallback = null` because no public callback. Pass every other nullable parameter explicitly as `null`. Wire names are `To`, `ScheduleType`, `SendAt`, `From`, `MessagingServiceSid`, `Body`; the SDK sends form URL encoding. | Direct `ApiV2010AccountMessage` (fields below). A nominal 2xx response without nonblank `Sid` is not a durable provider success; persist an unknown/failure outcome and do not discard diagnostic metadata. | Case B `SdkException<RawError>` with the accessors above. No pagination. Source adds a generated `Idempotency-Key` header containing a new GUID per method invocation; no caller parameter exists. | `operations/Api20100401Message.md`; `Api/Api20100401Message.cs`; `records-1-Ac-Ca.md`; `enums.md` |
| `client.Api20100401Message.FetchMessage` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid = Twilio:AccountSid`; `sid = locally persisted provider message SID`. | Direct `ApiV2010AccountMessage`; refresh status/error/timestamps and use `Body` only where content has not been disposed. | Case B `SdkException<RawError>`. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| `client.Api20100401Message.UpdateMessage` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Cancel scheduled: `body = null`, `status = MessageEnumUpdateStatus.Canceled` (wire `Status=canceled`). Redact content: `body = ""`, `status = null` (wire `Body=`), then fetch/inspect. Both nullable parameters have no C# default and must be passed explicitly. | Direct updated `ApiV2010AccountMessage`; persist returned status/error/timestamps. Redaction sentinel outcome remains **UNVERIFIED** until live provider response/fetch confirms body no longer retrievable. | Case B `SdkException<RawError>`. No pagination. Notes explicitly identify this operation for both body redaction and not-yet-sent cancellation. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md`; `enums.md` |
| `client.Api20100401Message.ListMessage` | `Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid = Twilio:AccountSid`; `to = null`; `from = Twilio:FromNumber` (wire `From`, provider-side filtering); `dateSent = null`; `dateSentQuery = requested to` (wire `DateSent<`); `dateSentQueryQuery = requested from` (wire `DateSent>`); choose/pass a bounded `pageSize` only if application policy defines one, else null; first page `page/pageToken = null`; later pages use the opaque `PageToken` from `NextPageUri` and preserve all filters. The SDK contract exposes strict `<`/`>` operators, not inclusive bounds. | Envelope `ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `Page`, `PageSize`, `Start`, `End`, `Uri`, `FirstPageUri`. Treat null `Messages` as empty. | Case B `SdkException<RawError>`. The generated SDK marks no automatic pagination helper; manual iteration through `NextPageUri`/`pageToken` is required until no next URI. The exact provider page-token URI round trip is **UNVERIFIED** until the required live reconciliation run; never silently truncate. | `operations/Api20100401Message.md`; `records-4-Li-Me.md` |

### Message record and enum values

`TwilioSdk.Models.ApiV2010AccountMessage` is a direct response/list item. Relevant optional fields (C# / JSON wire / type):

| Field | Type | Use |
|---|---|---|
| `Sid` / `sid` | `string?` | Provider identifier; persist for every later fetch/update/reconciliation action. |
| `Body` / `body` | `string?` | Content until disposed; never log. |
| `Status` / `status` | `TwilioSdk.Models.Enums.MessageEnumStatus?` | Persist `Status?.Value`, not just known static-member identity, so a new provider value survives. |
| `Direction` / `direction` | `TwilioSdk.Models.Enums.MessageEnumDirection?` | Reconciliation metadata. |
| `From` / `from`; `To` / `to` | `string?`; `string?` | Reconciliation/contact metadata; destination must never be logged. |
| `MessagingServiceSid` / `messaging_service_sid` | `string?` | Scheduling/provider metadata. |
| `DateCreated` / `date_created`; `DateUpdated` / `date_updated`; `DateSent` / `date_sent` | `string?` | Provider timestamps are strings in this generated model; parse best-effort and retain raw value on parse failure. |
| `ErrorCode` / `error_code` | `int?` | Delivery failure code. |
| `ErrorMessage` / `error_message` | `string?` | Delivery failure description; do not let it replace structured status/code. |
| `AccountSid` / `account_sid`; `Uri` / `uri` | `string?`; `string?` | Provider metadata. |

| Enum type | Literal C# members and wire values | Source |
|---|---|---|
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `Fixed (fixed)` | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `Canceled (canceled)` | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | `enums.md` |
| `TwilioSdk.Models.Enums.ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | `enums.md` |

`StringEnum<T>` values expose public `.Value`, preserve unknown deserialized strings, and provide `IsKnownValue()`. Do not map an unknown delivery status to success or failure; retain it as an unknown provider outcome. Source: `Core/Enum/StringEnum.cs`, `Core/Enum/TypedEnum.cs`.

### Error boundary consequences

| Context | Contract consequence | Source |
|---|---|---|
| Send/schedule/fetch/update/list | All five messaging operations are Case B and throw `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` on modeled non-2xx responses. Read `ex.Error.StatusCode` and bounded/sanitized body data; never log auth or phone number. | `operations/Api20100401Message.md`; `sdk-map.md` |
| Lookup | Also Case B with the same `RawError` surface. Unlike an order notification send, a provider lookup failure must reject registration because canonical validity was not established. | `operations/LookupsV2PhoneNumber.md` |
| Non-SDK failures | Transport, cancellation/timeout, and JSON failures can cross the boundary separately; map them according to the required companion skill. | `sdk-map.md`; `YOUR CALL — application HTTP contract not in map` |

## 3. Trap notes

- ⚠ Step 1 (client registration) — `HttpClient` ownership and SDK wrapper lifetime can exhaust sockets or dispose shared transport if wired incorrectly. **MUST load `dotnet-client-initialization`** before registration.
- ⚠ Step 1 (authentication) — credential timing/rotation and secret sourcing determine whether every call carries the right scheme without exposing the token. **MUST load `dotnet-authentication`** before credentials are wired.
- ⚠ Steps 2–9 (calls) — optional parameters have no C# defaults and positional calls can silently bind the wrong provider field. **MUST load `dotnet-calling-endpoints`** before the first operation call.
- ⚠ Steps 2–9 (models) — nullable response fields, string-backed enums, unknown enum values, and wire-name/C#-name differences can corrupt persisted provider state. **MUST load `dotnet-models`** before mapping models.
- ⚠ Steps 2–9 (error boundary) — Case-B errors and non-SDK failures can escape or be classified as retryable incorrectly, causing order actions to fail or deterministic rejections to be retried. **MUST load `dotnet-error-handling`** before any try/catch.
- ⚠ Step 1 and write operations (resilience) — retry/timeout/server-node choices determine whether a failed write may execute more than once and whether the messaging-only base override reaches every required call. **MUST load `dotnet-configuration-resilience`** before configuring resilience or servers.
- ⚠ Step 9 (pagination) — the generated list call has no automatic paginator; mishandling its token/next-page metadata returns a plausible but incomplete reconciliation. **MUST load `dotnet-configuration-resilience`** before implementing the range walk.
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `System.Text.Json.JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

- ⚠ Verification tests — faking generated controller internals instead of the HTTP seam makes tests brittle and misses serialization/query/header behavior. **MUST load `dotnet-testing`** before integration tests.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — client construction, `HttpClient` lifetime, and DI registration in Step 1.
- `dotnet-authentication` — credentials and secret rotation in Step 1.
- `dotnet-calling-endpoints` — exact call construction for Steps 2–9.
- `dotnet-models` — lookup/message response and enum handling for Steps 2–9.
- `dotnet-error-handling` — the error boundary for every provider call, including both `JsonException` directions.
- `dotnet-configuration-resilience` — retries, timeout, messaging server override, polling, and manual pagination.
- `dotnet-testing` — HTTP-seam unit/integration tests and provider-edge cases.

## 5. Assumptions & Blockers

### Assumptions

- Contact-number usability means the provider's V2 lookup returns `Valid == true` and a nonblank canonical `PhoneNumber`; later carrier delivery remains a separate outcome, which is required for the valid-but-unreachable fixture.
- The resend endpoint's caller idempotency key is enforced atomically in application persistence because `CreateMessage` exposes no caller-supplied key and generates its own provider header.
- The application selects the exact “few days” delay and supplies that future `DateTimeOffset`; the SDK map documents fixed scheduling through a Messaging Service but no allowed scheduling horizon. Provider acceptance at the selected delay is **UNVERIFIED** until the live schedule test.
- Provider body redaction uses `UpdateMessage(body: "", status: null)` and is considered complete only after the provider response/fetch no longer exposes content. The exact empty-body redaction behavior is **UNVERIFIED** because the SDK map/source names the capability but does not document the sentinel.
- Reconciliation interprets the SDK's strict provider filters as `DateSent > from` and `DateSent < to`; inclusivity beyond those literal wire operators is **YOUR CALL — not in the map** and must be documented in the HTTP response contract.
- Application persistence, authorization, ownership checks, notification text, order state/concurrency, compensation scheduling, and local identifier shapes are **YOUR CALL — not in the map**.

### Blockers

- None.
