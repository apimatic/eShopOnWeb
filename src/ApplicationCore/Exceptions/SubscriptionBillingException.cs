using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' is not available.")
    {
    }
}

public sealed class SubscriptionPaymentMethodRequiredException : SubscriptionBillingException
{
    public SubscriptionPaymentMethodRequiredException(string productHandle)
        : base($"Subscription plan '{productHandle}' requires a payment method and cannot be enrolled through this endpoint.")
    {
    }
}

public sealed class SubscriptionCreationInProgressException : SubscriptionBillingException
{
    public SubscriptionCreationInProgressException()
        : base("Subscription creation is already in progress. Retry this request shortly.")
    {
    }
}

public sealed class SubscriptionBillingUnavailableException : SubscriptionBillingException
{
    public SubscriptionBillingUnavailableException(string message)
        : base(message)
    {
    }

    public SubscriptionBillingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
