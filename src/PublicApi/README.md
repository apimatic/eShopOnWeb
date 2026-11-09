# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

The payment endpoints (`/api/payment-methods`, `/api/orders`, `/api/account/close`, `/api/admin/...`) call Stax through the SDK in `sdk/stax`.

Settings (configuration section `Stax:`; never commit values):

| Key | Meaning |
| --- | --- |
| `Stax:ApiKey` | the shop's Stax API key (sandbox key while testing) |
| `Stax:PlatformApiKey` | optional platform key, needed only to onboard sellers (`POST /api/admin/sellers`) |
| `Stax:BaseUrl` | optional; when set it is used verbatim as the Stax API base address |

Load them into user secrets from the environment, e.g.:

```
dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi
```

Checkout policy lives in `Payments:` (`CardFeePercent`, `CardFeeFixed`, `ReconcileSeconds`, `RefundRetrySeconds`): card payments carry the processing fee, bank payments none.
Bank payments become `paid` when Stax reports them settled, and bank refunds Stax cannot make yet (ACH voids/refunds open after a few business days) stay `pending` and are completed by the background reconciler.

The SDK needed three small additions (it is git-ignored here): `PaymentMethod.GetPaymentMethods(GetPaymentMethodsRequest)` (customer/status filter, paginated), `GetCustomersRequest.Email`, and typed (`object`) responses for the invoice-schedule operations.
