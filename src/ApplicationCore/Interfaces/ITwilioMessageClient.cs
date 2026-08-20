using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent);

public record TwilioCreateMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt);

public interface ITwilioMessageClient
{
    string ConfiguredFromNumber { get; }
    Task<TwilioMessageSnapshot> CreateAsync(TwilioCreateMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> UpdateAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
