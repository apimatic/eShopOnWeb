using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderPaymentException : Exception
{
    public int StatusCode { get; }

    public OrderPaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public OrderPaymentException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// PayPal required a shopper to complete a browser challenge (for example 3-D Secure).
/// Direct card processing cannot continue without that round-trip.
/// </summary>
public class PayerActionRequiredException : OrderPaymentException
{
    public PayerActionRequiredException(string message)
        : base(422, message)
    {
    }
}
