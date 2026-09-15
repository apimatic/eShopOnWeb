# Order notifications by SMS (Twilio)

Additive capability on top of eShopOnWeb: shoppers put a mobile number on file, receive SMS as their
orders move, and operators can act over what was sent. All Twilio traffic goes through a thin REST
client built only from the twilio-docs reference. Exposed as JWT-authenticated endpoints on
`src/PublicApi`.

## Endpoints

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/contact-numbers` | shopper | Register a mobile number. Rejected up front if the provider (Lookup) does not consider it a usable destination; the stored value is the provider's canonical E.164 form. Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's own numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one; afterwards nothing is sent to it again. |
| `POST /api/orders` | shopper | Place an order from catalog items. Texts "order placed". Returns `orderId`. |
| `POST /api/orders/{orderId}/dispatch` | operator | Texts "on its way" and queues a "how did delivery go?" follow-up with the provider a few days later. |
| `POST /api/orders/{orderId}/cancel` | operator | Calls off any not-yet-sent follow-up, then texts "cancelled". |
| `GET /api/my-orders` | shopper | The caller's orders, each with where its notifications got to. |
| `GET /api/orders/{orderId}/notifications` | shopper | Each message for an order and its outcome; each carries a `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | operator | Re-send a message that did not reach the shopper. Deduplicated by a caller-supplied idempotency key. Returns the new `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | operator | Dispose of a message's text — redacted at the provider and cleared locally; the fact and outcome survive. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | operator | The provider's own record of messages from `Twilio:FromNumber` over the range, lined up against what eShop believes it sent. |

Operator actions are restricted to the `Administrators` role. Every other endpoint is shopper-scoped and
acts only on the caller's own data.

## Configuration

Settings bind from the `Twilio:` section (no values are hard-coded):
`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and the
optional `Twilio:BaseUrl` (an override for the **messaging** API only — Lookup uses its own host).
Load them into .NET user-secrets for the `PublicApi` project; never commit the values.

```powershell
# from src/PublicApi (values from your environment; never printed or committed)
dotnet user-secrets set "Twilio:AccountSid"          $env:TWILIO_ACCOUNT_SID
dotnet user-secrets set "Twilio:AuthToken"           $env:TWILIO_AUTH_TOKEN
dotnet user-secrets set "Twilio:FromNumber"          $env:TWILIO_FROM_NUMBER
dotnet user-secrets set "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID
```

## Run (this machine)

```powershell
$env:DOTNET_ROLL_FORWARD   = "Major"
$env:ASPNETCORE_ENVIRONMENT= "Development"     # loads user-secrets
$env:ASPNETCORE_URLS       = "https://localhost:10503;http://localhost:10504"
$env:UseOnlyInMemoryDatabase = "true"          # no LocalDB on this box
dotnet run --project src/PublicApi --no-launch-profile
```

The in-memory store is per-process and per-host, so drive the whole flow through PublicApi in one run
(that is why `POST /api/orders` exists).

## Verify (PowerShell 7)

`-SkipCertificateCheck` avoids dev-cert friction from the client side. Use the two provided test numbers
only: `TWILIO_TEST_TO_NUMBER` (Canadian, really delivers) and `TWILIO_UNREACHABLE_TO_NUMBER` (US,
accepted then refused by the carrier — an expected outcome, not a defect).

```powershell
$B = 'https://localhost:10503/api'
$tok = (Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$B/authenticate" `
        -ContentType 'application/json' `
        -Body (@{username='admin@microsoft.com';password='Pass@word1'}|ConvertTo-Json)).token
$H = @{ Authorization = "Bearer $tok" }

# 1) Register the reachable number, place an order -> a real SMS arrives
Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$B/contact-numbers" -Headers $H `
  -ContentType 'application/json' -Body (@{phoneNumber=$env:TWILIO_TEST_TO_NUMBER}|ConvertTo-Json)
$item  = (Invoke-RestMethod -SkipCertificateCheck -Uri "$B/catalog-items?pageSize=1&pageIndex=0" -Headers $H).catalogItems[0].id
$order = Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$B/orders" -Headers $H `
  -ContentType 'application/json' -Body (@{items=@(@{catalogItemId=$item;quantity=1})}|ConvertTo-Json)
Invoke-RestMethod -SkipCertificateCheck -Uri "$B/orders/$($order.orderId)/notifications" -Headers $H | % notifications

# 2) Dispatch -> "on its way" + a follow-up shown as status=scheduled
Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$B/orders/$($order.orderId)/dispatch" -Headers $H
# 3) Cancel -> the follow-up flips to status=canceled before it ever sends
Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$B/orders/$($order.orderId)/cancel" -Headers $H
Invoke-RestMethod -SkipCertificateCheck -Uri "$B/orders/$($order.orderId)/notifications" -Headers $H | % notifications

# 4) Operator re-send with idempotency: same key = no second message, fresh key = a new one
$noteId = (Invoke-RestMethod -SkipCertificateCheck -Uri "$B/orders/$($order.orderId)/notifications" -Headers $H).notifications[-1].notificationId
Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$B/notifications/$noteId/resend" -Headers $H -ContentType 'application/json' -Body (@{idempotencyKey='key-1'}|ConvertTo-Json)
Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$B/notifications/$noteId/resend" -Headers $H -ContentType 'application/json' -Body (@{idempotencyKey='key-1'}|ConvertTo-Json)  # duplicate=true

# 5) Dispose of a message's content (redacted at the provider, cleared locally; outcome survives)
Invoke-RestMethod -SkipCertificateCheck -Method Delete -Uri "$B/notifications/$noteId/content" -Headers $H

# 6) Reconciliation over a range with data
$from=(Get-Date).ToUniversalTime().Date.ToString('yyyy-MM-ddTHH:mm:ssZ')
$to  =(Get-Date).ToUniversalTime().AddMinutes(5).ToString('yyyy-MM-ddTHH:mm:ssZ')
Invoke-RestMethod -SkipCertificateCheck -Uri "$B/notifications/reconciliation?from=$from&to=$to" -Headers $H |
  Select providerMessageCount,eShopMessageCount,matchedCount,@{n='providerOnly';e={$_.providerOnly.Count}}
```

To see the undeliverable + resend path end to end, register `TWILIO_UNREACHABLE_TO_NUMBER` for a shopper
and place an order for them: the message settles at `undelivered` (error `30034`), and an operator resend
produces a fresh message.

## Design notes

- **Best-effort messaging.** A send that fails is recorded on an `OrderNotification` but never fails the
  order operation; a shopper with no number on file is simply not messaged.
- **No callback URL.** There is no publicly reachable URL, so delivery outcomes are pulled from the
  provider (fetch/list) on read, never received.
- **Follow-up lives at the provider.** Scheduled via `ScheduleType=fixed` + `SendAt` on a Messaging
  Service; cancellation uses `Status=canceled`. Nothing waits on a timer in this app.
- **Content disposal** uses message redaction (`Body=""`) so the text is gone from Twilio while the
  resource — and its delivery outcome — remains.
- **Reconciliation** asks the provider for `Twilio:FromNumber`'s messages directly (not a filtered wider
  answer) and covers the whole range via pagination.
- A shopper's number is never written to logs.
