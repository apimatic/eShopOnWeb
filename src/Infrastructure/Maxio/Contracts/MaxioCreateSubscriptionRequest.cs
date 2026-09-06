namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Body for POST /subscriptions.json. A repeated submission carrying the same
/// <see cref="UniquenessToken"/> is rejected by Maxio with 409 instead of creating a second subscription.
/// </summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioSubscriptionAttributes Subscription { get; set; } = new();

    public string? UniquenessToken { get; set; }
}
