using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessagingProvider
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

public sealed class MessagingProviderException : Exception
{
    public MessagingProviderException(string operation, int? providerErrorCode, string safeMessage)
        : base(safeMessage)
    {
        Operation = operation;
        ProviderErrorCode = providerErrorCode;
    }

    public string Operation { get; }
    public int? ProviderErrorCode { get; }
}
