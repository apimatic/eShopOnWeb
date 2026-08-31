using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider boundary. Implementations talk to the SMS provider;
/// all provider SDK types stay behind this interface.
/// </summary>
public interface ISmsService
{
    /// <summary>Validates a number with the provider and returns its canonical form.</summary>
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SentMessage> SendMessageAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider for a later time (provider-side scheduling).</summary>
    Task<SentMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a not-yet-sent scheduled message at the provider.</summary>
    Task CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Asks the provider for the current state of a message.</summary>
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Erases the message text at the provider; the message record itself survives.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Lists the provider's own record of messages sent from the application's configured
    /// sending number within the given date-sent range.</summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, string? Reason);

public record SentMessage(string Sid, string Status);

public record ProviderMessage(
    string Sid,
    string? To,
    string? From,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>Provider message-status wire values the integration reasons about.</summary>
public static class MessageStatuses
{
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>Statuses the provider will not move out of on its own.</summary>
    public static bool IsTerminal(string? status) =>
        status is Delivered or Undelivered or Failed or Canceled;
}
