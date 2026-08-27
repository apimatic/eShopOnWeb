using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessagingProvider
{
    Task<DestinationValidation> ValidateDestinationAsync(string input, CancellationToken cancellationToken);
    Task<ProviderMessageState> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageState>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record DestinationValidation(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessageState(
    string? Sid,
    string? Status,
    string? From,
    string? MessagingServiceSid,
    string? DateCreated,
    string? DateSent,
    string? DateUpdated,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body);

public sealed class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException) => StatusCode = statusCode;

    public int? StatusCode { get; }
}
