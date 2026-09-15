using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderMessage(string Sid, string Status, string? Body, string? From, string? To,
    int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateCreated, DateTimeOffset? DateSent);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message, int? providerCode = null, int? httpStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderCode = providerCode;
        HttpStatusCode = httpStatusCode;
    }

    public int? ProviderCode { get; }
    public int? HttpStatusCode { get; }
}
