using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Json;

internal sealed class SubscriptionEnvelope
{
    public SubscriptionJson? Subscription { get; set; }
}

internal sealed class SubscriptionJson
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public int? ProductPriceInCents { get; set; }
    public CustomerJson? Customer { get; set; }
    public ProductJson? Product { get; set; }
}

internal sealed class SubscriptionCreateEnvelope
{
    public SubscriptionCreatePayload Subscription { get; set; } = new();
}

internal sealed class SubscriptionCreatePayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
}
