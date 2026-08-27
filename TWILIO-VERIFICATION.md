# Verifying the Twilio SMS order notifications

Step-by-step guide to run and verify the SMS notification feature end-to-end against
live Twilio. Two scripts automate the whole walkthrough: `verify-twilio.ps1` (shopper
flow) and `verify-twilio-2.ps1` (operator flow).

## 1. One-time setup

Credentials live in .NET user-secrets for the `src/PublicApi` project — never in the
repo. Set them once:

```powershell
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid" "<AC...>"
dotnet user-secrets set "Twilio:AuthToken" "<auth token>"
dotnet user-secrets set "Twilio:FromNumber" "<+1... — the app's sending number>"
dotnet user-secrets set "Twilio:MessagingServiceSid" "<MG... — used for scheduled messages>"
```

Optional: `Twilio:BaseUrl` overrides the messaging API host (e.g. a mock server); leave
it empty for production Twilio. Missing values stop the host at startup with a clear
validation error.

Set the two test destination numbers as environment variables (used by the scripts):

```powershell
$env:TWILIO_TEST_TO_NUMBER = "<+1... — reachable Canadian test number>"
$env:TWILIO_UNREACHABLE_TO_NUMBER = "<+1... — valid US number that cannot receive SMS>"
```

The app runs on the in-memory database (`UseOnlyInMemoryDatabase: true` in
`src/PublicApi/appsettings.json`), so no SQL Server is needed. `global.json` allows
SDK roll-forward (`latestMajor`), so any .NET 8+ SDK works.

## 2. Run the API

```powershell
dotnet run --project src/PublicApi --launch-profile PublicApi
```

Wait for `Now listening on: https://localhost:18083`. Swagger UI is at
`https://localhost:18083/swagger`.

Seeded accounts: shopper `demouser@microsoft.com` / operator `admin@microsoft.com`,
both `Pass@word1`. Operator actions require the `Administrators` role (admin).

## 3. Shopper flow — `verify-twilio.ps1`

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-twilio.ps1
```

What it proves, in order:

1. **Sign-in** — both users get JWTs from `POST /api/authenticate`.
2. **Number validation** — `POST /api/contact-numbers` with a bogus number is rejected
   (HTTP 400); the provider's lookup says it is not usable.
3. **Registration** — the reachable number registers; the response holds Twilio's
   canonical E.164 form and the `contactNumberId`. `GET /api/contact-numbers` lists it.
4. **Order placed** — `POST /api/orders` returns `orderId`; the order-placed SMS is
   delivered. `GET /api/orders/{orderId}/notifications` shows status `delivered`
   (polled from Twilio — there is no callback URL).
5. **Dispatch** — `POST /api/orders/{orderId}/dispatch` (operator) sends the dispatch
   SMS (`delivered`) and schedules the follow-up (`scheduled`, `scheduledFor` ~3 days
   out, via the messaging service).
6. **Authorization** — a shopper calling dispatch gets HTTP 403.
7. **Cancel** — `POST /api/orders/{orderId}/cancel` (operator) sends the cancellation
   SMS and calls off the pending follow-up at Twilio (`canceled`).
8. **My orders** — `GET /api/my-orders` shows the order with every notification's
   type, status, and timestamps.

## 4. Operator flow — `verify-twilio-2.ps1`

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-twilio-2.ps1
```

What it proves:

1. **Undeliverable number** — the US test number passes format validation, the SMS is
   accepted by Twilio, then refused by the carrier: status `undelivered`,
   `errorCode 30034`. The order itself still succeeds — message failures never fail
   the operation.
2. **Scoping** — the shopper cannot read another user's order notifications (404, no
   existence leak).
3. **Resend + idempotency** — `POST /api/notifications/{id}/resend` with an
   `Idempotency-Key` sends a fresh message; repeating the same key returns the same
   `notificationId` without sending again.
4. **Content disposal** — `DELETE /api/notifications/{id}/content` erases the message
   text at Twilio and locally (`body: null`, `contentDisposed: true`); the record and
   its delivery outcome survive. Resending a disposed message is rejected (400).
5. **Reconciliation** — `GET /api/notifications/reconciliation?from=...&to=...`
   (ISO-8601) lines eShop's records up against Twilio's own list for the app's sending
   number: `matched` (with per-message status comparison), `providerOnly` (messages
   Twilio shows that this database doesn't know — expected on a fresh in-memory DB),
   `appOnly` (records with no provider entry in range — e.g. canceled schedules and
   still-queued sends, which have no `DateSent` yet), and `providerListTruncated` if
   the page cap was hit.
6. **Number removal** — `DELETE /api/contact-numbers/{id}`; a second delete is 404.
7. **No number on file** — an order placed after removing the number succeeds with no
   notifications attempted.

## 5. Automated tests

```powershell
dotnet test eShopOnWeb.sln
```

100 tests, all offline (Twilio is faked at the HTTP boundary): SDK wire format, error
translation, the duplicate-send guard, cursor paging, contact-number rules, and the
notification orchestration (send/schedule/cancel/resend/dispose, including the
never-fail-the-order rule).

## Notes

- **Status freshness**: with no public callback URL, statuses are polled from Twilio
  whenever notifications are read. Terminal states (`delivered`, `undelivered`,
  `failed`, `canceled`) are not re-polled.
- **Privacy**: destination numbers are never written to logs; provider error text is
  truncated and sanitized before surfacing.
- **Resilience**: each Twilio attempt is bounded (10 s), whole calls are budgeted, and
  a single-send guard blocks duplicate messages if a transport retry ever replays a
  send.
