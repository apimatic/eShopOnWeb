using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneLookupResult(bool IsUsableDestination, string? CanonicalNumber, string? RejectionReason);

public sealed record SmsMessageSnapshot(
    string? Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent,
    string? DateCreated,
    string? Direction);

public sealed record SmsSendRequest(string To, string Body, DateTimeOffset? SendAt);

public interface ISmsNotificationGateway
{
    string ConfiguredFromNumber { get; }

    Task<PhoneLookupResult> LookupNumberAsync(string rawNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Sends or schedules a message. Never throws — a provider failure is represented on the snapshot.
    /// </summary>
    Task<SmsMessageSnapshot> TrySendAsync(SmsSendRequest request, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
