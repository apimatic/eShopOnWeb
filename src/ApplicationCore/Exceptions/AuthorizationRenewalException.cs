using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A stale PayPal authorization could not be renewed, so the order cannot be fulfilled
/// against it. The message tells the operator what to do next.
/// </summary>
public class AuthorizationRenewalException : Exception
{
    public AuthorizationRenewalException(string message) : base(message)
    {
    }
}
