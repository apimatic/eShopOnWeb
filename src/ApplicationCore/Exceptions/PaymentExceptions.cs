using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentDomainException : Exception
{
    public PaymentDomainException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public class OrderNotFoundException : PaymentDomainException
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.", HttpStatusCode.NotFound)
    {
    }
}

public class SavedPaymentMethodNotFoundException : PaymentDomainException
{
    public SavedPaymentMethodNotFoundException(int paymentMethodId)
        : base($"Payment method {paymentMethodId} was not found.", HttpStatusCode.NotFound)
    {
    }
}

public class InvalidOrderStateException : PaymentDomainException
{
    public InvalidOrderStateException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class AuthorizationCannotBeRenewedException : PaymentDomainException
{
    public AuthorizationCannotBeRenewedException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class PayerActionRequiredException : PaymentDomainException
{
    public PayerActionRequiredException()
        : base(
            "PayPal required a shopper challenge (for example 3-D Secure) that needs browser approval. This API does not collect that approval.",
            HttpStatusCode.Conflict)
    {
    }
}

public class PayPalApiException : PaymentDomainException
{
    public PayPalApiException(string message, string? debugId = null, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(string.IsNullOrEmpty(debugId) ? message : $"{message} (PayPal debug_id: {debugId})", statusCode)
    {
        DebugId = debugId;
    }

    public string? DebugId { get; }
}
