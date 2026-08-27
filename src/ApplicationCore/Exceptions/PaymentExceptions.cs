using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A business-rule violation while operating on an order's payment (wrong state,
/// refund exceeds captured amount, ...). Maps to HTTP 409.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message) {}
}

/// <summary>
/// PayPal declined the payment. Maps to HTTP 422.
/// </summary>
public class PaymentDeclinedException : PaymentOperationException
{
    public PaymentDeclinedException(string message) : base(message) {}
}

/// <summary>
/// The authorization holding the shopper's funds has expired and PayPal could not renew
/// it. No money is held; the shopper must pay again. Maps to HTTP 409.
/// </summary>
public class AuthorizationUnrecoverableException : PaymentOperationException
{
    public AuthorizationUnrecoverableException(string message) : base(message) {}
}

/// <summary>
/// PayPal answered a payment with a challenge that requires the shopper to approve in a
/// browser (e.g. 3DS payer action), which this integration does not support.
/// </summary>
public class PayerActionRequiredException : PaymentOperationException
{
    public PayerActionRequiredException(string message) : base(message) {}
}

/// <summary>
/// The call to PayPal itself failed. Carries PayPal's error name and debug id for
/// correlation. Maps to HTTP 502.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(int httpStatusCode, string? errorName, string message, string? debugId)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public int HttpStatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
}
