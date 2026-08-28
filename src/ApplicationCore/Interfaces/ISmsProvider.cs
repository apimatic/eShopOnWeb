using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<SmsDestinationValidation> ValidateDestinationAsync(string number, CancellationToken cancellationToken);
    Task<SmsProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<SmsProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<SmsProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<SmsProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<SmsProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record SmsDestinationValidation(bool IsValid, string? CanonicalNumber);

public sealed record SmsProviderMessage(
    string Sid,
    string Status,
    string? From,
    string? To,
    string? Body,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated,
    int? ErrorCode);

public class SmsProviderException : Exception
{
    public SmsProviderException(string operation, int? providerErrorCode = null, Exception? innerException = null)
        : base($"The SMS provider could not complete {operation}.", innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
