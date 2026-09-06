namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A concurrent or replayed subscribe was detected by the billing system's duplicate prevention and
/// its outcome could not be resolved to a subscription. The caller should retry the read shortly.
/// </summary>
public class SubscriptionConflictException : BillingException
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
