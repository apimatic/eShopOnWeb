using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        SubscriptionBillingError error,
        string message,
        HttpStatusCode? providerStatus = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        ProviderStatus = providerStatus;
    }

    public SubscriptionBillingError Error { get; }
    public HttpStatusCode? ProviderStatus { get; }
}

public enum SubscriptionBillingError
{
    InvalidRequest,
    NotFound,
    Conflict,
    ProviderUnavailable,
    UnknownWriteOutcome,
    InvalidProviderResponse
}
