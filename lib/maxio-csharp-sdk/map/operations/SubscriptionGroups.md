<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionGroups — operations

Accessor: `client.SubscriptionGroups` · Source: `Api/SubscriptionGroups.cs` · 9 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### AddSubscriptionToGroup

- **Signature**: `AddSubscriptionToGroup(int subscriptionId, AddSubscriptionToAGroup? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionGroupResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `AddSubscriptionToAGroup` | `Models/AddSubscriptionToAGroup.cs` |
| `SubscriptionGroupResponse` | `Models/SubscriptionGroupResponse.cs` |

### CreateSubscriptionGroup

- **Signature**: `CreateSubscriptionGroup(CreateSubscriptionGroupRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionGroupResponse`
- **Error**: `SdkException<CreateSubscriptionGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionGroupCreateErrorResponse1(out SubscriptionGroupCreateErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateSubscriptionGroupRequest` | `Models/CreateSubscriptionGroupRequest.cs` |
| `SubscriptionGroupResponse` | `Models/SubscriptionGroupResponse.cs` |
| `CreateSubscriptionGroupError` | `Errors/CreateSubscriptionGroupError.cs` |
| `SubscriptionGroupCreateErrorResponse1` | `Models/SubscriptionGroupCreateErrorResponse1.cs` |

### DeleteSubscriptionGroup

- **Signature**: `DeleteSubscriptionGroup(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `DeleteSubscriptionGroupResponse`
- **Error**: `SdkException<DeleteSubscriptionGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeleteSubscriptionGroupResponse` | `Models/DeleteSubscriptionGroupResponse.cs` |
| `DeleteSubscriptionGroupError` | `Errors/DeleteSubscriptionGroupError.cs` |

### FindSubscriptionGroup

- **Signature**: `FindSubscriptionGroup(string subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query params (wire ← C#)**: `subscription_id` ← `subscriptionId`
- **Returns**: `FullSubscriptionGroupResponse`
- **Error**: `SdkException<FindSubscriptionGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `FullSubscriptionGroupResponse` | `Models/FullSubscriptionGroupResponse.cs` |
| `FindSubscriptionGroupError` | `Errors/FindSubscriptionGroupError.cs` |

### ListSubscriptionGroups

- **Signature**: `ListSubscriptionGroups(IReadOnlyList<SubscriptionGroupsListInclude>? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `include` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `include` ← `include`
- **Returns**: `ListSubscriptionGroupsResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionGroupsListInclude` | `Models/Enums/SubscriptionGroupsListInclude.cs` |
| `ListSubscriptionGroupsResponse` | `Models/ListSubscriptionGroupsResponse.cs` |

### ReadSubscriptionGroup

- **Signature**: `ReadSubscriptionGroup(string uid, IReadOnlyList<SubscriptionGroupInclude>? include, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `include` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `include` ← `include`
- **Returns**: `FullSubscriptionGroupResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionGroupInclude` | `Models/Enums/SubscriptionGroupInclude.cs` |
| `FullSubscriptionGroupResponse` | `Models/FullSubscriptionGroupResponse.cs` |

### RemoveSubscriptionFromGroup

- **Signature**: `RemoveSubscriptionFromGroup(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RemoveSubscriptionFromGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `RemoveSubscriptionFromGroupError` | `Errors/RemoveSubscriptionFromGroupError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### SignupWithSubscriptionGroup

- **Signature**: `SignupWithSubscriptionGroup(SubscriptionGroupSignupRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionGroupSignupResponse`
- **Error**: `SdkException<SignupWithSubscriptionGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionGroupSignupErrorResponse1(out SubscriptionGroupSignupErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionGroupSignupRequest` | `Models/SubscriptionGroupSignupRequest.cs` |
| `SubscriptionGroupSignupResponse` | `Models/SubscriptionGroupSignupResponse.cs` |
| `SignupWithSubscriptionGroupError` | `Errors/SignupWithSubscriptionGroupError.cs` |
| `SubscriptionGroupSignupErrorResponse1` | `Models/SubscriptionGroupSignupErrorResponse1.cs` |

### UpdateSubscriptionGroupMembers

- **Signature**: `UpdateSubscriptionGroupMembers(string uid, UpdateSubscriptionGroupRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionGroupResponse`
- **Error**: `SdkException<UpdateSubscriptionGroupMembersError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionGroupUpdateErrorResponse1(out SubscriptionGroupUpdateErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateSubscriptionGroupRequest` | `Models/UpdateSubscriptionGroupRequest.cs` |
| `SubscriptionGroupResponse` | `Models/SubscriptionGroupResponse.cs` |
| `UpdateSubscriptionGroupMembersError` | `Errors/UpdateSubscriptionGroupMembersError.cs` |
| `SubscriptionGroupUpdateErrorResponse1` | `Models/SubscriptionGroupUpdateErrorResponse1.cs` |

