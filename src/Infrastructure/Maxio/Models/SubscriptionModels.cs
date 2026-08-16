using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models mirroring components/schemas/Subscription.yaml, Create-Subscription.yaml
// and their { "subscription": ... } envelopes.

/// <summary>Envelope for a single subscription, per Subscription-Response.yaml.</summary>
public class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Subset of Subscription.yaml used to confirm and list subscriptions.</summary>
public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Envelope for Create-Subscription-Request.yaml.</summary>
public class CreateSubscriptionEnvelope
{
    public CreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Fields we send when creating a subscription, per Create-Subscription.yaml. We
/// identify the plan by <see cref="ProductHandle"/> and the customer by
/// <see cref="CustomerId"/>. Remittance collection lets the subscription activate
/// without a stored payment method.
/// </summary>
public class CreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// App-owned reference for the subscription. Unique per site in Maxio, so a
    /// deterministic value makes subscription creation idempotent (a duplicate request
    /// is rejected rather than creating a second subscription).
    /// </summary>
    public string? Reference { get; set; }
}
