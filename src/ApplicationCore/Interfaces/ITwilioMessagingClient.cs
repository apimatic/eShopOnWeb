using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ValidatedPhoneNumber(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    string? Body = null);

public interface ITwilioMessagingClient
{
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken);

    Task<ProviderMessage> SendAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken);

    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactAsync(string messageSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string operation, int statusCode, int? providerErrorCode = null)
        : base($"The messaging provider rejected {operation} (HTTP {statusCode}).")
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int StatusCode { get; }
    public int? ProviderErrorCode { get; }
}
