using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public record ProviderMessageSnapshot(
    string? Sid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateCreated,
    string? DateSent);

public interface ITwilioMessagingGateway
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> SendSmsAsync(string to, string body, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> ScheduleSmsAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> FetchMessageAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ProviderMessageSnapshot> Messages, bool Truncated)> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken);
}
