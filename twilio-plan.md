# Twilio order-notification integration plan

## 1. Scope & sequence

| Step | Capability | SDK operations / implementation consequence | Source |
|---:|---|---|---|
| 1 | Install and register the client | Add the version-less `AsadAli.TwilioSdk` package to `src/PublicApi`; construct one `TwilioSdk.TwilioSdkClient` over an `IHttpClientFactory`-managed `HttpClient`; bind the five `Twilio:*` settings; configure Basic auth; override only the SDK's `Default` server node when `Twilio:BaseUrl` is set. | `sdk-map.md` (Getting a client; Servers & auth), `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs` |
| 2 | Validate and canonicalize a contact number | Call `client.LookupsV2PhoneNumber.FetchPhoneNumber3`; store `LookupResponse.PhoneNumber` only when `Valid == true` and the canonical number is non-empty. Do not store the caller's spelling. A lookup failure stores nothing. | `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md` |
| 3 | Send placed/cancelled/dispatched messages immediately | Call `client.Api20100401Message.CreateMessage` with `From = Twilio:FromNumber`, the stored canonical `To`, and `Body`; persist the returned provider `Sid`, status/outcome, error fields, timestamps, and the application notification ID. A provider failure is recorded but does not roll back the order transition. | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md` |
| 4 | Queue the delivery follow-up at the provider | Call the same `CreateMessage` with `ScheduleType = MessageEnumScheduleType.Fixed`, `SendAt = now + 3 days`, `MessagingServiceSid = Twilio:MessagingServiceSid`, and `From = Twilio:FromNumber`; persist its provider `Sid` and scheduled state immediately. This is provider scheduling, not an application timer. | `operations/Api20100401Message.md`, `enums.md` |
| 5 | Cancel an unsent follow-up | For the persisted follow-up provider SID call `UpdateMessage(body: null, status: MessageEnumUpdateStatus.Canceled)` and persist the returned state. Never use `DeleteMessage`: it would remove the resource rather than retain its delivery metadata. | `operations/Api20100401Message.md`, `enums.md` |
| 6 | Refresh/report notification state | Call `FetchMessage` by persisted provider SID; update the last-known provider state best-effort and return the last known state if refresh fails. No callback path is assumed. | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md` |
| 7 | Resend an undelivered notification | Enforce the caller key in an application idempotency record before invoking `CreateMessage`. A repeated key returns the already-created notification; a fresh key makes one new provider call and one new notification record. The SDK creates its own random provider `Idempotency-Key` per invocation and exposes no caller header parameter, so that SDK header does not implement the API's caller-key contract. | `Api/Api20100401Message.cs`; application persistence is `YOUR CALL — not in the map` |
| 8 | Dispose of provider content | Call `UpdateMessage(body: string.Empty, status: null)`, keep the returned metadata, then `FetchMessage` and require the provider `Body` to be null/empty before marking disposal complete. Never call `DeleteMessage`. | `operations/Api20100401Message.md` (Update notes explicitly include redaction), `records-1-Ac-Ca.md` |
| 9 | Reconcile a whole date range | Call `ListMessage` with `from: Twilio:FromNumber` in every request, provider date bounds, and explicit paging. Follow `NextPageUri` until absent, extracting its `PageToken` for the next SDK call; outer-join provider rows to application rows by provider SID. Apply exact `[from,to]` filtering locally after the provider's server-side `From` and date query. | `operations/Api20100401Message.md`, `records-4-Li-Me.md` |
| 10 | Verify without excess live traffic | Unit/integration-test the HTTP seam and all failure/idempotency/pagination paths; live verification sends only the two supplied configured destinations and the minimum calls needed for delivery, scheduled cancellation, resend, redaction, and reconciliation. | `sdk-map.md`; test policy is `YOUR CALL — not in the map` |

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

