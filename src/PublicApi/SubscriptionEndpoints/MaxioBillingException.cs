using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Failure raised by the Maxio billing integration boundary. The message is
/// caller-safe (no provider internals); provider detail is logged, not surfaced.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    /// <summary>
    /// The HTTP status the caller should receive.
    /// </summary>
    public int StatusCode { get; }

    public MaxioBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
