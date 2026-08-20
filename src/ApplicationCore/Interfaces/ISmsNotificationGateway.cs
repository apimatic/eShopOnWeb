using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsNotificationGateway
{
    string FromNumber { get; }

    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<ProviderMessageResult?> SendImmediateAsync(
        string to,
        string body,
        CancellationToken cancellationToken);

    Task<ProviderMessageResult?> ScheduleAsync(
        string to,
        string body,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken);

    Task<ProviderMessageResult?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageResult?> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageResult?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderMessageResult>> ListFromSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record PhoneNumberLookupResult(
    bool IsUsable,
    string? CanonicalNumber,
    string? RejectionReason);

public sealed record ProviderMessageResult(
    string? Sid,
    string? Status,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    string? DateCreated,
    string? DateSent);
