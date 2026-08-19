---
name: eshop-sms-notifications
description: How the eShopOnWeb PublicApi SMS order-notification (Twilio) feature is wired and run
metadata:
  type: project
---

Added an additive SMS order-notification feature to `src/PublicApi` (Twilio via `api-specs`, no SDK). See [[twilio-spec-endpoints]] for the spec-to-endpoint mapping.

**Layout**: domain in `ApplicationCore` (`Entities/Notifications/{ContactNumber,OrderNotification,NotificationKind,MessageDeliveryStatus}`, `OrderStatus` added to `Order` with `MarkDispatched/MarkCancelled`), `Interfaces/{ISmsGateway,IContactNumberService,IOrderNotificationService}`, `Services/{ContactNumberService,OrderNotificationService}`, `Notifications/NotificationModels` DTOs, specs. Twilio HTTP client is hand-written in `Infrastructure/Notifications/Twilio/TwilioSmsGateway.cs` (typed HttpClient, Basic auth). DbSets `ContactNumbers`/`OrderNotifications` + EF configs in `Infrastructure/Data`. Endpoints in `PublicApi/{ContactNumberEndpoints,OrderEndpoints,NotificationEndpoints}` via `MinimalApi.Endpoint` `IEndpoint<>` (max arity: IEndpoint`4 → HandleAsync with 3 params; scoped deps + ClaimsPrincipal injected through the AddRoute lambda, not ctor). DI in `PublicApi/Notifications/NotificationServicesExtensions.AddOrderSmsNotifications`.

**Config**: bound from `Twilio:` section (`AccountSid`,`AuthToken`,`FromNumber`,`MessagingServiceSid`,`BaseUrl` optional messaging-only override). Loaded into .NET user-secrets (id `7413ff73-f243-4604-84fb-57c751212009`), never in repo. Lookup API host is NOT overridden by BaseUrl.

**Run** (env has only .NET 10 SDK; ASP.NET 8 runtime IS present): `global.json` set `rollForward: latestMajor`; run PublicApi with `DOTNET_ROLL_FORWARD=Major`, `UseOnlyInMemoryDatabase=true`, `ASPNETCORE_URLS=https://localhost:11723;http://localhost:11724` (port block 11720–11739). In-memory store is per-host and per-run — drive the whole flow through PublicApi in one run. Verify as `admin@microsoft.com` / `Pass@word1` (admin = operator + can also be the shopper). Test numbers: `TWILIO_TEST_TO_NUMBER` (CA, deliverable), `TWILIO_UNREACHABLE_TO_NUMBER` (US, undelivered by design) — never message any other real number.

Follow-up = Twilio scheduled message (`ScheduleType=fixed`, `SendAt` now+3d via MessagingServiceSid); cancel order → POST `Status=canceled` before it sends. Content disposal = POST empty `Body` (redact), record survives. Reconciliation = List Messages `From=FromNumber` + `DateSent` range, matched by SID.
