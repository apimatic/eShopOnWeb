namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Request to enroll an eShopOnWeb shopper onto a plan.
/// </summary>
public class SubscribeCommand
{
    public SubscribeCommand(string userName, string planHandle)
    {
        UserName = userName;
        PlanHandle = planHandle;
    }

    /// <summary>The eShopOnWeb user name (email) taken from the caller's token. Identifies the shopper.</summary>
    public string UserName { get; }

    /// <summary>Handle of the plan to subscribe to. When empty the configured default plan is used.</summary>
    public string PlanHandle { get; }

    /// <summary>Optional given name used when the shopper has to be created in the billing system.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional family name used when the shopper has to be created in the billing system.</summary>
    public string? LastName { get; init; }

    /// <summary>Optional organization recorded against the billing customer.</summary>
    public string? Organization { get; init; }
}
