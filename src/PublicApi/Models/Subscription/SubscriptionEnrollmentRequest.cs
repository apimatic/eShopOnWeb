namespace Microsoft.eShopWeb.PublicApi.Models.Subscription;

public class SubscriptionEnrollmentRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
