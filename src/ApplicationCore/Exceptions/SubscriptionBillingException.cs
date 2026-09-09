using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the billing boundary surfaces. The Infrastructure implementation
/// translates every provider/SDK/transport failure into this type, carrying a caller-safe
/// <see cref="Exception.Message"/> and, where known, the provider HTTP <see cref="StatusCode"/>
/// so the API layer can map it to an appropriate response status.
/// </summary>
public class SubscriptionBillingException : Exception
{
    /// <summary>The provider HTTP status, when a response was received; <c>null</c> for transport failures.</summary>
    public int? StatusCode { get; }

    public SubscriptionBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
