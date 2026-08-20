using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioSmsClient
{
    string FromNumber { get; }

    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> UpdateAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListFromSenderAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record PhoneNumberLookupResult(
    bool Valid,
    string? CanonicalE164,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

public record ProviderMessage(
    string? Sid,
    string Status,
    string? Body,
    string? To,
    string? From,
    int? ErrorCode,
    string? DateSent,
    string? DateCreated);
