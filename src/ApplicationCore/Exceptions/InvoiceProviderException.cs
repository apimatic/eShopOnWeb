using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider itself reports a problem carrying out a request. Carries the HTTP
/// status the provider returned so the API surface can translate a provider-side state refusal (4xx)
/// into a 409/404 for the caller, while a genuine provider fault (5xx or transport) surfaces as 502.
/// The message never contains any secret.
/// </summary>
public class InvoiceProviderException : Exception
{
    /// <summary>The HTTP status the provider returned, or 0 when the call never reached the provider.</summary>
    public int ProviderStatusCode { get; }

    public InvoiceProviderException(string message, int providerStatusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }
}
