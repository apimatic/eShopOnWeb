using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
