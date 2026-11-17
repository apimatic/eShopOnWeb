using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An expected business failure that should be returned to the API caller with
/// an explicit HTTP status code and an actionable message.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentOperationException(int statusCode, string message, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
