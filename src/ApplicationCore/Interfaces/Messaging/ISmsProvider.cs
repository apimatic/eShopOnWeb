using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The messaging provider (Twilio) as this integration needs it. Everything the provider owns —
/// validating a destination, sending, scheduling, cancelling, reading status, redacting content
/// and listing sent messages for reconciliation — is reached only through here.
/// </summary>
public interface ISmsProvider
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber), in E.164 form.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Validates a number and returns its canonical E.164 form. A number the provider does not
    /// consider a usable destination comes back with <see cref="PhoneNumberLookupResult.IsValid"/> false.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. Returns the provider's SID and initial status.</summary>
    Task<SentMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a message to be sent by the provider at <paramref name="sendAt"/>. The provider
    /// holds and sends it; nothing in this application is responsible for the timing.
    /// </summary>
    Task<SentMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current status for a single message.</summary>
    Task<MessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a sent message's body at the provider so its text is no longer retrievable there,
    /// while the message record and its delivery outcome survive.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists, from the provider's own records, every message sent from <paramref name="fromNumber"/>
    /// whose send date falls in [<paramref name="from"/>, <paramref name="to"/>]. The provider is
    /// asked directly for that number's messages rather than filtering a wider answer afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromNumberAsync(
        string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
