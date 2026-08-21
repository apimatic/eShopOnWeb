using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<SmsMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken);
    Task<SmsMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<SmsMessageResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageResult> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken);
    string FromNumber { get; }
}

public sealed record PhoneLookupResult(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public sealed record SmsMessageResult(
    bool Accepted,
    string? Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    string? DateSent,
    int? ErrorCode,
    string? ErrorMessage,
    string? Direction,
    string? FailureReason);
