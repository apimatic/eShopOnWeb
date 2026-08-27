using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SmsSendResult
{
    public SmsSendResult(string messageSid, string status)
    {
        MessageSid = messageSid;
        Status = status;
    }

    public string MessageSid { get; }
    public string Status { get; }
}

public class SmsMessageDetails
{
    public string MessageSid { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public string? Body { get; set; }
}

/// <summary>
/// The messaging provider's SMS capability. Implementations are built against
/// the provider's OpenAPI contract.
/// </summary>
public interface ISmsGateway
{
    Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for delivery at a future time.
    /// </summary>
    Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a provider-scheduled message that has not yet gone out.
    /// </summary>
    Task<SmsSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the body of a message at the provider so its text is no longer retrievable.
    /// </summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageDetails> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's
    /// configured sending number within the given date range (server-side filtered).
    /// </summary>
    Task<IReadOnlyList<SmsMessageDetails>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
