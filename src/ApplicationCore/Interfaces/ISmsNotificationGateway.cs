using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public record SmsSendResult(
    bool Attempted,
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    bool OutcomeUnknown);

public record SmsMessageSnapshot(
    string Sid,
    string? From,
    string? To,
    string? Status,
    string? Body,
    string? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

public interface ISmsNotificationGateway
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<SmsSendResult> SendNowAsync(string to, string body, CancellationToken cancellationToken);
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<bool> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken);
}

public record SmsMessageListResult(IReadOnlyList<SmsMessageSnapshot> Messages, bool Truncated);
