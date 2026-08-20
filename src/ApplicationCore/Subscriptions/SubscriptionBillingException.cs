using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        string message,
        int? providerStatusCode = null,
        bool outcomeUnknown = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public int? ProviderStatusCode { get; }
    public bool OutcomeUnknown { get; }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message, int statusCode = 400)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

