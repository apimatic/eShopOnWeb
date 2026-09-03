# twilio-plan.md — SMS order notifications for eShopOnWeb (PublicApi)

## 1. Scope & sequence

Additive SMS-notification capability on `src/PublicApi`, Twilio via the APIMatic .NET SDK. Layering
mirrors the repo: interface + plain DTOs in `ApplicationCore`, Twilio impl in `Infrastructure`,
HTTP endpoints in `PublicApi` (minimal-API `IEndpoint` style, like `CatalogItemEndpoints`).

Steps:

1. **Config + client + DI (fail-fast).** Bind `Twilio:` section → `TwilioSettings`; validate every part
   present/non-blank at startup or the host refuses to start. Register `TwilioClient` (singleton,
   `IHttpClientFactory`) with Basic auth, per-attempt timeout, and — when `Twilio:BaseUrl` is set —
   `options.Server.Default.Production.BaseUrl` = that value (governs the messaging API only; Lookups is a
   different server group, `Default4`, and is left alone). Register `ISmsGateway → TwilioSmsGateway`.
2. **Contact numbers.** `TwilioSmsGateway.ValidateAndCanonicalize` → `LookupsV2PhoneNumber.FetchPhoneNumber3`
   (reject when not `valid`; store canonical `phone_number`). Endpoints: `POST/GET/DELETE /api/contact-numbers`.
3. **Orders + notifications.** `POST /api/orders` (build `Order` from catalog ids+qty, reuse OrderAggregate),
   `POST /api/orders/{id}/dispatch` (operator), `POST /api/orders/{id}/cancel` (operator). Each sends via
   `CreateMessage` (immediate, `from`=FromNumber). Dispatch also **schedules** the follow-up via
   `CreateMessage` (`messagingServiceSid`, `scheduleType=Fixed`, `sendAt`=now+3d). Cancel calls
   `UpdateMessage(status=Canceled)` on the still-scheduled follow-up. `GET /api/my-orders`,
   `GET /api/orders/{id}/notifications` (refresh status via `FetchMessage`).
4. **Operator ops.** `POST /api/notifications/{id}/resend` (idempotency key; new `CreateMessage`),
   `DELETE /api/notifications/{id}/content` (`UpdateMessage(body="")` redaction + clear stored body),
   `GET /api/notifications/reconciliation?from&to` (`ListMessage` filtered `from`=FromNumber, paged, vs eShop rows).

