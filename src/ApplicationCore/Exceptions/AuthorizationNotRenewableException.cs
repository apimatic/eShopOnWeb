using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A stale PayPal authorization could not be renewed. The message is operator-actionable:
/// the shopper must be charged again through a new authorization.
/// </summary>
public class AuthorizationNotRenewableException : PaymentStateConflictException
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}
