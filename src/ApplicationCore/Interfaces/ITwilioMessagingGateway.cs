using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingGateway
{
    string FromNumber { get; }

    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<MessageSendResult> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<MessageSendResult?> FetchAsync(string sid, CancellationToken cancellationToken);
    Task<MessageSendResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken);
    Task<MessageSendResult> RedactBodyAsync(string sid, CancellationToken cancellationToken);
    Task<ProviderMessagePage> ListFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneLookupResult(
    bool IsUsable,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors,
    string? FailureMessage);

public sealed record MessageSendResult(
    bool Succeeded,
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? FailureReason);

public sealed record ProviderMessage(
    string Sid,
    string? Status,
    string? Body,
    string? DateSent,
    string? DateCreated);

public sealed record ProviderMessagePage(
    IReadOnlyList<ProviderMessage> Messages,
    bool Incomplete);
