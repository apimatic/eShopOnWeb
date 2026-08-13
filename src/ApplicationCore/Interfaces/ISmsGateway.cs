using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). Every method obtains what it needs by asking the
/// provider directly (there is no callback into this application). Implementations must never write
/// a destination phone number or the auth token to logs. A provider failure surfaces as an
/// <see cref="Exceptions.SmsGatewayException"/>.
/// </summary>
public interface ISmsGateway
{
    /// <summary>This application's configured sending number (Twilio:FromNumber).</summary>
    string SenderNumber { get; }

    /// <summary>
    /// Ask the provider whether <paramref name="phoneNumber"/> is a usable destination and, if so,
    /// return its canonical (E.164) form.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateDestinationAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now.</summary>
    Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a scheduled message before it is sent.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetch a message's current delivery status from the provider.</summary>
    Task<string?> GetDeliveryStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Dispose a message's body content at the provider so its text is no longer retrievable.</summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's record of messages sent from <see cref="SenderNumber"/> within the given
    /// range. The From-number and date filters are applied by the provider (server-side), not here.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
