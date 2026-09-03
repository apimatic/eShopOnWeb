<!-- Generated file — do not edit; regenerated with the SDK. -->

# Offers — operations

Accessor: `client.Offers` · Source: `Api/Offers.cs` · 5 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ArchiveOffer

- **Signature**: `ArchiveOffer(int offerId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

### CreateOffer

- **Signature**: `CreateOffer(CreateOfferRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `OfferResponse`
- **Error**: `SdkException<CreateOfferError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateOfferRequest` | `Models/CreateOfferRequest.cs` |
| `OfferResponse` | `Models/OfferResponse.cs` |
| `CreateOfferError` | `Errors/CreateOfferError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### ListOffers

- **Signature**: `ListOffers(bool? includeArchived, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `includeArchived` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `include_archived` ← `includeArchived`
- **Returns**: `ListOffersResponse`
- **Error**: `SdkException<ListOffersError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ListOffersResponse` | `Models/ListOffersResponse.cs` |
| `ListOffersError` | `Errors/ListOffersError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadOffer

- **Signature**: `ReadOffer(int offerId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `OfferResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `OfferResponse` | `Models/OfferResponse.cs` |

### UnarchiveOffer

- **Signature**: `UnarchiveOffer(int offerId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

