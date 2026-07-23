using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>One of <c>pause</c>, <c>resume</c>, <c>cancel</c>, <c>reactivate</c>.</summary>
    public string Action { get; set; }

    /// <summary>For <c>cancel</c>: defer the cancellation to the end of the current period.</summary>
    public bool CancelAtEndOfPeriod { get; set; }

    /// <summary>For <c>pause</c>: schedule an automatic resume.</summary>
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }

    /// <summary>Optional reason recorded with the transition.</summary>
    public string Reason { get; set; }

    /// <summary>Administrators only: act on another user's subscription.</summary>
    public string UserReference { get; set; }
}
