using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) as this application needs it: validate a destination, send now,
/// schedule for later, cancel a scheduled send, read a message back, redact its content, and list
/// the messages the application's own sending number has produced.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Confirms whether <paramref name="phoneNumber"/> is a usable destination and returns the
    /// provider's canonical E.164 form of it.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for delivery at <paramref name="sendAt"/>. The provider holds
    /// and later sends it; this application does not.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is still scheduled, so it never goes out.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads back the current provider state of a single message.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable there, while the
    /// record of the message and its outcome survives.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number whose send date falls within [<paramref name="from"/>, <paramref name="to"/>].
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
