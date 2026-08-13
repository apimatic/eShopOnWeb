using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The application's abstraction over the SMS provider. The concrete implementation lives in the
/// Infrastructure layer and is the single boundary where provider-SDK failures are translated into
/// <see cref="SmsProviderException"/>. Every method may throw <see cref="SmsProviderException"/>.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Validates a raw phone number and returns the provider's canonical form. Does not throw for a
    /// merely-invalid number — that comes back as <see cref="PhoneValidationResult.IsUsable"/> false;
    /// it throws only when the provider itself could not be consulted.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SentMessageResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SentMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a message the provider is still holding unsent.</summary>
    Task<MessageDeliveryState> CancelScheduledAsync(string providerSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current record for a single message.</summary>
    Task<MessageDeliveryState> GetMessageStateAsync(string providerSid, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there,
    /// while the record that the message existed and what became of it survives.
    /// </summary>
    Task RedactContentAsync(string providerSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending
    /// number within the date range, covering the whole range (paged internally).
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
