using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class BillingApiException : Exception
{
    public BillingApiException(int statusCode, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

internal sealed class InvalidMaxioResponseException : Exception
{
    public InvalidMaxioResponseException(string message) : base(message)
    {
    }
}

internal sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A repeated provider write was blocked pending reconciliation.")
    {
    }
}
