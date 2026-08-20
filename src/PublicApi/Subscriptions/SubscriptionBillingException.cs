using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public enum SubscriptionBillingError
{
    InvalidRequest,
    NotFound,
    Conflict,
    ProviderRejected,
    ProviderUnavailable,
    ProviderContract,
    Indeterminate
}

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        SubscriptionBillingError error,
        string safeMessage,
        Exception? innerException = null,
        int? providerStatusCode = null)
        : base(safeMessage, innerException)
    {
        Error = error;
        ProviderStatusCode = providerStatusCode;
    }

    public SubscriptionBillingError Error { get; }
    public int? ProviderStatusCode { get; }
}
