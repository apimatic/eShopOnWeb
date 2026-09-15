using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider eShop talks to for validating numbers and sending, reading, cancelling,
/// redacting and reconciling SMS messages. This is the only seam through which the application reaches
/// the provider; the concrete implementation maps each method onto a documented provider REST call.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical E.164 form.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message immediately. Throws <see cref="SmsProviderException"/> if the provider does not
    /// accept the request.
    /// </summary>
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/> (a fixed schedule),
    /// so it is held by the provider rather than by this application.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current view of a message by its identifier.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is scheduled with the provider and has not yet been sent.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's text content at the provider, so the body is no longer retrievable there
    /// while the record of the message and its delivery outcome survives.
    /// </summary>
    Task RedactAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending number,
    /// with a <c>date sent</c> in the given range. The sender filter is applied by the provider, not after
    /// the fact, so traffic from other numbers on the account is never returned.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The provider's answer about whether a number can be used as a destination.</summary>
public record PhoneNumberLookupResult(bool Valid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>A message as the provider represents it.</summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
