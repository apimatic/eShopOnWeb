using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the messaging provider. <see cref="Exception.Message"/> is safe
/// to log (it never contains recipient numbers or credentials); <see cref="ProviderDetail"/>
/// carries the provider's own error text for storage/reporting and may contain PII.
/// </summary>
public class MessageProviderException : Exception
{
    public MessageProviderException(int httpStatusCode, int? providerErrorCode, string? providerDetail)
        : base($"Messaging provider request failed with HTTP {httpStatusCode}" +
               (providerErrorCode.HasValue ? $" (provider error {providerErrorCode.Value})" : string.Empty))
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
        ProviderDetail = providerDetail;
    }

    public MessageProviderException(string safeMessage, Exception inner)
        : base(safeMessage, inner)
    {
    }

    public int HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }
    public string? ProviderDetail { get; }
}
