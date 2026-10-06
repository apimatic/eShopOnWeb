# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Square integration

Keeps the merchant's Square account in step with the shop: catalog items (name, price, photo) and
online orders with their gift message. Code: `src/Infrastructure/SquareIntegration`, endpoints in
`SquareEndpoints/`, `OrderEndpoints/` and `CatalogItemEndpoints/UploadCatalogItemPhotoEndpoint.cs`.
Design notes and the Square contract sheet: `square-plan.md` at the repository root.

### Configuration (`Square:` section — never commit values)

| Key | Required | Meaning |
| --- | --- | --- |
| `Square:Environment` | yes | `sandbox` or `production` |
| `Square:ApplicationId` | yes | OAuth application id |
| `Square:ApplicationSecret` | yes | OAuth application secret |
| `Square:RedirectUri` | yes | Callback registered with Square; must reach `GET /api/square/callback` |
| `Square:AccessToken` | no | Used until a merchant connects through sign-in |

The host refuses to start when a required key is missing. For local development load them into
user-secrets, e.g. `dotnet user-secrets set "Square:ApplicationSecret" "$SQUARE_APPLICATION_SECRET" --project src/PublicApi`.

### Endpoints

| Endpoint | Who | Purpose |
| --- | --- | --- |
| `GET /api/square/connect` | Administrators | Returns `signInUrl` for the merchant to approve the shop |
| `GET /api/square/callback` | Square redirect (no token) | Completes sign-in; refuses callbacks the shop did not start |
| `GET /api/square/connection` | Administrators | Connected? As which merchant (`merchantId`, `businessName`) |
| `POST /api/square/catalog/sync` | Administrators | Creates/updates Square items; returns `created`/`updated`/`unchanged` |
| `PUT /api/catalog-items/{id}/photo` | Administrators | multipart field `photo`, JPEG/PNG ≤ 5 MB → `imageId`, `imageUrl` |
| `POST /api/orders` | Any signed-in shopper | `{ items: [{ catalogItemId, quantity }], giftMessage?, shipToAddress? }` → `orderId` |
| `GET /api/my-orders/{orderId}` | The order's shopper | Order + `squareOrderId` + the gift message as Square holds it now |

Database: the integration adds tables via the `AddSquareIntegration` migration (SQL Server). With
`UseOnlyInMemoryDatabase=true` they are recreated empty on each start; items already in Square are
recognised again by their SKU (`eshop-{catalogItemId}`), so no duplicates are created.
