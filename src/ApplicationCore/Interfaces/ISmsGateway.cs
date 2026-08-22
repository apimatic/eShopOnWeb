using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneNumberLookupResult(
    bool Valid,
    string? CanonicalNumber,
    string? CountryCode,
    IReadOnlyList<string> ValidationErrors);

public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed record SmsSendRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt);

public sealed record SmsMessageSnapshot(
    string Sid,
    string? From,
    string? To,
    string? Body,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public interface ISmsGateway
{
    string ConfiguredFromNumber { get; }

    Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> CancelAsync(string providerSid, CancellationToken cancellationToken = default);
    Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
