# Twilio integration plan

## 1. Scope & sequence

| Step | Application work | Twilio operations |
| --- | --- | --- |
| 1 | Bind and validate `Twilio:` options; register one long-lived SDK client and provider adapter. | Client construction only. |
| 2 | Register shopper contact numbers after provider validation and canonicalization. | `LookupsV2PhoneNumber.FetchPhoneNumber3`. |
| 3 | Add order state, contact-number and notification persistence; place orders from existing catalog/order models and send placed SMS notifications after committing the order. | `Api20100401Message.CreateMessage`. |
| 4 | Dispatch orders, send the dispatch SMS, and ask Twilio to schedule the delivery follow-up for three days later. | `Api20100401Message.CreateMessage` (immediate and scheduled). |
| 5 | Cancel orders and cancel every still-pending provider-scheduled follow-up before recording its refreshed outcome; cancellation remains successful if notification work fails. | `Api20100401Message.UpdateMessage`, `FetchMessage`. |
| 6 | Return shopper-owned orders and notification histories, refreshing provider-owned delivery state when possible without failing the read. | `Api20100401Message.FetchMessage`. |
| 7 | Idempotently reserve and send operator resend attempts; a persisted `(source notification, caller key)` reservation is the application idempotency boundary. | `Api20100401Message.FetchMessage`, `CreateMessage`. |
| 8 | Dispose of provider and local message text while retaining metadata. | `Api20100401Message.UpdateMessage`, `FetchMessage`. |
| 9 | Reconcile the complete interval, requesting only this application's configured `From` number and walking every provider page. | `Api20100401Message.ListMessage`. |
| 10 | Add unit/integration coverage, build, then exercise the minimum live flow against only the two authorized destinations. | All operations above. |

## 2. CONTRACT SHEET

**Signatures below are generated code, verbatim; every parameter name is the literal C# identifier (the cancellation-token parameter really is named `ct`, so named arguments write `ct:`).**

**Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.**

| Controller property | Method signature | Request model / fields used | Response envelope / fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `LookupsV2PhoneNumber` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | No model. Send caller input as `phoneNumber`; all optional query fields are explicitly `null` because base validation/canonicalization is required and paid enrichment is not. | `TwilioSdk.Models.LookupResponse`: `PhoneNumber (phone_number): string?`; `Valid (valid): bool?`; `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`. | Case B `SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | None. | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs`; `Api/LookupsV2PhoneNumber.cs` |
| `Api20100401Message` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | No model. Required: `accountSid`, canonical `to`. Immediate sends use configured `from`, configured `messagingServiceSid`, and `body`; scheduled sends additionally use `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` and `sendAt`. Every other nullable argument is explicitly `null`. | `TwilioSdk.Models.ApiV2010AccountMessage`: `Sid (sid): string?`; `Status (status): MessageEnumStatus?`; `ErrorCode (error_code): int?`; `ErrorMessage (error_message): string?`; `DateCreated (date_created): string?`; `DateSent (date_sent): string?`; `From (from): string?`; `Body (body): string?`. | Case B `SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; raw accessors as above. | None. | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs`; `Api/Api20100401Message.cs` |
| `Api20100401Message` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | No model; account SID and persisted provider message SID. | `TwilioSdk.Models.ApiV2010AccountMessage`; read `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `DateCreated`, `DateSent`, `Body`. | Case B `SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; raw accessors as above. | None. | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| `Api20100401Message` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | No model. Cancellation sends `body: null`, `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled`. Redaction sends `body: ""`, `status: null`; exact redaction sentinel is confirmed by refetch during verification. | `TwilioSdk.Models.ApiV2010AccountMessage`; read `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `Body`. | Case B `SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; raw accessors as above. | None. | `map/operations/Api20100401Message.md`; `Models/Enums/MessageEnumUpdateStatus.cs`; `Models/ApiV2010AccountMessage.cs`; exact empty-body result `UNVERIFIED` with refetch verification |
| `Api20100401Message` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | No model. Always pass configured sending number as `from`, range lower bound as `dateSentQueryQuery` (`DateSent>`), upper bound as `dateSentQuery` (`DateSent<`), page size 1000, and advance `page` until exhausted. | `TwilioSdk.Models.ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `Page (page): int?`, `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`; each message reads `Sid`, `Status`, `ErrorCode`, `DateCreated`, `DateSent`, `From`. | Case B `SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; raw accessors as above. | Generated map labels no SDK `Pageable`; operation exposes explicit page/page-token inputs and response page metadata, so the application iterates all pages defensively. | `map/operations/Api20100401Message.md`; `Models/ListMessageResponse.cs`; `Models/ApiV2010AccountMessage.cs`; `Api/Api20100401Message.cs` |

Enums used:

