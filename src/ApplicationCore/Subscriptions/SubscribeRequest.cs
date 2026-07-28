namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Command carrying the information needed to enroll an eShopOnWeb user in a plan.
/// The identity fields originate from the authenticated caller, never from client input.
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// Stable, unique identifier for the eShopOnWeb user, stored on the Maxio customer as its
    /// <c>reference</c>. This is what makes customer creation idempotent.
    /// </summary>
    public string UserReference { get; init; } = string.Empty;

    /// <summary>Email used when a Maxio customer must be created for this user.</summary>
    public string Email { get; init; } = string.Empty;

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>
    /// Handle of the plan to subscribe to. When null/blank the service falls back to the first
    /// plan available in the configured product family (keeps the flow catalog-agnostic).
    /// </summary>
    public string? PlanHandle { get; init; }
}
