using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class PhoneNumberLookupResult
{
    public bool IsUsable { get; init; }
    public string? CanonicalPhoneNumber { get; init; }
    public string? NationalFormat { get; init; }
    public string? CountryCode { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public sealed class ProviderMessageResult
{
    public bool Accepted { get; init; }
    public string? Sid { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}

public interface ITwilioMessagingClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string rawPhoneNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> SendAsync(string toE164, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessageResult>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
