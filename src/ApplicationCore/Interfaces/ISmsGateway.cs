using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over the SMS provider. The concrete implementation lives in the
/// Infrastructure layer and is the only place the provider SDK is used; nothing here depends on it.
///
/// Every method throws <see cref="Exceptions.SmsGatewayException"/> on a provider API error or a
/// connection failure — except <see cref="ValidateNumberAsync"/>, where an unusable number is a
/// normal result (<see cref="PhoneValidationResult.IsValid"/> = false), not an exception.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Asks the provider whether <paramref name="rawNumber"/> is a usable destination and, if so,
    /// returns its canonical E.164 form. Used at registration time so an unusable number is rejected
    /// up front rather than when a later message fails to go out.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct);

    /// <summary>Sends an SMS now from the application's configured sending number.</summary>
    Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>
    /// Queues an SMS with the provider to be sent at <paramref name="sendAt"/> — the provider holds
    /// and sends it, not this application.
    /// </summary>
    Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Calls off a message the provider has scheduled but not yet sent.</summary>
    Task<SmsDispatchResult> CancelScheduledAsync(string messageSid, CancellationToken ct);

    /// <summary>Reads the provider's current record of a single message (its delivery outcome).</summary>
    Task<SmsDispatchResult> FetchAsync(string messageSid, CancellationToken ct);

    /// <summary>
    /// Disposes of a message's body at the provider so its text is no longer retrievable there,
    /// while the record that the message was sent, and what became of it, survives.
    /// </summary>
    Task RedactAsync(string messageSid, CancellationToken ct);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending
    /// number within [<paramref name="from"/>, <paramref name="to"/>], asking the provider for that
    /// number's messages directly rather than filtering a wider answer. Pages the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageSummary>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Outcome of validating a candidate destination number.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>
/// What the provider owns about one message after a send / schedule / fetch / cancel: its
/// identifier and current delivery outcome.
/// </summary>
public record SmsDispatchResult(string? Sid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider itself reports it, used for reconciliation.</summary>
public record ProviderMessageSummary(string? Sid, string? To, string? From, string? Status, DateTimeOffset? DateSent, string? Body);
