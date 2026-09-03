using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(bool ProviderReached, bool IsUsable, string? CanonicalNumber, int? HttpStatus);

public record ProviderMessage(
    bool Accepted,
    string? Sid,
    string Status,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    DateTimeOffset? DateCreated);

public record ProviderMessagePage(
    IReadOnlyList<ProviderMessage> Messages,
    string? NextPageToken);

public interface ISmsGateway
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken ct);
    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken ct);
    Task<ProviderMessage> FetchAsync(string sid, CancellationToken ct);
    Task<ProviderMessagePage> ListSentFromAsync(DateTimeOffset rangeFrom, DateTimeOffset rangeTo, string? pageToken, CancellationToken ct);
    Task<ProviderMessage> RedactBodyAsync(string sid, CancellationToken ct);
    Task<ProviderMessage> CancelAsync(string sid, CancellationToken ct);
}
