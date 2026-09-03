<!-- Generated file — do not edit; regenerated with the SDK. -->

# BillingPortal — operations

Accessor: `client.BillingPortal` · Source: `Api/BillingPortal.cs` · 4 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### EnableBillingPortalForCustomer

- **Signature**: `EnableBillingPortalForCustomer(int customerId, AutoInvite? autoInvite, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `autoInvite` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `auto_invite` ← `autoInvite`
- **Returns**: `CustomerResponse`
- **Error**: `SdkException<EnableBillingPortalForCustomerError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `AutoInvite` | `Models/Enums/AutoInvite.cs` |
| `CustomerResponse` | `Models/CustomerResponse.cs` |
| `EnableBillingPortalForCustomerError` | `Errors/EnableBillingPortalForCustomerError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadBillingPortalLink

- **Signature**: `ReadBillingPortalLink(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PortalManagementLink`
- **Error**: `SdkException<ReadBillingPortalLinkError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetTooManyManagementLinkRequestsError1(out TooManyManagementLinkRequestsError1)` [429] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PortalManagementLink` | `Models/PortalManagementLink.cs` |
| `ReadBillingPortalLinkError` | `Errors/ReadBillingPortalLinkError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |
| `TooManyManagementLinkRequestsError1` | `Models/TooManyManagementLinkRequestsError1.cs` |

### ResendBillingPortalInvitation

- **Signature**: `ResendBillingPortalInvitation(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ResentInvitation`
- **Error**: `SdkException<ResendBillingPortalInvitationError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResentInvitation` | `Models/ResentInvitation.cs` |
| `ResendBillingPortalInvitationError` | `Errors/ResendBillingPortalInvitationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### RevokeBillingPortalAccess

- **Signature**: `RevokeBillingPortalAccess(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `RevokedInvitation`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `RevokedInvitation` | `Models/RevokedInvitation.cs` |

