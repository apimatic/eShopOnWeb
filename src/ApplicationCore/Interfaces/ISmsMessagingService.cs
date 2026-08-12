using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Everything the SMS integration needs from the messaging provider (Twilio). The
/// implementation owns the provider contract; the rest of the app depends only on this.
/// </summary>
public interface ISmsMessagingService
{
    /// <summary>The application's own configured sending number (E.164).</summary>
    string FromNumber { get; }

    /// <summary>
    /// Validates a number with the provider and returns its canonical E.164 form.
    /// A number the provider does not consider a usable destination comes back with
    /// <see cref="PhoneNumberValidationResult.IsValid"/> == false.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now.</summary>
    Task<SentMessageResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for future delivery. The provider holds it and
    /// sends it at <paramref name="sendAtUtc"/>; it is not held in this application.
    /// </summary>
    Task<SentMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that was scheduled with the provider but has not yet gone out.</summary>
    Task<SentMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current delivery outcome for a message.</summary>
    Task<MessageDeliveryState> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable,
    /// while the record that a message was sent — and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages the application sent from <see cref="FromNumber"/>
    /// within a date range. The provider is asked for that number's messages directly; the
    /// account's other traffic is never returned.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
