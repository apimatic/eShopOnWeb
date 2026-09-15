using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// A notification as reported to a caller: what was sent and what became of it. The destination number
/// and message text are deliberately never included.
/// </summary>
public class NotificationDto
{
    /// <summary>Identifier the operator endpoints act on.</summary>
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's identifier for this message, when one was obtained.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>Where the message got to — the provider's current delivery outcome, or a local marker.</summary>
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }
}