| Purpose / controller property | Exact generated signature | Parameters actually carried | Response fields read | Error / pagination | Source |
|---|---|---|---|---|---|
| Validate/canonicalize · `TwilioSdk.TwilioSdkClient.LookupsV2PhoneNumber` | `Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` is required. Pass `null` for all 15 optional lookup inputs (`fields` through `partnerSubId`) for the basic validity/canonicalization response. | `TwilioSdk.Models.LookupResponse`: `PhoneNumber (phone_number): string?`, `Valid (valid): bool?`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`, `CountryCode`, `NationalFormat`. Accept only `Valid == true` plus non-empty `PhoneNumber`; persist `PhoneNumber`. | Case B: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or `ReadAsBytes()`. No no-throw variant. No pagination. | `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md` |
| Create immediate, scheduled, or resend SMS · `TwilioSdk.TwilioSdkClient.Api20100401Message` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Required: `accountSid`, canonical `to`. Immediate/resend: all nullable inputs null except `from = Twilio:FromNumber`, `body = text`; do not supply callback/media/content. Scheduled: additionally `scheduleType = TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed`, `sendAt = UTC now + 3 days`, `messagingServiceSid = Twilio:MessagingServiceSid`. Keep `from = Twilio:FromNumber` so all app traffic uses the configured sender. Every nullable argument has no C# default and MUST be passed (prefer named args). | `TwilioSdk.Models.ApiV2010AccountMessage`: persist/read `Sid (sid)`, `Status (status)`, `Body (body)`, `From (from)`, `To (to)`, `MessagingServiceSid`, `DateCreated`, `DateSent`, `DateUpdated`, `ErrorCode`, `ErrorMessage`, `Direction`. | Case B raw error as above; no no-throw variant; no pagination. The generated method itself adds a fresh random `Idempotency-Key` header each invocation. | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md`, `enums.md`; auto-header: `Api/Api20100401Message.cs` |
| Fetch provider state · `TwilioSdk.TwilioSdkClient.Api20100401Message` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid = Twilio:AccountSid`; `sid` is the persisted provider message SID. | Same `ApiV2010AccountMessage` fields as create. | Case B raw error; no no-throw variant; no pagination. | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md` |
| Cancel scheduled message / redact body · `TwilioSdk.TwilioSdkClient.Api20100401Message` | `Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Cancel: `body: null`, `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled`. Redact: `body: string.Empty`, `status: null`. Both nullable args have no default and MUST be passed explicitly. | Same `ApiV2010AccountMessage`; for cancellation persist returned `Status`; for disposal preserve all metadata and verify returned/refetched `Body` null/empty. | Case B raw error; no no-throw variant; no pagination. | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md`, `enums.md` |
| Provider reconciliation list · `TwilioSdk.TwilioSdkClient.Api20100401Message` | `Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Every page: `accountSid`, `to: null`, `from: Twilio:FromNumber`, `dateSent: null`, `dateSentQuery: to` (wire `DateSent<`), `dateSentQueryQuery: from` (wire `DateSent>`), bounded `pageSize`, `page: null`, initial `pageToken: null`; subsequent calls keep all filters identical and use the token parsed from `NextPageUri`. These are server filters, especially `From`; do not make an unfiltered account-wide request. | `TwilioSdk.Models.ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri`, `Page`, `PageSize`, `Start`, `End`. Read each message's persisted fields listed above. | Case B raw error. The SDK exposes no iterator and labels pagination `none`; explicit paging is required. Stop only when `NextPageUri` is null/empty. | `operations/Api20100401Message.md`, `records-4-Li-Me.md`, `records-1-Ac-Ca.md` |

### Enums actually used

| Fully-qualified type | Members / wire values | Source |
|---|---|---|
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `Fixed (fixed)` | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `Canceled (canceled)` | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `enums.md` |

### Client, auth, server nodes, and package

| Fact | Exact contract | Source |
|---|---|---|
| Package | `dotnet add src/PublicApi package AsadAli.TwilioSdk` with no version pin. Package target is `netstandard2.0`. | `sdk-map.md` |
| Constructor | `new TwilioSdk.TwilioSdkClient(HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)`; DI alternative is `services.AddTwilioSdkClient(o => ...)`. | `sdk-map.md` |
| Authentication | `TwilioSdk.TwilioSdkClientOptions.AccountSidAuthToken` is `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`; set `Username = Twilio:AccountSid`, `Password = Twilio:AuthToken` before client construction. Both credential members are required strings. Never log/return the password. | `sdk-map.md` Servers & auth; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `options.Environment = TwilioSdk.Servers.ServerEnvironment.Production`. | `sdk-map.md` Servers & auth |
| Messaging default node | Message operations use server node `Default (api)`. Its production default is `https://api.twilio.com`. If and only if `Twilio:BaseUrl` is non-empty, assign its value verbatim to `options.Server.Default.Production.BaseUrl`. This covers create, fetch, update/redact/cancel, and list/reconcile because all use `Default`. | `operations/Api20100401Message.md`, `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Lookup node remains independent | Lookup V2 uses `Default4 (lookups)`, whose production default is `https://lookups.twilio.com`. Never apply `Twilio:BaseUrl` to `options.Server.Default4`; the override is messaging-only. | `operations/LookupsV2PhoneNumber.md`, `ServerOptions.cs`, `Servers/Default4Options.cs` |
| Configuration values | Bind exactly `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl`; validate required values except optional BaseUrl at startup. | Configuration names are user mandate; binding design is `YOUR CALL — not in the map` |
| Privacy boundary | Do not enable/request HTTP payload logging for these calls; application logs may carry application/provider notification IDs and outcome codes, never destination number, auth token, or message body. | `YOUR CALL — not in the map` |

### State and call directives forced by the SDK contract

