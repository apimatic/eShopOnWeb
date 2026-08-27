using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<PhoneValidationResult> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> SendAsync(string destination, string content, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> CancelAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> FetchAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> DisposeContentAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageSnapshot>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public sealed class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
