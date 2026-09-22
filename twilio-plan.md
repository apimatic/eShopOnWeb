# twilio-plan.md — Order SMS notifications (eShopOnWeb + Twilio)

## 1. Scope & sequence

Additive SMS notifications for eShopOnWeb, exposed as `/api/` endpoints on `src/PublicApi`
(JWT). All Twilio traffic goes through the vendored APIMatic **Twilio SDK** (`TwilioSdk`).

| # | Step | Twilio operations |
| --- | --- | --- |
| A | Vendor SDK into `src/TwilioSdk`; ProjectReference from Infrastructure | — |
| B | Domain: `ContactNumber`, `OrderNotification` entities; `Order` lifecycle (`OrderStatus`); EF configs + DbSets | — |
| C | `TwilioMessagingGateway` (Infrastructure) wrapping the SDK; client DI + fail-fast options | `FetchPhoneNumber3`, `CreateMessage`, `FetchMessage`, `ListMessage`, `UpdateMessage` |
| D | App services: contact-number, order-placement (reuse `Order`/`OrderItem`), order-notification | (via gateway) |
| E | PublicApi endpoints for the three flows | (via services) |
| F | DI wiring + user-secrets + test placeholder config | — |
| G | Build/test + live self-verify | all |

Flow → operation mapping:
- **Register number** (`POST /api/contact-numbers`) → `FetchPhoneNumber3` (validate + canonicalize; store `LookupResponse.PhoneNumber` only if `Valid == true`).
- **Place order** (`POST /api/orders`) → build `Order` from catalog ids+quantities → `CreateMessage` (immediate, `from`=FromNumber).
- **Dispatch** (`POST /api/orders/{id}/dispatch`) → `CreateMessage` (immediate "on its way") + `CreateMessage` (scheduled follow-up: `messagingServiceSid`, `scheduleType=Fixed`, `sendAt`=now+3d).
- **Cancel** (`POST /api/orders/{id}/cancel`) → `CreateMessage` (immediate "cancelled") + `UpdateMessage(status=Canceled)` on the not-yet-sent follow-up.
- **My orders / order notifications** (`GET`) → `FetchMessage` to refresh current outcome.
- **Resend** (`POST /api/notifications/{id}/resend`) → `CreateMessage` (idempotency-key gated).
- **Content disposal** (`DELETE /api/notifications/{id}/content`) → `UpdateMessage(body="")` (redact; record survives).
- **Reconciliation** (`GET /api/notifications/reconciliation`) → `ListMessage(from=FromNumber, DateSent> / DateSent<)`, paged over the whole range.

## 2. CONTRACT SHEET

⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
identifier; named arguments must use them exactly (the cancellation-token param is `ct`, so `ct:`).
⚠ **Every SDK type is fully qualified with the namespace its source path implies** (records →
`TwilioSdk.Models`, enums → `TwilioSdk.Models.Enums`, client/options → `TwilioSdk`, auth →
`TwilioSdk.Core.Authentication.Basic`, config → `TwilioSdk.Core.Configuration`), taken from that
type's own source path — never from a neighbour.

Accessor `client.Api20100401Message` (server group **`Default`** → `https://api.twilio.com`; overridden by `Twilio:BaseUrl`). Accessor `client.LookupsV2PhoneNumber` (server group **`Default4`** → `https://lookups.twilio.com`; **not** overridden by `Twilio:BaseUrl`).

