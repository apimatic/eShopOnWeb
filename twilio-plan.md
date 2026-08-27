# Twilio order-notification integration plan

## 1. Scope & sequence

| Step | Application work | Twilio operations |
|---|---|---|
| 1 | Install `AsadAli.TwilioSdk` version-less; bind and validate the exact `Twilio:` keys; register one long-lived client over a named `HttpClient`. Override only the messaging server node when `Twilio:BaseUrl` is set. | client construction only |
| 2 | Register a shopper contact: ask Lookup v2, require `Valid == true` and a nonblank returned `PhoneNumber`, and persist that provider-canonical number. Reject and do not persist on invalid/provider failure. | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | For order placed/dispatched/cancelled, commit the order state independently of notification success. Snapshot only active contact IDs, create one local notification per destination, call Twilio, and persist provider SID/status/error fields best-effort. Never log `To` or message bodies. | `Api20100401Message.CreateMessage`; `FetchMessage` for later refresh |
| 4 | On dispatch, send the immediate dispatch SMS and create the delivery follow-up at `UtcNow + 3 days` with provider scheduling (`ScheduleType.Fixed`, `SendAt`, `MessagingServiceSid`); do not build an application timer. | `CreateMessage` twice per active destination |
| 5 | On cancel, persist the order cancellation, send its SMS, and cancel every provider-scheduled unsent follow-up by its stored provider SID. Persist the returned `Canceled` outcome. | `UpdateMessage(status: MessageEnumUpdateStatus.Canceled)`; cancellation-message `CreateMessage` |
| 6 | With no callbacks available, refresh each notification that has a provider SID before reporting it; retain last-known state if refresh fails so reads still succeed. | `FetchMessage` |
| 7 | Resend only an active destination's terminal non-delivery. Reserve `(originalNotificationId, callerIdempotencyKey)` uniquely in eShop before Twilio; repeated keys return the already-created local notification ID and never call Twilio again. A fresh key creates a distinct local notification and send. | `FetchMessage`, then `CreateMessage` |
| 8 | Dispose provider content without deleting the provider record: redact with `UpdateMessage`, verify through `FetchMessage`, then erase the local text. Keep local/provider identifiers, type, timestamps, status and error outcome. | `UpdateMessage(body: "", status: null)`, then `FetchMessage` |
| 9 | Reconcile the full requested range: call `ListMessage` with `from: Twilio:FromNumber` in the provider query, day-covering sent-date bounds, `PageSize: 1000`, and every returned page token; then apply the exact ISO-instant bounds and full-outer-join by provider SID against eShop records. Never query account-wide traffic and filter sender locally. | `ListMessage` |

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

All calls are async/throw-only; no operation below has a no-throw `...Result` sibling.

