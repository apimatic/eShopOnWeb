using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal sealed class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioSubscriptionProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscriptionProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public int PriceInCents { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = "invoice";
}
