using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Notifications;

namespace PublicApiIntegrationTests.NotificationEndpoints;

internal sealed class FakeTwilioClient : ITwilioLookupClient, ITwilioMessagingClient
{
    private int _sid;
    private readonly ConcurrentDictionary<string, TwilioMessage> _messages = new();

    public string CanonicalNumber { get; set; } = "+15555550123";
    public bool NumberIsValid { get; set; } = true;
    public bool ThrowOnSend { get; set; }
    public bool ThrowOnNextCancellation { get; set; }
    public Queue<string> SendStatuses { get; } = new();
    public List<(string Destination, DateTimeOffset? SendAt)> Sends { get; } = new();
    public List<string> Cancellations { get; } = new();
    public List<string> Redactions { get; } = new();

    public Task<TwilioLookupResponse> LookupAsync(string phoneNumber, CancellationToken cancellationToken) =>
        Task.FromResult(new TwilioLookupResponse
        {
            Valid = NumberIsValid,
            PhoneNumber = NumberIsValid ? CanonicalNumber : phoneNumber,
            ValidationErrors = NumberIsValid ? null : new List<string> { "INVALID_LENGTH" }
        });

    public Task<TwilioMessage> SendAsync(
        string destination,
        string content,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        Sends.Add((destination, sendAt));
        if (ThrowOnSend)
        {
            throw new TwilioApiException(503, 20500);
        }
        var sid = $"SM{Interlocked.Increment(ref _sid):x32}";
        var status = SendStatuses.Count > 0 ? SendStatuses.Dequeue() : sendAt is null ? "delivered" : "scheduled";
        var now = DateTimeOffset.UtcNow;
        var message = new TwilioMessage
        {
            Sid = sid,
            Status = status,
            Body = content,
            From = "+15555550999",
            To = destination,
            ErrorCode = status == "undelivered" ? 30007 : null,
            ErrorMessage = status == "undelivered" ? "Filtered" : null,
            DateCreated = now,
            DateSent = sendAt is null ? now : null
        };
        _messages[sid] = message;
        return Task.FromResult(Clone(message));
    }

    public Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken) =>
        Task.FromResult(Clone(_messages[messageSid]));

    public Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
    {
        Cancellations.Add(messageSid);
        if (ThrowOnNextCancellation)
        {
            ThrowOnNextCancellation = false;
            throw new TwilioApiException(503, 20500);
        }
        var message = _messages[messageSid];
        message.Status = "canceled";
        return Task.FromResult(Clone(message));
    }

    public Task<TwilioMessage> RedactAsync(string messageSid, CancellationToken cancellationToken)
    {
        Redactions.Add(messageSid);
        var message = _messages[messageSid];
        message.Body = string.Empty;
        return Task.FromResult(Clone(message));
    }

    public Task<IReadOnlyList<TwilioMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TwilioMessage> result = _messages.Values
            .Where(x => x.DateSent >= from && x.DateSent <= to)
            .Select(Clone)
            .ToList();
        return Task.FromResult(result);
    }

    private static TwilioMessage Clone(TwilioMessage x) => new()
    {
        Sid = x.Sid,
        Status = x.Status,
        Body = x.Body,
        From = x.From,
        To = x.To,
        ErrorCode = x.ErrorCode,
        ErrorMessage = x.ErrorMessage,
        DateCreated = x.DateCreated,
        DateSent = x.DateSent
    };
}
