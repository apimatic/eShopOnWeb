namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionBody
{
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }
}

public class CancelSubscriptionRequest : BaseRequest
{
    public CancelSubscriptionRequest(long subscriptionId, string customerReference, bool actingAsAdmin, CancelSubscriptionBody body)
    {
        SubscriptionId = subscriptionId;
        CustomerReference = customerReference;
        ActingAsAdmin = actingAsAdmin;
        EndOfPeriod = body.EndOfPeriod;
        Reason = body.Reason;
    }

    public long SubscriptionId { get; }
    public string CustomerReference { get; }
    public bool ActingAsAdmin { get; }
    public bool EndOfPeriod { get; }
    public string? Reason { get; }
}
