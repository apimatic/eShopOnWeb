# twilio-plan.md — Order notifications by SMS (Twilio) for eShopOnWeb

Integration target: `src/PublicApi` (JWT). Twilio interaction is isolated behind an
`ISmsGateway` abstraction in `ApplicationCore`, implemented by `TwilioSmsGateway` in
`Infrastructure` (the only project that references the Twilio SDK). `ApplicationCore` never
references `TwilioSdk`.

## Repo survey — conventions (pattern + one exemplar to imitate)

- **PublicApi endpoints**: `IEndpoint<IResult, TRequest, TDeps...>` (MinimalApi.Endpoint), self-registered by `AddEndpoints()`/`MapEndpoints()`. Exemplar: `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs`. Routes hard-code `"api/..."`. `.Produces<T>().WithTags("...")`.
- **Admin gate**: attribute on the lambda — `[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`. Shopper gate: `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`. Role string = `"Administrators"`.
- **Caller identity**: not currently read anywhere; token carries `ClaimTypes.Name = username` (minted in `Infrastructure/Identity/IdentityTokenClaimService.cs`). Read via `ClaimsPrincipal` delegate param → `user.FindFirstValue(ClaimTypes.Name)`. `Order.BuyerId`/`Basket.BuyerId` == username (email).
- **Request/Response DTOs**: `TRequest : BaseRequest`, `TResponse : BaseResponse` (carry `CorrelationId()`), dotted filenames, per-endpoint folder. Exemplar: `CatalogItemEndpoints/*`.
- **Entities**: `BaseEntity` (int `Id`) + `IAggregateRoot` marker for repo-backed roots. Private EF ctor + guarded public ctor (`Ardalis.GuardClauses`), `private set`. Exemplar: `ApplicationCore/Entities/OrderAggregate/Order.cs`.
- **EF config**: `IEntityTypeConfiguration<T>` in `Infrastructure/Data/Config/`, auto-discovered by `CatalogContext.OnModelCreating` (`ApplyConfigurationsFromAssembly`). Add a `DbSet<T>` to `CatalogContext`. Exemplar: `Infrastructure/Data/Config/OrderConfiguration.cs`.
- **Repositories**: open generic `IRepository<>`/`IReadRepository<>` → `EfRepository<>` already registered in `PublicApi/Program.cs`. No new DI for a new aggregate in `CatalogContext`.
- **In-memory vs SQL**: `Infrastructure/Dependencies.cs` branches on `UseOnlyInMemoryDatabase`. New entities in `CatalogContext` work in both automatically. (In-memory ignores migrations.)
- **External-IO service analog**: `IEmailSender`/`Infrastructure/Services/EmailSender.cs`, registered in DI. Model `ISmsGateway` the same way.

## 1. Scope & sequence

1. **Vendor SDK**: copy Twilio SDK source into `src/TwilioSdk/`, set `ManagePackageVersionsCentrally=false` in its csproj (repo uses central package mgmt); add to solution; `ProjectReference` from `Infrastructure`. (SDK is not on NuGet; a `ProjectReference` carries its transitive package deps, a bare DLL would not.)
2. **Config + client**: bind `Twilio:` settings (fail-fast), register one long-lived `TwilioSdkClient` (Basic auth = AccountSid/AuthToken), messaging base-URL override, explicit LoggerFactory. `Infrastructure/Configuration/ConfigureTwilioServices.cs`.
3. **Gateway**: `ISmsGateway` (ApplicationCore) + `TwilioSmsGateway` (Infrastructure) wrapping ops: `LookupNumberAsync` (validate+canonicalize), `SendAsync` (immediate), `ScheduleAsync` (follow-up), `FetchAsync` (status), `CancelScheduledAsync`, `RedactContentAsync`, `ListSentAsync` (reconciliation). Own exception `SmsGatewayException` at the boundary.
4. **Entities + EF config**: `ContactNumber`, `OrderNotification` (+ `NotificationType` enum) in `ApplicationCore/Entities`; configs + DbSets in Infrastructure.
5. **App services** (ApplicationCore/Services, no Twilio ref): `ContactNumberService` (register/list/delete), `OrderMessagingService` (place/dispatch/cancel/resend/redact/reconcile + status refresh).
6. **PublicApi endpoints** (11): contact-numbers (POST/GET/DELETE), orders (POST, dispatch, cancel), my-orders (GET), order notifications (GET), notifications (resend POST, content DELETE, reconciliation GET).
7. **Wire DI** in `PublicApi/Program.cs`; user-secrets from env; migration for SQL path.
8. **Self-verify** end-to-end against the two provided numbers; write verify guide.

