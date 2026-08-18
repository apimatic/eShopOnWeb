using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic view of the SMS provider the rest of the application talks to. The Twilio
/// specifics live behind this in Infrastructure; nothing above this interface knows the provider.
/// Implementations must never log the shopper's phone number or any credential.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical E.164 form.
    /// Called at registration so an unusable number is rejected before any message is attempted.
    /// </summary>
    Task<PhoneValidationResult> ValidatePhoneNumberAsync(string rawPhoneNumber, CancellationToken ct = default);

    /// <summary>Send a message now. Returns the provider's identifier and initial status. Throws <see cref="Exceptions.SmsGatewayException"/> on provider/transport failure.</summary>
    Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken ct = default);

    /// <summary>Queue a message with the provider for a future send. Returns the provider's identifier and status.</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAtUtc, CancellationToken ct = default);

    /// <summary>Cancel a message the provider has queued for a future send, before it goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Read the provider's current delivery outcome for a message.</summary>
    Task<SmsStatusResult> FetchStatusAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Dispose of a message's text at the provider while keeping the record that it was sent and what became of it.</summary>
    Task RedactContentAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// The provider's own record of messages this application sent (server-side filtered to this
    /// application's configured sending number) within a date range. Used for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListOwnMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
