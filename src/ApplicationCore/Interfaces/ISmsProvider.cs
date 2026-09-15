using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneNumberLookupResult(bool IsValid, string? CanonicalPhoneNumber,
    IReadOnlyList<string> ValidationErrors);

public sealed record SmsProviderMessage(string Sid, string Status, string? Body, string? From,
    string? To, int? ErrorCode, DateTimeOffset? DateCreated, DateTimeOffset? DateSent);

public interface ISmsProvider
{
    Task<PhoneNumberLookupResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<SmsProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken);
    Task<SmsProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<SmsProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken);
    Task<SmsProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken);
    Task<SmsProviderMessage> DisposeContentAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<SmsProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException) => ProviderErrorCode = providerErrorCode;

    public int? ProviderErrorCode { get; }
}
