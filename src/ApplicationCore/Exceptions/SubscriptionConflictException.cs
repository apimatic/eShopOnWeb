namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The shopper already has a different subscription, or another subscribe request for them is still in flight.
/// </summary>
public class SubscriptionConflictException : DuplicateException
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
