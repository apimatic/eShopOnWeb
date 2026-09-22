# Twilio SMS order-notifications — integration plan (`src/PublicApi`)

Adds shopper mobile numbers, order-lifecycle SMS, and operator tooling to eShopOnWeb, using the
APIMatic-generated **Twilio .NET SDK** (root namespace `TwilioSdk`) for every Twilio interaction.
Additive only — existing catalog/basket/order flow untouched.

---

## 1. Scope & sequence

| # | Step | Twilio operations used |
| --- | --- | --- |
| 1 | Domain (ApplicationCore): `ContactNumber`, `SmsNotification`, `ResendClaim` entities + `OrderStatus` on `Order`; specs; `ISmsNotificationService` interface | — |
| 2 | Infra: `TwilioSettings` options (bind `Twilio:` + fail-fast), `TwilioMessagingClient` (thin SDK wrapper), EF configs, DI in `AddTwilioMessaging` | client construction |
| 3 | Register a contact number | `LookupsV2PhoneNumber.FetchPhoneNumber3` (validate + canonicalize) |
| 4 | Place order → "placed" SMS | `Api20100401Message.CreateMessage` (from = `Twilio:FromNumber`) |
| 5 | Dispatch → "on its way" SMS + schedule follow-up a few days out | `CreateMessage` (immediate, from=FromNumber) + `CreateMessage` (scheduleType=`fixed`, sendAt=+3d, messagingServiceSid) |
| 6 | Cancel → "cancelled" SMS + call off queued follow-up | `CreateMessage` (from=FromNumber) + `UpdateMessage` (status=`canceled`) |
| 7 | GET my-orders / order notifications; refresh each notification's provider state | `FetchMessage` |
| 8 | Resend (idempotency-key gated) | `CreateMessage` (from=FromNumber) |
| 9 | Content disposal (redact at provider) | `UpdateMessage` (body="") |
| 10 | Reconciliation over a date range, from = `Twilio:FromNumber` | `ListMessage` (paginated) |

Every Twilio call is issued through `TwilioMessagingClient`; the send path never throws out to the
order operation (§ readiness rows 12/14).

---

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier; named arguments must use them exactly (the cancellation-token parameter is `ct`, so
> write `ct:`). All the `Api20100401Message` writes have **24/2/etc. nullable-no-default params that
> must be passed explicitly** — pass `null` to skip.
> ⚠ **Every SDK type is written fully-qualified from the namespace its source path implies**
> (`Models/` → `TwilioSdk.Models`, `Models/Enums/` → `TwilioSdk.Models.Enums`, controllers `Api/` →
> `TwilioSdk.Api`, client/options/`BasicAuthCredentials`/`ServerEnvironment` → `TwilioSdk` /
> `TwilioSdk.Servers`), taken from the path the map gives for THAT type.

### Operations