| Purpose / controller | Exact generated signature | Request/path/query fields used | Response fields read | Error / pagination | Source |
|---|---|---|---|---|---|
| Validate and canonicalize · `client.LookupsV2PhoneNumber` | `Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Path `PhoneNumber <- phoneNumber` required. Pass all 15 nullable non-defaulted params explicitly as `null`; no paid/enrichment field is needed for basic validity/canonicalization. | Direct `TwilioSdk.Models.LookupResponse`: `PhoneNumber (phone_number): string?`, `Valid (valid): bool?`, optionally `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`. Accept only `Valid is true` plus nonblank `PhoneNumber`; store returned `PhoneNumber`. | Case B: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. No pagination. | `operations/LookupsV2PhoneNumber.md`; `records-4-Li-Me.md`; `enums.md` |
| Create immediate or scheduled SMS · `client.Api20100401Message` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Path `AccountSid <- accountSid`; form/query wire fields used: `To <- to`, `From <- from`, `MessagingServiceSid <- messagingServiceSid`, `Body <- body`, and for follow-up `ScheduleType <- scheduleType`, `SendAt <- sendAt`. Immediate: `scheduleType:null`, `sendAt:null`; follow-up: `MessageEnumScheduleType.Fixed`, future `DateTimeOffset`. Pass every other nullable non-defaulted param explicitly `null`. No `StatusCallback` because app is unreachable. The generated method adds an `Idempotency-Key` header with a new GUID internally; it exposes no caller-key parameter. | Direct `TwilioSdk.Models.ApiV2010AccountMessage`: persist/read `Sid (sid): string?`, `Status (status): MessageEnumStatus?`, `From (from): string?`, `To (to): string?`, `MessagingServiceSid (messaging_service_sid): string?`, `DateCreated (date_created): string?`, `DateSent (date_sent): string?`, `DateUpdated (date_updated): string?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`. Do not require `Delivered` from the create response; it reports an early provider state. | Case B `SdkException<RawError>` with the direct accessors above. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md`; `enums.md`; generated method body `Api/Api20100401Message.cs` (fresh internal GUID header) |
| Refresh one provider message · `client.Api20100401Message` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Required paths `AccountSid`, provider message `Sid`. | Same direct message model/fields as create; overwrite last-known provider status/error/timestamps, never the immutable local audit identity. | Case B `SdkException<RawError>`. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| Cancel scheduled follow-up · `client.Api20100401Message` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Required paths; pass `body: null`, `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled`. Update's documented purposes include cancelling not-yet-sent messages. | Same direct message model; require/persist returned provider `Sid` and `Status`; verify `Canceled` through response or subsequent fetch. | Case B `SdkException<RawError>`. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md`; `enums.md` |
| Provider-side body disposal · `client.Api20100401Message` | Same `UpdateMessage(...)` signature as prior row. | Pass `body: ""`, `status: null`; the operation is explicitly documented for redacting message body text. Do not use `DeleteMessage`, because deletion would remove the provider record instead of retaining delivery fact. | Read returned direct model and immediately `FetchMessage`; treat disposal complete only when provider `Body` is null/empty, then clear local text. | Case B `SdkException<RawError>`. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md`; `UNVERIFIED` for the empty-string redaction sentinel (surface/source documents redaction but not the sentinel; verify defensively) |
| Sender-filtered reconciliation · `client.Api20100401Message` | `Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Always `from: Twilio:FromNumber`; `to:null`; `dateSent:null`; wire `DateSent< <- dateSentQuery` is the upper bound and `DateSent> <- dateSentQueryQuery` the lower; `pageSize:1000` (documented max), `page:null`, first `pageToken:null`, then the provider token extracted from `NextPageUri`. Provider docs describe sent-date filters at GMT-date granularity, so widen to full covering days and apply exact requested instants after retrieval. | `TwilioSdk.Models.ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri (next_page_uri): string?`, plus `Page`, `PageSize`, `Start`, `End`. Consume messages and continue until `NextPageUri` is null; parse its `PageToken` query value for the next call. | Case B `SdkException<RawError>`. Map marks it non-auto-paginated; paging is manual despite `PageSize`/`PageToken`. A caller-independent page cap/no-progress guard may detect provider loops, but hitting it must fail the report as incomplete, never return silent partial data. | `operations/Api20100401Message.md`; `records-4-Li-Me.md`; generated XML docs in `Api/Api20100401Message.cs` |

### Enums used

All are `TwilioSdk.Models.Enums` string-enum records; persist/report `.Value` so provider outcomes survive beyond the request that obtained them.

| Type | Exact members and wire values | Source |
|---|---|---|
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `Fixed (fixed)` | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `Canceled (canceled)` | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `enums.md` |
| `TwilioSdk.Models.Enums.ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | `enums.md` |

### Client, auth, server nodes, and configuration

| Fact | Exact contract / consequence | Source |
|---|---|---|
| Package/client | Package `AsadAli.TwilioSdk` (install version-less); `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient, TwilioSdk.TwilioSdkClientOptions)`. Client/controllers are safe to retain behind one long-lived HTTP pipeline. | `sdk-map.md`; `TwilioSdkClient.cs` |
| Auth | `TwilioSdk.TwilioSdkClientOptions.AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`; set `Username` from `Twilio:AccountSid`, `Password` from `Twilio:AuthToken` before constructing. | `sdk-map.md` Servers & auth; `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.TwilioSdkClientOptions.Environment = TwilioSdk.Servers.ServerEnvironment.Production`. | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Messaging server | All five `Api20100401Message` operations use server node `Default`; production default is `https://api.twilio.com`. If and only if `Twilio:BaseUrl` is nonblank, assign it verbatim to `options.Server.Default.Production.BaseUrl`. Do not normalize, append, or trim it. | `operations/Api20100401Message.md`; `ServerOptions.cs`; `Servers/DefaultOptions.cs` |
| Lookup server isolation | `FetchPhoneNumber3` uses server node `Default4`; its production default is `https://lookups.twilio.com`. Never apply `Twilio:BaseUrl` to `options.Server.Default4`; therefore the optional override affects send/read/list/update messaging calls and no other Twilio capability. | `operations/LookupsV2PhoneNumber.md`; `ServerOptions.cs`; `Servers/Default4Options.cs` |
| DI extension limitation | `AddTwilioSdkClient` registers a singleton and resolves the unnamed factory client. Prefer explicit singleton construction over a named `HttpClient` so timeout/handlers do not affect other unnamed consumers. | `ServiceCollectionExtensions.cs` |
| Exact settings | Bind exactly `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl`. AccountSid/AuthToken/FromNumber/MessagingServiceSid are startup-required; BaseUrl is optional. Never log/return credentials or destination numbers. | User mandate; auth shape from `sdk-map.md` |
| Sender/service pairing | Pass configured `FromNumber` and `MessagingServiceSid` to all creates so provider records remain attributable to the configured sender and scheduled sends carry the required service. Confirm the scheduled-create response `From` equals configured `FromNumber`; otherwise record an integration failure rather than pretending reconciliation covers it. | `YOUR CALL — not in the map`; scheduling requirement for service from `MessageEnumScheduleType` note in `enums.md`; simultaneous live acceptance `UNVERIFIED` |
| Caller idempotency | `CreateMessage` has no caller idempotency parameter and internally creates a fresh GUID header. Enforce resend idempotency transactionally in eShop before invoking it; store the local result notification ID with the key. | generated method body `Api/Api20100401Message.cs`; application persistence is `YOUR CALL — not in the map` |

