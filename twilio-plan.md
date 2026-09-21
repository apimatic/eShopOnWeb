# twilio-plan.md — Order SMS notifications (eShopOnWeb + Twilio)

Additive SMS notification capability on `src/PublicApi`, backed by the vendored APIMatic-generated
Twilio .NET SDK (root namespace `TwilioSdk`, client `TwilioSdkClient`). Every Twilio contract fact
below comes from the SDK map / SDK source read this session; nothing from memory.

## 1. Scope & sequence

1. **Vendor the SDK & wire config/DI.** The SDK is not on NuGet; copy its source into `src/TwilioSdk`
   (own csproj, `ManagePackageVersionsCentrally=false` so it keeps its pinned versions), `ProjectReference`
   from `Infrastructure`. Bind `Twilio:` settings, fail-fast on missing parts, register a long-lived
   `TwilioSdkClient` behind an `ISmsProvider` abstraction.
2. **Domain.** New aggregates `ContactNumber` and `Notification`; add `OrderStatus` to `Order`
   (Placed→Dispatched / Placed|Dispatched→Canceled). EF configs + DbSets. Specs for owner/order scoping.
3. **Provider layer.** `ISmsProvider` (ApplicationCore, provider-agnostic DTOs) → `TwilioSmsProvider`
   (Infrastructure) using ops: `LookupsV2PhoneNumber.FetchPhoneNumber3` (validate+canonicalize),
   `Api20100401Message.CreateMessage` (send immediate; send scheduled follow-up),
   `.UpdateMessage` (cancel scheduled / redact body), `.FetchMessage` (refresh status / re-read),
   `.ListMessage` (reconciliation, filtered by `From`).
4. **Orchestration.** `OrderNotificationService` (ApplicationCore): placed/dispatched/canceled notify,
   resend (idempotent), content disposal, reconciliation. Send failures never fail the operation.
5. **Endpoints.** 11 endpoints on PublicApi (IEndpoint pattern), shopper- vs admin-scoped.
6. **Secrets, build, live self-verify**, then write a verification guide.

No capability required here is missing from the map — see §6 (no blockers).

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal C#
> identifier; in named arguments use exactly these (the cancellation-token parameter is `ct`, so `ct:`).
> ⚠ Every SDK type is written fully-qualified with the namespace its **source path** implies
> (`Models/` → `TwilioSdk.Models`, `Models/Enums/` → `TwilioSdk.Models.Enums`, `Api/` → `TwilioSdk.Api`,
> root → `TwilioSdk`), taken from the path the map gives for **that** type.

### Operations

