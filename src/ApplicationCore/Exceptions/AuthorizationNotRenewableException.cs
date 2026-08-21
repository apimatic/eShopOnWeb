using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at fulfilment when a stale authorization can no longer be renewed (re-authorized),
/// so the capture cannot proceed. The message is phrased so an operator knows the actionable
/// remedy: a new order must be placed and paid.
/// </summary>
public class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message, string? debugId = null)
        : base(message, 409, debugId)
    {
    }
}
