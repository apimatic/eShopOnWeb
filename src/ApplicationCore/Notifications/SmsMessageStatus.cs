namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>A later read of a message's current delivery outcome from the provider.</summary>
public class SmsMessageStatus
{
    public string? Status { get; init; }

    public int? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}
