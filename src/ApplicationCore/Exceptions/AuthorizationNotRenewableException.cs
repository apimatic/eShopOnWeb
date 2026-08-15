using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An order's authorization went stale before fulfilment and can no longer be renewed (the honor
/// period has fully elapsed). The message is written for an operator to act on — typically the
/// shopper must be asked to pay again. Maps to HTTP 409 Conflict.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }

    public AuthorizationNotRenewableException(string message, Exception innerException)
        : base(message, innerException) { }
}
