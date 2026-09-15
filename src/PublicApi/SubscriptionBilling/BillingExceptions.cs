using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class BillingException : Exception
{
    public BillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A Maxio write retry was blocked so its outcome can be reconciled safely.")
    {
    }
}
