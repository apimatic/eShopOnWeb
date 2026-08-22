using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneNumberLookupResult(
    bool IsUsable,
    string? CanonicalNumber,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

public sealed record SmsSendRequest(string To, string Body, DateTimeOffset? SendAt);

public sealed record SmsMessageSnapshot(
    string? Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    string? DateSent,
    string? DateCreated,
    int? ErrorCode,
    string? ErrorMessage,
    string? MessagingServiceSid);

public interface ISmsGateway
{
    string ConfiguredFromNumber { get; }

    Task<PhoneNumberLookupResult> LookupNumberAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
