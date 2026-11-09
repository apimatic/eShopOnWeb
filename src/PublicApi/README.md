# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Card and bank payments go through the Stax SDK in `sdk/stax`. Settings live in the `Stax:` configuration section and are
never committed: `Stax:ApiKey`, `Stax:PlatformApiKey` (marketplace onboarding only) and `Stax:BaseUrl` (optional override,
used verbatim). Load them into user-secrets from the `STAX_API_KEY` / `STAX_PLATFORM_API_KEY` environment variables:

```
dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi
```

`Payments:CardFeePercent` is the card fee passed on to shoppers (credit cards only; a rate reported by Stax wins when
surcharging is enabled on the merchant). Pending bank settlements and bank refunds are re-checked every
`Payments:SyncIntervalSeconds` by a background worker.

Shopper routes: `/api/payment-methods`, `/api/orders`, `/api/orders/{id}`, `/api/orders/{id}/checkout`,
`/api/orders/{id}/pay`, `/api/account/close`. Administrator routes: `/api/admin/shoppers/{email}/payment-methods`,
`/api/admin/orders/{id}`, `/api/admin/orders/{id}/refunds`, `/api/admin/invoice-schedules` (+ `/{id}/pause`, `/{id}/resume`),
`/api/admin/sellers`.
