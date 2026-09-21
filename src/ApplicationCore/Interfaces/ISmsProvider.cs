using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic SMS operations the notification flow needs. The concrete implementation talks to the
/// messaging provider; all provider-specific detail (SDK types, hosts, account identifiers) stays behind it.
/// Every method may throw <see cref="SmsProviderException"/> and nothing else provider-shaped.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Ask the provider whether a number is a usable destination and, if so, its canonical E.164 form.</summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct);

    /// <summary>Send a message now.</summary>
    Task<SentSms> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>Queue a message with the provider to go out at <paramref name="sendAt"/> (not held in this app).</summary>
    Task<SentSms> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Cancel a not-yet-sent message the provider holds.</summary>
    Task<SentSms> CancelScheduledAsync(string providerSid, CancellationToken ct);

    /// <summary>Dispose of the message text at the provider (redaction), leaving the record and its outcome.</summary>
    Task RedactAsync(string providerSid, CancellationToken ct);

    /// <summary>Re-read one message's current provider-owned state.</summary>
    Task<SentSms?> FetchAsync(string providerSid, CancellationToken ct);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured sending number within
    /// the range, asked of the provider directly (not filtered from a wider answer). Covers the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