| op | signature (verbatim) | request fields used | response fields read | error | pagination | source |
| --- | --- | --- | --- | --- | --- | --- |
| FetchPhoneNumber3 | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `phoneNumber`=caller input; all 15 optionals → **omit → provider default** (pass `null`) | `LookupResponse.Valid` (bool?), `LookupResponse.PhoneNumber` (E.164 canonical) | `SdkException<RawError>` (Case B) | none | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| CreateMessage | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`; `to`; `body`; **immediate:** `from`=FromNumber, `scheduleType`/`sendAt`/`messagingServiceSid`=null; **scheduled:** `messagingServiceSid`=MessagingServiceSid + `scheduleType`=Fixed + `sendAt`=now+3d, `from`=null. All other 20 optionals → **omit → provider default** (`null`). | `.Sid`, `.Status` (`MessageEnumStatus?`), `.ErrorCode` (int?), `.ErrorMessage`, `.DateCreated`, `.DateSent` | `SdkException<RawError>` (Case B) | none | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs`; `Models/ApiV2010AccountMessage.cs` |
| FetchMessage | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `sid` | `.Status`, `.ErrorCode`, `.ErrorMessage`, `.To`, `.DateSent` | `SdkException<RawError>` (Case B) | none | same page; `Models/ApiV2010AccountMessage.cs` |
| ListMessage | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`; `from`=FromNumber; `dateSentQueryQuery`=range-from (`DateSent>`); `dateSentQuery`=range-to (`DateSent<`); `pageSize`; `page`/`pageToken` for paging; `to`/`dateSent`=null | `.Messages[]` (each `.Sid`,`.Status`,`.To`,`.DateSent`,`.From`), `.NextPageUri` | `SdkException<RawError>` (Case B) | **no `Pageable`** — manual paging via `NextPageUri` → `page`+`pageToken` | same page; `Models/ListMessageResponse.cs` |
| UpdateMessage | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions=null, CancellationToken ct=default)` | **cancel:** `body`=null, `status`=Canceled; **redact:** `body`=`""` (empty string → `Body=` sent; null would be dropped), `status`=null | `.Status`, `.Body` | `SdkException<RawError>` (Case B) | none | same page; `Api/Api20100401Message.cs` (remarks: "redact Message body / cancel not-yet-sent") |

Enums (read via `.Value` → wire string; build via static members / `FromValue`):
- `MessageEnumScheduleType.Fixed` (`"fixed"`) — `Models/Enums/MessageEnumScheduleType.cs`
- `MessageEnumUpdateStatus.Canceled` (`"canceled"`) — `Models/Enums/MessageEnumUpdateStatus.cs`
- `MessageEnumStatus`: `Queued,Sending,Sent,Failed,Delivered,Undelivered,Receiving,Received,Accepted,Scheduled,Read,PartiallyDelivered,Canceled` — `Models/Enums/MessageEnumStatus.cs`. Read status string = `msg.Status?.Value` (`TypedEnum.Value`, `Core/Enum/TypedEnum.cs`).

Client construction / auth / server:
- `new TwilioSdkClient(HttpClient, TwilioSdkClientOptions)` (`TwilioSdkClient.cs`). DI helper `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>?)` builds options once, singleton, HttpClient via `IHttpClientFactory` (`ServiceCollectionExtensions.cs`). **YOUR CALL:** use a small wrapper so options bind from `Twilio:` once at registration.
- Auth: `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }` (`Core/Authentication/Basic/BasicAuthCredentials.cs`; both `required`).
- Server override: when `Twilio:BaseUrl` set → `options.Server.Default.Production.BaseUrl = <BaseUrl>` (messaging group `Default`; `Servers/DefaultOptions.cs`). Lookups (`Default4`) left at default.
- Logging: `options.Logging` (`Core/Configuration/LoggingOptions.cs`) — leave `LogRequestBody=false`, set `LoggerFactory` explicitly (see readiness §7).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| Order line items must reference catalog items that exist | `POST /api/orders` ← `IRepository<CatalogItem>` (existing catalog) | implementation (validate ids; 400 on unknown) |
| Resend/redact/cancel-follow-up act only on a `notificationId` eShop issued (and on a `Sid` eShop stored for it) | operator ops ← `OrderNotification` rows this app created | implementation (load by id; 404 if none) |
| Follow-up cancel targets the `Sid` returned by the follow-up's own `CreateMessage` | `UpdateMessage` ← follow-up `CreateMessage` | implementation (stored `MessageSid`) |

## 3. Trap notes

- Client/DI: HttpClient must be long-lived via `IHttpClientFactory`, not per-request; wrapper lifetime. **MUST load twilio-platforms-team:dotnet-client-initialization**
- Auth: when/where credentials are applied and the skip-not-throw behaviour of an unset credential. **MUST load twilio-platforms-team:dotnet-authentication**
- Calling: list/create ops have many optional params with no C# default → call with named arguments to avoid mis-binding; injected `Idempotency-Key` header is not a real key. **MUST load twilio-platforms-team:dotnet-calling-endpoints**
- Models: `StringEnum<T>` is not a C# enum; building/reading enum values, `AdditionalProperties` on responses. **MUST load twilio-platforms-team:dotnet-models**
- Errors: Case B `SdkException<RawError>`; and `System.Text.Json.JsonException` from a drifted 2xx body or a non-matching error body (see REQUIRED READING). **MUST load twilio-platforms-team:dotnet-error-handling**
- Config/resilience: `POST`/`PATCH`/`DELETE` not retried by default while `PUT` is; `Timeout` is per-attempt not total; `LogRequestBody` logs JSON unredacted; base-URL selection; manual pagination. **MUST load twilio-platforms-team:dotnet-configuration-resilience**
- Testing: the `HttpClient` ctor arg is the fake seam; match project frameworks (xUnit / MSTest). **MUST load twilio-platforms-team:dotnet-testing**

