using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

public class PaymentForbiddenException : Exception
{
    public PaymentForbiddenException(string message) : base(message) { }
}

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

public class PayPalGatewayException : Exception
{
    public int StatusCode { get; }
    public string? PayPalDebugId { get; }
    public string? PayPalName { get; }
    public string? PayPalIssue { get; }

    public PayPalGatewayException(
        string message,
        int statusCode = 502,
        string? payPalDebugId = null,
        string? payPalName = null,
        string? payPalIssue = null)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalDebugId = payPalDebugId;
        PayPalName = payPalName;
        PayPalIssue = payPalIssue;
    }
}

public class AuthorizationUnrenewableException : Exception
{
    public AuthorizationUnrenewableException(string message) : base(message) { }
}

public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message) { }
}
