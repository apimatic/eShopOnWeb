using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class SubscriptionEnvelope
{
    public SubscriptionWire? Subscription { get; set; }
}

internal class SubscriptionWire
{
    public long Id { get; set; }
    public string? State { get; set; }
    public CustomerWire? Customer { get; set; }
    public ProductWire? Product { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}

internal class CreateSubscriptionRequestEnvelope
{
    public CreateSubscriptionRequestWire Subscription { get; set; } = new();
}

internal class CreateSubscriptionRequestWire
{
    public string ProductHandle { get; set; } = string.Empty;
    public long CustomerId { get; set; }

    /// <summary>
    /// Maxio defaults new subscriptions to "automatic" collection, which tries to auto-charge a
    /// card immediately and fails with no payment method on file - even on plans configured with
    /// "require a payment method" turned off. "invoice" skips that charge attempt so subscribing
    /// works without card capture, matching this integration's no-payment-method-required plans.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "invoice";
}
