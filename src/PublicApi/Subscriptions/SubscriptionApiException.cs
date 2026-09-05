using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionApiException : Exception
{
    public SubscriptionApiException(int statusCode, string publicMessage, Exception? innerException = null)
        : base(publicMessage, innerException)
    {
        StatusCode = statusCode;
        PublicMessage = publicMessage;
    }

    public int StatusCode { get; }
    public string PublicMessage { get; }
}
