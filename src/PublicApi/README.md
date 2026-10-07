# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Card payments (Adyen)

`OrderEndpoints/` takes card payments through Adyen. All routes need a JWT from `POST api/authenticate`.

| Route | Who | What |
| --- | --- | --- |
| `POST api/orders` | shopper | Place an order from `{ "items": [{ "catalogItemId", "quantity" }] }` at catalog prices; starts `AwaitingPayment`. |
| `POST api/orders/{orderId}/pay` | owning shopper | Charge the total now with Adyen-encrypted card fields (`encryptedCardNumber`, `encryptedExpiryMonth`, `encryptedExpiryYear`, `encryptedSecurityCode`, `holderName`). Repeating it never charges twice. |
| `GET api/my-orders` | shopper | The caller's orders and their payment state. |
| `POST api/orders/{orderId}/refunds` | Administrators | `{ "amount", "reason"?, "idempotencyKey"? }`, never beyond what was paid. |
| `GET api/orders/{orderId}/adyen-record` | Administrators | Every payment attempt and refund with Adyen's responses verbatim. |

Configuration is bound from the `Adyen` section — `Adyen:ApiKey`, `Adyen:MerchantAccount`, `Adyen:Environment` (must be
`test`), `Adyen:Currency` — and the API refuses to start when one is missing. Keep the values out of the repository; for
local development load them into user-secrets from the `ADYEN_*` environment variables:

```bash
dotnet user-secrets --project src/PublicApi set "Adyen:ApiKey" "$ADYEN_API_KEY"
dotnet user-secrets --project src/PublicApi set "Adyen:MerchantAccount" "$ADYEN_MERCHANT_ACCOUNT"
dotnet user-secrets --project src/PublicApi set "Adyen:Environment" "$ADYEN_ENVIRONMENT"
dotnet user-secrets --project src/PublicApi set "Adyen:Currency" "$ADYEN_CURRENCY"
```

The Adyen SDK is referenced as a project from the installed **adyen** plugin (`src/Payments.Adyen/Payments.Adyen.csproj`);
re-point that `ProjectReference` if the plugin lives elsewhere on your machine. The design and SDK contract notes are in
`adyen-apis-plan.md` at the repository root.
