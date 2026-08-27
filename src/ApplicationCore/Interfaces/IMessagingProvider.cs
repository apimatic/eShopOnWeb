using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Result of asking the provider to send or schedule a message.</summary>
public record ProviderMessageResult(string ProviderMessageSid, string Status);

/// <summary>Provider-owned state of a single message.</summary>
public record ProviderMessage(string ProviderMessageSid, string Status, DateTimeOffset? DateSent);

/// <summary>
/// Abstraction over the SMS provider (Twilio). Implementations must never log
/// destination numbers or credentials.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>Send a message immediately from the configured sending number.</summary>
    Task<ProviderMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider for delivery at a later time.</summary>
    Task<ProviderMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message that is still in the provider's scheduled queue.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message.</summary>
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's text at the provider while keeping the record of the message itself.</summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured
    /// sending number within a date range. The provider performs the From/date filtering.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
