using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an operation requires an existing subscription that cannot be found.</summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(string message) : base(message)
    {
    }
}
