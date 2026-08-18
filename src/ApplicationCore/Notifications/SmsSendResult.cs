namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>What the provider returned when a message was created (sent or scheduled).</summary>
public class SmsSendResult
{
    /// <summary>The provider's message identifier (SID).</summary>
    public string? Sid { get; init; }

    /// <summary>The provider's initial status (raw wire value).</summary>
    public string? Status { get; init; }

    public int? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}
