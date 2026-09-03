<!-- Generated file — do not edit; regenerated with the SDK. -->

# AdvanceInvoice — operations

Accessor: `client.AdvanceInvoice` · Source: `Api/AdvanceInvoice.cs` · 3 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### IssueAdvanceInvoice

- **Signature**: `IssueAdvanceInvoice(int subscriptionId, IssueAdvanceInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `Invoice`
- **Error**: `SdkException<IssueAdvanceInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `IssueAdvanceInvoiceRequest` | `Models/IssueAdvanceInvoiceRequest.cs` |
| `Invoice` | `Models/Invoice.cs` |
| `IssueAdvanceInvoiceError` | `Errors/IssueAdvanceInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadAdvanceInvoice

- **Signature**: `ReadAdvanceInvoice(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `Invoice`
- **Error**: `SdkException<ReadAdvanceInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Invoice` | `Models/Invoice.cs` |
| `ReadAdvanceInvoiceError` | `Errors/ReadAdvanceInvoiceError.cs` |

### VoidAdvanceInvoice

- **Signature**: `VoidAdvanceInvoice(int subscriptionId, VoidInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `Invoice`
- **Error**: `SdkException<VoidAdvanceInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `VoidInvoiceRequest` | `Models/VoidInvoiceRequest.cs` |
| `Invoice` | `Models/Invoice.cs` |
| `VoidAdvanceInvoiceError` | `Errors/VoidAdvanceInvoiceError.cs` |

