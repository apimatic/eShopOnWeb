using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shop's view of the messaging provider. Everything the integration needs to send, observe,
/// call off and reconcile messages goes through here; the concrete implementation is the only place
/// that knows the provider's wire protocol, credentials and hosts.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, for its canonical form.
    /// Used to reject a bad number at registration rather than when a message later fails to send.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Create a message to be sent now. Throws <see cref="Exceptions.SmsProviderException"/> if the provider rejects the request.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/> (the provider holds it, not this app).</summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read a message's current delivery outcome from the provider.</summary>
    Task<SmsStatusResult> GetStatusAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Call off a scheduled message that has not yet been sent, so it never reaches the shopper.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's text at the provider so it can no longer be retrieved there. The message record itself survives.</summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages that were sent from this application's configured
    /// sending number within the given range. The From filter is applied by the provider, not here,
    /// because the provider account also carries traffic that is not this application's.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
