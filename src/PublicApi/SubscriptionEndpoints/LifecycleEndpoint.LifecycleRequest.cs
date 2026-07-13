namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>Omit to target the caller's own active subscription. Administrators may target any subscription.</summary>
    public int? SubscriptionId { get; set; }

    public LifecycleAction Action { get; set; }

    /// <summary>Cancel only: true = end of the current billing period, false = immediate.</summary>
    public bool EndOfPeriod { get; set; }

    /// <summary>Cancel only: optional reason recorded on the subscription.</summary>
    public string? Reason { get; set; }

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public string CallerReference { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public bool CallerIsAdmin { get; set; }
}
