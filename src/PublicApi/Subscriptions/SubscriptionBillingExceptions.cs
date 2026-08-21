using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal abstract class SubscriptionBillingException : Exception
{
    protected SubscriptionBillingException(string message) : base(message)
    {
    }
}

internal sealed class ShopperNotFoundException : SubscriptionBillingException
{
    public ShopperNotFoundException() : base("The authenticated shopper no longer exists.")
    {
    }
}

internal sealed class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.")
    {
    }
}

internal sealed class PaymentMethodRequiredException : SubscriptionBillingException
{
    public PaymentMethodRequiredException(string productHandle)
        : base($"Subscription plan '{productHandle}' requires a payment method and cannot be enrolled through this endpoint.")
    {
    }
}
