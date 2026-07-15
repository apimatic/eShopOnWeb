namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public enum LifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public LifecycleAction Action { get; set; }

    /// <summary>Only meaningful when <see cref="Action"/> is <see cref="LifecycleAction.Cancel"/>.</summary>
    public bool EndOfPeriod { get; set; }

    public string? Reason { get; set; }

    /// <summary>Server-assigned from the authenticated principal — never bound from client input.</summary>
    public string? UserId { get; set; }

    /// <summary>Server-assigned from the authenticated principal's role — never bound from client input.</summary>
    public bool ActingAsAdmin { get; set; }
}
