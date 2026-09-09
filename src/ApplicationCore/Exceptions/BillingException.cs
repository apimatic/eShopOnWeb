using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the external billing system (Maxio Advanced Billing) rejects a request
/// or is otherwise unable to complete an operation.
/// </summary>
public class BillingException : Exception
{
    public int? StatusCode { get; }

    public BillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