| Fully-qualified type | Values used | Source |
| --- | --- | --- |
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `Fixed` = `fixed` | `Models/Enums/MessageEnumScheduleType.cs` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `Canceled` = `canceled` | `Models/Enums/MessageEnumUpdateStatus.cs` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` | Provider values include `accepted`, `scheduled`, `canceled`, `queued`, `sending`, `sent`, `failed`, `delivered`, `undelivered`, `receiving`, `received`, `read`, `partially_delivered`; comparisons use the SDK string value. | `Models/Enums/MessageEnumStatus.cs` |

Client/auth/server facts:

| Fact | Contract | Source |
| --- | --- | --- |
| Client | `TwilioSdk.TwilioSdkClient(HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)`; API groups are client properties. | `sdk-map.md`; `TwilioSdkClient.cs` |
| Auth | `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = AccountSid, Password = AuthToken }` assigned to `AccountSidAuthToken`. | `sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Servers | Production has 15 groups. This scope touches `Default` (`https://api.twilio.com`) for message operations and `Default4` (`https://lookups.twilio.com`) for number validation. `Twilio:BaseUrl`, when nonblank, replaces `options.Server.Default.Production.BaseUrl` only. | `sdk-map.md`; `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `Servers/Default4Options.cs` |

## 3. Trap notes

- Client lifetime and `HttpClient` ownership can cause socket exhaustion or stale handlers if scoped incorrectly — **MUST load twilio-platforms-team:dotnet-client-initialization**.
- Credential application and missing/blank configuration can otherwise become a first-call production failure — **MUST load twilio-platforms-team:dotnet-authentication**.
- The many nullable positional parameters and generator-injected changing header can mis-bind calls or masquerade as application idempotency — **MUST load twilio-platforms-team:dotnet-calling-endpoints**.
- String-enum comparison and nullable/unknown response members can be mishandled — **MUST load twilio-platforms-team:dotnet-models**.
- Typed-versus-raw exceptions and safe error-body handling determine which failures can be classified without exposing content — **MUST load twilio-platforms-team:dotnet-error-handling**.
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it escape — **MUST load twilio-platforms-team:dotnet-error-handling**.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it — **MUST load twilio-platforms-team:dotnet-error-handling**.
- Retry eligibility, total timeout, messaging-only base URL override, full pagination, and logging controls affect correctness and privacy — **MUST load twilio-platforms-team:dotnet-configuration-resilience**.
- The supported transport seam and assertions must keep tests independent of generated internals — **MUST load twilio-platforms-team:dotnet-testing**.

## 4. REQUIRED READING

Load all of these **before implementation starts**; this sheet deliberately does not carry their contents.

| Skill | Governs |
| --- | --- |
| `twilio-platforms-team:dotnet-client-initialization` | SDK client and DI lifetime. |
| `twilio-platforms-team:dotnet-authentication` | Basic credentials and secret binding. |
| `twilio-platforms-team:dotnet-calling-endpoints` | Operation calls, named arguments and cancellation. |
| `twilio-platforms-team:dotnet-models` | Generated records and string enums. |
| `twilio-platforms-team:dotnet-error-handling` | Provider error boundary and JSON failures. |
| `twilio-platforms-team:dotnet-configuration-resilience` | Retries, deadlines, server override, pagination and logging. |
| `twilio-platforms-team:dotnet-testing` | Provider-adapter tests. |

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `Twilio:` in PublicApi startup and validate every required nonblank value (`AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`) before the host starts; `BaseUrl` is optional but must be absolute when supplied. |
| 2 | Secret sourcing & rotation | Local live credentials are copied from environment variables into PublicApi .NET user-secrets; deployments provide the same configuration keys through their secret store. Options and credentials are captured once by the singleton client, so rotation takes effect on process restart. |
| 3 | Total timeout budget | SDK per-attempt timeout is 10 seconds. Each provider adapter call creates a linked 30-second total deadline; the request cancellation token may shorten it. |
| 4 | Write-retry ownership | SDK retries remain disabled for all calls in this integration. No provider POST/update is replayed automatically; safe GET retry policy can be added later only behind the same total deadline. |
| 5 | Idempotency & ambiguous writes | Twilio create/update signatures have no real caller key; the generated fresh GUID header is not treated as one. Order state commits before best-effort messaging. Resend first persists a unique `(source notification id, caller-supplied key)` reservation and returns that same notification on repeats; provider SIDs and reconciliation expose ambiguous provider writes. |
| 6 | Observability | Log application correlation/order/notification identifiers, provider SID, outcome and HTTP status at structured levels. Never log auth, destination, or body. Raw errors expose no documented provider correlation-id field, so no provider correlation id is claimed; application correlation and provider SID are retained. |
| 7 | Sensitive data | Destination and message text are form fields. SDK request/response bodies and headers remain off, an explicit application `ILoggerFactory` is assigned so `TWILIOCLIENT_LOG` cannot enable them externally, and application logs never interpolate number/body/token. |
| 8 | Environment selection | Messaging uses `Default` production unless `Twilio:BaseUrl` verbatim overrides that group; Lookup remains `Default4` production and is deliberately not affected by the messaging override. The SDK declares no sandbox. Live verification is operationally restricted to `TWILIO_TEST_TO_NUMBER` and `TWILIO_UNREACHABLE_TO_NUMBER`. |

## 6. Assumptions & Blockers

- “A few days” is three days.
- Order events notify every currently registered contact number. A deleted number is physically removed, is never copied onto a notification row, and makes any historical notification tied to it ineligible for resend.
- Order placement requires a shipping address because the existing `Order` aggregate requires it; catalog item identifiers and positive quantities remain the requested line-item source.
- `GET /api/orders/{orderId}/notifications` is shopper-scoped and requires ownership; content disposal is administrator-only as mandated by the operator-action list.
- Provider failures are captured as notification outcomes and never roll back place/dispatch/cancel. Contact registration is the exception: provider validation must succeed before persistence.
- Reconciliation exposes metadata, not destination or message body.
- No blockers.
