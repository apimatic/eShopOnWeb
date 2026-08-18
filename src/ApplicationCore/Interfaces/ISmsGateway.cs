using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider abstraction for the messaging API: sending, scheduling, cancelling, reading and
/// reconciling SMS messages. Implemented against Twilio's Programmable Messaging API.
/// </summary>
public interface ISmsGateway
{
    /// <summary>Sends a message now. Throws <see cref="SmsGatewayException"/> if the provider refuses the request.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be delivered at <paramref name="sendAt"/>.</summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message the provider still holds as scheduled. No-op safe if already sent/canceled at the provider is not guaranteed — caller decides.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current view of a message (its status and delivery outcome).</summary>
    Task<SmsMessageState?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so its text is no longer retrievable, keeping the record.</summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number (<c>Twilio:FromNumber</c>) over the whole range, for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of accepting a message for (immediate or scheduled) delivery.</summary>
public record SmsSendResult(string Sid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current view of a single message.</summary>
public record SmsMessageState(
    string Sid, string? Status, int? ErrorCode, string? ErrorMessage,
    DateTimeOffset? DateSent, string? From, string? To);

/// <summary>A message as it appears in the provider's own log, for reconciliation.</summary>
public record ProviderMessageRecord(
    string Sid, string? Status, string? From, string? To, DateTimeOffset? DateSent, int? ErrorCode);
