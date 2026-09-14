namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A command to enroll an eShopOnWeb user in a subscription plan. The user's identity
/// (<see cref="UserReference"/>) originates from the authenticated caller, never the request body.
/// </summary>
public record SubscribeRequest
{
    /// <summary>
    /// Stable, unique identity of the eShopOnWeb user. Used as the billing customer's
    /// external reference so the mapping is idempotent across calls.
    /// </summary>
    public required string UserReference { get; init; }

    /// <summary>Email address of the eShopOnWeb user.</summary>
    public required string Email { get; init; }

    /// <summary>Optional given name for the billing customer record.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional family name for the billing customer record.</summary>
    public string? LastName { get; init; }

    /// <summary>Handle of the plan to subscribe the user to.</summary>
    public required string PlanHandle { get; init; }
}
