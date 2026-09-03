using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure returned by (or reaching) the payment provider, translated at the integration
/// boundary into a single domain type. Carries a caller-safe message plus, where available,
/// the provider HTTP status and debug id so an operator can act on it. Never carries raw
/// provider exception text.
/// </summary>
public class PaymentGatewayException : Exception
{
    /// <summary>The provider HTTP status, when known. A 4xx is caller-actionable; 5xx / null is not.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's debug id for the failure, when available — useful for operator support.</summary>
    public string? DebugId { get; }

    public bool IsClientError => StatusCode is >= 400 and < 500;

    public PaymentGatewayException(string message, int? statusCode = null, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }
}
