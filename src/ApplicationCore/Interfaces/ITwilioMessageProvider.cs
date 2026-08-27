using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessageProvider
{
    Task<PhoneValidationResult> ValidateDestinationAsync(string number, CancellationToken cancellationToken);
    Task<ProviderMessageState> SendAsync(string canonicalNumber, string body, CancellationToken cancellationToken);
    Task<ProviderMessageState> ScheduleAsync(string canonicalNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessageState> CancelAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageRecord>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class MessagingProviderException : Exception
{
    public MessagingProviderException(string safeMessage, int? statusCode, Exception innerException)
        : base(safeMessage, innerException) => StatusCode = statusCode;

    public MessagingProviderException(string safeMessage, Exception innerException)
        : this(safeMessage, null, innerException) { }

    public int? StatusCode { get; }
}
