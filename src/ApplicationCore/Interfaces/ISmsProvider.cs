using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessageState> SendAsync(string to, string content, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessageState> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> RedactMessageContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessageRecord(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed class SmsProviderException : Exception
{
    public SmsProviderException(string operation, int? providerErrorCode = null, Exception? innerException = null)
        : base($"The SMS provider could not complete the {operation} operation.", innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
