using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The outbound-SMS provider (Twilio), abstracted to the capabilities this integration needs.
/// Implementations must never write a destination number or message body to logs.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Asks the provider whether <paramref name="phoneNumber"/> is a usable destination and,
    /// if so, returns the provider's own canonical form of it. Used to reject bad numbers at
    /// registration time rather than when a message later fails to go out.
    /// </summary>
    Task<PhoneNumberLookupResult> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now.</summary>
    Task<GatewayMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<GatewayMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Calls off a message the provider has scheduled but not yet sent.</summary>
    Task<GatewayMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record for a message, including its delivery outcome.</summary>
    Task<GatewayMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the provider so it can no longer be retrieved there,
    /// while the message record — the fact it was sent and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the given range. The sending-number filter is applied by the
    /// provider, not after the fact, so traffic from other numbers on the account is excluded.
    /// </summary>
    Task<IReadOnlyList<GatewayMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of validating/canonicalizing a phone number with the provider.</summary>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalE164);

/// <summary>A message as the provider sees it.</summary>
public record GatewayMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    string? Body);
