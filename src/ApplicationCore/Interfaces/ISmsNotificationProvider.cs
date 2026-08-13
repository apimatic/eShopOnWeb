using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio). Everything provider-specific lives behind
/// this seam so the application layer stays free of the SDK. Implementations translate provider
/// failures into <see cref="Exceptions.NotificationProviderException"/> and never leak a raw
/// provider body or a shopper's number.
/// </summary>
public interface ISmsNotificationProvider
{
    /// <summary>
    /// Asks the provider whether <paramref name="phoneNumber"/> is a usable destination and, if so,
    /// returns the provider's canonical (E.164) form of it. An unusable number is reported as a
    /// non-throwing <see cref="PhoneNumberValidationResult.Invalid"/>.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. The returned message carries the provider's identifier and initial status.</summary>
    Task<ProviderMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/> — the provider holds and sends it.</summary>
    Task<ProviderMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a scheduled message that has not yet gone out.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current view of a message by its identifier.</summary>
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there,
    /// while the record that a message was sent and what became of it survive.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from eShop's configured sending number over
    /// the given range, for reconciliation. Only this application's sending number is asked about.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
