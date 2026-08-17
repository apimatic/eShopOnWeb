using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider seam. Everything the integration needs from the SMS provider is expressed
/// here in provider-neutral terms so <c>ApplicationCore</c> carries no dependency on a concrete SDK.
/// Implementations translate provider failures into <see cref="Exceptions.MessagingProviderException"/>.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>The application's own configured sending number (E.164), used for sends and for reconciliation.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable SMS destination and, if so, returns the provider's
    /// own canonical (E.164) form. Used to reject unusable numbers at registration time.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <summary>Sends an SMS immediately from the application's configured sending number.</summary>
    Task<SentMessage> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>Queues an SMS with the provider for delivery at <paramref name="sendAtUtc"/>, not held in this application.</summary>
    Task<SentMessage> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken);

    /// <summary>Calls off a message still scheduled at the provider so it never goes out. Idempotent if already sent/cancelled.</summary>
    Task<MessageDeliveryStatus> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>Reads the provider's current delivery outcome for a message.</summary>
    Task<MessageDeliveryStatus> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>Disposes of the message's body text at the provider while leaving the record and its status intact.</summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="fromNumber"/> within the
    /// inclusive date-time range, asking the provider to filter by sender rather than filtering afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}
