using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or fails an operation. Never rolls back or blocks
/// eShopOnWeb's own order lifecycle - callers surface this as a friendly error.
/// </summary>
public class BillingProviderException : Exception
{
    public string Operation { get; }
    public int? StatusCode { get; }

    public BillingProviderException(string operation, string message, int? statusCode = null, Exception? innerException = null)
        : base($"Billing provider error during {operation}: {message}", innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
    }
}
