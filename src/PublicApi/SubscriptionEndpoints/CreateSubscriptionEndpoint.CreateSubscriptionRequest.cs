namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class CreateSubscriptionEndpoint
{
    public class CreateSubscriptionApiRequest : BaseRequest
    {
        public string ProductHandle { get; set; } = "";
    }
}
