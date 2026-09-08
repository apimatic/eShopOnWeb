namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Details needed to enroll a shopper on a subscription plan.
/// </summary>
public sealed class SubscriptionSignup
{
    /// <summary>
    /// The eShopOnWeb user id, used as the Maxio customer reference (idempotency key).
    /// </summary>
    public string CustomerReference { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// The API handle of the plan (Maxio product) to subscribe to.
    /// </summary>
    public string PlanHandle { get; init; } = string.Empty;
}
