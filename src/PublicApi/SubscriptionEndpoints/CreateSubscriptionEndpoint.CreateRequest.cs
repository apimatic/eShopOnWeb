namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class CreateSubscriptionEndpoint
{
    public class CreateRequest : BaseRequest
    {
        public string ProductHandle { get; set; } = string.Empty;
    }
}
