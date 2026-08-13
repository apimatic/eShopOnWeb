using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shop's view of the SMS provider (Twilio). Every provider interaction goes through this
/// abstraction so the messaging concerns stay in one place and the rest of the app never talks to
/// the provider directly.
/// </summary>
public interface ISmsMessagingService
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and, if so, returns its canonical
    /// E.164 form. Used to reject unusable numbers at registration time rather than at send time.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the configured sending number.</summary>
    Task<SmsMessageResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/> (the delayed follow-up).
    /// The message is held by the provider, not by this application.
    /// </summary>
    Task<SmsMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads a message resource back from the provider by its SID.</summary>
    Task<SmsMessageResult> FetchAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<SmsMessageResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider by redacting its body, so the text is no longer
    /// retrievable from the provider. The provider's record that a message was sent, and its outcome, survives.
    /// </summary>
    Task RedactAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending number
    /// within the given range. The provider is asked for that number's messages directly rather than
    /// filtering a wider answer after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
