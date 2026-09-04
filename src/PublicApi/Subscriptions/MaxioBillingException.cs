using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
