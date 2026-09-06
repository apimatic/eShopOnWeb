using BlazorShared;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = null!;
    public string PaymentCollectionMethod { get; set; } = "automatic";

    // Set by endpoint from HTTP context
    public string UserId { get; set; } = null!;
    public string UserEmail { get; set; } = null!;
    public string FirstName { get; set; } = "Customer";
    public string LastName { get; set; } = "Account";
}
