using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>A failure of the subscription capability, carrying the HTTP status the API should answer with.</summary>
public class SubscriptionException : Exception
{
    public SubscriptionException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
