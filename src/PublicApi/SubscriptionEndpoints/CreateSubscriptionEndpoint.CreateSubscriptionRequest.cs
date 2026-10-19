using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>The authenticated caller; taken from the JWT, never from the body.</summary>
    [JsonIgnore]
    public Subscriber Subscriber { get; set; } = null!;
}