Operations used (all confirmed in map): `LookupsV2PhoneNumber.FetchPhoneNumber3`, `Api20100401Message.{CreateMessage, FetchMessage, ListMessage, UpdateMessage, DeleteMessage}`. No capability is missing → no Blockers.

## 2. CONTRACT SHEET

⚠ Signatures below are generated code, verbatim. Every parameter name is the literal C# identifier; named args use those exact names (cancellation-token param is `ct`, so `ct:`).
⚠ Every SDK type is fully-qualified with the namespace its **source path** implies (root namespace is `TwilioSdk`, per the map — NOT `Twilio`): `TwilioSdk` (client/options), `TwilioSdk.Servers` (`ServerEnvironment`), `TwilioSdk.Models` (records), `TwilioSdk.Models.Enums` (enums), `TwilioSdk.Core.Exceptions` (`SdkException<>`), `TwilioSdk.Core.ErrorResponse` (`RawError`), `TwilioSdk.Core.Authentication.Basic` (`BasicAuthCredentials`), `TwilioSdk.Core.Configuration` (`RetryOptions`, `LoggingOptions`).

| Op | Controller · signature | Request fields used | Response envelope (fields read) | Error | Pag | Source |
| --- | --- | --- | --- | --- | --- | --- |
| **Lookup/validate** | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `phoneNumber` = raw input; all 15 optionals → `null` | `LookupResponse`: `Valid` (bool?), `PhoneNumber` (string?, canonical E.164) | Case B `SdkException<RawError>` | none | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| **Send (immediate)** | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `to`=canonical, `from`=FromNumber, `body`=text; **scheduleType/sendAt/messagingServiceSid = null**; all other optionals null | `ApiV2010AccountMessage`: `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `To`, `From` | Case B `SdkException<RawError>` | none | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| **Schedule (follow-up)** | same `CreateMessage` | `to`=canonical, `messagingServiceSid`=MessagingServiceSid, `scheduleType`=`MessageEnumScheduleType.Fixed`, `sendAt`=now+~3d, `body`=text; **`from`=null** (mutually exclusive with messagingServiceSid); rest null | same; expect `Status`=`scheduled` | Case B | none | same |
| **Fetch status** | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `sid` | `ApiV2010AccountMessage`: `Status`, `ErrorCode`, `ErrorMessage`, `DateSent` | Case B | none | same page; model |
| **Cancel scheduled** | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `sid`, `body`=null, `status`=`MessageEnumUpdateStatus.Canceled` | `ApiV2010AccountMessage`: `Status` | Case B | none | same page; `Models/Enums/MessageEnumUpdateStatus.cs` |
| **Redact content** | same `UpdateMessage` | `accountSid`, `sid`, `body`=`""` (empty string redacts body at provider), `status`=null | `ApiV2010AccountMessage`: `Body` (now empty) | Case B | none | same. `<remarks>`: "used to redact Message body text and to cancel not-yet-sent messages" (`Api/Api20100401Message.cs`) |
| **Reconcile/list** | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `to`=null, `from`=**FromNumber** (provider-side sender filter — the mandate), `dateSent`=null, `dateSentQuery`= **to** (wire `DateSent<`, on/before), `dateSentQueryQuery`= **from** (wire `DateSent>`, on/after), `pageSize`=200, `page`=0..N, `pageToken`=null | `ListMessageResponse`: `Messages` (`IReadOnlyList<ApiV2010AccountMessage>`), `NextPageUri`, `Page` | Case B | manual (no Pageable) — loop `page`, stop when messages<pageSize or NextPageUri null; **page cap + deadline** | same page; `Models/ListMessageResponse.cs` |

Enums (values needed): `MessageEnumScheduleType.Fixed` (`"fixed"`); `MessageEnumUpdateStatus.Canceled` (`"canceled"`); `MessageEnumStatus` outcomes read as `.Value` string: `queued/sending/sent/failed/delivered/undelivered/receiving/received/accepted/scheduled/read/partially_delivered/canceled` (`Models/Enums/MessageEnumStatus.cs`). Sources: `Models/Enums/*.cs`.

Client construction / auth / servers:
- `new TwilioSdkClient(httpClient, options)`. Options: `AccountSidAuthToken = new BasicAuthCredentials { Username = Twilio:AccountSid, Password = Twilio:AuthToken }`; `Environment = ServerEnvironment.Production`.
- Messaging ops (`Api20100401Message.*`) resolve through server group **`Default`** (`https://api.twilio.com`) → override with `options.Server.Default.Production.BaseUrl = Twilio:BaseUrl` **only when set**. Lookup resolves through **`Default4`** (`https://lookups.twilio.com`) → **not** overridden (matches "does not govern those"). Source: sdk-map.md *Servers & auth*.
- `options.Logging = new LoggingOptions { LoggerFactory = <DI ILoggerFactory>, LogRequestBody=false, LogRequestHeaders=false, LogResponseHeaders=false }` — set explicitly so `TWILIOCLIENT_LOG` cannot enable body logging (row 7).
- `options.Retry = RetryOptions.Default() with { Timeout = 15s }` (per attempt). Writes (POST/DELETE) not resent by default — good.

## 3. Trap notes (name the hazard + MUST load; do not resolve here)

- Client/HttpClient lifetime & singleton stale-DNS vs OAuth-cache trade; where LoggerFactory comes from — **MUST load dotnet-client-initialization**.
- What `Retry.Timeout` actually bounds vs a whole-call budget; per-attempt vs total; base-URL override is per server+environment and read live; manual page-loop must be bounded — **MUST load dotnet-configuration-resilience**.
- `BasicAuthCredentials` shape; both credential halves must be non-blank; secret sourced from config, captured once at registration — **MUST load dotnet-authentication**.
- Named-argument requirement for the many no-default nullable params on `CreateMessage`/`ListMessage`/`FetchPhoneNumber3`; form-body ops have no `body` model — **MUST load dotnet-calling-endpoints**.
- `StringEnum` is not a C# enum; `.Value` not `ToString()` for wire value; `DateTimeOffset` serialization for `sendAt`/date filters — **MUST load dotnet-models**.
- Case B everywhere → `SdkException<RawError>` (`.StatusCode`, `.ReadAsString()`); a 2xx with a drifted body throws `JsonException` (not `SdkException`) and must be caught at the boundary; connection failures are `HttpRequestException`/`TaskCanceledException` — **MUST load dotnet-error-handling**.
- Faking the seam (the `HttpClient` ctor arg / `ISmsGateway`) for tests — **MUST load dotnet-testing** (only if tests are written).

## 4. REQUIRED READING (load before implementation; contents deliberately not copied here)

- **twilio-platforms-team:dotnet-client-initialization** — step 2 (client + DI).
- **twilio-platforms-team:dotnet-authentication** — step 2 (Basic creds + fail-fast).
- **twilio-platforms-team:dotnet-calling-endpoints** — step 3 (every SDK call).
- **twilio-platforms-team:dotnet-models** — step 3 (enums, DateTimeOffset, LookupResponse).
- **twilio-platforms-team:dotnet-configuration-resilience** — step 2/3 (base URL, retry/timeout, paging).
- **twilio-platforms-team:dotnet-error-handling** — step 3 (boundary; always required). Two mandatory hazard rows:
  - a drifted/malformed **2xx** body surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-only catch ladder lets it escape.
  - a **non-2xx** body not matching the operation's generated error shape throws `JsonException` **while the error object is constructed**, **replacing** the `SdkException` and destroying the HTTP status.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `TwilioSettings` bound from `Twilio:` with `[Required]` on `AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`; `.ValidateDataAnnotations().ValidateOnStart()` in `ConfigureTwilioServices`. Both Basic halves (AccountSid+AuthToken) checked — a blank half is misconfigured. `BaseUrl` optional. Host refuses to start otherwise. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (loaded from env vars by the operator; never in repo). Options built once at registration and captured in the singleton client → **rotation requires process restart** (documented; acceptable for this app). |
| 3 | Total timeout budget | `Retry.Timeout=15s` is **per attempt**, not a call budget. Each endpoint handler opens a linked CTS (`RequestAborted` + `CancelAfter(30s)`) and passes that token to every gateway call. Dispatch makes 2 SDK calls (send+schedule) → 2×15s < 30s budget. `HttpClient.Timeout=20s` backstop. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Sends/cancels/redacts/deletes are **POST/DELETE → never auto-resent** (prevents duplicate texts). Lookup/Fetch/List are GET → retryable (safe, idempotent reads). No verb added to the retry list. |
| 5 | Idempotency & ambiguous writes | `CreateMessage` exposes **no** caller idempotency-key param (generator-injected `Idempotency-Key` header is not one). **Resend** idempotency enforced at app layer: caller-supplied key stored on `OrderNotification.IdempotencyKey`; a repeat key returns the existing notification without sending; a fresh key sends. place/dispatch/cancel take no key — each is a distinct action; a transport failure = unknown outcome, settled by the **reconciliation** report. `YOUR CALL — not in the map` (app design). |
| 6 | Observability | Log at Info: orderId, notificationId, message SID, delivery status, Twilio `ErrorCode`. On failure log `RawError.StatusCode` + `ReadAsString()` (Twilio error body carries `code`/`message`, no secret). **Never** log phone number or body. SDK `LogRequestBody=false`. |
| 7 | Sensitive data | Phone number (`to` form field) and message `body` are sensitive. `LogRequestBody=false` **and** `LoggerFactory` assigned explicitly so `TWILIOCLIENT_LOG` cannot force bodies on. Our own logs never echo number/body. `to` for `CreateMessage` travels in the POST form body (not URL). **VERIFIED-AND-FIXED:** the one place a shopper number reached a log was the **Lookup** URL — the number is a path segment and the SDK logger redacts query keys but **not** the path. Fixed by passing `RequestOptions { LogLevel = LogLevel.None }` on the Lookup call, which makes the logger's `NeedsUrl` false so the URL is never computed or written at any level (Info/Warning-retry/Error). Confirmed via log scan: 0 occurrences of the number after the fix. `ListMessage`'s `From`=FromNumber is a query key and is masked by the SDK anyway. |
| 8 | Environment selection | Live account only; no sandbox env in the SDK. Messaging → `Default` (api.twilio.com), overridable via `Twilio:BaseUrl`. Lookup → `Default4` (lookups.twilio.com), never overridden. Test traffic restricted in code/verification to the two provided numbers (`TWILIO_TEST_TO_NUMBER` deliverable CA, `TWILIO_UNREACHABLE_TO_NUMBER` accepted-then-undeliverable US); never send elsewhere. |

## 6. Assumptions & Blockers

- **Assumption**: the messaging service `Twilio:MessagingServiceSid` has `Twilio:FromNumber` in its sender pool, so a scheduled follow-up (which must go via the messaging service) is sent `From` = FromNumber and therefore appears in the reconciliation report keyed on FromNumber. Immediate messages always send `from`=FromNumber directly, so they always reconcile.
- **Assumption**: US destinations are accepted then undelivered for this account (per task) — treated as a delivery **outcome** (`undelivered`/`failed`), not a gap or defect.
- **Assumption**: `POST /api/orders` builds an `Order` (reusing `Order`/`OrderItem`/`CatalogItemOrdered`) with a placeholder ship-to `Address` (checkout address is out of scope for SMS); identity = token username.
- No Blockers: every required capability (validate/canonicalize, send, schedule, cancel-scheduled, fetch status, redact content, list-by-sender-and-date) is present in the SDK map.

## 7. Source labels

Every contract row above cites its map page and/or declaring source file. Idempotency app-design and order-construction are `YOUR CALL — not in the map`. US-undeliverable behaviour is `UNVERIFIED` until observed at runtime (handled defensively as an outcome).