| Concern | Directive | Source |
|---|---|---|
| Provider-owned state | Persist the provider message SID from every successful create plus the latest `MessageEnumStatus`, provider timestamps, `ErrorCode`, and `ErrorMessage`. Refetch by SID when reporting; on provider-read failure retain/report the last known state and refresh error. | `records-1-Ac-Ca.md`, `operations/Api20100401Message.md` |
| Send failure isolation | Catch the documented SDK boundary around each provider call and record a failed notification attempt; never let it roll back order create/dispatch/cancel. | App transaction design is `YOUR CALL — not in the map`; SDK error types from operation rows |
| Caller idempotency | Atomically reserve `(original notification, caller idempotency key)` before `CreateMessage`; unique storage/concurrency is the authoritative dedupe. Return the created notification ID on repeats. The SDK's random header cannot represent the caller key. | SDK header: `Api/Api20100401Message.cs`; storage is `YOUR CALL — not in the map` |
| Cancellation safety | Persist the scheduled provider SID before the dispatch response is exposed. On order cancellation, invoke `UpdateMessage(...Canceled)` for each unsent follow-up and persist/verify `Canceled`; retryable provider failure must remain visibly pending cancellation, not be represented as success. | `operations/Api20100401Message.md`, `enums.md`; retry mechanism is `YOUR CALL — not in the map` |
| Redaction safety | `UpdateMessage` is the SDK operation whose provider Notes explicitly cover redacting body text. Use an empty body, retain the resource, and verify by fetch. Mark disposal complete only after provider body is absent; keep metadata locally. | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md` |
| Reconciliation completeness | Loop all provider pages and outer-join both directions by provider SID. Include provider-only and application-only rows. Always put `Twilio:FromNumber` into the provider `From` query parameter; never filter a broader provider response after retrieval. | `operations/Api20100401Message.md`, `records-4-Li-Me.md` |

## 3. Trap notes

- ⚠ Step 1 (client registration) — client/`HttpClient` ownership and DI lifetime can exhaust connections or make tests impossible if wired at the wrong seam. **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ Step 1 (authentication) — credential timing, secret rotation, and configuration failure handling determine whether calls authenticate and whether secrets escape. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Steps 2–9 (endpoint calls) — optional parameters without C# defaults, literal named arguments, and response-envelope depth can silently bind the wrong wire query or lose data. **MUST load `dotnet-calling-endpoints`** before the first SDK call.
- ⚠ Steps 2–9 (models/enums) — generated string enums, nullable fields, required members, and JSON wire names can produce invalid requests or incorrect state mapping. **MUST load `dotnet-models`** before constructing or mapping SDK values.
- ⚠ Steps 1–9 (resilience/base URL/pagination/logging) — retry triggers can duplicate provider writes, timeout scope can exceed the intended request budget, server-node selection can route non-messaging APIs to the override, manual paging can truncate reconciliation, and logging can expose private data. **MUST load `dotnet-configuration-resilience`** before configuring the client or loops.
- ⚠ Steps 2–9 (error boundary) — Case B raw errors, status/body access, cancellation, transport exceptions, and caller-safe mapping can otherwise leak exceptions or misclassify deterministic provider rejection. **MUST load `dotnet-error-handling`** before writing any catch boundary.
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

- ⚠ Step 10 (tests) — faking generated controller internals or asserting only that a call occurred misses wire binding, pagination, provider-state, redaction, and error paths. **MUST load `dotnet-testing`** before writing integration tests.

## 4. Assumptions & Blockers

### Assumptions

- “A few days later” is implemented as three days after dispatch, in UTC; it is comfortably a future provider schedule and remains an application constant/configuration choice.
- Basic Lookup V2 defines registration usability as `Valid == true` with a non-empty provider-canonical `PhoneNumber`. A valid-format but ultimately carrier-undeliverable destination remains registrable, which is required to exercise the supplied unreachable fixture as a delivery outcome.
- The configured messaging service contains/permits the configured sending number. Scheduled creates carry both its SID and the configured `From` so reconciliation by that sender remains complete.
- The reconciliation API uses an inclusive application range `[from,to]`. Provider `DateSent>`/`DateSent<` bounds reduce the server result; exact inclusion is decided locally from provider timestamps after all pages are collected.
- Provider state is refreshed by SDK reads because there is no callback URL.
- Sending `string.Empty` to the update operation is the redaction value; successful disposal is defensively confirmed by a subsequent provider fetch. The generated operation and its Notes establish the redaction capability, while the returned live representation is `UNVERIFIED` until that fetch.

### Blockers

- None. The SDK map exposes lookup validation/canonicalization plus create, provider scheduling, fetch, update/cancel/redact, and server-filtered list operations required by the task.

## 5. REQUIRED READING

Load every item below **before implementation starts**. This contract sheet deliberately does not carry their contents.

| Skill | Step governed |
|---|---|
| `dotnet-client-initialization` | Client construction, DI, and `HttpClient` lifetime |
| `dotnet-authentication` | Basic credentials and secret-safe configuration |
| `dotnet-calling-endpoints` | Every SDK operation call and manual list paging |
| `dotnet-models` | Lookup/message response mapping and generated enums |
| `dotnet-error-handling` | All SDK/transport/JSON error boundaries |
| `dotnet-configuration-resilience` | Retry, timeout, server-node override, logging, and pagination |
| `dotnet-testing` | HTTP-seam tests and provider-integration test boundaries |
