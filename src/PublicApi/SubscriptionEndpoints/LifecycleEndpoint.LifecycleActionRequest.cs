namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public enum LifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}

public class LifecycleActionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>One of "Pause", "Resume", "Cancel", "Reactivate" (case-insensitive). A plain string, not an
    /// enum, since the default System.Text.Json minimal-API body binder does not accept string enum values.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Only meaningful when <see cref="Action"/> is <see cref="LifecycleAction.Cancel"/>.</summary>
    public bool EndOfPeriod { get; set; }

    /// <summary>Overwritten server-side from the authenticated principal — never trust a client-supplied value.</summary>
    public string UserReference { get; set; } = string.Empty;

    /// <summary>Overwritten server-side from the caller's role — never trust a client-supplied value.</summary>
    public bool IsAdmin { get; set; }
}
