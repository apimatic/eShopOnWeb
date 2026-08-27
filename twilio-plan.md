# Twilio order-notification integration plan

## 1. Scope & sequence

| Step | Provider work | Application consequence | Source |
|---|---|---|---|
| 1. Package/client | Add the version-less `AsadAli.TwilioSdk` package; create one `TwilioSdk.TwilioSdkClient` over an `IHttpClientFactory`-owned `HttpClient`; bind `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optional `Twilio:BaseUrl`. | Validate required settings at startup; never log the auth token, destination, request body, or SDK form payload. | `sdk-map.md`; `TwilioSdkClientOptions.cs`; `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `Servers/Default4Options.cs` |
| 2. Register number | Call `client.LookupsV2PhoneNumber.FetchPhoneNumber3` with `fields: "validation"`; accept only `Valid == true` and a nonblank `PhoneNumber`; persist that returned `PhoneNumber` as the canonical number. | A provider rejection/`Valid != true` is a client validation failure. A provider outage is not proof the number is invalid. Never log the submitted or canonical number. | `map/operations/LookupsV2PhoneNumber.md`; `map/models/records-4-Li-Me.md`; `Api/LookupsV2PhoneNumber.cs` |
| 3. Immediate notifications | For placed, dispatched, cancelled, and resend messages call `client.Api20100401Message.CreateMessage` with `to`, `from: Twilio:FromNumber`, `body`, and all other optional form fields explicitly `null`. | Persist a local notification before the call, then persist returned `Sid`, `Status`, timestamps/error data. Catch every provider/transport/deserialization failure at the notification boundary so the order transition still commits; a shopper without an active number creates no send. | `map/operations/Api20100401Message.md`; `map/models/records-1-Ac-Ca.md` |
| 4. Provider-scheduled follow-up | On dispatch, call `CreateMessage` with `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt` a few days in the future, `messagingServiceSid: Twilio:MessagingServiceSid`, `from: Twilio:FromNumber`, `to`, and `body`. The provider, not an application timer, owns delivery. | Persist the follow-up notification and provider SID/status. With no public callback URL, do not pass `statusCallback`; refresh by polling. The exact delay (recommended: three days) is application policy. | `map/operations/Api20100401Message.md`; `map/models/enums.md` |
| 5. Cancel follow-up | On order cancellation, fetch the persisted scheduled provider message if useful, then call `UpdateMessage(body: null, status: MessageEnumUpdateStatus.Canceled)` before its send time. | Persist the provider response. Provider failure never rolls back the order cancellation; retain a visible cancellation-failed outcome for operator action/reconciliation. Never use `DeleteMessage`, because deletion would destroy the provider record that reporting must retain. | `map/operations/Api20100401Message.md`; `map/models/enums.md` |
| 6. Poll status | For each notification carrying a provider SID, call `FetchMessage`; update local provider status/error code/error message/date fields without restoring locally disposed content. | Reads of order notifications and my-orders can best-effort refresh provider state; stale/provider-unavailable state remains reportable rather than failing the order read. | `map/operations/Api20100401Message.md`; `map/models/records-1-Ac-Ca.md` |
| 7. Resend | Enforce caller idempotency in the application datastore before making a fresh `CreateMessage` call. Only a not-reached message is eligible, and no send may target a deleted number. | A unique application key over the resend request is the concurrency boundary. The SDK does not accept the caller key; it generates its own fresh `Idempotency-Key` for each `CreateMessage` invocation, so do not rely on the SDK header for endpoint idempotency. Return the new local notification identifier. | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs` |
| 8. Dispose content | Call `UpdateMessage(providerSid, body: string.Empty, status: null)`, then `FetchMessage` and verify the provider no longer returns nonempty `Body`; only then erase local body and record disposal while retaining SID/status/timestamps/error metadata. | The map confirms `UpdateMessage` is the redaction operation but does not state the exact successful response representation. Treat a remaining nonempty body as provider-disposal failure, not success. | `map/operations/Api20100401Message.md`; `map/models/records-1-Ac-Ca.md`; **UNVERIFIED response representation** |
| 9. Reconcile complete range | Repeatedly call `ListMessage` with `from: Twilio:FromNumber`, lower/upper `DateSent` bounds, `pageSize: 1000`, and the next `PageToken` parsed from `NextPageUri`, until `NextPageUri` is blank. Match provider `Sid` to local provider SID and report provider-only, local-only, and matched rows. | The `From` filter is sent to the provider on every page. Parse provider date strings defensively and apply the exact requested ISO-8601 instants after retrieving the server-bounded pages. Never replace the provider-side `From` predicate with a wider query plus local filtering. | `map/operations/Api20100401Message.md`; `map/models/records-1-Ac-Ca.md`; `map/models/records-3-Fl-Li.md` |

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

### Package, construction, authentication, and servers

| Contract | Exact SDK surface | Source |
|---|---|---|
| Package | `dotnet add package AsadAli.TwilioSdk` with no version pin. Root namespace `TwilioSdk`. | `sdk-map.md` |
| Client constructor | `new TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md`; `TwilioSdkClient.cs` |
| Options/auth | `TwilioSdk.TwilioSdkClientOptions.AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`; construct credentials with required init properties `Username: string` = `Twilio:AccountSid`, `Password: string` = `Twilio:AuthToken`. `Environment: TwilioSdk.Servers.ServerEnvironment = TwilioSdk.Servers.ServerEnvironment.Production`. | `sdk-map.md`; `TwilioSdkClientOptions.cs`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Messaging base URL | Messaging operations below are server node `Default (api)`. Set only `TwilioSdk.TwilioSdkClientOptions.Server.Default.Production.BaseUrl` to the configured `Twilio:BaseUrl` **verbatim when nonblank**. Default is `https://api.twilio.com`. Exact involved types: `TwilioSdk.ServerOptions`, `TwilioSdk.Servers.DefaultOptions`, nested `TwilioSdk.Servers.DefaultOptions.ProductionOptions`. | `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `map/operations/Api20100401Message.md` |
| Lookup base URL isolation | Lookup V2 is server node `Default4 (lookups)` and resolves through `options.Server.Default4.Production.BaseUrl`; leave it at SDK default `https://lookups.twilio.com`. `Twilio:BaseUrl` must not alter this node. Exact types: `TwilioSdk.Servers.Default4Options`, nested `TwilioSdk.Servers.Default4Options.ProductionOptions`. | `Servers/Default4Options.cs`; `map/operations/LookupsV2PhoneNumber.md` |
| Controller properties | `client.LookupsV2PhoneNumber: TwilioSdk.Api.LookupsV2PhoneNumber`; `client.Api20100401Message: TwilioSdk.Api.Api20100401Message`. | `sdk-map.md`; operation pages below |
| Error core | Case B operations throw `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. `RawError` members: `StatusCode: System.Net.HttpStatusCode`, `ReadAsBytes(): System.ReadOnlyMemory<byte>`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`. No operation has a no-throw `…Result` variant. | `sdk-map.md`; operation pages below |

