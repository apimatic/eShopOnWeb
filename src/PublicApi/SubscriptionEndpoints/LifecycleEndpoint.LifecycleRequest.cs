namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>One of <c>Pause</c>, <c>Resume</c>, <c>Cancel</c>, or <c>Reactivate</c>.</summary>
    public string Action { get; set; }

    /// <summary>
    /// For <c>Cancel</c> only: <c>Immediate</c> or <c>EndOfPeriod</c>. Defaults to immediate.
    /// </summary>
    public string? CancellationTiming { get; set; }

    public string? Reason { get; set; }

    /// <summary>
    /// Target another customer's subscription. Administrators only; omit to act on your own.
    /// </summary>
    public int? SubscriptionId { get; set; }

    /// <summary>Set from the caller's token.</summary>
    public string UserReference { get; set; }

    /// <summary>Set from the caller's token.</summary>
    public bool IsAdministrator { get; set; }
}
