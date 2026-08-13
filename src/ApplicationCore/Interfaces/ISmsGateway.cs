using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The outbound SMS provider, abstracted so the application layer never depends on the provider SDK.
/// Implementations translate provider outcomes into these provider-neutral results.
/// </summary>
public interface ISmsGateway
{
    /// <summary>This application's configured sending number — the one reconciliation asks the provider about.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns the provider's canonical
    /// (E.164) form. Throws <see cref="SmsGatewayException"/> if the provider could not be reached; a
    /// well-formed-but-rejected number comes back as a result with <c>IsValid == false</c>.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default);

    /// <summary>
    /// Sends an SMS now. Never throws: a provider or transport failure comes back as
    /// <c>Accepted == false</c> so a send failure can never fail the caller's operation.
    /// </summary>
    Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken ct = default);

    /// <summary>
    /// Queues an SMS with the provider for future delivery. Never throws (see <see cref="SendAsync"/>).
    /// </summary>
    Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>
    /// Calls off a message the provider has scheduled but not yet sent. Returns false if it could not be
    /// canceled. Never throws.
    /// </summary>
    Task<bool> CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>Reads a single message's current delivery outcome from the provider by its SID.</summary>
    Task<MessageStatusResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable there, while the
    /// record that it was sent and its outcome survive.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending number
    /// within a date-time range, walking every page. Used to reconcile against what eShop believes it sent.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>The outcome of validating a number against the provider.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>The outcome of handing a message to the provider.</summary>
public record SmsDispatchResult(bool Accepted, string? ProviderMessageSid, string? Status, int? ErrorCode, string? ErrorMessage)
{
    public static SmsDispatchResult Success(string sid, string? status) => new(true, sid, status, null, null);
    public static SmsDispatchResult Failure(int? errorCode, string? errorMessage) => new(false, null, null, errorCode, errorMessage);
}

/// <summary>A single message's current delivery outcome as read back from the provider.</summary>
public record MessageStatusResult(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's own record of one message, as returned by a reconciliation listing.</summary>
public record ProviderMessageRecord(string Sid, string? Status, DateTimeOffset? DateSent, string? To, string? From);
