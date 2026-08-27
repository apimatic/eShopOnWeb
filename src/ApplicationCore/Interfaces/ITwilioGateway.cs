using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioGateway
{
    Task<PhoneNumberValidation> ValidateMobileNumberAsync(string input, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string destination, string body,
        DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageContentAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsUsableMobile, string? CanonicalNumber, string? Reason);

public sealed record ProviderMessage(string Sid, string? From, string? To, string Status,
    string? Body, DateTimeOffset? DateCreated, DateTimeOffset? DateSent,
    int? ErrorCode, string? ErrorMessage);

public class TwilioProviderException : Exception
{
    public TwilioProviderException(string operation, int httpStatus, int? providerCode, string providerMessage)
        : base($"Twilio {operation} failed with HTTP {httpStatus}.")
    {
        Operation = operation;
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
        ProviderMessage = providerMessage;
    }

    public string Operation { get; }
    public int HttpStatus { get; }
    public int? ProviderCode { get; }
    public string ProviderMessage { get; }
}
