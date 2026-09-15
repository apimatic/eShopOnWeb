using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken);
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task RedactMessageContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record ValidatedPhoneNumber(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(string Sid, string Status, int? ErrorCode,
    DateTimeOffset? DateCreated, DateTimeOffset? DateSent);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(int statusCode, int? providerCode = null)
        : base("The messaging provider rejected or could not complete the request.")
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
