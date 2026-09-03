# Twilio SMS order notifications — plan

## 1. Scope & sequence

1. **Vendor + DI** — copy the Twilio .NET SDK into `external/twilio-csharp-sdk` as the *build* reference (map clone stays in temp). Bind `Twilio:` settings. Register `TwilioClient` via `AddTwilioClient`. Fail-fast if `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, or `Twilio:MessagingServiceSid` is missing/blank. Optional `Twilio:BaseUrl` overrides **only** server group `Default` (`options.Server.Default.Production.BaseUrl`). Lookups stay on `Default4`.
2. **Contact numbers (shopper)** — `LookupsV2PhoneNumber.FetchPhoneNumber3` to reject unusable destinations and store `LookupResponse.PhoneNumber` (provider canonical E.164). Endpoints: `POST/GET /api/contact-numbers`, `DELETE /api/contact-numbers/{contactNumberId}`.
3. **Place order (shopper)** — `POST /api/orders` writes the existing `Order`/`OrderItem` model. Then `Api20100401Message.CreateMessage` (immediate; `from` = `Twilio:FromNumber`). Send failure must not fail the HTTP call.
4. **Dispatch (admin)** — persist dispatched. Immediate `CreateMessage` (on its way) + scheduled `CreateMessage` (`scheduleType` = `MessageEnumScheduleType.Fixed`, `sendAt` ≈ now+3 days, `messagingServiceSid` = `Twilio:MessagingServiceSid`).
5. **Cancel (admin)** — persist cancelled. Immediate `CreateMessage` (cancelled). For any stored follow-up still not sent: `Api20100401Message.UpdateMessage` with `status` = `MessageEnumUpdateStatus.Canceled`.
6. **Read paths** — `GET /api/my-orders`, `GET /api/orders/{orderId}/notifications`. Refresh delivery outcome via `Api20100401Message.FetchMessage` using the stored provider SID.
7. **Resend (admin)** — `POST /api/notifications/{notificationId}/resend`. App-owned idempotency key (CreateMessage has **no** real caller key). Repeat key returns the existing new `notificationId`; fresh key sends another `CreateMessage`.
8. **Content disposal (admin)** — `DELETE /api/notifications/{notificationId}/content` → `UpdateMessage` with `body` = `""` (redact at provider). Clear local body. Do **not** `DeleteMessage` (that would drop the provider record).
9. **Reconciliation (admin)** — `GET /api/notifications/reconciliation?from=&to=` → `Api20100401Message.ListMessage` with `from` = `Twilio:FromNumber`, `dateSentQueryQuery` = range start (`DateSent>`), `dateSentQuery` = range end (`DateSent<`). Page until `next_page_uri` is null. Diff against local SIDs.
10. **Tests + live verify** — unit tests on the HttpClient seam; live PublicApi flow against `TWILIO_TEST_TO_NUMBER` / `TWILIO_UNREACHABLE_TO_NUMBER` only.

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim. Every parameter name is the literal C# identifier (cancellation token is `ct`, named arguments write `ct:`).
⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type.

### Operations

| Controller | Method signature | Request / params used | Response fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.LookupsV2PhoneNumber` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 15 nullable no-default params **must pass explicitly** | `phoneNumber` = caller input; `fields` = `null` (default body already has `valid` + `phone_number`); remaining 14 = `null`. **Server group `Default4`**. | `Twilio.Models.LookupResponse`: `PhoneNumber` (wire `phone_number`): `string?`; `Valid` (wire `valid`): `bool?`; `ValidationErrors` (wire `validation_errors`): `IReadOnlyList<Twilio.Models.Enums.ValidationError>?`. Reject unless `Valid == true` and `PhoneNumber` non-blank; store `PhoneNumber`. | Case B `Twilio.Core.Exceptions.SdkException<Twilio.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. Treat non-2xx (incl. 404) as unusable destination. | none (default) | `map/operations/LookupsV2PhoneNumber.md`; `Api/LookupsV2PhoneNumber.cs`; `Models/LookupResponse.cs` |
| `client.Api20100401Message` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 nullable no-default params **must pass explicitly**. **No real idempotency parameter** (injected `Idempotency-Key: Guid.NewGuid()` is not a key). | `accountSid` = `Twilio:AccountSid`; `to` = stored canonical number; `body` = SMS text. **Immediate:** `from` = `Twilio:FromNumber`; `messagingServiceSid` = `null`; `scheduleType` = `null`; `sendAt` = `null`. **Scheduled follow-up:** `from` = `Twilio:FromNumber`; `messagingServiceSid` = `Twilio:MessagingServiceSid`; `scheduleType` = `Twilio.Models.Enums.MessageEnumScheduleType.Fixed`; `sendAt` = UTC now + 3 days. All other optionals `null`. POST form to `/2010-04-01/Accounts/{AccountSid}/Messages.json`. Server group **Default**. | `Twilio.Models.ApiV2010AccountMessage`: `Sid` (wire `sid`): `string?`; `Status` (wire `status`): `Twilio.Models.Enums.MessageEnumStatus?`; `Body` (wire `body`): `string?`; `To` (wire `to`): `string?`; `From` (wire `from`): `string?`; `ErrorCode` (wire `error_code`): `int?`; `ErrorMessage` (wire `error_message`): `string?`; `DateSent` (wire `date_sent`): `string?`; `DateCreated` (wire `date_created`): `string?`; `MessagingServiceSid` (wire `messaging_service_sid`): `string?`. Persist `Sid` + `Status.Value`. | Case B `SdkException<RawError>` | none | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs`; `Models/ApiV2010AccountMessage.cs` |
| `client.Api20100401Message` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` = `Twilio:AccountSid`; `sid` = stored provider SID. GET. | Same `ApiV2010AccountMessage` fields as create (refresh `Status`, `ErrorCode`, `ErrorMessage`, `Body`). | Case B | none | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| `client.Api20100401Message` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` must pass explicitly | **Redact:** `body` = `""`, `status` = `null`. **Cancel scheduled:** `body` = `null`, `status` = `Twilio.Models.Enums.MessageEnumUpdateStatus.Canceled`. Remarks: “used to redact Message body text and to cancel not-yet-sent messages”. POST. | Same `ApiV2010AccountMessage` (`Status`, `Body`). | Case B | none | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs` |
| `client.Api20100401Message` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 nullable no-default **must pass explicitly**. Wire: `To`←`to`, `From`←`from`, `DateSent`←`dateSent`, `DateSent<`←`dateSentQuery`, `DateSent>`←`dateSentQueryQuery`, `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`. SDK serializes the three dates with `ToIso8601()`. | `accountSid` = `Twilio:AccountSid`; `to` = `null`; `from` = `Twilio:FromNumber` (provider-side filter — do not list account-wide then filter); `dateSent` = `null`; `dateSentQueryQuery` = `from` query (DateSent>); `dateSentQuery` = `to` query (DateSent<); `pageSize` = `1000`; `page`/`pageToken` advanced while `NextPageUri` is present. | `Twilio.Models.ListMessageResponse`: `Messages` (wire `messages`): `IReadOnlyList<ApiV2010AccountMessage>?`; `NextPageUri` (wire `next_page_uri`): `string?`; `Page` (wire `page`): `int?`. Inner message: `Sid`, `Status`, `To`, `From`, `Body`, `DateSent`, `DateCreated`, `ErrorCode`. | Case B | **No Pageable** (defaults table). Manual page loop until `next_page_uri` is null. `pageToken` “is provided by the API”. | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs`; `Models/ListMessageResponse.cs` |
| `client.Api20100401Message` | `DeleteMessage(...)` | **Not used.** Would remove the provider record; task requires the fact of the send to survive. | — | — | — | YOUR CALL — not in the map |

