using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberLookupResult
{
    public bool IsValid { get; init; }
    public string? CanonicalNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public interface IPhoneNumberLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public class SmsSendRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public class SmsMessageResult
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}

public interface ISmsGateway
{
    string FromNumber { get; }
    Task<SmsMessageResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
