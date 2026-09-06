using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.UnitTests.Builders;

public class SubscriptionPlanBuilder
{
    private string _handle = "eshop-pro";
    private long _priceInCents = 29900;
    private bool _requiresPaymentMethod;

    public SubscriptionPlanBuilder WithHandle(string handle)
    {
        _handle = handle;
        return this;
    }

    public SubscriptionPlanBuilder WithPriceInCents(long priceInCents)
    {
        _priceInCents = priceInCents;
        return this;
    }

    public SubscriptionPlanBuilder RequiringPaymentMethod(bool requiresPaymentMethod = true)
    {
        _requiresPaymentMethod = requiresPaymentMethod;
        return this;
    }

    public SubscriptionPlan Build() => new(
        handle: _handle,
        name: "Pro Plan",
        description: null,
        priceInCents: _priceInCents,
        currency: "USD",
        interval: 1,
        intervalUnit: "month",
        requiresPaymentMethod: _requiresPaymentMethod,
        productFamilyHandle: "eshop-subscribe",
        productFamilyName: "eShopSubscribe",
        trialPriceInCents: null,
        trialInterval: null,
        trialIntervalUnit: null,
        expirationInterval: null,
        expirationIntervalUnit: null,
        initialChargeInCents: null,
        taxable: false,
        pricePointName: "Original");
}
