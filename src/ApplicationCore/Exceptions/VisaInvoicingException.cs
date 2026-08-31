using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the invoicing provider (Visa / CyberSource) refuses or fails a request. Carries an
/// optional provider-supplied detail and the provider HTTP status so the caller can be told what
/// happened, and so a legitimate state-based refusal can be distinguished from a provider outage.
/// Never carries credentials.
/// </summary>
public class VisaInvoicingException : Exception
{
    public VisaInvoicingException(string message, int? providerStatusCode = null, string? providerReason = null)
        : base(message)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderReason = providerReason;
    }

    public VisaInvoicingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The HTTP status the provider returned, when the failure came from a provider response.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>A human-readable reason as reported by the provider, if one was available.</summary>
    public string? ProviderReason { get; }

    /// <summary>
    /// True when the provider rejected the request on business/state grounds (a 4xx) rather than
    /// failing to serve it (a 5xx / transport error). A state-based refusal is an expected outcome.
    /// </summary>
    public bool IsProviderRefusal => ProviderStatusCode is >= 400 and < 500;
}
