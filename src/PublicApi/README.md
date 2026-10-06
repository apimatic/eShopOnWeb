# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Card payments (Adyen)

`OrderEndpoints/` takes card payments for orders through Adyen's Checkout API (the Adyen APIs .NET SDK,
referenced as source from the adyen plugin — see `AdyenSdkProjectPath` in `src/Infrastructure/Infrastructure.csproj`).

| Endpoint | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from `{ "items": [{ "catalogItemId", "quantity" }] }`; prices come from the catalog. Starts `AwaitingPayment`. |
| `POST /api/orders/{orderId}/pay` | the order's shopper | Charge the total with Adyen-encrypted card fields (`encryptedCardNumber`, `encryptedExpiryMonth`, `encryptedExpiryYear`, `encryptedSecurityCode`, `holderName`, flat or under `paymentMethod`). Captured immediately; never charges twice. |
| `POST /api/orders/{orderId}/refunds` | Administrators | Refund `{ "amount": 12.50 }` (omit `amount` for everything left). Never beyond what was paid. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/orders/{orderId}/adyen-record` | Administrators | Every payment attempt and refund with Adyen's response verbatim. |

Configuration (section `Adyen:`; the host refuses to start when a required key is missing):

| Key | Required | Notes |
| --- | --- | --- |
| `Adyen:ApiKey` | yes | Secret. Development: `dotnet user-secrets`. Also read from `ADYEN_API_KEY` at lowest precedence. |
| `Adyen:MerchantAccount` | yes | `ADYEN_MERCHANT_ACCOUNT` |
| `Adyen:Environment` | yes | `test` or `live` (`ADYEN_ENVIRONMENT`) |
| `Adyen:Currency` | yes | ISO 4217 code all payments are taken in (`ADYEN_CURRENCY`) |
| `Adyen:CheckoutBaseUrl` | on `live` | Your live Checkout API URL from the Customer Area. |
| `Adyen:ReturnUrl` | no | Defaults to `baseUrls:webBase`. |

Refunds are accepted asynchronously by Adyen (`Received`); the final outcome arrives in Adyen's REFUND
webhook, which this build does not consume yet.