### Operations

| Controller · operation | Exact async signature | Request/form/query contract actually used | Response fields read | Error · pagination | Source |
|---|---|---|---|---|---|
| `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `System.Threading.Tasks.Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Path `phoneNumber` required. Query `Fields` ← `fields` = `"validation"`; all remaining nullable parameters explicitly `null`. | `TwilioSdk.Models.LookupResponse.PhoneNumber (phone_number): string?`; `Valid (valid): bool?`; `ValidationErrors (validation_errors): System.Collections.Generic.IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`. | Case B `RawError`; no pagination. | `map/operations/LookupsV2PhoneNumber.md`; `map/models/records-4-Li-Me.md`; `map/models/enums.md` |
| `client.Api20100401Message.CreateMessage` | `System.Threading.Tasks.Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, System.Collections.Generic.IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, System.DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, System.Collections.Generic.IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Form wire names in parameter order: `To`, `StatusCallback`, `ApplicationSid`, `MaxPrice`, `ProvideFeedback`, `Attempt`, `ValidityPeriod`, `ForceDelivery`, `ContentRetention`, `AddressRetention`, `SmartEncoded`, `PersistentAction`, `TrafficType`, `ShortenUrls`, `ScheduleType`, `SendAt`, `SendAsMms`, `ContentVariables`, `RiskCheck`, `From`, `FallbackFrom`, `MessagingServiceSid`, `Body`, `MediaUrl`, `ContentSid`. Only `accountSid` and `to` are nonnullable in C#. Immediate profile uses `from` and `body`; scheduled profile additionally uses `scheduleType`, `sendAt`, `messagingServiceSid`. Every unused nullable parameter must be explicitly passed as `null`. | `Body (body): string?`; `From (from): string?`; `To (to): string?`; `Status (status): MessageEnumStatus?`; `MessagingServiceSid (messaging_service_sid): string?`; `Sid (sid): string?`; `DateSent (date_sent): string?`; `DateCreated (date_created): string?`; `DateUpdated (date_updated): string?`; `ErrorCode (error_code): int?`; `ErrorMessage (error_message): string?`. | Case B `RawError`; no pagination. The generated method itself adds `Idempotency-Key: Guid.NewGuid()` and exposes no caller-header parameter. | `map/operations/Api20100401Message.md`; `map/models/records-1-Ac-Ca.md`; `Api/Api20100401Message.cs` |
| `client.Api20100401Message.FetchMessage` | `System.Threading.Tasks.Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Path `AccountSid`, `Sid`; no form/query fields. | Same message fields listed for `CreateMessage`; content-disposal verification specifically reads `Body`. | Case B `RawError`; no pagination. | `map/operations/Api20100401Message.md`; `map/models/records-1-Ac-Ca.md` |
| `client.Api20100401Message.UpdateMessage` | `System.Threading.Tasks.Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Form `Body` ← `body`; `Status` ← `status`. Cancel profile: `body: null`, `status: MessageEnumUpdateStatus.Canceled`. Redact profile: `body: string.Empty`, `status: null`, followed by fetch verification. | Same message fields listed for `CreateMessage`. | Case B `RawError`; no pagination. Notes explicitly say this operation redacts body text and cancels not-yet-sent messages. | `map/operations/Api20100401Message.md`; `map/models/records-1-Ac-Ca.md`; `map/models/enums.md` |
| `client.Api20100401Message.ListMessage` | `System.Threading.Tasks.Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, System.DateTimeOffset? dateSent, System.DateTimeOffset? dateSentQuery, System.DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken ct = default)` | Query wire mapping: `To` ← `to`; `From` ← `from` (**always `Twilio:FromNumber`**); `DateSent` ← `dateSent`; `DateSent<` ← `dateSentQuery` (upper bound); `DateSent>` ← `dateSentQueryQuery` (lower bound); `PageSize` ← `pageSize` (default 50, maximum 1000); `Page` ← `page` (provider docs call it client state); `PageToken` ← `pageToken`. For a range use `dateSent: null`, upper/lower bounds, `pageSize: 1000`, `page: null`, then the token for each next page. | Envelope is the response itself: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`; `NextPageUri (next_page_uri): string?`; `Page (page): int?`; `PageSize (page_size): int?`; plus `End`, `FirstPageUri`, `PreviousPageUri`, `Start`, `Uri`. Read message `Sid`, `From`, `To`, `Body`, `Status`, dates, error code/message. | Case B `RawError`. Map labels generated pagination as none, but the operation accepts `pageToken` and the response exposes `NextPageUri`; continue until blank, parsing `PageToken` from that URI. | `map/operations/Api20100401Message.md`; `map/models/records-3-Fl-Li.md`; `map/models/records-1-Ac-Ca.md` |

