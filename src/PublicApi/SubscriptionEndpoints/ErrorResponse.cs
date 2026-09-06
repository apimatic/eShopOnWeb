namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}
