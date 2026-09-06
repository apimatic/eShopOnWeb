namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Request to enroll an eShopOnWeb user in a plan.
/// </summary>
public class SubscribeCommand
{
    /// <summary>The authenticated eShopOnWeb user name, taken from the caller token.</summary>
    public required string UserName { get; init; }

    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public required string PlanHandle { get; init; }

    /// <summary>Optional. Used only when the billing customer has to be created.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional. Used only when the billing customer has to be created.</summary>
    public string? LastName { get; init; }

    /// <summary>
    /// Optional caller-supplied key. When supplied it becomes the Maxio subscription
    /// reference, so a retry of the same logical request cannot create a second
    /// subscription even when the retry lands on a different application instance.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
