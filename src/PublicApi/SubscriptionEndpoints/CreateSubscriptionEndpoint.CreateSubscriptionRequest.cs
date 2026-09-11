namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? CustomerFirstName { get; set; }
    public string? CustomerLastName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? SubscriptionReference { get; set; }
}

public class CreateSubscriptionRequestBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public string? CustomerReference { get; set; }
    public string? CustomerFirstName { get; set; }
    public string? CustomerLastName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? SubscriptionReference { get; set; }
}