| op | controller · signature | request fields used | response fields read | error | source |
| --- | --- | --- | --- | --- | --- |
| Validate & canonicalize number | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — **Server group `Default4`** (lookups host; NOT governed by `Twilio:BaseUrl`) | `phoneNumber` = raw input (E.164-ish); all other params `null` | `TwilioSdk.Models.LookupResponse`: `Valid: bool?` (usable destination), `PhoneNumber: string?` (provider canonical **E.164** — the value to store), `ValidationErrors: IReadOnlyList<ValidationError>?` (reason on invalid) | `SdkException<RawError>` — Case B | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| Send message (immediate) | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — Server group `Default` (api host; **governed by `Twilio:BaseUrl`**) | `accountSid`=`Twilio:AccountSid`; `to`=canonical E.164; `from`=`Twilio:FromNumber`; `body`=text; **all other optionals `null`** (omit → provider default) | `TwilioSdk.Models.ApiV2010AccountMessage`: `Sid: string?` (provider id), `Status: MessageEnumStatus?`, `ErrorCode: int?`, `ErrorMessage: string?`, `DateSent: string?` (RFC2822), `To/From/Body: string?` | `SdkException<RawError>` — Case B | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| Send follow-up (scheduled) | same `CreateMessage` | `accountSid`; `to`; **`scheduleType`=`MessageEnumScheduleType.Fixed`**; **`sendAt`**=DateTimeOffset (a few days out); **`messagingServiceSid`=`Twilio:MessagingServiceSid`** (scheduling is *Messaging-Service only* — see enum doc); `body`; `from`=**null** (mutually exclusive with messagingServiceSid); other optionals null | as above; captured `Sid`, `Status` (expect `scheduled`) | Case B | `Models/Enums/MessageEnumScheduleType.cs` (remarks: "For Messaging Services only … value `fixed` in conjunction with the send_time parameter") |
| Cancel scheduled follow-up | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`; `sid`=follow-up Sid; `body`=**null**; `status`=**`MessageEnumUpdateStatus.Canceled`** | `ApiV2010AccountMessage.Status` (expect `canceled`) | Case B | `Api/Api20100401Message.cs` remarks: "Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)" |
| Dispose (redact) content | same `UpdateMessage` | `accountSid`; `sid`; `body`=**`""` (empty string)** to redact; `status`=**null** | `ApiV2010AccountMessage` (record survives; `Body` cleared provider-side) | Case B | same remarks (redact body text) |
| Refresh / re-read one | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`; `sid` | `ApiV2010AccountMessage`: `Status`, `DateSent`, `ErrorCode`, `ErrorMessage` | Case B | `Api/Api20100401Message.cs` |
| Reconcile (list by sender) | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`; `to`=null; **`from`=`Twilio:FromNumber`** (ask provider for *this number's* messages — not filtered after); `dateSent`=null; **`dateSentQuery`** → wire `DateSent<` = **upper bound (`to`)**; **`dateSentQueryQuery`** → wire `DateSent>` = **lower bound (`from`)**; `pageSize`=1000; `page`=null; `pageToken`=null (then follow paging — see below) | `TwilioSdk.Models.ListMessageResponse`: `Messages: IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri: string?` (loop until null → **whole range, no cap**) | Case B | `map/operations/Api20100401Message.md`; `Models/ListMessageResponse.cs` |

Paging for reconciliation: no `Pagination` bullet on the row ⇒ no SDK auto-pager. `ListMessageResponse` has no
`page_token` field; the next token is embedded in `NextPageUri`'s query string (`PageToken=…`, `Page=…`). Loop:
call `ListMessage`, take `Messages`, if `NextPageUri != null` parse its `PageToken`/`Page` and pass them in
next call; stop when `NextPageUri == null`. Covers the whole range with no truncation.

### Enums (values needed) — `TwilioSdk.Models.Enums`

| enum | member → wire | source |
| --- | --- | --- |
| `MessageEnumScheduleType` | `.Fixed` → `fixed` | `Models/Enums/MessageEnumScheduleType.cs` |
| `MessageEnumUpdateStatus` | `.Canceled` → `canceled` (only member) | `Models/Enums/MessageEnumUpdateStatus.cs` |
| `MessageEnumStatus` (read-only, on response) | `queued/sending/sent/failed/delivered/undelivered/receiving/received/accepted/scheduled/read/partially_delivered/canceled` — read `.Value`; non-delivery = {`failed`,`undelivered`,`canceled`}; delivered = {`delivered`,`sent`,`received`,`read`} | `Models/Enums/MessageEnumStatus.cs` |

`StringEnum<T>`: build with `Type.Member` or `Type.FromValue("wire")`; read the wire via `.Value`. (Not a C# enum.)

### Client construction / auth / servers

- `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` (only ctor). Root ns `TwilioSdk`.
- Auth: `options.AccountSidAuthToken = new BasicAuthCredentials { Username = Twilio:AccountSid, Password = Twilio:AuthToken }` (ns `TwilioSdk`, per map "Getting a client"). Basic auth; a missing credential is *silently skipped* → looks like 401, hence fail-fast (§5 row 1).
- `options.Environment = ServerEnvironment.Production` (ns `TwilioSdk.Servers`).
- **`Twilio:BaseUrl` override** applies to the **messaging (Messages) host = `Default` group** only: set
  `options.Server.Default.Production.BaseUrl = Twilio:BaseUrl`. Do **not** touch `Default4` (lookups) — the
  task says BaseUrl governs the messaging API alone. ⚠ verify exact `ServerOptions` member path from
  `ServerOptions.cs`/`Servers/` at implementation time (map documents the point as `options.Server.Default.Production.BaseUrl`).
- `accountSid` **path param** on every Message op = `Twilio:AccountSid` (same account as the auth username).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A `catalogItemId` in `POST /api/orders` must be one the catalog actually has | order create ← existing `CatalogItem` repository (`CatalogItemsSpecification`) | implementation (400 on unknown id) |
| A phone number stored/messaged must be one the provider accepts as a usable destination, in the provider's canonical form | contact-number create ← `LookupsV2PhoneNumber.FetchPhoneNumber3` (`Valid==true`, store `PhoneNumber`) | implementation (400 on `Valid!=true`) |
| Cancel of a follow-up acts only on the Sid a prior scheduled `CreateMessage` returned | `UpdateMessage(status=Canceled)` ← scheduled `CreateMessage` | implementation (stored follow-up Notification Sid) |
| Resend/content/reconciliation act on a notification/message this app recorded | `UpdateMessage`/`FetchMessage` ← recorded Notification `ProviderMessageSid` | implementation |

## 3. Trap notes (name the hazard; the skill resolves it)

- **Client & HttpClient lifetime** — the SDK ctor takes an `HttpClient`; getting its lifetime/handler-pooling
  wrong leaks sockets or pins stale DNS. Consequence: the DI shape (long-lived handler vs transient wrapper)
  is not visible in the ctor. `MUST load twilio-platforms-team:dotnet-client-initialization`.
- **Auth wiring** — a credential never set is *skipped, not thrown*, so a config typo surfaces as a runtime
  401 not a startup error. `MUST load twilio-platforms-team:dotnet-authentication`.
- **Optional params bind by position** — `CreateMessage`/`ListMessage` have many nullable no-default params;
  a positional call mis-binds silently. `MUST load twilio-platforms-team:dotnet-calling-endpoints`.
- **Models: enums & unknown fields** — `StringEnum<T>` is not a C# enum; response carries `AdditionalProperties`.
  `MUST load twilio-platforms-team:dotnet-models`.
- **Error boundary** — every op here is Case B (`SdkException<RawError>`); a drifted 2xx body throws
  `JsonException` (not `SdkException`), and a non-2xx body that doesn't match destroys the status.
  `MUST load twilio-platforms-team:dotnet-error-handling`.
- **Timeout / retry / logging** — `Timeout` is per-attempt not total; `LogRequestBody` logs the SMS body+To
  unredacted and `TWILIOCLIENT_LOG` can arm it from outside code. `MUST load twilio-platforms-team:dotnet-configuration-resilience`.
- **Test seam** — the `HttpClient` ctor arg is the fake seam for provider tests.
  `MUST load twilio-platforms-team:dotnet-testing`.

## 4. REQUIRED READING (load all before implementing; contents deliberately not copied here)

- `twilio-platforms-team:dotnet-client-initialization` · client + DI registration (step 1)
- `twilio-platforms-team:dotnet-authentication` · Basic-auth credentials wiring (step 1)
- `twilio-platforms-team:dotnet-calling-endpoints` · first calls to Lookup/Message ops (step 3)
- `twilio-platforms-team:dotnet-models` · enums, request/response models (step 3)
- `twilio-platforms-team:dotnet-error-handling` · error boundary in TwilioSmsProvider (step 3) — always required
- `twilio-platforms-team:dotnet-configuration-resilience` · timeout budget, retry, logging/redaction (steps 1&3)
- `twilio-platforms-team:dotnet-testing` · faking the HttpClient seam (tests)

**Hazard rows (verbatim — `JsonException` reaches the boundary from two directions, opposite handling):**
- A drifted/malformed **2xx** body (missing `required` member) surfaces as a `System.Text.Json.JsonException`
  from deserialization, **not** an `SdkException`; an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that doesn't match its operation's generated error shape throws `JsonException`
  *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is lost.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AddOptions<TwilioSettings>().Bind(config.GetSection("Twilio")).Validate(all of AccountSid/AuthToken/FromNumber/MessagingServiceSid non-null **and non-blank**).ValidateOnStart()` in Infrastructure DI. Blank part ≠ missing → each checked with `IsNullOrWhiteSpace`. Host refuses to start otherwise (not a first-call 401). |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (PublicApi `UserSecretsId`) loaded from env by me; never in repo. Options bound once at registration and captured in the singleton client → rotation needs a process restart (accepted; documented). |