| Operation | Signature (verbatim) · returns · error case · used for | source |
| --- | --- | --- |
| `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `LookupResponse` · **Case B** · **Server group `Default4`** (`lookups.twilio.com` — NOT governed by `Twilio:BaseUrl`). Validate+canonicalize a contact number. Pass only `phoneNumber`; all 15 middle params `null`. | map/operations/LookupsV2PhoneNumber.md |
| `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` · **Case B** · **Server group `Default` (`api.twilio.com`) — governed by `Twilio:BaseUrl`.** Send immediate + scheduled messages. | map/operations/Api20100401Message.md |
| `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` · **Case B** · group `Default`. Refresh a notification's delivery outcome. | map/operations/Api20100401Message.md |
| `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` · **Case B** · group `Default`. `<remarks>` = "used to redact Message body text and to cancel not-yet-sent messages". Cancel follow-up: `body:null, status:MessageEnumUpdateStatus.Canceled`. Dispose content: `body:"" , status:null`. | map/operations/Api20100401Message.md · Api/Api20100401Message.cs:237-261 |
| `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ListMessageResponse` · **Case B** · group `Default`. Reconcile. Wire: `From←from`, `DateSent<←dateSentQuery` (upper bound=`to`), `DateSent>←dateSentQueryQuery` (lower bound=`from`), `PageSize←pageSize`, `Page←page`. Pass `from = Twilio:FromNumber` so the provider filters by our sender (not filtered client-side). | map/operations/Api20100401Message.md |
| `client.Api20100401Message.DeleteMessage` | (available, **not used** — disposal uses UpdateMessage redaction so the record + status survive). | map/operations/Api20100401Message.md |

### Response fields read (all `TwilioSdk.Models`)

- `LookupResponse` (`Models/LookupResponse.cs`): `Valid: bool?` (`valid`), `PhoneNumber: string?` (`phone_number`, E.164 canonical), `CountryCode: string?` (`country_code`), `CallingCountryCode: string?`. Reject registration when `Valid != true`; store `PhoneNumber`.
- `ApiV2010AccountMessage` (`Models/ApiV2010AccountMessage.cs`): `Sid: string?` (`sid`), `Status: MessageEnumStatus?` (`status`), `To: string?`, `From: string?`, `ErrorCode: int?`, `ErrorMessage: string?`, `DateSent: string?` (RFC-2822 GMT), `Body: string?`.
- `ListMessageResponse` (`Models/ListMessageResponse.cs`): `Messages: IReadOnlyList<ApiV2010AccountMessage>?` (`messages`), `NextPageUri: string?`, `Page: int?`, `PageSize: int?`. Paginate by incrementing `page` until `Messages` empty or `NextPageUri` null.

### Enum value tables (all `TwilioSdk.Models.Enums`, build via static members)

| Enum | Members used (C# → wire) | source |
| --- | --- | --- |
| `MessageEnumScheduleType` | `Fixed` → `fixed` (only member; `<summary>`: Messaging-Services only, use with send_time to schedule) | Models/Enums/MessageEnumScheduleType.cs |
| `MessageEnumUpdateStatus` | `Canceled` → `canceled` (only member) | Models/Enums/MessageEnumUpdateStatus.cs |
| `MessageEnumAddressRetention` | `Obfuscate` → `obfuscate` (keeps destination number out of provider-side logs) · `Retain` | Models/Enums/MessageEnumAddressRetention.cs |
| `MessageEnumStatus` (read) | `Queued Sending Sent Failed Delivered Undelivered Accepted Scheduled Canceled Read PartiallyDelivered Receiving Received` | Models/Enums/MessageEnumStatus.cs |

Optional create-body fields I deliberately set, with purpose (all others → **omit → provider default**):
- `from = Twilio:FromNumber` for all **immediate** messages (placed/dispatched/cancelled/resend) — makes them attributable to our sender for reconciliation. `messagingServiceSid` omitted for these.
- **Scheduled follow-up only:** `messagingServiceSid = Twilio:MessagingServiceSid` + `scheduleType = Fixed` + `sendAt = now+3d`; `from` omitted (scheduling is Messaging-Service-only per the enum `<summary>` — the two are mutually exclusive). `UNVERIFIED` (live-only): the exact accepted send-at window; mitigated by defensive directive below.
- `addressRetention = Obfuscate` on every send — provider-side privacy for the shopper's number.
- `to` (required-by-endpoint): the stored canonical E.164 number.
- `body`: the message text.
All remaining 20+ optional params passed as `null` (omit → provider default).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A `to` accepted for a send must be a canonical number the Lookup returned `Valid=true` for | `CreateMessage` ← `FetchPhoneNumber3` | application: only stored `ContactNumber.E164` (produced by a passing Lookup) is ever passed as `to`; a shopper with no stored number is not messaged |
| A `sid` passed to `UpdateMessage`/`FetchMessage` must be one a prior `CreateMessage` returned and eShop persisted | `UpdateMessage`/`FetchMessage` ← `CreateMessage` | application: operator endpoints resolve `sid` from the persisted `SmsNotification.ProviderSid`, never from caller input |
| The follow-up cancelled on order-cancel must be the scheduled message created at dispatch | `UpdateMessage(Canceled)` ← `CreateMessage(scheduleType=Fixed)` | application: cancel loads the order's `SmsNotification` of kind `DeliveryFollowUp` still in a cancellable status |

### Client construction / auth / server

- Construct: `new TwilioSdkClient(HttpClient, TwilioSdkClientOptions)` — the only ctor (`TwilioSdkClient.cs`). DI helper `services.AddTwilioSdkClient(options => …)` exists (sdk-map.md). HttpClient lifetime → **MUST load dotnet-client-initialization** (§3).
- Auth (`TwilioSdk`): `options.AccountSidAuthToken = new BasicAuthCredentials { Username = accountSid, Password = authToken }` (sdk-map.md *Servers & auth*; Basic auth). A credential never set is silently skipped → 401, so both parts must be present (readiness row 1).
- Server override: messaging ops resolve through group `Default`; when `Twilio:BaseUrl` is set, assign `options.Server.Default.Production.BaseUrl = <BaseUrl>` verbatim. Lookup (group `Default4`) is left on its own host. (map *Servers & auth* table; `DeleteMessage` route uses `_server.Default(...)`, Api/Api20100401Message.cs:148.)
- `accountSid` argument to every `Api20100401Message` op = `Twilio:AccountSid`.

---

## 3. Trap notes (name the hazard, defer to the skill — do not resolve here)

- **HttpClient lifetime & DI scope of the SDK client** — wrong lifetime leaks sockets or captures a stale handler. **MUST load dotnet-client-initialization.**
- **Where/when credentials are attached to the options object** — set-before-construct vs DI callback, and secret sourcing. **MUST load dotnet-authentication.**
- **Optional params with no C# default mis-bind in positional calls; enums are `StringEnum<T>` not C# enums; `sendAt` is `DateTimeOffset?`** — building the create/update/list calls. **MUST load dotnet-calling-endpoints + dotnet-models.**
- **Two-source `JsonException` boundary + Case-B `RawError` reading** — a drifted 2xx body and a non-2xx body that misses its error shape fail in opposite ways. **MUST load dotnet-error-handling.**
- **`Timeout` is per-attempt not total; `HttpMethodsToRetry` default excludes POST/PATCH/DELETE; `LogRequestBody` logs bodies unredacted; `TWILIOCLIENT_LOG` can arm logging from outside code** — tuning resilience & logging. **MUST load dotnet-configuration-resilience.**
- **The HttpClient ctor arg is the test seam; match the project's xUnit/Moq style** — unit-testing the wrapper. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load ALL before implementing; sheet does not carry their contents)

| skill | governs |
| --- | --- |
| `twilio-platforms-team:dotnet-client-initialization` | Step 2 — client construction, HttpClient lifetime, DI |
| `twilio-platforms-team:dotnet-authentication` | Step 2 — BasicAuthCredentials, secret sourcing |
| `twilio-platforms-team:dotnet-calling-endpoints` | Steps 3–10 — named-arg calls, must-pass params |
| `twilio-platforms-team:dotnet-models` | Steps 3–10 — StringEnum, response envelopes, AdditionalProperties |
| `twilio-platforms-team:dotnet-error-handling` | all — Case B `RawError`, the two `JsonException` directions |
| `twilio-platforms-team:dotnet-configuration-resilience` | Step 2 — timeout budget, retry eligibility, logging |
| `twilio-platforms-team:dotnet-testing` | tests — HttpClient seam |

Two mandatory hazard rows (verbatim): a drifted/malformed **2xx** body (missing `required` member)
surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an
SDK-exception-only catch ladder lets it escape. A **non-2xx** body that does not match its operation's
generated error shape throws `JsonException` **while the error object is constructed**, replacing the
`SdkException` and destroying the HTTP status. The boundary must catch `JsonException` on both sides.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | `TwilioSettings` bound from `Twilio:` via `AddOptions<TwilioSettings>().Bind(cfg.GetSection("Twilio")).Validate(non-blank AccountSid **and** AuthToken **and** FromNumber **and** MessagingServiceSid).ValidateOnStart()`. Each part checked separately (blank ≠ missing). Host refuses to start otherwise, never a first-call 401. `BaseUrl` optional (no validation). |
| 2 | **Secret sourcing & rotation** | Secrets from **.NET user-secrets** (`Twilio:*`), loaded via the default config providers (+`AddEnvironmentVariables()` already in Program.cs). Options built **once at registration** and captured in the singleton `TwilioMessagingClient`; a rotated secret takes effect on **process restart** — acceptable, documented; no hot-reload required by task. Values never written to any repo file. |
| 3 | **Total timeout budget** | Caller-facing budget bounded by a `CancellationToken` deadline (≈30 s) passed into every SDK call from the wrapper — `Timeout` alone is per-attempt (row noted). Because a failed send must not fail the operation, the wrapper additionally swallows timeout/transport exceptions into a recorded `Failed` outcome. Enforced in `TwilioMessagingClient`. **MUST load dotnet-configuration-resilience** for the knob semantics. |
| 4 | **Write-retry ownership** | Sends/updates are `POST` → **never auto-resent by the SDK** (default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS). No PUT in scope. So no accidental duplicate sends from SDK retries; duplicates only possible via caller replay → row 5. |
| 5 | **Idempotency & ambiguous writes** | `CreateMessage` takes **no** real caller idempotency key (only the generator-injected per-call `Idempotency-Key` GUID, which is not one). **Resend** carries a caller-supplied key → enforced by a DB claim (row 9). Placed/dispatched/cancelled sends are gated by **order-state transitions** (row 12), so a repeated dispatch does not resend. The recovery path for an ambiguous send with no key is the **reconciliation report** (row 13/§Flow 3). |
| 6 | **Observability** | Structured logs via `IAppLogger<T>`: operation + orderId + notificationId + provider Sid + resulting status + provider `ErrorCode`/`ErrorMessage` (the provider's correlation on failures). **Never** the destination number or message body. SDK `LogRequestBody` left **off**. |
| 7 | **Sensitive data** | Scope carries the shopper's phone number (`to`) and message `body` — both sensitive. `TwilioMessagingClient` sets `options.Logging.LoggerFactory` **explicitly** (so `TWILIOCLIENT_LOG` cannot arm body logging from outside), keeps `LogRequestBody` **off**, and `addressRetention = Obfuscate` on every send for provider-side number privacy. Our own logs never echo `to`/`body`. |
| 8 | **Environment selection** | Single `ServerEnvironment.Production`. Groups touched: `Default` (messaging: create/fetch/update/list) and `Default4` (lookup). `Twilio:BaseUrl`, when set, overrides **only** `Server.Default.Production.BaseUrl`. No SDK sandbox env exists; test traffic is kept off the live system by unit-testing at the **HttpClient seam** (no live calls in tests) and by placeholder Twilio config in the two host-booting test projects. |
| 9 | **Duplicate prevention under concurrency** | Store **`ResendClaims`**, column **`IdempotencyKey`** as the table's **primary key**. Resend inserts the claim row and `SaveChanges` **before** sending; a duplicate key makes the insert throw (`DbUpdateException`/tracked-key conflict) which the code **catches** and returns the already-produced `notificationId`. PK uniqueness is enforced by **both** SQL Server and the EF **in-memory** provider (unlike non-key unique indexes), so it holds in this environment. No dictionary/semaphore/pre-check. |
| 10 | **Partial results** | Reconciliation paginates `ListMessage` until `Messages` empty / `NextPageUri` null, capped at a `MaxPages` guard. If the cap is hit the response returns `Truncated = true` (a field, not a log) so the caller learns coverage was bounded. |
| 11 | **Startup validation vs test host** | `ValidateOnStart()` runs when either host-booting test project starts. Both are given non-blank **placeholder** `Twilio:*` config: `tests/FunctionalTests/PublicApi/ApiTestFixture.cs` (`TestApiApplication`) via `ConfigureAppConfiguration`, and `tests/PublicApiIntegrationTests/ProgramTest.cs` via a `WebApplicationFactory<Program>` subclass adding the same. Both projects are **run and confirmed green** after the change (baseline captured first). |
| 12 | **Ordering & no-op side effects** | For every send: the local `SmsNotification` row is written (`Pending`) **before** the provider call, then updated with Sid+status **after**. Order transitions `MarkDispatched()`/`MarkCancelled()` are **idempotent**: the "on its way"/"cancelled" SMS and the follow-up schedule/cancel are gated on the status actually changing (already-dispatched dispatch is a no-op that sends nothing). The provider response is never the first local write. |
| 13 | **Unknown outcomes** | If a `CreateMessage` transport fails after the request may have been received, the notification is recorded `Failed` with the error and **not** retried inline; recovery is the reconciliation report, which re-reads the provider via **`ListMessage`** searched by **`from = Twilio:FromNumber`** + date range and lines up provider Sids against eShop's `ProviderSid`s (there is no caller key on `CreateMessage` to re-read by). Not a definite-failure-without-re-read. |
| 14 | **Provider status & reconciliation clock** | `CreateMessage`/`FetchMessage`/`UpdateMessage` return `Status` (`MessageEnumStatus`); it is stored raw (never `?? "COMPLETED"`) and branched on: resend targets notifications in `Failed`/`Undelivered`; my-orders/notifications refresh via `FetchMessage`. Reconciliation: **both sides filter on the message's provider `DateSent`** — provider side via `DateSent</DateSent>`, eShop side via the stored `ProviderDateSent` (parsed from the send response), **not** the local row-creation column. |

---

## 6. Assumptions & Blockers

- **No blockers.** Every capability required maps to an operation above.
- **Assumption (minor):** "a few days later" → **3 days**, within the Messaging-Service scheduling window; adjustable via a constant.
- **Assumption (minor):** `POST /api/orders` needs a ship-to address (existing `Order` requires one). Request may include one; otherwise a placeholder address is used (feature is about notifications, not shipping).
- **Assumption (minor):** "did not reach the shopper" for resend = notification status in {`Failed`,`Undelivered`}; a fresh idempotency key is required per genuine attempt.
- **Defensive directive (for the `UNVERIFIED` send-at window):** compute `sendAt` well inside a few days and treat a provider rejection of the scheduled follow-up as a recorded `Failed` follow-up that never blocks dispatch; the immediate "on its way" SMS still goes out.

---

## 7. Post-implementation reconciliation (what shipped)

Every PRODUCTION READINESS row maps to a shipped artefact; the notable ones:

- **Row 1/2 fail-fast & secret sourcing** — `MessagingServiceCollectionExtensions.AddTwilioMessaging` (`.Validate(...).ValidateOnStart()`), options built once in the singleton factory. Secrets in user-secrets.
- **Row 7 sensitive data** — `TwilioMessagingClient` sets `LoggerFactory = NullLoggerFactory.Instance`, `LogRequestBody=false`, `addressRetention: Obfuscate`; wrapper/service logs never include the number or body.
- **Row 9 duplicate prevention** — `ResendClaim.IdempotencyKey` is the table PK (`ResendClaimConfiguration.HasKey`); `SmsNotificationService.ResendAsync` inserts-then-catches. **Confirmed live**: the in-memory provider enforces PK uniqueness, so a repeated key hit the catch/replay path.
- **Row 11 startup vs test host** — placeholder `Twilio:*` added to `ApiTestFixture.cs` and `ProgramTest.cs`; UnitTests (51), PublicApiIntegrationTests (15), FunctionalTests (12) all green.
- **Row 12/14 ordering, no-op gating, status** — `SmsNotification` written before the provider call and updated after; `Order.MarkDispatched/MarkCancelled` gate side effects on a real change; provider `Status` stored raw and branched on (`undelivered`→Failed). Reconciliation filters both sides on `ProviderDateSent`.

**Two fixes made during self-verification (plan rows unchanged, both are application-DI details):**
1. The `ITwilioMessagingClient` singleton must not capture the scoped `IAppLogger<>`; its logger is now built from the singleton `ILoggerFactory`.
2. `ResendClaimByKeySpecification` reads `AsNoTracking()` so the replay path returns the persisted claim's resulting notification id, not the stale tracked instance left by the failed insert.

**Live verification (against the live account):** real delivery to the Canadian number; the US number settled to `undelivered` (30034, handled as an outcome); follow-ups scheduled then canceled at the provider; operator resend created → replayed (same id, no second send) → fresh-key created; content redacted at the provider (status survived); reconciliation over a populated range returned in-both/provider-only counts filtered by `Twilio:FromNumber`.
