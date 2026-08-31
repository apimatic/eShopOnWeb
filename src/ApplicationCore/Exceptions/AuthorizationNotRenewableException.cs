using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The authorization on a paid order has expired and PayPal refused to reauthorize it.
/// The operator must ask the shopper to pay again (a new authorization is required).
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public string? DebugId { get; }

    public AuthorizationNotRenewableException(string message, string? debugId = null) : base(message)
    {
        DebugId = debugId;
    }
}
