namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Everything the billing layer needs to enroll an eShopOnWeb user in a plan.
/// The identity always comes from the authenticated caller, never from the request body.
/// </summary>
public class SubscribeRequest
{
    /// <summary>Stable eShopOnWeb identifier for the user; used as the Maxio customer <c>reference</c> so enrollment is idempotent.</summary>
    public required string UserReference { get; init; }

    /// <summary>The user's email (in eShopOnWeb this equals the username).</summary>
    public required string Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public required string PlanHandle { get; init; }
}
