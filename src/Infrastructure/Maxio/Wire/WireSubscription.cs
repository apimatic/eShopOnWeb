using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Mirrors maxio-spec/components/schemas/Subscription.yaml (only the fields eShopOnWeb consumes).
internal class WireSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public WireProduct? Product { get; set; }
    public WireCustomer? Customer { get; set; }
}

internal class SubscriptionEnvelope
{
    public WireSubscription? Subscription { get; set; }
}

// Mirrors maxio-spec/components/schemas/Create-Subscription.yaml (only the fields eShopOnWeb sends).
internal class CreateWireSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public string? PaymentCollectionMethod { get; set; }
}

internal class CreateSubscriptionEnvelope
{
    public CreateWireSubscription Subscription { get; set; } = new();
}
