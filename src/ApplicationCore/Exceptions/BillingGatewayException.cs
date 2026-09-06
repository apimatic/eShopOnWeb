using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system could not be reached, timed out, or answered with something this
/// application cannot act on. Surfaced as a 502 — an upstream problem, not a caller problem.
/// </summary>
public class BillingGatewayException : BillingException
{
    public BillingGatewayException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingGatewayException(string message, Exception innerException, int? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status returned by the billing system, when the call got that far.</summary>
    public int? StatusCode { get; }
}
