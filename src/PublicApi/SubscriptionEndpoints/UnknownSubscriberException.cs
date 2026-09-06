using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Thrown when a token is valid but the user it names cannot be resolved (for example the
/// account was removed, or the in-memory identity store was reset while the token lived on).
/// </summary>
public class UnknownSubscriberException : Exception
{
    public UnknownSubscriberException(string message) : base(message)
    {
    }
}
