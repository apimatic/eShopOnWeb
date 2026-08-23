using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, 409)
    {
    }
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
        : base($"Payment method {paymentMethodId} was not found.", 404)
    {
    }
}
