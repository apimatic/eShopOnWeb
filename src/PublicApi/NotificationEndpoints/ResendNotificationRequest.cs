namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(System.Guid correlationId) : base(correlationId) { }

    public ResendNotificationResponse() { }

    public int NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
}
