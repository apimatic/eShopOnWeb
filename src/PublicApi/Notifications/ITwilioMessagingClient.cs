using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface ITwilioMessagingClient
{
    Task<PhoneValidationResult> ValidatePhoneNumberAsync(string number, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken);
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(CancellationToken cancellationToken);
}

public sealed record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? From,
    string? To,
    string? Body,
    DateTimeOffset DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset DateUpdated,
    int? ErrorCode);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(int statusCode, int? providerCode)
        : base("The messaging provider rejected the request.")
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
