using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingGateway
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string rawNumber, string? countryCode,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record PhoneNumberValidation(bool Valid, string? E164Number, IReadOnlyList<string> Errors);

public sealed record ProviderMessage(string Sid, string Status, string? From, string? To, string? Body,
    int? ErrorCode, DateTimeOffset? DateCreated, DateTimeOffset? DateSent);
