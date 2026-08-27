using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessageProvider
{
    Task<string> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken);
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> GetAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> DisposeContentAsync(string providerSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);

public sealed class InvalidDestinationException : Exception
{
    public InvalidDestinationException(string message, Exception? innerException = null) : base(message, innerException) { }
}

public sealed class MessageProviderException : Exception
{
    public MessageProviderException(string message, System.Net.HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException) => StatusCode = statusCode;

    public System.Net.HttpStatusCode? StatusCode { get; }
}
