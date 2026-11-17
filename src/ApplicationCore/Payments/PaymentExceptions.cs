using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }
}

public sealed class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}

public sealed class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message)
    {
    }
}

public sealed class PaymentForbiddenException : PaymentException
{
    public PaymentForbiddenException(string message) : base(message)
    {
    }
}

public sealed class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}

public sealed class PaymentGatewayException : PaymentException
{
    public PaymentGatewayException(string message) : base(message)
    {
    }
}