A send failure never fails the order op (best-effort, recorded). No number in logs.

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal C# identifier;
> named arguments must use them exactly (the cancellation-token parameter is named `ct`, so `ct:`).
> ⚠ Every SDK type is written **fully-qualified** with the namespace its source path implies (taken from the
> path the map gives for THAT type, never from a neighbour's location).

Client/auth/server (source: `sdk-map.md` "Getting a client" / "Servers & auth"; `TwilioClientOptions.cs`;
`ServerOptions.cs`; `Servers/DefaultOptions.cs`; `Servers/Default4Options.cs`;
`Core/Authentication/Basic/BasicAuthCredentials.cs`):

- Client: `Twilio.TwilioClient(HttpClient httpClient, Twilio.TwilioClientOptions options)` — sole ctor.
  API groups are properties: `client.Api20100401Message`, `client.LookupsV2PhoneNumber`.
- Auth: `options.AccountSidAuthToken = new Twilio.Core.Authentication.Basic.BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }` (both `required`, `init`).
- Environment: `options.Environment = Twilio.Servers.ServerEnvironment.Production` (only environment).
- Messaging server group = `Default` (default; `https://api.twilio.com`). Override:
  `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (settable `string`, default `https://api.twilio.com`).
  Lookups group = `Default4` (`https://lookups.twilio.com`) — **not** overridden by `Twilio:BaseUrl`.
- Retry: `options.Retry = Twilio.Core.Configuration.RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }` (all members `required`; per-attempt).
- DI: `services.AddTwilioClient(Action<TwilioClientOptions>)` (source `ServiceCollectionExtensions.cs`) —
  builds options **once at registration**, captures in singleton; fills `Logging.LoggerFactory` from DI
  `ILoggerFactory` when null (so logging is on at Information; env-var body-logging path never fires).

Operations (accessor `client.Api20100401Message`, source `Api/Api20100401Message.cs`; all **Case B**
`SdkException<Twilio.Core.ErrorResponse.RawError>`; no `…Result` no-throw variant exists; no pagination helper):

| Op | Signature (verbatim, key params) | Returns / reads |
| --- | --- | --- |
| CreateMessage | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 middle params nullable/no-default → **pass explicitly (null to skip)**; call with **named args**. | `Twilio.Models.ApiV2010AccountMessage` — read `Sid`, `Status`, `From`, `To`, `Body`, `ErrorCode`, `ErrorMessage`, `DateSent`, `DateCreated` |
| FetchMessage | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` (read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `From`, `Body`) |
| ListMessage | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — named args. Wire: `From←from`, `DateSent<←dateSentQuery` (upper/to), `DateSent>←dateSentQueryQuery` (lower/from), `PageSize←pageSize`, `PageToken←pageToken`. | `Twilio.Models.ListMessageResponse` — read `Messages` (`IReadOnlyList<ApiV2010AccountMessage>?`), `NextPageUri` (extract `PageToken` to page; **cap pages**) |
| UpdateMessage | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — remarks: "used to redact Message body text and to cancel not-yet-sent messages". Redact → `body: ""`, `status: null`. Cancel → `body: null`, `status: MessageEnumUpdateStatus.Canceled`. | `ApiV2010AccountMessage` |
| DeleteMessage | `DeleteMessage(string accountSid, string sid, …)` — not used (removes the record entirely; disposal must keep the fact). | `void` |

Lookups (accessor `client.LookupsV2PhoneNumber`, source `Api/LookupsV2PhoneNumber.cs`, **Case B**, server group `Default4`):

| Op | Signature | Returns / reads |
| --- | --- | --- |
| FetchPhoneNumber3 | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — named args, pass null for the 15 optionals. | `Twilio.Models.LookupResponse` — read `Valid` (`bool?`), `PhoneNumber` (`string?`, canonical E.164). Source `Models/LookupResponse.cs` |

Enums (source `Models/Enums/…`; `StringEnum<T>`, use static members, not `new`):
- `Twilio.Models.Enums.MessageEnumScheduleType.Fixed` (`"fixed"`) — doc: "For Messaging Services only … in conjunction with send_time to schedule a Message" → scheduling **requires** `messagingServiceSid`.
- `Twilio.Models.Enums.MessageEnumUpdateStatus.Canceled` (`"canceled"`).
- `Twilio.Models.Enums.MessageEnumStatus`: Queued, Sending, Sent, Failed, Delivered, Undelivered, Accepted, Scheduled, Canceled, … (wire lowercase) — map to a stored status string via its `.Value`.

`accountSid` on every Api20100401Message call = `Twilio:AccountSid`.

## 3. Trap notes

- CreateMessage/Fetch/List/Update are **POST/GET**; the request **body** carries `To`, `Body` (the shopper's
  number + message text) — sensitive. Hazard: what the built-in logger writes and how the log env var can
  arm body logging. **MUST load twilio-platforms-team:dotnet-configuration-resilience** (done).
- 24-param CreateMessage + list filters: positional binding mis-binds silently. Hazard: which params have no
  C# default and how named args avoid it. **MUST load twilio-platforms-team:dotnet-calling-endpoints**.
- `MessageEnumStatus`/`ScheduleType`/`UpdateStatus` are `StringEnum<T>`, not C# enums; response carries
  `AdditionalProperties`. Hazard: enum construction/compare + reading wire value. **MUST load twilio-platforms-team:dotnet-models**.
- All ops Case B (`SdkException<RawError>`). Hazard: a drifted 2xx body → `JsonException` not `SdkException`;
  the two directions `JsonException` reaches the boundary. **MUST load twilio-platforms-team:dotnet-error-handling**.
- Client build/DI + HttpClient lifetime. Hazard: singleton vs transient, `IHttpClientFactory` ownership.
  **MUST load twilio-platforms-team:dotnet-client-initialization**.
- Basic auth wiring. Hazard: where credentials are set relative to client construction. **MUST load twilio-platforms-team:dotnet-authentication**.
- Testing seam. Hazard: which seam to fake. **MUST load twilio-platforms-team:dotnet-testing**.

## 4. REQUIRED READING (load all before implementation; contents deliberately not copied here)

- twilio-platforms-team:dotnet-client-initialization · step 1 (client + DI)
- twilio-platforms-team:dotnet-authentication · step 1 (Basic auth)
- twilio-platforms-team:dotnet-calling-endpoints · steps 2–4 (every SDK call)
- twilio-platforms-team:dotnet-models · steps 2–4 (enums, DTO mapping)
- twilio-platforms-team:dotnet-error-handling · all steps (error boundary) — **always required**
- twilio-platforms-team:dotnet-configuration-resilience · step 1 (retry/timeout/base-url/pagination/logging) — loaded
- twilio-platforms-team:dotnet-testing · tests

Two hazard rows that always hold: (a) a malformed/drifted **2xx** body (missing `required` member) surfaces as
`System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch
lets it escape; (b) a **non-2xx** body not matching its operation's error shape throws `JsonException` while
the error object is constructed, **replacing** the `SdkException` and destroying the status. The boundary
catches `JsonException` too.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `TwilioSettings` bound from `Twilio:`; a startup validator throws (host won't start) if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is null/blank — **each part checked** (a blank part ≠ missing). `BaseUrl` optional. Source: `YOUR CALL — not in the map`. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (`Twilio:*`), never in repo. `AddTwilioClient` builds options **once at registration**, captured in the singleton → rotation needs a process restart (acceptable for this task; documented). Source: `ServiceCollectionExtensions.cs`. |
| 3 | Total timeout budget | `Retry.Timeout` = 15s **per attempt**; a per-request `CancellationTokenSource` (linked to `RequestAborted`) with a 30s budget wraps each handler's SDK calls (send-then-schedule = 2 calls). Source: `YOUR CALL` + config-resilience. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → CreateMessage/UpdateMessage/DeleteMessage (POST/DELETE) **never resent by SDK**; keeps sends at one. No verb added. Source: config-resilience. |
| 5 | Idempotency & ambiguous writes | CreateMessage exposes **no** caller idempotency parameter (injected `Idempotency-Key` header is not one). Resend requirement is met with an **application-level** idempotency store keyed by the caller-supplied key (return prior `notificationId`, no second send). Place/dispatch/cancel: not SDK-idempotent; reconciliation endpoint is the recovery path for an ambiguous send. Source: map row + `YOUR CALL`. |
| 6 | Observability | App logs at Info (op + orderId + notificationId + provider SID + status); provider `ErrorCode`/`ErrorMessage` logged on failed sends. **Never** log `To`/phone/`Body`/auth token. The SDK's built-in HTTP logger is **disabled** (see row 7) — observability comes from the app's own structured logs. Source: `YOUR CALL` + config-resilience. |
| 7 | Sensitive data | Request bodies carry `To` (shopper number) + `Body`. **Verified on the wire:** the SDK's built-in request-line logger writes the request URL and the URL **path is not redacted**, so a Lookups call (`/v2/PhoneNumbers/{number}`) would log the shopper's number. Therefore `options.Logging.LoggerFactory` is set explicitly to `NullLoggerFactory.Instance` (SDK HTTP logging off) with `LogRequestBody = false` — which also disarms the `TWILIOCLIENT_LOG` env-var body path. Stored numbers/bodies are returned only to the owner/operator, never logged. Source: config-resilience + `LookupResponse.cs`/`ApiV2010AccountMessage.cs` + wire verification. |
| 8 | Environment selection | One environment: `Production`. Messaging via `Default` (`api.twilio.com`), overridable by `Twilio:BaseUrl`; Lookups via `Default4` (`lookups.twilio.com`), not overridable. Live account — test traffic limited to the two configured destinations; volume kept minimal. No sandbox environment in the SDK. Source: `sdk-map.md` Servers & auth. |

## 6. Assumptions & Blockers

- **No Blockers.** Every capability maps to an operation above.
- Assumptions (minor, decided): (a) `Order` requires a `ShipToAddress`; `POST /api/orders` carries only items,
  so a default placeholder address is supplied. (b) Follow-up `sendAt` = now + 3 days (within Twilio's 7-day
  scheduling window). (c) "usable destination" = Lookups `valid == true`; the reserved US number is `valid`
  (registers) but the carrier refuses at send → status `undelivered`/`failed` (expected outcome, not a gap).
  (d) GET my-orders / GET notifications refresh status from the provider (no inbound webhook URL exists).
  (e) Verification driven with the admin user (owns the orders it places **and** holds the operator role),
  keeping the flow end-to-end through PublicApi alone.

## 7. Source labels — every contract row above cites its map page or declaring file, or is marked
`YOUR CALL — not in the map` for application decisions. No `UNVERIFIED` rows (all facts settled by source).
