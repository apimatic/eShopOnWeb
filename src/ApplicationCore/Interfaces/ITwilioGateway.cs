using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioGateway
{
    string FromNumber { get; }

    Task<PhoneLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> SendMessageAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessageResult>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);
}

public record PhoneLookupResult(
    bool IsValid,
    string? CanonicalPhoneNumber,
    string? NationalFormat,
    string? LineType,
    int? LineTypeErrorCode,
    IReadOnlyList<string> ValidationErrors);

public record SendProviderMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public record ProviderMessageResult(
    string? Sid,
    string Status,
    int? ErrorCode,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    string? MessagingServiceSid);
