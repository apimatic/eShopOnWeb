namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system recognised the request as a replay of one it is still processing, and we could
/// not find the resulting subscription to hand back. Retrying shortly will return the subscription.
/// </summary>
public class DuplicateSubscribeRequestException : BillingException
{
    public DuplicateSubscribeRequestException(string planHandle)
        : base($"A subscribe request for plan '{planHandle}' is already in flight for this user. " +
               "Retry in a moment to read back the resulting subscription.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
