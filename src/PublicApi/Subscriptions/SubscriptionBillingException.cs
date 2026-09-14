using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(int statusCode, string safeMessage, Exception? innerException = null, bool outcomeUnknown = false)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public int StatusCode { get; }
    public bool OutcomeUnknown { get; }
}
