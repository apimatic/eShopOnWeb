using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}

public class ForbiddenOperationException : Exception
{
    public ForbiddenOperationException(string message) : base(message)
    {
    }
}

public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}

public class PaymentRequestException : Exception
{
    public PaymentRequestException(string message) : base(message)
    {
    }
}

public class PaymentGatewayException : Exception
{
    public string? PayPalDebugId { get; }
    public string? PayPalErrorName { get; }

    public PaymentGatewayException(string message, string? payPalErrorName = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        PayPalErrorName = payPalErrorName;
        PayPalDebugId = debugId;
    }
}

/// <summary>
/// PayPal required a shopper to complete a browser challenge (3-D Secure or similar).
/// This integration does not implement an approval round-trip.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}

public class AuthorizationRenewalException : Exception
{
    public AuthorizationRenewalException(string message) : base(message)
    {
    }
}
