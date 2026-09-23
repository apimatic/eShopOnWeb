# twilio-plan.md — Order SMS notifications for eShopOnWeb (Twilio)

Integration of Twilio SMS into `src/PublicApi`, additive to the existing catalog/basket/order flow.
Contract facts below come from the SDK map (`sdk-map.md` + `map/operations/*`) and the map-named
source files, read this session. Root namespace is **`TwilioSdk`** (the map's identity table; the plugin
name "Twilio" is not the C# namespace).

---

## 1. Scope & sequence

Layering: entities + service interfaces in **ApplicationCore**; Twilio client wrapper + service impls +
EF config + DI in **Infrastructure**; HTTP endpoints in **PublicApi** (MinimalApi.Endpoint `IEndpoint`
convention, JWT auth, `[Authorize(Roles=…)]` for operator routes). The APIMatic SDK is not on NuGet, so
it is **vendored** into the repo at `src/TwilioSdk/` (source copied from the session clone; clone path is
never referenced) and referenced by `Infrastructure.csproj`.

Steps:

1. **Vendor SDK** → `src/TwilioSdk/` (opt out of central package mgmt), add ProjectReference from Infrastructure, add to solution.
2. **Config + client + fail-fast**: `TwilioSettings` bound from `Twilio:` section; `AddTwilioSdkClient` DI; startup validation. Uses **CreateMessage/FetchMessage/ListMessage/UpdateMessage** (group `Default`) + **FetchPhoneNumber3** (group `Default4`).
3. **Domain**: entities `ContactNumber`, `OrderNotification`, `NotificationIdempotencyRecord`; add `OrderStatus` to `Order`; EF configs; register `DbSet`s on `CatalogContext`.
4. **Provider gateway** (`ITwilioMessagingGateway` in Infrastructure): thin wrapper over the 5 operations, translating SDK exceptions to a domain result and never leaking the auth token / destination number to logs.
5. **App services**: `IPhoneRegistrationService` (Flow 1), `IOrderNotificationService` (Flows 2 & 3).
6. **Endpoints** (PublicApi): contact-numbers ×3, orders ×3 + my-orders + order-notifications, notifications resend/content/reconciliation ×3.
7. **Build, unit tests, live end-to-end verification.**

No capability in scope is missing from the map (see §6). Follow-up scheduling uses CreateMessage's
`scheduleType`/`sendAt`/`messagingServiceSid`; cancellation and content-redaction use UpdateMessage;
reconciliation uses ListMessage's `from` + `DateSent</>` filters — all provider-native.

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C# identifier; in
> named arguments use exactly those names (the cancellation-token parameter is literally `ct`, so write `ct:`).
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**, taken from the
> path the map gives for THAT type (`Models/` → `TwilioSdk.Models`, `Models/Enums/` → `TwilioSdk.Models.Enums`,
> `Api/` → `TwilioSdk.Api`, root → `TwilioSdk`, `Core/Authentication/Basic/` → `TwilioSdk.Core.Authentication.Basic`,
> `Servers/` → `TwilioSdk.Servers`), never from where a neighbouring type sits.

Client construction / auth / servers (source: `TwilioSdkClient.cs`, `TwilioSdkClientOptions.cs`,
`ServiceCollectionExtensions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`,
`Core/Authentication/Basic/BasicAuthCredentials.cs`, sdk-map.md *Servers & auth*):

- `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure)` — registers a **singleton**
  `TwilioSdkClient` built over `IHttpClientFactory` (calls `services.AddHttpClient()`). Options captured **once**.
- `new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }` → `options.AccountSidAuthToken`.
  (Basic auth; account SID as username, auth token as password per *Servers & auth*.)
- `options.Environment = TwilioSdk.Servers.ServerEnvironment.Production` (only environment; the default).
- **Base-URL override for the messaging API** = `options.Server.Default.Production.BaseUrl` (default
  `https://api.twilio.com`). The messaging operations (CreateMessage/Fetch/List/Update) resolve through
  group **`Default`** (`_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages…")`), so `Twilio:BaseUrl`,
  when set, is assigned there **verbatim**. Lookups resolve through **`Default4`** (`lookups.twilio.com`) and
  are deliberately **not** governed by `Twilio:BaseUrl` (task: BaseUrl governs only the messaging API).
- `client.Api20100401Message` (source `Api/Api20100401Message.cs`), `client.LookupsV2PhoneNumber`
  (source `Api/LookupsV2PhoneNumber.cs`).

| Operation | Signature (verbatim param order) | Request fields used (wire) | Response fields read | Error | Source |
| --- | --- | --- | --- | --- | --- |
| `FetchPhoneNumber3` (Lookups) | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `phoneNumber` (path, caller-typed); pass all 15 optionals `null` (`fields`=null → base validation package, purpose: returns `valid`+`phone_number`; identity/reassigned fields omitted → not needed) | `LookupResponse.Valid` (`bool?` — usable-destination gate; treat null/false as **reject**), `LookupResponse.PhoneNumber` (`string?` canonical E.164 — the value stored) | `SdkException<RawError>` — **Case B** (404/400 ⇒ number not a usable destination ⇒ reject) | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| `CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | **Immediate**: `to`=canonical E.164, `from`=`Twilio:FromNumber` (purpose: keeps message in reconciliation's From-filtered view; omit `messagingServiceSid`), `body`=text, `scheduleType`/`sendAt`=null. **Scheduled follow-up**: `messagingServiceSid`=`Twilio:MessagingServiceSid` (purpose: scheduling is Messaging-Service-only per `MessageEnumScheduleType` doc), `scheduleType`=`MessageEnumScheduleType.Fixed`, `sendAt`=now+3d, `from`=null, `body`=text. All other 20 optionals `null` (omit → provider default). | `ApiV2010AccountMessage.Sid` (provider id to persist), `.Status` (`MessageEnumStatus?` — initial outcome), `.ErrorCode`,`.ErrorMessage`,`.DateSent`,`.To`,`.From` | `SdkException<RawError>` — **Case B** | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs`; `Models/ApiV2010AccountMessage.cs` |
| `FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `sid`=persisted provider id | `.Status`,`.ErrorCode`,`.ErrorMessage`,`.DateSent` (refresh stored outcome) | `SdkException<RawError>` — **Case B** | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| `ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `from`=`Twilio:FromNumber` (**ask provider for that number's msgs, wire `From`**), `dateSentQueryQuery`=range start (wire `DateSent>`, ≥), `dateSentQuery`=range end (wire `DateSent<`, ≤), `dateSent`=null, `pageSize`=1000, then follow pagination via `page`+`pageToken` parsed from `NextPageUri`. | `ListMessageResponse.Messages` (`IReadOnlyList<ApiV2010AccountMessage>?` — each `.Sid`,`.Status`,`.To`,`.DateSent`), `.NextPageUri` (null ⇒ last page) | `SdkException<RawError>` — **Case B** | `map/operations/Api20100401Message.md`; `Models/ListMessageResponse.cs` |
| `UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions=null, CancellationToken ct=default)` | **Cancel follow-up**: `status`=`MessageEnumUpdateStatus.Canceled`, `body`=null. **Redact content**: `body`=`""` (empty string ⇒ provider redacts body per method `<remarks>`: "used to redact Message body text"), `status`=null. | `.Status`,`.Body` (post-redaction) | `SdkException<RawError>` — **Case B** | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs` (UpdateMessage `<remarks>`) |

Enums (source `Models/Enums/…`):

| Enum | Members used | All members (for outcome sorting) |
| --- | --- | --- |
| `MessageEnumScheduleType` | `.Fixed` (wire `fixed`) | Fixed |
| `MessageEnumUpdateStatus` | `.Canceled` (wire `canceled`) | Canceled |
| `MessageEnumStatus` (read on `ApiV2010AccountMessage.Status`) | see OPERATION OUTCOMES | queued, sending, sent, failed, delivered, undelivered, receiving, received, accepted, scheduled, read, partially_delivered, canceled |

`MessageEnumStatus` is a `StringEnum<T>` (not a C# enum): compare via `== MessageEnumStatus.Delivered` etc.,
read wire text via `.Value`; unknown/absent status ⇒ treat as not-yet (see row 14).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A `to` sent to must be a canonical number this caller **registered and still owns** | `CreateMessage` ← `FetchPhoneNumber3`/`ContactNumber` store | implementation (`OrderNotificationService`: reads caller's `ContactNumber` rows; deleted numbers excluded) |
| A `sid` cancelled/redacted/fetched must be one **`CreateMessage` returned & we persisted** | `UpdateMessage`/`FetchMessage` ← `CreateMessage` | implementation (`OrderNotification.ProviderMessageSid`; null ⇒ skip provider call) |
| A reconciliation match is a `ListMessage` sid equal to a persisted `OrderNotification.ProviderMessageSid` | `ListMessage` ↔ `CreateMessage` | implementation (`ReconciliationService` set-compare by sid) |
| Resend destination + body come from the **existing notification** being resent | `CreateMessage` ← prior `OrderNotification` | implementation (resend copies `To`/`Body` from the source notification) |

---

## 3. Trap notes (hazard + skill pointer; not resolved here)

- **DI singleton captures options once** — a rotated `Twilio:AuthToken` will not take effect until restart; decide whether that is acceptable and where the client is built. **MUST load dotnet-client-initialization.**
- **HttpClient lifetime** — the client must reuse a factory-managed `HttpClient`, not a per-request one. **MUST load dotnet-client-initialization.**
- **Basic-auth two-part credential** — an applied-but-empty part fails silently as "no credential sent" → looks like a 401 later. **MUST load dotnet-authentication.**
- **Optional params with no C# default mis-bind positionally** — CreateMessage/ListMessage/FetchPhoneNumber3 have many; call with **named arguments**. **MUST load dotnet-calling-endpoints.**
- **`Idempotency-Key` header is generator-injected `Guid.NewGuid()` per call** — it is NOT a real idempotency key; resend idempotency is the app's job. **MUST load dotnet-calling-endpoints.**
- **`StringEnum<T>` is not a C# enum; response records keep unknown fields (`AdditionalProperties`)** — build/compare enums via static members, not casts. **MUST load dotnet-models.**
- **Two `JsonException` directions bypass an SDK-only catch ladder** (drifted 2xx body; non-2xx body not matching the error shape). **MUST load dotnet-error-handling.**
- **`Timeout` is per-attempt not total; `HttpMethodsToRetry` default excludes POST/DELETE** — a whole-call deadline must be a `CancellationToken`; POSTs are not auto-resent. **MUST load dotnet-configuration-resilience.**
- **`ListMessage` is not auto-paginated** (no Pagination bullet ⇒ single response); the caller must follow `NextPageUri`, and `LogRequestBody`/`TWILIOCLIENT_LOG` can echo bodies. **MUST load dotnet-configuration-resilience.**
- **Fake the `HttpClient` seam, not SDK internals**, for gateway tests. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load before implementation; contents deliberately not inlined here)

- **twilio-platforms-team:dotnet-client-initialization** — Step 2 client + DI registration.
- **twilio-platforms-team:dotnet-authentication** — Step 2 credential wiring / fail-fast.
- **twilio-platforms-team:dotnet-calling-endpoints** — Steps 4–6 every operation call.
- **twilio-platforms-team:dotnet-models** — Steps 4–6 request/response models, enums.
- **twilio-platforms-team:dotnet-error-handling** — Step 4 gateway error boundary (always required).
- **twilio-platforms-team:dotnet-configuration-resilience** — Step 2/4 retries, timeout, base-URL, pagination, logging.
- **twilio-platforms-team:dotnet-testing** — Step 7 tests.

These are usage skills; every contract fact still comes from §2 or a map lookup.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision | source |
| --- | --- | --- | --- |
| 1 | Credential fail-fast | `TwilioSettings` bound from `Twilio:` in Infrastructure DI; a startup validator throws if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is null/whitespace (each part checked — a blank part ≠ missing). `BaseUrl` optional. | YOUR CALL — not in the map |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (`Twilio:*`), loaded from env-var values; never in repo. `AddTwilioSdkClient` builds options once at registration ⇒ rotation needs a process restart (documented; acceptable for this app). | `ServiceCollectionExtensions.cs` |
| 3 | Total timeout budget | Each outbound provider call is bounded by a `CancellationToken` (linked to the request abort + a 30s ceiling) passed as `ct:` — the only whole-call bound, since SDK `Timeout` is per-attempt. Messaging failures never fail the order op (caught). | sdk-map.md RetryOptions; dotnet-configuration-resilience |
| 4 | Write-retry ownership | Messaging writes are `POST` (CreateMessage/UpdateMessage) and `DELETE` — **not** in default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) ⇒ SDK never auto-resends them. GET FetchMessage/ListMessage may retry (idempotent). We keep SDK defaults. | sdk-map.md RetryOptions |
| 5 | Idempotency & ambiguous writes | CreateMessage exposes **no** caller idempotency key (confirmed in signature). Resend idempotency enforced by app: `NotificationIdempotencyRecord` with a **unique index on `IdempotencyKey`**; repeated key ⇒ return prior `notificationId`, no second send. Non-resend sends reconcile via ListMessage. | `map/operations/Api20100401Message.md`; YOUR CALL — not in the map |
| 6 | Observability | Info logs on send/dispatch/cancel/resend/reconcile carry `orderId`/`notificationId`/provider `sid`/status only. Destination numbers and body are **never** logged. Provider error `StatusCode` + `ReadAsString()` are logged on failure. | dotnet-error-handling |
| 7 | Sensitive data | Message `body` and destination phone number are sensitive. `LogRequestBody` left **off**; `options.Logging.LoggerFactory` set explicitly (via DI) so `TWILIOCLIENT_LOG` cannot arm body logging from outside. Own logs never echo body/number. | `Models/ApiV2010AccountMessage.cs`; dotnet-configuration-resilience |
| 8 | Environment selection | Only `ServerEnvironment.Production` exists. Messaging ⇒ group `Default` (`Twilio:BaseUrl` override, default `api.twilio.com`); Lookups ⇒ group `Default4` (`lookups.twilio.com`, not overridden). No SDK sandbox: live account — test traffic limited to the two authorized destinations. | sdk-map.md *Servers & auth* |
| 9 | Duplicate prevention under concurrency | Store `NotificationIdempotencyRecord`, column `IdempotencyKey`, **unique index**; the second insert is rejected by the unique constraint and the code **catches `DbUpdateException`** and returns the first result. (SQL Server enforces this; in-memory dev provider does not enforce indexes — a documented environment limitation of the test host, not the design.) | YOUR CALL — not in the map |
| 10 | Partial results | Reconciliation pages until `NextPageUri` is null; a safety cap (`MaxPages`, high) sets a `Truncated` bool + `PagesRead` on the response body so the caller learns if it was cut short. | `Models/ListMessageResponse.cs` |
| 11 | Startup validation vs test host | `Microsoft.eShopWeb.FunctionalTests` boots the PublicApi host (`WebApplicationFactory<Program>`). The startup validator must not break it: validator reads `Twilio:*`; functional test config supplies placeholder `Twilio:*` values (added to `appsettings.test.json` with dummy non-blank strings — no secrets). Verified by running that project. | YOUR CALL — not in the map |
| 12 | Ordering & no-op side effects | The `OrderNotification` row is persisted (status `Pending`, no sid) **before** the CreateMessage call; the returned sid/status update the same row after. Dispatch/cancel are gated on an actual `Order.Status` transition — the notification + follow-up fire only when status truly changed. | YOUR CALL — not in the map |
| 13 | Unknown outcomes | If CreateMessage transport fails after the request may have been received, the pre-written `OrderNotification` row (status `Unknown`) stays; a later `GET …/notifications` re-reads via **`ListMessage`** by `from`+recent range (or FetchMessage once a sid is known) to discover a sid that landed. Order op still succeeds. | `map/operations/Api20100401Message.md` |
| 14 | Provider status & reconciliation clock | `ApiV2010AccountMessage.Status` (`MessageEnumStatus`). **Done**: delivered, sent, read. **Failed**: failed, undelivered, canceled. **Not-yet** (everything else incl. null/unknown): queued, sending, accepted, scheduled, receiving, received, partially_delivered. Resend offered only for Failed. Reconciliation filters both sides on **message sent-date** (`DateSent</>` on provider; `OrderNotification.ProviderDateSent` locally) — not a local row-creation column. | `Models/Enums/MessageEnumStatus.cs`; `Models/ApiV2010AccountMessage.cs` |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| resend a notification | `NotificationIdempotencyRecord.IdempotencyKey` (CatalogContext) | unique index on `IdempotencyKey` | `catch (DbUpdateException)` → return prior notificationId | TBD |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short | where in the code |
| --- | --- | --- | --- |
| reconciliation ListMessage | loop until `NextPageUri==null`; safety `MaxPages` | `Truncated` bool + `PagesRead` int on the reconciliation response body | TBD |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| dispatch order | `Order.Status` was not already `Dispatched`/`Cancelled` (transition returns whether it changed) | "on its way" SMS + scheduled follow-up queued | TBD |
| cancel order | `Order.Status` was not already `Cancelled` | "cancelled" SMS + cancel not-yet-sent follow-up | TBD |
| resend | idempotency key not seen before | the CreateMessage send + new notification row | TBD |
| content disposal | notification body not already redacted (`ContentRedacted` false) | provider redact call | TBD |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| CreateMessage (send) | `ListMessage` (by `from`+recent range) / `FetchMessage` once sid known | `OrderNotification.Id` (local) → `ProviderMessageSid`; provider matched by `to`+`DateSent` window | TBD |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| CreateMessage / FetchMessage / ListMessage entry | `ApiV2010AccountMessage.Status` (`MessageEnumStatus?`) | **Done**→ record Delivered/Sent/Read (delivered, sent, read); **Failed**→ record Failed + allow resend (failed, undelivered, canceled); **Not-yet**→ record Pending, no resend (queued, sending, accepted, scheduled, receiving, received, partially_delivered, **and null/unknown**) | TBD |
| UpdateMessage (cancel) | response `.Status` | expect canceled; if not canceled → log, still mark follow-up cancelled locally (best-effort) | TBD |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| send order-notification | `OrderNotification` row (OrderId, OwnerId, Kind, To, Body, Status=Pending, no sid) | `ProviderMessageSid`, `Status`, `ProviderDateSent`, error fields | TBD |
| resend | `OrderNotification` (Kind=Resend) + `NotificationIdempotencyRecord` claim, both before send | resulting sid/status on the new notification | TBD |

---

## 6. Assumptions & Blockers

**Blockers:** none — every required capability maps to an operation (validate=FetchPhoneNumber3, send/schedule=CreateMessage, status=FetchMessage/ListMessage, cancel & redact=UpdateMessage, reconcile=ListMessage).

**Assumptions (minor, decided and proceeding):**
- Caller identity = the JWT `ClaimTypes.Name` claim (username/email), matching `Order.BuyerId` and used as `ContactNumber.OwnerId`/`OrderNotification.OwnerId`.
- `POST /api/orders` builds `Order` directly from catalog items (BuyerId=caller); shipping address optional in the request, defaulted to a placeholder (address is outside this feature's scope but `Order` requires one).
- `Order.Status` (enum Placed/Dispatched/Cancelled) is added additively to the existing `Order` aggregate; default `Placed`.
- Follow-up "a few days later" = **+3 days** (within Twilio's scheduling window; leaves ample time to cancel during verification).
- Content disposal uses **redaction** (UpdateMessage `body=""`), not DeleteMessage — DeleteMessage would erase the record that a message was sent, violating "the fact a message was sent survives".
- `GET /api/orders/{orderId}/notifications` and `GET /api/my-orders` are shopper-scoped (caller's own orders); dispatch/cancel/resend/content/reconciliation are administrator-only.
