namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeBody
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public string? Timing { get; set; }
    public long ExpectedProratedAdjustmentInCents { get; set; }
}

public class CommitPlanChangeRequest : BaseRequest
{
    public CommitPlanChangeRequest(long subscriptionId, string customerReference, bool actingAsAdmin, CommitPlanChangeBody body)
    {
        SubscriptionId = subscriptionId;
        CustomerReference = customerReference;
        ActingAsAdmin = actingAsAdmin;
        TargetProductHandle = body.TargetProductHandle;
        Timing = PlanChangeTimingParser.Parse(body.Timing);
        ExpectedProratedAdjustmentInCents = body.ExpectedProratedAdjustmentInCents;
    }

    public long SubscriptionId { get; }
    public string CustomerReference { get; }
    public bool ActingAsAdmin { get; }
    public string TargetProductHandle { get; }
    public Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.PlanChangeTiming Timing { get; }
    public long ExpectedProratedAdjustmentInCents { get; }
}
