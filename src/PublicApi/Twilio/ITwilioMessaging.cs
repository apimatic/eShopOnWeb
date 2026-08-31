using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

/// <summary>
/// The narrow seam over the Twilio SDK — the only surface the rest of the app uses.
/// All failures are translated to <see cref="TwilioProviderException"/>.
/// </summary>
public interface ITwilioMessaging
{
    /// <summary>Validates a number with the provider and returns its canonical form.</summary>
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Sends a message immediately from the configured sending number.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct);

    /// <summary>Queues a message with the provider for a future time (provider-side scheduling).</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAtUtc, CancellationToken ct);

    /// <summary>Cancels a message that is still scheduled at the provider.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Fetches the provider's current record of a message; null when the provider no longer has it.</summary>
    Task<ProviderMessage?> FetchMessageAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Erases the message body at the provider while keeping the message record.</summary>
    Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within (fromUtc, toUtc), paging through the whole range.
    /// </summary>
    Task<ProviderMessageList> ListMessagesFromSenderAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}
