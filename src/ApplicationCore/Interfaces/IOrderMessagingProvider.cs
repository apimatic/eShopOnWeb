using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderMessagingProvider
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessageState> SendAsync(string to, string body, CancellationToken cancellationToken);
    Task<ProviderMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessageState> GetAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> CancelAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessageState> RedactContentAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageRecord>> ListFromApplicationNumberAsync(CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalPhoneNumber);

public sealed record ProviderMessageState(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage);

public sealed record ProviderMessageRecord(
    string Sid,
    string Status,
    string? From,
    string? To,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    int? ErrorCode,
    string? ErrorMessage);

public class MessagingProviderException : Exception
{
    public MessagingProviderException(string operation)
        : base($"The messaging provider could not complete the {operation} operation.") { }
}
