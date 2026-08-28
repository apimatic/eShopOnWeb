using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed record ProviderPhoneValidation(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? Body,
    string? From,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed record ProviderMessagePage(IReadOnlyList<ProviderMessage> Messages, string? NextPageToken);

public sealed class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public interface ITwilioMessagingGateway
{
    Task<ProviderPhoneValidation> ValidatePhoneNumberAsync(string submittedNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendImmediateAsync(string canonicalDestination, string body, CancellationToken cancellationToken);
    Task<ProviderMessage> ScheduleAsync(string canonicalDestination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessagePage> ListAsync(DateTimeOffset widenedLower, DateTimeOffset widenedUpper, string? pageToken, CancellationToken cancellationToken);
}
