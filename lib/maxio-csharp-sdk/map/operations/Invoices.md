<!-- Generated file — do not edit; regenerated with the SDK. -->

# Invoices — operations

Accessor: `client.Invoices` · Source: `Api/Invoices.cs` · 19 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateInvoice

- **Signature**: `CreateInvoice(int subscriptionId, CreateInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `InvoiceResponse`
- **Error**: `SdkException<CreateInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateInvoiceRequest` | `Models/CreateInvoiceRequest.cs` |
| `InvoiceResponse` | `Models/InvoiceResponse.cs` |
| `CreateInvoiceError` | `Errors/CreateInvoiceError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### DeleteInvoice

- **Signature**: `DeleteInvoice(int subscriptionId, string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeleteInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [404, 422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeleteInvoiceError` | `Errors/DeleteInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### IssueInvoice

- **Signature**: `IssueInvoice(string uid, IssueInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `Invoice`
- **Error**: `SdkException<IssueInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `IssueInvoiceRequest` | `Models/IssueInvoiceRequest.cs` |
| `Invoice` | `Models/Invoice.cs` |
| `IssueInvoiceError` | `Errors/IssueInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListConsolidatedInvoiceSegments

- **Signature**: `ListConsolidatedInvoiceSegments(string invoiceUid, Direction? direction, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `direction` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`
- **Returns**: `ConsolidatedInvoice`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `Direction` | `Models/Enums/Direction.cs` |
| `ConsolidatedInvoice` | `Models/ConsolidatedInvoice.cs` |

### ListCreditNotes

- **Signature**: `ListCreditNotes(int? subscriptionId, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? refunds = false, bool? applications = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `subscriptionId` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`, `lineItems` = `false`, `discounts` = `false`, `taxes` = `false`, `refunds` = `false`, `applications` = `false`
- **Query params (wire ← C#)**: `subscription_id` ← `subscriptionId`, `page` ← `page`, `per_page` ← `perPage`, `line_items` ← `lineItems`, `discounts` ← `discounts`, `taxes` ← `taxes`, `refunds` ← `refunds`, `applications` ← `applications`
- **Returns**: `ListCreditNotesResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ListCreditNotesResponse` | `Models/ListCreditNotesResponse.cs` |

### ListInvoiceEvents

- **Signature**: `ListInvoiceEvents(string? sinceDate, long? sinceId, string? invoiceUid, string? withChangeInvoiceStatus, IReadOnlyList<InvoiceEventType>? eventTypes, int? page = 1, int? perPage = 100, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`sinceDate` … `eventTypes`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `100`
- **Query params (wire ← C#)**: `since_date` ← `sinceDate`, `since_id` ← `sinceId`, `page` ← `page`, `per_page` ← `perPage`, `invoice_uid` ← `invoiceUid`, `with_change_invoice_status` ← `withChangeInvoiceStatus`, `event_types` ← `eventTypes`
- **Returns**: `ListInvoiceEventsResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `InvoiceEventType` | `Models/Enums/InvoiceEventType.cs` |
| `ListInvoiceEventsResponse` | `Models/ListInvoiceEventsResponse.cs` |

### ListInvoices

- **Signature**: `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 14 params (`startDate` … `sort`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`, `lineItems` = `false`, `discounts` = `false`, `taxes` = `false`, `credits` = `false`, `payments` = `false`, `customFields` = `false`, `refunds` = `false`
- **Query params (wire ← C#)**: `start_date` ← `startDate`, `end_date` ← `endDate`, `status` ← `status`, `subscription_id` ← `subscriptionId`, `subscription_group_uid` ← `subscriptionGroupUid`, `consolidation_level` ← `consolidationLevel`, `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`, `line_items` ← `lineItems`, `discounts` ← `discounts`, `taxes` ← `taxes`, `credits` ← `credits`, `payments` ← `payments`, `custom_fields` ← `customFields`, `refunds` ← `refunds`, `date_field` ← `dateField`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `customer_ids` ← `customerIds`, `number` ← `number`, `product_ids` ← `productIds`, `sort` ← `sort`
- **Returns**: `ListInvoicesResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `InvoiceStatus` | `Models/Enums/InvoiceStatus.cs` |
| `Direction` | `Models/Enums/Direction.cs` |
| `InvoiceDateField` | `Models/Enums/InvoiceDateField.cs` |
| `InvoiceSortField` | `Models/Enums/InvoiceSortField.cs` |
| `ListInvoicesResponse` | `Models/ListInvoicesResponse.cs` |

### PreviewCustomerInformationChanges

- **Signature**: `PreviewCustomerInformationChanges(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `CustomerChangesPreviewResponse`
- **Error**: `SdkException<PreviewCustomerInformationChangesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [404, 422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CustomerChangesPreviewResponse` | `Models/CustomerChangesPreviewResponse.cs` |
| `PreviewCustomerInformationChangesError` | `Errors/PreviewCustomerInformationChangesError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadCreditNote

- **Signature**: `ReadCreditNote(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `CreditNote`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CreditNote` | `Models/CreditNote.cs` |

### ReadInvoice

- **Signature**: `ReadInvoice(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `Invoice`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `Invoice` | `Models/Invoice.cs` |

### RecordPaymentForInvoice

- **Signature**: `RecordPaymentForInvoice(string uid, CreateInvoicePaymentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `Invoice`
- **Error**: `SdkException<RecordPaymentForInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateInvoicePaymentRequest` | `Models/CreateInvoicePaymentRequest.cs` |
| `Invoice` | `Models/Invoice.cs` |
| `RecordPaymentForInvoiceError` | `Errors/RecordPaymentForInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### RecordPaymentForMultipleInvoices

- **Signature**: `RecordPaymentForMultipleInvoices(CreateMultiInvoicePaymentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `MultiInvoicePaymentResponse`
- **Error**: `SdkException<RecordPaymentForMultipleInvoicesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateMultiInvoicePaymentRequest` | `Models/CreateMultiInvoicePaymentRequest.cs` |
| `MultiInvoicePaymentResponse` | `Models/MultiInvoicePaymentResponse.cs` |
| `RecordPaymentForMultipleInvoicesError` | `Errors/RecordPaymentForMultipleInvoicesError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### RecordPaymentForSubscription

- **Signature**: `RecordPaymentForSubscription(int subscriptionId, RecordPaymentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `RecordPaymentResponse`
- **Error**: `SdkException<RecordPaymentForSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `RecordPaymentRequest` | `Models/RecordPaymentRequest.cs` |
| `RecordPaymentResponse` | `Models/RecordPaymentResponse.cs` |
| `RecordPaymentForSubscriptionError` | `Errors/RecordPaymentForSubscriptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### RefundInvoice

- **Signature**: `RefundInvoice(string uid, RefundInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `Invoice`
- **Error**: `SdkException<RefundInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `RefundInvoiceRequest` | `Models/RefundInvoiceRequest.cs` |
| `Invoice` | `Models/Invoice.cs` |
| `RefundInvoiceError` | `Errors/RefundInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReopenInvoice

- **Signature**: `ReopenInvoice(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `Invoice`
- **Error**: `SdkException<ReopenInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetObject(out object?)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Invoice` | `Models/Invoice.cs` |
| `ReopenInvoiceError` | `Errors/ReopenInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### SendInvoice

- **Signature**: `SendInvoice(string uid, SendInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<SendInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SendInvoiceRequest` | `Models/SendInvoiceRequest.cs` |
| `SendInvoiceError` | `Errors/SendInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdateCustomerInformation

- **Signature**: `UpdateCustomerInformation(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `Invoice`
- **Error**: `SdkException<UpdateCustomerInformationError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [404, 422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Invoice` | `Models/Invoice.cs` |
| `UpdateCustomerInformationError` | `Errors/UpdateCustomerInformationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdateInvoice

- **Signature**: `UpdateInvoice(int subscriptionId, string uid, UpdateInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `InvoiceResponse`
- **Error**: `SdkException<UpdateInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [404] · `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateInvoiceRequest` | `Models/UpdateInvoiceRequest.cs` |
| `InvoiceResponse` | `Models/InvoiceResponse.cs` |
| `UpdateInvoiceError` | `Errors/UpdateInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### VoidInvoice

- **Signature**: `VoidInvoice(string uid, VoidInvoiceRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `Invoice`
- **Error**: `SdkException<VoidInvoiceError>` — **Case A (typed)**
- **Error accessors**: `TryGetObject(out object?)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `VoidInvoiceRequest` | `Models/VoidInvoiceRequest.cs` |
| `Invoice` | `Models/Invoice.cs` |
| `VoidInvoiceError` | `Errors/VoidInvoiceError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

