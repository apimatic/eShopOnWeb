using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<SmsOperationResult> SendAsync(SmsSendCommand command, CancellationToken cancellationToken = default);
    Task<SmsOperationResult> ScheduleAsync(SmsSendCommand command, DateTimeOffset sendAt, CancellationToken cancellationToken = default);
    Task<SmsOperationResult> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<SmsOperationResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<SmsOperationResult> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<SmsListResult> ListFromConfiguredSenderAsync(DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default);
}

public sealed class SmsSendCommand
{
    public required string To { get; init; }
    public required string Body { get; init; }
}

public sealed class SmsMessageSnapshot
{
    public required string Sid { get; init; }
    public required string Status { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class SmsOperationResult
{
    public bool Succeeded { get; init; }
    public SmsMessageSnapshot? Message { get; init; }
    public string? Error { get; init; }
}

public sealed class SmsListResult
{
    public bool Succeeded { get; init; }
    public IReadOnlyList<SmsMessageSnapshot> Messages { get; init; } = [];
    public string? FromNumber { get; init; }
    public string? Error { get; init; }
}
