using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400, string? paypalDebugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        PaypalDebugId = paypalDebugId;
    }

    public int StatusCode { get; }
    public string? PaypalDebugId { get; }
}

public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.", 404)
    {
    }
}

public class PaymentMethodNotFoundException : PaymentException
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"Saved payment method {paymentMethodId} was not found.", 404)
    {
    }
}

public class AuthorizationExpiredException : PaymentException
{
    public AuthorizationExpiredException(string message)
        : base(message, 409)
    {
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string paypalOrderId, string? payerActionUrl)
        : base(
            "PayPal required a shopper challenge (for example 3-D Secure) that needs a browser. This integration does not complete browser-based approval.",
            409)
    {
        PaypalOrderId = paypalOrderId;
        PayerActionUrl = payerActionUrl;
    }

    public string PaypalOrderId { get; }
    public string? PayerActionUrl { get; }
}
