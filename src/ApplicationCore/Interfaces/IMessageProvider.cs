using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A message as known by the SMS provider (e.g. Twilio).
/// </summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    string? Body,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

/// <summary>
/// Abstraction over the SMS provider's messaging API.
/// </summary>
public interface IMessageProvider
{
    /// <summary>Send a message for immediate delivery.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message, or null if unknown.</summary>
    Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a provider-scheduled message that has not yet been sent.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Permanently redact the body of a message at the provider.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's
    /// configured sending number within the given (UTC) range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
