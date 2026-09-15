using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string paypalOrderId)
        : base(
            $"PayPal required a shopper approval challenge (order {paypalOrderId}). Direct card processing cannot continue without a browser round-trip.",
            409)
    {
        PayPalOrderId = paypalOrderId;
    }

    public string PayPalOrderId { get; }
}
