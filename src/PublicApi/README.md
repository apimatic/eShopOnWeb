# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Digital downloads (Box)

Catalog items can be sold as digital editions whose files live in the merchant's Box account, in the folder
`eshop-digital-products` directly under the account root. The shop only ever reads from Box.

| Endpoint | Who | What |
| --- | --- | --- |
| `GET api/digital-files` | Administrators | Files in the Box folder: `id`, `name`, `size`, `sha1`; `isTruncated` when the folder is larger than the listing cap |
| `PUT api/catalog-items/{catalogItemId}/digital-file` `{ "fileId": "..." }` | Administrators | Links the item to one of the listed files (422 for any other id); returns its name and size |
| `POST api/orders` `{ "items": [{ "catalogItemId": 1, "quantity": 1 }] }` | Any signed-in shopper | Places an order (existing `Order`/`OrderItem` model) for the caller; returns `orderId` |
| `GET api/orders/{orderId}/downloads/{catalogItemId}` | The shopper who placed the order | Streams the linked file with its name and type; 404 for other shoppers' orders, items not in the order, or items without a file |

Downloads are passed through without buffering. If Box sends no data for `Box:StallTimeout` (30 s) the
download is abandoned and logged; a shopper never receives a truncated file as a complete response (the
response is aborted instead).

### Configuration (`Box` section)

| Key | Default | |
| --- | --- | --- |
| `Box:AccessToken` | — (required; the host refuses to start without it) | Box bearer token. Development: `dotnet user-secrets set Box:AccessToken <token> --project src/PublicApi`. Elsewhere: environment variable `Box__AccessToken`. Re-read about every 30 s, so a rotated token applies without a restart when the configuration source reloads. |
| `Box:FolderName` | `eshop-digital-products` | Folder under the account root holding the files |
| `Box:RequestTimeout` | `00:00:30` | Budget for one Box operation (listing, or opening a download up to its headers), retries included |
| `Box:AttemptTimeout` | `00:00:10` | Per-attempt timeout; GETs are retried twice |
| `Box:StallTimeout` | `00:00:30` | Download abandoned after this long without data |
| `Box:PageSize` / `Box:MaxListingPages` | `1000` / `10` | Folder listing paging and cap |

The Box Platform API SDK is referenced as source from the `box` Claude Code plugin
(`<plugin>/sdk/dotnet/BoxPlatformApi.csproj`); set the MSBuild property `BoxSdkProject` if the plugin is
installed somewhere other than `../marketplace/plugins/box` relative to the repository root.

The link table is created by the `AddCatalogItemDigitalFiles` migration (`src/Infrastructure/Data/Migrations`).
