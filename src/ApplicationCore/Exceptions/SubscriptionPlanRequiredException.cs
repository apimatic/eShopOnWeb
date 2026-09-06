namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscribe request named no plan and the deployment configures no default one.
/// </summary>
/// <remarks>
/// Deliberately an error rather than a guess: picking a plan on the shopper's behalf would commit
/// them to a recurring charge they never chose.
/// </remarks>
public class SubscriptionPlanRequiredException : BillingException
{
    public SubscriptionPlanRequiredException(string availablePlans)
        : base($"A plan handle is required because no default plan is configured. Available plans: {availablePlans}.")
    {
    }
}
