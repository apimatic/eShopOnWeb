using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A gateway to the SMS provider. Everything the app needs to know about a message is obtained
/// by asking the provider through this abstraction — there is no callback into the app.
/// Implementations translate provider failures into <see cref="Microsoft.eShopWeb.ApplicationCore.Interfaces.SmsGatewayException"/>
/// and never let a provider error carry secrets (e.g. the auth token) outward.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Asks the provider whether <paramref name="rawNumber"/> is a usable destination and, if so,
    /// returns the provider's canonical (E.164) form. A number the provider cannot parse or does
    /// not consider valid comes back with <see cref="PhoneValidationResult.IsValid"/> = false.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken);

    /// <summary>Sends an SMS now, from the application's configured sending number. </summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken);

    /// <summary>
    /// Hands the provider a message to send later. The provider holds and sends it — the app runs
    /// no timer. Returns the provider's identifier and a "scheduled" status.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>
    /// Calls off a not-yet-sent scheduled message. Returns true only when the provider confirms
    /// the message is cancelled.
    /// </summary>
    Task<bool> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>Reads one message's current state (delivery outcome) from the provider.</summary>
    Task<SmsSendResult> FetchAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there,
    /// while the record that a message was sent — and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the provider for the messages it sent from the application's own configured sending
    /// number within [<paramref name="from"/>, <paramref name="to"/>]. The whole range is covered.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageSummary>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
