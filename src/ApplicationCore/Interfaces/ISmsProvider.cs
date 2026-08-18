using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shop's view of the SMS provider. The concrete implementation owns all provider (Twilio) details.
///
/// Contract: methods return the provider's state when the provider answered (including a carrier-refused
/// message, which comes back with an "undelivered"/"failed" status — that is an outcome, not a failure).
/// A transport/configuration/parse failure — where no provider state exists — is signalled by throwing
/// <see cref="Exceptions.SmsProviderException"/>.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Ask the provider whether <paramref name="phoneNumber"/> is a usable destination and, if so, its
    /// canonical E.164 form.
    /// </summary>
    Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send an SMS now from the configured sending number.</summary>
    Task<SmsDispatchResult> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue an SMS with the provider to be sent at <paramref name="sendAt"/>. The provider itself holds the
    /// message until then — nothing in this application is responsible for sending it.
    /// </summary>
    Task<SmsDispatchResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current state for a previously sent/scheduled message.</summary>
    Task<SmsDispatchResult> GetStatusAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider has queued but not yet sent.</summary>
    Task<SmsDispatchResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider so its text is no longer retrievable there, while the
    /// record that the message was sent (and what became of it) survives.
    /// </summary>
    Task<SmsDispatchResult> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending number
    /// (<c>Twilio:FromNumber</c>) whose send time falls in [<paramref name="from"/>, <paramref name="to"/>].
    /// The whole range is covered (all pages are walked).
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
