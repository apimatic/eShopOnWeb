using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider's messaging API (Twilio). Everything this integration needs
/// from the provider's messaging surface — sending, scheduling, cancelling, reading state,
/// disposing of content and listing sent messages for reconciliation — goes through here so the
/// domain never depends on the concrete provider or its transport.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/> (a few days out).
    /// The provider — not this application — holds it until then.
    /// </summary>
    Task<ProviderSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a still-scheduled message so it never goes out.</summary>
    Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record for a single message (its status and any error).</summary>
    Task<ProviderMessageState> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider (redacts the body) while keeping the record of
    /// the message and its outcome. Afterwards the text is no longer retrievable from the provider.
    /// </summary>
    Task RedactContentAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number within the date range. The provider is asked for that number's messages directly.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of accepting a message for (immediate or scheduled) sending.</summary>
public record ProviderSendResult(string Sid, string? Status, int? ErrorCode);

/// <summary>The provider's current record for a single message.</summary>
public record ProviderMessageState(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A message as it appears in the provider's own logs, for reconciliation.</summary>
public record ProviderMessageRecord(string Sid, string? Status, int? ErrorCode, DateTimeOffset? DateSent, string? To);
