using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class InvalidSubscriptionRequestException : Exception
{
    public InvalidSubscriptionRequestException(string message)
        : base(message)
    {
    }
}
