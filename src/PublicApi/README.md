# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Card and bank payments go through the Stax SDK (`sdk/stax`). Settings are read from the `Stax:` section:
`Stax:ApiKey`, `Stax:PlatformApiKey` (only for seller onboarding) and `Stax:BaseUrl` (optional; used verbatim
instead of the public address). Never commit their values; load them into user-secrets from the environment:

```
dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi   # if the shop has one
```

(`STAX_API_KEY` / `STAX_PLATFORM_API_KEY` are also read directly when the settings are empty.)

Endpoints (shopper = any signed-in user, staff = the administrator role): `/api/payment-methods`,
`/api/orders` (+ `/checkout`, `/pay`), `/api/account/close`, `/api/admin/shoppers/{email}/payment-methods`,
`/api/admin/orders/{id}` (+ `/refunds`), `/api/admin/invoice-schedules` (+ `/pause`, `/resume`), `/api/admin/sellers`.
Orders and payment records live in a separate `PaymentsContext` database (`PaymentsConnection`, created on startup).
A background service settles bank payments and retries queued refunds (a bank payment cannot be refunded for
the first business days after it is made).
