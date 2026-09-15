using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);

    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    string ConfiguredFromNumber { get; }
}

public sealed class PhoneLookupResult
{
    public bool Valid { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    public string? CanonicalPhoneNumber { get; init; }
    public string? NationalFormat { get; init; }
    public string? CountryCode { get; init; }
    public string? LineType { get; init; }
    public int? LineTypeErrorCode { get; init; }
}

public sealed class SmsSendRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public sealed class SmsSendResult
{
    public bool Accepted { get; init; }
    public string? MessageSid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? FailureReason { get; init; }
}

public sealed class SmsMessageSnapshot
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public int? ErrorCode { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public string? Direction { get; init; }
    public string? MessagingServiceSid { get; init; }
}
