using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single seam onto the Twilio messaging + lookup provider. Every Twilio interaction goes through
/// this abstraction; the concrete implementation lives in the Infrastructure layer and owns the SDK.
/// Implementations translate provider/transport failures into a provider exception and never leak SDK types.
/// </summary>
public interface ITwilioMessagingGateway
{
    /// <summary>The application's own configured sending number (E.164). Reconciliation is scoped to it.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Validates a number against the provider and returns its canonical E.164 form. A number the provider
    /// does not consider a usable destination comes back with <see cref="PhoneValidationResult.IsValid"/> false.
    /// </summary>
    Task<PhoneValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <summary>Sends an SMS immediately from the application's configured sending number.</summary>
    Task<ProviderMessageState> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>Queues an SMS with the provider to be sent at a future time (provider-held, not app-held).</summary>
    Task<ProviderMessageState> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>Cancels a scheduled-but-unsent message so it never reaches the customer.</summary>
    Task<ProviderMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>Reads a single message's current delivery outcome from the provider.</summary>
    Task<ProviderMessageState> GetMessageStateAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable there, while the
    /// record that the message was sent and what became of it survives.
    /// </summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number within the
    /// [<paramref name="from"/>, <paramref name="to"/>] window, covering the whole range. The sending-number
    /// filter is applied at the provider (asked of it), not by filtering a wider answer after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageSummary>> ListMessagesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Outcome of a phone-number validation. When valid, <see cref="CanonicalNumber"/> is the E.164 form to store.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>The provider's identifier and current delivery outcome for a single message.</summary>
public record ProviderMessageState(string Sid, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A row from the provider's message log, used to reconcile against what eShop believes it sent.</summary>
public record ProviderMessageSummary(string? Sid, string? Status, string? From, string? To, string? DateSent);
