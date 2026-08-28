using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessageProvider
{
    Task<ProviderMessage> SendAsync(string destination, string body,
        DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string providerMessageSid,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string providerMessageSid,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactContentAsync(string providerMessageSid,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidation> ValidateAsync(string number,
        CancellationToken cancellationToken = default);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

public sealed record ProviderMessage(string Sid, string Status, int? ErrorCode,
    DateTimeOffset? DateCreated, DateTimeOffset? DateSent);

public sealed class ProviderRequestException : Exception
{
    public ProviderRequestException(string operation, int? providerCode = null, Exception? innerException = null)
        : base($"The messaging provider could not complete {operation}.", innerException)
    {
        ProviderCode = providerCode;
    }

    public int? ProviderCode { get; }
}
