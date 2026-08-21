using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message, string? debugId = null, string? issue = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
}

public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(int orderId)
        : base(404, $"Order {orderId} was not found.")
    {
    }
}

public class PaymentMethodNotFoundException : PaymentException
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base(404, $"Payment method {paymentMethodId} was not found.")
    {
    }
}

public class OrderAccessDeniedException : PaymentException
{
    public OrderAccessDeniedException()
        : base(403, "You cannot act on this order.")
    {
    }
}

public class PaymentMethodAccessDeniedException : PaymentException
{
    public PaymentMethodAccessDeniedException()
        : base(403, "You cannot act on this payment method.")
    {
    }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string payPalOrderId)
        : base(409, "PayPal required a browser authentication step, which this integration does not support.", issue: "PAYER_ACTION_REQUIRED")
    {
        PayPalOrderId = payPalOrderId;
    }

    public string PayPalOrderId { get; }
}

public class AuthorizationNotRenewableException : PaymentException
{
    public AuthorizationNotRenewableException(string? issue = null)
        : base(409,
            "The payment authorization has expired and PayPal can no longer renew it. Ask the shopper to pay again, then fulfil the new authorization.",
            issue: issue)
    {
    }
}
