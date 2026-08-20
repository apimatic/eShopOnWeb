namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public sealed class SubscriptionPlanDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequiresPaymentMethod { get; init; }
}
