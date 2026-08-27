using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider's own record of a message it knows about.
/// </summary>
public class ProviderMessage
{
    public string Sid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class MessageSendResult
{
    public MessageSendResult(bool succeeded, string? messageSid, string status, int? errorCode, string? errorMessage)
    {
        Succeeded = succeeded;
        MessageSid = messageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }
    public string? MessageSid { get; }
    public string Status { get; }
    public int? ErrorCode { get; }
    public string? ErrorMessage { get; }
}

/// <summary>
/// The messaging provider (Twilio). Implementations must never log phone numbers,
/// message bodies or credentials.
/// </summary>
public interface IMessageProvider
{
    /// <summary>The application's own configured sending number.</summary>
    string FromNumber { get; }

    /// <summary>Send a message immediately from the application's configured sending number.</summary>
    Task<MessageSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider for delivery at a future time.</summary>
    Task<MessageSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message.</summary>
    Task<ProviderMessage?> GetAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider has not sent yet.</summary>
    Task<bool> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact the body of a message so the provider no longer retains the text.</summary>
    Task<bool> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's record of messages sent from the application's own configured
    /// sending number within the given (inclusive) UTC window. Covers the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
