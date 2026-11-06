using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A failure of the subscription capability, already translated to the status the caller should see.
/// Handled by <see cref="Middleware.ExceptionMiddleware"/>.
/// </summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }

    public SubscriptionException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
