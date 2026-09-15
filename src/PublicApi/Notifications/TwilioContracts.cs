using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateCreated,
    string? DateUpdated,
    string? DateSent,
    string? Direction,
    string? From,
    string? To,
    string? Body);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException() : base("The mobile number is not a usable destination.") { }
}

public interface ITwilioMessagingGateway
{
    string ConfiguredFromNumber { get; }
    Task<string> ValidateAndCanonicalizeAsync(string suppliedNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string canonicalNumber, string content, DateTimeOffset? scheduledFor, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactContentAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
