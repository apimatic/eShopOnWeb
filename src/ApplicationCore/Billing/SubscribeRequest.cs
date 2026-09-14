namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Input to <see cref="Interfaces.ISubscriptionBillingService.SubscribeAsync"/>: everything the
/// billing layer needs to ensure a customer exists and enroll them in a plan.
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// Stable external reference for the shopper (the eShopOnWeb username / email).
    /// Used as the Maxio customer reference so find-or-create is idempotent.
    /// </summary>
    public string UserReference { get; init; } = string.Empty;

    /// <summary>Shopper email, used when a Maxio customer must be created.</summary>
    public string Email { get; init; } = string.Empty;

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>Handle of the plan (Maxio product) to subscribe to.</summary>
    public string PlanHandle { get; init; } = string.Empty;
}
