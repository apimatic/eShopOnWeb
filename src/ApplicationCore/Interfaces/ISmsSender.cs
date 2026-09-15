using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A provider-neutral seam over the SMS messaging provider. The concrete implementation lives in the
/// Infrastructure layer; the domain depends only on this. Every method either succeeds or throws
/// <see cref="Exceptions.SmsProviderException"/> — the single failure type the rest of the app handles.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical (E.164)
    /// form. Never throws for an "invalid" verdict — that is reported via
    /// <see cref="PhoneValidationResult.IsValid"/>; it throws only when the provider itself cannot be
    /// reached or errors.
    /// </summary>
    Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now, from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a fixed time in the future.</summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Calls off a scheduled message with the provider before it goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current delivery outcome for a single message.</summary>
    Task<MessageDeliveryInfo> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there,
    /// while the record that the message existed and what became of it survives.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number within the date range, covering the whole range. The provider is asked for that
    /// number's messages directly rather than filtering a wider answer afterward.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
