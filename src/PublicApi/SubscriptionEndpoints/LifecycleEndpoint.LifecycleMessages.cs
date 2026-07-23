using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>Taken from the route, never from the request body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>
    /// One of <c>Pause</c>, <c>Resume</c>, <c>Cancel</c>, <c>CancelAtEndOfPeriod</c> or <c>Reactivate</c>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubscriptionLifecycleAction Action { get; set; }

    public string? Reason { get; set; }
}

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LifecycleResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
