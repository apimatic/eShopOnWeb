using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) as this application talks to it. Every provider interaction
/// goes through here. Implementations talk to the messaging API through the configured
/// <c>Twilio:BaseUrl</c> (when set), and to the Lookup API through its own host.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>The application's own configured sending number (<c>Twilio:FromNumber</c>).</summary>
    string FromNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical form.
    /// Used to reject an unusable number at registration time rather than when a send later fails.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now, from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/> (a fixed schedule),
    /// so the provider — not this application — holds it until then.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads a message back from the provider to see its current delivery outcome.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider (redacts the body) so its text can no longer
    /// be retrieved, while the fact that it was sent and what became of it survive.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number within a
    /// date range. The sending-number filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>A message resource as the provider reports it.</summary>
public sealed record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? Price,
    string? NumSegments);

/// <summary>The result of asking the provider to validate and canonicalize a number.</summary>
public sealed record PhoneNumberLookupResult(
    bool Valid,
    string? PhoneNumber,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);
