using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A read/write view of a single provider Message, projected from the fields the Twilio
/// messaging API (api.v2010.account.message) owns.
/// </summary>
public record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    string? Body,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

/// <summary>
/// Talks to the provider's messaging API (Twilio 2010-04-01 Messages resource). Every call is
/// built directly against the api-specs contract: send, schedule, fetch, cancel, redact and
/// list messages. The configured sending number, messaging service and credentials live behind
/// this interface; callers deal only in destination numbers and message text.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>The configured sending number (<c>Twilio:FromNumber</c>) messages are reconciled against.</summary>
    string ConfiguredFromNumber { get; }

    /// <summary>Sends an SMS now, from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules an SMS to be sent by the provider at <paramref name="sendAt"/>, using the
    /// configured messaging service (required by the provider for scheduling). The provider,
    /// not this application, holds the message until then.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of a message, including its latest delivery outcome.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a scheduled message that has not yet been sent.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the message body at the provider (updates it to an empty string) so the text is
    /// no longer retrievable from the provider, while the record of the message survives.
    /// </summary>
    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number
    /// (<c>Twilio:FromNumber</c>) with a send date in the given range. The sending-number filter
    /// is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
