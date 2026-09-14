namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected the request as a duplicate submission and the integration could not
/// reconcile it to an existing subscription. The caller should re-read its subscriptions before
/// retrying.
/// </summary>
public class BillingConflictException : BillingException
{
    public BillingConflictException(string message) : base(message)
    {
    }
}
