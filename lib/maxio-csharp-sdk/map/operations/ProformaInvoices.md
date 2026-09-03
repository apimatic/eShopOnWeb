<!-- Generated file — do not edit; regenerated with the SDK. -->

# ProformaInvoices — operations

Accessor: `client.ProformaInvoices` · Source: `Api/ProformaInvoices.cs` · 10 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateConsolidatedProformaInvoice

- **Signature**: `CreateConsolidatedProformaInvoice(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<CreateConsolidatedProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateConsolidatedProformaInvoiceError` | `Errors/CreateConsolidatedProformaInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateProformaInvoice

- **Signature**: `CreateProformaInvoice(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProformaInvoice`
- **Error**: `SdkException<CreateProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ProformaInvoice` | `Models/ProformaInvoice.cs` |
| `CreateProformaInvoiceError` | `Errors/CreateProformaInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateSignupProformaInvoice

- **Signature**: `CreateSignupProformaInvoice(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProformaInvoice`
- **Error**: `SdkException<CreateSignupProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetProformaBadRequestErrorResponse1(out ProformaBadRequestErrorResponse1)` [400] · `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateSubscriptionRequest` | `Models/CreateSubscriptionRequest.cs` |
| `ProformaInvoice` | `Models/ProformaInvoice.cs` |
| `CreateSignupProformaInvoiceError` | `Errors/CreateSignupProformaInvoiceError.cs` |
| `ProformaBadRequestErrorResponse1` | `Models/ProformaBadRequestErrorResponse1.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### DeliverProformaInvoice

- **Signature**: `DeliverProformaInvoice(string proformaInvoiceUid, DeliverProformaInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProformaInvoice`
- **Error**: `SdkException<DeliverProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeliverProformaInvoiceRequest` | `Models/DeliverProformaInvoiceRequest.cs` |
| `ProformaInvoice` | `Models/ProformaInvoice.cs` |
| `DeliverProformaInvoiceError` | `Errors/DeliverProformaInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListProformaInvoices

- **Signature**: `ListProformaInvoices(int subscriptionId, string? startDate, string? endDate, ProformaInvoiceStatus? status, Direction? direction, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`startDate` … `direction`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`, `lineItems` = `false`, `discounts` = `false`, `taxes` = `false`, `credits` = `false`, `payments` = `false`, `customFields` = `false`
- **Query params (wire ← C#)**: `start_date` ← `startDate`, `end_date` ← `endDate`, `status` ← `status`, `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`, `line_items` ← `lineItems`, `discounts` ← `discounts`, `taxes` ← `taxes`, `credits` ← `credits`, `payments` ← `payments`, `custom_fields` ← `customFields`
- **Returns**: `ListProformaInvoicesResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProformaInvoiceStatus` | `Models/Enums/ProformaInvoiceStatus.cs` |
| `Direction` | `Models/Enums/Direction.cs` |
| `ListProformaInvoicesResponse` | `Models/ListProformaInvoicesResponse.cs` |

### ListSubscriptionGroupProformaInvoices

- **Signature**: `ListSubscriptionGroupProformaInvoices(string uid, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `lineItems` = `false`, `discounts` = `false`, `taxes` = `false`, `credits` = `false`, `payments` = `false`, `customFields` = `false`
- **Query params (wire ← C#)**: `line_items` ← `lineItems`, `discounts` ← `discounts`, `taxes` ← `taxes`, `credits` ← `credits`, `payments` ← `payments`, `custom_fields` ← `customFields`
- **Returns**: `ListProformaInvoicesResponse`
- **Error**: `SdkException<ListSubscriptionGroupProformaInvoicesError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ListProformaInvoicesResponse` | `Models/ListProformaInvoicesResponse.cs` |
| `ListSubscriptionGroupProformaInvoicesError` | `Errors/ListSubscriptionGroupProformaInvoicesError.cs` |

### PreviewProformaInvoice

- **Signature**: `PreviewProformaInvoice(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProformaInvoice`
- **Error**: `SdkException<PreviewProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ProformaInvoice` | `Models/ProformaInvoice.cs` |
| `PreviewProformaInvoiceError` | `Errors/PreviewProformaInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### PreviewSignupProformaInvoice

- **Signature**: `PreviewSignupProformaInvoice(CreateSignupProformaPreviewInclude? include, CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `include` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `include` ← `include`
- **Returns**: `SignupProformaPreviewResponse`
- **Error**: `SdkException<PreviewSignupProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetProformaBadRequestErrorResponse1(out ProformaBadRequestErrorResponse1)` [400] · `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateSignupProformaPreviewInclude` | `Models/Enums/CreateSignupProformaPreviewInclude.cs` |
| `CreateSubscriptionRequest` | `Models/CreateSubscriptionRequest.cs` |
| `SignupProformaPreviewResponse` | `Models/SignupProformaPreviewResponse.cs` |
| `PreviewSignupProformaInvoiceError` | `Errors/PreviewSignupProformaInvoiceError.cs` |
| `ProformaBadRequestErrorResponse1` | `Models/ProformaBadRequestErrorResponse1.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### ReadProformaInvoice

- **Signature**: `ReadProformaInvoice(string proformaInvoiceUid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProformaInvoice`
- **Error**: `SdkException<ReadProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ProformaInvoice` | `Models/ProformaInvoice.cs` |
| `ReadProformaInvoiceError` | `Errors/ReadProformaInvoiceError.cs` |

### VoidProformaInvoice

- **Signature**: `VoidProformaInvoice(string proformaInvoiceUid, VoidInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProformaInvoice`
- **Error**: `SdkException<VoidProformaInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `VoidInvoiceRequest` | `Models/VoidInvoiceRequest.cs` |
| `ProformaInvoice` | `Models/ProformaInvoice.cs` |
| `VoidProformaInvoiceError` | `Errors/VoidProformaInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

