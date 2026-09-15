using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("A subscription request for this plan is already being processed.")
    {
    }
}

internal sealed class MaxioWriteRetryBlockedException : Exception
{
}