### Persistence consequences forced by provider contracts

Each local notification needs at minimum: local `notificationId`; order/owner/contact IDs; kind; provider SID; provider status raw value; provider `From`/`MessagingServiceSid`; provider created/sent/updated timestamps; error code/message; scheduled send time; local attempt timestamps; body (nullable/redactable); content-disposed marker; and resend parent/idempotency key. Provider SID/status/error fields are nullable because a transport/provider failure must not roll back order state. Deleted contact IDs must remain referential audit data but be ineligible for future send/resend. These are application choices, not SDK model requirements (`YOUR CALL — not in the map`).

## 3. Trap notes

- ⚠ Step 1 (client registration) — client/`HttpClient` lifetime, named-pipeline isolation and singleton DNS behavior can turn a correct call surface into stale connections or shared-handler side effects. **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ Step 1 (auth) — nullable SDK credentials make missing secrets a late provider failure unless startup validation owns the boundary. **MUST load `dotnet-authentication`** before setting credentials.
- ⚠ Steps 2–9 (calls) — long generated signatures contain nullable parameters with no C# defaults, and response objects are direct resources versus envelopes per operation. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–9 (models) — string-enum read/write behavior and nullable response members affect status persistence and validation. **MUST load `dotnet-models`** before mapping provider models.
- ⚠ Steps 2–9 (error boundary) — every scoped operation is Case B and throws; swallowing the wrong exception shape either breaks the order-success guarantee or hides a disposal/reconciliation failure. **MUST load `dotnet-error-handling`** before writing catches.
- ⚠ Steps 3–9 (write safety/resilience) — transport retries, timeouts, total request budgets and manually-driven reconciliation pages determine whether writes duplicate or reports truncate. **MUST load `dotnet-configuration-resilience`** before tuning the client or loops.
- ⚠ Step 9 (manual pagination) — `ListMessage` is not SDK-auto-paginated; the full-range promise depends on advancing provider page tokens with a no-progress/deadline guard and surfacing incomplete enumeration. **MUST load `dotnet-configuration-resilience`** before implementing reconciliation.
- ⚠ Steps 3–8 (write tests) — the `HttpClient` handler is the supported seam for asserting serialized fields, retry counts and no second upstream call for a duplicate resend key. **MUST load `dotnet-testing`** before writing tests.
- ⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.
- ⚠ Error boundary — a **non-2xx** body that does not match its operation's generated error shape throws `System.Text.Json.JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client/DI and HTTP lifetime.
- `dotnet-authentication` — Step 1 credentials/startup validation.
- `dotnet-calling-endpoints` — Steps 2–9 exact invocation and response shapes.
- `dotnet-models` — Steps 2–9 enums/nullability/model mapping.
- `dotnet-error-handling` — Steps 2–9 exception boundary, including both `JsonException` directions.
- `dotnet-configuration-resilience` — Steps 1 and 3–9 retry, timeout, base-URL and manual-pagination behavior.
- `dotnet-testing` — Steps 2–9 provider seam and retry/idempotency coverage.

## 5. Assumptions & Blockers

### Assumptions

- The requested follow-up interval is three days; this is an application policy because the brief says only “a few days later.”
- Reconciliation range semantics are provider `DateSent`; exact ISO instants are enforced after a provider-side sender-filtered, day-covering query because the generated provider docs describe sent-date filters at GMT-date granularity.
- For SMS, resend eligibility is restricted to refreshed terminal `Failed` or `Undelivered`; other statuses are not proof that the shopper did not receive the message.
- A shopper with multiple active registered destinations is notified once per active destination; deleting one disables only future sends/resends to that destination and retains historical audit rows.

### Blockers

- None. The SDK exposes validation/canonicalization, create/schedule, fetch/list, scheduled cancellation and body-redaction surfaces. The empty-string redaction sentinel and simultaneous `From` plus `MessagingServiceSid` acceptance are live-only semantics, so the plan mandates immediate provider-response verification rather than leaving either open to the implementer.
