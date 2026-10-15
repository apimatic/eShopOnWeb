namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A user's subscription to one plan. Key: (<see cref="UserName"/>, <see cref="PlanHandle"/>).</summary>
public class UserSubscription : MaxioClaim
{
    public string UserName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
}
