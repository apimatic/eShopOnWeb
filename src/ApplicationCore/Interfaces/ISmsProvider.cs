using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> SendMessageAsync(
        string e164Destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> GetMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot> RedactMessageAsync(string messageSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidationResult(
    bool IsValid,
    string? E164Number,
    IReadOnlyList<string> ValidationErrors);

public sealed record SmsMessageSnapshot(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateUpdated,
    DateTimeOffset? DateSent);

public sealed class SmsProviderException : Exception
{
    public SmsProviderException(string operation, int? providerErrorCode = null, Exception? innerException = null)
        : base($"The messaging provider could not complete {operation}.", innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
