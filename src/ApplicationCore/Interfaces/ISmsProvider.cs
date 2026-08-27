using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider's messaging API.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Sends a message immediately.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a future time.</summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Gets the provider's current record for a message, or null if unknown.</summary>
    Task<SmsMessageInfo?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that has been queued with the provider but not yet sent.</summary>
    Task<bool> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts a message's text at the provider while keeping the message record itself.</summary>
    Task<bool> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the given range. Covers the whole range (all pages).
    /// </summary>
    Task<IReadOnlyList<SmsMessageInfo>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class SmsSendResult
{
    public bool Success { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ErrorMessage { get; set; }

    public static SmsSendResult Accepted(string sid, string status) =>
        new SmsSendResult { Success = true, ProviderMessageSid = sid, ProviderStatus = status };

    public static SmsSendResult Failed(string errorMessage) =>
        new SmsSendResult { Success = false, ErrorMessage = errorMessage };
}

public class SmsMessageInfo
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
