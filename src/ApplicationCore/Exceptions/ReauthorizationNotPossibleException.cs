namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a stale authorization can no longer be renewed (PayPal has no typed signal for
/// this — see paypal-plan.md UNVERIFIED note). The message is written for an operator to act
/// on: the only remedy is placing a new order to obtain a fresh authorization.
/// </summary>
public class ReauthorizationNotPossibleException : PaymentGatewayException
{
    public ReauthorizationNotPossibleException(string authorizationId, string reason)
        : base($"Authorization '{authorizationId}' can no longer be renewed ({reason}). " +
               "This order cannot be fulfilled from its current authorization — place a new order " +
               "to obtain a fresh authorization before fulfilling.")
    {
    }
}
