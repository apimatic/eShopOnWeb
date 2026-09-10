namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll an eShopOnWeb user in a subscription plan. The identity is taken from the
/// authenticated caller (never from the request body), so a caller can only subscribe itself.
/// </summary>
public sealed class SubscriptionEnrollmentRequest
{
    /// <summary>
    /// Stable identity of the eShopOnWeb user (the JWT name/username). Used to derive the
    /// deterministic Maxio customer and subscription references that make enrollment idempotent.
    /// </summary>
    public required string UserIdentity { get; init; }

    /// <summary>The user's email, used when a Maxio customer must be created.</summary>
    public string? Email { get; init; }

    /// <summary>
    /// Handle of the plan to subscribe to. When null/empty the first plan in the configured
    /// product family is used. Must belong to the configured family; otherwise the request is rejected.
    /// </summary>
    public string? PlanHandle { get; init; }
}
