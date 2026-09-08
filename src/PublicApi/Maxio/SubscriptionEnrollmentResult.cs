namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionEnrollmentResult
{
    public SubscriptionDto Subscription { get; set; } = new();
    public bool Created { get; set; }
}
