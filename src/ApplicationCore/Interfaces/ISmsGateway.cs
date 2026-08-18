using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A narrow, domain-facing seam over the SMS provider. It speaks only in plain values — no provider
/// SDK types leak across it — and it is the single place provider/transport failures are translated
/// into <see cref="Exceptions.SmsGatewayException"/>. The configured sending number and messaging
/// service are the gateway's own concern; callers never pass them.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Ask the provider whether <paramref name="rawNumber"/> is a usable destination and, if so, for its
    /// canonical E.164 form. Returns an invalid result for a number the provider will not accept; throws
    /// <see cref="Exceptions.SmsGatewayException"/> only for a transient/unknown provider problem.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>Send a message now. Throws <see cref="Exceptions.SmsGatewayException"/> if the provider will not accept it.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>
    /// Ask the provider to hold a message and send it at <paramref name="sendAt"/> (the delivery follow-up).
    /// The provider owns the timer, not this application.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancel a message the provider is still holding to send, before it goes out.</summary>
    Task<SmsMessageState> CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>Read the provider's current delivery outcome for a single message.</summary>
    Task<SmsMessageState> FetchStatusAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Redact the body text of a message at the provider so it can no longer be retrieved there, while the
    /// record that a message was sent and its final status survive.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// The provider's own record of the messages sent from this application's configured sending number within
    /// [<paramref name="from"/>, <paramref name="to"/>], walked across every page. The sending-number filter is
    /// applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
