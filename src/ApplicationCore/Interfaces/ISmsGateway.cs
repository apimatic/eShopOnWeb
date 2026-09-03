using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A provider-neutral abstraction over the SMS provider (Twilio). All provider SDK types stay behind this
/// seam in the Infrastructure layer; this interface and its result records use only primitives so the
/// domain never takes a dependency on the SDK. Every method translates provider/transport failures into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.SmsGatewayException"/>.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical (E.164) form.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Send a message immediately.</summary>
    Task<SmsDispatchResult> SendAsync(string to, string body, CancellationToken ct = default);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> (the provider holds it,
    /// this application does not). Returns the provider id so the message can later be called off.
    /// </summary>
    Task<SmsDispatchResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancel a message the provider has queued but not yet sent.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Fetch the provider's current delivery outcome for a message.</summary>
    Task<SmsDeliveryState> FetchStateAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// Dispose of a message's text at the provider (so it is no longer retrievable there), while the
    /// message record and its status survive.
    /// </summary>
    Task DisposeContentAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// The provider's own record of messages this application sent from its configured sending number
    /// within a date range, asked of the provider directly (filtered by sender), not filtered afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Whether a number is a usable destination and its canonical (E.164) form when it is.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>The provider's response to a send/schedule: its message id and initial status/error, if any.</summary>
public record SmsDispatchResult(string? MessageSid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current delivery outcome for a message.</summary>
public record SmsDeliveryState(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message from the provider's own records, for reconciliation.</summary>
public record ProviderMessage(string Sid, string? Status, string? To, string? From, string? DateSent);
