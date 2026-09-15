using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class SmsLookupResult
{
    public bool IsUsable { get; init; }
    public string? CanonicalNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public sealed class SmsMessageSnapshot
{
    public string? ProviderSid { get; init; }
    public string Status { get; init; } = "unknown";
    public string? Body { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? DateSent { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool OutcomeUnknown { get; init; }
}

public sealed class SmsReconciliationPage
{
    public IReadOnlyList<SmsMessageSnapshot> Messages { get; init; } = Array.Empty<SmsMessageSnapshot>();
    public bool Complete { get; init; } = true;
    public string FromNumber { get; init; } = string.Empty;
}

public enum SmsProviderFailureKind
{
    CallerRejected,
    ProviderUnavailable,
    RateLimited,
    OutcomeUnknown
}

public class SmsProviderException : Exception
{
    public SmsProviderException(string message, SmsProviderFailureKind kind, int? httpStatusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        HttpStatusCode = httpStatusCode;
    }

    public SmsProviderFailureKind Kind { get; }
    public int? HttpStatusCode { get; }
}

public interface ISmsNotificationGateway
{
    Task<SmsLookupResult> LookupDestinationAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> SendNowAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<SmsReconciliationPage> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
