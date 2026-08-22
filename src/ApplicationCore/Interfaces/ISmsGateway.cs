using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);
}

public record PhoneNumberLookupResult(
    bool Valid,
    string? CanonicalPhoneNumber,
    IReadOnlyList<string> ValidationErrors);

public record SmsSendRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public record SmsSendResult(
    bool Accepted,
    string? ProviderMessageSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);

public record SmsMessageSnapshot(
    string Sid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    string? DateSent,
    string? DateCreated,
    int? ErrorCode,
    string? ErrorMessage,
    string? Direction);
