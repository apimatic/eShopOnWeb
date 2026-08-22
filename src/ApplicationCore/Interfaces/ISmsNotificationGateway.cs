using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public record SmsSendResult(
    bool Succeeded,
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);

public record SmsMessageSnapshot(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? To,
    string? DateSent);

public interface ISmsNotificationGateway
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<SmsSendResult> SendImmediateAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public record SmsMessageListResult(IReadOnlyList<SmsMessageSnapshot> Messages, bool Truncated);
