using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneNumberLookup(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public sealed record SmsSendAttempt(
    bool Accepted,
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    bool OutcomeUnknown);

public sealed record ProviderMessage(
    string Sid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent,
    string? DateCreated,
    string? DateUpdated);

public interface ISmsGateway
{
    Task<PhoneNumberLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<SmsSendAttempt> SendImmediateAsync(string toCanonical, string body, CancellationToken cancellationToken);
    Task<SmsSendAttempt> ScheduleAsync(string toCanonical, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken);
    Task<SmsSendAttempt> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsSendAttempt> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage?> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}
