using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). Implementations must never log
/// destination numbers or credentials.
/// </summary>
public interface ISmsService
{
    /// <summary>Send a message immediately.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider for later delivery.</summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current state for a message.</summary>
    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message at the provider.</summary>
    Task<ProviderMessage?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact the body of a message at the provider so the text is no longer retrievable there.</summary>
    Task<ProviderMessage?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's
    /// configured sending number within the given (UTC) range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromShopNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class SmsSendResult
{
    public string MessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ProviderMessage
{
    public string MessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
