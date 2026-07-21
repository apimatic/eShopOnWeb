namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>Result of resolving/validating the configured metered component (UC2 precondition check).</summary>
public class BillingComponentInfo
{
    public BillingComponentInfo(string handle, bool isMetered)
    {
        Handle = handle;
        IsMetered = isMetered;
    }

    public string Handle { get; }
    public bool IsMetered { get; }
}
