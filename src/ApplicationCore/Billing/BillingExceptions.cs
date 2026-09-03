using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingProviderFailure
{
    Rejected,
    RateLimited,
    Unavailable,
    InvalidResponse,
    UnknownOutcome
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(
        string safeMessage,
        BillingProviderFailure failure,
        HttpStatusCode? providerStatusCode = null,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
        Failure = failure;
        ProviderStatusCode = providerStatusCode;
    }

    public string SafeMessage { get; }

    public BillingProviderFailure Failure { get; }

    public HttpStatusCode? ProviderStatusCode { get; }
}

public sealed class BillingPlanNotFoundException : Exception
{
    public BillingPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
    }
}

public sealed class SubscriptionProvisioningInProgressException : Exception
{
    public SubscriptionProvisioningInProgressException()
        : base("This subscription is already being provisioned. Retry shortly to retrieve it.")
    {
    }
}
