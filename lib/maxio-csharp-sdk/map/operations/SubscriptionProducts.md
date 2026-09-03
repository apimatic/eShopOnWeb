<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionProducts — operations

Accessor: `client.SubscriptionProducts` · Source: `Api/SubscriptionProducts.cs` · 2 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### MigrateSubscriptionProduct

- **Signature**: `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<MigrateSubscriptionProductError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionProductMigrationRequest` | `Models/SubscriptionProductMigrationRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `MigrateSubscriptionProductError` | `Errors/MigrateSubscriptionProductError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### PreviewSubscriptionProductMigration

- **Signature**: `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionMigrationPreviewResponse`
- **Error**: `SdkException<PreviewSubscriptionProductMigrationError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionMigrationPreviewRequest` | `Models/SubscriptionMigrationPreviewRequest.cs` |
| `SubscriptionMigrationPreviewResponse` | `Models/SubscriptionMigrationPreviewResponse.cs` |
| `PreviewSubscriptionProductMigrationError` | `Errors/PreviewSubscriptionProductMigrationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

