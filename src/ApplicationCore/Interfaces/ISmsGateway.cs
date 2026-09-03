using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<SmsMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsMessageResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageResult> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class SmsMessageListResult
{
    public required IReadOnlyList<SmsMessageResult> Messages { get; init; }
    public bool Truncated { get; init; }
}

public sealed class SmsMessageResult
{
    public bool ProviderAccepted { get; init; }
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Body { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public string? OutcomeDetail { get; init; }
}
