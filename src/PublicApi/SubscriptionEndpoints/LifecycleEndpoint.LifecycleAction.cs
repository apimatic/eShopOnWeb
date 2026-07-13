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