### Enum values used or persisted

All are `TwilioSdk.Models.Enums` string-enum records (not C# enums).

| Type | Exact static members and wire values needed by this integration | Source |
|---|---|---|
| `MessageEnumScheduleType` | `Fixed` (`fixed`) | `map/models/enums.md` |
| `MessageEnumUpdateStatus` | `Canceled` (`canceled`) | `map/models/enums.md` |
| `MessageEnumStatus` | `Queued` (`queued`), `Sending` (`sending`), `Sent` (`sent`), `Failed` (`failed`), `Delivered` (`delivered`), `Undelivered` (`undelivered`), `Receiving` (`receiving`), `Received` (`received`), `Accepted` (`accepted`), `Scheduled` (`scheduled`), `Read` (`read`), `PartiallyDelivered` (`partially_delivered`), `Canceled` (`canceled`) | `map/models/enums.md` |
| `ValidationError` | `TooShort` (`TOO_SHORT`), `TooLong` (`TOO_LONG`), `InvalidButPossible` (`INVALID_BUT_POSSIBLE`), `InvalidCountryCode` (`INVALID_COUNTRY_CODE`), `InvalidLength` (`INVALID_LENGTH`), `NotANumber` (`NOT_A_NUMBER`) | `map/models/enums.md` |

### Provider/application boundary directives

| Concern | Directive | Source |
|---|---|---|
| Accepted is not delivered | Persist the provider status returned by create/fetch. Treat `Failed` and `Undelivered` as not reached; `Delivered` as reached; keep intermediate and other terminal statuses verbatim for later polling/reporting. | `map/models/enums.md` |
| No callbacks | Pass `statusCallback: null`; status refresh is `FetchMessage` polling, because the application has no reachable callback URL. | User mandate; `map/operations/Api20100401Message.md` |
| Scheduled acceptance | Scheduling requires the Messaging Service profile: `MessageEnumScheduleType.Fixed` together with `sendAt` and `messagingServiceSid`. This plan also supplies configured `from` to keep the application sender explicit. Persist the returned SID/status and immediately expose scheduling failure without rolling back dispatch. | `map/models/enums.md`; `map/operations/Api20100401Message.md` |
| Redaction confirmation | `UpdateMessage` is the mapped provider redaction operation. Exact post-redaction body representation can only be confirmed live; fetch best-effort and accept only null/empty provider body before claiming disposal. | `map/operations/Api20100401Message.md`; **UNVERIFIED** |
| Provider/list timestamp shape | Message dates are nullable `string`, not `DateTimeOffset`; parse best-effort. A missing/unparseable provider timestamp stays visible with its generic provider record rather than disappearing from reconciliation. | `map/models/records-1-Ac-Ca.md`; **UNVERIFIED live formatting** |
| Local identity/idempotency/concurrency | Ownership checks, role authorization, order state transitions, local unique keys, and persistence transaction boundaries are application design, not SDK contracts. | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership, SDK-wrapper lifetime, and DI callback construction determine whether sockets and configuration are reused correctly. **MUST load `twilio-sdk:dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — credential placement/rotation and the secret boundary can turn a correct call into 401/403 or leak a token. **MUST load `twilio-sdk:dotnet-authentication`** before supplying credentials.

⚠ Steps 2–9 (endpoint calls) — nullable parameters without C# defaults, named-argument binding, cancellation, and response-envelope depth can silently alter requests or reads. **MUST load `twilio-sdk:dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–9 (models/enums) — generated string enums, nullable response members, and JSON wire names do not behave like ordinary C# enums or guaranteed fields. **MUST load `twilio-sdk:dotnet-models`** before mapping provider data.

⚠ Steps 2–9 (error boundary) — Case-B raw errors, transport exceptions, and deserialization failures cross different exception paths; an incomplete boundary either breaks order operations or misclassifies provider rejection. **MUST load `twilio-sdk:dotnet-error-handling`** before writing catches.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `twilio-sdk:dotnet-error-handling`** before writing that boundary.

⚠ Steps 1, 3–9 (resilience/base URL/pagination/logging) — retries on non-idempotent writes, timeout scope, per-server base-address overrides, pagination termination, and request logging can cause duplicate paid sends, incomplete reports, wrong-host calls, or disclosure of numbers/content. **MUST load `twilio-sdk:dotnet-configuration-resilience`** before configuration and calls.

⚠ Verification (tests) — the correct HTTP seam and assertions must cover transport/errors/status drift without spending live-message volume. **MUST load `twilio-sdk:dotnet-testing`** before testing the integration layer.

## 4. REQUIRED READING

Load all of these **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `twilio-sdk:dotnet-client-initialization` | Client construction, `HttpClient` ownership, DI lifetime |
| `twilio-sdk:dotnet-authentication` | Basic credentials and secret/configuration wiring |
| `twilio-sdk:dotnet-calling-endpoints` | Exact invocation style, optional arguments, envelopes, cancellation |
| `twilio-sdk:dotnet-models` | String enums, nullable response records, wire/C# mappings |
| `twilio-sdk:dotnet-error-handling` | Case-B SDK errors, transport and both `JsonException` directions |
| `twilio-sdk:dotnet-configuration-resilience` | Retry/timeout/base URL/logging/pagination hazards |
| `twilio-sdk:dotnet-testing` | HTTP test seam and integration-layer verification |

## 5. Assumptions & Blockers

### Assumptions

- Registration interprets Twilio Lookup V2 `Valid == true` as the provider's usability gate and stores its returned `PhoneNumber`. Eventual carrier delivery is a later message outcome; the task explicitly requires the reserved US destination to register and later become undeliverable.
- “A few days later” is application policy; three days is the recommended default and lies outside the SDK contract.
- A provider reconciliation range is defined by the Message resource's `DateSent`; server bounds are followed by exact instant filtering because the generated response exposes provider dates as strings.
- The configured Messaging Service contains/permits the configured sending number. Supplying both values on the scheduled create keeps the requested sender explicit; live verification must confirm provider acceptance and resulting `From`.
- An empty `NextPageUri` terminates reconciliation; when present, its `PageToken` query value drives the next request.

### Blockers

- None. The SDK map exposes provider validation/canonicalization, create/schedule, fetch/status, update/cancel/redact, and From-filtered list operations needed by the brief. The two live-only response/acceptance checks above have concrete defensive verification directives and do not block implementation.
