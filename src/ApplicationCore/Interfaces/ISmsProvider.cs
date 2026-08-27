namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? From,
    string? To,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);
