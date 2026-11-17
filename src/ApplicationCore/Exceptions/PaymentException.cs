using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base class for payment-flow failures that should be surfaced as HTTP responses.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message, 404)
    {
    }
}

public sealed class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message, 400)
    {
    }
}

public sealed class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message, 409)
    {
    }
}

public sealed class PaymentGatewayException : PaymentException
{
    public PaymentGatewayException(string message) : base(message, 502)
    {
    }
}

public sealed class PaymentConfigurationException : PaymentException
{
    public PaymentConfigurationException(string message) : base(message, 500)
    {
    }
}
