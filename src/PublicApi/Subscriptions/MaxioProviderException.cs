using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(string safeMessage, HttpStatusCode? statusCode = null, bool indeterminate = false, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        IsIndeterminate = indeterminate;
    }

    public HttpStatusCode? StatusCode { get; }
    public bool IsIndeterminate { get; }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException() : base("Subscription enrollment is still being confirmed. Retry this request shortly.") { }
}
