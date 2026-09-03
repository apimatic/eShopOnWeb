<!-- Generated file — do not edit; regenerated with the SDK. -->

# Sites — operations

Accessor: `client.Sites` · Source: `Api/Sites.cs` · 3 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ClearSite

- **Signature**: `ClearSite(CleanupScope? cleanupScope, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `cleanupScope` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `cleanup_scope` ← `cleanupScope`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CleanupScope` | `Models/Enums/CleanupScope.cs` |

### ListChargifyJsPublicKeys

- **Signature**: `ListChargifyJsPublicKeys(int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `ListPublicKeysResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ListPublicKeysResponse` | `Models/ListPublicKeysResponse.cs` |

### ReadSite

- **Signature**: `ReadSite(RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SiteResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SiteResponse` | `Models/SiteResponse.cs` |

