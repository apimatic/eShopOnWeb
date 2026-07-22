using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The signed in user asked to act on a subscription that belongs to somebody else and is not an
/// administrator. The message deliberately reveals nothing about the subscription itself.
/// </summary>
public class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException(string message) : base(message)
    {
    }
}
