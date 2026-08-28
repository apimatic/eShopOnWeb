using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessageProvider
{
    Task<string> ValidateAndCanonicalizeAsync(string number, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> SendAsync(string canonicalNumber, string body, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> ScheduleAsync(string canonicalNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> FetchAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class MessageProviderException : Exception
{
    public MessageProviderException(string message, int? providerStatusCode, Exception innerException)
        : base(message, innerException) => ProviderStatusCode = providerStatusCode;

    public int? ProviderStatusCode { get; }
}
