using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An error that should be surfaced to the API caller with a specific HTTP status.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
