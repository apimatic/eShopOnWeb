using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITextMessageProvider
{
    Task<ValidatedDestination> ValidateDestinationAsync(string input, CancellationToken cancellationToken = default);
    Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);
    Task<ProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record ValidatedDestination(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    string? To);

public sealed class TextMessageProviderException : Exception
{
    public TextMessageProviderException(string operation, int? providerErrorCode = null, int? httpStatusCode = null)
        : base($"The messaging provider could not complete the {operation} operation.")
    {
        ProviderErrorCode = providerErrorCode;
        HttpStatusCode = httpStatusCode;
    }

    public int? ProviderErrorCode { get; }
    public int? HttpStatusCode { get; }
}
