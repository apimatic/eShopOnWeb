namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>A fresh read of a message's delivery outcome from the provider.</summary>
public class SmsStatusResult
{
    public required string Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
