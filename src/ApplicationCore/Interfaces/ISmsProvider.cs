using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shop's view of the SMS provider. Everything the shop needs to talk to the provider goes
/// through this abstraction so the domain never depends on the provider's HTTP surface.
/// </summary>
public interface ISmsProvider
{
    /// <summary>This application's own configured sending number (E.164) — the reconciliation
    /// filter and the default <c>From</c>.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Ask the provider whether <paramref name="rawNumber"/> is a usable destination and, if so,
    /// return its canonical E.164 form. Validation happens once, at the edge — a number the
    /// provider does not consider usable is rejected here rather than at send time.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default);

    /// <summary>Send a message now. Returns the provider's identifier and initial status.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current state of a message by its identifier.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message so it never reaches the recipient.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact the text of a message at the provider so it can no longer be retrieved.</summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending
    /// number within the (day-inclusive) range, for reconciliation. Follows provider paging.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
