# Twilio order-notification integration plan

## 1. Scope & sequence

| Step | Provider work | Application consequence |
|---|---|---|
| 1. Client registration | Construct `TwilioSdk.TwilioSdkClient`; configure basic auth; override only messaging server `Default` when `Twilio:BaseUrl` is present; leave Lookup server `Default4` on its provider default. | Bind `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optional `Twilio:BaseUrl`; never log credentials, destination numbers, or message bodies. |
| 2. Register a destination | `LookupsV2PhoneNumber.FetchPhoneNumber3`. | Accept only `Valid == true` with a non-empty returned `PhoneNumber`; store that returned provider-canonical number, never caller input. A Lookup failure rejects registration because validation was not established. |
| 3. Send an immediate notification | `Api20100401Message.CreateMessage`. | For placed, dispatched, cancelled, and resend messages pass `from: Twilio:FromNumber`, `messagingServiceSid: null`, `body`, and the active canonical `to`. Persist returned `Sid`, `Status`, error fields, timestamps, destination ownership/contact id, and notification kind. Provider failure is captured on the notification and never rolls back the order transition. No active number means no call. |
| 4. Schedule the delivery follow-up | `Api20100401Message.CreateMessage` with `MessageEnumScheduleType.Fixed`, a future `sendAt`, and `Twilio:MessagingServiceSid`. | On dispatch, after the immediate dispatch notification, ask Twilio to schedule the follow-up three days later. Persist its returned `Sid` and state immediately. Do not add an application timer/worker. Failure to schedule is recorded and does not fail dispatch. |
| 5. Cancel an unsent follow-up | `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`. | On order cancellation, first persist the cancelled order transition, then best-effort cancel every provider-scheduled follow-up whose provider id is known and whose locally known state is not terminal. Persist the returned provider state. The cancellation notification itself is a separate immediate send. |
| 6. Refresh provider-owned state | `Api20100401Message.FetchMessage`. | Before returning notification/order progress, best-effort poll each known provider `Sid`; update status, error code/message, dates, and provider metadata. A polling failure leaves the last known persisted state and is reported as stale, not as failure of the underlying order action. There are no callbacks in this deployment. |
| 7. Resend | `Api20100401Message.CreateMessage`. | Claim the caller idempotency key transactionally before the provider call. Repeated requests return the already-created resend notification; a fresh key creates a child notification linked to the original. Re-check active contact ownership and retained content before calling Twilio. Persist the returned provider id/state. |
| 8. Dispose provider content | `Api20100401Message.UpdateMessage` with `body: ""`, `status: null`; then `FetchMessage` to verify the body is absent/empty. | Clear the local body in the same workflow while retaining immutable audit metadata and last-known outcome. Treat a successful update whose subsequent fetch still contains text as an incomplete disposal, not success. Preserve metadata even if later fetch is unavailable. |
| 9. Reconcile a complete range | Repeated `Api20100401Message.ListMessage` calls. | Send `from: Twilio:FromNumber` in every provider query, plus provider-side lower/upper date filters. Drive `page` until `NextPageUri` is null (with a high defensive cap that reports truncation rather than silently returning a partial report), collect every page, and full-outer-join provider and local records by provider `Sid`. Never obtain wider account traffic and filter locally. |
| 10. Verify | Exercise Lookup, immediate create/fetch, scheduled create/cancel/fetch, resend, redaction/fetch, and multi-page reconciliation against the permitted fixtures only. | Tests use an `HttpClient` handler seam; the one live verification run sends only the configured permitted Canadian and reserved US destinations and minimizes billable traffic. |

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

All seven operations below are throw-only; this SDK has no `…Result` variant. All use HTTP basic auth through `TwilioSdk.TwilioSdkClientOptions.AccountSidAuthToken`.

| Controller property · operation | Exact generated signature; required nullable arguments must still be passed | Request/form fields used | Return/envelope and fields read | Error · pagination | Source |
|---|---|---|---|---|---|
| `TwilioSdk.TwilioSdkClient.LookupsV2PhoneNumber` · `FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Path `phoneNumber`; pass `null` for all 15 optional query arguments (`fields` through `partnerSubId`) for basic validation. | `TwilioSdk.Models.LookupResponse`; read `PhoneNumber (phone_number): string?`, `Valid (valid): bool?`, `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.ValidationError>?`, `CountryCode (country_code): string?`, `NationalFormat (national_format): string?`. No envelope. | Case B: `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; inspect `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or bytes. No pagination. | `operations/LookupsV2PhoneNumber.md`; `records-4-Li-Me.md` |
| `TwilioSdk.TwilioSdkClient.Api20100401Message` · `CreateMessage` (immediate) | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Required path `accountSid`; required form `To (to): string`; explicitly pass null for every unused nullable argument. Immediate values: `from = Twilio:FromNumber`, `body = retained notification text`, `messagingServiceSid = null`, `scheduleType = null`, `sendAt = null`. Fields intentionally omitted: callback, application, price/feedback/attempt/validity/force-delivery, retention, encoding/actions/traffic/shortening, MMS/content/template/media options. | `TwilioSdk.Models.ApiV2010AccountMessage`; read `Sid (sid): string?`, `Status (status): TwilioSdk.Models.Enums.MessageEnumStatus?`, `From (from): string?`, `To (to): string?`, `Body (body): string?`, `MessagingServiceSid (messaging_service_sid): string?`, `DateCreated/DateUpdated/DateSent: string?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`. No envelope. | Case B raw error as above. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| `TwilioSdk.TwilioSdkClient.Api20100401Message` · `CreateMessage` (scheduled) | Same exact `CreateMessage` signature as the preceding row. | Required `accountSid` and `to`; set `scheduleType = TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed`, `sendAt = future DateTimeOffset`, `messagingServiceSid = Twilio:MessagingServiceSid`, `body = follow-up text`, and `from = null`; all other nullable fields null. The enum contract says scheduling is for Messaging Services and requires `fixed` with the send-time parameter. | Same `TwilioSdk.Models.ApiV2010AccountMessage`; require a non-empty `Sid`; persist expected `Status = Scheduled` when returned plus all fields listed above. | Case B raw error. No pagination. | `operations/Api20100401Message.md`; `enums.md`; `records-1-Ac-Ca.md` |
| `TwilioSdk.TwilioSdkClient.Api20100401Message` · `UpdateMessage` (cancel) | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `body = null`; `status = TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `canceled`). | `TwilioSdk.Models.ApiV2010AccountMessage`; read/persist `Sid`, `Status`, timestamps, and error fields. | Case B raw error. No pagination. | `operations/Api20100401Message.md`; `enums.md`; `records-1-Ac-Ca.md` |
| `TwilioSdk.TwilioSdkClient.Api20100401Message` · `UpdateMessage` (redact) | Same exact `UpdateMessage` signature as preceding row. | `body = ""`; `status = null`. The operation's provider note explicitly identifies this operation as the body-redaction operation. | `TwilioSdk.Models.ApiV2010AccountMessage`; preserve metadata and require returned/fetched `Body` to contain no retained text before declaring disposal complete. | Case B raw error. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| `TwilioSdk.TwilioSdkClient.Api20100401Message` · `FetchMessage` | `FetchMessage(string accountSid, string sid, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | Path `accountSid`, provider `sid`. | `TwilioSdk.Models.ApiV2010AccountMessage`; read all persisted fields listed in Create. For a disposed notification, never rehydrate local text from provider. | Case B raw error. No pagination. | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| `TwilioSdk.TwilioSdkClient.Api20100401Message` · `ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `from = Twilio:FromNumber` **on every request**; `dateSentQueryQuery = requested from` maps to wire `DateSent>`; `dateSentQuery = requested to` maps to wire `DateSent<`; `dateSent = null`, `to = null`; set `pageSize` and increment `page`, with `pageToken = null`, until response says no next page. | `TwilioSdk.Models.ListMessageResponse` wrapper: `Messages (messages): IReadOnlyList<TwilioSdk.Models.ApiV2010AccountMessage>?`, `NextPageUri (next_page_uri): string?`, `Page`, `PageSize`, `Start`, `End`. Read each message `Sid`, `From`, `To`, `Status`, dates, and errors; local join key is `Sid`. | Case B raw error. SDK auto-pagination: none. This is a manual page loop; a hit page cap must make report explicitly incomplete/error, never silently partial. | `operations/Api20100401Message.md`; `records-4-Li-Me.md`; `records-1-Ac-Ca.md` |

`DeleteMessage(string accountSid, string sid, TwilioSdk.Core.Models.RequestOptions? requestOptions = null, CancellationToken ct = default)` exists and deletes the entire provider Message resource, but is intentionally not the content-disposal path because `UpdateMessage` is the map-documented body-redaction operation. Source: `operations/Api20100401Message.md`.

### Enum values used and reported

| Fully-qualified type | Generated member → wire value | Use | Source |
|---|---|---|---|
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `Fixed` → `fixed` | Schedule follow-up. | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `Canceled` → `canceled` | Cancel not-yet-sent follow-up. | `enums.md` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` | `Queued` → `queued`; `Sending` → `sending`; `Sent` → `sent`; `Failed` → `failed`; `Delivered` → `delivered`; `Undelivered` → `undelivered`; `Receiving` → `receiving`; `Received` → `received`; `Accepted` → `accepted`; `Scheduled` → `scheduled`; `Read` → `read`; `PartiallyDelivered` → `partially_delivered`; `Canceled` → `canceled` | Persist provider outcome verbatim; do not collapse failure/cancellation/delivery states into one boolean. | `enums.md` |

### Client, authentication, and server nodes

| Fact | Exact SDK contract | Source |
|---|---|---|
| Package | Install `AsadAli.TwilioSdk` version-less into the calling project. | `sdk-map.md` |
| Constructor | `new TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)`; `HttpClient` is the externally supplied transport seam. | `sdk-map.md`; `TwilioSdkClient.cs` |
| Environment | `options.Environment = TwilioSdk.Servers.ServerEnvironment.Production`. It is the sole generated environment member. | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Auth | `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = Twilio:AccountSid, Password = Twilio:AuthToken }`; both members are required strings. Never log/return/serialize `Password`. | `sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Server options type | `TwilioSdk.TwilioSdkClientOptions.Server` is `TwilioSdk.ServerOptions`; the messaging node is `Default`, Lookup v2 node is `Default4`. | `sdk-map.md`; `Server.cs`; `ServerOptions.cs` |
| Messaging default and override | All five `Api20100401Message` operations resolve through `options.Server.Default.Production.BaseUrl`; default is `https://api.twilio.com`. If and only if `Twilio:BaseUrl` is non-empty, assign that string verbatim to this property before client construction. This changes Create/Fetch/List/Update/Delete messaging calls together. | `operations/Api20100401Message.md`; `Servers/DefaultOptions.cs` |
| Lookup host isolation | `LookupsV2PhoneNumber.FetchPhoneNumber3` resolves through `options.Server.Default4.Production.BaseUrl`; default is `https://lookups.twilio.com`. Never apply `Twilio:BaseUrl` here. | `operations/LookupsV2PhoneNumber.md`; `Servers/Default4Options.cs` |
| Request models | These operations use path/query/form parameters directly; there is no generated request-body record. Every nullable no-default parameter in the signatures still must be passed, normally with named arguments. | operation pages above |
| Caller identity, ownership, persistence, idempotency claims, transactions, authorization, message wording | Application concerns; the SDK contract neither implements nor chooses them. | YOUR CALL — not in the map |

## 3. Trap notes

- ⚠ Step 1 (client registration) — `HttpClient` ownership, DI registration, and the SDK wrapper lifetime can create socket churn or cross-client configuration bleed if wired at the wrong seam. **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ Step 1 (authentication) — credential timing, configuration binding, and rotation can leave a constructed client using missing or stale secrets. **MUST load `dotnet-authentication`** before setting auth.
- ⚠ Step 1 (server selection/resilience) — the per-server override, environment selection, request deadline, retry triggers, and non-idempotent POST transport behavior can send a call to the wrong host, exceed an HTTP request budget, or duplicate a billable write. **MUST load `dotnet-configuration-resilience`** before configuring the client.
- ⚠ Steps 2–9 (calling operations) — required-but-nullable positional parameters, named `ct`, throw-only operations, and response-envelope depth can mis-bind arguments or hide the payload. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 3–8 (models/enums) — generated string-enum construction and nullable response members can turn scheduling/cancellation or state persistence into invalid wire values or null dereferences. **MUST load `dotnet-models`** before using those types.
- ⚠ Steps 2–9 (provider boundary) — Case-B raw errors, unreadable bodies, cancellation, and transport ambiguity can leak exceptions, disclose provider bodies, or misclassify an unknown send outcome. **MUST load `dotnet-error-handling`** before writing the boundary.
- ⚠ Steps 3 and 7 (writes/idempotency) — a transport failure can leave whether the provider accepted a write unknown and can defeat an otherwise-correct application idempotency claim. **MUST load `dotnet-configuration-resilience`** before implementing send/resend.
- ⚠ Step 9 (manual pagination) — relying only on provider continuation can hang, while an unreported client cap can produce a reconciliation report falsely presented as complete. **MUST load `dotnet-configuration-resilience`** before implementing reconciliation.
- ⚠ Step 10 (tests) — mocking generated controllers rather than the HTTP transport seam makes tests brittle and can miss actual verb/path/form behavior. **MUST load `dotnet-testing`** before writing tests.
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` · client/DI/`HttpClient` construction in step 1.
- `dotnet-authentication` · basic-auth configuration and secret lifetime in step 1.
- `dotnet-configuration-resilience` · server-node override, retry/timeout/write ambiguity, and pagination in steps 1, 3, 7, and 9.
- `dotnet-calling-endpoints` · exact invocation, named arguments, cancellation, and response shapes in steps 2–9.
- `dotnet-models` · generated string enums and nullable records in steps 2–9.
- `dotnet-error-handling` · the complete provider exception boundary, including both `JsonException` directions, in steps 2–9.
- `dotnet-testing` · HTTP seam and behavioral tests in step 10.

## 5. Assumptions & Blockers

### Assumptions

- `Twilio:MessagingServiceSid` identifies a Messaging Service whose sender pool contains this application's `Twilio:FromNumber`; Twilio scheduling uses that service, while immediate messages explicitly use `Twilio:FromNumber`.
- “A few days later” is implemented as three days after dispatch, with an absolute `DateTimeOffset` passed to Twilio.
- The application owns transactional uniqueness and recovery for resend idempotency; `CreateMessage` exposes no caller idempotency-key parameter.
- Provider delivery state is eventually refreshed by API reads because no callback URL exists.
- The reconciliation interval uses provider `DateSent>` for the lower boundary and `DateSent<` for the upper boundary, and “covers the whole range” means every provider page inside those bounds is consumed or the report explicitly fails as incomplete.
- Basic Lookup (`fields: null`) is the provider validation/canonicalization gate: `Valid == true` plus returned `PhoneNumber` is usable for registration. It does not promise carrier delivery; later `undelivered`/`failed` is a legitimate message outcome.

### Blockers

- None.
