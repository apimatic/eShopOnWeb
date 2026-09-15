using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS messaging provider. The domain talks to this; the concrete provider
/// (Twilio) lives in Infrastructure. Keeping the contract provider-agnostic means the application
/// layer never depends on the provider's HTTP shape or SDK.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical form.
    /// </summary>
    Task<PhoneNumberLookup> LookupAsync(string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message now, or — when <paramref name="request"/> carries a send time — queues it with
    /// the provider to be sent at that later time.
    /// </summary>
    Task<GatewayMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record of one message by its identifier.</summary>
    Task<GatewayMessage> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Calls off a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the provider so it is no longer retrievable there, while the
    /// record that a message was sent — and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="fromNumber"/> whose sent
    /// date falls within the range. The provider is asked for that sender's messages directly.
    /// </summary>
    Task<IReadOnlyList<GatewayMessage>> ListSentFromAsync(
        string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of a number lookup: whether it is usable and its canonical E.164 form.</summary>
public record PhoneNumberLookup(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>A request to send one SMS. When <see cref="SendAt"/> is set the message is scheduled.</summary>
public record SendSmsRequest(string To, string Body, DateTimeOffset? SendAt = null);

/// <summary>The provider's view of a message: its identifier and current delivery outcome.</summary>
public record GatewayMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? To = null,
    string? From = null,
    DateTimeOffset? DateSent = null);
