using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CheckoutException : Exception
{
    public CheckoutException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public class PayerActionRequiredException : CheckoutException
{
    public PayerActionRequiredException(string paypalOrderId)
        : base(409, "PayPal required a shopper approval challenge that cannot be completed without a browser. No approval round-trip is implemented.")
    {
        PayPalOrderId = paypalOrderId;
    }

    public string PayPalOrderId { get; }
}
