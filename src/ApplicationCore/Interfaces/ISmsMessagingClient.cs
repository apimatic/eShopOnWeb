using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SmsSendResult
{
    public string MessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class SmsMessageDetails
{
    public string MessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ProviderSmsMessage
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? From { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

/// <summary>
/// The provider's messaging API: send (immediate or provider-scheduled), read,
/// cancel, redact and list messages.
/// </summary>
public interface ISmsMessagingClient
{
    Task<SmsSendResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);
    Task<SmsMessageDetails?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageDetails?> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// All messages the provider recorded as sent from this application's own
    /// configured sending number, across every page.
    /// </summary>
    Task<IReadOnlyList<ProviderSmsMessage>> ListMessagesFromSendingNumberAsync(CancellationToken cancellationToken = default);
}
