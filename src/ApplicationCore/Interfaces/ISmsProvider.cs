using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? Error { get; set; }
}

public class ProviderMessage
{
    public string Sid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ProviderSendResult
{
    public bool Success { get; set; }
    public ProviderMessage? Message { get; set; }
    public string? Error { get; set; }

    /// <summary>The provider reported the message resource itself as unknown (HTTP 404).</summary>
    public bool NotFound { get; set; }

    public static ProviderSendResult Ok(ProviderMessage message) => new() { Success = true, Message = message };
    public static ProviderSendResult Fail(string error, bool notFound = false) => new() { Success = false, Error = error, NotFound = notFound };
}

/// <summary>
/// Abstraction over the SMS messaging provider (Twilio). Implementations must never
/// log destination numbers, message bodies or credentials.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Validates a number with the provider and returns its canonical form.</summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a later time.</summary>
    Task<ProviderSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-queued (scheduled) message that has not gone out yet.</summary>
    Task<ProviderSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message. Null if unknown to the provider.</summary>
    Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Erases the message body at the provider while keeping the message record.</summary>
    Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured
    /// sending number within the range, covering the whole range (all pages).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromSendingNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
