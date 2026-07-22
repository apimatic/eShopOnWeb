using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>Pause, Resume, CancelImmediately, CancelAtPeriodEnd or Reactivate.</summary>
    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>Optional reason recorded with the transition.</summary>
    public string? Reason { get; set; }

    /// <summary>Bound from the route, never from the body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Resolved from the bearer token; null for an administrator.</summary>
    public string? OwnerReference { get; set; }

    /// <summary>False when the request carried no usable identity.</summary>
    public bool IsAuthenticated { get; set; }
}
