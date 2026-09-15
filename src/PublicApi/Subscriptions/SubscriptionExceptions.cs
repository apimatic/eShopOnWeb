using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message)
    {
    }
}

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(string message, HttpStatusCode? providerStatus = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatus = providerStatus;
    }

    public HttpStatusCode? ProviderStatus { get; }
}
