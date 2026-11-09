# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Card and bank payments go through Stax (the SDK in `sdk/stax`). The settings are read from the `Stax:` configuration section and are never stored in the repository:

| Key | Meaning |
| --- | --- |
| `Stax:ApiKey` | The shop's Stax API key (sandbox key while testing). |
| `Stax:PlatformApiKey` | The platform key, only when the shop has one. Seller onboarding (`POST /api/admin/sellers`) answers `409` without it. |
| `Stax:BaseUrl` | Optional override of the Stax API address, used verbatim. |

For development load them into user-secrets from the environment (`STAX_API_KEY`, `STAX_PLATFORM_API_KEY`):

```
dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi
```

Elsewhere use environment variables such as `Stax__ApiKey`.

How it behaves:

- Shopper endpoints (`/api/payment-methods`, `/api/orders`, `/api/account/close`) identify the shopper by the JWT's name claim; staff endpoints (`/api/admin/...`) need the `Administrators` role. Failures answer with a 4xx status and a reason.
- Checkout and pay agree on the amount: card payments carry the shop's own card processing fee (the discount rate and per-transaction fee of the shop's Stax registration, by card type), bank payments carry none.
- A bank payment is accepted at once but is `paid` only after the bank settles it. A background service (and every read of the order) asks Stax, so no one has to check by hand.
- A refund always returns the whole amount (fee included). Banks refuse ACH refunds for the first business days, so a bank refund stays `refundStatus: "pending"` and is completed automatically by the same background service.
- Stax does not list removed payment methods, so the support view combines the methods Stax lists, those used in the customer's transactions, and the ones the shop saved itself.
- Closing an account erases the shopper's cards, bank accounts and customer record at Stax and the shop's data; orders that moved money are kept anonymously so settlement and refunds can finish.