## 4. REQUIRED READING (load all before implementing; contents deliberately not inlined here)

| skill | governs |
| --- | --- |
| twilio-platforms-team:dotnet-client-initialization | Step C client construction + DI |
| twilio-platforms-team:dotnet-authentication | Step C basic-auth credentials |
| twilio-platforms-team:dotnet-calling-endpoints | Steps C/D operation calls |
| twilio-platforms-team:dotnet-models | Steps C/D enums + model mapping |
| twilio-platforms-team:dotnet-error-handling | Step C error boundary |
| twilio-platforms-team:dotnet-configuration-resilience | Step C retries/timeout/logging/base-url/paging |
| twilio-platforms-team:dotnet-testing | Step G tests |

Two mandatory hazard rows (both reach the boundary as `System.Text.Json.JsonException`, needing opposite handling):
- A drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match the operation's generated error shape throws `JsonException` while the error object is constructed, **replacing** the `SdkException` and destroying the HTTP status.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AddOptions<TwilioOptions>().Bind(config.GetSection("Twilio")).Validate(...).ValidateOnStart()` in the Twilio registration; **every** part checked non-blank: `AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`. `BaseUrl` optional. Host refuses to start otherwise. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (`Twilio:*`), auto-loaded in Development; env-vars in other hosts. `TwilioSdkClientOptions`/`BasicAuthCredentials` built **once at registration** in the singleton → a rotated `AuthToken` takes effect only on process restart (accepted; documented). |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Every gateway call is bounded by a caller `CancellationToken` from a per-call `CancellationTokenSource` (≈100 s) → that is the whole-call budget; `RetryOptions` left at default retry count. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Message writes are **POST** → SDK never resents them (good: no accidental double-send). Reconciliation/fetch are GET → safely retried. No `PUT` in scope. |
| 5 | Idempotency & ambiguous writes | `CreateMessage`/`UpdateMessage` take **no** real caller key (injected `Idempotency-Key` is per-call `Guid`, not one). Resend dedup is enforced by **eShop's** caller-supplied idempotency key stored on `OrderNotification.IdempotencyKey` (unique index). For the other sends, the recovery path is reconciliation (`ListMessage from=FromNumber`), not a provider key. |
| 6 | Observability | Info: order/notification lifecycle by `orderId`/`notificationId` and Twilio `Sid` + status. Warn: send failures with provider `ErrorCode`/`ErrorMessage` + `Sid`. Never logged: phone numbers, message bodies, auth token. `LogRequestBody` stays off so JSON bodies are never emitted by the SDK. |
| 7 | Sensitive data | `CreateMessage` body carries `To` (phone) and `Body` (message text). So `LogRequestBody=false` **and** `LoggerFactory` set explicitly at registration → `TWILIOCLIENT_LOG` cannot switch body logging on from outside code. App logs never echo `To`/`Body`. |
| 8 | Environment selection | One environment (`Production`). Messaging group `Default` (api.twilio.com) — overridable by `Twilio:BaseUrl` (used to point at a mock in tests / a different region). Lookups group `Default4` (lookups.twilio.com) untouched by `BaseUrl`. No SDK sandbox env exists; test traffic is kept off the live account by (a) never invoking gateway ops in automated tests and (b) `Twilio:BaseUrl` pointing at a non-Twilio mock when needed. |
| 9 | Duplicate prevention under concurrency | Store **`OrderNotifications`** table, column **`IdempotencyKey`**; **unique index** rejects the second row (`DbUpdateException` caught in `OrderNotificationService.ResendAsync`, which then re-reads by key and returns the first row's id). ⚠ In-memory provider (task-mandated `UseOnlyInMemoryDatabase=true`) does not enforce unique indexes — an externally-imposed environment limit, noted in §6; the index + catch is correct on SQL Server and a pre-read fast-path covers the in-memory case. |
| 10 | Partial results | Reconciliation pages until `NextPageUri` is null. A safety page cap (`MaxPages`) is enforced; when hit, the response carries `"complete": false` so the caller learns the range was truncated (not a log line). Normal completion → `"complete": true`. |
| 11 | Startup validation vs existing test host | Host-booting projects: `PublicApiIntegrationTests` (`WebApplicationFactory<Program>`) and `FunctionalTests` (`WebApplicationFactory<AuthenticateEndpoint>`). Both given **placeholder** `Twilio:*` config (integration: in `appsettings.test.json`; functional: `ConfigureAppConfiguration` in the fixture) so `ValidateOnStart` passes. Both test suites run and must be green (Step G). |
| 12 | Ordering & no-op side effects | The local `OrderNotification` row is inserted **before** the provider `CreateMessage` and updated with `Sid`/status **after**. Dispatch/cancel first flip `Order.Status` (guarded transition); the notification + follow-up are gated on the transition actually happening — an already-dispatched/-cancelled order does not re-notify or re-schedule. |
| 13 | Unknown outcomes | `CreateMessage` transport failure after the provider may have created the message: the row stays with `Sid=null`, status `send_failed`. Re-read via **`ListMessage`** (from=`FromNumber`, `To`+`DateSent` window) — surfaced by the **reconciliation** endpoint as "provider has, eShop doesn't". No definite-failure is reported without that reconciliation path existing. |
| 14 | Provider status & reconciliation clocks | `CreateMessage`/`FetchMessage`/`ListMessage` return `Status`; each non-success value (`failed`,`undelivered`,`canceled`, null) is stored verbatim on the row and surfaced (never coalesced to a fake success). Reconciliation single timestamp both sides filter on = the **message send time**: provider via `DateSent</DateSent>`, eShop via `OrderNotification.SentAtUtc` (the dispatch/scheduled-send moment) — **not** the row-creation column (`CreatedAtUtc` is audit-only). |

### DUPLICATE CLAIMS
| write | where the claim is stored | what rejects the second one | where that rejection is caught |
| --- | --- | --- | --- |
| resend a notification (`POST /api/notifications/{id}/resend`) | `OrderNotifications.IdempotencyKey` (unique index, per `OrderNotificationConfiguration`) | the unique index → `DbUpdateException` | `OrderNotificationService.ResendAsync` (catch → re-read by key → return existing `notificationId`) |

### PAGED READS
| read | what caps it | how the caller learns the answer was cut short |
| --- | --- | --- |
| reconciliation `ListMessage` loop | provider page size + `MaxPages` safety cap | response field `complete` (`false` when the cap was hit) |

### REPEATED OPERATIONS
| operation | what tells you the state actually changed | the effects gated on that |
| --- | --- | --- |
| dispatch | `Order.Status` transitioned `Placed → Dispatched` (guard rejects if not `Placed`) | "on its way" message + scheduled follow-up |
| cancel | `Order.Status` transitioned to `Cancelled` (guard rejects if already `Cancelled`) | "cancelled" message + `UpdateMessage(Canceled)` on the not-yet-sent follow-up |
| resend | no existing row for the idempotency key | the `CreateMessage` send |

### UNKNOWN OUTCOMES
| write | the operation you re-read with | the reference you search by |
| --- | --- | --- |
| `CreateMessage` (any send) | `ListMessage` (reconciliation) | `from`=`FromNumber` (+ `To` / `DateSent` window) |
| `UpdateMessage` (cancel follow-up) | `FetchMessage` | the stored follow-up `Sid` |

## 6. Assumptions & Blockers

- **Assumption (minor):** "a few days later" for the delivery follow-up = **3 days** (within Twilio's 15-min–7-day scheduling window). Decided; proceed.
- **Assumption (minor):** order placement uses a fixed placeholder ship-to address (the API carries no address; existing `Order` requires one). Decided; proceed.
- **Assumption (minor):** SDK is **not** on NuGet → vendored as source under `src/TwilioSdk` (opting out of central package management there) and referenced from Infrastructure. This is the "build from source and reference" path the getting-started skill prescribes.
- **Environment limits (task-imposed, not gaps):** `UseOnlyInMemoryDatabase=true` → unique indexes/migrations not enforced, data non-durable across runs (see §9); US destinations are legitimately undeliverable for this live account (an outcome, recorded on the row, not a gap).
- **Blockers:** none. Every required capability is covered by the five operations above.
