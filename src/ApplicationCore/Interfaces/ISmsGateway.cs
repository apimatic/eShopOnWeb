using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken);
    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record PhoneNumberLookupResult(bool IsUsable, string? CanonicalNumber, bool ProviderUnavailable);

public sealed record SmsSendRequest(string To, string Body, DateTimeOffset? SendAt);

public sealed record SmsSendResult(
    bool Accepted,
    string? ProviderSid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    bool OutcomeUnknown);

public sealed record SmsMessageSnapshot(
    string? Sid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    string? DateSent,
    string? DateCreated,
    int? ErrorCode,
    string? ErrorMessage);