| 3 | Total timeout budget | Caller-facing budget enforced by a **linked `CancellationTokenSource` deadline** (default 30s total) in `TwilioSmsProvider`, since SDK `Timeout` is *per attempt*. That CTS token is the `ct:` passed to every op. `MUST load dotnet-configuration-resilience`. |
| 4 | Write-retry ownership | Send/redact/cancel/dispose are `POST`/`DELETE` → **never resent** by SDK default (`HttpMethodsToRetry`=GET,HEAD,PUT,OPTIONS) — no duplicate SMS. Lookup/Fetch/List are `GET`/idempotent → safe to retry. Left at default. |
| 5 | Idempotency & ambiguous writes | `CreateMessage`/`UpdateMessage` expose **no real caller key** (the injected `Idempotency-Key: Guid` is not one). **Resend** carries a *caller-supplied* key → persisted on `Notification.IdempotencyKey` with a **unique index**; repeat key returns the existing row (no 2nd send). Ambiguous send failures → reconciliation (row 13). |
| 6 | Observability | App logs (Info) carry operation, orderId, notificationId, provider Sid, provider status/HTTP status only — **never** the phone number or body (`OrderNotificationService`, `TwilioSmsProvider.Translate`). **Shipped correction:** the SDK's own built-in request-line logger writes the request URL, and Lookups puts the phone number in the URL *path* (which the SDK does not redact), so it is disabled (`NullLoggerFactory`) rather than pointed at the host factory. Verified: grepping the running app log for the numbers' last-4 returns 0 hits. |
| 7 | Sensitive data | Request models carry **To (phone)** and **Body (message text)** = sensitive; the Lookups **path** carries the number too. So `LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is set explicitly to `NullLoggerFactory.Instance` (`TwilioServiceCollectionExtensions`), which both suppresses the URL-logging leak and stops `TWILIOCLIENT_LOG` arming logging from outside. App logs never echo number/body. |
| 8 | Environment selection | One environment (`Production`). Messages/Lookups groups touched: `Default` (api.twilio.com) + `Default4` (lookups.twilio.com). `Twilio:BaseUrl` overrides only `Default` (messaging). No SDK sandbox env → test code fakes the HttpClient seam; live traffic only in explicit verification, to the two allowed numbers. |
| 9 | Duplicate prevention under concurrency | Store = **Notifications** table, column = **`IdempotencyKey`**, **filtered unique index** (`NotificationConfiguration`: `HasIndex(n => n.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL")`). `OrderNotificationService.ResendAsync` first checks for an existing row by key (sequential repeat), and wraps the insert so a concurrent 2nd insert that the unique constraint rejects is **caught and re-read by key**, returning the winner. (ApplicationCore is EF-free, so the catch is on the persistence exception generally rather than `DbUpdateException` specifically, then confirmed by the re-read.) SQL Server enforces the index; the dev in-memory provider does not enforce indexes — an environment caveat, not the mechanism. |
| 10 | Partial results | Reconciliation **fully paginates** (`NextPageUri` loop until null) → no cap, whole range. If a cap were ever introduced it would surface via a response flag; none is. |
| 11 | Startup validation vs test host | Host-booting test project = **`tests/PublicApiIntegrationTests`** (`WebApplicationFactory<Program>`). It gets **placeholder Twilio config** in its `appsettings.test.json` (fake non-blank values) so `ValidateOnStart()` passes; it never calls Twilio live. Will run `dotnet test` on it and confirm green. |
| 12 | Ordering & no-op side effects | Notification row written (status `Pending`) **before** the provider call, updated with Sid/status **after**. Dispatch/Cancel notifications are **gated on an actual `OrderStatus` change** (idempotent transition → no duplicate SMS). Provider response is never the first local write. |
| 13 | Unknown outcomes | `CreateMessage` has no caller key; on transport failure after possible receipt, the Notification is recorded `status=Unknown` (no Sid) and the operation still succeeds. Re-read path = **`ListMessage` by `From`=`Twilio:FromNumber`** over the date range (the reconciliation report) — surfaces a provider message eShop lacks a Sid for. |
| 14 | Provider status & reconciliation clock | Store provider `Status` verbatim (never defaulted); `GET` notification/my-orders endpoints refresh via `FetchMessage`. Resend branches on non-delivery statuses; content/cancel branch on state. Reconciliation: **both sides filter on the provider `date_sent`** — provider via `DateSent<`/`DateSent>`, local via stored `ProviderDateSent` (captured from send/fetch). Not a local row-creation column. |

## 6. Assumptions & Blockers

- **No blockers.** Every required capability maps to an operation above.
- Assumptions (minor; decided, proceeding): (a) caller identity = JWT `ClaimTypes.Name` (username/email),
  reused as `Order.BuyerId`/`OwnerId`; (b) a shopper may register multiple numbers and each order event
  messages **all** currently-registered numbers of the owner (one Notification per number); (c) resend
  idempotency key carried via the `Idempotency-Key` request header; (d) `OrderStatus` is added to the
  existing `Order` (additive, reuses the order model — not a parallel one); (e) "usable destination" =
  Lookup `Valid==true`.