### Enums in scope

| Type | Members used | Source |
| --- | --- | --- |
| `Twilio.Models.Enums.MessageEnumScheduleType` | `Fixed` = wire `"fixed"` (remarks: Messaging Services only, with send time) | `Models/Enums/MessageEnumScheduleType.cs` |
| `Twilio.Models.Enums.MessageEnumUpdateStatus` | `Canceled` = wire `"canceled"` | `Models/Enums/MessageEnumUpdateStatus.cs` |
| `Twilio.Models.Enums.MessageEnumStatus` | `Queued`, `Sending`, `Sent`, `Failed`, `Delivered`, `Undelivered`, `Receiving`, `Received`, `Accepted`, `Scheduled`, `Read`, `PartiallyDelivered`, `Canceled` — persist `.Value` (string). Build with `FromValue` / static members (`StringEnum<T>`, not a C# enum). | `Models/Enums/MessageEnumStatus.cs`; `Core/Enum/TypedEnum.cs` (`Value`, `ToString`) |

### Client / auth / servers

| Fact | Value | Source |
| --- | --- | --- |
| Client | `Twilio.TwilioClient(HttpClient httpClient, TwilioClientOptions options)` only constructor; `services.AddTwilioClient` singleton wrapping `IHttpClientFactory.CreateClient()` | `sdk-map.md`; `ServiceCollectionExtensions.cs` |
| Auth | HTTP Basic: `options.AccountSidAuthToken = new Twilio.Core.Authentication.Basic.BasicAuthCredentials { Username, Password }` — Username = `Twilio:AccountSid`, Password = `Twilio:AuthToken` | `sdk-map.md` Servers & auth; `TwilioClientOptions.cs` |
| Environment | `Twilio.Servers.ServerEnvironment.Production` only (default). No sandbox member. | `sdk-map.md` |
| Messaging base | Group `Default` → `https://api.twilio.com`; override `options.Server.Default.Production.BaseUrl` from `Twilio:BaseUrl` when set (verbatim). Message ops have no Server-group bullet → Default. | `sdk-map.md`; `Servers/DefaultOptions.cs`; `map/operations/Api20100401Message.md` |
| Lookups base | Group `Default4` → `https://lookups.twilio.com`. **Not** governed by `Twilio:BaseUrl`. | `sdk-map.md`; `Servers/Default4Options.cs` |
| Retry default | `RetryOptions.Default()`: GET/HEAD/PUT/OPTIONS retried; POST/PATCH/DELETE not; MaxRetries=3; Timeout=100s | `Core/Configuration/RetryOptions.cs` |
| Logging | `LoggingOptions`: `LogRequestBody`, `LoggerFactory`, `RedactedKeys` (form deny-list). CreateMessage is `FormUrlEncodedRequest`. | `Core/Configuration/LoggingOptions.cs`; `Api/Api20100401Message.cs` |
| Log env var | `TWILIOCLIENT_LOG` | `dotnet-getting-started` identity table |

### Application decisions (not SDK)

| Decision | Choice | Source |
| --- | --- | --- |
| Follow-up delay | 3 days UTC | YOUR CALL — not in the map |
| Which stored number to text | Most recently registered number still on file; none → skip send | YOUR CALL — not in the map |
| Resend idempotency | Unique `(OriginalNotificationId, CallerKey)` → produced `notificationId`; Twilio create has no key | YOUR CALL — not in the map |
| Order status | Add `Pending` / `Dispatched` / `Cancelled` on existing `Order` | YOUR CALL — not in the map |
| Place-order body | `{ items: [{ catalogItemId, quantity }], shipTo: { street, city, state, country, zipCode } }` | YOUR CALL — not in the map |
| ListMessage paging token | Parse `PageToken` from `next_page_uri` query; increment `page` | YOUR CALL — not in the map |
| SendAt form encoding | CreateMessage passes `DateTimeOffset?` as `Param("SendAt", sendAt)` (ListMessage dates use `ToIso8601()` explicitly). If provider rejects format, inspect the built request via hook — do not invent a different param. | UNVERIFIED |
| Scheduled-message min/max window | 3 days; if provider rejects `sendAt`, treat as send failure (order still dispatches) | UNVERIFIED |

### Repo conventions (pattern + one exemplar)

| Convention | Exemplar |
| --- | --- |
| PublicApi endpoint class (`IEndpoint`, `MapPost`/`MapGet`/`MapDelete`, JWT `Authorize`) | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| Admin role string | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (`BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS`) |
| Auth token issue (buyer id = `ClaimTypes.Name`) | `src/Infrastructure/Identity/IdentityTokenClaimService.cs` |
| Seed users | `src/Infrastructure/Identity/AppIdentityDbContextSeed.cs` |
| Aggregate + `IRepository` | `src/ApplicationCore/Entities/OrderAggregate/Order.cs` |
| EF config | `src/Infrastructure/Data/Config/OrderConfiguration.cs` |
| Infra adapter | `src/Infrastructure/Services/EmailSender.cs` |
| DI / host | `src/PublicApi/Program.cs` |
| Unit test style (xUnit) | `tests/UnitTests/ApplicationCore/Entities/OrderTests/OrderTotal.cs` |

## 3. Trap notes

- Step 1 DI: `HttpClient` lifetime vs wrapper lifetime — a per-request `HttpClient` burns sockets; a mis-scoped client shares handlers unsafely. **MUST load `dotnet-client-initialization`**
- Step 1 auth: when credentials must be set relative to construction, and what a blank `Username`/`Password` part does vs a missing `AccountSidAuthToken`. **MUST load `dotnet-authentication`**
- Steps 2–9 calls: 15/24/8 nullable no-default parameters mis-bind positionally; named arguments required. **MUST load `dotnet-calling-endpoints`**
- Steps 2–9 models: `StringEnum<T>` is not a C# enum — constructing with `new` or comparing to strings without `.Value`/`FromValue` compiles wrong or round-trips wrong. **MUST load `dotnet-models`**
- Steps 2–9 errors: Case B `RawError` vs typed Case A; `TryGetRawError` is not a catch-all on this path. **MUST load `dotnet-error-handling`**
- Step 1 retries: `HttpMethodsToRetry` gates every retry trigger — a hung GET costs a multiple of `Timeout`; a POST is never resent by the SDK, so an ambiguous create is the app’s problem. **MUST load `dotnet-configuration-resilience`**
- Step 1 timeout: what `Timeout` actually bounds vs a `CancellationToken` deadline on the whole call — the number on `RetryOptions` is not the caller’s total budget. **MUST load `dotnet-configuration-resilience`**
- Step 1 logging: JSON bodies log unredacted when `LogRequestBody` is on; form bodies mask only via deny-list; unset `LoggerFactory` lets `TWILIOCLIENT_LOG` force body logging from outside the process. CreateMessage form includes `To`/`From`/`Body`. **MUST load `dotnet-configuration-resilience`**
- Step 9 paging: ListMessage is not a `Pageable`; stopping after one page silently drops the rest of the range. **MUST load `dotnet-configuration-resilience`**
- Step 10 tests: which constructor argument is the fake seam; asserting the request the SDK built, not internal types. **MUST load `dotnet-testing`**

## 4. REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

- `dotnet-client-initialization` — Step 1 client & DI
- `dotnet-authentication` — Step 1 credentials
- `dotnet-calling-endpoints` — Steps 2–9 first SDK call
- `dotnet-models` — StringEnum / records / LookupResponse / ApiV2010AccountMessage
- `dotnet-error-handling` — every catch around an SDK call
- `dotnet-configuration-resilience` — retries, timeout, BaseUrl, logging, ListMessage paging
- `dotnet-testing` — HttpClient seam tests

Mandatory JsonException hazards (both directions):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it. (These operations are Case B `RawError`, but the deserializer of a success body can still throw `JsonException` on 2xx; catch it at the gateway boundary.)

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bind `IOptions<TwilioSettings>` from section `Twilio`. At registration, if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is null/whitespace, throw `InvalidOperationException` and do not start. `BaseUrl` optional (blank = SDK default). Check **every** required part; a blank `AuthToken` with a present `AccountSid` still fails. |
| 2 | **Secret sourcing & rotation** | Values from env (`TWILIO_ACCOUNT_SID` etc.) copied into user-secrets under `Twilio:*` (never into repo files). Program also overlays non-blank env vars onto the `Twilio:` section so non-dev hosts work without user-secrets. `AddTwilioClient` builds options **once** and the client is singleton — rotation requires process restart. |
| 3 | **Total timeout budget** | Set `Retry.Timeout` = 20s (per attempt). Writes (POST Create/Update) are not retried → one attempt, cap ~20s. Reads (GET Fetch/List/Lookup) may retry up to 3 times → up to ~80s plus backoff. Each gateway method also passes `ct` from a 90s `CancellationTokenSource` so a hung retry chain cannot run unbounded. HTTP endpoints use the request token linked with that 90s cap. |
| 4 | **Write-retry ownership** | SDK may retry `FetchMessage`, `ListMessage`, `FetchPhoneNumber3` (GET). SDK will **not** retry `CreateMessage` or `UpdateMessage` (POST). No app-level automatic retry on those writes. |
| 5 | **Idempotency & ambiguous writes** | CreateMessage: **no** real key — persist SID only after 2xx; on transport/`JsonException` with no SID, record local row as `send_failed` without SID (visible in reconciliation as eShop-only). UpdateMessage redact/cancel: no key; retry is operator-driven. Resend: caller `idempotencyKey` stored locally; same key does not call CreateMessage again. Reconciliation (ListMessage GET) is the duplicate-detection path for ambiguous creates. |
| 6 | **Observability** | App logs at Information: operation name, local `notificationId`, provider `sid`, `status` wire value, correlation id = `RawError` string truncated / HTTP status from Case B. Never log `to`/`from`/`body`/auth token. `LogRequestBody` = false. Provider correlation: Case B `StatusCode` + `ReadAsString()` (truncated, no number/body echo). |
| 7 | **Sensitive data** | In-scope form fields: `To`, `From`, `Body` (`Api/Api20100401Message.cs` Param list). `LogRequestBody` **off**. `LoggerFactory` set explicitly to the host factory (so `TWILIOCLIENT_LOG` cannot enable bodies). `RedactedKeys` extended with `To`, `From`, `Body`, `AuthToken`, `Password`. `RedactedHeaders` includes `Authorization`. App log messages never include the shopper number. |
| 8 | **Environment selection** | Only `ServerEnvironment.Production`. Groups touched: `Default` (messages) and `Default4` (lookups). No sandbox. Live account; verify only with configured test/unreachable destinations. `Twilio:BaseUrl` when set replaces `Default` Production base URL only. |

## 6. Assumptions & Blockers

Assumptions:

- Lookup `Valid == true` is the provider’s “usable destination” check; a reserved US number can still be Valid and is stored (delivery failure is a later outcome, not a registration defect).
- Messaging Service SID is required for `scheduleType=fixed` (enum remarks: Messaging Services only). Immediate SMS uses `From` only.
- In-memory catalog and identity DBs; dispatch/cancel the same process that placed the order.
- Shopper JWT identity is `User.Identity.Name` (seed: `demouser@microsoft.com` / `admin@microsoft.com`).

Blockers: none. Map covers lookup, send, schedule, cancel, redact, fetch, list-by-From.

## 7. Sources

Cited per row above. Clone path is session-local and is not recorded here.
