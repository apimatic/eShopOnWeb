using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base for exceptions that carry the HTTP status code the API should return, letting the
/// PublicApi exception middleware translate domain failures into meaningful responses instead
/// of a blanket 500.
/// </summary>
public abstract class ApiException : Exception
{
    protected ApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    protected ApiException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

/// <summary>Raised when an order does not exist or is not owned by the caller.</summary>
public class OrderNotFoundException : ApiException
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.", 404) { }
}

/// <summary>Raised when a saved card does not exist or is not owned by the caller.</summary>
public class SavedPaymentMethodNotFoundException : ApiException
{
    public SavedPaymentMethodNotFoundException(int paymentMethodId)
        : base($"Saved payment method {paymentMethodId} was not found.", 404) { }
}

/// <summary>Raised when an operation is invalid for the current state (e.g. fulfilling an unpaid order).</summary>
public class PaymentStateException : ApiException
{
    public PaymentStateException(string message) : base(message, 409) { }
}

/// <summary>Raised for invalid caller input (e.g. a refund larger than the captured amount).</summary>
public class PaymentValidationException : ApiException
{
    public PaymentValidationException(string message) : base(message, 400) { }
}
