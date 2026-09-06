namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The details used to enroll an existing billing customer on a plan.
/// </summary>
public class NewSubscription
{
    public int CustomerId { get; init; }

    /// <summary>The handle of the plan (Maxio product) to enroll on.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>
    /// A long, random, caller-owned value. The billing system rejects a second create carrying the
    /// same token within its duplicate-prevention window, which makes a retry safe.
    /// </summary>
    public string? UniquenessToken { get; init; }
}
