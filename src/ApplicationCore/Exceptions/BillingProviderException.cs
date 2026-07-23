using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects a request, is unreachable, or answers with something
/// the integration cannot interpret. This is the only provider failure type that escapes the
/// billing client, so callers never have to know how the provider is reached.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public BillingProviderException(string message, int? statusCode, string? providerMessage) : base(message)
    {
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
    }

    /// <summary>The HTTP status the provider answered with, when the call reached it.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own error text, suitable for surfacing to the actor (UC1/UC3 failure scenarios).</summary>
    public string? ProviderMessage { get; }
}
