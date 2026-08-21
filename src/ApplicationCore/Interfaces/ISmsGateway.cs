using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(bool IsValid, string? CanonicalNumber);

public record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);

public record SendMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt);

public interface ISmsGateway
{
    string SendingNumber { get; }

    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<ProviderMessage> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
