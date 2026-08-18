using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio). Implementations own the provider contract;
/// the application core only depends on this. Implementations must never write shopper numbers or the
/// provider auth secret to logs.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical E.164 form.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from this application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Schedules a message with the provider to be delivered at <paramref name="sendAt"/>.</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is still scheduled with the provider so it never goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current delivery outcome for a message.</summary>
    Task<SmsDeliveryState> FetchStateAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there,
    /// while the provider's record that the message existed and its status survive.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending number
    /// within the given range. The sender filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
