using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>The provider's acknowledgement of a message it accepted (an immediate send or a scheduled one).</summary>
public class SmsSendResult
{
    public SmsSendResult(string providerMessageSid, string? status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledSendAt = null)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
    }

    /// <summary>The provider's identifier for the message (Twilio message SID).</summary>
    public string ProviderMessageSid { get; }

    /// <summary>The delivery status the provider reported at hand-off (e.g. queued, accepted, scheduled).</summary>
    public string? Status { get; }

    public int? ErrorCode { get; }

    public string? ErrorMessage { get; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; }
}
