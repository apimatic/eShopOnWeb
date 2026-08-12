using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the SMS provider used to reach shoppers. Every Twilio interaction lives behind this
/// seam; the application core depends only on this abstraction.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider whether <paramref name="rawNumber"/> is a usable destination and returns
    /// its canonical E.164 form. Used to reject an unusable number at registration time rather than
    /// when a message later fails to go out.
    /// </summary>
    Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SentMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands a message to the provider to be sent at <paramref name="sendAt"/>. The provider holds
    /// and later sends it; nothing is held in this application.
    /// </summary>
    Task<SentMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Calls off a scheduled message with the provider before it is sent.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current delivery outcome for a message.</summary>
    Task<MessageDeliveryState> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content on the provider's side so its text is no longer retrievable,
    /// while the record that the message was sent and its outcome survive.
    /// </summary>
    Task DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending
    /// number within the date range, over the whole range. Used for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
