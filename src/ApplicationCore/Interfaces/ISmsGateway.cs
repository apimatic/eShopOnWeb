using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The SMS provider (Twilio) seen through eShop's own contract. Implementations own the wire
/// details; the application layer only knows these capabilities.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Validate a number and return its canonical E.164 form. A number the provider does not
    /// consider a usable destination comes back with <see cref="PhoneLookupResult.IsValid"/> false.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message now, or — when <paramref name="scheduleAt"/> is supplied — queue it with the
    /// provider to be sent at that time. Returns the provider's identifier and initial status.
    /// </summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current delivery outcome for a message.</summary>
    Task<SmsStatusResult> FetchStatusAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Call off a message that the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redact the message content at the provider so its text is no longer retrievable, while the
    /// message record and its status survive.
    /// </summary>
    Task RedactContentAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ask the provider for every message it sent from eShop's configured sending number within the
    /// range, following pagination to cover the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
