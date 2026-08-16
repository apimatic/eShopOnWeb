namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A stale authorization could not be renewed before fulfilment and can no longer be
/// reauthorized. The order cannot be captured against the original hold; an operator must have
/// the shopper pay for the order again (a fresh authorization) before it can be fulfilled.
/// </summary>
public class ReauthorizationNotAllowedException : PaymentException
{
    public ReauthorizationNotAllowedException(string message) : base(message)
    {
    }
}
