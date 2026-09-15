using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioGateway
{
    Task<PhoneNumberLookup> LookupPhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string to, string content, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberLookup(bool Valid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string operation, int? providerCode = null)
        : base($"Twilio {operation} failed.") => ProviderCode = providerCode;

    public int? ProviderCode { get; }
}
