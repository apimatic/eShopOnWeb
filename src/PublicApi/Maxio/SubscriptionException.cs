using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A failure of the subscription flow, already translated to a caller-safe message and HTTP status.</summary>
public class SubscriptionException : Exception
{
    public SubscriptionException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
