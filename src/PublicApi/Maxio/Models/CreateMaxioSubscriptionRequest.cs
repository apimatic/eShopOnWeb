namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>Request body for POST /subscriptions.json.</summary>
public class CreateMaxioSubscriptionRequest
{
    public MaxioSubscriptionAttributes Subscription { get; set; } = new();
}

public class MaxioSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? UniquenessToken { get; set; }
}
