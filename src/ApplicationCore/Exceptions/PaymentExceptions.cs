using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message)
    {
    }
}

public class ForbiddenOperationException : Exception
{
    public ForbiddenOperationException(string message) : base(message)
    {
    }
}

public class InvalidPaymentRequestException : Exception
{
    public InvalidPaymentRequestException(string message) : base(message)
    {
    }
}

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}

public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message)
    {
    }
}

public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}

public class PayPalGatewayException : Exception
{
    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? PayPalName { get; }

    public PayPalGatewayException(string message, int statusCode, string? debugId = null, string? paypalName = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        PayPalName = paypalName;
    }
}
