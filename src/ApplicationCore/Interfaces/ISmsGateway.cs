using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging side of the SMS provider: sending, scheduling, reading, cancelling,
/// redacting and reconciling messages. Implementations talk to the provider's messaging
/// API (the one governed by the <c>Twilio:BaseUrl</c> override).
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's own configured sending number (<c>Twilio:FromNumber</c>), in E.164.</summary>
    string FromNumber { get; }

    /// <summary>Sends a message immediately from the configured sending number. Throws on provider rejection.</summary>
    Task<SmsMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a message to be sent by the provider at <paramref name="sendAt"/> - the provider holds
    /// and sends it, not this application. Throws on provider rejection.
    /// </summary>
    Task<SmsMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads a message back from the provider to obtain its current delivery outcome.</summary>
    Task<SmsMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never reaches the recipient.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the text content of a message at the provider so it can no longer be retrieved there,
    /// while the message record and its delivery outcome survive.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number within the
    /// given range, asking the provider to filter by that number rather than filtering a wider answer
    /// after the fact. Covers the whole range (following pagination).
    /// </summary>
    Task<IReadOnlyList<SmsMessage>> ListMessagesFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A snapshot of a provider message resource.</summary>
public record SmsMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    DateTimeOffset? DateSent);
