using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when Finix returns an error response. The original Finix code,
/// message and log reference are preserved so they can be shown to operators.
/// </summary>
public class FinixApiException : PaymentOperationException
{
    public FinixApiException(
        int finixStatusCode,
        string message,
        string? finixCode = null,
        string? logReference = null,
        Exception? innerException = null)
        : base(502, message, finixCode ?? "FINIX_ERROR", innerException)
    {
        FinixStatusCode = finixStatusCode;
        FinixCode = finixCode;
        LogReference = logReference;
    }

    public int FinixStatusCode { get; }
    public string? FinixCode { get; }
    public string? LogReference { get; }
}
