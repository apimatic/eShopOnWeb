using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>A failure of the subscription flow with the HTTP status the API should answer with.</summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }

    public SubscriptionException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
