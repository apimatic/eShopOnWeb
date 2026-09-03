# Twilio SMS order notifications — plan

## 1. Scope & sequence

1. **SDK reference** — build the unpublished Twilio .NET SDK from source (`Twilio` / `TwilioClient`) and ProjectReference it from Infrastructure. No NuGet package exists.
2. **Configuration & client** — bind `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (optional). Fail-fast on blank required parts. Construct `TwilioClient` via `AddTwilioClient`. When `Twilio:BaseUrl` is set, assign it verbatim to the **Default** server group only (Messages API). Lookups stay on **Default4**.
3. **Contact numbers (shopper)** — `LookupsV2PhoneNumber.FetchPhoneNumber3` to reject unusable destinations and store `LookupResponse.PhoneNumber` (E.164). CRUD via PublicApi `/api/contact-numbers`.
4. **Orders (existing aggregate)** — `POST /api/orders` creates `Order` + `OrderItem` from catalog ids/qty. Add `Order.Status` (Placed / Dispatched / Cancelled) on the existing entity. Dispatch / cancel are operator-only.
5. **Send on place / dispatch / cancel** — `Api20100401Message.CreateMessage`. Immediate SMS uses `from` = `Twilio:FromNumber`. Dispatch also queues a delivery follow-up via `scheduleType=fixed` + `sendAt` (~3 days) + `messagingServiceSid`. Send failures never fail the HTTP operation.
6. **Cancel follow-up** — on order cancel, `UpdateMessage(status: Canceled)` for any still-scheduled follow-up SID stored on the notification row.
7. **Read path** — `GET /api/my-orders` and `GET /api/orders/{orderId}/notifications` refresh delivery outcome via `FetchMessage` (no inbound webhooks). Persist provider `Sid` + status.
8. **Resend (operator)** — application-level idempotency key (CreateMessage has no real key). Repeat key returns the existing new `notificationId`; fresh key calls `CreateMessage` again.
9. **Redact content (operator)** — `UpdateMessage(body: "")` so the provider no longer returns the text; keep the row and outcome. Clear locally stored body.
10. **Reconciliation (operator)** — `ListMessage(from: Twilio:FromNumber, DateSent> from, DateSent< to)`, walk `next_page_uri` / `pageToken` until exhausted. Compare provider SIDs to local rows.

---

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim. Every parameter name is the literal C# identifier (the cancellation-token parameter is named `ct`, so named arguments write `ct:`).
⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

### Client / auth / servers

| Fact | Value | Source |
| --- | --- | --- |
| Client | `Twilio.TwilioClient(HttpClient httpClient, Twilio.TwilioClientOptions options)` only ctor | `sdk-map.md` Getting a client |
| DI | `Twilio.ServiceCollectionExtensions.AddTwilioClient(Action<TwilioClientOptions>?)` — singleton capturing options | `ServiceCollectionExtensions.cs` |
| Auth | `options.AccountSidAuthToken = new Twilio.Core.Authentication.Basic.BasicAuthCredentials { Username, Password }` (required members). Username = Account SID, Password = auth token. | `sdk-map.md` Servers & auth; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `Twilio.Servers.ServerEnvironment.Production` (default) | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Messages server group | **Default** `https://api.twilio.com` — override `options.Server.Default.Production.BaseUrl` | `sdk-map.md` Servers & auth; `Api/Api20100401Message.cs` uses `_server.Default(...)` |
| Lookups server group | **Default4** `https://lookups.twilio.com` — **not** governed by `Twilio:BaseUrl` | `map/operations/LookupsV2PhoneNumber.md` Server group Default4 |
| Retry defaults | `RetryOptions.Default()`: GET/HEAD/PUT/OPTIONS retried; POST/PATCH/DELETE not; `Timeout` 100s; MaxRetries 3 | `Core/Configuration/RetryOptions.cs` |
| Options surface | `Environment`, `Retry`, `Logging`, `Server`, `Hooks`, `AccountSidAuthToken` | `TwilioClientOptions.cs` |
| Logging | `Twilio.Core.Configuration.LoggingOptions`: `LogRequestBody`, `LoggerFactory`, `RedactedKeys` (form deny-list). Log env var `TWILIOCLIENT_LOG` | `sdk-map.md`; `Core/Configuration/LoggingOptions.cs`; getting-started |
| Errors | Throw-only. No `…Result` variants. 861 Case B / 37 Case A. This scope is **all Case B**. | `sdk-map.md` Error-handling model |
| `SdkException<TError>` | `Twilio.Core.Exceptions.SdkException<TError>` with `required TError Error` | `Core/Exceptions/SdkException.cs` |
| Case B `RawError` | `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | `sdk-map.md` |

### Operations

| Controller | Method (verbatim) | Request | Response fields used | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.LookupsV2PhoneNumber` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 15 nullable params **must pass explicitly** | `phoneNumber` required. Optional `fields` unused (default lookup already returns `valid` + `phone_number`). All other optionals `null`. | `Twilio.Models.LookupResponse`: `PhoneNumber` (wire `phone_number`) E.164 canonical; `Valid` (wire `valid`); `ValidationErrors` (wire `validation_errors`). Reject unless `Valid == true` and `PhoneNumber` non-empty. | Case B `SdkException<Twilio.Core.ErrorResponse.RawError>` | none (default) | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs`; `Api/LookupsV2PhoneNumber.cs` |
| `client.Api20100401Message` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 nullable params **must pass explicitly**. HTTP POST form. Injected `Idempotency-Key: Guid.NewGuid()` is **not** a real key. | Immediate: `to`, `from`=`Twilio:FromNumber`, `body`; `messagingServiceSid`/`scheduleType`/`sendAt` = null. Scheduled follow-up: `to`, `body`, `messagingServiceSid`=`Twilio:MessagingServiceSid`, `scheduleType`=`MessageEnumScheduleType.Fixed`, `sendAt` ≈ now+3d; `from`=null. Unused optionals `null`. No `statusCallback` (app has no public URL). | `Twilio.Models.ApiV2010AccountMessage`: `Sid`, `Status`, `To`, `From`, `Body`, `ErrorCode`, `ErrorMessage`, `DateCreated`, `DateSent`, `DateUpdated`, `MessagingServiceSid` | Case B | none | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs`; `Api/Api20100401Message.cs` |
| `client.Api20100401Message` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` (provider message SID) | same `ApiV2010AccountMessage` fields | Case B | none | `map/operations/Api20100401Message.md` |
| `client.Api20100401Message` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 nullable **must pass explicitly**. Query: `To`←`to`, `From`←`from`, `DateSent`←`dateSent`, `DateSent<`←`dateSentQuery`, `DateSent>`←`dateSentQueryQuery`, `PageSize`, `Page`, `PageToken`. | `from` = `Twilio:FromNumber` (provider-side filter, not post-filter). `dateSentQueryQuery` = report `from`, `dateSentQuery` = report `to`. `to`/`dateSent` = null. `pageSize` = 1000 (doc max). Walk `pageToken` from `NextPageUri`. | `Twilio.Models.ListMessageResponse`: `Messages` (`IReadOnlyList<ApiV2010AccountMessage>?`), `NextPageUri`, `Page`, `PageSize` | Case B | **Not a Pageable** (no Pagination bullet). Returns one page; app loops `pageToken` until `NextPageUri` is null. `pageSize` default 50, max 1000 (`Api/Api20100401Message.cs` param docs). | `map/operations/Api20100401Message.md`; `Models/ListMessageResponse.cs`; `Api/Api20100401Message.cs` |
| `client.Api20100401Message` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` **must pass explicitly**. HTTP POST. Remarks: “used to redact Message body text and to cancel not-yet-sent messages”. | Redact: `body=""`, `status`=null. Cancel scheduled: `status`=`MessageEnumUpdateStatus.Canceled`, `body`=null. | `ApiV2010AccountMessage` | Case B | none | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs` |
| — | `DeleteMessage` **out of scope** — would remove the resource; task requires the fact of the send + outcome to survive. | — | — | — | — | YOUR CALL — not in the map |

### Enums (needed)

| Type | Members used | Source |
| --- | --- | --- |
| `Twilio.Models.Enums.MessageEnumScheduleType` | `Fixed` wire `fixed`. Remarks: Messaging Services only, with send time. | `Models/Enums/MessageEnumScheduleType.cs` |
| `Twilio.Models.Enums.MessageEnumUpdateStatus` | `Canceled` wire `canceled` | `Models/Enums/MessageEnumUpdateStatus.cs` |
| `Twilio.Models.Enums.MessageEnumStatus` | `Queued`, `Sending`, `Sent`, `Failed`, `Delivered`, `Undelivered`, `Receiving`, `Received`, `Accepted`, `Scheduled`, `Read`, `PartiallyDelivered`, `Canceled`. Persist `.Value`. | `Models/Enums/MessageEnumStatus.cs` |
| `Twilio.Models.Enums.ValidationError` | `TooShort`, `TooLong`, `InvalidButPossible`, `InvalidCountryCode`, `InvalidLength`, `NotANumber` | `Models/Enums/ValidationError.cs` |
| StringEnum | `Twilio.Core.Enum.StringEnum<T>` / `TypedEnum<TValue,TEnum>`: `.Value`, `ToString()`, implicit to underlying | `Core/Enum/StringEnum.cs`; `Core/Enum/TypedEnum.cs` |

### Request-model fields actually passed (CreateMessage has no request record — form params)

All CreateMessage fields are method params. Carried: `to`, `from`, `body`, `messagingServiceSid`, `scheduleType`, `sendAt`. Left null: `statusCallback`, `applicationSid`, `maxPrice`, `provideFeedback`, `attempt`, `validityPeriod`, `forceDelivery`, `contentRetention`, `addressRetention`, `smartEncoded`, `persistentAction`, `trafficType`, `shortenUrls`, `sendAsMms`, `contentVariables`, `riskCheck`, `fallbackFrom`, `mediaUrl`, `contentSid`.

---

## 3. Trap notes

| Step | Hazard | Skill |
| --- | --- | --- |
| 2 Client/DI | `HttpClient` lifetime vs wrapper lifetime — a per-request client rebuilds the handler pipeline. | **MUST load `dotnet-client-initialization`** |
| 2 Auth | Credentials must be on options before construct; blank vs missing parts; where they are read from. | **MUST load `dotnet-authentication`** |
| 5–10 Calls | Named arguments required: many optionals have no C# default and mis-bind positionally. Real vs injected idempotency. | **MUST load `dotnet-calling-endpoints`** |
| 3,5 Models | `StringEnum<T>` is not a C# enum; `required`/nullability on records; unknown fields live on `AdditionalProperties`. | **MUST load `dotnet-models`** |
| 3,5–10 Errors | All in-scope ops are Case B; catch type must match. A 2xx body missing a `required` member is `JsonException`, not `SdkException`. A non-2xx body that fails `{Operation}Error` shape also throws `JsonException` and destroys the status. | **MUST load `dotnet-error-handling`** |
| 2,5,10 Resilience | `Timeout` vs total budget across retries; `HttpMethodsToRetry` gates POST (Create/Update). `Twilio:BaseUrl` vs 15 server groups. ListMessage is not Pageable — looping is ours. JSON bodies unredacted if `LogRequestBody` on; form keys only deny-listed; `TWILIOCLIENT_LOG=trace` can enable bodies if `LoggerFactory` is unset. | **MUST load `dotnet-configuration-resilience`** |
| Tests | Which constructor argument is the fake seam; do not stub SDK internals. | **MUST load `dotnet-testing`** |

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
| --- | --- |
| `twilio-platforms-team/dotnet/dotnet-client-initialization` | Step 2 — constructing/registering `TwilioClient` |
| `twilio-platforms-team/dotnet/dotnet-authentication` | Step 2 — `AccountSidAuthToken` |
| `twilio-platforms-team/dotnet/dotnet-calling-endpoints` | Steps 3–10 — every operation call |
| `twilio-platforms-team/dotnet/dotnet-models` | Lookup/message records and StringEnums |
| `twilio-platforms-team/dotnet/dotnet-error-handling` | Every try/catch around an SDK call |
| `twilio-platforms-team/dotnet/dotnet-configuration-resilience` | Retries, timeout, Default vs Default4, ListMessage paging, logging |
| `twilio-platforms-team/dotnet/dotnet-testing` | Unit tests around the integration layer |

JsonException rows (mandatory):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bind `IOptions<TwilioOptions>` from section `Twilio` at startup in PublicApi. Refuse to start (`InvalidOperationException`) if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is missing or whitespace. `BaseUrl` may be blank. All four required parts checked independently. |
| 2 | **Secret sourcing & rotation** | Secrets from .NET user-secrets (Development) / env (`Twilio__*` or user-secrets populated from `TWILIO_*`). Never in repo files. `AddTwilioClient` captures options in a singleton — rotation requires process restart. No in-process rotation. |
| 3 | **Total timeout budget** | Keep SDK `Retry.Timeout` at default 100s/attempt. Writes (POST Create/Update) are not retried → one attempt, ~100s. Lookups/Fetch/List are GET → up to 1+3 attempts. Each app call also takes a `CancellationToken` from the HTTP request so the caller's deadline bounds the whole operation. |
| 4 | **Write-retry ownership** | `CreateMessage` and `UpdateMessage` are POST → SDK will not resend. `FetchMessage`/`ListMessage`/`FetchPhoneNumber3` are GET → SDK may retry. App does not add its own write retry. |
| 5 | **Idempotency & ambiguous writes** | CreateMessage: **no** real caller key (injected header is a fresh GUID). Resend: app stores `(IdempotencyKey, SourceNotificationId) → NewNotificationId`; same key returns existing id without a second CreateMessage. UpdateMessage (redact/cancel): no provider key; cancel is keyed by stored SID (repeat cancel is the same SID). Reconciliation is the recovery path for any CreateMessage that succeeded on the wire after the client timed out. |
| 6 | **Observability** | `ILogger` at Information for operation+provider SID+status (never `To`/`From`/body). Warning when a send is skipped (no number) or swallowed so the order still succeeds. `LogRequestBody` stays **false**. Correlation: Case B `RawError.StatusCode` + `ReadAsString()` truncated; do not log `To`/`Body`. |
| 7 | **Sensitive data** | CreateMessage form carries `To` and `Body` (shopper number + message text). `LogRequestBody=false`. `LoggerFactory` assigned explicitly from DI so `TWILIOCLIENT_LOG` cannot turn bodies on. Extend `RedactedKeys` with `To`, `From`, `Body`, `MessagingServiceSid`. Application logs never include the shopper number. After redact, local body is cleared. Auth token never logged or returned. |
| 8 | **Environment selection** | Only `ServerEnvironment.Production` exists (no sandbox in the map). Test traffic is the live account, volume limited to `TWILIO_TEST_TO_NUMBER` and `TWILIO_UNREACHABLE_TO_NUMBER`. Messages API (Default) uses `Twilio:BaseUrl` when set; Lookups (Default4) always default host. |

---

## 6. Assumptions & Blockers

**Assumptions (minor — proceeding):**

- “Usable destination” = Lookups V2 `Valid == true` plus a non-empty canonical `PhoneNumber`. No extra paid `fields` packages. The unreachable US fixture is still a valid E.164 and **must register**; later undelivered/failed is an expected outcome, not a registration reject.
- Immediate SMS uses `From`; scheduled follow-up uses `MessagingServiceSid` + `ScheduleType.Fixed` (enum remarks: scheduling is Messaging Services only). Follow-up delay is **3 days**.
- One destination per shopper event: the most recently registered remaining number. Deleted numbers are never used.
- `GET /api/orders/{orderId}/notifications` is shopper-owned (404 if not the caller’s order). Operator obtains `notificationId` from `GET /api/my-orders` (when acting as that shopper in tests) and from the reconciliation report (includes eShop `notificationId` + provider SID).
- US undeliverable after accept is stored as the provider status, not treated as a gap.
- In-memory DB for this host; all verify steps in one PublicApi process.
- `global.json` will use `rollForward: latestMajor` per the machine note.

**Blockers:** none. Required operations (lookup, create, fetch, list-by-From, update for redact + cancel, schedule via CreateMessage) are all on the map.

---

## 7. Application conventions (read-only survey)

| Convention | Exemplar to imitate at edit time |
| --- | --- |
| PublicApi endpoint (MinimalApi `IEndpoint`) | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| Admin-only `[Authorize(Roles=ADMINISTRATORS, AuthenticationSchemes=JwtBearer)]` | `src/PublicApi/CatalogItemEndpoints/DeleteCatalogItemEndpoint.cs` |
| Request/response + correlation | `CreateCatalogItemEndpoint.CreateCatalogItemRequest.cs` / `CreateCatalogItemResponse.cs`; `src/PublicApi/BaseResponse.cs` |
| JWT authenticate | `src/PublicApi/AuthEndpoints/AuthenticateEndpoint.cs` |
| JWT identity claim | `src/Infrastructure/Identity/IdentityTokenClaimService.cs` (`ClaimTypes.Name` = username) |
| Admin role constant | `BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS`; seed `src/Infrastructure/Identity/AppIdentityDbContextSeed.cs` (`admin@microsoft.com` / `demouser@microsoft.com`, password `AuthorizationConstants.DEFAULT_PASSWORD`) |
| Aggregate + EF config | `src/ApplicationCore/Entities/OrderAggregate/Order.cs`; `src/Infrastructure/Data/Config/OrderConfiguration.cs` |
| Specification | `src/ApplicationCore/Specifications/CustomerOrdersWithItemsSpecification.cs` |
| Repository | `src/Infrastructure/Data/EfRepository.cs`; register in `src/PublicApi/Program.cs` |
| Infra service behind interface | `src/ApplicationCore/Interfaces/IEmailSender.cs` + `src/Infrastructure/Services/EmailSender.cs` |
| Exception middleware | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
| Unit test style | `tests/UnitTests/ApplicationCore/Services/BasketServiceTests/AddItemToBasket.cs` (xunit + NSubstitute) |
| Catalog seed items | `src/Infrastructure/Data/CatalogContextSeed.cs` (ids 1–12 after seed) |
| PublicApi host | `src/PublicApi/Properties/launchSettings.json` — `https://localhost:21583` |
| DI / JWT / swagger | `src/PublicApi/Program.cs` |

### Application design (YOUR CALL — not in the map)

- New aggregates: `ContactNumber` (BuyerId, CanonicalNumber); `OrderNotification` (OrderId, BuyerId, ProviderSid, Kind, Body?, Status, ErrorCode?, ToNumber, FromNumber?, ScheduledSendAt?, ContentRedacted, CreatedAt); `ResendIdempotency` (Key, SourceNotificationId, ResultNotificationId).
- `Order` gains `Status` + `MarkDispatched()` / `MarkCancelled()`.
- Gateway interface in ApplicationCore; Twilio SDK only in Infrastructure.
- Notification send after the order write; catch SDK/`JsonException`/`Exception` and log without destination.
- Resend allowed when latest fetched status is `failed` or `undelivered`.
- PublicApi only; no Web UI.

### Config keys (names only)

| Binding key | Arrives as |
| --- | --- |
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` via user-secrets |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | optional; unset here |
