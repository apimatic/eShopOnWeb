namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>"Immediate" or "EndOfPeriod" — see <see cref="ApplicationCore.Entities.SubscriptionAggregate.CancellationTiming"/>.</summary>
    public string Timing { get; set; } = string.Empty;

    public string? Reason { get; set; }
    public string? OwnerUserId { get; set; }
}
