using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    [Required]
    public int SubscriptionId { get; set; }

    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>
    /// Only meaningful for <see cref="SubscriptionLifecycleAction.Cancel"/>: defer the cancellation
    /// to the end of the current billing period instead of cancelling immediately.
    /// </summary>
    public bool CancelAtEndOfPeriod { get; set; }

    public string? Reason { get; set; }
}
