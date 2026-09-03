using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, HttpStatusCode statusCode, Exception inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ProviderDebugId { get; init; }
    public string? ProviderErrorName { get; init; }
}

public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.", HttpStatusCode.NotFound)
    {
    }
}

public class PaymentMethodNotFoundException : PaymentException
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"Payment method {paymentMethodId} was not found.", HttpStatusCode.NotFound)
    {
    }
}

public class ForbiddenResourceException : PaymentException
{
    public ForbiddenResourceException(string message)
        : base(message, HttpStatusCode.Forbidden)
    {
    }
}

public class InvalidOrderStateException : PaymentException
{
    public InvalidOrderStateException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string payPalOrderId)
        : base(
            $"PayPal requires a shopper browser challenge (3-D Secure / payer-action) for order {payPalOrderId}. This integration does not collect in-browser approval.",
            HttpStatusCode.Conflict)
    {
    }
}
