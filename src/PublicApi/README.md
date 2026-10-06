# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Square integration

Keeps the merchant's Square account in step with the shop: the catalog (names, prices, photos) and every order
placed through `POST /api/orders`, with the shopper's gift message stored only on the Square order. The code lives
in `src/Infrastructure/SquareIntegration` (services, persistence) and `SquareEndpoints` / `OrderEndpoints` here.
Contract notes and design decisions: `square-plan.md` at the repository root.

| Endpoint | Who | What |
| --- | --- | --- |
| `GET /api/square/connect` | Administrators | Returns `signInUrl`, the Square page the merchant opens to approve the shop (valid 10 minutes, single use). |
| `GET /api/square/callback` | Anonymous (merchant's browser) | Square redirects here; refused unless its `state` came from `/connect`. Stores the merchant's tokens encrypted. |
| `GET /api/square/connection` | Administrators | Whether the shop is connected, how (`oauth` or `accessToken`), merchant id and business name. |
| `POST /api/square/catalog/sync` | Administrators | Creates/updates Square items for every eShop catalog item; returns `created`, `updated`, `unchanged`, `failed`. |
| `PUT /api/catalog-items/{id}/photo` | Administrators | `multipart/form-data` field `photo`, JPEG or PNG, ≤ 5 MB; becomes the item's primary Square image. Returns `imageId`, `imageUrl`. |
| `POST /api/orders` | Any signed-in user | `{ "items": [{ "catalogItemId": 1, "quantity": 2 }], "giftMessage": "≤ 200 chars", "shipToAddress": { … optional } }` → `orderId`. |
| `GET /api/my-orders/{orderId}` | The order's buyer | The order, its `squareOrderId`, and the gift message as Square holds it now. |

### Configuration

Bound from the `Square:` section; the host refuses to start when a required key is missing.

| Key | Required | Notes |
| --- | --- | --- |
| `Square:Environment` | yes | `sandbox` or `production` |
| `Square:ApplicationId` | yes | OAuth application id |
| `Square:ApplicationSecret` | yes | OAuth application secret |
| `Square:RedirectUri` | yes | The callback URL registered with Square; point it at `/api/square/callback` |
| `Square:AccessToken` | no | Used while no merchant has connected through sign-in |

Keep the values in user-secrets (`dotnet user-secrets set "Square:ApplicationId" "…" --project src/PublicApi`), a
secret store, or `Square__*` environment variables. The `SQUARE_ENVIRONMENT`, `SQUARE_APPLICATION_ID`,
`SQUARE_APPLICATION_SECRET`, `SQUARE_REDIRECT_URI` and `SQUARE_ACCESS_TOKEN` variables are also read, with the
lowest precedence.

### Operating notes

- **SDK reference.** The Square .NET SDK ships as source inside the `square` Claude plugin. `Infrastructure.csproj`
  references it through the `SquareSdkProject` MSBuild property, which defaults to the plugin's location next to
  this repository; pass `-p:SquareSdkProject=<path>/sdk/dotnet/Square.csproj` where it lives elsewhere (CI, Docker).
- **Database.** Apply the `AddSquareIntegration` migration (`CatalogContext`) on SQL Server. It only creates the
  four `Square*` tables. With `UseOnlyInMemoryDatabase=true` the links are lost on restart; the next sync re-links
  the Square items it created (their variation SKU is `eshop-item-{catalogItemId}`) instead of duplicating them.
- **Token encryption.** OAuth tokens are encrypted with ASP.NET Core Data Protection. Persist and share the key ring
  across instances and deployments, or the stored connection becomes unreadable and the merchant must reconnect.
- **Token lifetime.** Access tokens are refreshed a week before Square's expiry (and on use after expiry), so the
  merchant never has to sign in again unless they revoke the shop's access.
