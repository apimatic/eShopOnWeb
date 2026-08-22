using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsMessageGateway
{
    Task<SmsMessageResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class SmsSendRequest
{
    public SmsSendRequest(string to, string body, DateTimeOffset? sendAt = null)
    {
        To = to;
        Body = body;
        SendAt = sendAt;
    }

    public string To { get; }
    public string Body { get; }
    public DateTimeOffset? SendAt { get; }
}

public sealed class SmsMessageResult
{
    public SmsMessageResult(
        string? sid,
        string? status,
        string? body,
        string? from,
        string? to,
        int? errorCode,
        string? errorMessage,
        string? dateCreated,
        string? dateSent,
        string? dateUpdated)
    {
        Sid = sid;
        Status = status;
        Body = body;
        From = from;
        To = to;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateCreated = dateCreated;
        DateSent = dateSent;
        DateUpdated = dateUpdated;
    }

    public string? Sid { get; }
    public string? Status { get; }
    public string? Body { get; }
    public string? From { get; }
    public string? To { get; }
    public int? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public string? DateCreated { get; }
    public string? DateSent { get; }
    public string? DateUpdated { get; }
}
