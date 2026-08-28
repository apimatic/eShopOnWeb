using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioGateway
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string destination, string content, CancellationToken cancellationToken);
    Task<ProviderMessage> ScheduleMessageAsync(string destination, string content, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public class TwilioProviderException : Exception
{
    public TwilioProviderException(string operation, int? providerErrorCode = null)
        : base(providerErrorCode.HasValue
            ? $"Twilio rejected the {operation} operation with error code {providerErrorCode}."
            : $"Twilio rejected the {operation} operation.")
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
