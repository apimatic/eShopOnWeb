using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The result of validating/canonicalizing a phone number with the provider.
/// </summary>
public record PhoneNumberLookup(bool IsValid, string? CanonicalNumber);

/// <summary>
/// The provider-owned state of a message the shop sent: its identifier and current outcome.
/// </summary>
public record SentMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>
/// A message as the provider reports it (used for reconciliation).
/// </summary>
public record ProviderMessage(
    string Sid,
    string? To,
    string? From,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>
/// Abstraction over every Twilio messaging interaction this integration needs. The concrete
/// implementation lives in Infrastructure and is the only place the Twilio SDK is referenced.
/// Every method throws <see cref="Exceptions.SmsGatewayException"/> on provider/transport failure.
/// </summary>
public interface ISmsGateway
{
    /// <summary>Validate and canonicalize a number. A number the provider does not consider a
    /// usable destination comes back with <c>IsValid == false</c>.</summary>
    Task<PhoneNumberLookup> LookupNumberAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Send a message now, from the configured sending number.</summary>
    Task<SentMessage> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>
    /// (via the configured messaging service). The provider — not this app — holds it until then.</summary>
    Task<SentMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Read the provider's current state for a message.</summary>
    Task<SentMessage> FetchAsync(string sid, CancellationToken ct);

    /// <summary>Cancel a not-yet-sent (scheduled) message so it never reaches the shopper.</summary>
    Task<SentMessage> CancelScheduledAsync(string sid, CancellationToken ct);

    /// <summary>Dispose of the provider's copy of the message text (redaction), leaving the
    /// fact of the send and its outcome intact.</summary>
    Task RedactContentAsync(string sid, CancellationToken ct);

    /// <summary>List the provider's own record of messages sent from this application's configured
    /// sending number within the range. Asks the provider to filter by sender — never filters a
    /// wider answer after the fact.</summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
