<!-- Generated file — do not edit; regenerated with the SDK. -->

# ApiExports — operations

Accessor: `client.ApiExports` · Source: `Api/ApiExports.cs` · 9 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ExportInvoices

- **Signature**: `ExportInvoices(RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `BatchJobResponse`
- **Error**: `SdkException<ExportInvoicesError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [409] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BatchJobResponse` | `Models/BatchJobResponse.cs` |
| `ExportInvoicesError` | `Errors/ExportInvoicesError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

### ExportProformaInvoices

- **Signature**: `ExportProformaInvoices(RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `BatchJobResponse`
- **Error**: `SdkException<ExportProformaInvoicesError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [409] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BatchJobResponse` | `Models/BatchJobResponse.cs` |
| `ExportProformaInvoicesError` | `Errors/ExportProformaInvoicesError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

### ExportSubscriptions

- **Signature**: `ExportSubscriptions(RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `BatchJobResponse`
- **Error**: `SdkException<ExportSubscriptionsError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [409] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BatchJobResponse` | `Models/BatchJobResponse.cs` |
| `ExportSubscriptionsError` | `Errors/ExportSubscriptionsError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

### ListExportedInvoices

- **Signature**: `ListExportedInvoices(string batchId, int? perPage = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `perPage` = `100`, `page` = `1`
- **Query params (wire ← C#)**: `per_page` ← `perPage`, `page` ← `page`
- **Returns**: `IReadOnlyList<Invoice>`
- **Error**: `SdkException<ListExportedInvoicesError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Invoice` | `Models/Invoice.cs` |
| `ListExportedInvoicesError` | `Errors/ListExportedInvoicesError.cs` |

### ListExportedProformaInvoices

- **Signature**: `ListExportedProformaInvoices(string batchId, int? perPage = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `perPage` = `100`, `page` = `1`
- **Query params (wire ← C#)**: `per_page` ← `perPage`, `page` ← `page`
- **Returns**: `IReadOnlyList<ProformaInvoice>`
- **Error**: `SdkException<ListExportedProformaInvoicesError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ProformaInvoice` | `Models/ProformaInvoice.cs` |
| `ListExportedProformaInvoicesError` | `Errors/ListExportedProformaInvoicesError.cs` |

### ListExportedSubscriptions

- **Signature**: `ListExportedSubscriptions(string batchId, int? perPage = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `perPage` = `100`, `page` = `1`
- **Query params (wire ← C#)**: `per_page` ← `perPage`, `page` ← `page`
- **Returns**: `IReadOnlyList<Subscription>`
- **Error**: `SdkException<ListExportedSubscriptionsError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Subscription` | `Models/Subscription.cs` |
| `ListExportedSubscriptionsError` | `Errors/ListExportedSubscriptionsError.cs` |

### ReadInvoicesExport

- **Signature**: `ReadInvoicesExport(string batchId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `BatchJobResponse`
- **Error**: `SdkException<ReadInvoicesExportError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BatchJobResponse` | `Models/BatchJobResponse.cs` |
| `ReadInvoicesExportError` | `Errors/ReadInvoicesExportError.cs` |

### ReadProformaInvoicesExport

- **Signature**: `ReadProformaInvoicesExport(string batchId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `BatchJobResponse`
- **Error**: `SdkException<ReadProformaInvoicesExportError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BatchJobResponse` | `Models/BatchJobResponse.cs` |
| `ReadProformaInvoicesExportError` | `Errors/ReadProformaInvoicesExportError.cs` |

### ReadSubscriptionsExport

- **Signature**: `ReadSubscriptionsExport(string batchId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `BatchJobResponse`
- **Error**: `SdkException<ReadSubscriptionsExportError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BatchJobResponse` | `Models/BatchJobResponse.cs` |
| `ReadSubscriptionsExportError` | `Errors/ReadSubscriptionsExportError.cs` |

