using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException()
        : base("Subscription billing is not configured.") { }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException()
        : base("The requested subscription plan is not available.") { }
}

public sealed class BillingIdentityException : Exception
{
    public BillingIdentityException()
        : base("The authenticated user could not be resolved for billing.") { }
}

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(string message, int? providerStatusCode = null,
        bool outcomeUnknown = false, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public int? ProviderStatusCode { get; }
    public bool OutcomeUnknown { get; }
}

internal sealed class MaxioWriteReplayBlockedException : Exception { }
