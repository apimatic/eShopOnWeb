using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription request is malformed (e.g. a missing plan handle) before any call to
/// the billing provider is made.
/// </summary>
public class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message)
    {
    }
}
