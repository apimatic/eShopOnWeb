namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed class SmsMessageResult
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? Body { get; init; }
    public string? DateCreated { get; init; }
    public string? DateSent { get; init; }
    public string? DateUpdated { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MessagingServiceSid { get; init; }
}
