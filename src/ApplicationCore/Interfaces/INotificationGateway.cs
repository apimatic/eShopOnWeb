using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider boundary. All delivery state (message id, outcome) is owned by the
/// provider; this gateway is the only way the application talks to it.
/// </summary>
public interface INotificationGateway
{
    /// <summary>Validates a raw number and returns the provider's canonical form. Throws InvalidPhoneNumberException when unusable.</summary>
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider for a future time (provider-held, not app-held).</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent message at the provider.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current state of one message.</summary>
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Lists the provider's own record of messages sent from the application's sending number in a date range (all pages).</summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider while keeping the message record.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}

public sealed record ValidatedPhoneNumber(string CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);
