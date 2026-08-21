using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<ProviderMessage> CreateMessageAsync(CreateProviderMessageRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessagePage> ListMessagesFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record ProviderMessagePage(
    string FromNumber,
    IReadOnlyList<ProviderMessage> Messages);

public sealed record PhoneNumberLookupResult(
    bool Valid,
    string? PhoneNumber,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

public sealed record CreateProviderMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? DateSent,
    string? DateCreated);
