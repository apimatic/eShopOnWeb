using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookup(
    string CanonicalNumber,
    bool Valid,
    IReadOnlyList<string> ValidationErrors,
    string? LineType);

public record ProviderMessage(
    string Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    string? DateCreated,
    string? DateSent,
    string? DateUpdated,
    int? ErrorCode,
    string? ErrorMessage,
    string? MessagingServiceSid,
    string? Direction);

public interface IOrderMessagingGateway
{
    Task<PhoneNumberLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);

    Task<ProviderMessage> FetchAsync(string sid, CancellationToken cancellationToken);

    Task<ProviderMessage> CancelScheduledAsync(string sid, CancellationToken cancellationToken);

    Task<ProviderMessage> RedactBodyAsync(string sid, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ProviderMessage> Messages, bool Truncated)> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
