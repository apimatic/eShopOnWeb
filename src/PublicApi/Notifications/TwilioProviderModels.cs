using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed record ProviderMessage(
    string? Sid,
    string? From,
    string? Body,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);

public sealed record ProviderMessagePage(IReadOnlyList<ProviderMessage> Messages, string? NextPageToken);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(
        string safeMessage,
        HttpStatusCode? statusCode = null,
        bool ambiguous = false,
        Exception? innerException = null) : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        IsAmbiguous = ambiguous;
    }

    public HttpStatusCode? StatusCode { get; }
    public bool IsAmbiguous { get; }
}

public interface ITwilioMessagingService
{
    Task<string?> ValidateAndCanonicalizeAsync(string input, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string canonicalDestination, string body, CancellationToken cancellationToken);
    Task<ProviderMessage> ScheduleAsync(string canonicalDestination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessagePage> ListAsync(DateTimeOffset fromExclusive, DateTimeOffset toExclusive, string? pageToken, CancellationToken cancellationToken);
}
