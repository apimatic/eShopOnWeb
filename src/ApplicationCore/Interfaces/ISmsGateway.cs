using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<SmsDispatchResult?> SendAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsDispatchResult?> ScheduleAsync(string to, string body, System.DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsDispatchResult?> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<bool> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<bool> RedactBodyAsync(string providerSid, string originalBody, CancellationToken cancellationToken);

    Task<ProviderMessageList> ListSentFromConfiguredNumberAsync(
        System.DateTimeOffset fromInclusive,
        System.DateTimeOffset toExclusive,
        CancellationToken cancellationToken);
}

public sealed class ProviderMessageList
{
    public required IReadOnlyList<ProviderMessageRecord> Messages { get; init; }
    public bool Truncated { get; init; }
}

public sealed class PhoneNumberLookupResult
{
    public bool IsUsable { get; init; }
    public string? CanonicalNumber { get; init; }
}

public sealed class SmsDispatchResult
{
    public required string ProviderSid { get; init; }
    public required string Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Body { get; init; }
}

public sealed class ProviderMessageRecord
{
    public required string ProviderSid { get; init; }
    public required string Status { get; init; }
    public string? Body { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
