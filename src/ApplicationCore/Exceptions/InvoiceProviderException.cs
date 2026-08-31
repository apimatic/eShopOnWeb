using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure returned by (or while reaching) the Visa/CyberSource provider. Carries the provider's
/// HTTP status where one was available, so the API boundary can map it deliberately — a caller-caused
/// 4xx back to the caller, our-credential/quota and transport failures to a 5xx. Never carries a secret
/// or the raw provider body.
/// </summary>
public class InvoiceProviderException : Exception
{
    /// <summary>The provider's HTTP status, when one was returned. Null for transport/timeout failures.</summary>
    public int? StatusCode { get; }

    public InvoiceProviderException(string message, int? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
