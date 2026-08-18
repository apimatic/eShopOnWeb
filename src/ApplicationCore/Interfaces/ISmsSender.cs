using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). The concrete implementation lives in Infrastructure and is the
/// only place that talks to the provider SDK. All provider failures surface as
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.SmsProviderException"/>.
/// </summary>
public interface ISmsSender
{
    /// <summary>The configured sending number messages go out from (Twilio:FromNumber). Not a secret.</summary>
    string SendingNumber { get; }

    /// <summary>Ask the provider whether a number is a usable destination and, if so, its canonical (E.164) form.</summary>
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <summary>Send a message immediately from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/> (not held in this app).</summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>Cancel a message the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>Read a message's current delivery outcome from the provider.</summary>
    Task<SmsMessageStatus> FetchStatusAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>Redact a message's body at the provider so its text is no longer retrievable, keeping the record.</summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>
    /// List the provider's messages sent from the configured sending number over a date range,
    /// asking the provider to filter by that number rather than filtering a wider answer here.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}
