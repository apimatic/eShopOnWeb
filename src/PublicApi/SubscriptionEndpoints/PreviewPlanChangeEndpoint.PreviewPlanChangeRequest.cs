namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeBody
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public string? Timing { get; set; }
}

public class PreviewPlanChangeRequest : BaseRequest
{
    public PreviewPlanChangeRequest(long subscriptionId, string customerReference, bool actingAsAdmin, PreviewPlanChangeBody body)
    {
        SubscriptionId = subscriptionId;
        CustomerReference = customerReference;
        ActingAsAdmin = actingAsAdmin;
        TargetProductHandle = body.TargetProductHandle;
        Timing = PlanChangeTimingParser.Parse(body.Timing);
    }

    public long SubscriptionId { get; }
    public string CustomerReference { get; }
    public bool ActingAsAdmin { get; }
    public string TargetProductHandle { get; }
    public Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.PlanChangeTiming Timing { get; }
}
