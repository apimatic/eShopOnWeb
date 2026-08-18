using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The provider's messaging API — the one this integration sends, reads, cancels, redacts and reconciles
/// messages through. The implementation owns the sending number and messaging service; callers pass only a
/// destination and content. All calls target the messaging base address (which may be overridden by
/// configuration).
/// </summary>
public interface ISmsGateway
{
    /// <summary>Send a message now. Returns the provider's identifier and initial status.</summary>
    Task<MessageDispatchResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<MessageDispatchResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent scheduled message so it never goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current authoritative state for one message.</summary>
    Task<MessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redact a message's body text at the provider so it can no longer be retrieved there, while the
    /// message record — the fact it was sent and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages this application sent (i.e. from the configured sending number)
    /// over a date-time range. The From filter is applied by the provider, not after the fact, so traffic on
    /// the account that this application did not send is excluded.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
