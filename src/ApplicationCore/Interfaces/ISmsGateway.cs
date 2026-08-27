using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's SMS API. Implementations must never throw for a
/// provider-side rejection of a send; they report it through <see cref="SmsSendResult"/>.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Sends a message immediately, or queues it with the provider for
    /// <paramref name="sendAtUtc"/> when supplied (provider-side scheduling).
    /// </summary>
    Task<SmsSendResult> SendAsync(string to, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the provider's current view of a message, or null if it cannot be retrieved.</summary>
    Task<SmsMessageStatus?> GetMessageStatusAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is still scheduled with the provider.</summary>
    Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the body of a message at the provider so the text is no longer retrievable there.</summary>
    Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's
    /// configured sending number whose sent date falls within [fromUtc, toUtc].
    /// </summary>
    Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

public record SmsSendResult(bool Success, string? MessageSid, string? Status, int? ErrorCode, string? ErrorMessage);

public record SmsMessageStatus(string Status, int? ErrorCode, string? ErrorMessage);

public record SmsMessageRecord(string MessageSid, string? To, string? Status, DateTimeOffset? DateSent, DateTimeOffset? DateCreated);
