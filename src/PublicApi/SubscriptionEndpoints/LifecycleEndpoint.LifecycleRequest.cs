using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[JsonConverter(typeof(JsonStringEnumConverter))]
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

    /// <summary>Only meaningful for <see cref="LifecycleAction.Cancel"/>: true = end of current billing period, false = immediate.</summary>
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }

    /// <summary>Set by the route handler from the authenticated caller's identity — never bound from the request body.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Set by the route handler from the authenticated caller's role — never bound from the request body.</summary>
    public bool IsAdministrator { get; set; }
}
