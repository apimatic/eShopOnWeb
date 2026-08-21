namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class ProviderMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public string? Direction { get; init; }
    public string? MessagingServiceSid { get; init; }
}
